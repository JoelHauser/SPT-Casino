using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Roulette.Client
{
    /// <summary>
    /// The table window.
    ///
    /// A first pass built around the wheel: the wheel spins, the ball lands, and there
    /// is enough of a control strip to put chips on a few spots and turn it. The full
    /// betting cloth comes next -- proving the spin lands where the server said is
    /// worth doing before a hundred betting spots are drawn on top of it.
    ///
    /// The server decides everything. The panel renders the view it is handed and
    /// posts what the player pressed.
    /// </summary>
    internal static class RoulettePanel
    {
        private const string RootName = "RouletteTableCanvas";

        /// <summary>
        /// The wheel shares the screen with the cloth now, so it is smaller than when
        /// it had the middle to itself. Wheel on the left, cloth on the right: both are
        /// things you look at while betting, and stacking them put the cloth off the
        /// bottom of a 16:9 screen.
        /// </summary>
        private const float WheelSize = 560f;

        private static readonly Color Gold = new Color(0.72f, 0.62f, 0.34f, 1f);
        private static readonly Color Ink = new Color(0.88f, 0.86f, 0.80f, 1f);

        private static GameObject _root;
        private static TMP_FontAsset _font;

        private static RectTransform _wheelHolder;
        private static RectTransform _clothHolder;
        private static RectTransform _chipTray;
        private static TextMeshProUGUI _balance;
        private static RectTransform _actionRow;
        private static TextMeshProUGUI _status;
        private static TextMeshProUGUI _result;

        // The running fade, and whether it is on its way out. IsOpen has to read as
        // closed the moment a close starts, or the tab toggles it straight back open
        // again mid-fade.
        private static Coroutine _fade;
        private static bool _closing;

        private static JObject _lastReply;

        /// <summary>Whether the table is showing a finished spin. See Reopen.</summary>
        private static bool _settled;

        /// <summary>Money has moved and the running game has not been told. See ResyncStash.</summary>
        private static bool _syncOwed;
        private static string _pocketSignature;

        /// <summary>What the next chip put down is worth. Chosen from the tray.</summary>
        private static int _chip = 10_000;

        /// <summary>The layout the server sent, so the cloth offers only bets it accepts.</summary>
        private static ClothLayout _layout;

        private static string _layoutSignature;

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

                Canvas.ForceUpdateCanvases();
                LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)_root.transform);

                Render(RouletteApi.State());
            }
            catch (Exception ex)
            {
                RouletteClientPlugin.Log.LogError("[Roulette] could not open the table: " + ex);
            }
        }

        internal static void Close()
        {
            if (_root == null || !_root.activeSelf || _closing)
            {
                return;
            }

            // Walking out mid-spin, or closing on the result. The animation's callback
            // may never run, and the money has moved regardless, so the debt is settled
            // on the way out rather than left for a reload to discover.
            ResyncStash();

            _closing = true;

            FadeTo(0f, () =>
            {
                _root.SetActive(false);
                _closing = false;
            });
        }

        /// <summary>
        /// Fades the whole panel, rather than switching it.
        ///
        /// The backdrop is nearly opaque, so toggling the canvas takes the screen from
        /// menu to table and back in a single frame -- which is what makes leaving feel
        /// like a jump cut. Both siblings settled on this and on the numbers below; it
        /// is a port rather than a fresh attempt.
        /// </summary>
        private static void FadeTo(float target, Action done)
        {
            var group = _root == null ? null : _root.GetComponent<CanvasGroup>();
            var host = RouletteClientPlugin.Instance;

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
            // settled on and Poker kept, because that is the version that was tried and
            // found to read correctly.
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

        internal static void OnEscape()
        {
            if (_root == null || !_root.activeSelf)
            {
                return;
            }

            // Not while the wheel is turning. The result is already settled on the
            // server, so nothing is lost by closing -- but a table that vanishes
            // mid-spin looks like a crash.
            if (WheelView.IsSpinning)
            {
                return;
            }

            Close();
        }

        // ---------------------------------------------------------------- actions

        private static void Place(string kind, int selection)
        {
            if (WheelView.IsSpinning)
            {
                return;
            }

            // Putting a chip down after a result plainly means "next spin, this bet".
            // The table is still settled and would refuse it -- which it did, ten times
            // in five seconds, in the first session that played for money. The player
            // was clicking the cloth and being told "The wheel has turned" with no way
            // to tell that a button somewhere else was what they needed.
            if (!Reopen())
            {
                return;
            }

            var reply = RouletteApi.Place(kind, selection, _chip);

            if (reply == null)
            {
                SetStatus("No answer from the server.");
                return;
            }

            Render(reply);

            var error = (string)reply["Error"];
            if (!string.IsNullOrEmpty(error))
            {
                SetStatus(error);
            }
        }

        /// <summary>
        /// Takes a chip back off a spot -- the right-click half of placing one.
        ///
        /// The chip currently held is what comes off, so a spot built up with three
        /// 100k chips gives back one at a time. Asking for more than is there takes what
        /// is there, so a big chip in hand still clears a small pile rather than doing
        /// nothing.
        /// </summary>
        private static void Lift(string kind, int selection)
        {
            if (WheelView.IsSpinning)
            {
                return;
            }

            // Right-clicking a settled cloth clears it and stops there. The chips shown
            // belong to a spin that is over, so there is nothing to lift off -- and
            // reopening then removing would take a chip the player never placed.
            if (!Reopen())
            {
                return;
            }

            var reply = RouletteApi.Remove(kind, selection, _chip);

            if (reply == null)
            {
                SetStatus("No answer from the server.");
                return;
            }

            Render(reply);

            var error = (string)reply["Error"];
            if (!string.IsNullOrEmpty(error))
            {
                SetStatus(error);
            }
        }

        /// <summary>
        /// Tells the running game its stash changed, once the result is out.
        ///
        /// The money moves on the server before the reply is even sent, so the client
        /// has to be told or the roubles are invisible until a reload -- the failure
        /// that reads as the mod having eaten them. Worse than invisible, actually: the
        /// client goes on believing in stacks the server deleted, so the next thing the
        /// player drags in their stash is refused.
        ///
        /// **The timing is the whole point.** Asking for it the moment the spin replies
        /// updates the rouble counter on the screen behind the table, and the player
        /// watches their money go up or down while the ball is still rolling. The wheel
        /// then spends nine seconds animating a result they already know. So it waits
        /// for the ball, and the two land together.
        ///
        /// Deferring the telling is not deferring the money. The stake is gone and the
        /// return is paid either way; this only decides when the game is let in on it.
        /// </summary>
        private static void ResyncStash()
        {
            if (!_syncOwed)
            {
                return;
            }

            _syncOwed = false;
            ProfileSync.Request();
        }

        /// <summary>
        /// Clears a finished spin so the cloth takes bets again.
        ///
        /// Returns false when it did the clearing, because that was a whole action on
        /// its own: the old chips have just come off and putting the new one down in
        /// the same click would be putting it on a cloth the player has not looked at
        /// yet.
        /// </summary>
        private static bool Reopen()
        {
            if (!_settled)
            {
                return true;
            }

            // The same call the NEXT SPIN button makes. On a settled table it clears
            // and reopens without turning the wheel or moving any money.
            Render(RouletteApi.Spin());
            SetStatus("Cloth cleared. Place your bets.");

            return false;
        }

        private static void Clear() => Render(RouletteApi.Clear());

        /// <summary>
        /// Shows anything the server needed to say regardless of what was asked, and
        /// resyncs if it moved money.
        ///
        /// The only thing that arrives this way is a stake given back after a spin the
        /// server never finished -- a crash between the debit and the credit. It is
        /// rare and it is the player's money, so it is said plainly rather than
        /// swallowed.
        /// </summary>
        private static void Note(JObject reply)
        {
            var note = (string)reply?["Note"];

            if (string.IsNullOrEmpty(note))
            {
                return;
            }

            ProfileSync.Request();
            SetStatus(note);

            RouletteClientPlugin.Log.LogInfo("[Roulette] " + note);
        }

        /// <summary>
        /// Turns the wheel.
        ///
        /// The server settles before this returns, so the animation is played over a
        /// result that already exists. **The money has already moved by the time the
        /// ball starts rolling** -- the stake is out of the stash and the return is
        /// back in it. The animation is theatre over a settled fact, which is the only
        /// way to do it: a wheel that decided the result when it stopped would be a
        /// wheel the client could be made to lie about.
        ///
        /// The buttons go away while it runs, not to protect the money, which is
        /// already safe, but because a table that accepts bets during a spin is lying
        /// about what it is doing.
        /// </summary>
        private static void Spin()
        {
            var reply = RouletteApi.Spin();

            if (reply == null)
            {
                SetStatus("No answer from the server.");
                return;
            }

            var error = (string)reply["Error"];
            if (!string.IsNullOrEmpty(error))
            {
                Render(reply);
                SetStatus(error);
                return;
            }

            // Deliberately NOT synced here. See ResyncStash: the money has already
            // moved server-side, and asking the game to notice it now would print the
            // answer in the rouble counter behind the table before the ball has
            // stopped rolling.
            _syncOwed = true;

            Note(reply);

            var last = reply["Table"]?["Last"] as JObject;

            // Pressing spin on a settled table opens the next one, and that reply
            // carries the *previous* result rather than a new one. Nothing to animate.
            //
            // Told apart by the phase, which is the only thing that actually says it.
            // This used to test `Staked > 0`, on the reasoning that a fresh spin still
            // has chips on the cloth -- and it does, because settling deliberately
            // leaves the bets in place so the player can see what each one did. So the
            // test was true exactly when there *was* something new to animate: the
            // result was printed under the wheel the instant SPIN was pressed, and the
            // animation then played on the next press, over the old number.
            var settled = string.Equals(
                (string)reply["Table"]?["Phase"], "Settled", StringComparison.OrdinalIgnoreCase);

            if (last == null || !settled)
            {
                Render(reply);
                return;
            }

            _lastReply = reply;

            var position = (int?)last["Position"] ?? 0;

            SetStatus("No more bets.");
            SetResult(string.Empty, null);
            BuildActions([]);

            WheelView.Spin(position, () =>
            {
                var label = (string)last["Label"] ?? "?";
                var colour = (string)last["Colour"];
                var profit = (int?)last["Profit"] ?? 0;

                // Now, with the ball. Any earlier and the stash gives the result away.
                ResyncStash();

                SetResult(label, colour);

                SetStatus(profit >= 0
                    ? $"Up {profit:N0} on the spin."
                    : $"Down {Math.Abs(profit):N0} on the spin.");

                Render(_lastReply, keepStatus: true, keepResult: true);
            });
        }

        // ---------------------------------------------------------------- rendering

        private static void Render(JObject reply, bool keepStatus = false, bool keepResult = false)
        {
            var table = reply?["Table"] as JObject;

            if (table == null)
            {
                SetStatus("Not at a table.");
                BuildActions(Lobby());
                return;
            }

            _lastReply = reply;

            EnsureWheel(table["Pockets"] as JArray);
            EnsureCloth(table["Layout"] as JObject);
            ShowBets(table["Bets"] as JArray);

            var staked = (int?)table["Staked"] ?? 0;
            var phase = (string)table["Phase"] ?? "Betting";

            _settled = string.Equals(phase, "Settled", StringComparison.OrdinalIgnoreCase);
            var bets = table["Bets"] as JArray;

            if (!keepStatus)
            {
                // The right-click is said where it will be read: on the line the player
                // is already looking at while there are chips down. An undiscoverable
                // control is the same as one that is not there.
                SetStatus(staked > 0
                    ? $"{staked:N0} on the cloth across {bets?.Count ?? 0} bet(s).  Right-click a spot to take a chip back."
                    : "Left-click to put a chip down, right-click to take one back.");
            }

            if (!keepResult)
            {
                var last = table["Last"] as JObject;

                if (last != null && string.Equals(phase, "Settled", StringComparison.OrdinalIgnoreCase))
                {
                    SetResult((string)last["Label"], (string)last["Colour"]);
                }
                else
                {
                    SetResult(string.Empty, null);
                }
            }

            SetBalance(staked);
            BuildActions(Controls(phase, staked));
        }

        /// <summary>
        /// Builds the wheel the first time, and again only if the pockets changed.
        ///
        /// Keyed on the pocket list itself rather than on a wheel name: the list is
        /// what the wheel is drawn from, so anything that would change the drawing
        /// changes the key.
        /// </summary>
        private static void EnsureWheel(JArray pockets)
        {
            if (pockets == null || _wheelHolder == null)
            {
                return;
            }

            var signature = string.Join(",", pockets.Select(p => (string)p["Label"]));

            if (signature == _pocketSignature)
            {
                return;
            }

            for (var i = _wheelHolder.childCount - 1; i >= 0; i--)
            {
                UnityEngine.Object.Destroy(_wheelHolder.GetChild(i).gameObject);
            }

            var built = pockets
                .Select(p => new PocketInfo(
                    (int?)p["Number"] ?? 0,
                    (string)p["Label"] ?? "?",
                    (string)p["Colour"] ?? "Black"))
                .ToList();

            WheelView.Build(_wheelHolder, built, WheelSize, _font);
            _pocketSignature = signature;
        }

        /// <summary>
        /// Builds the cloth once, and again only if the layout changed. Keyed on the
        /// layout itself rather than on a flag, so anything that would change what the
        /// cloth offers changes the key.
        /// </summary>
        private static void EnsureCloth(JObject layout)
        {
            if (layout == null || _clothHolder == null)
            {
                return;
            }

            var splits = layout["Splits"] as JArray;
            var streets = layout["Streets"] as JArray;
            var corners = layout["Corners"] as JArray;
            var sixLines = layout["SixLines"] as JArray;

            var signature = $"{splits?.Count}/{streets?.Count}/{corners?.Count}/{sixLines?.Count}";

            if (signature == _layoutSignature)
            {
                return;
            }

            for (var i = _clothHolder.childCount - 1; i >= 0; i--)
            {
                UnityEngine.Object.Destroy(_clothHolder.GetChild(i).gameObject);
            }

            _layout = new ClothLayout(
                splits == null
                    ? new List<(int, int)>()
                    : splits.Select(x => ((int?)x["Low"] ?? 0, (int?)x["High"] ?? 0)).ToList(),
                streets?.Select(x => (int)x).ToList() ?? new List<int>(),
                corners?.Select(x => (int)x).ToList() ?? new List<int>(),
                sixLines?.Select(x => (int)x).ToList() ?? new List<int>());

            ClothView.Build(_clothHolder, _layout, _font, Place, Lift);
            _layoutSignature = signature;
        }

        private static void ShowBets(JArray bets)
        {
            if (_layoutSignature == null)
            {
                return;
            }

            ClothView.ShowBets(
                bets?.Select(b => (
                    (string)b["Kind"] ?? string.Empty,
                    (int?)b["Selection"] ?? 0,
                    (int?)b["Amount"] ?? 0)));
        }

        private static void SetBalance(int staked)
        {
            if (_balance != null)
            {
                _balance.text = staked > 0
                    ? $"CHIP  {_chip:N0}          ON THE CLOTH  {staked:N0}"
                    : $"CHIP  {_chip:N0}";
            }
        }

        /// <summary>
        /// The tray. Picking a chip decides what the next one put on the cloth is
        /// worth, which is how a real table works -- you choose a denomination and then
        /// place it, rather than typing an amount.
        /// </summary>
        private static void BuildChipTray()
        {
            if (_chipTray == null)
            {
                return;
            }

            for (var i = _chipTray.childCount - 1; i >= 0; i--)
            {
                UnityEngine.Object.Destroy(_chipTray.GetChild(i).gameObject);
            }

            foreach (var chip in ChipView.Denominations)
            {
                var value = chip.Value;

                var box = NewBox("Chip_" + chip.File, _chipTray, Color.white);
                box.sizeDelta = new Vector2(58f, 58f);

                var image = box.GetComponent<Image>();
                image.sprite = ChipView.Face(chip);
                image.preserveAspect = true;

                // The chosen one is full strength and the rest are dimmed, which reads
                // faster than a border and cannot be missed at a glance.
                image.color = value == _chip ? Color.white : new Color(1f, 1f, 1f, 0.42f);

                box.gameObject.AddComponent<Button>().onClick.AddListener(() =>
                {
                    _chip = value;
                    BuildChipTray();
                    SetBalance(Staked());
                });
            }
        }

        private static int Staked() => (int?)_lastReply?["Table"]?["Staked"] ?? 0;

        private static List<KeyValuePair<string, Action>> Lobby() =>
            [Action("OPEN A TABLE", () => Render(RouletteApi.State())), Action("CLOSE", Close)];

        /// <summary>
        /// A handful of bets and a spin. Not the cloth -- that is the next piece of
        /// work -- but enough to put money on several different rules at once and see
        /// them settle.
        /// </summary>
        /// <summary>
        /// What is left once the cloth carries the betting: the table's own verbs.
        /// Seven buttons standing in for a layout is what made this unreadable -- there
        /// was no way to bet a number other than 17, and nothing showed what was down.
        /// </summary>
        private static List<KeyValuePair<string, Action>> Controls(string phase, int staked)
        {
            if (string.Equals(phase, "Settled", StringComparison.OrdinalIgnoreCase))
            {
                return [Action("NEXT SPIN", Spin), Action("CLOSE", Close)];
            }

            var controls = new List<KeyValuePair<string, Action>>();

            if (staked > 0)
            {
                controls.Add(Action("SPIN", Spin));
                controls.Add(Action("CLEAR", Clear));
            }

            controls.Add(Action("CLOSE", Close));

            return controls;
        }

        // ---------------------------------------------------------------- building

        private static void Build()
        {
            _font = BorrowFont();

            var canvasObject = new GameObject(
                RootName, typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));

            var canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 30000;

            var scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);

            // Match height, not a blend, or an ultrawide stretches the wheel into an
            // ellipse -- which on a wheel is worse than on anything else.
            scaler.matchWidthOrHeight = 1f;

            _root = canvasObject;

            // Faded rather than switched. See FadeTo.
            var group = canvasObject.AddComponent<CanvasGroup>();
            group.alpha = 0f;

            var backdrop = NewBox("Backdrop", canvasObject.transform, new Color(0f, 0f, 0f, 0.93f));
            Stretch(backdrop);

            var title = NewText("Title", canvasObject.transform, "ROULETTE", 30f, TextAlignmentOptions.Center);
            title.rectTransform.anchorMin = new Vector2(0.5f, 1f);
            title.rectTransform.anchorMax = new Vector2(0.5f, 1f);
            title.rectTransform.pivot = new Vector2(0.5f, 1f);
            title.rectTransform.sizeDelta = new Vector2(600f, 44f);
            title.rectTransform.anchoredPosition = new Vector2(0f, -18f);
            title.color = Gold;

            // Wheel left, cloth right. Both are looked at while betting, and stacking
            // them ran the cloth off the bottom of a 16:9 screen. The pair is centred as
            // a whole, so an ultrawide simply gets more dark either side.
            // Solved rather than eyeballed. Putting the wheel at -(table + gap)/2 makes
            // the pair centre on the screen for any wheel size: the left edge is
            // wheelX - WheelSize/2 and the right edge wheelX + WheelSize/2 + table + gap,
            // and those two sum to zero exactly when wheelX takes that value. The old
            // figure only balanced when the wheel happened to be as wide as the cloth.
            const float gap = 70f;

            var wheelX = -(ClothView.Framed + gap) * 0.5f;
            var clothX = wheelX + (WheelSize * 0.5f) + (ClothView.Framed * 0.5f) + gap;

            _wheelHolder = NewBox("Wheel", canvasObject.transform, Color.clear);
            _wheelHolder.anchorMin = _wheelHolder.anchorMax = new Vector2(0.5f, 0.5f);
            _wheelHolder.pivot = new Vector2(0.5f, 0.5f);
            _wheelHolder.sizeDelta = new Vector2(WheelSize, WheelSize);
            _wheelHolder.anchoredPosition = new Vector2(wheelX, 70f);

            _clothHolder = NewBox("ClothHolder", canvasObject.transform, Color.clear);
            _clothHolder.anchorMin = _clothHolder.anchorMax = new Vector2(0.5f, 0.5f);
            _clothHolder.pivot = new Vector2(0.5f, 0.5f);
            _clothHolder.sizeDelta = new Vector2(ClothView.Framed, ClothView.FramedHeight);
            _clothHolder.anchoredPosition = new Vector2(clothX, 70f);

            // The result goes under the wheel, where the eye already is when the ball
            // stops, rather than in the middle of the screen away from both halves.
            _result = NewText("Result", canvasObject.transform, string.Empty, 58f, TextAlignmentOptions.Center);
            _result.rectTransform.anchorMin = _result.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            _result.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            _result.rectTransform.sizeDelta = new Vector2(WheelSize, 72f);
            _result.rectTransform.anchoredPosition = new Vector2(wheelX, 70f - (WheelSize * 0.5f) - 52f);
            _result.color = Gold;

            _balance = NewText("Balance", canvasObject.transform, string.Empty, 20f, TextAlignmentOptions.Center);
            _balance.rectTransform.anchorMin = _balance.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            _balance.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            _balance.rectTransform.sizeDelta = new Vector2(ClothView.Framed, 28f);
            _balance.rectTransform.anchoredPosition =
                new Vector2(clothX, 70f + ClothView.Reach + 30f);
            _balance.color = Gold;

            // The tray under the cloth: a chip is picked, then put down, the way a real
            // table works rather than by typing an amount.
            _chipTray = NewBox("ChipTray", canvasObject.transform, Color.clear);
            _chipTray.anchorMin = _chipTray.anchorMax = new Vector2(0.5f, 0.5f);
            _chipTray.pivot = new Vector2(0.5f, 0.5f);
            _chipTray.sizeDelta = new Vector2(ClothView.Framed, 62f);
            _chipTray.anchoredPosition = new Vector2(clothX, 70f - ClothView.Reach - 52f);

            var tray = _chipTray.gameObject.AddComponent<HorizontalLayoutGroup>();
            tray.spacing = 12f;
            tray.childAlignment = TextAnchor.MiddleCenter;
            tray.childForceExpandWidth = false;
            tray.childForceExpandHeight = false;
            tray.childControlWidth = false;
            tray.childControlHeight = false;

            BuildChipTray();

            _status = NewText("Status", canvasObject.transform, string.Empty, 19f, TextAlignmentOptions.Center);
            _status.rectTransform.anchorMin = new Vector2(0.5f, 0f);
            _status.rectTransform.anchorMax = new Vector2(0.5f, 0f);
            _status.rectTransform.pivot = new Vector2(0.5f, 0f);
            _status.rectTransform.sizeDelta = new Vector2(1400f, 30f);
            _status.rectTransform.anchoredPosition = new Vector2(0f, 108f);

            _actionRow = NewBox("Actions", canvasObject.transform, Color.clear);
            _actionRow.anchorMin = new Vector2(0.5f, 0f);
            _actionRow.anchorMax = new Vector2(0.5f, 0f);
            _actionRow.pivot = new Vector2(0.5f, 0f);
            _actionRow.sizeDelta = new Vector2(1700f, 52f);
            _actionRow.anchoredPosition = new Vector2(0f, 46f);

            var strip = _actionRow.gameObject.AddComponent<HorizontalLayoutGroup>();
            strip.spacing = 10f;
            strip.childAlignment = TextAnchor.MiddleCenter;
            strip.childForceExpandWidth = false;
            strip.childForceExpandHeight = false;
            strip.childControlWidth = false;
            strip.childControlHeight = false;
        }

        private static void BuildActions(IEnumerable<KeyValuePair<string, Action>> actions)
        {
            if (_actionRow == null)
            {
                return;
            }

            for (var i = _actionRow.childCount - 1; i >= 0; i--)
            {
                UnityEngine.Object.Destroy(_actionRow.GetChild(i).gameObject);
            }

            foreach (var action in actions)
            {
                BuildButton(_actionRow, action.Key, action.Value);
            }
        }

        private static void BuildButton(Transform parent, string label, Action onClick)
        {
            var box = NewBox("Button_" + label, parent, Color.white);
            box.sizeDelta = new Vector2(Mathf.Max(78f, 26f + (label.Length * 12f)), 44f);

            var image = box.GetComponent<Image>();
            image.sprite = Textures.ButtonFace(
                6,
                new Color(0.19f, 0.20f, 0.19f, 1f),
                new Color(0.11f, 0.12f, 0.11f, 1f),
                Gold);
            image.type = Image.Type.Sliced;

            var text = NewText("Label", box, label, 18f, TextAlignmentOptions.Center);
            Stretch(text.rectTransform);

            box.gameObject.AddComponent<Button>().onClick.AddListener(() => onClick());
        }

        private static KeyValuePair<string, Action> Action(string label, Action onClick) =>
            new KeyValuePair<string, Action>(label, onClick);

        private static void SetStatus(string text)
        {
            if (_status != null)
            {
                _status.text = text;
            }
        }

        /// <summary>
        /// The winning number, drawn in the colour it came up. A player looks here
        /// first and should not have to read a word to know how it went.
        /// </summary>
        private static void SetResult(string label, string colour)
        {
            if (_result == null)
            {
                return;
            }

            _result.text = string.IsNullOrEmpty(label) ? string.Empty : label;

            _result.color = colour switch
            {
                "Red" => new Color(0.85f, 0.27f, 0.25f, 1f),
                "Green" => new Color(0.35f, 0.78f, 0.50f, 1f),
                "Black" => new Color(0.86f, 0.86f, 0.88f, 1f),
                _ => Gold,
            };
        }

        private static RectTransform NewBox(string name, Transform parent, Color colour)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.transform.SetParent(parent, false);
            go.GetComponent<Image>().color = colour;
            return (RectTransform)go.transform;
        }

        private static TextMeshProUGUI NewText(
            string name, Transform parent, string text, float size, TextAlignmentOptions alignment)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);

            var label = go.AddComponent<TextMeshProUGUI>();
            label.text = text;
            label.fontSize = size;
            label.alignment = alignment;
            label.color = Ink;
            label.raycastTarget = false;

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

        /// <summary>
        /// Borrows a font the game has already loaded rather than shipping one.
        /// TextMeshPro renders nothing at all with a null font, so a label that never
        /// appears looks like a layout bug rather than a missing asset.
        /// </summary>
        private static TMP_FontAsset BorrowFont()
        {
            try
            {
                return Resources.FindObjectsOfTypeAll<TMP_FontAsset>().FirstOrDefault();
            }
            catch (Exception ex)
            {
                RouletteClientPlugin.Log.LogWarning("[Roulette] could not borrow a font: " + ex.Message);
                return null;
            }
        }
    }
}
