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
    /// ## It is allowed to fail
    ///
    /// Rendering needs a live `ItemIconCreator`, which needs a session. Open the panel
    /// early enough, or on a build where a name has moved, and this quietly gives up and
    /// the reels keep the drawn art they shipped with. **A missing icon must never be
    /// able to take the machine down**, so every step is inside a try and every failure
    /// is a log line and a fallback.
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

        private static bool _running;

        /// <summary>The game's icon for a symbol, or null if there is not one yet.</summary>
        internal static Sprite For(string symbol) =>
            symbol != null && Ready.TryGetValue(symbol, out var sprite) ? sprite : null;

        /// <summary>Where a rendered icon is kept between launches.</summary>
        private static string CacheFolder => Path.Combine(Host.AssetFolder, "symbols/ingame");

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

            foreach (var symbol in symbols)
            {
                if (symbol == null || Ready.ContainsKey(symbol) || Asked.Contains(symbol))
                {
                    continue;
                }

                if (!Templates.TryGetValue(symbol, out var template))
                {
                    // A symbol the server knows about and this build does not. It keeps
                    // whatever art shipped with it, which is the right outcome.
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
                    // No session, no renderer. Let it be asked again next time the panel
                    // opens rather than giving up for the run.
                    Asked.Remove(symbol);
                    break;
                }

                while (!task.IsCompleted)
                {
                    yield return null;
                }

                if (task.IsFaulted || task.Result == null)
                {
                    SlotClientPlugin.Log.LogWarning(
                        $"[Slots] the game could not draw {symbol} ({template}); keeping the shipped art.");

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

            _running = false;
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
        /// Only when the sprite owns its whole texture. A sprite that is a region of an
        /// atlas would have to be cropped, and the two coordinate conventions in play --
        /// the sprite's, and the one <c>ReadPixels</c> uses -- disagree about which way
        /// up the image is. A wrong crop is worse than no cache, so that case is simply
        /// left uncached and re-rendered next launch.
        /// </summary>
        private static void Save(string symbol, Sprite sprite)
        {
            try
            {
                var source = sprite.texture;

                if (source == null
                    || (int)sprite.textureRect.width != source.width
                    || (int)sprite.textureRect.height != source.height)
                {
                    return;
                }

                Directory.CreateDirectory(CacheFolder);

                // Through a RenderTexture, because the icon's own texture is not
                // readable and EncodeToPNG needs one that is.
                var buffer = RenderTexture.GetTemporary(
                    source.width, source.height, 0, RenderTextureFormat.ARGB32);

                var previous = RenderTexture.active;

                Graphics.Blit(source, buffer);
                RenderTexture.active = buffer;

                var readable = new Texture2D(source.width, source.height, TextureFormat.RGBA32, false);
                readable.ReadPixels(new Rect(0f, 0f, source.width, source.height), 0, 0);
                readable.Apply();

                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(buffer);

                File.WriteAllBytes(
                    Path.Combine(CacheFolder, symbol.ToLowerInvariant() + ".png"), readable.EncodeToPNG());

                UnityEngine.Object.Destroy(readable);
            }
            catch (Exception ex)
            {
                // Not worth a warning every launch: the icon still works, it is only the
                // saving of it that did not.
                SlotClientPlugin.Log.LogInfo($"[Slots] {symbol} was drawn but not cached: {ex.Message}");
            }
        }
    }
}
