using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Casino.Shared;
using Newtonsoft.Json.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace HorseRacing.Client
{
    /// <summary>
    /// Everything <see cref="RacePanel"/> draws with.
    ///
    /// Split from the logic half because the two are read for different reasons: one
    /// answers "what happens to the money", the other "where does the button go", and a
    /// single fifteen-hundred-line file makes the first question expensive to answer.
    /// Same class, two files -- nothing here is reachable from outside the panel.
    ///
    /// ## Everything is placed from the band table, and nothing from its neighbour
    ///
    /// The first version of this file positioned the pair-bet row relative to *where
    /// the runner loop happened to finish*, and the status and result lines relative to
    /// the bottom of the frame. Two coordinate systems growing towards each other: with
    /// eight runners they overlapped by 18 pixels and the word NOTHING was drawn
    /// through the EXACTA/QUINELLA selector.
    ///
    /// So every element now takes its vertical position from a named constant below,
    /// measured down from the top of the frame, and never from the element before it.
    /// Adding a ninth runner moves nothing except the rows themselves -- it makes the
    /// board overflow its own band, which is visible and local, rather than silently
    /// shunting a control into a label three bands away.
    /// </summary>
    internal static partial class RacePanel
    {
        private const float FrameWidth = 1520f;
        private const float FrameHeight = 900f;

        // --- the band table. Distance down from the top edge of the frame. ---------
        //
        // Checked for overlaps arithmetically rather than by looking at it, which is
        // the thing this environment cannot do. Adding a course tab pushed every band
        // below it down; the track lost 20 units to pay for it.
        private const float TabsTop = 84f;
        private const float TabsHeight = 34f;

        private const float TrackTop = 128f;
        private const float TrackHeight = 268f;

        private const float ColumnHeaderTop = 406f;
        private const float RowsTop = 430f;
        private const float RowHeight = 27f;

        private const float PairsTop = 654f;
        private const float ResultTop = 722f;
        private const float StatusTop = 758f;
        private const float ControlsTop = 794f;

        // --- horizontal: the board on the left, the slip on the right --------------
        private const float Margin = 26f;
        private const float BoardWidth = 890f;
        private const float SlipWidth = 540f;

        // Columns inside the board, as offsets from its left edge.
        private const float ColName = 4f;
        private const float ColChance = 306f;
        private const float ColFirstPrice = 400f;
        private const float PriceWidth = 142f;
        private const float PriceGap = 8f;

        /// <summary>
        /// Builds the panel. Called once; every later open is a SetActive and a fade.
        /// </summary>
        private static void Build()
        {
            var clock = System.Diagnostics.Stopwatch.StartNew();

            _font = FindFont();

            _root = new GameObject(RootName, typeof(RectTransform), typeof(Canvas), typeof(CanvasGroup));
            UnityEngine.Object.DontDestroyOnLoad(_root);

            var canvas = _root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            // 30000, the same as every other table. Everything covers the lobby -- see
            // the layer table in the root CLAUDE.md, which is what makes the
            // transitions work.
            canvas.sortingOrder = 30000;

            _root.AddComponent<GraphicRaycaster>();

            // Matched to Roulette's and Slots' scaler rather than left at
            // ConstantPixelSize, which is what this was first written with. Constant
            // pixels means a 1520x900 panel stays 1520x900 actual pixels, so on a 1440p
            // or 4K monitor it shrinks into the middle of the screen while every other
            // table scales up around it. Matching height against a 1080 reference makes
            // the whole casino behave the same way on every display.
            var scaler = _root.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 1f;

            var backdrop = New("Backdrop", _root.transform);
            Stretch(backdrop);
            var vignette = backdrop.gameObject.AddComponent<Image>();
            vignette.sprite = Textures.Vignette(new Color(0f, 0f, 0f, 0.92f));
            vignette.color = Color.white;

            var frame = New("Frame", _root.transform);
            frame.anchorMin = new Vector2(0.5f, 0.5f);
            frame.anchorMax = new Vector2(0.5f, 0.5f);
            frame.pivot = new Vector2(0.5f, 0.5f);
            frame.anchoredPosition = Vector2.zero;
            frame.sizeDelta = new Vector2(FrameWidth, FrameHeight);

            var frameImage = frame.gameObject.AddComponent<Image>();
            frameImage.sprite = Textures.RoundedBox(16, Panel, Gold, 2);
            frameImage.type = Image.Type.Sliced;

            BuildHeader(frame);
            BuildCourseTabs(frame);
            BuildTrack(frame);
            BuildBoard(frame);
            BuildSlip(frame);
            BuildPairs(frame);
            BuildControls(frame);

            RenderBoard();
            RenderSlip();

            RaceClientPlugin.Log.LogInfo($"[Races] panel built in {clock.ElapsedMilliseconds}ms");
        }

        private static void BuildHeader(RectTransform frame)
        {
            var title = Text("Title", frame, "HORSE RACING", 30f, Gold, TextAlignmentOptions.Left);
            TopLeft(title.rectTransform, Margin, 18f, 420f, 40f);
            title.fontStyle = FontStyles.Bold;

            _blurb = Text(
                "Blurb",
                frame,
                string.Empty,
                15f,
                new Color(Ink.r, Ink.g, Ink.b, 0.55f),
                TextAlignmentOptions.Left);
            TopLeft(_blurb.rectTransform, Margin + 2f, 60f, 1000f, 22f);

            SetBlurb();

            var close = MakeButton("Close", frame, "CLOSE", 110f, 36f, Close);
            TopRight(close, Margin, 18f);

            _balance = Text("Balance", frame, string.Empty, 20f, Ink, TextAlignmentOptions.Right);
            TopRight(_balance.rectTransform, Margin + 126f, 22f, 300f, 32f);
        }

        /// <summary>
        /// The line under the title: what this course is, and what the house takes.
        ///
        /// The takeout comes from the server with the rest of the board rather than
        /// being written here, for the same reason the prices do -- a number the panel
        /// owns is a number that can drift from the one the table actually charges.
        /// </summary>
        private static void SetBlurb()
        {
            if (_blurb == null)
            {
                return;
            }

            var takeout = _card?.Value<double?>("Takeout") ?? 0d;
            var course = Course;
            var what = course?.Value<string>("Blurb") ?? "Eight runners.";

            _blurb.text = $"{what}   {takeout:P2} to the house on every bet, the same at every course.";
        }

        private static void BuildTrack(RectTransform frame)
        {
            _trackHolder = New("Track", frame);
            _trackHolder.anchorMin = new Vector2(0f, 1f);
            _trackHolder.anchorMax = new Vector2(1f, 1f);
            _trackHolder.pivot = new Vector2(0.5f, 1f);
            _trackHolder.anchoredPosition = new Vector2(0f, -TrackTop);
            _trackHolder.sizeDelta = new Vector2(-(Margin * 2f), TrackHeight);

            BuildTrackForCourse();
        }

        /// <summary>
        /// Draws the course currently selected, replacing whatever was there.
        ///
        /// The shape can change -- the dash is a straight and the other two are ovals --
        /// so this rebuilds rather than repositions.
        ///
        /// **A layout pass is forced first**, because the oval is laid out from the
        /// holder's own rect and a RectTransform that has not been through one reports
        /// a size of zero. A zero-radius oval draws eight horses in a heap at the
        /// centre and nothing about it looks like a layout problem.
        /// </summary>
        private static void BuildTrackForCourse()
        {
            if (_trackHolder == null)
            {
                return;
            }

            var course = Course;

            if (course == null)
            {
                return;
            }

            var runners = new List<KeyValuePair<int, string>>();

            if (course["Runners"] is JArray list)
            {
                foreach (var runner in list)
                {
                    runners.Add(new KeyValuePair<int, string>(
                        runner.Value<int?>("Number") ?? 0,
                        runner.Value<string>("Name") ?? string.Empty));
                }
            }

            LayoutRebuilder.ForceRebuildLayoutImmediate(_trackHolder);

            var shape = string.Equals(
                course.Value<string>("Shape"), "Oval", StringComparison.OrdinalIgnoreCase)
                ? Shape.Oval
                : Shape.Straight;

            TrackView.Build(
                _trackHolder,
                shape,
                course.Value<int?>("Laps") ?? 1,
                (float)(course.Value<double?>("RunSeconds") ?? 6d),
                runners,
                _font);
        }

        /// <summary>
        /// One tab per course, left to right, shortest first.
        ///
        /// Built into a holder at a fixed band and refilled by
        /// <see cref="RenderCourseTabs"/>, so the selected one can change appearance
        /// without the bands below moving.
        /// </summary>
        private static void BuildCourseTabs(RectTransform frame)
        {
            _tabsHolder = New("Tabs", frame);
            TopLeft(_tabsHolder, Margin, TabsTop, FrameWidth - (Margin * 2f), TabsHeight);

            RenderCourseTabs();
        }

        private static void RenderCourseTabs()
        {
            if (_tabsHolder == null)
            {
                return;
            }

            foreach (Transform child in _tabsHolder)
            {
                UnityEngine.Object.Destroy(child.gameObject);
            }

            var courses = Courses;
            var x = 0f;

            for (var i = 0; i < courses.Count; i++)
            {
                var course = courses[i] as JObject;

                if (course == null)
                {
                    continue;
                }

                var on = i == _course;
                var index = i;

                var name = course.Value<string>("Name") ?? "COURSE";
                var distance = course.Value<string>("Distance") ?? string.Empty;

                var rect = New($"Tab{i}", _tabsHolder);
                TopLeft(rect, x, 0f, 260f, TabsHeight);

                var image = rect.gameObject.AddComponent<Image>();
                image.sprite = Textures.ButtonFace(
                    6,
                    on ? Gold : Slate,
                    on ? new Color(Gold.r * 0.78f, Gold.g * 0.78f, Gold.b * 0.78f, 1f)
                       : new Color(0.09f, 0.10f, 0.09f, 1f),
                    on ? Gold : new Color(0f, 0f, 0f, 0.45f),
                    on ? 2 : 1);
                image.type = Image.Type.Sliced;

                var label = Text(
                    "Label", rect, $"{name}   {distance}", 15f,
                    on ? new Color(0.1f, 0.1f, 0.1f, 1f) : Ink,
                    TextAlignmentOptions.Center);
                Stretch(label.rectTransform);
                label.fontStyle = on ? FontStyles.Bold : FontStyles.Normal;

                var button = rect.gameObject.AddComponent<Button>();
                button.targetGraphic = image;
                button.onClick.AddListener(() => SwitchCourse(index));

                x += 272f;
            }
        }

        private static void BuildBoard(RectTransform frame)
        {
            var faint = new Color(Ink.r, Ink.g, Ink.b, 0.42f);

            var runner = Text("HeaderRunner", frame, "RUNNER", 13f, faint, TextAlignmentOptions.Left);
            TopLeft(runner.rectTransform, Margin + ColName, ColumnHeaderTop, 300f, 20f);

            var form = Text("HeaderForm", frame, "FORM", 13f, faint, TextAlignmentOptions.Left);
            TopLeft(form.rectTransform, Margin + ColChance, ColumnHeaderTop, 80f, 20f);

            // Positioned over the columns they label rather than spaced out inside one
            // string. The single-string version lined up in a monospace preview and
            // then sat a hundred pixels left of its own buttons in the game's
            // proportional font, which is what it looked like in the first screenshot.
            var kinds = new[] { "WIN", "PLACE", "SHOW" };

            for (var i = 0; i < kinds.Length; i++)
            {
                var label = Text("Header" + kinds[i], frame, kinds[i], 13f, faint, TextAlignmentOptions.Center);
                TopLeft(
                    label.rectTransform,
                    Margin + ColFirstPrice + (i * (PriceWidth + PriceGap)),
                    ColumnHeaderTop,
                    PriceWidth,
                    20f);
            }

            _boardHolder = New("Board", frame);
            TopLeft(_boardHolder, Margin, RowsTop, BoardWidth, RowHeight * 8f);
        }

        private static void BuildSlip(RectTransform frame)
        {
            var header = Text("SlipHeader", frame, "YOUR SLIP", 15f, Gold, TextAlignmentOptions.Left);
            TopRight(header.rectTransform, Margin + SlipWidth - 200f, ColumnHeaderTop, 200f, 20f);
            header.fontStyle = FontStyles.Bold;

            var clear = MakeButton("ClearSlip", frame, "CLEAR", 90f, 24f, ClearSlip);
            TopRight(clear, Margin, ColumnHeaderTop - 3f);

            var back = New("SlipBack", frame);
            TopRight(back, Margin, RowsTop, SlipWidth, PairsTop - RowsTop - 14f);

            var backImage = back.gameObject.AddComponent<Image>();
            backImage.sprite = Textures.RoundedBox(10, Slate, new Color(0f, 0f, 0f, 0.5f), 1);
            backImage.type = Image.Type.Sliced;

            _slipHolder = New("Slip", back);
            _slipHolder.anchorMin = new Vector2(0f, 1f);
            _slipHolder.anchorMax = new Vector2(1f, 1f);
            _slipHolder.pivot = new Vector2(0.5f, 1f);
            _slipHolder.anchoredPosition = new Vector2(0f, -10f);
            _slipHolder.sizeDelta = new Vector2(-18f, PairsTop - RowsTop - 40f);

            _slipTotal = Text("SlipTotal", frame, string.Empty, 16f, Ink, TextAlignmentOptions.Right);
            TopRight(_slipTotal.rectTransform, Margin + 4f, PairsTop - 8f, SlipWidth, 24f);
        }

        /// <summary>
        /// The pair-bet selector, in a band of its own.
        ///
        /// 56 exactas and 28 quinellas do not fit on a board beside the singles, and a
        /// grid of 84 two-digit buttons is not something anybody reads -- so the pair is
        /// chosen with two steppers and the price for the current pair is shown live on
        /// the two buttons beside them. The whole board still travels from the server;
        /// this only picks which spot on it is being looked at.
        ///
        /// Built once here, into a holder at a fixed band. <see cref="RenderPairs"/>
        /// refills that holder as the steppers move, so the selector can redraw without
        /// the rest of the board moving underneath it.
        /// </summary>
        private static void BuildPairs(RectTransform frame)
        {
            var header = Text(
                "PairHeader", frame, "EXACTA  /  QUINELLA", 13f,
                new Color(Ink.r, Ink.g, Ink.b, 0.42f), TextAlignmentOptions.Left);
            TopLeft(header.rectTransform, Margin + ColName, PairsTop, 300f, 20f);

            _pairsHolder = New("Pairs", frame);
            TopLeft(_pairsHolder, Margin, PairsTop + 24f, BoardWidth, 34f);
        }

        private static void BuildControls(RectTransform frame)
        {
            _result = Text("Result", frame, string.Empty, 24f, Ink, TextAlignmentOptions.Left);
            TopLeft(_result.rectTransform, Margin + 4f, ResultTop, 900f, 32f);
            _result.fontStyle = FontStyles.Bold;

            _status = Text("Status", frame, string.Empty, 16f, Ink, TextAlignmentOptions.Left);
            TopLeft(_status.rectTransform, Margin + 4f, StatusTop, 900f, 24f);

            var stakeLabel = Text(
                "StakeLabel", frame, "STAKE PER BET", 13f,
                new Color(Ink.r, Ink.g, Ink.b, 0.5f), TextAlignmentOptions.Left);
            TopLeft(stakeLabel.rectTransform, Margin + 4f, ControlsTop + 12f, 150f, 18f);

            var down = MakeButton("StakeDown", frame, "-", 38f, 34f, () => StepStakeBy(-1));
            TopLeft(down, Margin + 154f, ControlsTop);

            _stakeField = MakeStakeField(frame, Margin + 198f, ControlsTop);

            var up = MakeButton("StakeUp", frame, "+", 38f, 34f, () => StepStakeBy(1));
            TopLeft(up, Margin + 362f, ControlsTop);

            var wallet = MakeButton("Wallet", frame, string.Empty, 130f, 34f, NextWallet);
            TopLeft(wallet, Margin + 414f, ControlsTop);
            _walletLabel = wallet.GetComponentInChildren<TextMeshProUGUI>();

            var run = MakeButton("Run", frame, "RUN THE RACE", 240f, 46f, Go);
            TopRight(run, Margin, ControlsTop - 6f);
            _runButton = run.GetComponent<Button>();

            SetWalletLabel();
            SetStakeText();
        }

        // ------------------------------------------------------------------ rendering

        /// <summary>
        /// Draws the card: one row per runner, with its three single-runner prices.
        /// </summary>
        private static void RenderBoard()
        {
            if (_boardHolder == null)
            {
                return;
            }

            foreach (Transform child in _boardHolder)
            {
                UnityEngine.Object.Destroy(child.gameObject);
            }

            if (_card?["Runners"] is not JArray runners)
            {
                return;
            }

            var y = 0f;

            foreach (var runner in runners)
            {
                var number = runner.Value<int?>("Number") ?? 0;
                var name = runner.Value<string>("Name") ?? string.Empty;
                var chance = runner.Value<double?>("Chance") ?? 0d;

                var label = Text($"Runner{number}", _boardHolder,
                    $"{number}  {name}", 16f, Ink, TextAlignmentOptions.Left);
                TopLeft(label.rectTransform, ColName, y, 300f, RowHeight - 3f);

                var form = Text($"Form{number}", _boardHolder,
                    $"{chance:P1}", 13f, new Color(Ink.r, Ink.g, Ink.b, 0.40f), TextAlignmentOptions.Left);
                TopLeft(form.rectTransform, ColChance, y, 80f, RowHeight - 3f);

                for (var i = 0; i < 3; i++)
                {
                    var kind = i switch { 0 => "Win", 1 => "Place", _ => "Show" };
                    MakeSpot(
                        _boardHolder,
                        kind,
                        number,
                        0,
                        ColFirstPrice + (i * (PriceWidth + PriceGap)),
                        y,
                        PriceWidth);
                }

                y += RowHeight;
            }

            RenderPairs();
        }

        private static void RenderPairs()
        {
            if (_pairsHolder == null)
            {
                return;
            }

            // A runner cannot beat itself into second. Rather than refusing the click
            // and saying so, the second stepper simply steps past the first -- there is
            // no state in which the pair is illegal, so there is no error to explain.
            if (_pairSecond == _pairFirst)
            {
                _pairSecond = Wrap(_pairFirst + 1);
            }

            foreach (Transform child in _pairsHolder)
            {
                UnityEngine.Object.Destroy(child.gameObject);
            }

            MakeStepper(_pairsHolder, "1st", _pairFirst, ColName, value =>
            {
                _pairFirst = Wrap(value);
                RenderPairs();
            });

            MakeStepper(_pairsHolder, "2nd", _pairSecond, ColName + 296f, value =>
            {
                _pairSecond = Wrap(value);
                RenderPairs();
            });

            MakeSpot(
                _pairsHolder, "Exacta", _pairFirst, _pairSecond,
                ColFirstPrice + (PriceWidth + PriceGap) - 42f, 0f, PriceWidth + 42f);

            MakeSpot(
                _pairsHolder, "Quinella", Math.Min(_pairFirst, _pairSecond), Math.Max(_pairFirst, _pairSecond),
                ColFirstPrice + (2f * (PriceWidth + PriceGap)), 0f, PriceWidth + 42f);
        }

        private static int _pairFirst = 1;
        private static int _pairSecond = 2;

        private static int Wrap(int runner)
        {
            var count = (_card?["Runners"] as JArray)?.Count ?? 8;

            if (runner < 1)
            {
                return count;
            }

            return runner > count ? 1 : runner;
        }

        private static void RenderSlip()
        {
            if (_slipHolder == null)
            {
                return;
            }

            foreach (Transform child in _slipHolder)
            {
                UnityEngine.Object.Destroy(child.gameObject);
            }

            var y = 0f;
            var total = 0L;

            foreach (var bet in _slip)
            {
                total += bet.Stake;

                var line = New("Line", _slipHolder);
                line.anchorMin = new Vector2(0f, 1f);
                line.anchorMax = new Vector2(1f, 1f);
                line.pivot = new Vector2(0.5f, 1f);
                line.anchoredPosition = new Vector2(0f, -y);
                line.sizeDelta = new Vector2(0f, 24f);

                var text = Text("Text", line, Describe(bet), 15f, Ink, TextAlignmentOptions.Left);
                Stretch(text.rectTransform);
                text.rectTransform.offsetMin = new Vector2(10f, 0f);
                text.rectTransform.offsetMax = new Vector2(-10f, 0f);

                y += 25f;
            }

            if (_slipTotal != null)
            {
                _slipTotal.text = _slip.Count == 0
                    ? "Nothing on the slip."
                    : $"{_slip.Count} bet{(_slip.Count == 1 ? string.Empty : "s")}   total {Sign()}{Money(total)}";
            }
        }

        private static string Describe(SlipBet bet)
        {
            var price = PriceOf(bet.Kind, bet.First, bet.Second);

            var what = bet.Second > 0
                ? $"{bet.Kind.ToUpperInvariant()} {bet.First}-{bet.Second}"
                : $"{bet.Kind.ToUpperInvariant()} {bet.First} {RunnerName(bet.First)}";

            return $"{what}   {Sign()}{Money(bet.Stake)}  @ {Odds(price)}";
        }

        // ------------------------------------------------------------------- widgets

        /// <summary>One clickable spot on the board: its price, and whether it is on.</summary>
        private static RectTransform MakeSpot(
            RectTransform parent, string kind, int first, int second, float x, float y, float width)
        {
            var price = PriceOf(kind, first, second);
            var on = _slip.Any(b => b.Kind == kind && b.First == first && b.Second == second);

            var rect = New($"{kind}{first}_{second}", parent);
            TopLeft(rect, x, y, width, RowHeight - 3f);

            var image = rect.gameObject.AddComponent<Image>();
            image.sprite = Textures.ButtonFace(
                6,
                on ? Gold : Slate,
                on ? new Color(Gold.r * 0.8f, Gold.g * 0.8f, Gold.b * 0.8f, 1f) : new Color(0.10f, 0.11f, 0.10f, 1f),
                on ? Gold : new Color(0f, 0f, 0f, 0.4f),
                1);
            image.type = Image.Type.Sliced;

            // The pair spots say what they are; the three single-runner columns are
            // already labelled by the header above them and would only repeat it.
            var caption = second > 0
                ? $"{kind.ToUpperInvariant()} {first}-{second}    {Odds(price)}"
                : Odds(price);

            var label = Text("Label", rect, caption, 15f,
                on ? new Color(0.1f, 0.1f, 0.1f, 1f) : Ink, TextAlignmentOptions.Center);
            Stretch(label.rectTransform);

            var button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            button.onClick.AddListener(() => Toggle(kind, first, second));

            return rect;
        }

        private static RectTransform MakeStepper(
            RectTransform parent, string caption, int value, float x, Action<int> set)
        {
            var rect = New($"Stepper{caption}", parent);
            TopLeft(rect, x, 0f, 282f, RowHeight - 3f);

            var label = Text("Caption", rect, caption, 13f,
                new Color(Ink.r, Ink.g, Ink.b, 0.5f), TextAlignmentOptions.Left);
            TopLeft(label.rectTransform, 0f, 4f, 30f, 18f);

            var down = MakeButton($"{caption}Down", rect, "-", 26f, 24f, () => set(value - 1));
            TopLeft(down, 32f, 0f);

            // Wide enough for the longest name on the card with its number in front.
            // At 100 units "NIGHT RAIDER" came out as "NIGHT RAIDE", which reads as a
            // typo in the card rather than as a box that is too small.
            var shown = Text("Value", rect, $"{value}  {RunnerName(value)}", 15f, Ink, TextAlignmentOptions.Center);
            TopLeft(shown.rectTransform, 62f, 0f, 186f, 24f);

            var up = MakeButton($"{caption}Up", rect, "+", 26f, 24f, () => set(value + 1));
            TopLeft(up, 252f, 0f);

            return rect;
        }

        private static TMP_InputField MakeStakeField(RectTransform parent, float x, float y)
        {
            var rect = New("Stake", parent);
            TopLeft(rect, x, y, 158f, 34f);

            var image = rect.gameObject.AddComponent<Image>();
            image.sprite = Textures.RoundedBox(6, Slate, new Color(0f, 0f, 0f, 0.5f), 1);
            image.type = Image.Type.Sliced;

            var area = New("TextArea", rect);
            Stretch(area);
            area.offsetMin = new Vector2(8f, 2f);
            area.offsetMax = new Vector2(-8f, -2f);
            area.gameObject.AddComponent<RectMask2D>();

            var text = Text("Text", area, string.Empty, 17f, Ink, TextAlignmentOptions.Center);
            Stretch(text.rectTransform);

            var input = rect.gameObject.AddComponent<TMP_InputField>();
            input.textViewport = area;
            input.textComponent = text;
            input.contentType = TMP_InputField.ContentType.Standard;
            input.lineType = TMP_InputField.LineType.SingleLine;
            input.onValidateInput += MoneyField.DigitsOnly;

            // Without this the caret is invisible on this font at this size, which
            // reads as a box that cannot be typed in. Slots hit exactly this and it
            // took a round trip with the player to work out what they were seeing.
            MoneyField.MakeCaretVisible(input, Gold);

            input.onEndEdit.AddListener(typed =>
            {
                _stake = MoneyField.Reformat(input, typed);
                ClampStake();
                SetStakeText();
                RestakeSlip();
            });

            return input;
        }

        private static RectTransform MakeButton(
            string name, Transform parent, string caption, float width, float height, Action onClick)
        {
            var rect = New(name, parent);
            rect.sizeDelta = new Vector2(width, height);

            var image = rect.gameObject.AddComponent<Image>();
            image.sprite = Textures.ButtonFace(6, Slate, new Color(0.09f, 0.10f, 0.09f, 1f), Gold, 1);
            image.type = Image.Type.Sliced;

            var label = Text("Label", rect, caption, 16f, Ink, TextAlignmentOptions.Center);
            Stretch(label.rectTransform);

            var button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            button.onClick.AddListener(() => onClick());

            return rect;
        }

        private static TextMeshProUGUI Text(
            string name, Transform parent, string content, float size, Color colour, TextAlignmentOptions align)
        {
            var rect = New(name, parent);

            var text = rect.gameObject.AddComponent<TextMeshProUGUI>();
            text.font = _font;
            text.fontSize = size;
            text.color = colour;
            text.alignment = align;
            text.text = content;
            text.raycastTarget = false;

            return text;
        }

        // ------------------------------------------------------------------ placement

        /// <summary>Places a rect by its top-left corner, measured from its parent's.</summary>
        private static void TopLeft(RectTransform rect, float x, float y, float width, float height)
        {
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.sizeDelta = new Vector2(width, height);
            rect.anchoredPosition = new Vector2(x, -y);
        }

        /// <summary>Places an already-sized rect by its top-left corner.</summary>
        private static void TopLeft(RectTransform rect, float x, float y)
        {
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(x, -y);
        }

        /// <summary>Places a rect by its top-right corner, x measured in from the right.</summary>
        private static void TopRight(RectTransform rect, float x, float y, float width, float height)
        {
            rect.anchorMin = new Vector2(1f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(1f, 1f);
            rect.sizeDelta = new Vector2(width, height);
            rect.anchoredPosition = new Vector2(-x, -y);
        }

        /// <summary>Places an already-sized rect by its top-right corner.</summary>
        private static void TopRight(RectTransform rect, float x, float y)
        {
            rect.anchorMin = new Vector2(1f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(1f, 1f);
            rect.anchoredPosition = new Vector2(-x, -y);
        }

        // -------------------------------------------------------------------- stake

        private static void StepStakeBy(int direction)
        {
            _stake += direction * StepStake();
            ClampStake();
            SetStakeText();
            RestakeSlip();
        }

        private static void ClampStake()
        {
            if (_stake < MinStake())
            {
                _stake = MinStake();
            }
        }

        private static void SetStakeText()
        {
            if (_stakeField != null)
            {
                _stakeField.text = MoneyField.Format(_stake);
            }
        }

        /// <summary>
        /// Puts the current stake on every bet already on the slip.
        ///
        /// The alternative -- leaving each bet at whatever the stake was when it was
        /// added -- makes a slip whose lines cannot be explained from anything on
        /// screen, and the first thing anybody does after building one is change the
        /// stake and wonder why the total did not move.
        /// </summary>
        private static void RestakeSlip()
        {
            foreach (var bet in _slip)
            {
                bet.Stake = _stake;
            }

            RenderSlip();
        }

        private static void NextWallet()
        {
            if (TrackView.IsRunning)
            {
                return;
            }

            var wallets = (_card?["Limits"] as JObject)?.Properties().Select(p => p.Name).ToList();

            if (wallets == null || wallets.Count == 0)
            {
                return;
            }

            var index = wallets.IndexOf(_wallet);
            _wallet = wallets[(index + 1) % wallets.Count];

            _stake = MinStake();
            ClampStake();
            SetStakeText();
            SetWalletLabel();
            RestakeSlip();
            RefreshBalance();
        }

        private static void SetWalletLabel()
        {
            if (_walletLabel != null)
            {
                _walletLabel.text = _wallet.ToUpperInvariant();
            }
        }

        // --------------------------------------------------------------------- fade

        /// <summary>
        /// Fades the whole panel, rather than switching it.
        ///
        /// The backdrop is nearly opaque, so toggling the canvas takes the screen from
        /// menu to table and back in a single frame -- which is what makes leaving feel
        /// like a jump cut. Every table in this casino settled on this and on the
        /// numbers below; this is a port rather than a fresh attempt.
        /// </summary>
        private static void FadeTo(float target, Action done)
        {
            var group = _root == null ? null : _root.GetComponent<CanvasGroup>();
            var host = RaceClientPlugin.Instance;

            if (group == null || host == null)
            {
                if (group != null)
                {
                    group.alpha = target;
                }

                done?.Invoke();
                return;
            }

            if (_fade != null)
            {
                host.StopCoroutine(_fade);
            }

            _fade = host.StartCoroutine(Fade(group, target, done));
        }

        private static IEnumerator Fade(CanvasGroup group, float target, Action done)
        {
            // A sixth of a second, linear, both directions -- the numbers Blackjack
            // settled on and every table since has kept, because that is the version
            // that was tried and found to read correctly.
            const float duration = 0.16f;

            var start = group.alpha;
            var elapsed = 0f;

            // Clicks stop landing the moment a close begins, so a button pressed during
            // the fade cannot fire at a table on its way out.
            group.blocksRaycasts = target > 0f;
            group.interactable = target > 0f;

            while (elapsed < duration)
            {
                // Unscaled: the menu is not necessarily running at a normal timescale,
                // and a fade that stalls with it would hang the panel open.
                elapsed += Time.unscaledDeltaTime;
                group.alpha = Mathf.Lerp(start, target, Mathf.Clamp01(elapsed / duration));
                yield return null;
            }

            group.alpha = target;
            _fade = null;

            done?.Invoke();
        }

        // ------------------------------------------------------------------ plumbing

        /// <summary>
        /// A font the game already has loaded.
        ///
        /// Taken from whatever TMP has in memory rather than loaded from disk: the menu
        /// is already using one, and a table that ships its own is a table whose text
        /// does not match the game around it.
        /// </summary>
        private static TMP_FontAsset FindFont()
        {
            var fonts = Resources.FindObjectsOfTypeAll<TMP_FontAsset>();

            foreach (var font in fonts)
            {
                if (font != null && font.name.IndexOf("bender", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return font;
                }
            }

            return fonts.Length > 0 ? fonts[0] : TMP_Settings.defaultFontAsset;
        }

        private static RectTransform New(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);

            return rect;
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }
    }
}
