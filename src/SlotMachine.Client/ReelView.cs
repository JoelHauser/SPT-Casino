using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using Casino.Shared;

namespace SlotMachine.Client
{
    /// <summary>
    /// The five reels, and the only part of this that is actually hard.
    ///
    /// ## How a real reel moves, and how this one does
    ///
    /// A physical reel is a printed band on a drum. It **snaps up to speed, holds flat
    /// out, then decelerates into its stop and thumps against the detent.** It does not
    /// slow down from the first frame, which is what the first version of this did and
    /// why it read as a wheel winding down rather than a reel being spun.
    ///
    /// So the motion here is a real velocity profile, integrated exactly:
    ///
    /// * `SpinUp` (the first tenth) -- smoothstep from a standstill to full speed.
    /// * flat out until `HoldUntil`, which is most of the spin.
    /// * a squared ease-out into the stop, which is long, and is where the tension is.
    /// * an overshoot of `Overshoot` of a cell, sprung back over `SettleSeconds`. That
    ///   last bounce is the whole difference between stopping and *landing*.
    ///
    /// ## The cells do not move. The symbols do.
    ///
    /// Nine cells sit at fixed positions behind a masked three-cell window and never
    /// move relative to each other. What travels is a **read head** over a strip: cell
    /// *i* shows `strip[i - floor(position)]`, and the whole column slides by the
    /// fractional part of the position. Advance the position by one and every symbol
    /// has moved down exactly one cell, seamlessly, forever.
    ///
    /// The version before this recycled cells -- moved the lowest to the top and gave
    /// it a new face. It looked right and it was wrong: once a cell has been moved, the
    /// array index no longer says where a cell *is*, so writing the landing symbols to
    /// indices 3, 4 and 5 scattered them up and down the belt. With fixed cells, 3, 4
    /// and 5 are the window, always, and that class of bug cannot happen.
    ///
    /// ## Stopping on the answer
    ///
    /// The server decided every stop before the first frame drew, so the total travel
    /// is rounded to a whole number of cells and the landing symbols are written into
    /// the strip **at the place the reel is going to rest on**. The reel then simply
    /// spins there. Nothing is swapped in at the last moment and nothing appears.
    ///
    /// The spin is theatre over a settled fact, which is the only honest way round:
    /// reels that chose their own stopping place would be reels the client could be
    /// made to lie with. Same arrangement as the roulette wheel.
    /// </summary>
    internal static class ReelView
    {
        /// <summary>One symbol cell, square.</summary>
        internal const float Cell = 104f;

        /// <summary>Gap between reels.</summary>
        private const float Gutter = 10f;

        /// <summary>
        /// How many cells each reel carries. Three show; the rest are the belt above and
        /// below, which is what the mask hides and what makes the window look cut into
        /// something continuous.
        /// </summary>
        private const int Cells = 9;

        /// <summary>
        /// How long a strip is while it is turning.
        ///
        /// Longer than any single spin travels, so the landing symbols written into it
        /// are not also passing the window three times on the way. Not the real 30-stop
        /// strip: what scrolls past is unreadable at speed, and reproducing the true
        /// order would cost a lookup a frame to say nothing.
        /// </summary>
        private const int StripLength = 64;

        /// <summary>Full speed, in cells per second.</summary>
        private const float PeakCellsPerSecond = 22f;

        /// <summary>
        /// The ceiling <see cref="PeakCellsPerSecond"/> was chosen under.
        ///
        /// Past about one cell per frame the symbols stop being a moving belt and become
        /// a row of separate pictures -- the same strobing that took three rounds to
        /// find on the roulette ball. 22 cells a second is 0.73 of a cell at 30fps and
        /// 0.37 at 60, so it stays a blur on a bad frame rate as well as a good one.
        /// </summary>
        private const float MaxCellsPerFrame = 0.85f;

        private const float SpinUp = 0.10f;

        private const float HoldUntil = 0.62f;

        /// <summary>How far past its stop a reel throws itself, in cells.</summary>
        private const float Overshoot = 0.16f;

        private const float SettleSeconds = 0.11f;

        private const float MinDuration = 1.05f;

        /// <summary>Each reel runs a little longer than the one before it.</summary>
        private const float Stagger = 0.34f;

