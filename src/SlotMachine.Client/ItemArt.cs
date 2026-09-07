using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Casino.Shared;
using Comfort.Common;
using EFT;
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
    /// So the path is: a template id, an `Item` from `Singleton&lt;ItemFactory&gt;`, and
    /// that item handed to the renderer. Three calls, all public, all read off the
    /// assembly rather than remembered:
    ///
    /// ```
    /// Singleton&lt;ItemFactory&gt;.Instance.CreateItem(MongoID.Generate(true), template, null)
    /// ItemViewFactory.GetItemSpriteAsync(item, ScaleFactor)   ->   Task&lt;Sprite&gt;
    /// ```
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
    /// A rendered icon is saved beside the plugin as a PNG, so the second launch reads a
    /// file instead of posing a camera at a helmet. The cache is only written when the
    /// sprite owns its whole texture -- an atlas region would need cropping, and a
    /// wrongly cropped icon is worse than a slow one.
    /// </summary>
    internal static class ItemArt
    {
        /// <summary>
        /// Which real item each reel symbol is.
        ///
        /// Read out of `SPT_Data/database/templates/items.json` rather than typed from
        /// memory, which matters more than it sounds: the id that comes to mind for
        /// "BEAR dogtag" is the USEC one, and the Labs keycard has two plausible ids of
        /// which only one is the violet.
        /// </summary>
        private static readonly Dictionary<string, string> Templates =
            new Dictionary<string, string>
            {
                ["Medkit"] = "5755356824597772cb798962",   // AI-2 medkit
                ["AmmoBox"] = "6570254fcfc010a0f5006a22",  // 7.62x51mm M61 ammo pack (20)
                ["Grenade"] = "5710c24ad2720bc3458b45a3",  // F-1 hand grenade
                ["Helmet"] = "5ac8d6885acfc400180ae7b0",   // Ops-Core FAST MT (Urban Tan)
                ["DogTag"] = "59f32bb586f774757e1e8442",   // Dogtag BEAR
                ["Roubles"] = "5449016a4bdc2d6f028b456f",  // Roubles
                ["GpCoin"] = "5d235b4d86f7742e017bc88a",   // GP coin
                ["Bitcoin"] = "59faff1d86f7746c51718c9c",  // Physical Bitcoin
                ["Keycard"] = "5c1e495a86f7743109743dfb",  // TerraGroup Labs keycard (Violet)
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
                if (symbol == null || Ready.ContainsKey(symbol))
                {
                    continue;
                }

                var cached = FromDisk(symbol);

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

                var cached = FromDisk(symbol);

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
                    Asked.Remove(symbol);
                    unable = true;
                    break;
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
                Save(symbol, task.Result);
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
                if (!Singleton<ItemFactory>.Instantiated || !Singleton<ItemIconCreator>.Instantiated)
                {
                    return false;
                }

                var item = Singleton<ItemFactory>.Instance.CreateItem(MongoID.Generate(true), template, null);

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
                SlotClientPlugin.Log.LogWarning($"[Slots] could not ask the game for {symbol}: {ex.Message}");
                return false;
            }
        }

        private static Sprite FromDisk(string symbol)
        {
            try
            {
                return Textures.FromFile(Path.Combine(CacheFolder, symbol.ToLowerInvariant() + ".png"));
            }
            catch (Exception ex)
            {
                SlotClientPlugin.Log.LogWarning($"[Slots] could not read the cached {symbol}: {ex.Message}");
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
        private static void Save(string symbol, Sprite sprite)
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
                File.WriteAllBytes(Path.Combine(CacheFolder, symbol.ToLowerInvariant() + ".png"), png);
            }
            catch (Exception ex)
            {
                // Not worth a warning every launch: the icon still works, it is only the
                // saving of it that did not.
                SlotClientPlugin.Log.LogInfo($"[Slots] {symbol} was drawn but not cached: {ex.Message}");
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

                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(buffer);

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
