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
    /// ## A reel is a strip, not a slideshow
    ///
    /// The naive version swaps three sprites a few times and stops. It reads as
    /// flickering rather than spinning, because nothing ever moves. This builds a
    /// column of symbol cells taller than the window it shows through, slides the whole
    /// column, and recycles cells off the bottom back to the top -- so what the eye
    /// follows is one continuous belt.
    ///
    /// ## Stopping on the answer
    ///
    /// The server has already decided where every reel lands before the first frame is
    /// drawn. The spin is theatre over a settled fact, which is the only honest way
    /// round: reels that chose their own stopping place would be reels the client could
    /// be made to lie with.
    ///
    /// Each reel runs for its own duration, so they come to rest left to right. That
    /// stagger is most of what makes a slot feel like a slot -- five reels stopping
    /// together reads as a picture appearing rather than as anything spinning.
    /// </summary>
    internal static class ReelView
    {
        /// <summary>One symbol cell, square.</summary>
        internal const float Cell = 104f;

        /// <summary>Gap between reels.</summary>
        private const float Gutter = 10f;

        /// <summary>
        /// How many cells each reel carries.
        ///
        /// Three show; the rest are the belt above and below. Enough that the column
        /// can slide a whole cell height several times before recycling, which is what
        /// keeps the motion continuous rather than jumping.
        /// </summary>
        private const int Cells = 9;

        private const float MinDuration = 0.9f;

        /// <summary>Each reel runs a little longer than the one before it.</summary>
        private const float Stagger = 0.42f;

        /// <summary>
        /// How far a reel may travel in one frame, as a fraction of a cell.
        ///
        /// Past about one cell per frame the symbols stop being a moving belt and
        /// become a row of separate pictures -- the same strobing that took three
        /// rounds to find on the roulette ball. Capping the speed is what keeps it a
        /// blur instead.
        /// </summary>
        private const float MaxCellsPerFrame = 0.85f;

        private static readonly Color Face = new Color(0.09f, 0.10f, 0.11f, 1f);
        private static readonly Color Edge = new Color(0.42f, 0.36f, 0.22f, 1f);
        private static readonly Color WinTint = new Color(1f, 0.86f, 0.45f, 1f);

        private static readonly Dictionary<string, Sprite> Faces = new Dictionary<string, Sprite>();

        private static RectTransform[] _columns;
        private static Image[][] _cells;
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
            _symbols = symbols is { Count: > 0 } ? [.. symbols] : ["Bandage"];

            var root = NewBox("Reels", parent, Color.white);
            root.sizeDelta = new Vector2(Width + 28f, Height + 28f);

            var frame = root.GetComponent<Image>();
            frame.sprite = Textures.RoundedBox(10, Face, Edge, 3);
            frame.type = Image.Type.Sliced;

            _columns = new RectTransform[5];
            _cells = new Image[5][];

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

                for (var i = 0; i < Cells; i++)
                {
                    var cell = NewBox("Cell" + i, column, Color.white);
                    cell.sizeDelta = new Vector2(Cell - 6f, Cell - 6f);
                    cell.anchoredPosition = new Vector2(0f, TopOf(i));

                    var image = cell.GetComponent<Image>();
                    image.preserveAspect = true;
                    image.raycastTarget = false;
                    image.sprite = FaceFor(_symbols[(reel + i) % _symbols.Length]);

                    _cells[reel][i] = image;
                }
            }

            return root.gameObject;
        }

        /// <summary>
        /// Replaces what the belts show while idle.
        ///
        /// For a machine built before the server answered: the reels fall back to a
        /// single symbol so they are not empty, and this puts the real set in once it
        /// arrives rather than leaving a column of bandages spinning forever.
        /// </summary>
        internal static void Restock(IReadOnlyList<string> symbols)
        {
            if (symbols is { Count: > 0 })
            {
                _symbols = [.. symbols];
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
                _columns[reel].anchoredPosition = Vector2.zero;

                for (var row = 0; row < 3 && row < grid[reel].Count; row++)
                {
                    // Cells 3, 4 and 5 are the three in the window when the belt sits
                    // at rest. See TopOf.
                    _cells[reel][3 + row].sprite = FaceFor(grid[reel][row]);
                    _cells[reel][3 + row].color = Color.white;
                }
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

            var running = 5;

            for (var reel = 0; reel < 5; reel++)
            {
                host.StartCoroutine(SpinOne(reel, MinDuration + (reel * Stagger), grid[reel], () => running--));
            }

            while (running > 0)
            {
                yield return null;
            }

            Spinning = false;
            onStopped?.Invoke();
        }

        /// <summary>
        /// One reel: run, slow, and drop the answer into the window as it settles.
        ///
        /// The symbols scrolling past are picked at random because nobody can read them
        /// at speed and pretending otherwise costs a strip lookup per frame. The three
        /// that matter are written in when the belt is within a cell of home, which is
        /// late enough that they arrive already moving rather than appearing.
        /// </summary>
        private static IEnumerator SpinOne(int reel, float duration, IReadOnlyList<string> landing, Action done)
        {
            var column = _columns[reel];
            var random = new System.Random(reel * 7919 + Environment.TickCount);
            var elapsed = 0f;
            var offset = 0f;
            var placed = false;

            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;

                // Fast, then easing off. The last quarter is where a slot earns its
                // tension, so the curve is deliberately slow to let go.
                var t = Mathf.Clamp01(elapsed / duration);
                var speed = Mathf.Lerp(1f, 0.06f, Mathf.SmoothStep(0f, 1f, t));
                var step = Mathf.Min(speed * Cell * 34f * Time.unscaledDeltaTime, Cell * MaxCellsPerFrame);

                offset += step;

                while (offset >= Cell)
                {
                    offset -= Cell;
                    Recycle(reel, random);
                }

                if (!placed && t > 0.86f)
                {
                    placed = true;

                    for (var row = 0; row < 3 && row < landing.Count; row++)
                    {
                        _cells[reel][3 + row].sprite = FaceFor(landing[row]);
                    }
                }

                column.anchoredPosition = new Vector2(0f, offset);
                yield return null;
            }

            // Home exactly. A reel resting a pixel or two off its cell is the sort of
            // thing nobody can name but everybody sees.
            column.anchoredPosition = Vector2.zero;

            for (var row = 0; row < 3 && row < landing.Count; row++)
            {
                _cells[reel][3 + row].sprite = FaceFor(landing[row]);
            }

            done?.Invoke();
        }

        /// <summary>
        /// Moves the bottom cell to the top and gives it a new face, which is what makes
        /// a finite column behave like an endless belt.
        /// </summary>
        private static void Recycle(int reel, System.Random random)
        {
            var cells = _cells[reel];
            var lowest = 0;

            for (var i = 1; i < cells.Length; i++)
            {
                if (cells[i].rectTransform.anchoredPosition.y < cells[lowest].rectTransform.anchoredPosition.y)
                {
                    lowest = i;
                }
            }

            var highest = 0;

            for (var i = 1; i < cells.Length; i++)
            {
                if (cells[i].rectTransform.anchoredPosition.y > cells[highest].rectTransform.anchoredPosition.y)
                {
                    highest = i;
                }
            }

            cells[lowest].rectTransform.anchoredPosition =
                new Vector2(0f, cells[highest].rectTransform.anchoredPosition.y + Cell);

            cells[lowest].sprite = FaceFor(_symbols[random.Next(_symbols.Length)]);
        }

        /// <summary>Lights the three rows a win ran through, and dims the rest.</summary>
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
                    _cells[reel][3 + row].color = on ? (lit ? WinTint : Color.white) : new Color(1f, 1f, 1f, 0.32f);
                }
            }
        }

        /// <summary>Where cell <paramref name="i"/> sits when the belt is at rest.</summary>
        private static float TopOf(int i) => ((Cells - 1) * 0.5f - i) * Cell;

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
