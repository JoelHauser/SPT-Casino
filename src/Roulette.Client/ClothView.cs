using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Roulette.Client
{
    /// <summary>
    /// The betting cloth.
    ///
    /// ## The layout is the rules
    ///
    /// The grid is three rows of twelve running 1..36 **up the columns**, which is why
    /// the top row is 3, 6, 9 and the bottom row 1, 4, 7. That is not decoration: a
    /// street is a column of the printed grid, a corner is a square of four on it, and
    /// the three "2 to 1" boxes down the side are the *rows* of the print, which the
    /// rules call columns. Draw it any other way and every inside bet points at the
    /// wrong numbers.
    ///
    /// ## Line bets are spots, not cells
    ///
    /// A chip on a number is a straight-up bet; a chip on the line between two numbers
    /// is a split; on a corner where four meet, a corner bet. Those are real positions
    /// on a real cloth rather than extra buttons, so they are drawn where they belong
    /// -- small round targets sitting on the joins -- rather than as a separate list of
    /// controls.
    ///
    /// The engine already enumerates every legal one, and **a split is sent as an index
    /// into that list** because "the split on 1" is ambiguous between 1-2 and 1-4. This
    /// builds its targets from the same enumeration, so the cloth cannot offer a bet
    /// the server would refuse.
    /// </summary>
    internal static class ClothView
    {
        /// <summary>How wide one number cell is. Everything else is measured off it.</summary>
        private const float Cell = 66f;

        /// <summary>
        /// How much bigger the whole cloth is drawn than it is laid out.
        ///
        /// Applied as a scale on the root rather than by growing every constant, so the
        /// cells, the fonts, the line-bet dots and the chips all keep their proportions
        /// and there is exactly one number to change.
        ///
        /// It exists to make the value printed on a chip readable. The art is 440x440
        /// and the figure on it is only about 70 pixels tall -- 16% of the diameter --
        /// so at a 56-unit chip it lands around 9 pixels on a 1080p screen. The wheel
        /// and cloth together came to 1598 of the 1920 available, so the 322 spare were
        /// going to waste; this spends them.
        /// </summary>
        private const float Scale = 1.27f;

        private const float Rows = 3f;
        private const float Columns = 12f;

        /// <summary>The dozen and outside rows below the grid.</summary>
        private const float OutsideRow = 46f;

        /// <summary>The round targets that sit on the joins between cells.</summary>
        private const float SpotSize = 19f;

        /// <summary>How much wooden rail there is around the felt.</summary>
        private const float Surround = 22f;

        /// <summary>
        /// How big a chip is drawn on a number.
        ///
        /// Sized to be readable rather than to be tidy. The chip art is 440x440 with
        /// its value printed on the face, and at the 42 units this used to draw at, that
        /// value came out about ten pixels tall and unreadable -- which is what made a
        /// second number painted over the top look necessary. A number cell is 66 wide
        /// with a 3-unit gap, so 56 nearly fills it, the way a real chip nearly fills
        /// the square it is put on.
        /// </summary>
        private const float ChipOnNumber = 56f;

        /// <summary>Breathing room between a chip and the edge of the box it sits in.</summary>
        private const float ChipInset = 6f;

        private static readonly Color Felt = new Color(0.055f, 0.24f, 0.145f, 1f);
        private static readonly Color FeltEdge = new Color(0.72f, 0.62f, 0.34f, 1f);
        private static readonly Color Red = new Color(0.62f, 0.11f, 0.13f, 1f);
        private static readonly Color Black = new Color(0.09f, 0.09f, 0.10f, 1f);
        private static readonly Color Green = new Color(0.09f, 0.40f, 0.22f, 1f);
        private static readonly Color Ink = new Color(0.93f, 0.91f, 0.86f, 1f);
        private static readonly Color Spot = new Color(0.85f, 0.78f, 0.55f, 0.22f);

        /// <summary>The wooden rail the felt is inset into.</summary>
        private static readonly Color Rail = new Color(0.24f, 0.16f, 0.10f, 1f);

        private static readonly Color RailEdge = new Color(0.55f, 0.44f, 0.24f, 1f);

        private static Sprite _felt;

        private static TMP_FontAsset _font;

        /// <summary>The label of the cell being built, so Wire can register it.</summary>
        private static TextMeshProUGUI _pendingLabel;
        private static Action<string, int> _onBet;
        private static Action<string, int> _onLift;

        /// <summary>Every bet that has a place on the cloth, and where its chips go.</summary>
        private static readonly Dictionary<string, RectTransform> Stacks = new Dictionary<string, RectTransform>();

        /// <summary>
        /// How big a chip may be drawn on each spot.
        ///
        /// Not one number for the whole cloth: the outside rows are 46 units tall, so a
        /// chip sized for a number would hang out of them top and bottom.
        /// </summary>
        private static readonly Dictionary<string, float> ChipSizes = new Dictionary<string, float>();

        /// <summary>
        /// The printed number on each square, so it can be hidden under a chip.
        ///
        /// A real chip sits on top of the number and covers it. Leaving it showing round
        /// the edge of the chip is the sort of thing that reads as wrong without anyone
        /// being able to say why.
        /// </summary>
        private static readonly Dictionary<string, TextMeshProUGUI> Labels =
            new Dictionary<string, TextMeshProUGUI>();

        internal static float Width => (Columns + 2f) * Cell;

        internal static float Height => (Rows * Cell) + (2f * OutsideRow);

        /// <summary>
        /// The table as it is actually drawn, wooden rail included.
        ///
        /// Anything placed near the cloth has to clear this rather than the felt, which
        /// is the mistake the first pass made: the balance line and the chip tray were
        /// both measured off the felt and so ended up sitting on the wood.
        /// </summary>
        internal static float Framed => (Width + (2f * Surround)) * Scale;

        /// <summary>Framed height. Its half is <see cref="Reach"/>.</summary>
        internal static float FramedHeight => (Height + (2f * Surround)) * Scale;

        /// <summary>How far the table reaches above and below its centre.</summary>
        internal static float Reach => FramedHeight * 0.5f;

        /// <summary>
        /// Builds the cloth. <paramref name="onBet"/> is handed the bet kind and its
        /// selection, exactly as the server names them.
        /// </summary>
        internal static GameObject Build(
            Transform parent,
            ClothLayout layout,
            TMP_FontAsset font,
            Action<string, int> onBet,
            Action<string, int> onLift)
        {
            _font = font;
            _onBet = onBet;
            _onLift = onLift;
            Stacks.Clear();
            Labels.Clear();
            ChipSizes.Clear();

            // A wooden surround with the felt inset into it, rather than a green
            // rectangle with a line round it. The frame is what makes it read as a
            // table you are standing at instead of a control panel.
            var root = NewBox("Cloth", parent, Color.white);
            root.sizeDelta = new Vector2(Width + (2f * Surround), Height + (2f * Surround));
            root.localScale = Vector3.one * Scale;

            var frame = root.GetComponent<Image>();
            frame.sprite = Textures.RoundedBox(14, Rail, RailEdge, 3);
            frame.type = Image.Type.Sliced;

            var felt = NewBox("Felt", root, Color.white);
            felt.sizeDelta = new Vector2(Width, Height);
            felt.GetComponent<Image>().sprite = FeltSprite();

            // Origin at the top left of the number grid, which is one cell in from the
            // left edge because the zero has that column to itself.
            var left = (-Width * 0.5f) + Cell;
            var top = Height * 0.5f;

            BuildZero(felt, left, top);
            BuildNumbers(felt, left, top);
            BuildColumnBets(felt, left, top);
            BuildDozens(felt, left, top);
            BuildOutside(felt, left, top);
            BuildLineBets(felt, layout, left, top);

            return root.gameObject;
        }

        /// <summary>
        /// Shows what is on the cloth. Rebuilt from the server's list every time rather
        /// than tracked here, so a refused bet or a cleared table cannot leave a chip
        /// behind that the server does not think exists.
        /// </summary>
        internal static void ShowBets(IEnumerable<(string Kind, int Selection, int Amount)> bets)
        {
            foreach (var stack in Stacks.Values)
            {
                for (var i = stack.childCount - 1; i >= 0; i--)
                {
                    UnityEngine.Object.Destroy(stack.GetChild(i).gameObject);
                }
            }

            foreach (var label in Labels.Values)
            {
                label.enabled = true;
            }

            if (bets == null)
            {
                return;
            }

            foreach (var bet in bets)
            {
                var key = Key(bet.Kind, bet.Selection);

                if (Stacks.TryGetValue(key, out var stack))
                {
                    ChipView.BuildOnCloth(
                        stack,
                        bet.Amount,
                        _font,
                        ChipSizes.TryGetValue(key, out var size) ? size : ChipOnNumber);
                }

                // The chip covers the number, as it would on a cloth.
                if (Labels.TryGetValue(key, out var covered))
                {
                    covered.enabled = false;
                }
            }
        }

        /// <summary>
        /// The felt: green, with a grain to it and a vignette towards the rail.
        ///
        /// Flat colour is what made this look like a form rather than a table. Cloth is
        /// never one value -- it is darker where it meets the wood and it has a weave.
        /// Both are cheap to fake and neither survives being left out.
        /// </summary>
        private static Sprite FeltSprite()
        {
            if (_felt != null)
            {
                return _felt;
            }

            const int w = 512;
            const int h = 192;

            var texture = new Texture2D(w, h, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.HideAndDontSave,
            };

            var pixels = new Color32[w * h];
            var random = new System.Random(4517);

            for (var y = 0; y < h; y++)
            {
                for (var x = 0; x < w; x++)
                {
                    // Darker towards the edges, so the middle of the cloth lifts.
                    var u = ((x / (float)w) - 0.5f) * 2f;
                    var v = ((y / (float)h) - 0.5f) * 2f;
                    var vignette = 1f - (0.34f * Mathf.Clamp01(((u * u) + (v * v)) * 0.75f));

                    // A little noise for the weave. Fine enough to read as texture
                    // rather than as dirt.
                    var grain = 1f + (((float)random.NextDouble() - 0.5f) * 0.075f);

                    var shade = vignette * grain;

                    pixels[(y * w) + x] = new Color32(
                        (byte)Mathf.Clamp(Felt.r * 255f * shade, 0f, 255f),
                        (byte)Mathf.Clamp(Felt.g * 255f * shade, 0f, 255f),
                        (byte)Mathf.Clamp(Felt.b * 255f * shade, 0f, 255f),
                        255);
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply();

            return _felt = Sprite.Create(texture, new Rect(0f, 0f, w, h), new Vector2(0.5f, 0.5f), 100f);
        }

        /// <summary>A spot was clicked. Routed through here so the spots stay dumb.</summary>
        internal static void Bet(string kind, int selection) => _onBet?.Invoke(kind, selection);

        /// <summary>A spot was right-clicked -- take a chip back off it.</summary>
        internal static void Lift(string kind, int selection) => _onLift?.Invoke(kind, selection);

        private static string Key(string kind, int selection) =>
            kind.ToLowerInvariant() + ":" + selection;

        // ---------------------------------------------------------------- the grid

        /// <summary>
        /// Where a number sits on the printed grid.
        ///
        /// The grid runs up the columns, so 1, 2, 3 share the first column with 3 at the
        /// top. Column is (n-1)/3 and the row is counted from the top, which is what
        /// turns a street -- three consecutive numbers -- into one printed column.
        /// </summary>
        private static Vector2 Place(int number, float left, float top)
        {
            var column = (number - 1) / 3;
            var row = 2 - ((number - 1) % 3);

            return new Vector2(
                left + (column * Cell) + (Cell * 0.5f),
                top - (row * Cell) - (Cell * 0.5f));
        }

        private static void BuildZero(RectTransform root, float left, float top)
        {
            var cell = NewCell(root, "0", Green, Cell, Rows * Cell);
            cell.anchoredPosition = new Vector2(left - (Cell * 0.5f), top - (Rows * Cell * 0.5f));
            Wire(cell, "Straight", 0);
        }

        private static void BuildNumbers(RectTransform root, float left, float top)
        {
            for (var n = 1; n <= 36; n++)
            {
                var red = IsRed(n);
                var cell = NewCell(root, n.ToString(), red ? Red : Black, Cell, Cell);
                cell.anchoredPosition = Place(n, left, top);
                Wire(cell, "Straight", n);
            }
        }

        /// <summary>
        /// The three "2 to 1" boxes down the right-hand side. They are the *rows* of the
        /// printed grid, which the rules call columns -- column 1 is 1, 4, 7 and so on,
        /// which prints along the bottom.
        /// </summary>
        private static void BuildColumnBets(RectTransform root, float left, float top)
        {
            for (var row = 0; row < 3; row++)
            {
                var column = 3 - row;
                var cell = NewCell(root, "2 to 1", Color.clear, Cell, Cell);
                cell.anchoredPosition = new Vector2(
                    left + (Columns * Cell) + (Cell * 0.5f),
                    top - (row * Cell) - (Cell * 0.5f));

                Wire(cell, "Column", column);
            }
        }

        private static void BuildDozens(RectTransform root, float left, float top)
        {
            var labels = new[] { "1st 12", "2nd 12", "3rd 12" };
            var width = 4f * Cell;

            for (var d = 0; d < 3; d++)
            {
                var cell = NewCell(root, labels[d], Color.clear, width, OutsideRow);
                cell.anchoredPosition = new Vector2(
                    left + (d * width) + (width * 0.5f),
                    top - (Rows * Cell) - (OutsideRow * 0.5f));

                Wire(cell, "Dozen", d + 1);
            }
        }

        private static void BuildOutside(RectTransform root, float left, float top)
        {
            var bets = new (string Label, string Kind, Color Tint)[]
            {
                ("1-18", "Low", Color.clear),
                ("EVEN", "Even", Color.clear),
                ("RED", "Red", Red),
                ("BLACK", "Black", Black),
                ("ODD", "Odd", Color.clear),
                ("19-36", "High", Color.clear),
            };

            var width = 2f * Cell;

            for (var i = 0; i < bets.Length; i++)
            {
                var cell = NewCell(root, bets[i].Label, bets[i].Tint, width, OutsideRow);
                cell.anchoredPosition = new Vector2(
                    left + (i * width) + (width * 0.5f),
                    top - (Rows * Cell) - OutsideRow - (OutsideRow * 0.5f));

                Wire(cell, bets[i].Kind, 0);
            }
        }

        /// <summary>
        /// Splits, streets, corners and six lines, as targets on the joins.
        ///
        /// Built from the engine's own enumeration rather than from a fresh reading of
        /// the grid, so what the cloth offers and what the server accepts are the same
        /// list. A split's selection is its index in that list.
        /// </summary>
        private static void BuildLineBets(RectTransform root, ClothLayout layout, float left, float top)
        {
            // Splits: on the join between the two numbers, which is their midpoint.
            for (var i = 0; i < layout.Splits.Count; i++)
            {
                var pair = layout.Splits[i];

                var a = pair.Low == 0
                    ? new Vector2(left - (Cell * 0.5f), Place(pair.High, left, top).y)
                    : Place(pair.Low, left, top);

                var b = Place(pair.High, left, top);

                AddSpot(root, (a + b) * 0.5f, "Split", i);
            }

            // Streets: off the bottom edge of each printed column.
            foreach (var street in layout.Streets)
            {
                var p = Place(street, left, top);
                AddSpot(root, new Vector2(p.x, p.y - (Cell * 0.5f)), "Street", street);
            }

            // Corners: the point where four cells meet, which is the corner of the
            // lowest of them.
            foreach (var corner in layout.Corners)
            {
                var p = Place(corner, left, top);
                AddSpot(root, new Vector2(p.x + (Cell * 0.5f), p.y + (Cell * 0.5f)), "Corner", corner);
            }

            // Six lines: on the bottom edge, between two columns.
            foreach (var line in layout.SixLines)
            {
                var p = Place(line, left, top);
                AddSpot(root, new Vector2(p.x + (Cell * 0.5f), p.y - (Cell * 0.5f)), "SixLine", line);
            }
        }

        private static void AddSpot(RectTransform root, Vector2 at, string kind, int selection)
        {
            var spot = NewBox("Spot_" + kind + selection, root, Spot);
            spot.sizeDelta = new Vector2(SpotSize, SpotSize);
            spot.anchoredPosition = at;

            var image = spot.GetComponent<Image>();
            image.sprite = Textures.RoundedBox(
                (int)(SpotSize * 0.5f), Spot, new Color(0.85f, 0.78f, 0.55f, 0.5f), 1);
            image.type = Image.Type.Sliced;
            image.raycastTarget = true;

            Wire(spot, kind, selection);
        }

        // ---------------------------------------------------------------- the pieces

        private static RectTransform NewCell(
            RectTransform root, string label, Color tint, float width, float height)
        {
            var cell = NewBox("Cell_" + label, root, tint == Color.clear ? new Color(0f, 0f, 0f, 0f) : tint);
            cell.sizeDelta = new Vector2(width - 3f, height - 3f);

            var image = cell.GetComponent<Image>();
            image.sprite = Textures.RoundedBox(
                4, tint == Color.clear ? new Color(1f, 1f, 1f, 0.04f) : tint, FeltEdge, 2);
            image.type = Image.Type.Sliced;
            image.color = Color.white;
            image.raycastTarget = true;

            var text = NewText(cell, label, height > Cell ? 22f : (width > Cell * 1.5f ? 18f : 21f));
            Stretch(text.rectTransform);
            _pendingLabel = text;

            return cell;
        }

        /// <summary>
        /// Makes a cell take a chip, and gives it somewhere to show what is on it.
        ///
        /// The chip holder is a child of the cell rather than a separate layer, so a
        /// stack cannot drift away from the spot it belongs to.
        /// </summary>
        private static void Wire(RectTransform cell, string kind, int selection)
        {
            // Not a Button: a Button only knows about the left mouse button, and the
            // right one is how a chip comes back off.
            var spot = cell.gameObject.AddComponent<ClothSpot>();
            spot.Kind = kind;
            spot.Selection = selection;

            // As big as the box allows, up to a chip on a number. The line-bet targets
            // are 19-unit dots and are the exception: a chip straddling a join is the
            // same chip as one on a number, so it is sized as one rather than shrunk to
            // its target.
            // Anything much smaller than a cell is a line-bet dot rather than a box.
            var box = cell.sizeDelta;
            var chipSize = box.x < Cell * 0.5f
                ? ChipOnNumber
                : Mathf.Min(ChipOnNumber, box.x - ChipInset, box.y - ChipInset);

            // A plain centred square, deliberately with no layout group on it. The
            // first version used a horizontal group the width of the whole cell, which
            // is what pushed every pile off its spot and let the figure overflow into
            // the neighbouring square.
            var stack = NewBox("Chips", cell, new Color(0f, 0f, 0f, 0f));
            stack.sizeDelta = new Vector2(chipSize, chipSize);
            stack.GetComponent<Image>().raycastTarget = false;

            var key = Key(kind, selection);
            Stacks[key] = stack;
            ChipSizes[key] = chipSize;

            // Only the numbers get hidden. A chip on "1st 12" sits in a box far wider
            // than itself, and blanking that label would leave an unlabelled box.
            if (_pendingLabel != null && string.Equals(kind, "Straight", StringComparison.OrdinalIgnoreCase))
            {
                Labels[key] = _pendingLabel;
            }

            _pendingLabel = null;
        }

        private static bool IsRed(int n) =>
            n == 1 || n == 3 || n == 5 || n == 7 || n == 9 || n == 12 || n == 14 || n == 16
            || n == 18 || n == 19 || n == 21 || n == 23 || n == 25 || n == 27 || n == 30
            || n == 32 || n == 34 || n == 36;

        private static RectTransform NewBox(string name, Transform parent, Color colour)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.transform.SetParent(parent, false);

            var rect = (RectTransform)go.transform;
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);

            go.GetComponent<Image>().color = colour;

            return rect;
        }

        private static TextMeshProUGUI NewText(Transform parent, string text, float size)
        {
            var go = new GameObject("Label", typeof(RectTransform));
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

            return label;
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }
    }

    /// <summary>
    /// The cloth's spots, as the server described them.
    ///
    /// Read off the view rather than worked out here. A split is placed by its index in
    /// <see cref="Splits"/>, so a client that enumerated its own would be sending
    /// indices into a list nobody else has.
    /// </summary>
    internal sealed class ClothLayout
    {
        internal ClothLayout(
            IReadOnlyList<(int Low, int High)> splits,
            IReadOnlyList<int> streets,
            IReadOnlyList<int> corners,
            IReadOnlyList<int> sixLines)
        {
            Splits = splits;
            Streets = streets;
            Corners = corners;
            SixLines = sixLines;
        }

        internal IReadOnlyList<(int Low, int High)> Splits { get; }

        internal IReadOnlyList<int> Streets { get; }

        internal IReadOnlyList<int> Corners { get; }

        internal IReadOnlyList<int> SixLines { get; }
    }

    /// <summary>
    /// A betting spot. Left click puts a chip on, right click takes one off.
    ///
    /// A plain <c>Button</c> cannot do this -- it raises <c>onClick</c> for the left
    /// button only and never sees the right one at all.
    /// </summary>
    internal sealed class ClothSpot : MonoBehaviour, IPointerClickHandler
    {
        internal string Kind { get; set; }

        internal int Selection { get; set; }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (eventData == null)
            {
                return;
            }

            if (eventData.button == PointerEventData.InputButton.Right)
            {
                ClothView.Lift(Kind, Selection);
                return;
            }

            if (eventData.button == PointerEventData.InputButton.Left)
            {
                ClothView.Bet(Kind, Selection);
            }
        }
    }
}
