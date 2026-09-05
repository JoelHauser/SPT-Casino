using System;
using System.Collections;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Textures = Roulette.Client.Textures;

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
        internal static bool Anything => IsOpen || CasinoIntro.IsOpen || Games.Playing() != null;

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
                CasinoIntro.Open(() => Show(instant: true));
                return;
            }

            Show();
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

            var title = NewText("Title", canvasObject.transform, "SPT CASINO", 44f);
            title.rectTransform.anchorMin = title.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            title.rectTransform.sizeDelta = new Vector2(900f, 60f);
            title.rectTransform.anchoredPosition = new Vector2(0f, 250f);
            title.color = Gold;

            var sub = NewText("Sub", canvasObject.transform, "Pick a table.", 22f);
            sub.rectTransform.anchorMin = sub.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            sub.rectTransform.sizeDelta = new Vector2(900f, 30f);
            sub.rectTransform.anchoredPosition = new Vector2(0f, 200f);

            BuildTiles(canvasObject.transform);

            var hint = NewText("Hint", canvasObject.transform, "Escape closes the casino. At a table it brings you back here.", 19f);
            hint.rectTransform.anchorMin = hint.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            hint.rectTransform.sizeDelta = new Vector2(1200f, 28f);
            hint.rectTransform.anchoredPosition = new Vector2(0f, -230f);
            hint.color = new Color(0.65f, 0.63f, 0.58f, 1f);

            BuildButton(canvasObject.transform, "CLOSE", new Vector2(0f, -300f), CloseEverything);
        }

        /// <summary>
        /// One tile per game, in a row, centred as a group.
        ///
        /// Sized and placed rather than laid out by a group component, because a
        /// HorizontalLayoutGroup on a canvas this size fights the scaler and the tiles
        /// end up a pixel out from each other at some resolutions.
        /// </summary>
        private static void BuildTiles(Transform parent)
        {
            const float width = 300f;
            const float height = 240f;
            const float gap = 36f;

            var games = Games.All;
            var span = (games.Count * width) + ((games.Count - 1) * gap);
            var left = -span * 0.5f;

            for (var i = 0; i < games.Count; i++)
            {
                var game = games[i];

                var tile = NewBox("Tile_" + game.Name, parent, Color.white);
                tile.sizeDelta = new Vector2(width, height);
                tile.anchoredPosition = new Vector2(left + (i * (width + gap)) + (width * 0.5f), 20f);

                var face = tile.GetComponent<Image>();
                face.sprite = Textures.RoundedBox(10, Tile, TileEdge, 2);
                face.type = Image.Type.Sliced;

                var pip = NewBox("Pip", tile, Color.white);
                pip.sizeDelta = new Vector2(84f, 84f);
                pip.anchoredPosition = new Vector2(0f, 52f);
                pip.GetComponent<Image>().sprite = Textures.Suit(game.Pip, Gold);
                pip.GetComponent<Image>().raycastTarget = false;

                var name = NewText("Name", tile, game.Name, 26f);
                name.rectTransform.anchorMin = name.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
                name.rectTransform.sizeDelta = new Vector2(width - 24f, 34f);
                name.rectTransform.anchoredPosition = new Vector2(0f, -28f);
                name.color = Gold;

                var blurb = NewText("Blurb", tile, game.Blurb, 17f);
                blurb.rectTransform.anchorMin = blurb.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
                blurb.rectTransform.sizeDelta = new Vector2(width - 34f, 60f);
                blurb.rectTransform.anchoredPosition = new Vector2(0f, -78f);
                blurb.enableWordWrapping = true;
                blurb.color = new Color(0.70f, 0.68f, 0.63f, 1f);

                var chosen = game;
                tile.gameObject.AddComponent<Button>().onClick.AddListener(() => Enter(chosen));
            }
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
