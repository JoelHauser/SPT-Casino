using System;
using System.Collections;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Textures = Casino.Shared.Textures;

namespace Casino.Client
{
    /// <summary>
    /// The room the tables are in.
    ///
    /// One tab opens this; this opens a game. Pressing escape at a table comes back
    /// here rather than out to the menu, which is the whole reason the casino is a
    /// place rather than three doors on the same corridor -- see
    /// <see cref="CasinoEscape"/>.
    ///
    /// Drawn with the same procedural pieces as the tables, so it is the same room:
    /// <see cref="Textures.RoundedBox"/> for the tiles and <see cref="Textures.Suit"/>
    /// for the pips, both of which the tables have been using since before this
    /// existed. Nothing here loads an image.
    /// </summary>
    internal static class CasinoLobby
    {
        private const string RootName = "CasinoLobbyCanvas";

        private static readonly Color Gold = new Color(0.85f, 0.72f, 0.38f, 1f);
        private static readonly Color Ink = new Color(0.93f, 0.91f, 0.86f, 1f);
        private static readonly Color Tile = new Color(0.10f, 0.11f, 0.12f, 0.96f);
        private static readonly Color TileEdge = new Color(0.45f, 0.38f, 0.22f, 1f);

        private static GameObject _root;
        private static CanvasGroup _group;
        private static TMP_FontAsset _font;
        private static Coroutine _fade;
        private static bool _closing;

        internal static bool IsOpen => _root != null && _root.activeSelf && !_closing;

        /// <summary>
        /// True when the casino is showing anything at all -- the lobby, the intro, or
        /// a table. What the tab and the escape key ask.
        /// </summary>
        internal static bool Anything =>
            IsOpen || CasinoIntro.IsOpen || CasinoGift.IsOpen || Games.Playing() != null;

        internal static void Toggle()
        {
            if (Anything)
            {
                CloseEverything();
                return;
            }

            // The intro comes first, once per account, and the lobby is what it opens
            // on to. A player who has read it never sees this branch again.
            if (CasinoIntro.ShouldShow())
            {
                CasinoIntro.Open(AfterIntro);
                return;
            }

            // Then the 1.2.6 apology, once per profile, which the server decides on.
            // Its money moves as it opens rather than when it is dismissed, so nothing
            // below this point can cost a player the gift.
            if (CasinoGift.ShouldShow())
            {
                CasinoGift.Open(() => Show(instant: true));
                return;
            }

            Show();
        }

        /// <summary>
        /// What the welcome card opens on to.
        ///
        /// The lobby goes up solid first either way, so the welcome has something
        /// underneath it to fade away over -- see the note on <see cref="Show"/>'s
        /// instant flag. A profile new enough to be reading the welcome on 1.2.6 is
        /// also owed the apology, so the gift card is built straight on top of the
        /// lobby while the welcome is still fading off it. It sits a layer above both.
        /// </summary>
        private static void AfterIntro()
        {
            Show(instant: true);

            if (CasinoGift.ShouldShow())
            {
                // No continuation: the lobby is already up and solid behind it.
                CasinoGift.Open(null);
            }
        }

        /// <summary>
        /// Shows the lobby. Also what a table falls back to when it closes, which is
        /// why it is separate from <see cref="Toggle"/>.
        /// </summary>
        /// <param name="instant">
        /// Skip the fade and come up solid.
        ///
        /// Used whenever something opaque is already covering the screen: the welcome
        /// card and the tables both draw above the lobby, so bringing it up underneath
        /// them costs nothing visually and there is nothing to fade in from.
        ///
        /// **Fading in from zero is what caused the flash.** Continue used to destroy
        /// the welcome card and then start a fade, so for the length of that fade the
        /// only thing on screen was the menu, and the casino appeared to blink out and
        /// come back. Building the lobby first and then taking the cover away has no
        /// frame in it where neither is drawn.
        /// </param>
        internal static void Show(bool instant = false)
        {
            try
            {
                if (_root == null)
                {
                    Build();
                }

                if (_root == null)
                {
                    return;
                }

                _closing = false;
                _root.SetActive(true);

                if (instant)
                {
                    if (_fade != null && CasinoPlugin.Instance != null)
                    {
                        CasinoPlugin.Instance.StopCoroutine(_fade);
                        _fade = null;
                    }

                    _group.alpha = 1f;
                    return;
                }

                FadeTo(1f, null);
            }
            catch (Exception ex)
            {
                CasinoPlugin.Log.LogError("[Casino] could not open the lobby: " + ex);
            }
        }

