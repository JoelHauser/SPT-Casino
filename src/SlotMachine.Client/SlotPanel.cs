using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Casino.Shared;
using Newtonsoft.Json.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SlotMachine.Client
{
    /// <summary>
    /// The machine.
    ///
    /// The server settles the pull before this has drawn a frame, so the reels are
    /// animating towards an answer that already exists. That is the only honest way
    /// round -- reels that decided where to stop would be reels the client could be made
    /// to lie with -- and it is the same arrangement the roulette wheel uses.
    ///
    /// **The stash is not told the money moved until the reels stop.** The server has
    /// already taken the stake and paid the win by the time the first frame draws, so
    /// asking the game to notice straight away would show the result in the rouble
    /// counter behind the machine while the reels were still turning. Roulette learned
    /// that with its wheel.
    ///
    /// ## Everything lives inside one frame
    ///
    /// Deliberately, and not only for tidiness. The first layout scattered pieces across
    /// a full-screen canvas at hand-picked coordinates, and when `Build` threw partway
    /// down -- it did, on a null from `AddComponent&lt;Image&gt;` for the old lever --
    /// what was left looked like a finished panel with a few things missing rather than
    /// like a crash. One frame, laid out from its own edges, makes a partial build
    /// obvious instead of plausible.
    /// </summary>
    internal static class SlotPanel
    {
        private const string RootName = "SlotMachineCanvas";

        private const float FrameWidth = 1240f;
        private const float FrameHeight = 700f;
        private const float PayWidth = 380f;
        private const float PayRow = 42f;

        /// <summary>Breathing room inside the paytable's own frame.</summary>
        private const float PayInset = 16f;

        private const float PayGap = 28f;
        private const float SpinGap = 26f;
        private const float SpinSize = 164f;

        /// <summary>
        /// The paytable, the reels and the spin button, side by side, centred in the
        /// frame.
        ///
        /// Measured rather than nudged. The first version placed each piece at a
        /// hand-picked offset from the left edge, which is how the paytable's names
        /// ended up starting five units to the LEFT of the icons they were labelling.
        /// </summary>
        private static float ContentWidth =>
            PayWidth + PayGap + (ReelView.Width + 28f) + SpinGap + SpinSize;

        private static float ContentLeft => -ContentWidth * 0.5f;

        /// <summary>
        /// How many winning ways get a line drawn through them.
        ///
        /// A five-reel win on a symbol showing twice on three of them is eight ways and
        /// eight lines, and a big one runs to dozens. Past about a dozen the machine is
        /// a ball of string and the player learns less rather than more, so the rest are
        /// counted in words instead.
        /// </summary>
        private const int MaxLines = 12;

        private static readonly Color Gold = new Color(0.85f, 0.72f, 0.38f, 1f);
        private static readonly Color Ink = new Color(0.93f, 0.91f, 0.86f, 1f);
        private static readonly Color Dim = new Color(0.60f, 0.58f, 0.54f, 1f);
        private static readonly Color Cabinet = new Color(0.13f, 0.13f, 0.15f, 1f);
        private static readonly Color Edge = new Color(0.42f, 0.36f, 0.22f, 1f);
        private static readonly Color ButtonFace = new Color(0.17f, 0.17f, 0.19f, 1f);
        private static readonly Color SpinRed = new Color(0.62f, 0.14f, 0.14f, 1f);
        private static readonly Color SpinDead = new Color(0.28f, 0.16f, 0.16f, 1f);

        /// <summary>
        /// How thick a win line is. Thin, deliberately: it is drawn over photographs of
        /// items, and the frames around the winning symbols carry the meaning. A fat
        /// line over the artwork is a line the player has to look past.
        /// </summary>
        private const float LineWidth = 3f;

        /// <summary>
        /// The band inside a symbol that the lines are spread across.
        ///
        /// Every line of a win runs through the same cells, so without this they sit on
        /// top of each other and a win on eight ways looks like a win on one. They are
        /// laid out evenly across this band instead, the way a payline machine spaces
        /// its lines -- parallel where they share a row, and separating where they do
        /// not.
        /// </summary>
        private const float LineBand = 64f;

        /// <summary>
        /// The most two neighbouring lines are allowed to be apart.
        ///
        /// Without a cap, two lines would take the whole band and run along the top and
        /// bottom edges of the symbols rather than through them. This keeps a small win
        /// looking like it goes through the middle.
        /// </summary>
        private const float MaxLineGap = 17f;

        /// <summary>
        /// Above this many lines the numbered badges are dropped.
        ///
        /// They are 22 units tall and the lines can be six apart, so past a handful they
        /// stack into an unreadable pile. The numbering is a convenience, not a fact
        /// about the game -- a way has no name the way a payline does.
        /// </summary>
        private const int MaxBadges = 8;

        /// <summary>
        /// One colour per drawn way. Chosen to stay apart on a dark cabinet -- the whole
        /// point of a line is telling it from the line beside it.
        /// </summary>
        private static readonly Color[] LineColours =
        [
            new Color(1.00f, 0.85f, 0.30f, 1f),
            new Color(0.35f, 0.85f, 1.00f, 1f),
            new Color(1.00f, 0.45f, 0.45f, 1f),
            new Color(0.55f, 1.00f, 0.55f, 1f),
            new Color(1.00f, 0.60f, 0.20f, 1f),
            new Color(0.75f, 0.60f, 1.00f, 1f),
            new Color(0.40f, 1.00f, 0.85f, 1f),
            new Color(1.00f, 0.55f, 0.85f, 1f),
            new Color(0.85f, 0.95f, 0.45f, 1f),
            new Color(0.50f, 0.70f, 1.00f, 1f),
            new Color(1.00f, 0.75f, 0.55f, 1f),
            new Color(0.70f, 0.90f, 0.75f, 1f),
        ];

        private static GameObject _root;
        private static CanvasGroup _group;
        private static TMP_FontAsset _font;
        private static Coroutine _fade;
        private static bool _closing;

        private static TextMeshProUGUI _status;
        private static TMP_InputField _stakeInput;
        private static TextMeshProUGUI _walletLabel;
        private static TextMeshProUGUI _paidLabel;
        private static RectTransform _lines;
        private static readonly Dictionary<string, Image> PayFaces = new Dictionary<string, Image>();
        private static Image _spinFace;
        private static TextMeshProUGUI _spinLabel;

        private static string _wallet = "Roubles";
        private static long _stake;
        private static readonly Dictionary<string, long[]> Limits = new Dictionary<string, long[]>();
        private static readonly List<string> Symbols = new List<string>();
        private static readonly Dictionary<string, int[]> Pays = new Dictionary<string, int[]>();
        private static bool _syncOwed;

        internal static bool IsOpen => _root != null && _root.activeSelf && !_closing;

        /// <summary>
        /// Whether the machine has all its symbols and can be played.
        ///
        /// It cannot spin without them. There is no stand-in art any more, so a spin
        /// before the icons land would animate blank tiles to a result nobody could
        /// read.
        /// </summary>
        private static bool Ready => ItemArt.HasAll(Symbols);

        internal static void Toggle()
        {
            if (IsOpen)
            {
                Close();
                return;
            }

            Open();
        }

        internal static void Open()
        {
            try
            {
                var ping = SlotApi.Ping();

                // The limits and the paytable come from the server, so a machine built
                // while it was not answering knows nothing about stakes. Read them on
                // any open that finds them still missing rather than only the first.
                if (ping != null && (_root == null || Limits.Count == 0))
                {
                    ReadMachine(ping);
                }

                // Before Build, not after: the coroutine that renders icons cannot run
                // until the next frame, so a cache hit read there would still mean one
                // frame of blank reels and then a swap.
                ItemArt.PrimeFromDisk(Symbols);

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
                FadeTo(1f, null);

                ClearLines();
                UseRealArt();

                // Anything not already cached, the game draws now. On a first run that
                // is nine model renders and the reels stay blank until they land.
                ItemArt.Fetch(SlotClientPlugin.Instance, Symbols, UseRealArt);

                Note(ping);

                SetStatus(ping == null
                    ? "The server is not answering. The machine will not take a spin."
                    : Ready
                        ? "Press SPIN."
                        : "Fetching the symbols from your install...");

                Refresh();
            }
            catch (Exception ex)
            {
                SlotClientPlugin.Log.LogError("[Slots] could not open the machine: " + ex);
            }
        }

        internal static void Close()
        {
            if (_root == null || !_root.activeSelf || _closing)
            {
                return;
            }

            // Walking out mid-spin. The money has moved regardless, so the debt to the
            // running game is settled on the way rather than left for a reload.
            Resync();

            _closing = true;

            FadeTo(0f, () =>
            {
                _root.SetActive(false);
                _closing = false;
            });
        }

        /// <summary>Escape, from the casino's own handler.</summary>
        internal static void OnEscape() => Close();

        // ------------------------------------------------------------------ playing

        /// <summary>
        /// Spins, then animates to what came back.
        ///
        /// A refusal stops here and says why. The reels do not move on a pull that cost
        /// nothing -- a machine that spins and then says "you cannot afford that" has
        /// already told the player it took their money.
        /// </summary>
        private static void Pull()
        {
            if (ReelView.Spinning)
            {
                return;
            }

            if (!Ready)
            {
                SetStatus("Still fetching the symbols from your install.");
                return;
            }

            var reply = SlotApi.Pull(_wallet, _stake);

            if (reply == null)
            {
                SetStatus("No answer from the server.");
                return;
            }

            Note(reply);

            var error = (string)reply["Error"];

            if (!string.IsNullOrEmpty(error))
            {
                SetStatus(error);
                return;
            }

            var pull = reply["Pull"] as JObject;

            if (pull == null)
            {
                SetStatus("The machine answered with nothing.");
                return;
            }

            // The stake is gone the moment the server replied, so the game is told to
            // catch up -- but not until the reels stop. See Resync.
            _syncOwed = true;

            var grid = ReadGrid(pull);
            var paid = (long?)pull["Paid"] ?? 0;
            var wins = pull["Wins"] as JArray;

            SetStatus("...");
            SetPaid(null);
            ClearLines();
            ReelView.Highlight(null);
            SetSpinEnabled(false);

            var host = SlotClientPlugin.Instance;

            if (host == null)
            {
                ReelView.Show(grid);
                Settled(grid, paid, wins);
                return;
            }

            host.StartCoroutine(ReelView.Spin(host, grid, () => Settled(grid, paid, wins)));
        }

        /// <summary>What happens when the reels stop.</summary>
        private static void Settled(
            IReadOnlyList<IReadOnlyList<string>> grid, long paid, JArray wins)
        {
            // Now, with the reels. Any earlier and the stash gives the answer away.
            Resync();
            SetSpinEnabled(Ready);

            if (paid > 0 && wins is { Count: > 0 })
            {
                var best = wins[0] as JObject;
                var symbol = (string)best?["Symbol"] ?? "something";
                var reels = (int?)best?["Reels"] ?? 0;
                var ways = (int?)best?["Ways"] ?? 1;

                SetPaid(paid);

                var drawn = DrawWinLines(grid, wins);
                var total = wins.Sum(w => (int?)w["Ways"] ?? 0);

                SetStatus(
                    $"{reels} x {NameOf(symbol)} on {ways} way{(ways == 1 ? string.Empty : "s")}."
                    + (wins.Count > 1 ? $"  And {wins.Count - 1} more." : string.Empty)
                    + (total > drawn ? $"  Showing {drawn} of {total} ways." : string.Empty));

                ReelView.Highlight([.. Enumerable.Range(0, reels)]);
            }
            else
            {
                SetPaid(0);
                SetStatus("Nothing. Press SPIN.");
            }

            Refresh();
        }

        /// <summary>
        /// Tells the running game its stash changed, once the result is out.
        ///
        /// Deferring the telling is not deferring the money. The stake is gone and the
        /// win is paid either way; this only decides when the game is let in on it, and
        /// doing it early puts the answer in the rouble counter before the reels stop.
        /// </summary>
        private static void Resync()
        {
            if (!_syncOwed)
            {
                return;
            }

            _syncOwed = false;
            ProfileSync.Request(SyncAction);
        }

        /// <summary>Must stay in step with `SlotActions.Sync` on the server.</summary>
        private const string SyncAction = "SlotsSync";

        // ------------------------------------------------------------ the win lines

        /// <summary>
        /// Draws a line through every way that paid, and returns how many it drew.
        ///
        /// **A 243-ways machine has no paylines**, and that is the whole difference
        /// between it and the twenty-line machines these lines are borrowed from. A win
        /// is any position on each reel, so the lines are not fixed, cannot be printed
        /// down the side of the cabinet, and do not exist until the reels have stopped.
        ///
        /// So they are worked out here rather than sent: for each winning symbol, which
        /// rows it occupies on each reel it ran through, and then every combination of
        /// those. That count is exactly what the server calls `Ways`, arrived at
        /// independently -- so a line drawn through anything but matching symbols means
        /// the two disagree and one of them is wrong.
        /// </summary>
        private static int DrawWinLines(IReadOnlyList<IReadOnlyList<string>> grid, JArray wins)
        {
            ClearLines();

            if (_lines == null || grid == null || wins == null)
            {
                return 0;
            }

            // Worked out in full before anything is drawn, because the spacing between
            // the lines depends on how many there are going to be.
            var plan = new List<int[]>();

            foreach (var token in wins)
            {
                var symbol = (string)token["Symbol"];
                var reels = (int?)token["Reels"] ?? 0;

                if (string.IsNullOrEmpty(symbol) || reels < 1)
                {
                    continue;
                }

                // Which rows hold the symbol, reel by reel.
                var rows = new List<List<int>>();

                for (var reel = 0; reel < reels && reel < grid.Count; reel++)
                {
                    var here = new List<int>();

                    for (var row = 0; row < 3 && row < grid[reel].Count; row++)
                    {
                        if (grid[reel][row] == symbol)
                        {
                            here.Add(row);
                        }
                    }

                    rows.Add(here);
                }

                // The cells first, once for the whole win, so the frames sit under every
                // line that runs through them. Drawing them per way would stack nine
                // identical outlines on one symbol and turn the edge into a smear.
                var frame = LineColours[plan.Count % LineColours.Length];

                for (var reel = 0; reel < rows.Count; reel++)
                {
                    foreach (var row in rows[reel])
                    {
                        MarkCell(reel, row, frame);
                    }
                }

                foreach (var way in Ways(rows))
                {
                    if (plan.Count >= MaxLines)
                    {
                        break;
                    }

                    plan.Add(way);
                }
            }

            // Evenly spread about the middle of the symbol. One line runs dead centre;
            // any more and they fan out either side of it.
            var gap = plan.Count > 1
                ? Mathf.Min(LineBand / (plan.Count - 1), MaxLineGap)
                : 0f;

            var first = -(plan.Count - 1) * 0.5f * gap;

            for (var i = 0; i < plan.Count; i++)
            {
                DrawWay(plan[i], LineColours[i % LineColours.Length], i, first + (i * gap));
            }

            return plan.Count;
        }

        /// <summary>
        /// Every combination of one row per reel: the ways, spelled out.
        ///
        /// Yielded rather than collected, so a win worth 243 ways costs twelve of them
        /// and then stops.
        /// </summary>
        private static IEnumerable<int[]> Ways(List<List<int>> rows)
        {
            if (rows.Count == 0 || rows.Any(r => r.Count == 0))
            {
                yield break;
            }

            var at = new int[rows.Count];

            while (true)
            {
                var way = new int[rows.Count];

                for (var i = 0; i < rows.Count; i++)
                {
                    way[i] = rows[i][at[i]];
                }

                yield return way;

                var carry = rows.Count - 1;

                while (carry >= 0 && ++at[carry] >= rows[carry].Count)
                {
                    at[carry] = 0;
                    carry--;
                }

                if (carry < 0)
                {
                    yield break;
                }
            }
        }

        /// <summary>
        /// Rings one winning symbol.
        ///
        /// The frames do most of the work of saying what won -- a line tells you the
        /// shape of a way, a frame tells you which symbols are in it, and the second is
        /// the thing a player actually looks for. The first version had lines and no
        /// frames, and a bare polyline over five photographs reads as a scratch on the
        /// screen.
        /// </summary>
        private static void MarkCell(int reel, int row, Color colour)
        {
            var mark = NewBox($"Cell_{reel}_{row}", _lines, Color.white);
            mark.sizeDelta = new Vector2(ReelView.Cell - 12f, ReelView.Cell - 12f);
            mark.anchoredPosition = new Vector2(ReelView.ReelX(reel), ReelView.RowY(row));

            var image = mark.GetComponent<Image>();

            // A wash of the colour inside a bright edge of it. The wash is what stops
            // the frame reading as a sticker sitting on top of the symbol.
            image.sprite = Textures.RoundedBox(
                12,
                new Color(colour.r, colour.g, colour.b, 0.13f),
                new Color(colour.r, colour.g, colour.b, 0.85f),
                3);

            image.type = Image.Type.Sliced;
            image.raycastTarget = false;
        }

        /// <summary>
        /// One way: a numbered badge and a run through the middle of every symbol it
        /// claims.
        ///
        /// Drawn in three passes, and the order is the whole reason it reads at all:
        ///
        /// 1. a dark halo under everything, so the line survives crossing a bright
        ///    rouble stack as well as a dark grenade;
        /// 2. the line itself;
        /// 3. a dot at each corner, because two rotated rectangles meeting at an angle
        ///    leave a notch on the outside of the turn, and a notch on every corner is
        ///    most of what made the first version look broken.
        /// </summary>
        private static void DrawWay(int[] way, Color colour, int index, float nudge)
        {
            const float overhang = 12f;
            var points = new List<Vector2>
            {
                new Vector2(
                    ReelView.ReelX(0) - (ReelView.Cell * 0.5f) - overhang,
                    ReelView.RowY(way[0]) + nudge),
            };

            for (var reel = 0; reel < way.Length; reel++)
            {
                points.Add(new Vector2(ReelView.ReelX(reel), ReelView.RowY(way[reel]) + nudge));
            }

            points.Add(new Vector2(
                ReelView.ReelX(way.Length - 1) + (ReelView.Cell * 0.5f) + overhang,
                ReelView.RowY(way[way.Length - 1]) + nudge));

            var halo = new Color(0f, 0f, 0f, 0.55f);

            for (var i = 0; i < points.Count - 1; i++)
            {
                Segment(points[i], points[i + 1], halo, LineWidth + 3.5f);
            }

            for (var i = 0; i < points.Count - 1; i++)
            {
                Segment(points[i], points[i + 1], colour, LineWidth);
            }

            // Corners only: the ends are covered by the badge and the overhang.
            for (var i = 1; i < points.Count - 1; i++)
            {
                Joint(points[i], colour);
            }

            if (index < MaxBadges)
            {
                Badge(index, points[0], colour);
            }
        }

        /// <summary>A numbered tag on the left, the way a payline machine numbers its lines.</summary>
        private static void Badge(int index, Vector2 at, Color colour)
        {
            var badge = NewBox("Badge" + index, _lines, Color.white);
            badge.sizeDelta = new Vector2(22f, 22f);
            badge.anchoredPosition = at + new Vector2(-13f, 0f);

            var face = badge.GetComponent<Image>();
            face.sprite = Textures.RoundedBox(10, colour, new Color(0f, 0f, 0f, 0.65f), 2);
            face.type = Image.Type.Sliced;
            face.raycastTarget = false;

            var number = NewText("BadgeText", badge, (index + 1).ToString(), 13f);
            number.rectTransform.anchorMin = Vector2.zero;
            number.rectTransform.anchorMax = Vector2.one;
            number.rectTransform.offsetMin = Vector2.zero;
            number.rectTransform.offsetMax = Vector2.zero;
            number.color = new Color(0.06f, 0.06f, 0.06f, 1f);
        }

        /// <summary>
        /// One straight piece of a line: a thin box as long as the gap, turned to face
        /// along it. uGUI has no line renderer, and a rotated rect is the whole of what
        /// one would be.
        /// </summary>
        private static void Segment(Vector2 from, Vector2 to, Color colour, float width)
        {
            var delta = to - from;

            var bar = NewBox("Segment", _lines, colour);
            bar.pivot = new Vector2(0f, 0.5f);
            bar.sizeDelta = new Vector2(delta.magnitude, width);
            bar.anchoredPosition = from;
            bar.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg);

            bar.GetComponent<Image>().raycastTarget = false;
        }

        /// <summary>
        /// A round cap on a corner, filling the notch two rotated rectangles leave
        /// between them. What a line renderer would call a joint.
        /// </summary>
        private static void Joint(Vector2 at, Color colour)
        {
            var dot = NewBox("Joint", _lines, Color.white);
            dot.sizeDelta = new Vector2(LineWidth + 3.5f, LineWidth + 3.5f);
            dot.anchoredPosition = at;

            var image = dot.GetComponent<Image>();
            image.sprite = Textures.RoundedBox(8, colour, colour, 0);
            image.type = Image.Type.Sliced;
            image.raycastTarget = false;
        }

        private static void ClearLines()
        {
            if (_lines == null)
            {
                return;
            }

            for (var i = _lines.childCount - 1; i >= 0; i--)
            {
                UnityEngine.Object.Destroy(_lines.GetChild(i).gameObject);
            }
        }

        // ------------------------------------------------------------------ reading

        private static void ReadMachine(JObject ping)
        {
            Limits.Clear();
            Symbols.Clear();
            Pays.Clear();

            if (ping?["Limits"] is JObject limits)
            {
                foreach (var pair in limits)
                {
                    var l = pair.Value as JObject;

                    Limits[pair.Key] =
                    [
                        (long?)l?["Min"] ?? 0,
                        (long?)l?["Max"] ?? 0,
                        (long?)l?["Step"] ?? 1,
                    ];
                }
            }

            if (ping?["Paytable"] is JObject paytable)
            {
                foreach (var pair in paytable)
                {
                    Symbols.Add(pair.Key);

                    // Three numbers: what three, four and five reels pay. They travel
                    // from the server rather than being written in here, so the panel
                    // cannot advertise a payout the machine does not give.
                    Pays[pair.Key] = pair.Value is JArray row
                        ? [.. row.Select(v => (int?)v ?? 0)]
                        : [0, 0, 0];
                }
            }

            if (Limits.TryGetValue(_wallet, out var mine))
            {
                _stake = mine[0];
            }

            ReelView.Restock(Symbols);
        }

        private static IReadOnlyList<IReadOnlyList<string>> ReadGrid(JObject pull)
        {
            var grid = new List<IReadOnlyList<string>>();

            if (pull["Grid"] is JArray reels)
            {
                foreach (var reel in reels)
                {
                    grid.Add(reel is JArray rows
                        ? [.. rows.Select(r => (string)r ?? "Cola")]
                        : new List<string> { "Cola", "Cola", "Cola" });
                }
            }

            return grid;
        }

        // ------------------------------------------------------------------ drawing

        private static void Build()
        {
            _font = Resources.FindObjectsOfTypeAll<TMP_FontAsset>().FirstOrDefault();

            var canvasObject = new GameObject(
                RootName, typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));

            UnityEngine.Object.DontDestroyOnLoad(canvasObject);
            _root = canvasObject;

            var canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 30000;

            var scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 1f;

            _group = canvasObject.AddComponent<CanvasGroup>();
            _group.alpha = 0f;

            var backdrop = NewBox("Backdrop", canvasObject.transform, new Color(0f, 0f, 0f, 0.86f));
            backdrop.anchorMin = Vector2.zero;
            backdrop.anchorMax = Vector2.one;
            backdrop.offsetMin = Vector2.zero;
            backdrop.offsetMax = Vector2.zero;

            // Everything below hangs off this and is placed from its edges, so the
            // layout holds together at any resolution and a piece that fails to build
            // leaves an obvious hole rather than a plausible panel.
            var frame = NewBox("Frame", canvasObject.transform, Color.white);
            frame.sizeDelta = new Vector2(FrameWidth, FrameHeight);
            frame.anchoredPosition = new Vector2(0f, 20f);

            var frameImage = frame.GetComponent<Image>();
            frameImage.sprite = Textures.RoundedBox(16, Cabinet, Edge, 3);
            frameImage.type = Image.Type.Sliced;

            var top = FrameHeight * 0.5f;

            var title = NewText("Title", frame, "SLOTS", 34f);
            title.rectTransform.anchoredPosition = new Vector2(0f, top - 42f);
            title.rectTransform.sizeDelta = new Vector2(FrameWidth - 40f, 44f);
            title.color = Gold;

            var ways = NewText(
                "Ways",
                frame,
                "243 ways -- matching symbols in any position, from the leftmost reel.",
                18f);

            ways.rectTransform.anchoredPosition = new Vector2(0f, top - 74f);
            ways.rectTransform.sizeDelta = new Vector2(FrameWidth - 40f, 24f);
            ways.color = Dim;

            BuildPaytable(frame, top);

            // The reels, and the lines over them, in the space the paytable leaves.
            var reelsX = ContentLeft + PayWidth + PayGap + ((ReelView.Width + 28f) * 0.5f);
            const float reelsY = 34f;

            var reels = ReelView.Build(frame, Symbols);
            var reelsRect = (RectTransform)reels.transform;
            reelsRect.anchoredPosition = new Vector2(reelsX, reelsY);

            // A sibling of the reel windows rather than a child of one: the windows are
            // Masks, and a line inside one would be clipped to a single reel. Added
            // last, so it draws over them.
            _lines = NewBox("WinLines", reelsRect, Color.clear);
            _lines.sizeDelta = new Vector2(ReelView.Width, ReelView.Height);
            _lines.anchoredPosition = Vector2.zero;
            _lines.GetComponent<Image>().raycastTarget = false;

            _paidLabel = NewText("Paid", frame, string.Empty, 30f);
            _paidLabel.rectTransform.anchoredPosition =
                new Vector2(reelsX, reelsY + (ReelView.Height * 0.5f) + 46f);

            _paidLabel.rectTransform.sizeDelta = new Vector2(ReelView.Width, 38f);
            _paidLabel.color = Gold;

            BuildSpinButton(frame, reelsX, reelsY);

            BuildStakeRow(frame, reelsX);

            _status = NewText("Status", frame, string.Empty, 19f);
            _status.rectTransform.anchoredPosition = new Vector2(0f, -(top - 94f));
            _status.rectTransform.sizeDelta = new Vector2(FrameWidth - 40f, 26f);

            BuildControls(frame, top);
        }

        /// <summary>
        /// The big one, on the right where the handle used to be.
        ///
        /// It replaced a lever you could drag, at the player's request -- and that lever
        /// is also what threw the null that stopped this method halfway the first time
        /// it ran on a real machine.
        /// </summary>
        private static void BuildSpinButton(RectTransform frame, float reelsX, float reelsY)
        {
            var button = NewBox("Spin", frame, Color.white);
            button.sizeDelta = new Vector2(SpinSize, SpinSize);
            button.anchoredPosition = new Vector2(
                reelsX + ((ReelView.Width + 28f) * 0.5f) + SpinGap + (SpinSize * 0.5f), reelsY);

            _spinFace = button.GetComponent<Image>();

            _spinLabel = NewText("SpinLabel", button, "SPIN", 30f);
            _spinLabel.rectTransform.anchorMin = Vector2.zero;
            _spinLabel.rectTransform.anchorMax = Vector2.one;
            _spinLabel.rectTransform.offsetMin = Vector2.zero;
            _spinLabel.rectTransform.offsetMax = Vector2.zero;

            SetSpinEnabled(true);

            button.gameObject.AddComponent<Button>().onClick.AddListener(() => Pull());
        }

        /// <summary>
        /// Greys the spin button while the reels are turning.
        ///
        /// <see cref="Pull"/> refuses a second spin anyway; this is so the machine looks
        /// like it is refusing rather than like it missed the click.
        /// </summary>
        private static void SetSpinEnabled(bool on)
        {
            if (_spinFace != null)
            {
                _spinFace.sprite = Textures.RoundedBox(80, on ? SpinRed : SpinDead, Edge, 4);
                _spinFace.type = Image.Type.Sliced;
            }

            if (_spinLabel != null)
            {
                _spinLabel.text = on ? "SPIN" : "...";
                _spinLabel.color = on ? Ink : Dim;
            }
        }

        /// <summary>
        /// What the machine pays, down the side of the cabinet.
        ///
        /// Written out as artwork, name and <c>25x 150x 1000x</c> rather than as a grid
        /// of bare numbers, because a paytable nobody can read is a machine that looks
        /// like it pays at random. Richest first, which is the order anybody reads one
        /// in. Multipliers on the stake rather than amounts -- the stake is three
        /// currencies, and a column of roubles would be wrong in two of them.
        ///
        /// Every number comes from the server's ping response. Nothing about the payouts
        /// is written into the client, so the panel cannot advertise something the
        /// machine does not give.
        /// </summary>
        private static void BuildPaytable(RectTransform frame, float top)
        {
            const float IconSize = 36f;

            // Three right-aligned columns rather than one string of padded numbers. The
            // padded version lines up in a monospaced font and in nothing else, and the
            // game's font is not monospaced.
            var edge = (PayWidth * 0.5f) - PayInset;
            var columns = new[]
            {
                new { Head = "x3", Width = 52f, Right = edge - 128f },
                new { Head = "x4", Width = 58f, Right = edge - 68f },
                new { Head = "x5", Width = 66f, Right = edge },
            };

            var iconX = -(PayWidth * 0.5f) + PayInset + (IconSize * 0.5f);

            // The name starts a clear gap to the RIGHT of the icon's right edge. Written
            // as that sentence rather than as a number, because the number was wrong: the
            // names used to begin five units before the icons ended.
            var nameLeft = iconX + (IconSize * 0.5f) + 16f;
            var nameWidth = columns[0].Right - columns[0].Width - 12f - nameLeft;

            var ordered = Symbols
                .OrderByDescending(s => Pays.TryGetValue(s, out var p) ? p[2] : 0)
                .ToList();

            PayFaces.Clear();

            var panel = NewBox("Paytable", frame, Color.white);
            panel.sizeDelta = new Vector2(PayWidth, (Math.Max(ordered.Count, 1) * PayRow) + 104f);
            panel.anchoredPosition = new Vector2(
                ContentLeft + (PayWidth * 0.5f), top - 102f - (panel.sizeDelta.y * 0.5f));

            var face = panel.GetComponent<Image>();
            face.sprite = Textures.RoundedBox(10, new Color(0.09f, 0.09f, 0.11f, 1f), Edge, 2);
            face.type = Image.Type.Sliced;

            var payTop = (panel.sizeDelta.y * 0.5f) - 24f;

            var heading = NewText("PayTitle", panel, "PAYTABLE", 20f);
            heading.rectTransform.anchoredPosition = new Vector2(0f, payTop);
            heading.rectTransform.sizeDelta = new Vector2(PayWidth - 24f, 24f);
            heading.color = Gold;

            foreach (var column in columns)
            {
                var head = NewText("Head_" + column.Head, panel, column.Head, 15f);
                head.rectTransform.anchoredPosition =
                    new Vector2(column.Right - (column.Width * 0.5f), payTop - 28f);

                head.rectTransform.sizeDelta = new Vector2(column.Width, 18f);
                head.alignment = TextAlignmentOptions.Right;
                head.color = Dim;
            }

            if (ordered.Count == 0)
            {
                var none = NewText("PayNone", panel, "The server has not said.", 15f);
                none.rectTransform.sizeDelta = new Vector2(PayWidth - 24f, 24f);
                none.color = Dim;
                return;
            }

            for (var i = 0; i < ordered.Count; i++)
            {
                var symbol = ordered[i];
                var y = payTop - 58f - (i * PayRow);
                var colour = i < 3 ? Gold : Ink;

                var art = NewBox("Face_" + symbol, panel, Color.white);
                art.sizeDelta = new Vector2(IconSize, IconSize);
                art.anchoredPosition = new Vector2(iconX, y);

                var image = art.GetComponent<Image>();
                image.sprite = ReelView.Artwork(symbol);
                image.preserveAspect = true;
                image.raycastTarget = false;

                PayFaces[symbol] = image;

                var name = NewText("Name_" + symbol, panel, NameOf(symbol), 14f);
                name.rectTransform.anchoredPosition = new Vector2(nameLeft + (nameWidth * 0.5f), y);
                name.rectTransform.sizeDelta = new Vector2(nameWidth, PayRow);
                name.alignment = TextAlignmentOptions.Left;
                name.overflowMode = TextOverflowModes.Ellipsis;
                name.color = colour;

                var pays = Pays.TryGetValue(symbol, out var p) ? p : [0, 0, 0];

                for (var c = 0; c < columns.Length; c++)
                {
                    var cell = NewText(
                        $"Pay_{symbol}_{columns[c].Head}", panel, $"{pays[c]}x", 16f);

                    cell.rectTransform.anchoredPosition =
                        new Vector2(columns[c].Right - (columns[c].Width * 0.5f), y);

                    cell.rectTransform.sizeDelta = new Vector2(columns[c].Width, PayRow);
                    cell.alignment = TextAlignmentOptions.Right;
                    cell.color = colour;
                }
            }

            var note = NewText(
                "PayNote",
                panel,
                "x your stake, and again by how many ways it landed.",
                13f);

            note.rectTransform.anchoredPosition = new Vector2(0f, -(panel.sizeDelta.y * 0.5f) + 20f);
            note.rectTransform.sizeDelta = new Vector2(PayWidth - 20f, 18f);
            note.color = new Color(0.55f, 0.53f, 0.50f, 1f);
        }

        private static void BuildControls(RectTransform frame, float top)
        {
            var row = NewBox("Controls", frame, Color.clear);
            row.sizeDelta = new Vector2(FrameWidth - 60f, 52f);
            row.anchoredPosition = new Vector2(0f, -(top - 48f));

            var strip = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            strip.spacing = 12f;
            strip.childAlignment = TextAnchor.MiddleCenter;
            strip.childForceExpandWidth = false;
            strip.childForceExpandHeight = false;
            strip.childControlWidth = false;
            strip.childControlHeight = false;

            SmallButton(row, "CLOSE", Close, 200f);
        }

        private static void SmallButton(
            RectTransform parent, string label, Action action, float width = 180f)
        {
            var box = NewBox("Button_" + label, parent, Color.white);
            box.sizeDelta = new Vector2(width, 46f);

            var image = box.GetComponent<Image>();
            image.sprite = Textures.RoundedBox(6, ButtonFace, Edge, 2);
            image.type = Image.Type.Sliced;

            var text = NewText("Label", box, label, 19f);
            text.rectTransform.anchorMin = Vector2.zero;
            text.rectTransform.anchorMax = Vector2.one;
            text.rectTransform.offsetMin = Vector2.zero;
            text.rectTransform.offsetMax = Vector2.zero;
            text.color = Ink;

            box.gameObject.AddComponent<Button>().onClick.AddListener(() => action());
        }

        private static void Refresh() => SetStake();

        /// <summary>
        /// The stake: a box you can type in, a minus and a plus either side of it, and
        /// the currency beside that.
        ///
        /// **Typed, because a stepper alone cannot express "I want to bet 12,345".**
        /// The buttons stay because they are faster for the common case, and they walk
        /// the wallet's own step. The server stopped requiring a multiple of that step
        /// when this box arrived -- see `WalletInfo.Allows`.
        /// </summary>
        private static void BuildStakeRow(RectTransform frame, float reelsX)
        {
            var row = NewBox("StakeRow", frame, Color.clear);
            row.sizeDelta = new Vector2(ReelView.Width + 40f, 52f);
            row.anchoredPosition = new Vector2(reelsX, -196f);
            row.GetComponent<Image>().raycastTarget = false;

            var strip = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            strip.spacing = 10f;
            strip.childAlignment = TextAnchor.MiddleCenter;
            strip.childForceExpandWidth = false;
            strip.childForceExpandHeight = false;
            strip.childControlWidth = false;
            strip.childControlHeight = false;

            var caption = NewText("StakeCaption", row, "STAKE", 20f);
            caption.rectTransform.sizeDelta = new Vector2(76f, 46f);
            caption.alignment = TextAlignmentOptions.Right;
            caption.color = Dim;

            SmallButton(row, "-", () => StepStake(-1), 52f);

            _stakeInput = NewStakeField(row);

            SmallButton(row, "+", () => StepStake(1), 52f);

            var wallet = NewBox("Wallet", row, Color.white);
            wallet.sizeDelta = new Vector2(170f, 46f);

            var walletImage = wallet.GetComponent<Image>();
            walletImage.sprite = Textures.RoundedBox(6, ButtonFace, Edge, 2);
            walletImage.type = Image.Type.Sliced;

            _walletLabel = NewText("WalletLabel", wallet, string.Empty, 18f);
            _walletLabel.rectTransform.anchorMin = Vector2.zero;
            _walletLabel.rectTransform.anchorMax = Vector2.one;
            _walletLabel.rectTransform.offsetMin = Vector2.zero;
            _walletLabel.rectTransform.offsetMax = Vector2.zero;
            _walletLabel.color = Ink;

            wallet.gameObject.AddComponent<Button>().onClick.AddListener(() => NextWallet());
        }

        /// <summary>
        /// The box itself.
        ///
        /// Built by hand because there is no prefab to instantiate: a background image,
        /// a viewport to clip against, a text object inside it, and the input field
        /// pointed at both. Miss the viewport and the caret is placed relative to
        /// nothing; miss `targetGraphic` and clicking it does not focus it.
        /// </summary>
        private static TMP_InputField NewStakeField(RectTransform parent)
        {
            var box = NewBox("StakeField", parent, Color.white);
            box.sizeDelta = new Vector2(190f, 46f);

            var background = box.GetComponent<Image>();
            background.sprite = Textures.RoundedBox(6, new Color(0.07f, 0.07f, 0.08f, 1f), Edge, 2);
            background.type = Image.Type.Sliced;

            var viewport = NewBox("Viewport", box, Color.clear);
            viewport.anchorMin = Vector2.zero;
            viewport.anchorMax = Vector2.one;
            viewport.offsetMin = new Vector2(10f, 4f);
            viewport.offsetMax = new Vector2(-10f, -4f);
            viewport.GetComponent<Image>().raycastTarget = false;

            var text = NewText("Text", viewport, string.Empty, 22f);
            text.rectTransform.anchorMin = Vector2.zero;
            text.rectTransform.anchorMax = Vector2.one;
            text.rectTransform.offsetMin = Vector2.zero;
            text.rectTransform.offsetMax = Vector2.zero;
            text.alignment = TextAlignmentOptions.Center;
            text.richText = false;
            text.color = Ink;

            var input = box.gameObject.AddComponent<TMP_InputField>();
            input.targetGraphic = background;
            input.textViewport = viewport;
            input.textComponent = text;
            input.contentType = TMP_InputField.ContentType.IntegerNumber;
            input.characterLimit = 9;
            input.restoreOriginalTextOnEscape = false;

            // Three things, because one of them alone was not enough to see where the
            // click had landed. The caret at its default single pixel is invisible on a
            // 1440p screen; selecting the whole number on focus paints a gold block that
            // cannot be missed; and the border lighting up says the box has the keyboard
            // even before anything is typed.
            input.caretColor = Gold;
            input.customCaretColor = true;
            input.caretWidth = 3;
            input.caretBlinkRate = 0.9f;
            input.selectionColor = new Color(Gold.r, Gold.g, Gold.b, 0.45f);
            input.onFocusSelectAll = true;

            input.transition = Selectable.Transition.SpriteSwap;

            var lit = Textures.RoundedBox(6, new Color(0.07f, 0.07f, 0.08f, 1f), Gold, 2);

            input.spriteState = new SpriteState
            {
                highlightedSprite = lit,
                pressedSprite = lit,
                selectedSprite = lit,
            };

            // On leaving the box or pressing return, whichever comes first. Both are
            // "I have finished typing a number", and a stake that only took effect on
            // one of them would be a stake somebody spins without.
            input.onEndEdit.AddListener(TypedStake);

            return input;
        }

        /// <summary>
        /// Takes what was typed, or explains why it could not.
        ///
        /// Clamped rather than refused: somebody who types 90,000 into a machine whose
        /// ceiling is 50,000 meant "as much as it takes", and putting 50,000 in the box
        /// tells them what that is. The box is always rewritten from the accepted value,
        /// so what is on screen is what the next spin will cost.
        /// </summary>
        private static void TypedStake(string typed)
        {
            if (!Limits.TryGetValue(_wallet, out var limits))
            {
                return;
            }

            if (!long.TryParse(typed, out var wanted))
            {
                SetStake();
                return;
            }

            var clamped = Math.Max(limits[0], Math.Min(limits[1], wanted));

            if (clamped != wanted)
            {
                SetStatus(
                    $"A spin in {_wallet.ToLowerInvariant()} costs {limits[0]:N0} to {limits[1]:N0}."
                    + $"  Set to {clamped:N0}.");
            }

            _stake = clamped;
            SetStake();
        }

        /// <summary>
        /// Puts the game's own icons on the reels and down the paytable, as each one
        /// finishes being drawn.
        /// </summary>
        private static void UseRealArt()
        {
            ReelView.Repaint();

            foreach (var pair in PayFaces)
            {
                if (pair.Value != null)
                {
                    pair.Value.sprite = ReelView.Artwork(pair.Key);
                }
            }

            SetSpinEnabled(Ready && !ReelView.Spinning);

            // Nothing is drawn on the reels or in the paytable until every symbol is
            // the real thing. The blanks are a fallback, not something to look at.
            ReelView.ShowSymbols(Ready);

            foreach (var pair in PayFaces)
            {
                if (pair.Value != null)
                {
                    pair.Value.enabled = Ready;
                }
            }

            if (Ready && _status != null && _status.text.StartsWith("Fetching"))
            {
                SetStatus("Press SPIN.");
            }
        }

        private static void StepStake(int direction)
        {
            if (!Limits.TryGetValue(_wallet, out var l))
            {
                return;
            }

            _stake = Math.Max(l[0], Math.Min(l[1], _stake + (direction * l[2])));
            SetStake();
        }

        private static void NextWallet()
        {
            var names = Limits.Keys.ToList();

            if (names.Count == 0)
            {
                return;
            }

            var at = names.IndexOf(_wallet);
            _wallet = names[(at + 1 + names.Count) % names.Count];
            _stake = Limits[_wallet][0];

            SetStake();
        }

        private static void SetStake()
        {
            if (_stakeInput != null)
            {
                // SetTextWithoutNotify: assigning .text raises onEndEdit on some paths,
                // and a setter that calls the handler that calls the setter is a loop
                // waiting for an excuse.
                _stakeInput.SetTextWithoutNotify(_stake.ToString());
            }

            if (_walletLabel != null)
            {
                _walletLabel.text = _wallet.ToUpperInvariant();
            }
        }

        private static void SetPaid(long? paid)
        {
            if (_paidLabel == null)
            {
                return;
            }

            _paidLabel.text = paid switch
            {
                null => string.Empty,
                0 => string.Empty,
                _ => $"+{paid:N0}",
            };
        }

        private static void SetStatus(string text)
        {
            if (_status != null)
            {
                _status.text = text ?? string.Empty;
            }
        }

        private static void Note(JObject reply)
        {
            var note = (string)reply?["Note"];

            if (string.IsNullOrEmpty(note))
            {
                return;
            }

            ProfileSync.Request(SyncAction);
            SetStatus(note);

            SlotClientPlugin.Log.LogInfo("[Slots] " + note);
        }

        /// <summary>
        /// What a symbol is called on the paytable and in the win line.
        ///
        /// Presentation only, and deliberately falls through to the server's own name
        /// for anything it does not know -- a symbol added on the server should appear
        /// on an old client looking plain, not looking broken.
        /// </summary>
        private static string NameOf(string symbol) => symbol switch
        {
            "Cola" => "TARCOLA",
            "Salewa" => "SALEWA",
            "Moonshine" => "MOONSHINE",
            "Tetriz" => "TETRIZ",
            "Watch" => "GOLD WATCH",
            "Rooster" => "GOLD ROOSTER",
            "Gpu" => "GRAPHICS CARD",
            "Bitcoin" => "BITCOIN",
            "Keycard" => "RED KEYCARD",
            _ => symbol?.ToUpperInvariant() ?? string.Empty,
        };

        // ------------------------------------------------------------------- pieces

        private static void FadeTo(float target, Action done)
        {
            var host = SlotClientPlugin.Instance;

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

        private static TextMeshProUGUI NewText(string name, Transform parent, string text, float size)
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
    }
}
