using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Newtonsoft.Json.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Blackjack.Client
{
    /// <summary>
    /// The table.
    ///
    /// Laid out in stacks rather than by arithmetic. Every overlap so far -- controls
    /// on top of each other, an error message hidden behind the buttons -- came from
    /// positioning siblings by hand at heights that were each individually plausible.
    /// A layout group cannot make that mistake, so anything below the cloth belongs to
    /// one.
    ///
    /// This side renders and asks. It never shuffles, never scores a hand and never
    /// decides an outcome; the dealer's hole card is not in the response until the
    /// hand is over, so it could not cheat even by accident.
    /// </summary>
    internal static class BlackjackPanel
    {
        private const string RootName = "BlackjackPanel";

        private static readonly Color Felt = new Color(0.055f, 0.30f, 0.17f, 1f);
        private static readonly Color FeltEdge = new Color(0.21f, 0.13f, 0.07f, 1f);
        private static readonly Color Rail = new Color(0.31f, 0.20f, 0.11f, 1f);
        private static readonly Color Ink = new Color(0.92f, 0.91f, 0.86f, 1f);
        private static readonly Color Faint = new Color(0.66f, 0.72f, 0.64f, 1f);
        private static readonly Color Gold = new Color(0.78f, 0.68f, 0.38f, 0.90f);
        private static readonly Color Good = new Color(0.55f, 0.82f, 0.45f, 1f);
        private static readonly Color Bad = new Color(0.92f, 0.42f, 0.36f, 1f);
        // Buttons are described by two stops and an edge rather than one flat colour,
        // because a flat rectangle beside a photograph of a table reads as a dialog box
        // that wandered in from another program.
        private static readonly Color ChipTop = new Color(0.17f, 0.18f, 0.19f, 0.97f);
        private static readonly Color ChipBottom = new Color(0.09f, 0.10f, 0.11f, 0.97f);
        private static readonly Color ChipEdge = new Color(0.30f, 0.28f, 0.24f, 1f);

        // Brass, matching the cup holders and fittings on the table itself.
        private static readonly Color BrassTop = new Color(0.55f, 0.43f, 0.16f, 1f);
        private static readonly Color BrassBottom = new Color(0.33f, 0.25f, 0.08f, 1f);
        private static readonly Color BrassEdge = new Color(0.72f, 0.58f, 0.26f, 1f);

        private static GameObject _root;
        private static TMP_FontAsset _font;

        private static TextMeshProUGUI _balance;
        private static TextMeshProUGUI _message;
        private static TextMeshProUGUI _dealerValue;
        private static TextMeshProUGUI _held;
        private static TMP_InputField _wagerInput;
        private static RectTransform _dealerCards;
        private static RectTransform _handsRow;
        private static RectTransform _actionRow;
        private static GameObject _betControls;
        private static GameObject _bettingSpot;
        private static CanvasGroup _fade;
        private static Coroutine _fading;
        private static GameObject _leave;
        private static RectTransform _cloth;
        private static GameObject _statsPanel;
        private static GameObject _statsButton;
        private static RectTransform _statsTiles;
        private static RectTransform _statsRows;
        private static TextMeshProUGUI _statsEmpty;
        private static GameObject _confirm;
        private static TextMeshProUGUI _confirmText;

        private static readonly List<(string Wallet, GameObject Chip)> Wallets = new List<(string, GameObject)>();

        /// <summary>
        /// What the player holds, as of the last time the server said. Used for the
        /// header, for ALL IN, and to catch a bet bigger than the stash before it is
        /// sent. The server checks again regardless.
        /// </summary>
        private static readonly Dictionary<string, long> Balances = new Dictionary<string, long>();

        /// <summary>
        /// Left, top, right, bottom padding between the table's edge and the cloth it
        /// is safe to put things on, as a fraction of the table's size.
        ///
        /// A fraction, not pixels, so resizing the table cannot silently push the
        /// dealer's cards onto the wooden rail. The photograph's numbers were measured
        /// off the image: the cloth starts 8.4% in from the left, 7.9% from the right,
        /// 14.6% down and 18.7% up.
        /// </summary>
        private static Vector4 _feltFraction = new Vector4(0.03f, 0.03f, 0.03f, 0.03f);

        private static string TableImagePath => System.IO.Path.Combine(
            System.IO.Path.GetDirectoryName(BlackjackClientPlugin.Instance?.Info?.Location ?? ".") ?? ".",
            "table.png");

        private static string _wallet = "Roubles";
        private static long _wager = 10_000;

        internal static bool IsOpen => _root != null && _root.activeSelf;

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

                _root.SetActive(true);
                StartFade(1f, false);

                // A canvas built this frame has not had a layout pass yet, so its
                // controls have no real size or position until one happens. Anything
                // that forced a rebuild -- pressing the one button that was where the
                // raycast expected it -- appeared to "wake up" the rest. Doing it here
                // means the table is live the moment it opens.
                Canvas.ForceUpdateCanvases();
                LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)_root.transform);

                RefreshBalances();

                // Resume rather than assume: a hand can still be live from an earlier
                // visit, and /blackjack/state is what says so.
                Render(BlackjackApi.State(), true);
            }
            catch (Exception ex)
            {
                BlackjackClientPlugin.Log.LogError("[Blackjack] could not open the table: " + ex);
            }
        }

        /// <summary>
        /// Escape, handled in the order things are stacked: the question first, then
        /// the record sheet, then the table itself. Anything else and escape would
        /// close the whole table out from under an unanswered prompt.
        /// </summary>
        internal static void OnEscape()
        {
            if (_root == null || !_root.activeSelf)
            {
                return;
            }

            if (_confirm != null && _confirm.activeSelf)
            {
                CancelConfirm();
                return;
            }

            if (_statsPanel != null && _statsPanel.activeSelf)
            {
                ToggleStats();
                return;
            }

            Close();
        }

        internal static void Close()
        {
            // An unanswered question must not be waiting when the table is reopened.
            if (_confirm != null)
            {
                _confirm.SetActive(false);
            }

            // The sheet must not still be lying on the table next time it is opened.
            HideStats();

            if (_root == null || !_root.activeSelf)
            {
                return;
            }

            // Faded rather than switched off. A full-screen dim and a table vanishing
            // between one frame and the next reads as a glitch; a sixth of a second is
            // enough to read as leaving.
            StartFade(0f, true);
        }

        private static void StartFade(float target, bool deactivateAfter)
        {
            if (_fade == null || BlackjackClientPlugin.Instance == null)
            {
                // No coroutine to run it, so snap and stay correct.
                if (_fade != null)
                {
                    _fade.alpha = target;
                }

                if (deactivateAfter && _root != null)
                {
                    _root.SetActive(false);
                }

                return;
            }

            if (_fading != null)
            {
                BlackjackClientPlugin.Instance.StopCoroutine(_fading);
            }

            _fading = BlackjackClientPlugin.Instance.StartCoroutine(FadeTo(target, deactivateAfter));
        }

        private static IEnumerator FadeTo(float target, bool deactivateAfter)
        {
            const float duration = 0.16f;
            var from = _fade.alpha;

            // Clicks are ignored while it is on its way out, so a stray one during the
            // fade cannot deal a hand at a table that is closing.
            _fade.interactable = !deactivateAfter;
            _fade.blocksRaycasts = !deactivateAfter;

            for (var t = 0f; t < duration; t += Time.unscaledDeltaTime)
            {
                _fade.alpha = Mathf.Lerp(from, target, t / duration);
                yield return null;
            }

            _fade.alpha = target;
            _fading = null;

            if (deactivateAfter && _root != null)
            {
                _root.SetActive(false);
            }
        }

        // ------------------------------------------------------------------- actions

        private static void Deal()
        {
            if (_wager <= 0)
            {
                Say("Type an amount to bet first.", Bad);
                return;
            }

            // Caught here only to save a round trip and a refusal that reads worse than
            // this does. The server checks the balance itself and is the authority.
            if (Balances.TryGetValue(_wallet, out var held) && _wager > held)
            {
                Say($"You have {held:N0} {Short(_wallet)}. That is more than you are carrying.", Bad);
                return;
            }

            // The field accepts fifteen digits so nobody is stopped mid-type, but a
            // wager crosses the wire as an int. Saying so beats a request that binds to
            // zero at the other end and comes back refused for being too small.
            if (_wager > int.MaxValue)
            {
                Say($"The largest bet the table can take is {int.MaxValue:N0}.", Bad);
                return;
            }

            // A hand dealt behind the sheet would be dealt out of sight.
            HideStats();

            Render(BlackjackApi.Deal(_wallet, _wager));
        }

        private static void Act(string action) => Render(BlackjackApi.Act(action));

        private static void ChooseWallet(string wallet)
        {
            _wallet = wallet;

            // Valuables are staked by the piece and currency in thousands, so a wager
            // carried over from roubles is nonsense in bitcoin.
            _wager = IsValuable(wallet) ? 1 : 10_000;
            SetWagerText();
            HighlightWallet();
            UpdateHeld();
        }

        private static bool IsValuable(string wallet) =>
            wallet == "Bitcoin" || wallet == "LegaMedals" || wallet == "GpCoins";

        /// <summary>
        /// Asks before staking the lot. Every other control here is recoverable; this
        /// one is a single click beside the field the player was already aiming at.
        /// </summary>
        private static void AskBetEverything()
        {
            if (!Balances.TryGetValue(_wallet, out var held) || held <= 0)
            {
                Say($"You have no {Short(_wallet)} to bet.", Bad);
                return;
            }

            _confirmText.text = $"Bet everything?\n\n<size=32>{held:N0}  {Short(_wallet)}</size>";
            _confirm.SetActive(true);
            _confirm.transform.SetAsLastSibling();
        }

        private static void CancelConfirm() => _confirm.SetActive(false);

        private static void ConfirmBetEverything()
        {
            _confirm.SetActive(false);

            if (Balances.TryGetValue(_wallet, out var held) && held > 0)
            {
                _wager = held;
                SetWagerText();
                UpdateHeld();
            }
        }

        // ----------------------------------------------------------------- rendering

        private static void Render(JObject response, bool quiet = false)
        {
            if (response == null)
            {
                Say("No answer from the server. Is it running?", Bad);
                return;
            }

            var ok = response["Ok"]?.ToObject<bool>() ?? false;
            var error = response["Error"]?.ToString();
            var note = response["Note"]?.ToString();

            if (!ok && !string.IsNullOrEmpty(error))
            {
                Say(error, Bad);
            }
            else if (!string.IsNullOrEmpty(note))
            {
                Say(note, Faint);
            }
            else if (!quiet)
            {
                Say("", Faint);
            }

            var round = response["Round"] as JObject;

            // A settled hand has moved money, so what is held has changed.
            if ((round?["Phase"]?.ToString() ?? "") == "Settled")
            {
                RefreshBalances();
            }

            RenderRound(round);
        }

        private static void RenderRound(JObject round)
        {
            Clear(_dealerCards);
            Clear(_handsRow);

            var phase = round?["Phase"]?.ToString() ?? "AwaitingBet";
            var betting = phase == "AwaitingBet" || phase == "Settled";

            _betControls.SetActive(betting);

            // No leaving mid-hand. The stake is already gone and the round is still
            // owed: walking away would look to the player like the money vanished, and
            // the table would be waiting for them when they came back anyway.
            if (_leave != null)
            {
                _leave.SetActive(betting);
            }

            // Stats are for between hands. Reading them mid-round would mean taking
            // the cards off the table while a hand is still owed, which is the one
            // moment the table should be showing the hand.
            if (_statsButton != null)
            {
                _statsButton.SetActive(betting);
            }

            var dealer = round?["Dealer"] as JObject;
            var dealerCards = dealer?["Cards"]?.ToObject<List<string>>() ?? new List<string>();
            foreach (var card in dealerCards)
            {
                CardView.Build(_dealerCards, card, _font);
            }

            if (phase == "PlayerTurn" && dealerCards.Count > 0)
            {
                // The hole card is not in the response during play. Drawing a back in
                // its place is honest: the client does not have it to show.
                CardView.Build(_dealerCards, null, _font);
            }

            var dealerValue = dealer?["Value"]?.ToObject<int>() ?? 0;
            _dealerValue.text = dealerCards.Count == 0
                ? ""
                : (phase == "PlayerTurn" ? $"{dealerValue} + ?" : dealerValue.ToString());

            var hands = round?["PlayerHands"] as JArray;
            var anyHands = hands != null && hands.Count > 0;

            // The betting spot is only a spot while it is empty; once cards are on it,
            // it is under them.
            _bettingSpot.SetActive(!anyHands);

            if (anyHands)
            {
                var active = round["ActiveHandIndex"]?.ToObject<int>() ?? -1;
                for (var i = 0; i < hands.Count; i++)
                {
                    BuildHand(_handsRow, (JObject)hands[i], i == active && phase == "PlayerTurn");
                }
            }

            FitHands();
            RenderActions(round, phase);
        }

        /// <summary>
        /// Scales the hands down if they no longer fit across the cloth.
        ///
        /// Two five-card hands come to 1088 against an area of 940. It is a rare hand
        /// and a rarer pair of them, but the alternative to shrinking is cards sliding
        /// off the felt, and a slightly small hand still reads correctly.
        /// </summary>
        private static void FitHands()
        {
            if (_handsRow == null)
            {
                return;
            }

            _handsRow.localScale = Vector3.one;

            var area = _handsRow.parent as RectTransform;
            if (area == null)
            {
                return;
            }

            LayoutRebuilder.ForceRebuildLayoutImmediate(_handsRow);

            var needed = LayoutUtility.GetPreferredWidth(_handsRow);
            var available = area.rect.width;

            if (needed > available && available > 1f)
            {
                var scale = available / needed;
                _handsRow.localScale = new Vector3(scale, scale, 1f);
            }
        }

        private static void BuildHand(RectTransform parent, JObject hand, bool isActive)
        {
            var column = NewBox(
                "Hand",
                parent,
                isActive ? new Color(0f, 0f, 0f, 0.28f) : new Color(0f, 0f, 0f, 0f),
                12,
                isActive ? Gold : new Color(0f, 0f, 0f, 0f),
                isActive ? 2 : 0);

            var layout = column.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.childAlignment = TextAnchor.UpperCenter;
            layout.spacing = 6f;
            layout.padding = new RectOffset(16, 16, 12, 12);
            layout.childForceExpandHeight = false;
            layout.childForceExpandWidth = false;
            layout.childControlHeight = true;

            // Left to their own widths. The card row and the labels each know how wide
            // they should be; a column that overrides them undoes the calculation above.
            layout.childControlWidth = false;

            var fitter = column.gameObject.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var cards = hand["Cards"]?.ToObject<List<string>>() ?? new List<string>();

            // Width computed from the cards, not left at zero.
            //
            // This row previously claimed a preferred width of nothing, so the column
            // around it measured about as wide as its own padding while the cards
            // spilled out of it -- which is why two split hands sat on top of each
            // other. A layout group believes what its children tell it.
            const float cardGap = 10f;
            var rowWidth = cards.Count > 0
                ? (cards.Count * CardView.Width) + ((cards.Count - 1) * cardGap)
                : CardView.Width;

            var cardsRow = NewRow("Cards", column, cardGap);
            SetSize(cardsRow, rowWidth, CardView.Height);

            foreach (var card in cards)
            {
                CardView.Build(cardsRow, card, _font);
            }

            var value = hand["Value"]?.ToObject<int>() ?? 0;
            var soft = hand["IsSoft"]?.ToObject<bool>() ?? false;
            var outcome = hand["Outcome"]?.ToString();
            var wager = hand["Wager"]?.ToObject<long>() ?? 0;

            // Labels no wider than the cards they belong to, or a two-card hand claims
            // 220 of width for its total and the hands drift apart for no reason.
            var labelWidth = Mathf.Max(rowWidth, 140f);

            SetSize(Label(column, $"{value}{(soft ? " soft" : "")}", 26f, Ink, TextAlignmentOptions.Center).rectTransform, labelWidth, 32f);
            SetSize(Label(column, $"{wager:N0} {Short(_wallet)}", 17f, Faint, TextAlignmentOptions.Center).rectTransform, labelWidth, 22f);

            if (!string.IsNullOrEmpty(outcome) && outcome != "Pending")
            {
                var won = outcome == "Win" || outcome == "Blackjack";
                var pushed = outcome == "Push";
                SetSize(
                    Label(column, outcome.ToUpperInvariant(), 24f, pushed ? Faint : (won ? Good : Bad), TextAlignmentOptions.Center).rectTransform,
                    labelWidth,
                    30f);
            }
        }

        private static void RenderActions(JObject round, string phase)
        {
            Clear(_actionRow);

            if (phase == "PlayerTurn")
            {
                var actions = round?["AvailableActions"]?.ToObject<List<string>>() ?? new List<string>();

                // A fixed order, not the server's. Hit and Stand are muscle memory and
                // should not move about because the legal set changed.
                foreach (var action in new[] { "Hit", "Stand", "Double", "Split" })
                {
                    if (!actions.Contains(action))
                    {
                        continue;
                    }

                    var captured = action;
                    Chip(_actionRow, action.ToUpperInvariant(), 160f, () => Act(captured));
                }

                return;
            }

            // The only button here that starts a round, so the only one in brass.
            // Everything else is a choice within a hand that is already running.
            var deal = Chip(_actionRow, "DEAL", 200f, Deal, primary: true);

            // Greyed when the bet cannot be placed, rather than looking ready and then
            // refusing. Still clickable, because the refusal explains itself and a
            // button that silently does nothing is worse than one that answers.
            var affordable = !Balances.TryGetValue(_wallet, out var held) || (_wager > 0 && _wager <= held);
            if (affordable)
            {
                return;
            }

            var face = deal.GetComponent<Image>();
            if (face != null)
            {
                face.sprite = Textures.ButtonFace(
                    8,
                    new Color(0.13f, 0.12f, 0.11f, 0.96f),
                    new Color(0.08f, 0.08f, 0.08f, 0.96f),
                    new Color(0.26f, 0.22f, 0.18f, 1f));
            }

            var dealLabel = deal.GetComponentInChildren<TextMeshProUGUI>();
            if (dealLabel != null)
            {
                dealLabel.color = new Color(0.50f, 0.48f, 0.46f, 1f);
            }
        }

        private static void Say(string text, Color colour)
        {
            if (_message != null)
            {
                _message.text = text;
                _message.color = colour;
            }
        }

        // ------------------------------------------------------------------ balances

        private static void RefreshBalances()
        {
            var balances = BlackjackApi.Ping()?["Balances"];
            if (balances == null)
            {
                return;
            }

            Balances.Clear();
            foreach (var entry in balances.Children<JProperty>())
            {
                Balances[entry.Name] = entry.Value.ToObject<long>();
            }

            UpdateHeld();
        }

        /// <summary>
        /// The header and the line beside the field both say what is held. Advisory
        /// only: the server decides what is a legal bet.
        /// </summary>
        private static void UpdateHeld()
        {
            var known = Balances.TryGetValue(_wallet, out var held);

            if (_balance != null)
            {
                _balance.text = known ? $"{held:N0}  {Short(_wallet)}" : "";
            }

            if (_held == null)
            {
                return;
            }

            if (!known)
            {
                _held.text = "";
                return;
            }

            var beyond = _wager > held;
            _held.text = beyond ? $"you have {held:N0} -- not enough" : $"you have {held:N0}";
            _held.color = beyond ? Bad : Faint;
        }

        private static void SetWagerText()
        {
            if (_wagerInput != null)
            {
                _wagerInput.SetTextWithoutNotify(_wager.ToString());
            }
        }

        private static void OnWagerTyped(string typed)
        {
            // An empty or half-typed box is not an error worth shouting about; it is
            // simply not a bet yet. Parsed as long, because a stash holds more than an
            // int and refusing to let someone type their own balance would be absurd.
            _wager = long.TryParse(typed, out var value) && value > 0 ? value : 0;
            UpdateHeld();
        }

        // ------------------------------------------------------------------ building

        private static void Build()
        {
            _font = BorrowFont();

            var canvasObject = new GameObject(RootName, typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 30000;

            var scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);

            // Match height, not a blend. Blending grows the table with screen width, so
            // an ultrawide gets it stretched across the monitor while a 16:9 screen gets
            // a smaller one. Tying it to height keeps one table and lets the extra width
            // stay as empty space, which is what a table on a floor looks like anyway.
            scaler.matchWidthOrHeight = 1f;

            _root = canvasObject;

            _fade = canvasObject.AddComponent<CanvasGroup>();
            _fade.alpha = 0f;

            var backdrop = NewBox("Backdrop", canvasObject.transform, new Color(0f, 0f, 0f, 0.86f), 0, default, 0);
            Stretch(backdrop);

            // The table above, the controls under it.
            //
            // The photograph is an oval, and an oval has far less usable room than the
            // rectangle it sits in: measured, the felt is 1230 by 654 with the cloth
            // narrowing to 58% of the table's width near the bottom edge. The betting
            // bar is 1340 wide and the whole layout wants 750 of height, so none of it
            // fits inside. Putting the cloth above and the controls beneath it is not a
            // compromise either -- it is how a real table is arranged.
            // Sized to fit 1080 with room to spare. The first attempt asked for 1060
            // and put 1076 of content in it, so the buttons at the bottom went off the
            // screen: header 36, table 720, controls 254, two gaps of 14.
            var root = NewBox("Root", canvasObject.transform, new Color(0f, 0f, 0f, 0f), 0, default, 0);
            root.anchorMin = root.anchorMax = new Vector2(0.5f, 0.5f);
            root.pivot = new Vector2(0.5f, 0.5f);
            root.sizeDelta = new Vector2(1400f, 1038f);

            var rootColumn = root.gameObject.AddComponent<VerticalLayoutGroup>();
            rootColumn.childAlignment = TextAnchor.MiddleCenter;
            rootColumn.spacing = 14f;
            rootColumn.childForceExpandWidth = false;
            rootColumn.childForceExpandHeight = false;
            rootColumn.childControlWidth = false;
            rootColumn.childControlHeight = false;

            // The header goes above the table, not on it. Dark text on worn green is
            // hard to read wherever it is put, and the top of an oval is the narrowest
            // part of the cloth.
            BuildHeader(root);

            var photo = Textures.FromFile(TableImagePath);
            RectTransform felt;

            // 1.655 is the photograph's aspect; the drawn table keeps it so that
            // swapping between the two does not move everything else.
            const float tableHeight = 720f;
            const float tableWidth = tableHeight * 1.655f;

            if (photo != null)
            {
                var table = NewImage("Table", root, Color.white);
                table.sprite = photo;
                table.preserveAspect = true;
                var tableRect = (RectTransform)table.transform;
                SetSize(tableRect, tableWidth, tableHeight);

                felt = tableRect;
                _feltFraction = new Vector4(0.084f, 0.146f, 0.079f, 0.187f);
            }
            else
            {
                var rim = NewBox("Rim", root, FeltEdge, 26, Rail, 6);
                SetSize(rim, tableWidth, tableHeight);

                felt = NewBox("Felt", rim, Felt, 20, default, 0);
                felt.anchorMin = Vector2.zero;
                felt.anchorMax = Vector2.one;
                felt.offsetMin = new Vector2(18f, 18f);
                felt.offsetMax = new Vector2(-18f, -18f);

                var vignette = NewImage("Vignette", felt, Color.white);
                vignette.sprite = Textures.Vignette(new Color(0f, 0f, 0f, 0.55f));
                vignette.raycastTarget = false;
                Stretch((RectTransform)vignette.transform);

                _feltFraction = new Vector4(0.03f, 0.03f, 0.03f, 0.03f);
            }

            // What is on the cloth, in one column: the dealer, then the player. Placing
            // them as separate regions meant each was individually plausible and any
            // two could still collide -- a settled hand's BLACKJACK label landed on the
            // betting bar, because a won hand is taller than a live one and nothing
            // said where the space came from.
            var column = NewBox("Column", felt, new Color(0f, 0f, 0f, 0f), 0, default, 0);
            Stretch(column);
            column.offsetMin = new Vector2(tableWidth * _feltFraction.x, tableHeight * _feltFraction.w);
            column.offsetMax = new Vector2(-tableWidth * _feltFraction.z, -tableHeight * _feltFraction.y);

            var flow = column.gameObject.AddComponent<VerticalLayoutGroup>();
            flow.childAlignment = TextAnchor.UpperCenter;
            flow.spacing = 8f;
            flow.childForceExpandWidth = false;
            flow.childForceExpandHeight = false;
            flow.childControlWidth = false;
            flow.childControlHeight = false;

            _cloth = column;

            BuildDealer(column);

            // Air between the dealer's total and the player's cards. Without it the
            // two blocks read as one: the dealer's number sits directly on top of the
            // player's hand, and it is not obvious which side it belongs to.
            SetSize(NewBox("Gap", column, new Color(0f, 0f, 0f, 0f), 0, default, 0), 10f, 26f);

            BuildHands(column);

            // Under the table, on the dark, where there is room for a full-width bar.
            BuildStats(felt);

            BuildBottom(root);
            BuildConfirm(canvasObject.transform);

            EnsureEventSystem();

            BlackjackClientPlugin.Log.LogInfo("[Blackjack] table built");
        }

        /// <summary>
        /// Title on the left, balance on the right, above the table rather than on it.
        ///
        /// Both were previously on the cloth, where worn green under a vignette is a
        /// poor background for small text however it is coloured, and the top of an
        /// oval is its narrowest part. On the dark above the table they are simply
        /// legible.
        /// </summary>
        /// <summary>
        /// The lifetime figures, laid over the cloth.
        ///
        /// On the table rather than in a window of its own, and the cards come off
        /// while it is up: a table with a sheet of numbers lying on it reads as a
        /// table between hands, where a panel floating over a dealt hand is just in
        /// the way of it.
        ///
        /// Built as tiles and rows rather than one block of text. Worn felt under a
        /// vignette is a poor background for a paragraph, and columns of numbers only
        /// line up if something is actually holding them in columns.
        /// </summary>
        private static void BuildStats(RectTransform felt)
        {
            var sheet = NewBox("Stats", felt, new Color(0f, 0f, 0f, 0f), 0, default, 0);
            Stretch(sheet);
            sheet.offsetMin = new Vector2(140f, 118f);
            sheet.offsetMax = new Vector2(-140f, -118f);
            _statsPanel = sheet.gameObject;

            // Something to read the numbers off. The felt is beautiful and completely
            // unsuited to small text.
            var card = NewBox("Sheet", sheet, new Color(0.06f, 0.07f, 0.07f, 0.90f), 14, new Color(1f, 1f, 1f, 0.10f), 2);
            Stretch(card);

            var column = card.gameObject.AddComponent<VerticalLayoutGroup>();
            column.childAlignment = TextAnchor.UpperCenter;
            column.spacing = 12f;
            column.padding = new RectOffset(24, 24, 18, 18);
            column.childForceExpandWidth = false;
            column.childForceExpandHeight = false;
            column.childControlWidth = false;
            column.childControlHeight = false;

            SetSize(Label(card, "STATS", 22f, Gold, TextAlignmentOptions.Center).rectTransform, 700f, 26f);

            _statsTiles = NewRow("Tiles", card, 10f);
            SetSize(_statsTiles, 860f, 82f);

            SetSize(NewBox("Rule", card, new Color(1f, 1f, 1f, 0.10f), 0, default, 0), 820f, 2f);

            // Tall enough for every wallet at once. Six currencies plus the header
            // come to 217, and the sheet has room for 264 -- the first version gave it
            // 150, which was fine until somebody played more than three of them.
            _statsRows = NewBox("Rows", card, new Color(0f, 0f, 0f, 0f), 0, default, 0);
            SetSize(_statsRows, 860f, 240f);

            var rows = _statsRows.gameObject.AddComponent<VerticalLayoutGroup>();
            rows.childAlignment = TextAnchor.UpperCenter;
            rows.spacing = 4f;
            rows.childForceExpandWidth = false;
            rows.childForceExpandHeight = false;
            rows.childControlWidth = false;
            rows.childControlHeight = false;

            _statsEmpty = Label(card, "", 19f, Faint, TextAlignmentOptions.Center);
            SetSize(_statsEmpty.rectTransform, 700f, 26f);

            _statsPanel.SetActive(false);
        }

        /// <summary>
        /// Shows the figures and clears the table, or puts the hand back.
        ///
        /// The round is untouched either way -- it lives on the server, and this only
        /// decides what is drawn. Coming back asks for the state again rather than
        /// trusting what was on screen before.
        /// </summary>
        private static void ToggleStats()
        {
            if (_statsPanel == null || _cloth == null)
            {
                return;
            }

            var showing = !_statsPanel.activeSelf;

            _statsPanel.SetActive(showing);
            _cloth.gameObject.SetActive(!showing);

            if (showing)
            {
                Populate(BlackjackApi.Stats());
            }
            else
            {
                Render(BlackjackApi.State(), true);
            }
        }

        private static void HideStats()
        {
            if (_statsPanel != null && _statsPanel.activeSelf)
            {
                _statsPanel.SetActive(false);

                if (_cloth != null)
                {
                    _cloth.gameObject.SetActive(true);
                }
            }
        }

        private static void Populate(JObject stats)
        {
            Clear(_statsTiles);
            Clear(_statsRows);
            _statsEmpty.text = "";

            if (stats == null)
            {
                _statsEmpty.text = "No answer from the server.";
                _statsEmpty.color = Bad;
                return;
            }

            int Get(string name) => stats[name]?.ToObject<int>() ?? 0;

            var rounds = Get("RoundsPlayed");
            if (rounds == 0)
            {
                _statsEmpty.text = "No hands played yet.";
                _statsEmpty.color = Faint;
                return;
            }

            var wins = Get("Wins");
            var losses = Get("Losses");

            // Pushes excluded: a hand nobody won is not a hand you lost, and counting
            // it against the rate makes a cautious player look worse than they were.
            var decided = wins + losses;
            var rate = decided > 0 ? (100.0 * wins / decided) : 0.0;

            Tile(rounds.ToString("N0"), "rounds", Ink);
            Tile(Get("HandsPlayed").ToString("N0"), "hands", Ink);
            Tile($"{wins:N0}-{losses:N0}-{Get("Pushes"):N0}", "w-l-p", Ink);
            Tile($"{rate:F0}%", "of decided", decided == 0 ? Faint : (rate >= 50.0 ? Good : Bad));
            Tile(Get("Blackjacks").ToString("N0"), "blackjacks", Gold);
            Tile(Get("Busts").ToString("N0"), "busts", Ink);
            Tile($"{Get("CurrentStreak"):N0} / {Get("BestStreak"):N0}", "streak / best", Ink);

            var byCurrency = stats["ByCurrency"] as JObject;
            if (byCurrency == null || !byCurrency.HasValues)
            {
                _statsEmpty.text = "Nothing staked yet.";
                _statsEmpty.color = Faint;
                return;
            }

            MoneyRow("", "staked", "returned", "net", Faint, 17f);

            foreach (var entry in byCurrency.Properties())
            {
                var staked = entry.Value["Wagered"]?.ToObject<long>() ?? 0;
                var back = entry.Value["Returned"]?.ToObject<long>() ?? 0;
                var net = entry.Value["Net"]?.ToObject<long>() ?? (back - staked);

                MoneyRow(
                    Short(entry.Name),
                    staked.ToString("N0"),
                    back.ToString("N0"),
                    (net > 0 ? "+" : "") + net.ToString("N0"),
                    net > 0 ? Good : (net < 0 ? Bad : Faint),
                    20f);
            }
        }

        /// <summary>One figure with its name under it.</summary>
        private static void Tile(string value, string name, Color colour)
        {
            var tile = NewBox("Tile", _statsTiles, new Color(1f, 1f, 1f, 0.04f), 8, new Color(1f, 1f, 1f, 0.07f), 2);
            SetSize(tile, 110f, 78f);

            var inner = tile.gameObject.AddComponent<VerticalLayoutGroup>();
            inner.childAlignment = TextAnchor.MiddleCenter;
            inner.spacing = 2f;
            inner.childForceExpandWidth = false;
            inner.childForceExpandHeight = false;
            inner.childControlWidth = false;
            inner.childControlHeight = false;

            SetSize(Label(tile, value, 23f, colour, TextAlignmentOptions.Center).rectTransform, 104f, 30f);
            SetSize(Label(tile, name, 13f, Faint, TextAlignmentOptions.Center).rectTransform, 104f, 18f);
        }

        /// <summary>
        /// One currency across four columns. Fixed widths, because numbers that do not
        /// line up are harder to read than numbers that are simply small.
        /// </summary>
        private static void MoneyRow(string wallet, string staked, string back, string net, Color netColour, float size)
        {
            var row = NewRow("Row", _statsRows, 0f);
            SetSize(row, 840f, size + 8f);

            SetSize(Label(row, wallet, size, Faint, TextAlignmentOptions.Left).rectTransform, 120f, size + 6f);
            SetSize(Label(row, staked, size, Ink, TextAlignmentOptions.Right).rectTransform, 250f, size + 6f);
            SetSize(Label(row, back, size, Ink, TextAlignmentOptions.Right).rectTransform, 250f, size + 6f);
            SetSize(Label(row, net, size, netColour, TextAlignmentOptions.Right).rectTransform, 220f, size + 6f);
        }

        private static void BuildHeader(RectTransform parent)
        {
            var bar = NewBox("Header", parent, new Color(0f, 0f, 0f, 0f), 0, default, 0);
            SetSize(bar, 1340f, 36f);

            var title = Label(bar, "BLACKJACK", 28f, Ink, TextAlignmentOptions.Left);
            Anchor(title.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(210f, 0f), new Vector2(400f, 36f));

            _balance = Label(bar, "", 24f, Ink, TextAlignmentOptions.Right);
            Anchor(_balance.rectTransform, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-210f, 0f), new Vector2(500f, 36f));
        }

        private static void BuildDealer(RectTransform column)
        {
            SetSize(Label(column, "DEALER", 19f, Faint, TextAlignmentOptions.Center).rectTransform, 400f, 24f);

            _dealerCards = NewRow("DealerCards", column, 10f);
            SetSize(_dealerCards, 820f, CardView.Height);

            _dealerValue = Label(column, "", 26f, Ink, TextAlignmentOptions.Center);
            SetSize(_dealerValue.rectTransform, 300f, 30f);
        }

        /// <summary>
        /// Where the player's hands sit, with the betting spot painted underneath. The
        /// spot occupies the same block, so an empty table is not a void and a dealt
        /// one does not shift everything below it.
        /// </summary>
        private static void BuildHands(RectTransform column)
        {
            var area = NewBox("HandArea", column, new Color(0f, 0f, 0f, 0f), 0, default, 0);
            SetSize(area, 940f, 214f);

            var spot = NewImage("Spot", area, Gold);
            spot.sprite = Textures.Ring(Gold);
            spot.raycastTarget = false;
            var spotRect = (RectTransform)spot.transform;
            spotRect.anchorMin = spotRect.anchorMax = new Vector2(0.5f, 0.5f);
            spotRect.pivot = new Vector2(0.5f, 0.5f);
            spotRect.sizeDelta = new Vector2(150f, 150f);
            spotRect.anchoredPosition = Vector2.zero;
            _bettingSpot = spot.gameObject;

            var place = Label(spotRect, "PLACE\nYOUR BET", 15f, Gold, TextAlignmentOptions.Center);
            place.enableWordWrapping = true;
            Stretch(place.rectTransform);

            _handsRow = NewRow("Hands", area, 48f);
            Stretch(_handsRow);
        }

        /// <summary>
        /// Everything under the cloth, in one stack: betting bar, message, buttons.
        ///
        /// Stacked rather than placed, because placing them by hand is exactly how the
        /// error message ended up behind the DEAL button.
        /// </summary>
        private static void BuildBottom(RectTransform parent)
        {
            var stack = NewBox("Bottom", parent, new Color(0f, 0f, 0f, 0f), 0, default, 0);
            SetSize(stack, 900f, 254f);

            var column = stack.gameObject.AddComponent<VerticalLayoutGroup>();
            column.childAlignment = TextAnchor.UpperCenter;
            column.spacing = 10f;
            column.childForceExpandWidth = false;
            column.childForceExpandHeight = false;
            column.childControlWidth = false;
            column.childControlHeight = false;

            BuildBetting(stack);

            _message = Label(stack, "", 20f, Faint, TextAlignmentOptions.Center);
            SetSize(_message.rectTransform, 880f, 26f);

            _actionRow = NewRow("Actions", stack, 14f);
            SetSize(_actionRow, 880f, 48f);

            var footer = NewRow("Footer", stack, 14f);
            SetSize(footer, 880f, 44f);

            _statsButton = Chip(footer, "STATS", 180f, ToggleStats);

            _leave = Chip(footer, "LEAVE TABLE", 220f, Close);
        }

        private static void BuildBetting(RectTransform parent)
        {
            // A warm edge rather than a grey one, so the bar reads as part of the same
            // furniture as the table above it rather than a dialog parked underneath.
            var holder = NewBox("Betting", parent, new Color(0.04f, 0.05f, 0.04f, 0.74f), 12, new Color(0.31f, 0.20f, 0.11f, 0.85f), 2);
            // Sized to its contents rather than to the table above it. The rows come
            // to 832 for the wallets and 852 for the bet; at 1340 the box carried 460
            // of empty space on either side of them, which is what made it look like a
            // panel the controls had been dropped into rather than one built for them.
            SetSize(holder, 880f, 118f);
            _betControls = holder.gameObject;

            var column = holder.gameObject.AddComponent<VerticalLayoutGroup>();
            column.childAlignment = TextAnchor.MiddleCenter;
            column.spacing = 10f;
            column.padding = new RectOffset(14, 14, 12, 12);
            column.childForceExpandWidth = false;
            column.childForceExpandHeight = false;
            column.childControlWidth = false;
            column.childControlHeight = false;

            var walletRow = NewRow("Wallets", holder, 8f);
            SetSize(walletRow, 832f, 44f);

            foreach (var wallet in new[] { "Roubles", "Dollars", "Euros", "GpCoins", "Bitcoin", "LegaMedals" })
            {
                var captured = wallet;
                Wallets.Add((wallet, Chip(walletRow, Short(wallet), 132f, () => ChooseWallet(captured))));
            }

            var betRow = NewRow("Bet", holder, 12f);
            SetSize(betRow, 852f, 46f);

            SetSize(Label(betRow, "BET", 20f, Faint, TextAlignmentOptions.Right).rectTransform, 56f, 32f);
            BuildWagerInput(betRow);
            Chip(betRow, "ALL IN", 120f, AskBetEverything);

            _held = Label(betRow, "", 19f, Faint, TextAlignmentOptions.Left);
            SetSize(_held.rectTransform, 300f, 32f);

            HighlightWallet();
        }

        /// <summary>
        /// A field the player types into, rather than a stepper they wear out.
        ///
        /// Wide, and centred. The first version was Unity's default hundred-pixel
        /// square with the text left-aligned, so anything past five digits scrolled out
        /// of sight while it was being typed.
        /// </summary>
        private static void BuildWagerInput(Transform parent)
        {
            // Recessed rather than raised: the gradient runs the other way, so the field
            // reads as a slot cut into the bar while the buttons sit proud of it.
            var frame = NewBox("WagerInput", parent, new Color(0.06f, 0.06f, 0.07f, 1f), 8, ChipEdge, 2);
            frame.GetComponent<Image>().sprite = Textures.ButtonFace(
                8, new Color(0.05f, 0.05f, 0.06f, 1f), new Color(0.11f, 0.11f, 0.12f, 1f), ChipEdge);
            SetSize(frame, 340f, 44f);

            var viewport = new GameObject("TextArea", typeof(RectTransform), typeof(RectMask2D));
            viewport.transform.SetParent(frame, false);
            var viewportRect = (RectTransform)viewport.transform;
            Stretch(viewportRect);
            viewportRect.offsetMin = new Vector2(10f, 5f);
            viewportRect.offsetMax = new Vector2(-10f, -5f);

            var text = Label(viewportRect, string.Empty, 22f, Ink, TextAlignmentOptions.Center);
            Stretch(text.rectTransform);
            text.raycastTarget = true;

            var input = frame.gameObject.AddComponent<TMP_InputField>();
            input.textViewport = viewportRect;
            input.textComponent = text;
            input.fontAsset = _font;
            input.pointSize = 22f;
            input.contentType = TMP_InputField.ContentType.IntegerNumber;

            // Long enough for any stash. No cap here on purpose: what counts as a legal
            // bet is the server's business, not this field's.
            input.characterLimit = 15;
            input.restoreOriginalTextOnEscape = true;
            input.text = _wager.ToString();
            input.onValueChanged.AddListener(OnWagerTyped);

            // The field is a Selectable too, so it can carry the same states as the
            // buttons beside it rather than being the one dead-looking control.
            input.transition = Selectable.Transition.SpriteSwap;
            input.targetGraphic = frame.GetComponent<Image>();
            input.spriteState = new SpriteState
            {
                highlightedSprite = Textures.RoundedBox(8, new Color(0.13f, 0.14f, 0.14f, 1f), Gold, 2),
                pressedSprite = Textures.RoundedBox(8, new Color(0.13f, 0.14f, 0.14f, 1f), Gold, 2),
                selectedSprite = Textures.RoundedBox(8, new Color(0.13f, 0.14f, 0.14f, 1f), Gold, 2),
            };

            _wagerInput = input;
        }

        private static void BuildConfirm(Transform parent)
        {
            var root = NewBox("Confirm", parent, new Color(0f, 0f, 0f, 0.66f), 0, default, 0);
            Stretch(root);
            _confirm = root.gameObject;

            var box = NewBox("Box", root, new Color(0.10f, 0.10f, 0.11f, 0.99f), 16, ChipEdge, 2);
            box.anchorMin = box.anchorMax = new Vector2(0.5f, 0.5f);
            box.pivot = new Vector2(0.5f, 0.5f);
            box.sizeDelta = new Vector2(600f, 260f);

            _confirmText = Label(box, "", 24f, Ink, TextAlignmentOptions.Center);
            _confirmText.enableWordWrapping = true;
            Anchor(_confirmText.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -86f), new Vector2(540f, 130f));

            var buttons = NewRow("Buttons", box, 16f);
            Anchor(buttons, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 46f), new Vector2(540f, 46f));

            Chip(buttons, "BET IT ALL", 210f, ConfirmBetEverything);
            Chip(buttons, "CANCEL", 170f, CancelConfirm);

            _confirm.SetActive(false);
        }

        // ------------------------------------------------------------------- widgets

        private static void HighlightWallet()
        {
            foreach (var (wallet, chip) in Wallets)
            {
                if (chip == null)
                {
                    continue;
                }

                // Restyled rather than recoloured, so the chosen wallet keeps its hover
                // and pressed states instead of losing them the moment it is selected.
                StyleChip(chip, wallet == _wallet);

                // A lit brass face wants dark text on it; pale text on brass is the one
                // combination that does not read.
                var label = chip.GetComponentInChildren<TextMeshProUGUI>();
                if (label != null)
                {
                    label.color = wallet == _wallet ? new Color(0.10f, 0.09f, 0.06f, 1f) : Ink;
                }
            }
        }

        private static string Short(string wallet) => wallet switch
        {
            "Roubles" => "RUB",
            "Dollars" => "USD",
            "Euros" => "EUR",
            "GpCoins" => "GP",
            "Bitcoin" => "BTC",
            "LegaMedals" => "LEGA",
            _ => wallet.ToUpperInvariant(),
        };

        private static GameObject Chip(Transform parent, string text, float width, Action onClick, bool primary = false)
        {
            var rect = NewBox("Chip_" + text, parent, ChipTop, 8, ChipEdge, 2);
            SetSize(rect, width, 44f);

            // Letter-spaced, which is how a label is engraved onto a control rather
            // than printed near it.
            // Dark on brass, pale on steel. Light text on a lit brass face is the one
            // combination that stops reading, and the wallet chips already do this.
            var label = Label(rect, text, 19f, primary ? new Color(0.10f, 0.09f, 0.06f, 1f) : Ink, TextAlignmentOptions.Center);
            label.characterSpacing = 6f;
            Stretch(label.rectTransform);

            var button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = rect.GetComponent<Image>();
            button.onClick.AddListener(() => onClick());

            StyleChip(rect.gameObject, primary);

            return rect.gameObject;
        }

        /// <summary>
        /// Gives a button its hover and pressed states.
        ///
        /// Sprite swapping rather than colour tinting, which is Unity's default and is
        /// useless here. A tint multiplies the graphic's colour, and these buttons are
        /// white images carrying a dark sprite -- multiplying white by the default
        /// highlight of 0.96 grey is a change nobody can see. That is why hovering did
        /// nothing at all.
        ///
        /// Hovering lifts the fill and turns the border gold, which reads as lit rather
        /// than merely different, and matches the gold already used for the table's own
        /// markings.
        /// </summary>
        private static void StyleChip(GameObject chip, bool primary)
        {
            var image = chip.GetComponent<Image>();
            var button = chip.GetComponent<Button>();
            if (image == null || button == null)
            {
                return;
            }

            var top = primary ? BrassTop : ChipTop;
            var bottom = primary ? BrassBottom : ChipBottom;
            var edge = primary ? BrassEdge : ChipEdge;

            // Hovering lifts both stops and lights the edge gold; pressing darkens and
            // flattens them, which reads as the face going in rather than merely
            // changing colour.
            var normal = Textures.ButtonFace(8, top, bottom, edge);
            var hover = Textures.ButtonFace(8, Lift(top, 0.09f), Lift(bottom, 0.07f), Gold);
            var pressed = Textures.ButtonFace(8, Lift(bottom, 0.02f), Lift(bottom, -0.02f), Gold);

            image.sprite = normal;
            image.type = Image.Type.Sliced;

            button.transition = Selectable.Transition.SpriteSwap;
            button.spriteState = new SpriteState
            {
                highlightedSprite = hover,
                pressedSprite = pressed,
                selectedSprite = normal,
                disabledSprite = normal,
            };
        }

        /// <summary>Lightens or darkens a colour, keeping its alpha.</summary>
        private static Color Lift(Color colour, float amount) => new Color(
            Mathf.Clamp01(colour.r + amount),
            Mathf.Clamp01(colour.g + amount),
            Mathf.Clamp01(colour.b + amount),
            colour.a);

        private static TextMeshProUGUI Label(Transform parent, string text, float size, Color colour, TextAlignmentOptions align)
        {
            var go = new GameObject("Label", typeof(RectTransform));
            go.transform.SetParent(parent, false);

            var label = go.AddComponent<TextMeshProUGUI>();
            if (_font != null)
            {
                label.font = _font;
            }

            label.text = text;
            label.fontSize = size;
            label.color = colour;
            label.alignment = align;
            label.enableWordWrapping = false;
            label.raycastTarget = false;
            return label;
        }

        private static Image NewImage(string name, Transform parent, Color colour)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.transform.SetParent(parent, false);
            var image = go.GetComponent<Image>();
            image.color = colour;
            return image;
        }

        /// <summary>A rounded panel. Radius zero means a plain rectangle.</summary>
        private static RectTransform NewBox(string name, Transform parent, Color fill, int radius, Color border, int borderWidth)
        {
            var image = NewImage(name, parent, Color.white);

            if (radius > 0)
            {
                image.sprite = Textures.RoundedBox(radius, fill, border, borderWidth);
                image.type = Image.Type.Sliced;
            }
            else
            {
                image.color = fill;
            }

            return (RectTransform)image.transform;
        }

        private static RectTransform NewRow(string name, Transform parent, float spacing)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);

            var layout = go.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = spacing;
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;
            layout.childControlWidth = false;
            layout.childControlHeight = false;

            return (RectTransform)go.transform;
        }

        /// <summary>
        /// Sets a size that both a layout group and a bare RectTransform will respect.
        /// These rows do not control their children's size, so sizeDelta is what counts
        /// -- a LayoutElement on its own is silently ignored, which is how the wager
        /// field ended up as Unity's default hundred-pixel square.
        /// </summary>
        private static void SetSize(RectTransform rect, float width, float height)
        {
            rect.sizeDelta = new Vector2(width, height);

            var element = rect.gameObject.GetComponent<LayoutElement>() ?? rect.gameObject.AddComponent<LayoutElement>();
            element.preferredWidth = width;
            element.preferredHeight = height;
            element.minHeight = height;
        }

        private static void Anchor(RectTransform rect, Vector2 min, Vector2 max, Vector2 position, Vector2 size)
        {
            rect.anchorMin = min;
            rect.anchorMax = max;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

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

        /// <summary>
        /// Nothing on a canvas is clickable without an EventSystem in the scene. EFT
        /// has one, so this almost never fires -- but a table nobody can press is a
        /// silent, baffling failure, and the check costs nothing.
        /// </summary>
        private static void EnsureEventSystem()
        {
            if (UnityEngine.EventSystems.EventSystem.current != null)
            {
                return;
            }

            BlackjackClientPlugin.Log.LogWarning("[Blackjack] no EventSystem in the scene; adding one.");

            var go = new GameObject("BlackjackEventSystem",
                typeof(UnityEngine.EventSystems.EventSystem),
                typeof(UnityEngine.EventSystems.StandaloneInputModule));

            UnityEngine.Object.DontDestroyOnLoad(go);
        }

        private static TMP_FontAsset BorrowFont()
        {
            try
            {
                var label = UnityEngine.Object.FindObjectsOfType<TextMeshProUGUI>()
                    .FirstOrDefault(t => t != null && t.font != null);

                if (label != null)
                {
                    return label.font;
                }
            }
            catch (Exception ex)
            {
                BlackjackClientPlugin.Log.LogWarning("[Blackjack] could not borrow a font: " + ex.Message);
            }

            return TMP_Settings.defaultFontAsset;
        }
    }
}
