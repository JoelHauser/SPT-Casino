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
    /// </summary>
    internal static partial class RacePanel
    {
        private const float FrameWidth = 1520f;
        private const float FrameHeight = 900f;
        private const float TrackHeight = 384f;

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
            _root.AddComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;

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
            BuildTrack(frame);
            BuildBoard(frame);
            BuildSlip(frame);
            BuildControls(frame);

            RenderBoard();
            RenderSlip();

            RaceClientPlugin.Log.LogInfo($"[Races] panel built in {clock.ElapsedMilliseconds}ms");
        }

        private static void BuildHeader(RectTransform frame)
        {
            var title = Text("Title", frame, "HORSE RACING", 30f, Gold, TextAlignmentOptions.Left);
            title.rectTransform.anchorMin = new Vector2(0f, 1f);
            title.rectTransform.anchorMax = new Vector2(0f, 1f);
            title.rectTransform.pivot = new Vector2(0f, 1f);
            title.rectTransform.anchoredPosition = new Vector2(26f, -18f);
            title.rectTransform.sizeDelta = new Vector2(420f, 40f);
            title.fontStyle = FontStyles.Bold;

            // The takeout, stated on the table. It comes from the server with the rest
            // of the board rather than being written here, for the same reason the
            // prices do: a number the panel owns is a number that can drift from the
            // one the table actually charges.
            var takeout = _card?.Value<double?>("Takeout") ?? 0d;

            var blurb = Text(
                "Blurb",
                frame,
                $"Eight runners. {takeout:P2} to the house on every bet, computed rather than measured.",
                15f,
                new Color(Ink.r, Ink.g, Ink.b, 0.55f),
                TextAlignmentOptions.Left);
            blurb.rectTransform.anchorMin = new Vector2(0f, 1f);
            blurb.rectTransform.anchorMax = new Vector2(0f, 1f);
            blurb.rectTransform.pivot = new Vector2(0f, 1f);
            blurb.rectTransform.anchoredPosition = new Vector2(28f, -54f);
            blurb.rectTransform.sizeDelta = new Vector2(760f, 22f);

            var close = MakeButton("Close", frame, "CLOSE", 110f, 36f, Close);
            close.anchorMin = new Vector2(1f, 1f);
            close.anchorMax = new Vector2(1f, 1f);
            close.pivot = new Vector2(1f, 1f);
            close.anchoredPosition = new Vector2(-26f, -18f);

            _balance = Text("Balance", frame, string.Empty, 20f, Ink, TextAlignmentOptions.Right);
            _balance.rectTransform.anchorMin = new Vector2(1f, 1f);
            _balance.rectTransform.anchorMax = new Vector2(1f, 1f);
            _balance.rectTransform.pivot = new Vector2(1f, 1f);
            _balance.rectTransform.anchoredPosition = new Vector2(-148f, -20f);
            _balance.rectTransform.sizeDelta = new Vector2(280f, 32f);
        }

        private static void BuildTrack(RectTransform frame)
        {
            _trackHolder = New("Track", frame);
            _trackHolder.anchorMin = new Vector2(0f, 1f);
            _trackHolder.anchorMax = new Vector2(1f, 1f);
            _trackHolder.pivot = new Vector2(0.5f, 1f);
            _trackHolder.anchoredPosition = new Vector2(0f, -84f);
            _trackHolder.sizeDelta = new Vector2(-52f, TrackHeight);

            var runners = new List<KeyValuePair<int, string>>();

            if (_card?["Runners"] is JArray list)
            {
                foreach (var runner in list)
                {
                    runners.Add(new KeyValuePair<int, string>(
                        runner.Value<int?>("Number") ?? 0,
                        runner.Value<string>("Name") ?? string.Empty));
                }
            }

            TrackView.Build(_trackHolder, runners, _font);
        }

        private static void BuildBoard(RectTransform frame)
        {
            var header = Text(
                "BoardHeader",
                frame,
                "RUNNER                                   WIN        PLACE       SHOW",
                14f,
                new Color(Ink.r, Ink.g, Ink.b, 0.45f),
                TextAlignmentOptions.Left);
            header.rectTransform.anchorMin = new Vector2(0f, 1f);
            header.rectTransform.anchorMax = new Vector2(0f, 1f);
            header.rectTransform.pivot = new Vector2(0f, 1f);
            header.rectTransform.anchoredPosition = new Vector2(30f, -(84f + TrackHeight) - 6f);
            header.rectTransform.sizeDelta = new Vector2(880f, 20f);

            _boardHolder = New("Board", frame);
            _boardHolder.anchorMin = new Vector2(0f, 1f);
            _boardHolder.anchorMax = new Vector2(0f, 1f);
            _boardHolder.pivot = new Vector2(0f, 1f);
            _boardHolder.anchoredPosition = new Vector2(26f, -(84f + TrackHeight) - 28f);
            _boardHolder.sizeDelta = new Vector2(890f, 300f);
        }

        private static void BuildSlip(RectTransform frame)
        {
            var header = Text("SlipHeader", frame, "YOUR SLIP", 16f, Gold, TextAlignmentOptions.Left);
            header.rectTransform.anchorMin = new Vector2(1f, 1f);
            header.rectTransform.anchorMax = new Vector2(1f, 1f);
            header.rectTransform.pivot = new Vector2(1f, 1f);
            header.rectTransform.anchoredPosition = new Vector2(-330f, -(84f + TrackHeight) - 6f);
            header.rectTransform.sizeDelta = new Vector2(240f, 22f);
            header.fontStyle = FontStyles.Bold;

            var clear = MakeButton("ClearSlip", frame, "CLEAR", 90f, 26f, ClearSlip);
            clear.anchorMin = new Vector2(1f, 1f);
            clear.anchorMax = new Vector2(1f, 1f);
            clear.pivot = new Vector2(1f, 1f);
            clear.anchoredPosition = new Vector2(-26f, -(84f + TrackHeight) - 4f);

            var back = New("SlipBack", frame);
            back.anchorMin = new Vector2(1f, 1f);
            back.anchorMax = new Vector2(1f, 1f);
            back.pivot = new Vector2(1f, 1f);
            back.anchoredPosition = new Vector2(-26f, -(84f + TrackHeight) - 32f);
            back.sizeDelta = new Vector2(560f, 250f);

            var backImage = back.gameObject.AddComponent<Image>();
            backImage.sprite = Textures.RoundedBox(10, Slate, new Color(0f, 0f, 0f, 0.5f), 1);
            backImage.type = Image.Type.Sliced;

            _slipHolder = New("Slip", back);
            _slipHolder.anchorMin = new Vector2(0f, 1f);
            _slipHolder.anchorMax = new Vector2(1f, 1f);
            _slipHolder.pivot = new Vector2(0.5f, 1f);
            _slipHolder.anchoredPosition = new Vector2(0f, -8f);
            _slipHolder.sizeDelta = new Vector2(-16f, 234f);

            _slipTotal = Text("SlipTotal", frame, string.Empty, 17f, Ink, TextAlignmentOptions.Right);
            _slipTotal.rectTransform.anchorMin = new Vector2(1f, 1f);
            _slipTotal.rectTransform.anchorMax = new Vector2(1f, 1f);
            _slipTotal.rectTransform.pivot = new Vector2(1f, 1f);
            _slipTotal.rectTransform.anchoredPosition = new Vector2(-30f, -(84f + TrackHeight) - 288f);
            _slipTotal.rectTransform.sizeDelta = new Vector2(540f, 24f);
        }

        private static void BuildControls(RectTransform frame)
        {
            const float row = 52f;

            _status = Text("Status", frame, string.Empty, 16f, Ink, TextAlignmentOptions.Left);
            _status.rectTransform.anchorMin = new Vector2(0f, 0f);
            _status.rectTransform.anchorMax = new Vector2(0f, 0f);
            _status.rectTransform.pivot = new Vector2(0f, 0f);
            _status.rectTransform.anchoredPosition = new Vector2(30f, row + 30f);
            _status.rectTransform.sizeDelta = new Vector2(880f, 24f);

            _result = Text("Result", frame, string.Empty, 24f, Ink, TextAlignmentOptions.Left);
            _result.rectTransform.anchorMin = new Vector2(0f, 0f);
            _result.rectTransform.anchorMax = new Vector2(0f, 0f);
            _result.rectTransform.pivot = new Vector2(0f, 0f);
            _result.rectTransform.anchoredPosition = new Vector2(30f, row + 58f);
            _result.rectTransform.sizeDelta = new Vector2(880f, 32f);
            _result.fontStyle = FontStyles.Bold;

            var stakeLabel = Text("StakeLabel", frame, "STAKE PER BET", 13f,
                new Color(Ink.r, Ink.g, Ink.b, 0.5f), TextAlignmentOptions.Left);
            stakeLabel.rectTransform.anchorMin = new Vector2(0f, 0f);
            stakeLabel.rectTransform.anchorMax = new Vector2(0f, 0f);
            stakeLabel.rectTransform.pivot = new Vector2(0f, 0f);
            stakeLabel.rectTransform.anchoredPosition = new Vector2(30f, row + 4f);
            stakeLabel.rectTransform.sizeDelta = new Vector2(150f, 18f);

            var down = MakeButton("StakeDown", frame, "-", 38f, 34f, () => StepStakeBy(-1));
            down.anchorMin = new Vector2(0f, 0f);
            down.anchorMax = new Vector2(0f, 0f);
            down.pivot = new Vector2(0f, 0f);
            down.anchoredPosition = new Vector2(180f, row - 8f);

            _stakeField = MakeStakeField(frame, new Vector2(224f, row - 8f));

            var up = MakeButton("StakeUp", frame, "+", 38f, 34f, () => StepStakeBy(1));
            up.anchorMin = new Vector2(0f, 0f);
            up.anchorMax = new Vector2(0f, 0f);
            up.pivot = new Vector2(0f, 0f);
            up.anchoredPosition = new Vector2(388f, row - 8f);

            var wallet = MakeButton("Wallet", frame, string.Empty, 130f, 34f, NextWallet);
            wallet.anchorMin = new Vector2(0f, 0f);
            wallet.anchorMax = new Vector2(0f, 0f);
            wallet.pivot = new Vector2(0f, 0f);
            wallet.anchoredPosition = new Vector2(440f, row - 8f);
            _walletLabel = wallet.GetComponentInChildren<TextMeshProUGUI>();

            var run = MakeButton("Run", frame, "RUN THE RACE", 240f, 46f, Go);
            run.anchorMin = new Vector2(1f, 0f);
            run.anchorMax = new Vector2(1f, 0f);
            run.pivot = new Vector2(1f, 0f);
            run.anchoredPosition = new Vector2(-26f, row - 14f);
            _runButton = run.GetComponent<Button>();

            SetWalletLabel();
            SetStakeText();
        }

        // ------------------------------------------------------------------ rendering

        /// <summary>
        /// Draws the card: one row per runner, with its three single-runner prices as
        /// buttons, and the pair bets underneath.
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
                label.rectTransform.anchorMin = new Vector2(0f, 1f);
                label.rectTransform.anchorMax = new Vector2(0f, 1f);
                label.rectTransform.pivot = new Vector2(0f, 1f);
                label.rectTransform.anchoredPosition = new Vector2(4f, y);
                label.rectTransform.sizeDelta = new Vector2(300f, 26f);

                var form = Text($"Form{number}", _boardHolder,
                    $"{chance:P1}", 13f, new Color(Ink.r, Ink.g, Ink.b, 0.40f), TextAlignmentOptions.Left);
                form.rectTransform.anchorMin = new Vector2(0f, 1f);
                form.rectTransform.anchorMax = new Vector2(0f, 1f);
                form.rectTransform.pivot = new Vector2(0f, 1f);
                form.rectTransform.anchoredPosition = new Vector2(310f, y);
                form.rectTransform.sizeDelta = new Vector2(80f, 26f);

                var x = 420f;

                foreach (var kind in new[] { "Win", "Place", "Show" })
                {
                    var spot = MakeSpot(_boardHolder, kind, number, 0, new Vector2(x, y));
                    x += 150f;
                }

                y -= 30f;
            }

            RenderPairs(y - 10f);
        }

        /// <summary>
        /// The pair bets.
        ///
        /// 56 exactas and 28 quinellas do not fit on a board beside the singles, and a
        /// grid of 84 two-digit buttons is not something anybody reads -- so the pair is
        /// chosen with two steppers and the price for the current pair is shown live.
        /// The whole board still travels from the server; this is only which spot on it
        /// is being looked at.
        /// </summary>
        private static void RenderPairs(float y)
        {
            var header = Text("PairHeader", _boardHolder, "EXACTA  /  QUINELLA", 14f,
                new Color(Ink.r, Ink.g, Ink.b, 0.45f), TextAlignmentOptions.Left);
            header.rectTransform.anchorMin = new Vector2(0f, 1f);
            header.rectTransform.anchorMax = new Vector2(0f, 1f);
            header.rectTransform.pivot = new Vector2(0f, 1f);
            header.rectTransform.anchoredPosition = new Vector2(4f, y);
            header.rectTransform.sizeDelta = new Vector2(300f, 22f);

            y -= 26f;

            var first = MakeStepper(_boardHolder, "1st", _pairFirst, new Vector2(4f, y), value =>
            {
                _pairFirst = Wrap(value);
                RenderBoard();
            });

            var second = MakeStepper(_boardHolder, "2nd", _pairSecond, new Vector2(220f, y), value =>
            {
                _pairSecond = Wrap(value);
                RenderBoard();
            });

            // A runner cannot beat itself into second. Rather than refusing the click
            // and saying so, the second stepper simply steps past the first -- there is
            // no state in which the pair is illegal, so there is no error to explain.
            if (_pairSecond == _pairFirst)
            {
                _pairSecond = Wrap(_pairFirst + 1);
            }

            var exacta = MakeSpot(_boardHolder, "Exacta", _pairFirst, _pairSecond, new Vector2(440f, y));
            var quinella = MakeSpot(
                _boardHolder,
                "Quinella",
                Math.Min(_pairFirst, _pairSecond),
                Math.Max(_pairFirst, _pairSecond),
                new Vector2(620f, y));
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
                line.anchoredPosition = new Vector2(0f, y);
                line.sizeDelta = new Vector2(0f, 24f);

                var text = Text("Text", line, Describe(bet), 15f, Ink, TextAlignmentOptions.Left);
                Stretch(text.rectTransform);
                text.rectTransform.offsetMin = new Vector2(8f, 0f);
                text.rectTransform.offsetMax = new Vector2(-8f, 0f);

                y -= 26f;
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
            RectTransform parent, string kind, int first, int second, Vector2 at)
        {
            var price = PriceOf(kind, first, second);
            var on = _slip.Any(b => b.Kind == kind && b.First == first && b.Second == second);

            var rect = New($"{kind}{first}_{second}", parent);
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = at;
            rect.sizeDelta = new Vector2(kind == "Win" || kind == "Place" || kind == "Show" ? 130f : 160f, 26f);

            var image = rect.gameObject.AddComponent<Image>();
            image.sprite = Textures.ButtonFace(
                6,
                on ? Gold : Slate,
                on ? new Color(Gold.r * 0.8f, Gold.g * 0.8f, Gold.b * 0.8f, 1f) : new Color(0.10f, 0.11f, 0.10f, 1f),
                on ? Gold : new Color(0f, 0f, 0f, 0.4f),
                1);
            image.type = Image.Type.Sliced;

            var label = Text("Label", rect,
                second > 0 ? $"{kind.Substring(0, 1)} {first}-{second}   {Odds(price)}" : Odds(price),
                15f,
                on ? new Color(0.1f, 0.1f, 0.1f, 1f) : Ink,
                TextAlignmentOptions.Center);
            Stretch(label.rectTransform);

            var button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            button.onClick.AddListener(() => Toggle(kind, first, second));

            return rect;
        }

        private static RectTransform MakeStepper(
            RectTransform parent, string caption, int value, Vector2 at, Action<int> set)
        {
            var rect = New($"Stepper{caption}", parent);
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = at;
            rect.sizeDelta = new Vector2(200f, 26f);

            var label = Text("Caption", rect, caption, 14f,
                new Color(Ink.r, Ink.g, Ink.b, 0.5f), TextAlignmentOptions.Left);
            label.rectTransform.anchorMin = new Vector2(0f, 0f);
            label.rectTransform.anchorMax = new Vector2(0f, 1f);
            label.rectTransform.pivot = new Vector2(0f, 0.5f);
            label.rectTransform.anchoredPosition = new Vector2(2f, 0f);
            label.rectTransform.sizeDelta = new Vector2(34f, 0f);

            var down = MakeButton($"{caption}Down", rect, "-", 26f, 24f, () => set(value - 1));
            down.anchorMin = new Vector2(0f, 1f);
            down.anchorMax = new Vector2(0f, 1f);
            down.pivot = new Vector2(0f, 1f);
            down.anchoredPosition = new Vector2(40f, -1f);

            var shown = Text("Value", rect, $"{value}  {RunnerName(value)}", 15f, Ink, TextAlignmentOptions.Center);
            shown.rectTransform.anchorMin = new Vector2(0f, 1f);
            shown.rectTransform.anchorMax = new Vector2(0f, 1f);
            shown.rectTransform.pivot = new Vector2(0f, 1f);
            shown.rectTransform.anchoredPosition = new Vector2(70f, -1f);
            shown.rectTransform.sizeDelta = new Vector2(100f, 24f);

            var up = MakeButton($"{caption}Up", rect, "+", 26f, 24f, () => set(value + 1));
            up.anchorMin = new Vector2(0f, 1f);
            up.anchorMax = new Vector2(0f, 1f);
            up.pivot = new Vector2(0f, 1f);
            up.anchoredPosition = new Vector2(172f, -1f);

            return rect;
        }

        private static TMP_InputField MakeStakeField(RectTransform parent, Vector2 at)
        {
            var rect = New("Stake", parent);
            rect.anchorMin = new Vector2(0f, 0f);
            rect.anchorMax = new Vector2(0f, 0f);
            rect.pivot = new Vector2(0f, 0f);
            rect.anchoredPosition = at;
            rect.sizeDelta = new Vector2(158f, 34f);

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
