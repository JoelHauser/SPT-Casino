using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using Casino.Shared;
using Comfort.Common;
using EFT;
using EFT.InventoryLogic;
using EFT.UI.DragAndDrop;
using UnityEngine;

namespace SlotMachine.Client
{
    /// <summary>
    /// The real item icons, taken from the game rather than drawn for it.
    ///
    /// ## Where they come from
    ///
    /// Tarkov does not ship item icons as pictures. It **renders them**: an item's 3D
    /// model, posed by a camera, into a texture. That is what `ItemIconCreator` is, and
    /// `ItemViewFactory.GetItemSpriteAsync` is the front door to it -- the same call the
    /// stash and the flea market make for every icon you have ever seen in the menu.
    ///
    /// So the path is: a template id, an `Item` from the game's own item factory, and
    /// that item handed to the renderer.
    ///
    /// ```
    /// factory.CreateItem(MongoID.Generate(true), template, null)   -- see TryResolveFactory
    /// ItemViewFactory.GetItemSpriteAsync(item, ScaleFactor)        -> Task&lt;Sprite&gt;
    /// ```
    ///
    /// `ItemViewFactory` is still a normal, nameable type -- only the factory it asks for
    /// an `Item` had to stop being one. See <see cref="TryResolveFactory"/> for why.
    ///
    /// **Nothing here ships BSG's art.** The icons are made on the player's own machine
    /// out of their own installation, which is both the honest arrangement and the
    /// reason the mod does not carry a folder of somebody else's pictures.
    ///
    /// ## It is allowed to fail, but not to hang
    ///
    /// Rendering needs a live `ItemIconCreator`, which needs a session. Open the panel
    /// early enough, or on a build where a name has moved, and it cannot draw anything.
    /// Every step is inside a try and every failure is a log line.
    ///
    /// Failing is not the same as never answering, though, and the panel will not let
    /// anybody spin until the symbols are in. So a symbol the game refuses outright is
    /// recorded as **given up on** rather than left pending, and after
    /// `MaxAttempts` fruitless passes the whole set is given up on. The machine is then
    /// playable with blank tiles and a warning in the log, which is poor -- but a good
    /// deal better than a panel that never becomes usable.
    ///
    /// ## Cached to disk, once
    ///
    /// A rendered icon is saved beside the plugin as a PNG, named for its **template
    /// id**, so the second launch reads a file instead of posing a camera at a rooster.
    /// The id rather than the symbol name, because the name is what this build calls the
    /// symbol and the id is what the picture is actually of -- see <see cref="FromDisk"/>.
    /// </summary>
    internal static class ItemArt
    {
        /// <summary>
        /// Which real item each reel symbol is.
        ///
        /// Chosen to be **worth looking at**, and to climb: a can of cola, a first aid
        /// kit, a bottle of moonshine, a games console, a gold watch, a golden rooster,
        /// a graphics card, a bitcoin, a red keycard. The first set was medkits, ammo
        /// boxes and dog tags, which is what a Tarkov player already scrolls past.
        ///
        /// Every id is read out of `SPT_Data/database/templates/items.json` rather than
        /// typed from memory, which matters more than it sounds: the id that comes to
        /// mind for "BEAR dogtag" is the USEC one, and the Labs keycard has several
        /// plausible ids of which exactly one is the red.
        /// </summary>
        private static readonly Dictionary<string, string> Templates =
            new Dictionary<string, string>
            {
                ["Cola"] = "57514643245977207f2c2d09",      // Can of TarCola soda
                ["Salewa"] = "544fb45d4bdc2dee738b4568",    // Salewa first aid kit
                ["Moonshine"] = "5d1b376e86f774252519444e", // Fierce Hatchling moonshine
                ["Tetriz"] = "5c12620d86f7743f8b198b72",    // Tetriz portable game console
                ["Watch"] = "59faf7ca86f7740dbe19f6c2",     // Roler Submariner gold watch
                ["Rooster"] = "5bc9bc53d4351e00367fbcee",   // Golden rooster figurine
                ["Gpu"] = "57347ca924597744596b4e71",       // Graphics card
                ["Bitcoin"] = "59faff1d86f7746c51718c9c",   // Physical Bitcoin
                ["Keycard"] = "5c1d0efb86f7744baf2e7b7b",   // TerraGroup Labs keycard (Red)
            };

