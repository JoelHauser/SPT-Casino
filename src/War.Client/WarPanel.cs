using Casino.Shared;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace War.Client
{
    /// <summary>
    /// The war table.
    ///
    /// Two cards face each other across the cloth: the dealer's above, the player's
    /// below, the higher one wins. A tie stops the round and asks a question -- go to
    /// war or surrender -- and a war deals a second card to each side beside the first,
    /// with the three burned cards left face down on the felt where they can be counted.
    ///
    /// Built from Blackjack's pieces on purpose: the same table photograph (already
    /// beside the plugin), the same chips, the same money boxes. A player walking from
    /// one table to the other should feel they are in the same room.
    ///
    /// This side renders and asks. It never shuffles, never compares two cards and never
    /// decides who won; every card and every amount comes from the server.
    /// </summary>
    internal static class WarPanel
    {
        internal const string RootName = "WarPanel";

        private static readonly Color Felt = new Color(0.055f, 0.30f, 0.17f, 1f);
        private static readonly Color FeltEdge = new Color(0.21f, 0.13f, 0.07f, 1f);
        private static readonly Color Rail = new Color(0.31f, 0.20f, 0.11f, 1f);
        private static readonly Color Ink = new Color(0.92f, 0.91f, 0.86f, 1f);
        private static readonly Color Faint = new Color(0.66f, 0.72f, 0.64f, 1f);
        private static readonly Color Gold = new Color(0.78f, 0.68f, 0.38f, 0.90f);
        private static readonly Color Good = new Color(0.55f, 0.82f, 0.45f, 1f);
        private static readonly Color Bad = new Color(0.92f, 0.42f, 0.36f, 1f);
        private static readonly Color ChipTop = new Color(0.17f, 0.18f, 0.19f, 0.97f);
        private static readonly Color ChipBottom = new Color(0.09f, 0.10f, 0.11f, 0.97f);
        private static readonly Color ChipEdge = new Color(0.30f, 0.28f, 0.24f, 1f);
        private static readonly Color BrassTop = new Color(0.55f, 0.43f, 0.16f, 1f);
        private static readonly Color BrassBottom = new Color(0.33f, 0.25f, 0.08f, 1f);
        private static readonly Color BrassEdge = new Color(0.72f, 0.58f, 0.26f, 1f);

        // Where the cards sit on the cloth, from the centre of their row. The first card
        // and the war card each have a fixed place, so a war arriving does not slide the
        // card that started it sideways -- a row laid out by a group would.
        private const float FirstCardX = -70f;
        private const float WarCardX = 70f;
        private const float BurnX = -330f;

        // Blackjack's pace, not Poker's: a round here is two cards, and the slower deal
        // reads as waiting rather than as drama.
        private const float DealStagger = 0.22f;
        private const float DealDuration = 0.35f;

        private static GameObject _root;
        private static TMP_FontAsset _font;
        private static CanvasGroup _fade;
        private static Coroutine _fading;

        private static TextMeshProUGUI _balance;
        private static TextMeshProUGUI _message;
        private static TextMeshProUGUI _headline;
        private static TextMeshProUGUI _subline;
        private static TextMeshProUGUI _feltPrint;
        private static TextMeshProUGUI _warMark;
        private static TextMeshProUGUI _held;
        private static RectTransform _dealerRow;
        private static RectTransform _playerRow;
        private static RectTransform _burnPile;
        private static RectTransform _shoe;
        private static RectTransform _actionRow;
        private static RectTransform _cloth;
        private static GameObject _betControls;
        private static GameObject _leave;
        private static GameObject _statsButton;
        private static GameObject _statsPanel;
        private static RectTransform _statsTiles;
        private static RectTransform _statsRows;
        private static TextMeshProUGUI _statsEmpty;
        private static TMP_InputField _anteInput;
        private static TMP_InputField _tieInput;

        private static readonly List<(string Wallet, GameObject Chip)> Wallets = new List<(string, GameObject)>();
        private static readonly Dictionary<string, long> Balances = new Dictionary<string, long>();
        private static readonly Dictionary<string, long> Maximums = new Dictionary<string, long>();
        private static readonly Dictionary<string, long> Minimums = new Dictionary<string, long>();

        /// <summary>
        /// What each place on the cloth last showed, so a redraw only deals the cards
        /// that are new -- the same idea as Blackjack's. Cleared when a new hand is
        /// dealt, so a card that happens to repeat in the same place still flies in.
        /// </summary>
        private static readonly Dictionary<string, string> Dealt = new Dictionary<string, string>();

        private static string _wallet = "Roubles";
        private static long _ante = 10_000;
        private static long _tie;
        private static long _absoluteMax = 100_000_000;
        private static int _tiePays = 10;

        /// <summary>
        /// True while cards are moving. Buttons do nothing until they stop, so a double
        /// click cannot deal a second hand over a first that has not finished landing.
        /// </summary>
        private static bool _busy;

        private static bool _rewriting;

        internal static bool IsOpen => _root != null && _root.activeSelf;

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

                // A canvas built this frame has had no layout pass yet. See Blackjack.
                Canvas.ForceUpdateCanvases();
                LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)_root.transform);

                _busy = false;
                RefreshBalances();

                // Resume rather than assume: a tie can still be waiting from an earlier
                // visit. Drawn where it lies, not dealt in again.
                Dealt.Clear();
                Render(WarApi.State(), animate: false);
            }
            catch (Exception ex)
            {
                WarClientPlugin.Log?.LogError("[War] could not open the table: " + ex);
            }
        }

        internal static void Close()
        {
            HideStats();

            if (_root == null || !_root.activeSelf)
            {
                return;
            }

            StartFade(0f, true);
        }

        // ------------------------------------------------------------------- actions

        private static void Deal()
        {
            if (_busy)
            {
                return;
            }

            if (_ante <= 0)
            {
                Say("Type an amount to bet first.", Bad);
                return;
            }

            // Caught here only to save a round trip. The server checks again and is the
            // authority.
            if (Balances.TryGetValue(_wallet, out var held) && _ante + _tie > held)
            {
                Say($"That hand costs {_ante + _tie:N0} {Short(_wallet)} and you have {held:N0}.", Bad);
                return;
            }

            HideStats();

            var reply = WarApi.Deal(_wallet, _ante, _tie);

            if (reply?["Ok"]?.ToObject<bool>() == true)
            {
                SoundBoard.Play(Cue.ChipBet);

                // A new hand: every place on the cloth deals in again, even a card that
                // happens to match the one that sat there last time.
                Dealt.Clear();
            }

            Render(reply, animate: true);
        }

        private static void Decide(string choice)
        {
            if (_busy)
            {
                return;
            }

            var reply = WarApi.Decide(choice);

            if (choice == "War" && reply?["Ok"]?.ToObject<bool>() == true)
            {
                SoundBoard.Play(Cue.ChipBet);
            }

            Render(reply, animate: true);
        }

        private static void ChooseWallet(string wallet)
        {
            if (_busy)
            {
                return;
            }

            _wallet = wallet;

            // A rouble bet carried over to dollars is a hundred times the money.
            _ante = wallet == "Roubles" ? 10_000 : 100;
            _tie = 0;
            SetText(_anteInput, _ante);
            SetText(_tieInput, _tie);
            HighlightWallet();
            UpdateHeld();
        }

        private static long CeilingFor(string wallet)
        {
            if (WarClientPlugin.NoBetCap?.Value == true)
            {
                return _absoluteMax;
            }

            return Maximums.TryGetValue(wallet, out var max) && max > 0 ? max : 0;
        }

        // ----------------------------------------------------------------- rendering

        private static void Render(JObject response, bool animate)
        {
            if (response == null)
            {
                Say("No answer from the server. Is it running?", Bad);
                return;
            }

            var ok = response["Ok"]?.ToObject<bool>() ?? false;
            var error = response["Error"]?.ToString();
            var note = response["Note"]?.ToString();
            var round = response["Round"] as JObject;

            if (!ok && !string.IsNullOrEmpty(error))
            {
                Say(error, Bad);
            }
            else if (!string.IsNullOrEmpty(note))
            {
                Say(note, Faint);
            }
            else if (round?["Shuffled"]?.ToObject<bool>() == true)
            {
                Say("A fresh shoe: six decks, shuffled.", Faint);
            }
            else
            {
                Say("", Faint);
            }

            // The round's own currency, so a tie left waiting in dollars reopens in
            // dollars rather than under whatever the chips last said.
            var wallet = response["Wallet"]?.ToString();
            if (round != null && (round["Phase"]?.ToString() ?? "AwaitingBet") != "AwaitingBet"
                && !string.IsNullOrEmpty(wallet) && wallet != _wallet)
            {
                _wallet = wallet;
                HighlightWallet();
            }

            var finish = DrawRound(round, animate);

            // The headline, the balance, the buttons and the stash are all held back
            // until the cards that decided them have landed. Reading WIN before the
            // dealer's card arrives is reading the result off the wrong thing.
            _busy = finish > 0f;
            ClearActions();

            DealAnimator.After(finish, () =>
            {
                _busy = false;

                if (_root == null)
                {
                    return;
                }

                ShowHeadline(round);
                RenderActions(round);

                if (response["Balance"] != null && !string.IsNullOrEmpty(wallet))
                {
                    Balances[wallet] = response["Balance"].ToObject<long>();
                    UpdateHeld();
                }

                // Only now. The money moved the moment the server answered, but telling
                // the game straight away puts the result in the rouble counter behind
                // the table while the cards are still in the air -- the lesson Roulette
                // learned with its wheel.
                ProfileSync.Request("WarSync");
            });
        }

        /// <summary>
        /// Puts every card on the cloth and deals in the ones that are new. Returns when,
        /// in seconds from now, the last of them stops moving.
        /// </summary>
        private static float DrawRound(JObject round, bool animate)
        {
            Clear(_dealerRow);
            Clear(_playerRow);
            Clear(_burnPile);

            var phase = round?["Phase"]?.ToString() ?? "AwaitingBet";
            var player = round?["PlayerCard"]?.ToString();
            var dealer = round?["DealerCard"]?.ToString();
            var playerWar = round?["PlayerWarCard"]?.ToString();
            var dealerWar = round?["DealerWarCard"]?.ToString();
            var burned = round?["Burned"]?.ToObject<int>() ?? 0;

            _feltPrint.gameObject.SetActive(string.IsNullOrEmpty(player));
            _warMark.gameObject.SetActive(!string.IsNullOrEmpty(playerWar));
            _headline.text = "";
            _subline.text = "";

            if (phase == "AwaitingBet" || string.IsNullOrEmpty(player))
            {
                return 0f;
            }

            var sequence = 0;
            var finish = 0f;

            // The order a dealer would deal them: player, dealer, then for a war three
            // burned, the player's war card and the dealer's.
            Place(_playerRow, player, FirstCardX, 1f, "player", animate, ref sequence, ref finish);
            Place(_dealerRow, dealer, FirstCardX, 1f, "dealer", animate, ref sequence, ref finish);

            for (var i = 0; i < burned; i++)
            {
                Place(_burnPile, null, i * 16f, 0.62f, "burn" + i, animate, ref sequence, ref finish);
            }

            if (!string.IsNullOrEmpty(playerWar))
            {
                Place(_playerRow, playerWar, WarCardX, 1f, "playerWar", animate, ref sequence, ref finish);
                Place(_dealerRow, dealerWar, WarCardX, 1f, "dealerWar", animate, ref sequence, ref finish);
            }

            return finish;
        }

        /// <summary>
        /// One card at a fixed place in its row, dealt in from the shoe if that place
        /// did not already show it.
        /// </summary>
        private static void Place(
            RectTransform row, string code, float x, float scale, string key, bool animate, ref int sequence, ref float finish)
        {
            var card = CardView.BuildSlotted(row, code, _font, scale);
            var slot = (RectTransform)card.transform.parent;
            slot.anchorMin = slot.anchorMax = new Vector2(0.5f, 0.5f);
            slot.pivot = new Vector2(0.5f, 0.5f);
            slot.anchoredPosition = new Vector2(x, 0f);

            var state = code ?? "back";

            if (Dealt.TryGetValue(key, out var previous) && previous == state)
            {
                return;
            }

            Dealt[key] = state;

            if (!animate)
            {
                return;
            }

            var delay = sequence++ * DealStagger;
            DealAnimator.Deal(card, delay, _shoe, DealDuration);
            finish = Mathf.Max(finish, DealAnimator.FinishTime(delay, DealDuration));
        }

        /// <summary>The big line in the middle of the cloth, and the money under it.</summary>
        private static void ShowHeadline(JObject round)
        {
            var phase = round?["Phase"]?.ToString() ?? "AwaitingBet";
            var outcome = round?["Outcome"]?.ToString() ?? "None";
            var profit = round?["Profit"]?.ToObject<long>() ?? 0;
            var ante = round?["Ante"]?.ToObject<long>() ?? 0;
            var tieReturn = round?["TieReturn"]?.ToObject<long>() ?? 0;
            var cur = Short(_wallet);

            var tieNote = tieReturn > 0 ? $"   tie bet paid {tieReturn:N0}" : "";

            if (phase == "AwaitingDecision")
            {
                Headline("TIE", Gold);
                _subline.text = $"Go to war for {ante:N0} more, or surrender and take back {ante - (ante / 2):N0}.{tieNote}";
                _subline.color = Ink;
                return;
            }

            if (phase != "Settled")
            {
                return;
            }

            switch (outcome)
            {
                case "Win":
                    Headline("YOU WIN", Good);
                    break;
                case "Lose":
                    Headline("DEALER WINS", Bad);
                    break;
                case "Surrender":
                    Headline("SURRENDERED", Faint);
                    break;
                case "WarWin":
                    Headline("YOU WIN THE WAR", Good);
                    break;
                case "WarLose":
                    Headline("DEALER WINS THE WAR", Bad);
                    break;
            }

            _subline.text = (profit > 0 ? "+" : "") + $"{profit:N0} {cur}{tieNote}";
            _subline.color = profit > 0 ? Good : (profit < 0 ? Bad : Faint);
        }

        private static void Headline(string text, Color colour)
        {
            _headline.text = text;
            _headline.color = colour;
        }

        private static void ClearActions()
        {
            Clear(_actionRow);
        }

        private static void RenderActions(JObject round)
        {
            ClearActions();

            var phase = round?["Phase"]?.ToString() ?? "AwaitingBet";
            var deciding = phase == "AwaitingDecision";

            _betControls.SetActive(!deciding);

            // No leaving with a tie on the table from here -- the ante is still in play.
            // Escape still closes the table, and the tie is waiting when it reopens.
            _leave.SetActive(!deciding);
            _statsButton.SetActive(!deciding);

            if (deciding)
            {
                var ante = round["Ante"]?.ToObject<long>() ?? 0;
                Chip(_actionRow, $"GO TO WAR  +{ante:N0}", 300f, () => Decide("War"), primary: true);
                Chip(_actionRow, $"SURRENDER  {ante - (ante / 2):N0} BACK", 320f, () => Decide("Surrender"));
                return;
            }

            var deal = Chip(_actionRow, "DEAL", 200f, Deal, primary: true);

            // Greyed when the bet cannot be placed, rather than looking ready and then
            // refusing. Still clickable: the refusal explains itself.
            var ceiling = CeilingFor(_wallet);
            var within = ceiling <= 0 || (_ante <= ceiling && _tie <= ceiling);
            var affordable = within
                && (!Balances.TryGetValue(_wallet, out var held) || (_ante > 0 && _ante + _tie <= held));

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

            var label = deal.GetComponentInChildren<TextMeshProUGUI>();
            if (label != null)
            {
                label.color = new Color(0.50f, 0.48f, 0.46f, 1f);
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
            var ping = WarApi.Ping();

            if (ping == null)
            {
                return;
            }

            if (ping["Balances"] is JObject balances)
            {
                Balances.Clear();
                foreach (var entry in balances.Properties())
                {
                    Balances[entry.Name] = entry.Value.ToObject<long>();
                }
            }

            if (ping["Limits"] is JObject limits)
            {
                Maximums.Clear();
                Minimums.Clear();
                foreach (var entry in limits.Properties())
                {
                    Maximums[entry.Name] = entry.Value?["Max"]?.ToObject<long>() ?? 0;
                    Minimums[entry.Name] = entry.Value?["Min"]?.ToObject<long>() ?? 0;
                }
            }

            _absoluteMax = ping["AbsoluteMax"]?.ToObject<long>() ?? _absoluteMax;
            _tiePays = ping["TiePays"]?.ToObject<int>() ?? _tiePays;

            // Printed on the cloth the way a real table prints its rules -- and read
            // from the server, so the cloth cannot promise odds the table does not pay.
            var edge = ping["HouseEdgeWar"]?.ToObject<double>() ?? 0d;
            _feltPrint.text = $"TIE PAYS {_tiePays} TO 1\n<size=60%>ON A TIE: GO TO WAR, OR SURRENDER HALF YOUR BET"
                + (edge > 0d ? $"\nHOUSE EDGE {edge:F2}% GOING TO WAR" : "") + "</size>";

            var note = ping["Note"]?.ToString();
            if (!string.IsNullOrEmpty(note))
            {
                Say(note, Faint);
            }

            UpdateHeld();
        }

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

            var ceiling = CeilingFor(_wallet);
            if (ceiling > 0 && (_ante > ceiling || _tie > ceiling))
            {
                _held.text = $"up to {ceiling:N0} a bet";
                _held.color = Bad;
                return;
            }

            var beyond = _ante + _tie > held;
            _held.text = beyond ? $"you have {held:N0} -- not enough" : $"you have {held:N0}";
            _held.color = beyond ? Bad : Faint;
        }

        private static void SetText(TMP_InputField input, long value)
        {
            if (input == null)
            {
                return;
            }

            _rewriting = true;
            input.SetTextWithoutNotify(value > 0 ? MoneyField.Format(value) : "");
            _rewriting = false;
        }

        private static void OnAnteTyped(string typed)
        {
            if (_rewriting)
            {
                return;
            }

            _rewriting = true;
            _ante = MoneyField.Reformat(_anteInput, typed);
            _rewriting = false;
            UpdateHeld();
        }

        private static void OnTieTyped(string typed)
        {
            if (_rewriting)
            {
                return;
            }

            _rewriting = true;
            _tie = MoneyField.Reformat(_tieInput, typed);
            _rewriting = false;
            UpdateHeld();
        }

        // ------------------------------------------------------------------ building

        private static void Build()
        {
            _font = BorrowFont();

            var canvasObject = new GameObject(RootName, typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            // With the other tables, above the lobby. See the root CLAUDE.md.
            canvas.sortingOrder = 30000;

            var scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 1f;

            _root = canvasObject;

            _fade = canvasObject.AddComponent<CanvasGroup>();
            _fade.alpha = 0f;

            var backdrop = NewBox("Backdrop", canvasObject.transform, new Color(0f, 0f, 0f, 0.86f), 0, default, 0);
            Stretch(backdrop);

            // Blackjack's arrangement: header, table, controls underneath, in one column
            // sized to fit 1080 with room to spare.
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

            BuildHeader(root);

            const float tableHeight = 690f;
            const float tableWidth = tableHeight * 1.655f;

            // Blackjack's photograph, which the pack script already stages beside the
            // plugin. Its cloth was measured off the image: 8.4% in from the left, 7.9%
            // from the right, 14.6% down and 18.7% up.
            var photo = Textures.FromFile(System.IO.Path.Combine(Host.AssetFolder, "table.png"));
            RectTransform felt;
            Vector4 inset;

            if (photo != null)
            {
                var table = NewImage("Table", root, Color.white);
                table.sprite = photo;
                table.preserveAspect = true;
                felt = (RectTransform)table.transform;
                SetSize(felt, tableWidth, tableHeight);
                inset = new Vector4(0.084f, 0.146f, 0.079f, 0.187f);
            }
            else
            {
                var rim = NewBox("Rim", root, FeltEdge, 26, Rail, 6);
                SetSize(rim, tableWidth, tableHeight);

                felt = NewBox("Felt", rim, Felt, 20, default, 0);
                Stretch(felt);
                felt.offsetMin = new Vector2(18f, 18f);
                felt.offsetMax = new Vector2(-18f, -18f);
                inset = new Vector4(0.03f, 0.03f, 0.03f, 0.03f);
            }

            _cloth = NewBox("Cloth", felt, new Color(0f, 0f, 0f, 0f), 0, default, 0);
            Stretch(_cloth);
            _cloth.offsetMin = new Vector2(tableWidth * inset.x, tableHeight * inset.w);
            _cloth.offsetMax = new Vector2(-tableWidth * inset.z, -tableHeight * inset.y);

            BuildCloth(_cloth);
            BuildStats(felt);
            BuildBottom(root);

            EnsureEventSystem();
            HighlightWallet();

            WarClientPlugin.Log?.LogInfo("[War] table built");
        }

        private static void BuildHeader(RectTransform parent)
        {
            var bar = NewBox("Header", parent, new Color(0f, 0f, 0f, 0f), 0, default, 0);
            SetSize(bar, 1340f, 36f);

            var title = Label(bar, "CASINO WAR", 28f, Ink, TextAlignmentOptions.Left);
            Anchor(title.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(210f, 0f), new Vector2(400f, 36f));

            _balance = Label(bar, "", 24f, Ink, TextAlignmentOptions.Right);
            Anchor(_balance.rectTransform, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-210f, 0f), new Vector2(500f, 36f));
        }

        /// <summary>
        /// The cloth, placed rather than stacked: the dealer's row at the top, the
        /// player's at the bottom, the result between them. These positions are fixed
        /// because the cards arriving must not move anything already on the table.
        ///
        /// The cloth is about 960 by 450 on the photograph. Top to bottom: label 22,
        /// cards 138, a band of 110 for the result, cards 138, label 22 -- 430.
        /// </summary>
        private static void BuildCloth(RectTransform cloth)
        {
            var dealerLabel = Label(cloth, "DEALER", 18f, Faint, TextAlignmentOptions.Center);
            Anchor(dealerLabel.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -11f), new Vector2(300f, 22f));

            _dealerRow = Holder("DealerRow", cloth, new Vector2(0.5f, 1f), new Vector2(0f, -22f - (CardView.Height / 2f)));

            var playerLabel = Label(cloth, "YOU", 18f, Faint, TextAlignmentOptions.Center);
            Anchor(playerLabel.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 11f), new Vector2(300f, 22f));

            _playerRow = Holder("PlayerRow", cloth, new Vector2(0.5f, 0f), new Vector2(0f, 22f + (CardView.Height / 2f)));

            // The burned cards, face down off to the side, where a player can count
            // three and see the dealer did not pick the war card by hand.
            _burnPile = Holder("Burned", cloth, new Vector2(0.5f, 0.5f), new Vector2(BurnX, 0f));

            _warMark = Label(cloth, "WAR", 20f, Gold, TextAlignmentOptions.Center);
            _warMark.characterSpacing = 10f;
            Anchor(_warMark.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(WarCardX, 0f), new Vector2(160f, 26f));
            _warMark.gameObject.SetActive(false);

            // The rules, printed on the felt while it is empty. Filled from the ping.
            _feltPrint = Label(cloth, "", 26f, new Color(0.85f, 0.75f, 0.45f, 0.55f), TextAlignmentOptions.Center);
            _feltPrint.characterSpacing = 4f;
            _feltPrint.enableWordWrapping = true;
            Anchor(_feltPrint.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(760f, 140f));

            // The result, to the right of the cards so it never sits on top of one. The
            // war cards reach x +118 and the cloth's edge is about +478, so this runs
            // 180 to 460. In the 140 band between the rows: headline +5..+55, the line
            // under it -70..0, which is room for three wrapped lines of the tie prompt.
            _headline = Label(cloth, "", 40f, Ink, TextAlignmentOptions.Left);
            _headline.enableAutoSizing = true;
            _headline.fontSizeMin = 24f;
            _headline.fontSizeMax = 40f;
            Anchor(_headline.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(320f, 30f), new Vector2(280f, 50f));

            _subline = Label(cloth, "", 19f, Faint, TextAlignmentOptions.TopLeft);
            _subline.enableWordWrapping = true;
            Anchor(_subline.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(320f, -35f), new Vector2(280f, 70f));

            // Where the cards come from: the dealer's right hand, top right of the cloth.
            _shoe = Holder("Shoe", cloth, new Vector2(1f, 1f), new Vector2(-60f, -60f));
        }

        /// <summary>An empty rect at a point, for cards to be placed relative to.</summary>
        private static RectTransform Holder(string name, RectTransform parent, Vector2 anchor, Vector2 position)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = rect.anchorMax = anchor;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = position;
            rect.sizeDelta = new Vector2(CardView.Width, CardView.Height);
            return rect;
        }

        /// <summary>Betting bar, message, buttons, footer -- stacked, never placed by hand.</summary>
        private static void BuildBottom(RectTransform parent)
        {
            var stack = NewBox("Bottom", parent, new Color(0f, 0f, 0f, 0f), 0, default, 0);
            SetSize(stack, 980f, 284f);

            var column = stack.gameObject.AddComponent<VerticalLayoutGroup>();
            column.childAlignment = TextAnchor.UpperCenter;
            column.spacing = 10f;
            column.childForceExpandWidth = false;
            column.childForceExpandHeight = false;
            column.childControlWidth = false;
            column.childControlHeight = false;

            BuildBetting(stack);

            _message = Label(stack, "", 20f, Faint, TextAlignmentOptions.Center);
            _message.enableWordWrapping = true;
            SetSize(_message.rectTransform, 960f, 26f);

            _actionRow = NewRow("Actions", stack, 14f);
            SetSize(_actionRow, 960f, 48f);

            var footer = NewRow("Footer", stack, 14f);
            SetSize(footer, 960f, 44f);

            _statsButton = Chip(footer, "STATS", 180f, ToggleStats);
            _leave = Chip(footer, "LEAVE TABLE", 220f, Close);
        }

        private static void BuildBetting(RectTransform parent)
        {
            var holder = NewBox("Betting", parent, new Color(0.04f, 0.05f, 0.04f, 0.74f), 12, new Color(0.31f, 0.20f, 0.11f, 0.85f), 2);
            SetSize(holder, 960f, 118f);
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
            SetSize(walletRow, 930f, 44f);

            foreach (var wallet in new[] { "Roubles", "Dollars", "Euros" })
            {
                var captured = wallet;
                Wallets.Add((wallet, Chip(walletRow, Short(wallet), 132f, () => ChooseWallet(captured))));
            }

            _held = Label(walletRow, "", 19f, Faint, TextAlignmentOptions.Left);
            SetSize(_held.rectTransform, 300f, 32f);

            var betRow = NewRow("Bet", holder, 12f);
            SetSize(betRow, 930f, 46f);

            SetSize(Label(betRow, "BET", 20f, Faint, TextAlignmentOptions.Right).rectTransform, 56f, 32f);
            _anteInput = MoneyInput(betRow, 300f, _ante, OnAnteTyped);

            SetSize(Label(betRow, "TIE BET", 20f, Faint, TextAlignmentOptions.Right).rectTransform, 110f, 32f);
            _tieInput = MoneyInput(betRow, 260f, _tie, OnTieTyped);

            SetSize(Label(betRow, "optional", 16f, Faint, TextAlignmentOptions.Left).rectTransform, 110f, 32f);
        }

        /// <summary>
        /// A typed amount, built the way Blackjack builds its wager box -- with
        /// <see cref="MoneyField"/> doing the separators and the caret.
        /// </summary>
        private static TMP_InputField MoneyInput(Transform parent, float width, long initial, UnityEngine.Events.UnityAction<string> onTyped)
        {
            var frame = NewBox("MoneyInput", parent, new Color(0.06f, 0.06f, 0.07f, 1f), 8, ChipEdge, 2);
            frame.GetComponent<Image>().sprite = Textures.ButtonFace(
                8, new Color(0.05f, 0.05f, 0.06f, 1f), new Color(0.11f, 0.11f, 0.12f, 1f), ChipEdge);
            SetSize(frame, width, 44f);

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
            input.contentType = TMP_InputField.ContentType.Standard;
            input.onValidateInput = MoneyField.DigitsOnly;
            MoneyField.MakeCaretVisible(input, Gold);
            input.characterLimit = 19;
            input.restoreOriginalTextOnEscape = true;
            input.text = initial > 0 ? MoneyField.Format(initial) : "";
            input.onValueChanged.AddListener(onTyped);

            input.transition = Selectable.Transition.SpriteSwap;
            input.targetGraphic = frame.GetComponent<Image>();
            var lit = Textures.RoundedBox(8, new Color(0.13f, 0.14f, 0.14f, 1f), Gold, 2);
            input.spriteState = new SpriteState { highlightedSprite = lit, pressedSprite = lit, selectedSprite = lit };

            return input;
        }

        // --------------------------------------------------------------------- stats

        private static void BuildStats(RectTransform felt)
        {
            var sheet = NewBox("Stats", felt, new Color(0f, 0f, 0f, 0f), 0, default, 0);
            Stretch(sheet);
            sheet.offsetMin = new Vector2(130f, 110f);
            sheet.offsetMax = new Vector2(-130f, -110f);
            _statsPanel = sheet.gameObject;

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
            SetSize(_statsTiles, 880f, 82f);

            SetSize(NewBox("Rule", card, new Color(1f, 1f, 1f, 0.10f), 0, default, 0), 820f, 2f);

            _statsRows = NewBox("Rows", card, new Color(0f, 0f, 0f, 0f), 0, default, 0);
            SetSize(_statsRows, 860f, 120f);

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

        private static void ToggleStats()
        {
            if (_busy || _statsPanel == null)
            {
                return;
            }

            var showing = !_statsPanel.activeSelf;
            _statsPanel.SetActive(showing);
            _cloth.gameObject.SetActive(!showing);

            if (showing)
            {
                Populate(WarApi.Stats());
            }
        }

        private static void HideStats()
        {
            if (_statsPanel != null && _statsPanel.activeSelf)
            {
                _statsPanel.SetActive(false);
                _cloth.gameObject.SetActive(true);
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

            Tile(rounds.ToString("N0"), "hands", Ink);
            Tile($"{Get("Wins"):N0}-{Get("Losses"):N0}", "won-lost", Ink);
            Tile(Get("Ties").ToString("N0"), "ties", Gold);
            Tile($"{Get("WarsWon"):N0}-{Get("WarsLost"):N0}", "wars won-lost", Ink);
            Tile(Get("Surrenders").ToString("N0"), "surrenders", Ink);
            Tile(Get("TieBetsWon").ToString("N0"), "tie bets won", Gold);
            Tile($"{Get("CurrentStreak"):N0} / {Get("BestStreak"):N0}", "streak / best", Ink);

            if (!(stats["ByCurrency"] is JObject byCurrency) || !byCurrency.HasValues)
            {
                return;
            }

            MoneyRow("", "staked", "returned", "net", Faint, 17f);

            foreach (var entry in byCurrency.Properties())
            {
                var staked = entry.Value["Wagered"]?.ToObject<long>() ?? 0;
                var back = entry.Value["Returned"]?.ToObject<long>() ?? 0;
                var net = back - staked;

                MoneyRow(
                    Short(entry.Name),
                    staked.ToString("N0"),
                    back.ToString("N0"),
                    (net > 0 ? "+" : "") + net.ToString("N0"),
                    net > 0 ? Good : (net < 0 ? Bad : Faint),
                    20f);
            }
        }

        private static void Tile(string value, string name, Color colour)
        {
            var tile = NewBox("Tile", _statsTiles, new Color(1f, 1f, 1f, 0.04f), 8, new Color(1f, 1f, 1f, 0.07f), 2);
            SetSize(tile, 116f, 78f);

            var inner = tile.gameObject.AddComponent<VerticalLayoutGroup>();
            inner.childAlignment = TextAnchor.MiddleCenter;
            inner.spacing = 2f;
            inner.childForceExpandWidth = false;
            inner.childForceExpandHeight = false;
            inner.childControlWidth = false;
            inner.childControlHeight = false;

            SetSize(Label(tile, value, 23f, colour, TextAlignmentOptions.Center).rectTransform, 110f, 30f);
            SetSize(Label(tile, name, 13f, Faint, TextAlignmentOptions.Center).rectTransform, 110f, 18f);
        }

        private static void MoneyRow(string wallet, string staked, string back, string net, Color netColour, float size)
        {
            var row = NewRow("Row", _statsRows, 0f);
            SetSize(row, 840f, size + 8f);

            SetSize(Label(row, wallet, size, Faint, TextAlignmentOptions.Left).rectTransform, 120f, size + 6f);
            SetSize(Label(row, staked, size, Ink, TextAlignmentOptions.Right).rectTransform, 250f, size + 6f);
            SetSize(Label(row, back, size, Ink, TextAlignmentOptions.Right).rectTransform, 250f, size + 6f);
            SetSize(Label(row, net, size, netColour, TextAlignmentOptions.Right).rectTransform, 220f, size + 6f);
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

                StyleChip(chip, wallet == _wallet);

                var label = chip.GetComponentInChildren<TextMeshProUGUI>();
                if (label != null)
                {
                    label.color = wallet == _wallet ? new Color(0.10f, 0.09f, 0.06f, 1f) : Ink;
                }
            }

            UpdateHeld();
        }

        private static string Short(string wallet) => wallet switch
        {
            "Roubles" => "RUB",
            "Dollars" => "USD",
            "Euros" => "EUR",
            _ => wallet.ToUpperInvariant(),
        };

        private static GameObject Chip(Transform parent, string text, float width, Action onClick, bool primary = false)
        {
            var rect = NewBox("Chip_" + text, parent, ChipTop, 8, ChipEdge, 2);
            SetSize(rect, width, 44f);

            var label = Label(rect, text, 19f, primary ? new Color(0.10f, 0.09f, 0.06f, 1f) : Ink, TextAlignmentOptions.Center);
            label.characterSpacing = 4f;
            Stretch(label.rectTransform);

            var button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = rect.GetComponent<Image>();
            button.onClick.AddListener(() => onClick());

            StyleChip(rect.gameObject, primary);

            return rect.gameObject;
        }

        /// <summary>Sprite swapping, not tinting -- see Blackjack's StyleChip for why.</summary>
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

        /// <summary>A size both a layout group and a bare RectTransform respect. See Blackjack.</summary>
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

        private static void StartFade(float target, bool deactivateAfter)
        {
            var host = WarClientPlugin.Instance;

            if (_fade == null || host == null)
            {
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
                host.StopCoroutine(_fading);
            }

            _fading = host.StartCoroutine(FadeTo(target, deactivateAfter));
        }

        private static IEnumerator FadeTo(float target, bool deactivateAfter)
        {
            const float duration = 0.16f;
            var from = _fade.alpha;

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

            // Leaving can itself have moved money -- a stranded stake is handed back on
            // contact -- so the game is told on the way out too.
            ProfileSync.Request("WarSync");
        }

        private static void EnsureEventSystem()
        {
            if (UnityEngine.EventSystems.EventSystem.current != null)
            {
                return;
            }

            WarClientPlugin.Log?.LogWarning("[War] no EventSystem in the scene; adding one.");

            var go = new GameObject(
                "WarEventSystem",
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
                WarClientPlugin.Log?.LogWarning("[War] could not borrow a font: " + ex.Message);
            }

            return TMP_Settings.defaultFontAsset;
        }
    }
}