        internal static void Close()
        {
            if (_root == null || !_root.activeSelf || _closing)
            {
                return;
            }

            _closing = true;

            FadeTo(0f, () =>
            {
                _root.SetActive(false);
                _closing = false;
            });
        }

        /// <summary>Shuts the whole casino: the table, the intro and the lobby.</summary>
        internal static void CloseEverything()
        {
            Games.CloseAll();
            CasinoIntro.Close();
            CasinoGift.Close();
            Close();
        }

        /// <summary>
        /// Leaves a table and comes back here.
        ///
        /// The lobby comes up solid underneath first and the table then fades off the
        /// top of it, so the room is already there when the table goes. It reads as
        /// stepping back rather than as the screen going dark and something arriving.
        /// </summary>
        internal static void Leave(ICasinoGame game)
        {
            // The lobby first, solid, and then the table fades off the top of it. The
            // other order dips through the menu in the middle of the two fades: both
            // backdrops sit at 93%, so half way through neither is covering anything
            // and the menu shows through the pair of them.
            Show(instant: true);
            game?.Close();
        }

        // ------------------------------------------------------------------ drawing

        private static void Build()
        {
            _font = Resources.FindObjectsOfTypeAll<TMP_FontAsset>().FirstOrDefault();

            var canvasObject = new GameObject(RootName, typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            UnityEngine.Object.DontDestroyOnLoad(canvasObject);
            _root = canvasObject;

            var canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            // Above the menu, below the tables. A table opened from here draws over it
            // rather than through it.
            canvas.sortingOrder = 2900;

            var scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 1f;

            _group = canvasObject.AddComponent<CanvasGroup>();
            _group.alpha = 0f;

            var backdrop = NewBox("Backdrop", canvasObject.transform, new Color(0f, 0f, 0f, 0.93f));
            backdrop.anchorMin = Vector2.zero;
            backdrop.anchorMax = Vector2.one;
            backdrop.offsetMin = Vector2.zero;
            backdrop.offsetMax = Vector2.zero;

            // The tiles go down first, and everything else is positioned off the block
            // they occupy rather than at numbers of its own. With one row that lands
            // the title, the hint and CLOSE exactly where they always were; with two it
            // moves them out of the way instead of letting a second row run through
            // them. See BuildTiles.
            var tiles = BuildTiles(canvasObject.transform);

            var sub = NewText("Sub", canvasObject.transform, "Pick a table.", 22f);
            sub.rectTransform.anchorMin = sub.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            sub.rectTransform.sizeDelta = new Vector2(900f, 30f);
            sub.rectTransform.anchoredPosition = new Vector2(0f, tiles.yMax + 60f);

            var title = NewText("Title", canvasObject.transform, "SPT CASINO", 44f);
            title.rectTransform.anchorMin = title.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            title.rectTransform.sizeDelta = new Vector2(900f, 60f);
            title.rectTransform.anchoredPosition = new Vector2(0f, tiles.yMax + 110f);
            title.color = Gold;

            var hint = NewText("Hint", canvasObject.transform, "Escape closes the casino. At a table it brings you back here.", 19f);
            hint.rectTransform.anchorMin = hint.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            hint.rectTransform.sizeDelta = new Vector2(1200f, 28f);
            hint.rectTransform.anchoredPosition = new Vector2(0f, tiles.yMin - 130f);
            hint.color = new Color(0.65f, 0.63f, 0.58f, 1f);

            BuildButton(canvasObject.transform, "CLOSE", new Vector2(0f, tiles.yMin - 200f), CloseEverything);
        }

        /// <summary>
        /// How many tiles go on one row before a new one starts.
        ///
        /// **Four, and it is a standing rule rather than a number that happened to
        /// suit five games.** A fifth table arriving in September 2026 was the first
        /// time the single row did not fit; rather than widening it again and again,
        /// the lobby now wraps, and every fourth table after this one starts a row of
        /// its own without anybody editing this file.
        /// </summary>
        private const int PerRow = 4;

        /// <summary>Where the block of tiles ended up, so the rest of the lobby can dodge it.</summary>
        private readonly struct TileBlock
        {
            internal TileBlock(float yMin, float yMax)
            {
                this.yMin = yMin;
                this.yMax = yMax;
            }

            internal float yMin { get; }

            internal float yMax { get; }
        }

        /// <summary>
        /// The tiles, four to a row, each row centred and the block centred as a whole.
        ///
        /// Sized and placed rather than laid out by a group component, because a
        /// HorizontalLayoutGroup on a canvas this size fights the scaler and the tiles
        /// end up a pixel out from each other at some resolutions. A GridLayoutGroup
        /// would have the same problem and would additionally left-align the last row,
        /// which on five games means one tile hanging off the left rather than sitting
        /// under the middle of the four above it.
        ///
        /// **A second row shrinks the tiles.** Two rows at the single-row size come to
        /// 516 units, and the canvas only has 1080 to give -- the scaler matches height
        /// against a 1080 reference with matchWidthOrHeight at 1, so that budget is the
        /// same at every resolution rather than something to test per monitor.
        ///
        /// The single-row case is arithmetically unchanged: with four games or fewer
        /// this puts the title at 250, the subtitle at 200, the hint at -230 and CLOSE
        /// at -300, which are the exact numbers they were hardcoded to before.
        ///
        /// **It fits comfortably to eight games and runs out at thirteen.** Three rows
        /// (nine to twelve) puts CLOSE at -546 against a -540 edge, so it is already
        /// six units over and wants the tiles shrinking again; four rows is far past
        /// it. Whoever adds a ninth table should shrink rather than assume this scales
        /// -- it does not, and it fails by drawing off the bottom of the screen rather
        /// than by complaining.
        /// </summary>
        private static TileBlock BuildTiles(Transform parent)
        {
            var games = Games.All;
            var rows = ((games.Count - 1) / PerRow) + 1;

            var width = rows > 1 ? 272f : 300f;
            var height = rows > 1 ? 212f : 240f;
            var gap = rows > 1 ? 30f : 36f;
            const float rowGap = 26f;

            // Where a single row has always sat. The block stays centred on it, so one
            // row is unchanged and two straddle it evenly.
            const float centreY = 20f;

            var blockHeight = (rows * height) + ((rows - 1) * rowGap);
            var blockTop = centreY + (blockHeight * 0.5f);

            for (var i = 0; i < games.Count; i++)
            {
                var game = games[i];

                var row = i / PerRow;
                var column = i % PerRow;

                // The last row is usually short, and it is centred on its own count
                // rather than on PerRow -- otherwise the fifth game sits under the
                // first column instead of under the middle of the row above it.
                var inRow = Math.Min(PerRow, games.Count - (row * PerRow));
                var span = (inRow * width) + ((inRow - 1) * gap);
                var left = -span * 0.5f;

                var tile = NewBox("Tile_" + game.Name, parent, Color.white);
                tile.sizeDelta = new Vector2(width, height);
                tile.anchoredPosition = new Vector2(
                    left + (column * (width + gap)) + (width * 0.5f),
                    blockTop - (row * (height + rowGap)) - (height * 0.5f));

                var face = tile.GetComponent<Image>();
                face.sprite = Textures.RoundedBox(10, Tile, TileEdge, 2);
                face.type = Image.Type.Sliced;

                // The table's own artwork, or a drawn suit if it is not on disk.
                //
                // Untinted when it is artwork: these are black-and-white glyphs with
                // their own shading, and tinting them gold would flatten that into one
                // colour. The drawn fallback is a flat shape and does want the tint.
                var art = Textures.FromFile(System.IO.Path.Combine(Casino.Shared.Host.AssetFolder, game.Icon));

                // Everything inside a tile is a fraction of it rather than a fixed
                // number, so the shrunk two-row tile keeps the same proportions
                // instead of a full-size glyph crowding a smaller box.
                var scale = height / 240f;

                var pip = NewBox("Pip", tile, Color.white);
                pip.sizeDelta = new Vector2(104f * scale, 104f * scale);
                pip.anchoredPosition = new Vector2(0f, 46f * scale);

                var pipImage = pip.GetComponent<Image>();
                pipImage.sprite = art ?? Textures.Suit(game.Pip, Gold);
                pipImage.color = Color.white;
                pipImage.preserveAspect = true;
                pipImage.raycastTarget = false;

                if (art == null)
                {
                    CasinoPlugin.Log.LogInfo(
                        $"[Casino] no {game.Icon} beside the plugin; {game.Name} falls back to a drawn suit.");
                }

                var name = NewText("Name", tile, game.Name, 26f * scale);
                name.rectTransform.anchorMin = name.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
                name.rectTransform.sizeDelta = new Vector2(width - 24f, 34f);
                name.rectTransform.anchoredPosition = new Vector2(0f, -34f * scale);
                name.color = Gold;

                var blurb = NewText("Blurb", tile, game.Blurb, 17f * scale);
                blurb.rectTransform.anchorMin = blurb.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
                blurb.rectTransform.sizeDelta = new Vector2(width - 34f, 54f * scale);
                blurb.rectTransform.anchoredPosition = new Vector2(0f, -84f * scale);
                blurb.enableWordWrapping = true;
                blurb.color = new Color(0.70f, 0.68f, 0.63f, 1f);

                var chosen = game;
                tile.gameObject.AddComponent<Button>().onClick.AddListener(() => Enter(chosen));
            }

            return new TileBlock(centreY - (blockHeight * 0.5f), blockTop);
        }

        /// <summary>
        /// Goes to a table. The lobby closes behind the player rather than staying lit
        /// under it -- two backdrops at 93% is nearly black.
        /// </summary>
        private static void Enter(ICasinoGame game)
        {
            Close();
            game.Open();
        }

        private static void BuildButton(Transform parent, string label, Vector2 at, Action onClick)
        {
            var box = NewBox("Button_" + label, parent, Color.white);
            box.sizeDelta = new Vector2(180f, 44f);
            box.anchoredPosition = at;

            var image = box.GetComponent<Image>();
            image.sprite = Textures.RoundedBox(6, new Color(0.16f, 0.16f, 0.17f, 1f), TileEdge, 2);
            image.type = Image.Type.Sliced;

            var text = NewText("Label", box, label, 20f);
            text.rectTransform.anchorMin = Vector2.zero;
            text.rectTransform.anchorMax = Vector2.one;
            text.rectTransform.offsetMin = Vector2.zero;
            text.rectTransform.offsetMax = Vector2.zero;
            text.color = Ink;

            box.gameObject.AddComponent<Button>().onClick.AddListener(() => onClick());
        }

        // ------------------------------------------------------------------ pieces

        private static void FadeTo(float target, Action done)
        {
            var host = CasinoPlugin.Instance;

            if (host == null || _group == null)
            {
                if (_group != null)
                {
                    _group.alpha = target;
                }

                done?.Invoke();
                return;
            }

            if (_fade != null)
            {
                host.StopCoroutine(_fade);
            }

            _fade = host.StartCoroutine(Fade(target, done));
        }

        private static IEnumerator Fade(float target, Action done)
        {
            const float seconds = 0.13f;
            var from = _group.alpha;

            for (var t = 0f; t < seconds; t += Time.unscaledDeltaTime)
            {
                _group.alpha = Mathf.Lerp(from, target, t / seconds);
                yield return null;
            }

            _group.alpha = target;
            _fade = null;
            done?.Invoke();
        }

        internal static RectTransform NewBox(string name, Transform parent, Color colour)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.transform.SetParent(parent, false);

            var rect = (RectTransform)go.transform;
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);

            go.GetComponent<Image>().color = colour;

            return rect;
        }

        internal static TextMeshProUGUI NewText(string name, Transform parent, string text, float size)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);

            var label = go.AddComponent<TextMeshProUGUI>();
            label.text = text;
            label.fontSize = size;
            label.alignment = TextAlignmentOptions.Center;
            label.color = Ink;
            label.raycastTarget = false;
            label.enableWordWrapping = false;

            if (_font != null)
            {
                label.font = _font;
            }

            var rect = (RectTransform)go.transform;
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);

            return label;
        }

        /// <summary>The font the lobby borrowed, so the intro can use the same one.</summary>
        internal static TMP_FontAsset Font => _font;
    }
}