        private static readonly Color Face = new Color(0.09f, 0.10f, 0.11f, 1f);
        private static readonly Color Edge = new Color(0.42f, 0.36f, 0.22f, 1f);
        private static readonly Color WinTint = new Color(1f, 0.86f, 0.45f, 1f);

        private static readonly Dictionary<string, Sprite> Faces = new Dictionary<string, Sprite>();

        private static RectTransform[] _columns;
        private static Image[][] _cells;
        private static string[][] _strips;
        private static float[] _positions;
        private static string[] _symbols;

        internal static bool Spinning { get; private set; }

        internal static float Width => (5f * Cell) + (4f * Gutter);

        internal static float Height => 3f * Cell;

        /// <summary>
        /// Builds the window and the five belts behind it.
        /// </summary>
        /// <param name="symbols">
        /// Every symbol name the machine can show, from the server. The belts are filled
        /// from this while idle, so a reel that has never spun still looks like a reel.
        /// </param>
        internal static GameObject Build(Transform parent, IReadOnlyList<string> symbols)
        {
            _symbols = symbols is { Count: > 0 } ? [.. symbols] : ["Medkit"];

            var root = NewBox("Reels", parent, Color.white);
            root.sizeDelta = new Vector2(Width + 28f, Height + 28f);

            var frame = root.GetComponent<Image>();
            frame.sprite = Textures.RoundedBox(10, Face, Edge, 3);
            frame.type = Image.Type.Sliced;

            _columns = new RectTransform[5];
            _cells = new Image[5][];
            _strips = new string[5][];
            _positions = new float[5];

            var left = -Width * 0.5f;

            for (var reel = 0; reel < 5; reel++)
            {
                // A window that clips, so the belt above and below is not drawn outside
                // the machine. Without the mask the reels are five columns of symbols
                // sliding across the whole panel.
                var window = NewBox("Window" + reel, root, new Color(0.05f, 0.05f, 0.06f, 1f));
                window.sizeDelta = new Vector2(Cell, Height);
                window.anchoredPosition = new Vector2(left + (reel * (Cell + Gutter)) + (Cell * 0.5f), 0f);
                window.gameObject.AddComponent<Mask>().showMaskGraphic = true;

                var column = NewBox("Belt" + reel, window, Color.clear);
                column.sizeDelta = new Vector2(Cell, Cells * Cell);
                column.anchoredPosition = Vector2.zero;

                _columns[reel] = column;
                _cells[reel] = new Image[Cells];
                _strips[reel] = NewStrip(new System.Random(reel * 7919));

                for (var i = 0; i < Cells; i++)
                {
                    // Set once and never moved again. See the class comment.
                    var cell = NewBox("Cell" + i, column, Color.white);
                    cell.sizeDelta = new Vector2(Cell - 6f, Cell - 6f);
                    cell.anchoredPosition = new Vector2(0f, RestingY(i));

                    var image = cell.GetComponent<Image>();
                    image.preserveAspect = true;
                    image.raycastTarget = false;

                    _cells[reel][i] = image;
                }

                Render(reel);
            }

            return root.gameObject;
        }

        /// <summary>
        /// Replaces what the belts show while idle.
        ///
        /// For a machine built before the server answered: the reels fall back to a
        /// single symbol so they are not empty, and this puts the real set in once it
        /// arrives rather than leaving a column of medkits spinning forever.
        /// </summary>
        internal static void Restock(IReadOnlyList<string> symbols)
        {
            if (symbols is not { Count: > 0 } || _strips == null)
            {
                return;
            }

            _symbols = [.. symbols];

            for (var reel = 0; reel < 5; reel++)
            {
                _strips[reel] = NewStrip(new System.Random(reel * 7919));
                Render(reel);
            }
        }

        /// <summary>
        /// Shows a settled grid without spinning to it. Used when the panel opens.
        /// </summary>
        internal static void Show(IReadOnlyList<IReadOnlyList<string>> grid)
        {
            if (_cells == null || grid == null)
            {
                return;
            }

            for (var reel = 0; reel < 5 && reel < grid.Count; reel++)
            {
                _positions[reel] = 0f;
                WriteLanding(reel, 0, grid[reel]);
                Render(reel);
            }
        }

