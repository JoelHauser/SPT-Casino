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
        private const float PayWidth = 340f;
        private const float PayRow = 40f;

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
        private static TextMeshProUGUI _stakeLabel;
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
                SetSpinEnabled(!ReelView.Spinning);

                // The game draws the real item icons, some frames from now. Until they
                // arrive the reels show the pictures that shipped with the mod.
                ItemArt.Fetch(SlotClientPlugin.Instance, Symbols, UseRealArt);

                Note(ping);

                SetStatus(ping == null
                    ? "The server is not answering. The machine will not take a pull."
                    : "Press SPIN.");

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
            SetSpinEnabled(true);

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

            var drawn = 0;

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

                foreach (var way in Ways(rows))
                {
                    if (drawn >= MaxLines)
                    {
                        return drawn;
                    }

                    DrawWay(way, LineColours[drawn % LineColours.Length], drawn);
                    drawn++;
                }
            }

            return drawn;
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
        /// One way, as a numbered badge and a run of segments through the middle of
        /// every symbol it claims.
        /// </summary>
        private static void DrawWay(int[] way, Color colour, int index)
        {
            // Two ways through the same cells would sit exactly on top of each other,
            // so each is nudged. Small enough to still read as going through the symbol,
            // big enough to count them.
            var nudge = ((index % 5) - 2) * 5f;

            var points = new List<Vector2>
            {
                new Vector2(
                    ReelView.ReelX(0) - (ReelView.Cell * 0.5f) - 26f,
                    ReelView.RowY(way[0]) + nudge),
            };

            for (var reel = 0; reel < way.Length; reel++)
            {
                points.Add(new Vector2(ReelView.ReelX(reel), ReelView.RowY(way[reel]) + nudge));
            }

            points.Add(new Vector2(
                ReelView.ReelX(way.Length - 1) + (ReelView.Cell * 0.5f) + 26f,
                ReelView.RowY(way[way.Length - 1]) + nudge));

            for (var i = 0; i < points.Count - 1; i++)
            {
                Segment(points[i], points[i + 1], colour);
            }

            // A numbered tag on the left, the way a payline machine numbers its lines.
            var badge = NewBox("Badge" + index, _lines, Color.white);
            badge.sizeDelta = new Vector2(26f, 26f);
            badge.anchoredPosition = points[0];

            var face = badge.GetComponent<Image>();
            face.sprite = Textures.RoundedBox(12, colour, new Color(0f, 0f, 0f, 0.6f), 2);
            face.type = Image.Type.Sliced;
            face.raycastTarget = false;

            var number = NewText("BadgeText", badge, (index + 1).ToString(), 15f);
            number.rectTransform.anchorMin = Vector2.zero;
            number.rectTransform.anchorMax = Vector2.one;
            number.rectTransform.offsetMin = Vector2.zero;
            number.rectTransform.offsetMax = Vector2.zero;
            number.color = new Color(0.08f, 0.08f, 0.08f, 1f);
        }

        /// <summary>
        /// One straight piece of a line: a thin box as long as the gap, turned to face
        /// along it. uGUI has no line renderer, and a rotated rect is the whole of what
        /// one would be.
        /// </summary>
        private static void Segment(Vector2 from, Vector2 to, Color colour)
        {
            var delta = to - from;

            var bar = NewBox("Segment", _lines, colour);
            bar.pivot = new Vector2(0f, 0.5f);
            bar.sizeDelta = new Vector2(delta.magnitude, 4f);
            bar.anchoredPosition = from;
            bar.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg);

            bar.GetComponent<Image>().raycastTarget = false;
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
                        ? [.. rows.Select(r => (string)r ?? "Medkit")]
                        : new List<string> { "Medkit", "Medkit", "Medkit" });
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
            var left = -FrameWidth * 0.5f;

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

            BuildPaytable(frame, left, top);

            // The reels, and the lines over them, in the space the paytable leaves.
            var reelsX = left + 24f + PayWidth + 28f + ((ReelView.Width + 28f) * 0.5f);
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

            _stakeLabel = NewText("Stake", frame, string.Empty, 24f);
            _stakeLabel.rectTransform.anchoredPosition = new Vector2(reelsX, -190f);
            _stakeLabel.rectTransform.sizeDelta = new Vector2(ReelView.Width + 220f, 32f);

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
            button.sizeDelta = new Vector2(164f, 164f);
            button.anchoredPosition =
                new Vector2(reelsX + ((ReelView.Width + 28f) * 0.5f) + 26f + 82f, reelsY);

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
        private static void BuildPaytable(RectTransform frame, float left, float top)
        {
            var ordered = Symbols
                .OrderByDescending(s => Pays.TryGetValue(s, out var p) ? p[2] : 0)
                .ToList();

            PayFaces.Clear();

            var panel = NewBox("Paytable", frame, Color.white);
            panel.sizeDelta = new Vector2(PayWidth, (Math.Max(ordered.Count, 1) * PayRow) + 100f);
            panel.anchoredPosition = new Vector2(
                left + 24f + (PayWidth * 0.5f), top - 102f - (panel.sizeDelta.y * 0.5f));

            var face = panel.GetComponent<Image>();
            face.sprite = Textures.RoundedBox(10, new Color(0.09f, 0.09f, 0.11f, 1f), Edge, 2);
            face.type = Image.Type.Sliced;

            var payTop = (panel.sizeDelta.y * 0.5f) - 24f;

            var heading = NewText("PayTitle", panel, "PAYTABLE", 20f);
            heading.rectTransform.anchoredPosition = new Vector2(0f, payTop);
            heading.rectTransform.sizeDelta = new Vector2(PayWidth - 24f, 24f);
            heading.color = Gold;

            var heads = NewText("PayHeads", panel, "x3       x4        x5", 15f);
            heads.rectTransform.anchoredPosition = new Vector2(62f, payTop - 28f);
            heads.rectTransform.sizeDelta = new Vector2(196f, 18f);
            heads.alignment = TextAlignmentOptions.Right;
            heads.color = Dim;

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
                var y = payTop - 56f - (i * PayRow);

                var art = NewBox("Face_" + symbol, panel, Color.white);
                art.sizeDelta = new Vector2(34f, 34f);
                art.anchoredPosition = new Vector2(-(PayWidth * 0.5f) + 28f, y);

                var image = art.GetComponent<Image>();
                image.sprite = ReelView.Artwork(symbol);
                image.preserveAspect = true;
                image.raycastTarget = false;

                PayFaces[symbol] = image;

                var name = NewText("Name_" + symbol, panel, NameOf(symbol), 14f);
                name.rectTransform.anchoredPosition = new Vector2(-60f, y);
                name.rectTransform.sizeDelta = new Vector2(140f, PayRow);
                name.alignment = TextAlignmentOptions.Left;
                name.color = i < 3 ? Gold : Ink;

                var pays = Pays.TryGetValue(symbol, out var p) ? p : [0, 0, 0];

                var row = NewText(
                    "Row_" + symbol, panel, $"{pays[0],4}x {pays[1],5}x {pays[2],6}x", 16f);

                row.rectTransform.anchoredPosition = new Vector2(62f, y);
                row.rectTransform.sizeDelta = new Vector2(196f, PayRow);
                row.alignment = TextAlignmentOptions.Right;
                row.color = i < 3 ? Gold : Ink;
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

            SmallButton(row, "STAKE -", () => StepStake(-1));
            SmallButton(row, "STAKE +", () => StepStake(1));
            SmallButton(row, "CURRENCY", NextWallet);
            SmallButton(row, "CLOSE", Close);
        }

        private static void SmallButton(RectTransform parent, string label, Action action)
        {
            var box = NewBox("Button_" + label, parent, Color.white);
            box.sizeDelta = new Vector2(180f, 46f);

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
            if (_stakeLabel != null)
            {
                _stakeLabel.text = $"STAKE  {_stake:N0} {_wallet.ToUpperInvariant()}";
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
            "Medkit" => "AI-2 MEDKIT",
            "AmmoBox" => "7.62 AMMO",
            "Grenade" => "GRENADE",
            "Helmet" => "HELMET",
            "DogTag" => "BEAR TAG",
            "Roubles" => "ROUBLE STACK",
            "GpCoin" => "GP COIN",
            "Bitcoin" => "BITCOIN",
            "Keycard" => "VIOLET KEYCARD",
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