        /// <summary>
        /// Multiplies the icon's natural pixel size, which is its grid footprint times
        /// the inventory cell size. Three puts a one-cell item at about 190px, which is
        /// comfortably more than the reel draws it at on a 1440p screen.
        /// </summary>
        private const int ScaleFactor = 3;

        private static readonly Dictionary<string, Sprite> Ready =
            new Dictionary<string, Sprite>();

        private static readonly HashSet<string> Asked = new HashSet<string>();

        /// <summary>
        /// Symbols the game will not draw. Counted as settled, not as pending: the panel
        /// waits on <see cref="HasAll"/>, and a symbol that is never coming would make
        /// it wait for ever.
        /// </summary>
        private static readonly HashSet<string> Abandoned = new HashSet<string>();

        /// <summary>
        /// How many passes may end with the game unable to draw anything before the
        /// machine gives up and lets itself be played with blanks.
        /// </summary>
        private const int MaxAttempts = 3;

        private static int _attempts;

        private static bool _running;

        /// <summary>The game's icon for a symbol, or null if there is not one yet.</summary>
        internal static Sprite For(string symbol) =>
            symbol != null && Ready.TryGetValue(symbol, out var sprite) ? sprite : null;

        /// <summary>Where a rendered icon is kept between launches.</summary>
        private static string CacheFolder => Path.Combine(Host.AssetFolder, "symbols/ingame");

        /// <summary>
        /// Loads whatever is already on disk, **before the panel is built**.
        ///
        /// This is the whole reason the reels do not visibly change their minds. The
        /// coroutine below cannot help with that: it runs a frame after `Build`, so even
        /// a cache hit meant one frame of something else followed by a swap. Reading the
        /// files synchronously here means that on every launch but the very first, the
        /// first frame the reels ever draw is already the real icons.
        ///
        /// Cheap: nine small PNGs off a local disk, once per session.
        /// </summary>
        internal static void PrimeFromDisk(IReadOnlyList<string> symbols)
        {
            if (symbols == null)
            {
                return;
            }

            foreach (var symbol in symbols)
            {
                if (symbol == null || Ready.ContainsKey(symbol)
                    || !Templates.TryGetValue(symbol, out var template))
                {
                    continue;
                }

                var cached = FromDisk(template);

                if (cached != null)
                {
                    Ready[symbol] = cached;
                }
            }
        }