        /// <summary>
        /// Spins, and lands on the grid the server already settled.
        /// </summary>
        internal static IEnumerator Spin(
            MonoBehaviour host, IReadOnlyList<IReadOnlyList<string>> grid, Action onStopped)
        {
            if (_cells == null || grid == null)
            {
                onStopped?.Invoke();
                yield break;
            }

            Spinning = true;

            for (var reel = 0; reel < 5; reel++)
            {
                for (var i = 0; i < Cells; i++)
                {
                    _cells[reel][i].color = Color.white;
                }
            }

            var running = 0;

            for (var reel = 0; reel < 5 && reel < grid.Count; reel++)
            {
                running++;
                host.StartCoroutine(
                    SpinOne(reel, MinDuration + (reel * Stagger), grid[reel], () => running--));
            }

            while (running > 0)
            {
                yield return null;
            }

            Spinning = false;
            onStopped?.Invoke();
        }

        /// <summary>
        /// One reel: up to speed, flat out, ease down, thump.
        ///
        /// The travel is a whole number of cells and the landing symbols are written to
        /// where that leaves the window, so the reel is spinning towards them from the
        /// first frame rather than having them dropped in at the end.
        /// </summary>
        private static IEnumerator SpinOne(
            int reel, float duration, IReadOnlyList<string> landing, Action done)
        {
            var random = new System.Random((reel * 7919) + Environment.TickCount);

            // A fresh belt each spin, so the same order does not scroll past five times
            // running and give the machine a pattern.
            _strips[reel] = NewStrip(random);

            var from = Mathf.Floor(_positions[reel]);

            // Rounded to whole cells: a reel that stops a third of a cell along is a
            // reel showing half of four symbols.
            var travel = Mathf.Round(PeakCellsPerSecond * duration * ProfileArea);
            var rest = (int)(from + travel);

            WriteLanding(reel, rest, landing);

            for (var elapsed = 0f; elapsed < duration; elapsed += Time.unscaledDeltaTime)
            {
                var u = Mathf.Clamp01(elapsed / duration);

                // The overshoot is carried by the same curve, so the reel arrives past
                // its stop still travelling rather than jumping there.
                _positions[reel] = from + ((travel + Overshoot) * Travelled(u) / ProfileArea);
                Render(reel);

                yield return null;
            }

            // The bounce back onto the detent.
            for (var t = 0f; t < SettleSeconds; t += Time.unscaledDeltaTime)
            {
                _positions[reel] = rest + (Overshoot * (1f - Mathf.SmoothStep(0f, 1f, t / SettleSeconds)));
                Render(reel);

                yield return null;
            }

            // Home exactly. A reel resting a pixel or two off its cell is the sort of
            // thing nobody can name but everybody sees.
            //
            // Wrapped to the strip length as well: the faces are read modulo it, so this
            // draws identically while keeping the position small. A float counting cells
            // for a whole session would eventually be coarser than the cell it is
            // measuring.
            _positions[reel] = Wrap(rest, StripLength);
            Render(reel);

            done?.Invoke();
        }

        /// <summary>
        /// How far a reel has gone at <paramref name="u"/> of its spin: the exact
        /// integral of the velocity profile described on the class.
        ///
        /// Analytic rather than accumulated per frame, so the reel lands on its stop to
        /// the pixel on a machine dropping frames as well as on one that is not.
        /// </summary>
        private static float Travelled(float u)
        {
            if (u <= SpinUp)
            {
                // The integral of smoothstep, 3x^2 - 2x^3, is x^3 - x^4/2.
                var x = u / SpinUp;
                return SpinUp * ((x * x * x) - (x * x * x * x * 0.5f));
            }

            var upArea = SpinUp * 0.5f;

            if (u <= HoldUntil)
            {
                return upArea + (u - SpinUp);
            }

            // The integral of (1-x)^2 is (1 - (1-x)^3) / 3.
            var fall = 1f - ((u - HoldUntil) / (1f - HoldUntil));

            return upArea + (HoldUntil - SpinUp) + ((1f - HoldUntil) * (1f - (fall * fall * fall)) / 3f);
        }

        /// <summary>The whole area under the profile, which is <c>Travelled(1)</c>.</summary>
        private static float ProfileArea =>
            (SpinUp * 0.5f) + (HoldUntil - SpinUp) + ((1f - HoldUntil) / 3f);

