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
        private const float FrameHeight = 740f;
        private const float PayWidth = 380f;
        private const float PayRow = 42f;

        /// <summary>Breathing room inside the paytable's own frame.</summary>
        private const float PayInset = 16f;

        private const float PayGap = 28f;
        private const float SpinGap = 26f;
        private const float SpinSize = 164f;

        /// <summary>
        /// AUTO sits under SPIN, in the same column. A rectangle rather than a disc --
        /// see <see cref="BuildAutoButton"/> -- so it never reads as a second way to
        /// take the same action.
        /// </summary>
        private const float AutoWidth = SpinSize;

        private const float AutoHeight = 50f;
        private const float AutoGap = 18f;

        /// <summary>Below AUTO, same column, same width -- see BuildSpeedButton.</summary>
        private const float SpeedHeight = 24f;

        private const float SpeedGap = 6f;

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

        // Green both while armed and while idle, deliberately -- see SetAutoRunning.
        private static readonly Color AutoGreen = new Color(0.18f, 0.56f, 0.22f, 1f);
        private static readonly Color AutoGreenOn = new Color(0.32f, 0.82f, 0.36f, 1f);

        // Same values Blackjack's stats sheet uses, so a currency in the red reads
        // the same way at every table.
        private static readonly Color Good = new Color(0.55f, 0.82f, 0.45f, 1f);
        private static readonly Color Bad = new Color(0.92f, 0.42f, 0.36f, 1f);

        /// <summary>The size the win banner sits at when the win is smaller than the stake.</summary>
        private const float QuietSize = 30f;

        /// <summary>
        /// The multiple of the stake at which the banner starts running through colours.
        ///
        /// Twenty, which is `HUGE WIN` and up. Every tier doing it would make it mean
        /// nothing -- the point of the rainbow is that most wins do not get one.
        /// </summary>
        private const double RainbowFrom = 20d;

        /// <summary>Hues per second the wave travels.</summary>
        private const float RainbowSpeed = 0.55f;

        /// <summary>How far apart two neighbouring letters sit on the wheel.</summary>
        private const float RainbowSpread = 0.06f;

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
        private static TextMeshProUGUI _balanceLabel;
        private static TextMeshProUGUI _paidLabel;
        private static Coroutine _pop;
        private static Coroutine _rainbow;
        private static RectTransform _lines;
        private static readonly Dictionary<string, Image> PayFaces = new Dictionary<string, Image>();
        private static Image _spinFace;
        private static TextMeshProUGUI _spinLabel;
        private static Image _autoFace;
        private static TextMeshProUGUI _autoLabel;

        /// <summary>
        /// Armed by AUTO. The only thing that ever schedules another pull while this is
        /// true is <see cref="Settled"/>, once the reels it is currently animating stop
        /// -- so disarming it is just setting it back to false and letting the next
        /// settle see that instead of scheduling one.
        /// </summary>
        private static bool _autoSpinning;

        private static Coroutine _autoWait;

        /// <summary>1x, 2x, 4x, 6x -- what SPEED cycles through, in order.</summary>
        private static readonly float[] SpeedSteps = [1f, 2f, 4f, 6f];

        private static int _speedStep;
        private static TextMeshProUGUI _speedLabel;

        /// <summary>
        /// What every spin runs at, manual or AUTO. <see cref="Pull"/> reads this
        /// unconditionally rather than only while a run is armed -- SPEED is its own
        /// control, not a setting that belongs to AUTO just because it lives under it.
        /// </summary>
        private static float SpinSpeed => SpeedSteps[_speedStep];

        /// <summary>
        /// Everything the machine draws to actually play: paytable, reels, spin
        /// button, stake row. One wrapper so STATS can hide all of it in one call the
        /// way Blackjack's felt column hides behind its own stats sheet.
        /// </summary>
        private static RectTransform _machine;

        private static GameObject _statsPanel;
        private static RectTransform _statsTiles;
        private static RectTransform _statsRows;
        private static TextMeshProUGUI _statsEmpty;

        private static string _wallet = "Roubles";
        private static long _stake;
        private static readonly Dictionary<string, long> Balances = new Dictionary<string, long>();
        private static readonly Dictionary<string, long[]> Limits = new Dictionary<string, long[]>();
        private static readonly List<string> Symbols = new List<string>();
        private static readonly Dictionary<string, int[]> Pays = new Dictionary<string, int[]>();
        private static bool _syncOwed;

        /// <summary>
        /// What the spin in flight cost.
        ///
        /// Kept because the win is announced relative to it, and the player is free to
        /// retype the stake while the reels are still turning.
        /// </summary>
        private static long _paidStake;

        /// <summary>
        /// Set while this code is the one writing to the stake box, so the change
        /// handler knows the keystroke was its own and leaves it alone.
        /// </summary>
        private static bool _rewriting;

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

                // Unconditionally, unlike the paytable and limits above: the balance can
                // have moved between one open and the next just from playing another
                // table, so it is read fresh every time rather than only the first.
                RefreshBalances(ping);

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

            // The banner's colour loop would otherwise go on running against a canvas
            // nobody is looking at.
            StopRainbow();

            // A run in progress must not keep pulling once the panel is shut and nobody
            // is there to click STOP.
            StopAuto();

            // The sheet must not still be lying over the reels next time this opens.
            HideStats();

            _closing = true;

            FadeTo(0f, () =>
            {
                _root.SetActive(false);
                _closing = false;
            });
        }

        /// <summary>Escape, from the casino's own handler.</summary>
        internal static void OnEscape()
        {
            if (_statsPanel != null && _statsPanel.activeSelf)
            {
                ToggleStats();
                return;
            }

            Close();
        }

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
                StopAuto();
                return;
            }

            Note(reply);

            var error = (string)reply["Error"];

            if (!string.IsNullOrEmpty(error))
            {
                SetStatus(error);
                StopAuto();
                return;
            }

            var pull = reply["Pull"] as JObject;

            if (pull == null)
            {
                SetStatus("The machine answered with nothing.");
                StopAuto();
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
            _paidStake = _stake;
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

            host.StartCoroutine(ReelView.Spin(host, grid, () => Settled(grid, paid, wins), SpinSpeed));
        }

        /// <summary>What happens when the reels stop.</summary>
        private static void Settled(
            IReadOnlyList<IReadOnlyList<string>> grid, long paid, JArray wins)
        {
            // The button first, before anything that reads a reply the server wrote.
            //
            // This used to sit under Resync(), and everything below it is presentation
            // -- win lines, a headline, the paytable -- reading a JSON reply. Any of
            // that throwing skipped the one line that puts SPIN back, so the machine
            // was left showing "..." for good: still clickable, and Pull() would go on
            // sending pulls that the player could not see the result of. Whatever else
            // fails about a spin, the machine has to end it playable.
            SetSpinEnabled(Ready);

            // Now, with the reels. Any earlier and the stash gives the answer away.
            Resync();

            // A fresh ping rather than staking-minus-paid worked out here: a win big
            // enough to overflow the stash posts the rest as mail (see the root
            // CLAUDE.md, "Where the money is"), so the server is the only thing that
            // actually knows what landed in the wallet.
            RefreshBalances(SlotApi.Ping());

            try
            {
                if (paid > 0 && wins is { Count: > 0 })
                {
                    var best = wins[0] as JObject;
                    var symbol = (string)best?["Symbol"] ?? "something";
                    var reels = (int?)best?["Reels"] ?? 0;
                    var ways = (int?)best?["Ways"] ?? 1;

                    SetPaid(paid, _paidStake);

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
            catch (Exception ex)
            {
                // The money is already settled and the reels are already showing it.
                // Say so and leave the machine playable rather than taking it down over
                // a headline.
                SlotClientPlugin.Log.LogError($"[Slots] could not show the result: {ex}");
                SetStatus("The spin settled, but this panel could not draw the result.");
            }

            // The only place another pull ever gets scheduled from. A run that was
            // disarmed mid-spin, or that just failed inside Pull, leaves this false and
            // the run simply ends here rather than needing to be cancelled.
            if (_autoSpinning)
            {
                ScheduleAuto();
            }
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

        /// <summary>
        /// What the wallet actually holds, read off a ping response and shown beside
        /// STATS and CLOSE -- the one place on the panel visible whether the reels are
        /// spinning or STATS is covering them. The stake box only ever says what the
        /// next pull will cost; a player asked to see what they had left to spend it
        /// with.
        /// </summary>
        private static void RefreshBalances(JObject ping)
        {
            if (ping?["Balances"] is not JObject balances)
            {
                return;
            }

            Balances.Clear();

            foreach (var pair in balances)
            {
                Balances[pair.Key] = (long?)pair.Value ?? 0;
            }

            SetBalanceLabel();
        }

        private static void SetBalanceLabel()
        {
            if (_balanceLabel == null)
            {
                return;
            }

            _balanceLabel.text = Balances.TryGetValue(_wallet, out var held)
                ? $"{held:N0}  {Short(_wallet)}"
                : string.Empty;
        }

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

            // Everything below that actually plays the machine hangs off this one
            // wrapper, centred on the frame exactly like the frame's own children
            // would be, so STATS can hide the lot with one SetActive rather than
            // hunting down each piece.
            _machine = NewBox("Machine", frame, Color.clear);
            _machine.sizeDelta = new Vector2(FrameWidth, FrameHeight);
            _machine.GetComponent<Image>().raycastTarget = false;

            BuildPaytable(_machine, top);

            // The reels, and the lines over them, in the space the paytable leaves.
            var reelsX = ContentLeft + PayWidth + PayGap + ((ReelView.Width + 28f) * 0.5f);

            // Lower than the middle: the win banner above needs somewhere to grow into,
            // and it grows upwards from the top of the reels.
            const float reelsY = 14f;

            var reels = ReelView.Build(_machine, Symbols);
            var reelsRect = (RectTransform)reels.transform;
            reelsRect.anchoredPosition = new Vector2(reelsX, reelsY);

            // A sibling of the reel windows rather than a child of one: the windows are
            // Masks, and a line inside one would be clipped to a single reel. Added
            // last, so it draws over them.
            _lines = NewBox("WinLines", reelsRect, Color.clear);
            _lines.sizeDelta = new Vector2(ReelView.Width, ReelView.Height);
            _lines.anchoredPosition = Vector2.zero;
            _lines.GetComponent<Image>().raycastTarget = false;

            _paidLabel = NewText("Paid", _machine, string.Empty, QuietSize);
            _paidLabel.rectTransform.anchoredPosition =
                new Vector2(reelsX, reelsY + (ReelView.Height * 0.5f) + 58f);

            // 600 wide, which stops it well short of the paytable
            // -- the banner is centred on the reels, and the reels are not centred in
            // the frame. Auto-sizing does the rest: with the cap off, a win can be
            // "JACKPOT   +50,000,000,000", and a banner that overflows its box onto the
            // paytable is worse than one that shrinks a little.
            _paidLabel.rectTransform.sizeDelta = new Vector2(600f, 78f);
            _paidLabel.enableAutoSizing = true;
            _paidLabel.fontSizeMin = 22f;
            _paidLabel.fontSizeMax = QuietSize;
            _paidLabel.color = Gold;

            BuildSpinButton(_machine, reelsX, reelsY);
            BuildAutoButton(_machine, reelsX, reelsY);
            BuildSpeedButton(_machine, reelsX, reelsY);

            BuildStakeRow(_machine, reelsX);

            // Outside _machine, like the status line: what it says stays visible
            // right up to the moment STATS covers the reels, not a moment before.
            _status = NewText("Status", frame, string.Empty, 19f);
            _status.rectTransform.anchoredPosition = new Vector2(0f, -(top - 94f));
            _status.rectTransform.sizeDelta = new Vector2(FrameWidth - 40f, 26f);

            BuildStats(frame);

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
        /// Under SPIN, in the same column. A rectangle rather than a disc, on purpose --
        /// SPIN is the button that spends money and AUTO only ever presses SPIN on the
        /// player's behalf, so the two should not read as the same kind of control at a
        /// glance.
        /// </summary>
        private static void BuildAutoButton(RectTransform frame, float reelsX, float reelsY)
        {
            var button = NewBox("Auto", frame, Color.white);
            button.sizeDelta = new Vector2(AutoWidth, AutoHeight);
            button.anchoredPosition = new Vector2(
                reelsX + ((ReelView.Width + 28f) * 0.5f) + SpinGap + (SpinSize * 0.5f),
                reelsY - (SpinSize * 0.5f) - AutoGap - (AutoHeight * 0.5f));

            _autoFace = button.GetComponent<Image>();

            _autoLabel = NewText("AutoLabel", button, "AUTO", 24f);
            _autoLabel.rectTransform.anchorMin = Vector2.zero;
            _autoLabel.rectTransform.anchorMax = Vector2.one;
            _autoLabel.rectTransform.offsetMin = Vector2.zero;
            _autoLabel.rectTransform.offsetMax = Vector2.zero;
            _autoLabel.color = Ink;

            SetAutoRunning(false);

            button.gameObject.AddComponent<Button>().onClick.AddListener(() => ToggleAuto());
        }

        /// <summary>
        /// Green either way. Only the label and the shade say whether a run is armed --
        /// see <see cref="AutoGreen"/> and <see cref="AutoGreenOn"/> -- because AUTO
        /// never becomes a different colour of "cannot be pressed" the way SPIN does:
        /// it is always the thing to click to change what is happening.
        /// </summary>
        private static void SetAutoRunning(bool on)
        {
            if (_autoFace != null)
            {
                _autoFace.sprite = Textures.RoundedBox(8, on ? AutoGreenOn : AutoGreen, Edge, 3);
                _autoFace.type = Image.Type.Sliced;
            }

            if (_autoLabel != null)
            {
                _autoLabel.text = on ? "STOP" : "AUTO";
            }
        }

        /// <summary>
        /// Arms or disarms a run. Arming spins immediately if the reels are free, the
        /// same as a manual click would -- otherwise a reel already in flight finishes
        /// on its own and <see cref="Settled"/> picks the run up from there.
        /// Disarming never touches a spin in flight; see <see cref="StopAuto"/>.
        /// </summary>
        private static void ToggleAuto()
        {
            if (_autoSpinning)
            {
                StopAuto();
                return;
            }

            if (!Ready)
            {
                SetStatus("Still fetching the symbols from your install.");
                return;
            }

            _autoSpinning = true;
            SetAutoRunning(true);

            if (!ReelView.Spinning)
            {
                Pull();
            }
        }

        /// <summary>
        /// Disarms a run and cancels whatever it is waiting on. Safe to call whether or
        /// not a run is actually active: <see cref="Close"/> and every failure inside
        /// <see cref="Pull"/> call it unconditionally rather than checking first.
        /// </summary>
        private static void StopAuto()
        {
            _autoSpinning = false;

            if (_autoWait != null && SlotClientPlugin.Instance != null)
            {
                SlotClientPlugin.Instance.StopCoroutine(_autoWait);
            }

            _autoWait = null;

            SetAutoRunning(false);
        }

        /// <summary>
        /// Under AUTO, same width. One button rather than three, cycling 1X, 2X, 4X,
        /// 6X on every click and wrapping back to 1X rather than needing a separate
        /// button to get back to normal.
        /// </summary>
        private static void BuildSpeedButton(RectTransform frame, float reelsX, float reelsY)
        {
            var button = NewBox("Speed", frame, Color.white);
            button.sizeDelta = new Vector2(AutoWidth, SpeedHeight);
            button.anchoredPosition = new Vector2(
                reelsX + ((ReelView.Width + 28f) * 0.5f) + SpinGap + (SpinSize * 0.5f),
                reelsY - (SpinSize * 0.5f) - AutoGap - AutoHeight - SpeedGap - (SpeedHeight * 0.5f));

            var image = button.GetComponent<Image>();
            image.sprite = Textures.RoundedBox(6, ButtonFace, Edge, 2);
            image.type = Image.Type.Sliced;

            _speedLabel = NewText("SpeedLabel", button, string.Empty, 17f);
            _speedLabel.rectTransform.anchorMin = Vector2.zero;
            _speedLabel.rectTransform.anchorMax = Vector2.one;
            _speedLabel.rectTransform.offsetMin = Vector2.zero;
            _speedLabel.rectTransform.offsetMax = Vector2.zero;
            _speedLabel.color = Ink;

            RenderSpeed();

            button.gameObject.AddComponent<Button>().onClick.AddListener(CycleSpeed);
        }

        private static void CycleSpeed()
        {
            _speedStep = (_speedStep + 1) % SpeedSteps.Length;
            RenderSpeed();
        }

        private static void RenderSpeed()
        {
            if (_speedLabel != null)
            {
                _speedLabel.text = $"{SpinSpeed:0}X";
            }
        }

        /// <summary>
        /// How long a result sits on screen before AUTO pulls again, at 1x. Scaled down
        /// by <see cref="SpinSpeed"/> like the reels themselves, so SPEED shortens the
        /// whole cycle and not just the part the reels are responsible for.
        /// </summary>
        private const float AutoPauseSeconds = 1.25f;

        private static void ScheduleAuto()
        {
            if (SlotClientPlugin.Instance == null)
            {
                Pull();
                return;
            }

            _autoWait = SlotClientPlugin.Instance.StartCoroutine(AutoWait(AutoPauseSeconds / SpinSpeed));
        }

        private static IEnumerator AutoWait(float seconds)
        {
            yield return new WaitForSecondsRealtime(seconds);

            _autoWait = null;

            if (_autoSpinning)
            {
                Pull();
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

            _balanceLabel = BuildReadout(row, "Balance", 220f);

            SmallButton(row, "STATS", ToggleStats, 200f);
            SmallButton(row, "CLOSE", Close, 200f);
        }

        /// <summary>
        /// A dark plate the same size a button would be, but with no <see cref="Button"/>
        /// on it -- it says something rather than doing something. Gold rather than the
        /// ink every button label uses, so it reads as money on sight the way the win
        /// banner and the paytable's own figures do.
        /// </summary>
        private static TextMeshProUGUI BuildReadout(RectTransform parent, string name, float width)
        {
            var box = NewBox(name, parent, Color.white);
            box.sizeDelta = new Vector2(width, 46f);

            var image = box.GetComponent<Image>();
            image.sprite = Textures.RoundedBox(6, ButtonFace, Edge, 2);
            image.type = Image.Type.Sliced;

            var text = NewText("Label", box, string.Empty, 19f);
            text.rectTransform.anchorMin = Vector2.zero;
            text.rectTransform.anchorMax = Vector2.one;
            text.rectTransform.offsetMin = Vector2.zero;
            text.rectTransform.offsetMax = Vector2.zero;
            text.color = Gold;

            return text;
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

        // ------------------------------------------------------------------- stats

        /// <summary>
        /// The lifetime figures, laid over the reels the same way Blackjack lays its
        /// own sheet over the felt: something to read numbers off, in the space the
        /// machine itself occupies while nobody is spinning it.
        /// </summary>
        private static void BuildStats(RectTransform frame)
        {
            var sheet = NewBox("StatsSheet", frame, new Color(0.06f, 0.07f, 0.07f, 0.94f));
            sheet.sizeDelta = new Vector2(FrameWidth - 140f, 520f);
            sheet.anchoredPosition = new Vector2(0f, 10f);

            var sheetImage = sheet.GetComponent<Image>();
            sheetImage.sprite = Textures.RoundedBox(14, new Color(0.06f, 0.07f, 0.07f, 0.94f), Edge, 2);
            sheetImage.type = Image.Type.Sliced;

            _statsPanel = sheet.gameObject;

            var column = sheet.gameObject.AddComponent<VerticalLayoutGroup>();
            column.childAlignment = TextAnchor.UpperCenter;
            column.spacing = 14f;
            column.padding = new RectOffset(24, 24, 20, 20);
            column.childForceExpandWidth = false;
            column.childForceExpandHeight = false;
            column.childControlWidth = false;
            column.childControlHeight = false;

            var heading = NewText("StatsTitle", sheet, "STATS", 22f);
            heading.rectTransform.sizeDelta = new Vector2(700f, 26f);
            heading.color = Gold;

            _statsTiles = NewRow("Tiles", sheet, 10f);
            _statsTiles.sizeDelta = new Vector2(700f, 82f);

            var rule = NewBox("Rule", sheet, new Color(1f, 1f, 1f, 0.10f));
            rule.sizeDelta = new Vector2(700f, 2f);

            _statsRows = NewBox("Rows", sheet, Color.clear);
            _statsRows.sizeDelta = new Vector2(700f, 110f);
            _statsRows.GetComponent<Image>().raycastTarget = false;

            var rows = _statsRows.gameObject.AddComponent<VerticalLayoutGroup>();
            rows.childAlignment = TextAnchor.UpperCenter;
            rows.spacing = 4f;
            rows.childForceExpandWidth = false;
            rows.childForceExpandHeight = false;
            rows.childControlWidth = false;
            rows.childControlHeight = false;

            _statsEmpty = NewText("StatsEmpty", sheet, string.Empty, 19f);
            _statsEmpty.rectTransform.sizeDelta = new Vector2(700f, 26f);

            _statsPanel.SetActive(false);
        }

        /// <summary>
        /// Shows the figures and hides the machine, or puts it back. No pull is
        /// touched either way -- the record lives on the server, and this only
        /// decides what is drawn. Refused mid-spin the same way a second pull is:
        /// the reels are already answering a question, and switching the sheet in
        /// over them would not stop that.
        /// </summary>
        private static void ToggleStats()
        {
            if (_statsPanel == null || _machine == null || ReelView.Spinning)
            {
                return;
            }

            var showing = !_statsPanel.activeSelf;

            _statsPanel.SetActive(showing);
            _machine.gameObject.SetActive(!showing);

            if (showing)
            {
                Populate(SlotApi.Stats());
            }
        }

        private static void HideStats()
        {
            if (_statsPanel != null && _statsPanel.activeSelf)
            {
                _statsPanel.SetActive(false);

                if (_machine != null)
                {
                    _machine.gameObject.SetActive(true);
                }
            }
        }

        private static void Populate(JObject stats)
        {
            Clear(_statsTiles);
            Clear(_statsRows);
            _statsEmpty.text = string.Empty;

            if (stats == null)
            {
                _statsEmpty.text = "No answer from the server.";
                _statsEmpty.color = Bad;
                return;
            }

            int GetInt(string name) => stats[name]?.ToObject<int>() ?? 0;
            double GetDouble(string name) => stats[name]?.ToObject<double>() ?? 0d;

            var pulls = GetInt("PullsPlayed");
            if (pulls == 0)
            {
                _statsEmpty.text = "No pulls yet.";
                _statsEmpty.color = Dim;
                return;
            }

            var wins = GetInt("Wins");
            var losses = GetInt("Losses");
            var rate = 100.0 * wins / pulls;

            Tile(pulls.ToString("N0"), "pulls", Ink);
            Tile($"{wins:N0}-{losses:N0}", "w-l", Ink);

            // Not coloured good or bad, unlike Blackjack's win rate: a slot's hit
            // frequency is a property of its paytable, not a thing a normal player
            // is expected to clear half the time, so there is no honest threshold
            // to judge it against.
            Tile($"{rate:F0}%", "hit rate", Ink);

            Tile($"{GetDouble("BestMultiple"):F0}x", "best pull", Gold);
            Tile(GetInt("Jackpots").ToString("N0"), "jackpots", Gold);
            Tile($"{GetInt("CurrentStreak"):N0} / {GetInt("BestStreak"):N0}", "streak / best", Ink);

            var byCurrency = stats["ByCurrency"] as JObject;
            if (byCurrency == null || !byCurrency.HasValues)
            {
                _statsEmpty.text = "Nothing staked yet.";
                _statsEmpty.color = Dim;
                return;
            }

            MoneyRow(string.Empty, "staked", "returned", "net", Dim);

            foreach (var entry in byCurrency.Properties())
            {
                var staked = entry.Value["Wagered"]?.ToObject<long>() ?? 0;
                var back = entry.Value["Returned"]?.ToObject<long>() ?? 0;
                var net = entry.Value["Net"]?.ToObject<long>() ?? (back - staked);

                MoneyRow(
                    Short(entry.Name),
                    staked.ToString("N0"),
                    back.ToString("N0"),
                    (net > 0 ? "+" : string.Empty) + net.ToString("N0"),
                    net > 0 ? Good : (net < 0 ? Bad : Dim));
            }
        }

        /// <summary>One figure with its name under it.</summary>
        private static void Tile(string figure, string caption, Color colour)
        {
            var tile = NewBox("Tile", _statsTiles, new Color(1f, 1f, 1f, 0.04f));
            tile.sizeDelta = new Vector2(110f, 78f);

            var image = tile.GetComponent<Image>();
            image.sprite = Textures.RoundedBox(
                8, new Color(1f, 1f, 1f, 0.04f), new Color(1f, 1f, 1f, 0.07f), 2);
            image.type = Image.Type.Sliced;

            var inner = tile.gameObject.AddComponent<VerticalLayoutGroup>();
            inner.childAlignment = TextAnchor.MiddleCenter;
            inner.spacing = 2f;
            inner.childForceExpandWidth = false;
            inner.childForceExpandHeight = false;
            inner.childControlWidth = false;
            inner.childControlHeight = false;

            var value = NewText("Value", tile, figure, 23f);
            value.rectTransform.sizeDelta = new Vector2(104f, 30f);
            value.color = colour;

            var label = NewText("Label", tile, caption, 13f);
            label.rectTransform.sizeDelta = new Vector2(104f, 18f);
            label.color = Dim;
        }

        /// <summary>
        /// One currency across four columns, matching the layout Blackjack's own
        /// stats sheet uses. Fixed widths, because numbers that do not line up are
        /// harder to read than numbers that are simply small.
        /// </summary>
        private static void MoneyRow(string wallet, string staked, string back, string net, Color netColour)
        {
            var row = NewRow("Row", _statsRows, 0f);
            row.sizeDelta = new Vector2(700f, 24f);

            var w = NewText("Wallet", row, wallet, 17f);
            w.rectTransform.sizeDelta = new Vector2(110f, 22f);
            w.alignment = TextAlignmentOptions.Left;
            w.color = Dim;

            var s = NewText("Staked", row, staked, 17f);
            s.rectTransform.sizeDelta = new Vector2(200f, 22f);
            s.alignment = TextAlignmentOptions.Right;
            s.color = Ink;

            var b = NewText("Returned", row, back, 17f);
            b.rectTransform.sizeDelta = new Vector2(200f, 22f);
            b.alignment = TextAlignmentOptions.Right;
            b.color = Ink;

            var n = NewText("Net", row, net, 17f);
            n.rectTransform.sizeDelta = new Vector2(180f, 22f);
            n.alignment = TextAlignmentOptions.Right;
            n.color = netColour;
        }

        /// <summary>Currency names, abbreviated the same way Blackjack's stats sheet does.</summary>
        private static string Short(string wallet) => wallet switch
        {
            "Roubles" => "RUB",
            "Dollars" => "USD",
            "Euros" => "EUR",
            _ => wallet?.ToUpperInvariant() ?? string.Empty,
        };

        private static void Clear(RectTransform parent)
        {
            if (parent == null)
            {
                return;
            }

            for (var i = parent.childCount - 1; i >= 0; i--)
            {
                UnityEngine.Object.Destroy(parent.GetChild(i).gameObject);
            }
        }

        private static RectTransform NewRow(string name, Transform parent, float spacing)
        {
            var row = NewBox(name, parent, Color.clear);
            row.GetComponent<Image>().raycastTarget = false;

            var layout = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = spacing;
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;
            layout.childControlWidth = false;
            layout.childControlHeight = false;

            return row;
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
            input.fontAsset = _font;
            input.pointSize = 22f;

            // Standard rather than IntegerNumber: the box holds thousands separators
            // now, and integer validation refuses to display them -- it would strip the
            // commas straight back out of anything written here. Digits are enforced by
            // the validator instead, which does the same job and leaves this code's own
            // formatting alone. Blackjack found this first.
            input.contentType = TMP_InputField.ContentType.Standard;
            input.onValidateInput = MoneyField.DigitsOnly;

            // Long enough for any stash, plus the separators that many digits attract.
            input.characterLimit = 19;
            input.restoreOriginalTextOnEscape = false;

            MoneyField.MakeCaretVisible(input, Gold);

            input.onValueChanged.AddListener(StakeTyped);

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
            // one of them would be a stake somebody spins without. The clamping lives
            // here rather than in StakeTyped, because clamping mid-keystroke means
            // somebody typing 50,000 has it snapped to the minimum the moment they have
            // typed a 5.
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

            if (_stake <= 0)
            {
                // The box was cleared, or holds something that is not a number. The
                // smallest spin the wallet takes is the one answer that is always valid
                // and never a surprise -- restoring the old value would mean the box
                // disagreeing with what was just typed into it.
                _stake = limits[0];
                SetStake();
                SetStatus($"A spin in {_wallet.ToLowerInvariant()} costs at least {limits[0]:N0}.");
                return;
            }

            var wanted = _stake;
            var ceiling = Ceiling(limits);
            var clamped = Math.Max(limits[0], Math.Min(ceiling, wanted));

            if (clamped != wanted)
            {
                SetStatus(
                    (ceiling == long.MaxValue
                        ? $"A spin in {_wallet.ToLowerInvariant()} costs at least {limits[0]:N0}."
                        : $"A spin in {_wallet.ToLowerInvariant()} costs {limits[0]:N0} to {ceiling:N0}.")
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

        /// <summary>
        /// The machine's ceiling, or none if the player has turned it off in F12.
        ///
        /// The server is the one that decides -- it is sent the switch with every spin
        /// and checks it -- but the panel has to agree, or the box would clamp a stake
        /// the machine would happily have taken.
        /// </summary>
        private static long Ceiling(long[] limits) =>
            SlotClientPlugin.NoStakeCap?.Value == true ? long.MaxValue : limits[1];

        private static void StepStake(int direction)
        {
            if (!Limits.TryGetValue(_wallet, out var l))
            {
                return;
            }

            _stake = Math.Max(l[0], Math.Min(Ceiling(l), _stake + (direction * l[2])));
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

        /// <summary>
        /// Every keystroke: the number is regrouped and the caret put back where the
        /// typist thinks it is.
        ///
        /// Nothing is clamped here. Clamping as you type means somebody reaching for
        /// 50,000 has it snapped to the minimum the instant they have typed a 5. The
        /// ends are applied when the box is left, in <see cref="TypedStake"/>.
        /// </summary>
        private static void StakeTyped(string typed)
        {
            if (_rewriting)
            {
                return;
            }

            _rewriting = true;
            _stake = MoneyField.Reformat(_stakeInput, typed);
            _rewriting = false;
        }

        private static void SetStake()
        {
            if (_stakeInput != null)
            {
                // SetTextWithoutNotify: assigning .text raises the change handlers, and
                // a setter that calls the handler that calls the setter is a loop
                // waiting for an excuse.
                _rewriting = true;
                _stakeInput.SetTextWithoutNotify(MoneyField.Format(_stake));
                _rewriting = false;
            }

            if (_walletLabel != null)
            {
                _walletLabel.text = _wallet.ToUpperInvariant();
            }

            // Redrawn from whatever RefreshBalances last fetched -- switching currency
            // needs no round trip, since a single ping already brought back every
            // wallet's balance at once.
            SetBalanceLabel();
        }

        /// <summary>
        /// Announces the win, at a size and a colour that say how big it was.
        ///
        /// **A slot machine's whole job at this moment is to make the number felt.** The
        /// first version printed every win at the same 30pt gold, so ten times the stake
        /// and a thousand times it looked identical and the machine had no top end.
        ///
        /// The tiers are multiples of the stake, not absolute amounts, because the stake
        /// is three currencies and can now be anything at all -- 100,000 is a rounding
        /// error on an uncapped spin and a life-changing sum on a minimum one. What the
        /// player feels is the multiple.
        ///
        /// The size is set on a label with a fixed rect, so a very long number at 64pt
        /// would overflow rather than wrap: the rect is 920 wide, which fits
        /// "JACKPOT  +999,999,999" with room to spare.
        /// </summary>
        private static void SetPaid(long? paid, long stake = 0)
        {
            if (_paidLabel == null)
            {
                return;
            }

            StopRainbow();

            if (paid is null or 0)
            {
                _paidLabel.text = string.Empty;
                _paidLabel.fontSizeMax = QuietSize;
                _paidLabel.rectTransform.localScale = Vector3.one;
                return;
            }

            var won = paid.Value;

            // Guard the division rather than trusting the stake: a spin is never free,
            // but this is the one line where a zero would take the panel down.
            var multiple = stake > 0 ? (double)won / stake : 1d;

            var (word, size, colour) = multiple switch
            {
                >= 100d => ("JACKPOT", 64f, new Color(1.00f, 0.97f, 0.85f, 1f)),
                >= 20d => ("HUGE WIN", 54f, new Color(1.00f, 0.62f, 0.24f, 1f)),
                >= 5d => ("BIG WIN", 44f, new Color(1.00f, 0.84f, 0.34f, 1f)),
                >= 1d => ("WIN", 36f, Gold),
                _ => (string.Empty, QuietSize, new Color(0.78f, 0.72f, 0.55f, 1f)),
            };

            // Same boundaries as the word above, on purpose -- one switch to retune
            // if the tiers ever move, rather than two that can quietly disagree.
            var cue = multiple switch
            {
                >= 100d => Cue.SlotJackpot,
                >= 20d => Cue.SlotHugeWin,
                >= 5d => Cue.SlotBigWin,
                >= 1d => Cue.SlotWin,
                _ => (Cue?)null,
            };

            // One space, and no plus sign on a tiered win. "WIN   +17,331" reads as two
            // separate things that happen to be near each other; "WIN 17,331" reads as
            // a sentence, which is what it is.
            _paidLabel.text = string.IsNullOrEmpty(word) ? $"+{won:N0}" : $"{word} {won:N0}";
            _paidLabel.fontSizeMax = size;
            _paidLabel.color = colour;

            if (multiple >= RainbowFrom && SlotClientPlugin.Instance != null)
            {
                _rainbow = SlotClientPlugin.Instance.StartCoroutine(Rainbow());
            }

            // Anything worth a word gets a pop as well. A number that simply appears is
            // a number the eye has already finished reading.
            if (!string.IsNullOrEmpty(word) && SlotClientPlugin.Instance != null)
            {
                if (_pop != null)
                {
                    SlotClientPlugin.Instance.StopCoroutine(_pop);
                }

                _pop = SlotClientPlugin.Instance.StartCoroutine(Pop());

                // Alongside the pop, not before it: the sound and the banner's own
                // bounce are the same event and should start on the same frame.
                if (cue.HasValue)
                {
                    SoundBoard.Play(cue.Value);
                }
            }
            else
            {
                _paidLabel.rectTransform.localScale = Vector3.one;
            }
        }

        private static void StopRainbow()
        {
            if (_rainbow != null && SlotClientPlugin.Instance != null)
            {
                SlotClientPlugin.Instance.StopCoroutine(_rainbow);
            }

            _rainbow = null;
        }

        /// <summary>
        /// Runs a band of colour along the banner, a letter at a time.
        ///
        /// TMP colours a label as a whole, so this reaches past that and writes the four
        /// vertex colours of every visible glyph itself, giving each one a hue a little
        /// further round the wheel than the last. Advance all of them together every
        /// frame and the band travels along the word.
        ///
        /// `ForceMeshUpdate` is called **once**, not per frame. Per frame it would also
        /// re-run auto-sizing, and a banner that resizes itself sixty times a second
        /// hunts visibly for a font size. After that only the colours are pushed, which
        /// is what <see cref="TMP_VertexDataUpdateFlags.Colors32"/> is for.
        /// </summary>
        private static IEnumerator Rainbow()
        {
            var label = _paidLabel;
            label.ForceMeshUpdate();

            var info = label.textInfo;
            var characters = info.characterCount;

            while (label != null && !string.IsNullOrEmpty(label.text))
            {
                for (var i = 0; i < characters; i++)
                {
                    var character = info.characterInfo[i];

                    if (!character.isVisible)
                    {
                        continue;
                    }

                    // Saturation held back from full: a pure spectrum on a dark cabinet
                    // reads as a test pattern rather than as gold going strange.
                    var hue = Mathf.Repeat((Time.unscaledTime * RainbowSpeed) + (i * RainbowSpread), 1f);
                    var colour = (Color32)Color.HSVToRGB(hue, 0.62f, 1f);

                    var colours = info.meshInfo[character.materialReferenceIndex].colors32;
                    var vertex = character.vertexIndex;

                    colours[vertex] = colour;
                    colours[vertex + 1] = colour;
                    colours[vertex + 2] = colour;
                    colours[vertex + 3] = colour;
                }

                label.UpdateVertexData(TMP_VertexDataUpdateFlags.Colors32);
                yield return null;
            }

            _rainbow = null;
        }

        /// <summary>
        /// Overshoots and settles, like the reels do. 0.28s, which is long enough to
        /// register and short enough not to be in the way of the next spin.
        /// </summary>
        private static IEnumerator Pop()
        {
            const float seconds = 0.28f;
            var rect = _paidLabel.rectTransform;

            for (var t = 0f; t < seconds; t += Time.unscaledDeltaTime)
            {
                var u = t / seconds;

                // Up past full size, then back to it.
                var scale = u < 0.55f
                    ? Mathf.Lerp(0.55f, 1.14f, Mathf.SmoothStep(0f, 1f, u / 0.55f))
                    : Mathf.Lerp(1.14f, 1f, Mathf.SmoothStep(0f, 1f, (u - 0.55f) / 0.45f));

                rect.localScale = new Vector3(scale, scale, 1f);
                yield return null;
            }

            rect.localScale = Vector3.one;
            _pop = null;
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