        /// <summary>
        /// Whether every symbol has its real icon in hand.
        ///
        /// The panel holds the reels blank until this is true. There is nothing else to
        /// show them: the drawn stand-ins were removed precisely so that nobody would
        /// watch the machine change its symbols a second after opening it.
        /// </summary>
        internal static bool HasAll(IReadOnlyList<string> symbols)
        {
            if (symbols == null || symbols.Count == 0)
            {
                return false;
            }

            foreach (var symbol in symbols)
            {
                if (symbol != null && !Ready.ContainsKey(symbol) && !Abandoned.Contains(symbol))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Fetches every icon that is not in hand yet, and calls back as each arrives.
        ///
        /// One at a time and off the main thread's critical path: nine model renders in
        /// a single frame is a visible hitch on the frame the panel opens, and there is
        /// nothing to hurry for -- the reels have art to show meanwhile.
        /// </summary>
        internal static void Fetch(MonoBehaviour host, IReadOnlyList<string> symbols, Action onArrived)
        {
            if (host == null || symbols == null || _running)
            {
                return;
            }

            _running = true;
            host.StartCoroutine(FetchAll(symbols, onArrived));
        }

        private static IEnumerator FetchAll(IReadOnlyList<string> symbols, Action onArrived)
        {
            var loaded = 0;
            var rendered = 0;
            var unable = false;

            foreach (var symbol in symbols)
            {
                if (symbol == null || Ready.ContainsKey(symbol) || Asked.Contains(symbol))
                {
                    continue;
                }

                // Yield first, so the panel gets a frame up before nine model renders
                // begin. A machine that appears and then hitches looks worse than one
                // that appears and fills in.
                yield return null;

                if (!Templates.TryGetValue(symbol, out var template))
                {
                    // A symbol the server knows about and this build does not. Nothing
                    // to render it from, so it is settled as a blank rather than left
                    // holding the machine up.
                    Abandoned.Add(symbol);
                    continue;
                }

                Asked.Add(symbol);

                var cached = FromDisk(template);

                if (cached != null)
                {
                    Ready[symbol] = cached;
                    loaded++;
                    onArrived?.Invoke();
                    continue;
                }

                Task<Sprite> task;

                if (!TryRender(symbol, template, out task))
                {
                    // No session, no renderer -- usually "not yet". Ask again next time
                    // the panel opens, but not for ever.
                    //
                    // continue, not break: this used to stop the whole pass on the first
                    // symbol that could not be drawn, so one bad symbol -- or the game
                    // simply not being ready yet on the pass that happened to test the
                    // first reel -- meant the other eight were never even attempted, and
                    // three such passes abandoned all nine for the rest of the session.
                    // Every symbol earns its own MaxAttempts now.
                    Asked.Remove(symbol);
                    unable = true;
                    continue;
                }

                while (!task.IsCompleted)
                {
                    yield return null;
                }

                if (task.IsFaulted || task.Result == null)
                {
                    SlotClientPlugin.Log.LogWarning(
                        $"[Slots] the game would not draw {symbol} ({template}); it will show blank.");

                    Abandoned.Add(symbol);
                    continue;
                }

                Ready[symbol] = task.Result;
                rendered++;
                Save(template, task.Result);
                onArrived?.Invoke();
            }

            if (loaded + rendered > 0)
            {
                SlotClientPlugin.Log.LogInfo(
                    $"[Slots] item icons: {rendered} drawn by the game, {loaded} from the cache.");
            }

            if (unable && ++_attempts >= MaxAttempts)
            {
                // Three opens and the game has still never been in a state to draw
                // anything. Rather than a machine that can never be played, take the
                // blanks and say so where somebody will find it.
                foreach (var symbol in symbols)
                {
                    if (symbol != null && !Ready.ContainsKey(symbol))
                    {
                        Abandoned.Add(symbol);
                    }
                }

                SlotClientPlugin.Log.LogWarning(
                    $"[Slots] the game has not been able to draw item icons in {MaxAttempts} tries. "
                    + "The reels will show blanks. Reopening after a profile is loaded usually fixes it.");
            }

            _running = false;
            onArrived?.Invoke();
        }

        private static MethodInfo _createItem;

        private static PropertyInfo _factoryInstantiated;

        private static PropertyInfo _factoryInstance;

        private static bool _factoryResolveFailed;

        /// <summary>
        /// Resolves <see cref="_createItem"/> and its matching
        /// <c>Singleton&lt;&gt;.Instantiated</c> / <c>.Instance</c> accessors, once.
        ///
        /// A lookup that fails on this build will fail again on the next attempt for the
        /// same reason, so a failure is cached rather than retried -- one warning for the
        /// session instead of one per symbol per panel open.
        /// </summary>
        private static bool TryResolveFactory()
        {
            if (_createItem != null)
            {
                return true;
            }

            if (_factoryResolveFailed)
            {
                return false;
            }

            try
            {
                _createItem = FindCreateItem();

                if (_createItem == null)
                {
                    throw new MissingMethodException(
                        "nothing in Assembly-CSharp is shaped like CreateItem(string, string, diff) -> Item.");
                }

                var singletonOfFactory = typeof(Singleton<>).MakeGenericType(_createItem.DeclaringType);

                _factoryInstantiated = singletonOfFactory.GetProperty(
                    "Instantiated", BindingFlags.Public | BindingFlags.Static);
                _factoryInstance = singletonOfFactory.GetProperty(
                    "Instance", BindingFlags.Public | BindingFlags.Static);

                if (_factoryInstantiated == null || _factoryInstance == null)
                {
                    throw new MissingMemberException(
                        "Singleton<> no longer exposes Instantiated/Instance by those names.");
                }

                return true;
            }
            catch (Exception ex)
            {
                _factoryResolveFailed = true;
                _createItem = null;

                SlotClientPlugin.Log.LogWarning(
                    $"[Slots] item icon rendering is off: {ex.Message} "
                    + "The reels will show blanks.");

                return false;
            }
        }

        /// <summary>
        /// Finds the item factory's
        /// <c>CreateItem(string itemId, string templateId, ItemComponentsProperties diff)</c>
        /// by its shape, because it cannot be found by the name of the class it is on.
        ///
        /// The class was `ItemFactory` until EFT 0.16.9.5. Its `Name` in the assembly's
        /// own metadata is now the empty string -- not garbled, absent -- which is not
        /// something `nameof` or `AccessTools.TypeByName` can be handed.
        ///
        /// **This was a pinned metadata token (`0x06009726`) between 8 Sep and 9 Sep 2026,
        /// and that is what shipped broken in 1.2.0 and 1.2.1.** A token addresses
        /// metadata directly, so it does not care what anything is named -- but it is
        /// only correct for the exact assembly it was read out of. On a player's install
        /// it came back as something that was not even a `MethodInfo`, and the cast threw
        /// `Specified cast is not valid` before anything could be drawn. A number read
        /// off one machine's copy of the file was never going to survive the fleet.
        ///
        /// A search does. The *method's* name has stayed readable through every rename
        /// this repo has hit -- it is the declaring type that keeps losing its own -- so
        /// the search is over every type in the assembly for a method that is named
        /// `CreateItem`, takes an id, a template id and a nullable diff, and returns an
        /// `Item`. That is the call site this file has always made, described instead of
        /// numbered, and it re-derives itself on whatever build it is actually running
        /// on rather than on the one somebody last decompiled.
        ///
        /// Costs one pass over the assembly's types, once per session, behind the same
        /// cache the token sat behind.
        /// </summary>
        private static MethodInfo FindCreateItem()
        {
            foreach (var type in TypesIn(typeof(MongoID).Module))
            {
                // Cheap tests before the expensive one. `GetMethods` resolves every
                // signature on the type and allocates an array to hand back, and this
                // runs on the main thread with the panel already open -- across an
                // assembly this size that is worth not doing fifteen thousand times.
                // What is being looked for reaches `Singleton<>`, so it is an ordinary
                // instantiable class.
                if (!type.IsClass || type.IsAbstract || type.ContainsGenericParameters)
                {
                    continue;
                }

                MethodInfo[] methods;

                try
                {
                    methods = type.GetMethods(
                        BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
                }
                catch (Exception)
                {
                    // A type whose own members will not load. Not the one being looked for.
                    continue;
                }

                foreach (var method in methods)
                {
                    if (method.Name != "CreateItem" || method.ReturnType != typeof(Item))
                    {
                        continue;
                    }

                    var parameters = method.GetParameters();

                    if (parameters.Length != 3
                        || parameters[0].ParameterType != typeof(string)
                        || parameters[1].ParameterType != typeof(string)
                        || parameters[2].ParameterType.IsValueType)
                    {
                        continue;
                    }

                    return method;
                }
            }

            return null;
        }

        /// <summary>
        /// Every type in a module that will actually load.
        ///
        /// `Module.GetTypes()` throws on the first type it cannot resolve and hands back
        /// the rest on the exception. In a heavily modded install that is a real
        /// possibility and none of it is this mod's business -- the wanted type is
        /// almost certainly among the ones that did load.
        /// </summary>
        private static Type[] TypesIn(Module module)
        {
            try
            {
                return module.GetTypes();
            }
            catch (ReflectionTypeLoadException ex)
            {
                var loaded = new List<Type>();

                foreach (var type in ex.Types)
                {
                    if (type != null)
                    {
                        loaded.Add(type);
                    }
                }

                return loaded.ToArray();
            }
        }

        /// <summary>
        /// Asks the game to draw one item.
        ///
        /// Returns false rather than throwing when the game is not in a state to do it,
        /// because "not yet" and "never" want different answers from the caller.
        /// </summary>
        private static bool TryRender(string symbol, string template, out Task<Sprite> task)
        {
            task = null;

            try
            {
                if (!TryResolveFactory() || !(bool)_factoryInstantiated.GetValue(null))
                {
                    return false;
                }

                var factory = _factoryInstance.GetValue(null);
                string itemId = MongoID.Generate(true);
                var item = (Item)_createItem.Invoke(factory, new object[] { itemId, template, null });

                if (item == null)
                {
                    SlotClientPlugin.Log.LogWarning($"[Slots] no item template {template} for {symbol}.");
                    return false;
                }

                task = ItemViewFactory.GetItemSpriteAsync(item, ScaleFactor);
                return task != null;
            }
            catch (Exception ex)
            {
                var reason = (ex as TargetInvocationException)?.InnerException?.Message ?? ex.Message;
                SlotClientPlugin.Log.LogWarning($"[Slots] could not ask the game for {symbol}: {reason}");
                return false;
            }
        }

        /// <summary>
        /// A cached icon, **keyed by template id rather than by symbol name**.
        ///
        /// The name is what the symbol is called in this build; the id is what the
        /// picture is of. Keying on the name is wrong the moment a symbol keeps its name
        /// and changes its item, which is exactly what happened when `Keycard` moved
        /// from the violet Labs card to the red one: the cache would have gone on
        /// serving a violet card under a symbol that had become red.
        /// </summary>
        private static Sprite FromDisk(string template)
        {
            try
            {
                return Textures.FromFile(Path.Combine(CacheFolder, template + ".png"));
            }
            catch (Exception ex)
            {
                SlotClientPlugin.Log.LogWarning($"[Slots] could not read the cached {template}: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Keeps a rendered icon for next time.
        ///
        /// **The icons are regions of an atlas, not textures of their own.** The first
        /// version of this refused to cache anything whose `textureRect` was not the
        /// whole texture, on the reasoning that cropping it was risky -- and every one
        /// of the nine failed that test, so the cache never held a single file and every
        /// launch re-rendered all nine. A guard that never passes is not a safe guard,
        /// it is a disabled feature.
        ///
        /// So it crops, two ways round:
        ///
        /// 1. `GetPixels` over the sprite's rect, if the texture will allow it. No
        ///    orientation to get wrong -- `GetPixels` and `EncodeToPNG` agree about
        ///    which way up a texture is.
        /// 2. Otherwise a `Blit` that applies the crop as a UV scale and offset, into a
        ///    render texture the size of the sprite, then a full-surface `ReadPixels`.
        ///    Full-surface is the point: reading a sub-rectangle is where the two
        ///    coordinate conventions disagree, and reading all of it cannot.
        /// </summary>
        private static void Save(string template, Sprite sprite)
        {
            try
            {
                var source = sprite.texture;

                if (source == null)
                {
                    return;
                }

                var rect = sprite.textureRect;
                var width = Mathf.RoundToInt(rect.width);
                var height = Mathf.RoundToInt(rect.height);

                if (width < 1 || height < 1)
                {
                    return;
                }

                var png = Crop(source, rect, width, height);

                if (png == null)
                {
                    return;
                }

                Directory.CreateDirectory(CacheFolder);
                File.WriteAllBytes(Path.Combine(CacheFolder, template + ".png"), png);
            }
            catch (Exception ex)
            {
                // Not worth a warning every launch: the icon still works, it is only the
                // saving of it that did not.
                SlotClientPlugin.Log.LogInfo($"[Slots] {template} was drawn but not cached: {ex.Message}");
            }
        }

        private static byte[] Crop(Texture2D source, Rect rect, int width, int height)
        {
            Texture2D cut = null;

            try
            {
                try
                {
                    // The straightforward way, when the texture allows it. The icon
                    // renderer writes its own icons to disk, so quite often it does.
                    var pixels = source.GetPixels(
                        Mathf.RoundToInt(rect.x), Mathf.RoundToInt(rect.y), width, height);

                    cut = new Texture2D(width, height, TextureFormat.RGBA32, false);
                    cut.SetPixels(pixels);
                    cut.Apply();

                    return cut.EncodeToPNG();
                }
                catch (UnityException)
                {
                    // Not readable. Through the GPU instead.
                }

                var buffer = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32);
                var previous = RenderTexture.active;

                // Restoring the active render texture is a finally, not two lines after
                // the read. `RenderTexture.active` is global to the whole game: leaving
                // it pointed at this buffer -- worse, at this buffer after it has gone
                // back to the temporary pool -- is not a leak confined to the mod, it is
                // the game's next frame drawing into somebody else's target. ReadPixels
                // and Apply are exactly the calls that throw on a texture the driver
                // will not hand back, and until this release nothing here had ever run
                // on a player's machine, because no icon had ever rendered to save.
                try
                {
                    // The crop as a UV transform, so the render texture holds exactly the
                    // sprite and the read below can take all of it.
                    Graphics.Blit(
                        source,
                        buffer,
                        new Vector2(rect.width / source.width, rect.height / source.height),
                        new Vector2(rect.x / source.width, rect.y / source.height));

                    RenderTexture.active = buffer;

                    cut = new Texture2D(width, height, TextureFormat.RGBA32, false);
                    cut.ReadPixels(new Rect(0f, 0f, width, height), 0, 0);
                    cut.Apply();
                }
                finally
                {
                    RenderTexture.active = previous;
                    RenderTexture.ReleaseTemporary(buffer);
                }

                return cut.EncodeToPNG();
            }
            finally
            {
                if (cut != null)
                {
                    UnityEngine.Object.Destroy(cut);
                }
            }
        }
    }
}
