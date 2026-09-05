using System.IO;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Blackjack.Client
{
    /// <summary>
    /// Draws one playing card.
    ///
    /// The server sends cards as two characters, rank then suit: "TD" is the ten of
    /// diamonds, "AS" the ace of spades. If the matching image is installed beside
    /// the plugin it is used; the card files are named for those same two characters,
    /// so the lookup is a string format rather than a table.
    ///
    /// Without the images it falls back to drawing the card: rounded ivory face, the
    /// rank in opposite corners the way a real card carries it, and the suit through
    /// the middle. That path is worth keeping. It is what runs if someone deletes the
    /// art, and it is the only thing that draws a court card's rank at all -- a drawn
    /// king is a K and a crown-less pip, where the real image has a portrait on it.
    /// </summary>
    internal static class CardView
    {
        internal const float Width = 96f;
        internal const float Height = 138f;

        private static readonly Color Face = new Color(0.95f, 0.94f, 0.90f, 1f);
        private static readonly Color Edge = new Color(0.72f, 0.70f, 0.65f, 1f);
        private static readonly Color Red = new Color(0.70f, 0.11f, 0.12f, 1f);
        private static readonly Color Black = new Color(0.10f, 0.10f, 0.11f, 1f);

        // The back of a card, for the dealer's hole card while the hand is live. Drawn
        // rather than loaded: the card set has no back in it.
        private static readonly Color BackFace = new Color(0.42f, 0.10f, 0.12f, 1f);
        private static readonly Color BackEdge = new Color(0.90f, 0.88f, 0.84f, 1f);
        private static readonly Color BackPattern = new Color(0.30f, 0.07f, 0.09f, 1f);

        private static string _cardDirectory;

        internal static GameObject Build(Transform parent, string code, TMP_FontAsset font)
        {
            var faceDown = string.IsNullOrEmpty(code) || code.Length < 2;

            var go = new GameObject(
                faceDown ? "Card_back" : "Card_" + code,
                typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.transform.SetParent(parent, false);

            var rect = (RectTransform)go.transform;
            rect.sizeDelta = new Vector2(Width, Height);

            var image = go.GetComponent<Image>();

            // A drop shadow, so cards sit on the cloth rather than being printed on it.
            var shadow = go.AddComponent<Shadow>();
            shadow.effectColor = new Color(0f, 0f, 0f, 0.45f);
            shadow.effectDistance = new Vector2(3f, -4f);

            if (faceDown)
            {
                BuildBack(rect, image);
                return go;
            }

            var photo = Textures.FromFile(PathFor(code));
            if (photo != null)
            {
                image.sprite = photo;
                image.type = Image.Type.Simple;
                return go;
            }

            BuildDrawn(rect, image, code, font);
            return go;
        }

        private static string PathFor(string code)
        {
            if (_cardDirectory == null)
            {
                var beside = Path.GetDirectoryName(BlackjackClientPlugin.Instance?.Info?.Location ?? ".") ?? ".";
                _cardDirectory = Path.Combine(beside, "cards");
            }

            // Upper case, because the server's codes are and a file system that cares
            // would otherwise find nothing on one machine and everything on another.
            return Path.Combine(_cardDirectory, code.ToUpperInvariant() + ".png");
        }

        private static void BuildBack(RectTransform rect, Image image)
        {
            image.type = Image.Type.Sliced;
            image.sprite = Textures.RoundedBox(10, BackFace, BackEdge, 3);

            var inner = NewImage("Pattern", rect, Color.white);
            inner.sprite = Textures.RoundedBox(8, BackPattern, BackPattern);
            inner.type = Image.Type.Sliced;

            var innerRect = (RectTransform)inner.transform;
            innerRect.anchorMin = Vector2.zero;
            innerRect.anchorMax = Vector2.one;
            innerRect.offsetMin = new Vector2(10f, 10f);
            innerRect.offsetMax = new Vector2(-10f, -10f);
        }

        private static void BuildDrawn(RectTransform rect, Image image, string code, TMP_FontAsset font)
        {
            image.type = Image.Type.Sliced;
            image.sprite = Textures.RoundedBox(10, Face, Edge, 2);

            var rank = RankOf(code);
            var suit = char.ToUpperInvariant(code[code.Length - 1]);
            var colour = IsRed(suit) ? Red : Black;

            Corner(rect, rank, suit, font, colour, false);
            Corner(rect, rank, suit, font, colour, true);

            var pip = NewImage("Pip", rect, Color.white);
            pip.sprite = Textures.Suit(suit, colour);
            pip.preserveAspect = true;
            var pipRect = (RectTransform)pip.transform;
            pipRect.anchorMin = pipRect.anchorMax = new Vector2(0.5f, 0.5f);
            pipRect.pivot = new Vector2(0.5f, 0.5f);
            pipRect.sizeDelta = new Vector2(46f, 46f);
            pipRect.anchoredPosition = Vector2.zero;
        }

        /// <summary>
        /// Rank over suit in a corner, the second copy rotated a half turn.
        ///
        /// Geometry worth spelling out, because guessing at it collided twice. The card
        /// is 96 by 138 with its origin in the middle, so the top edge is at +69. This
        /// block is 38 tall centred 25 below that edge, which puts its lower edge at
        /// +25 -- clear of the centre pip, which is 46 across and so reaches only +23.
        ///
        /// The pivot is the middle of the block, not the corner of the card. Rotating
        /// about a pivot sitting on the card's edge swings the whole block outside it,
        /// which is what put a stray red D on the cloth below the dealer's hand.
        /// </summary>
        private static void Corner(RectTransform card, string rank, char suit, TMP_FontAsset font, Color colour, bool flipped)
        {
            var holder = new GameObject(flipped ? "CornerFlipped" : "Corner", typeof(RectTransform));
            holder.transform.SetParent(card, false);

            var rect = (RectTransform)holder.transform;
            rect.sizeDelta = new Vector2(24f, 38f);
            rect.anchorMin = rect.anchorMax = flipped ? new Vector2(1f, 0f) : new Vector2(0f, 1f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = flipped ? new Vector2(-18f, 25f) : new Vector2(18f, -25f);
            rect.localRotation = Quaternion.Euler(0f, 0f, flipped ? 180f : 0f);

            var label = Text(rect, rank, font, 21f, colour);
            var labelRect = label.rectTransform;
            labelRect.anchorMin = new Vector2(0f, 0.44f);
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = Vector2.zero;
            labelRect.offsetMax = Vector2.zero;

            var pip = NewImage("Pip", rect, Color.white);
            pip.sprite = Textures.Suit(suit, colour);
            pip.preserveAspect = true;
            var pipRect = (RectTransform)pip.transform;
            pipRect.anchorMin = pipRect.anchorMax = new Vector2(0.5f, 0f);
            pipRect.pivot = new Vector2(0.5f, 0f);
            pipRect.sizeDelta = new Vector2(13f, 13f);
            pipRect.anchoredPosition = new Vector2(0f, 0f);
        }

        private static Image NewImage(string name, Transform parent, Color colour)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.transform.SetParent(parent, false);
            var image = go.GetComponent<Image>();
            image.color = colour;
            image.raycastTarget = false;
            return image;
        }

        private static TextMeshProUGUI Text(Transform parent, string value, TMP_FontAsset font, float size, Color colour)
        {
            var go = new GameObject("Rank", typeof(RectTransform));
            go.transform.SetParent(parent, false);

            var label = go.AddComponent<TextMeshProUGUI>();
            if (font != null)
            {
                label.font = font;
            }

            label.text = value;
            label.fontSize = size;
            label.color = colour;
            label.alignment = TextAlignmentOptions.Center;
            label.enableWordWrapping = false;
            label.raycastTarget = false;
            return label;
        }

        private static string RankOf(string code)
        {
            var rank = code[0];
            return rank == 'T' ? "10" : rank.ToString();
        }

        private static bool IsRed(char suit) => suit == 'H' || suit == 'D';
    }
}
