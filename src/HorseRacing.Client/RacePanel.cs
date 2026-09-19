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
    /// The betting ring: the card and its prices on the left, the slip on the right,
    /// and the track across the top.
    ///
    /// ## What this file is and is not allowed to decide
    ///
    /// Nothing. Every price shown here came from <c>/races/ping</c> and every result
    /// from <c>/races/place</c>; this file adds up what the player has clicked so it
    /// can show them a total, and that total is **not** what gets charged -- the server
    /// re-adds it from the bets themselves. If the two ever disagree the server wins,
    /// silently, and the player is charged what the server said.
    ///
    /// That is worth stating because a panel holding its own copy of the board is a
    /// panel that can advertise a price the table does not pay, and the drift would
    /// show up as an angry bug report rather than as a failing test.
    ///
    /// ## The payout waits for the horses
    ///
    /// The money has already moved by the time the reply lands -- the server settled
    /// the race before answering. But the stash on screen is not updated until the last
    /// runner is home, because a rouble counter that jumps while the field is still in
    /// the back straight tells the player the result several seconds before the race
    /// does. Roulette found this with its wheel and Slots with its reels; this is the
    /// same fix, which is why the sync is deferred to <see cref="Settled"/>.
    /// </summary>
    internal static partial class RacePanel
    {
        private const string RootName = "HorseRacingTableCanvas";

        /// <summary>
        /// Matches `HorseRacing.Server.RaceActions.Sync`. **These two strings have to
        /// agree and nothing checks that they do** -- a mismatch is a sync that is
        /// silently never answered, so the stash goes stale with no error anywhere.
        /// </summary>
        private const string SyncAction = "RacesSync";

        private static readonly Color Gold = new Color(0.72f, 0.62f, 0.34f, 1f);
        private static readonly Color Ink = new Color(0.88f, 0.86f, 0.80f, 1f);
        private static readonly Color Panel = new Color(0.07f, 0.08f, 0.07f, 0.97f);
        private static readonly Color Slate = new Color(0.13f, 0.14f, 0.13f, 1f);
        private static readonly Color Win = new Color(0.45f, 0.78f, 0.45f, 1f);
        private static readonly Color Lose = new Color(0.78f, 0.38f, 0.38f, 1f);

        private static GameObject _root;
        private static TMP_FontAsset _font;

        private static RectTransform _trackHolder;
        private static RectTransform _boardHolder;
        private static RectTransform _pairsHolder;
        private static RectTransform _tabsHolder;
        private static TextMeshProUGUI _blurb;
        private static RectTransform _slipHolder;
        private static TextMeshProUGUI _balance;
        private static TextMeshProUGUI _status;
        private static TextMeshProUGUI _result;
        private static TextMeshProUGUI _slipTotal;
        private static TMP_InputField _stakeField;
        private static TextMeshProUGUI _walletLabel;
        private static Button _runButton;

        private static Coroutine _fade;
        private static bool _closing;

        /// <summary>The last ping, which is where the board and the limits come from.</summary>
        private static JObject _card;

        /// <summary>The bets the player has built up, in the order they added them.</summary>
        private static readonly List<SlipBet> _slip = new List<SlipBet>();

        /// <summary>
        /// Which course is being looked at, as an index into the ping's Courses.
        ///
        /// The panel draws one course at a time. Switching is the only operation on
        /// this table that **empties the slip**, and it has to be: the three courses
        /// price the same bet differently -- runner 7 is 5.78 at the dash and 36.28 at
        /// the marathon -- so a bet carried across would be sitting at a price the
        /// player never agreed to. Carrying them over and silently re-pricing would be
        /// worse still.
        /// </summary>
        private static int _course;

        private static string _wallet = "Roubles";
        private static long _stake = 10_000;

        /// <summary>Set when money has moved and the game's stash has not been told.</summary>
        private static bool _syncOwed;

        internal static bool IsOpen => _root != null && _root.activeSelf && !_closing;

        internal static void Toggle()
        {
            if (IsOpen)
            {
                Close();
            }
            else
            {
                Open();
            }
        }

        internal static void Open()
        {
            try
            {
                if (_root == null)
                {
                    // The card has to be in hand before anything can be drawn: the
                    // lanes are built from the runners the server named, and the board
                    // from the prices it quoted. Neither is known until the ping lands.
                    _card = RaceApi.Ping();

                    if (_card == null)
                    {
                        RaceClientPlugin.Log.LogError(
                            "[Races] the server did not answer /races/ping -- is the server half installed?");
                        return;
                    }

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

                // Re-ping on every open: balances move while the panel is shut, and a
                // stake stranded by an interrupted race is handed back here.
                var fresh = RaceApi.Ping();

                if (fresh != null)
                {
                    _card = fresh;
                    Note(fresh);
                }

                TrackView.Reset();
                ClearSlip();
                RefreshBalance();
                SetStatus("Pick your bets, then run the race.");
                SetResult(string.Empty, Ink);
            }
            catch (Exception ex)
            {
                RaceClientPlugin.Log.LogError("[Races] could not open the table: " + ex);
            }
        }

        internal static void Close()
        {
            if (_root == null || !_root.activeSelf || _closing)
            {
                return;
            }

            // Walking out mid-race, or on the result. The animation's callback may
            // never run and the money has moved regardless, so the debt is settled on
            // the way out rather than left for a reload to discover.
            ResyncStash();

            _closing = true;

            FadeTo(0f, () =>
            {
                _root.SetActive(false);
                _closing = false;
                TrackView.Reset();
            });
        }

        internal static void OnEscape()
        {
            if (_root == null || !_root.activeSelf)
            {
                return;
            }

            // Not while they are running. The result is already settled on the server,
            // so nothing is lost by closing -- but a table that vanishes mid-race looks
            // like a crash.
            if (TrackView.IsRunning)
            {
                return;
            }

            Close();
        }

        // ------------------------------------------------------------------ betting

        /// <summary>
        /// Puts a bet on the slip, or takes it off if it is already there.
        ///
        /// Clicking a spot twice removing it is the behaviour roulette's cloth has, and
        /// the alternative -- stacking a second bet on the same spot -- makes a slip
        /// that reads as two identical lines nobody can tell apart.
        /// </summary>
        private static void Toggle(string kind, int first, int second)
        {
            if (TrackView.IsRunning)
            {
                return;
            }

            var key = kind + ":" + first + ":" + second;
            var existing = _slip.FirstOrDefault(b => b.Key == key);

            if (existing != null)
            {
                _slip.Remove(existing);
            }
            else
            {
                if (_slip.Count >= MaxBets)
                {
                    SetStatus($"A slip takes at most {MaxBets} bets.");
                    return;
                }

                _slip.Add(new SlipBet { Kind = kind, First = first, Second = second, Stake = _stake });
                SoundBoard.Play(Cue.ChipPlace);
            }

            RenderSlip();
            RenderBoard();
        }

        /// <summary>
        /// Moves to another course.
        ///
        /// Rebuilds the track -- the shape may change from a straight to an oval -- and
        /// **empties the slip**, because every price on it belonged to the old board.
        /// </summary>
        private static void SwitchCourse(int index)
        {
            if (TrackView.IsRunning || index == _course)
            {
                return;
            }

            var all = Courses;

            if (index < 0 || index >= all.Count)
            {
                return;
            }

            _course = index;

            _slip.Clear();
            BuildTrackForCourse();
            RenderCourseTabs();
            RenderBoard();
            RenderSlip();
            SetResult(string.Empty, Ink);

            var name = Course?.Value<string>("Name") ?? "this course";
            var blurb = Course?.Value<string>("Blurb") ?? string.Empty;

            SetStatus($"{name}. {blurb}");
            SetBlurb();
        }

        private static void ClearSlip()
        {
            _slip.Clear();
            RenderSlip();
            RenderBoard();
        }

        /// <summary>
        /// Sends the slip and runs the race.
        ///
        /// The reply is the whole result -- the finishing order and every settlement --
        /// and the money has already moved by the time it arrives. What happens here is
        /// only that the horses are shown doing what already happened, and the stash is
        /// left alone until they have finished doing it.
        /// </summary>
        private static void Go()
        {
            if (TrackView.IsRunning)
            {
                return;
            }

            if (_slip.Count == 0)
            {
                SetStatus("There is nothing on the slip.");
                return;
            }

            if (string.IsNullOrEmpty(CourseId))
            {
                SetStatus("No course is selected.");
                return;
            }

            var reply = RaceApi.Place(
                CourseId,
                _slip,
                _wallet,
                RaceClientPlugin.NoStakeCap != null && RaceClientPlugin.NoStakeCap.Value);

            if (reply == null)
            {
                SetStatus("The server did not answer. Nothing has been staked.");
                return;
            }

            Note(reply);

            if (reply.Value<bool?>("Ok") != true)
            {
                SetStatus(reply.Value<string>("Error") ?? "That slip was refused.");
                return;
            }

            var race = reply["Race"] as JObject;
            var order = race?["Order"] as JArray;

            if (race == null || order == null || order.Count == 0)
            {
                // The money has moved and the reply is unreadable. Say so plainly and
                // resync, rather than animating a race nobody can verify.
                SetStatus("The race ran but the result could not be read. Your stash has been resynced.");
                _syncOwed = true;
                ResyncStash();
                return;
            }

            SetInteractable(false);
            SetStatus("And they're off.");
            SetResult(string.Empty, Ink);

            // Money moved on the server the moment it answered. Whatever happens to the
            // animation from here, the stash is owed an update.
            _syncOwed = true;

            var finishing = order.Select(t => (int)t).ToList();

            TrackView.Run(finishing, () => Settled(race, finishing));
        }

        /// <summary>
        /// The last runner is home. Pay out on screen, and only now.
        /// </summary>
        private static void Settled(JObject race, IReadOnlyList<int> order)
        {
            try
            {
                var staked = race.Value<long?>("Staked") ?? 0L;
                var returned = race.Value<long?>("Returned") ?? 0L;
                var profit = returned - staked;

                var winner = order.Count > 0 ? order[0] : 0;
                var name = RunnerName(winner);

                SetStatus($"{winner} {name} wins.");

                if (returned > 0)
                {
                    SoundBoard.Play(Cue.RaceWin);

                    SetResult(
                        profit > 0
                            ? $"PAID {Money(returned)}   ({(profit > 0 ? "+" : string.Empty)}{Money(profit)})"
                            : $"PAID {Money(returned)}",
                        Win);
                }
                else
                {
                    SetResult($"NOTHING   (-{Money(staked)})", Lose);
                }

                MarkSettlements(race);
            }
            catch (Exception ex)
            {
                // Presentation must never eat the resync below it. Slots shipped 1.2.6
                // with a throw in exactly this position: every table paid out correctly
                // and then drew nothing, because the line that threw ran ahead of the
                // try that guarded the drawing.
                RaceClientPlugin.Log.LogError("[Races] could not draw the result: " + ex);
            }

            // Last, and outside anything that can throw above it.
            ResyncStash();
            RefreshBalance();
            SetInteractable(true);
        }

        /// <summary>Colours each line of the slip by whether it came in.</summary>
        private static void MarkSettlements(JObject race)
        {
            if (race["Settlements"] is not JArray settled || _slipHolder == null)
            {
                return;
            }

            for (var i = 0; i < settled.Count && i < _slipHolder.childCount; i++)
            {
                var line = _slipHolder.GetChild(i);
                var label = line.GetComponentInChildren<TextMeshProUGUI>();

                if (label == null)
                {
                    continue;
                }

                var won = settled[i].Value<bool?>("Won") == true;
                var paid = settled[i].Value<long?>("Returned") ?? 0L;

                label.color = won ? Win : new Color(Ink.r, Ink.g, Ink.b, 0.45f);

                if (won)
                {
                    label.text += $"   +{Money(paid)}";
                }
            }
        }

        // ------------------------------------------------------------------- stash

        /// <summary>
        /// Tells the running game its stash changed.
        ///
        /// Static routes cannot update the game's inventory, so currency moved through
        /// one leaves the counter on screen stale until a reload -- which reads to a
        /// player as the mod eating their money. An item event carries the profile
        /// changes SPT has been holding; this asks for them.
        /// </summary>
        private static void ResyncStash()
        {
            if (!_syncOwed)
            {
                return;
            }

            _syncOwed = false;

            try
            {
                ProfileSync.Request(SyncAction);
            }
            catch (Exception ex)
            {
                RaceClientPlugin.Log.LogError("[Races] could not resync the stash: " + ex);
            }
        }

        private static void RefreshBalance()
        {
            if (_balance == null)
            {
                return;
            }

            // Read off the ping rather than worked out as stake-minus-paid: a win big
            // enough to overflow the stash posts the rest as mail, and only the server
            // knows what actually landed. Slots learned this one the same way.
            var fresh = RaceApi.Ping();

            if (fresh != null)
            {
                _card = fresh;
            }

            var balances = _card?["Balances"] as JObject;
            var held = balances?.Value<long?>(_wallet) ?? 0L;

            _balance.text = $"{Sign()}{Money(held)}";
        }

        private static void Note(JObject reply)
        {
            var note = reply?.Value<string>("Note");

            if (string.IsNullOrEmpty(note))
            {
                return;
            }

            SetStatus(note);

            // A note means money moved that the player did not ask to move -- a stake
            // handed back from a race that never finished. The stash has to be told.
            _syncOwed = true;
            ResyncStash();
        }

        // ------------------------------------------------------------------ helpers

        private static int MaxBets => _card?.Value<int?>("MaxBets") ?? 108;

        /// <summary>Every course the server offers, or an empty array before the first ping.</summary>
        private static JArray Courses => _card?["Courses"] as JArray ?? [];

        /// <summary>The course being looked at, or null if the ping has not landed.</summary>
        private static JObject Course
        {
            get
            {
                var all = Courses;

                if (all.Count == 0)
                {
                    return null;
                }

                // Clamped rather than trusted. The index survives a re-ping, and a
                // server that came back offering fewer courses would otherwise read
                // off the end.
                var index = Mathf.Clamp(_course, 0, all.Count - 1);

                return all[index] as JObject;
            }
        }

        private static string CourseId => Course?.Value<string>("Id") ?? string.Empty;

        /// <summary>
        /// The most this slip may cost: the smaller of the currency's cap and the
        /// course's own.
        ///
        /// The course cap is arithmetic -- the dash has the longest price on any board,
        /// so it has the lowest ceiling. The server applies the same min() and is the
        /// one that counts; this is so the panel can say the number before the player
        /// finds out by being refused.
        /// </summary>
        private static long SlipCap()
        {
            var limits = _card?["Limits"] as JObject;
            var wallet = (limits?[_wallet] as JObject)?.Value<long?>("Max") ?? 2_000_000L;
            var course = Course?.Value<long?>("MaxSlip") ?? 2_000_000L;

            return Math.Min(wallet, course);
        }

        private static string Sign()
        {
            var limits = _card?["Limits"] as JObject;
            return (limits?[_wallet] as JObject)?.Value<string>("Sign") ?? string.Empty;
        }

        private static long MinStake()
        {
            var limits = _card?["Limits"] as JObject;
            return (limits?[_wallet] as JObject)?.Value<long?>("Min") ?? 10_000L;
        }

        private static long StepStake()
        {
            var limits = _card?["Limits"] as JObject;
            return (limits?[_wallet] as JObject)?.Value<long?>("Step") ?? 5_000L;
        }

        private static string RunnerName(int number)
        {
            if (Course?["Runners"] is not JArray runners)
            {
                return string.Empty;
            }

            foreach (var runner in runners)
            {
                if (runner.Value<int?>("Number") == number)
                {
                    return runner.Value<string>("Name") ?? string.Empty;
                }
            }

            return string.Empty;
        }

        /// <summary>The board price for a spot, or zero if the server did not quote it.</summary>
        private static double PriceOf(string kind, int first, int second)
        {
            if (Course?["Board"] is not JArray board)
            {
                return 0d;
            }

            foreach (var spot in board)
            {
                if (spot.Value<string>("Kind") == kind
                    && spot.Value<int?>("First") == first
                    && spot.Value<int?>("Second") == second)
                {
                    return spot.Value<double?>("Price") ?? 0d;
                }
            }

            return 0d;
        }

        private static string Money(long value) => value.ToString("N0", CultureInfo.InvariantCulture);

        private static string Odds(double price) =>
            price <= 0d ? "--" : price.ToString("0.00", CultureInfo.InvariantCulture);

        private static void SetStatus(string text)
        {
            if (_status != null)
            {
                _status.text = text;
            }
        }

        private static void SetResult(string text, Color colour)
        {
            if (_result != null)
            {
                _result.text = text;
                _result.color = colour;
            }
        }

        private static void SetInteractable(bool on)
        {
            if (_runButton != null)
            {
                _runButton.interactable = on;
            }
        }
    }
}
