using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Poker.Client
{
    /// <summary>
    /// Draws an amount as a stack of chips.
    ///
    /// The six denominations are the ones printed on the artwork, so the picture and
    /// the arithmetic cannot drift apart: a chip's value is read from the same table
    /// that names its file. Adding a denomination means adding an image and one row
    /// here.
    ///
    /// Sprites come from disk beside the DLL through <see cref="Textures.FromFile"/>,
    /// which caches, so a redraw costs nothing after the first.
    /// </summary>
    internal static class ChipView
    {
        /// <summary>One denomination: what it is worth and what it is called on the art.</summary>
        internal struct Chip
        {
            public readonly int Value;
            public readonly string File;

            public Chip(int value, string file)
            {
                Value = value;
                File = file;
            }
        }

        /// <summary>
        /// Highest first, which is the order a greedy breakdown needs and the order a
        /// real rack is stacked in.
        /// </summary>
        internal static readonly Chip[] Denominations =
        {
            new Chip(1_000_000, "1M"),
            new Chip(500_000, "500k"),
            new Chip(100_000, "100k"),
            new Chip(50_000, "50k"),
            new Chip(25_000, "25k"),
            new Chip(10_000, "10k"),
        };

        internal static int Smallest => Denominations[Denominations.Length - 1].Value;

        private static string _directory;

        /// <summary>The largest amount every denomination divides into: 5,000.</summary>
        private const int Unit = 5_000;

        /// <summary>
        /// Beyond this many units the exact search is skipped and the biggest chips
        /// are peeled off first. 100,000 units is 500,000,000 -- far past any pot a
        /// table with these stakes can hold, and the array behind it is small.
        /// </summary>
        private const int MaxUnits = 100_000;

        /// <summary>
        /// Breaks an amount into the fewest chips that make it exactly.
        ///
        /// **Greedy is wrong for this set.** It is tempting, and it is what this
        /// first did, but 10,000 does not divide 25,000 -- so a pot of 30,000, which
        /// is three 10k chips, comes out of a greedy pass as one 25k chip and 5,000
        /// stranded. The pre-flop pot at these blinds is exactly 30,000, so the very
        /// first thing anyone sees would have been wrong.
        ///
        /// The denominations share a highest common factor of 5,000, so the search
        /// runs in units of that: a few hundred entries for any pot this table can
        /// hold. Anything not representable -- an amount that is not a whole number
        /// of 5,000, or a leftover under the smallest chip -- comes back as the
        /// remainder rather than being rounded away. A table that quietly loses the
        /// odd thousand is the sort of drift nobody notices until the numbers are far
        /// apart.
        /// </summary>
        internal static List<KeyValuePair<Chip, int>> Breakdown(int amount, out int remainder)
        {
            var stack = new List<KeyValuePair<Chip, int>>();
            var left = amount < 0 ? 0 : amount;

            // Peel the top chip off anything enormous so the search below stays small.
            // Safe to do greedily: every denomination divides 1,000,000.
            var biggest = Denominations[0];
            while (left / Unit > MaxUnits)
            {
                var count = (left - (MaxUnits * Unit)) / biggest.Value;
                if (count <= 0)
                {
                    break;
                }

                stack.Add(new KeyValuePair<Chip, int>(biggest, count));
                left -= count * biggest.Value;
            }

            var units = left / Unit;
            remainder = left - (units * Unit);

            // Fewest chips for each reachable total, and which chip was taken to get
            // there, so the counts can be walked back out afterwards.
            var best = new int[units + 1];
            var took = new int[units + 1];

            for (var u = 1; u <= units; u++)
            {
                best[u] = int.MaxValue;
                took[u] = -1;

                for (var d = 0; d < Denominations.Length; d++)
                {
                    var cost = Denominations[d].Value / Unit;
                    if (cost > u || best[u - cost] == int.MaxValue)
                    {
                        continue;
                    }

                    if (best[u - cost] + 1 < best[u])
                    {
                        best[u] = best[u - cost] + 1;
                        took[u] = d;
                    }
                }
            }

            // Nothing makes this total exactly -- 5,000 on its own, say. Take what can
            // be made and report the rest.
            var reachable = units;
            while (reachable > 0 && took[reachable] < 0)
            {
                reachable--;
            }

            remainder += (units - reachable) * Unit;

            var counts = new int[Denominations.Length];
            while (reachable > 0)
            {
                var d = took[reachable];
                counts[d]++;
                reachable -= Denominations[d].Value / Unit;
            }

            for (var d = 0; d < Denominations.Length; d++)
            {
                if (counts[d] > 0)
                {
                    stack.Add(new KeyValuePair<Chip, int>(Denominations[d], counts[d]));
                }
            }

            return stack;
        }

        /// <summary>
        /// Draws the amount as chips with the number beside them.
        ///
        /// The number is always shown. Chips read at a glance and the exact figure
        /// does not, and an amount smaller than the smallest chip has no chips to
        /// draw at all -- so the text is the truth and the chips are the emphasis.
        /// </summary>
        /// <param name="maxChips">
        /// How many chip faces to draw before giving up and letting the number carry
        /// it. A pot worth forty chips is a wall of artwork, not information.
        /// </param>
        internal static GameObject Build(
            Transform parent,
            int amount,
            TMP_FontAsset font,
            float size = 44f,
            int maxChips = 6)
        {
            // A column: the chips, and the number underneath them. Side by side, a
            // stack of overlapping discs and a five-figure number fight for the same
            // horizontal space and the eye has to work out which belongs to which.
            // Stacked, the number reads as a caption on the pile it describes.
            var go = new GameObject("Chips", typeof(RectTransform));
            go.transform.SetParent(parent, false);

            var column = go.AddComponent<VerticalLayoutGroup>();
            column.spacing = size * 0.12f;
            column.childAlignment = TextAnchor.UpperCenter;
            column.childForceExpandWidth = false;
            column.childForceExpandHeight = false;
            column.childControlWidth = false;
            column.childControlHeight = false;

            var stack = new GameObject("Stack", typeof(RectTransform));
            stack.transform.SetParent(go.transform, false);

            var row = stack.AddComponent<HorizontalLayoutGroup>();
            row.spacing = -size * 0.42f;
            row.childAlignment = TextAnchor.MiddleCenter;
            row.childForceExpandWidth = false;
            row.childForceExpandHeight = false;
            row.childControlWidth = false;
            row.childControlHeight = false;

            var drawn = 0;
            int remainder;

            foreach (var entry in Breakdown(amount, out remainder))
            {
                var sprite = Sprite(entry.Key);
                if (sprite == null)
                {
                    continue;
                }

                for (var i = 0; i < entry.Value && drawn < maxChips; i++, drawn++)
                {
                    var chip = new GameObject(
                        "Chip_" + entry.Key.File,
                        typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));

                    chip.transform.SetParent(stack.transform, false);
                    ((RectTransform)chip.transform).sizeDelta = new Vector2(size, size);

                    var image = chip.GetComponent<Image>();
                    image.sprite = sprite;
                    image.preserveAspect = true;
                    image.raycastTarget = false;
                }

                if (drawn >= maxChips)
                {
                    break;
                }
            }

            var label = new GameObject("Amount", typeof(RectTransform));
            label.transform.SetParent(go.transform, false);

            var text = label.AddComponent<TextMeshProUGUI>();
            text.text = amount.ToString("N0");
            text.fontSize = size * 0.52f;
            text.alignment = TextAlignmentOptions.Center;
            text.color = new Color(0.92f, 0.89f, 0.80f, 1f);
            text.raycastTarget = false;

            if (font != null)
            {
                text.font = font;
            }

            // Sizes are computed rather than left to a ContentSizeFitter. The parent
            // is itself a layout group with childControl off, so it places these by
            // their own rects -- a fitter that resolves a frame later would leave the
            // pot jumping on the first draw.
            var labelHeight = size * 0.66f;
            var stackWidth = StackWidth(drawn, size);

            ((RectTransform)stack.transform).sizeDelta = new Vector2(stackWidth, size);
            ((RectTransform)label.transform).sizeDelta = new Vector2(Mathf.Max(stackWidth, size * 4.4f), labelHeight);

            ((RectTransform)go.transform).sizeDelta = new Vector2(
                Mathf.Max(stackWidth, size * 4.4f),
                size + column.spacing + labelHeight);

            return go;
        }

        /// <summary>
        /// How wide a row of overlapping chips ends up.
        ///
        /// The negative spacing means each disc after the first only advances by the
        /// part of it that shows, so the row is much narrower than the count suggests.
        /// </summary>
        private static float StackWidth(int chips, float size) =>
            chips <= 0 ? 0f : size + ((chips - 1) * size * 0.58f);

        private static Sprite Sprite(Chip chip)
        {
            if (_directory == null)
            {
                var beside = Path.GetDirectoryName(PokerClientPlugin.Instance?.Info?.Location ?? ".") ?? ".";
                _directory = Path.Combine(beside, "chips");
            }

            return Textures.FromFile(Path.Combine(_directory, chip.File + ".png"));
        }
    }
}