        /// <summary>
        /// Draws a reel at its current position: the column slid by the fractional part,
        /// and every cell showing the symbol the read head puts under it.
        /// </summary>
        private static void Render(int reel)
        {
            var position = _positions[reel];
            var whole = Mathf.FloorToInt(position);
            var strip = _strips[reel];

            _columns[reel].anchoredPosition = new Vector2(0f, -(position - whole) * Cell);

            for (var i = 0; i < Cells; i++)
            {
                _cells[reel][i].sprite = FaceFor(strip[Wrap(i - whole, strip.Length)]);
            }
        }

        /// <summary>
        /// Writes the three symbols the reel is going to stop on into the strip, at the
        /// place the window will be sitting over when it does.
        /// </summary>
        private static void WriteLanding(int reel, int rest, IReadOnlyList<string> landing)
        {
            var strip = _strips[reel];

            for (var row = 0; row < 3 && row < landing.Count; row++)
            {
                strip[Wrap(3 + row - rest, strip.Length)] = landing[row];
            }
        }

        /// <summary>
        /// A belt to scroll past. Weighted towards the low symbols the way the real
        /// strips are, because a belt with as many keycards on it as medkits reads as a
        /// machine about to pay out.
        /// </summary>
        private static string[] NewStrip(System.Random random)
        {
            var strip = new string[StripLength];

            for (var i = 0; i < StripLength; i++)
            {
                // Biased low: two draws, keep the earlier symbol. The paytable arrives
                // from the server ordered cheapest first, so this is roughly the shape
                // of the real strips without needing to know their counts.
                var a = random.Next(_symbols.Length);
                var b = random.Next(_symbols.Length);

                strip[i] = _symbols[Math.Min(a, b)];
            }

            return strip;
        }

        /// <summary>Lights the reels a win ran through, and dims the rest.</summary>
        internal static void Highlight(IReadOnlyList<int> reelsWon)
        {
            if (_cells == null)
            {
                return;
            }

            var lit = reelsWon is { Count: > 0 };

            for (var reel = 0; reel < 5; reel++)
            {
                var on = !lit || reelsWon.Contains(reel);

                for (var row = 0; row < 3; row++)
                {
                    _cells[reel][3 + row].color =
                        on ? (lit ? WinTint : Color.white) : new Color(1f, 1f, 1f, 0.32f);
                }
            }
        }

        /// <summary>
        /// The middle of reel <paramref name="reel"/>, in the reel root's own space.
        ///
        /// Public because the win lines are drawn over the reels by the panel, and a
        /// line that does not go through the middle of the symbol it is claiming is
        /// worse than no line.
        /// </summary>
        internal static float ReelX(int reel) =>
            (-Width * 0.5f) + (reel * (Cell + Gutter)) + (Cell * 0.5f);

        /// <summary>The middle of row <paramref name="row"/> of the window.</summary>
        internal static float RowY(int row) => (1 - row) * Cell;

        /// <summary>Where cell <paramref name="i"/> sits. It never sits anywhere else.</summary>
        private static float RestingY(int i) => (((Cells - 1) * 0.5f) - i) * Cell;

        private static int Wrap(int value, int length) => ((value % length) + length) % length;

        /// <summary>
        /// A symbol's artwork, for anyone else who needs to draw one -- the paytable
        /// down the side of the panel is the only caller.
        /// </summary>
        internal static Sprite Artwork(string symbol) => FaceFor(symbol);

        /// <summary>
        /// A symbol's artwork, cached.
        ///
        /// A missing file falls back to a drawn box rather than an empty cell: a reel
        /// with holes in it looks broken, where a plain tile looks like a symbol nobody
        /// has drawn yet.
        /// </summary>
        private static Sprite FaceFor(string symbol)
        {
            var key = symbol ?? string.Empty;

            if (Faces.TryGetValue(key, out var cached))
            {
                return cached;
            }

            var path = Path.Combine(Host.AssetFolder, "symbols", key.ToLowerInvariant() + ".png");
            var sprite = Textures.FromFile(path)
                ?? Textures.RoundedBox(8, new Color(0.20f, 0.21f, 0.23f, 1f), Edge, 2);

            Faces[key] = sprite;
            return sprite;
        }

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
    }
}
