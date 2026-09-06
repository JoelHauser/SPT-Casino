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
    /// </summary>
    internal static class SlotPanel
    {
        private const string RootName = "SlotMachineCanvas";

        private static readonly Color Gold = new Color(0.85f, 0.72f, 0.38f, 1f);
        private static readonly Color Ink = new Color(0.93f, 0.91f, 0.86f, 1f);
        private static readonly Color Cabinet = new Color(0.13f, 0.13f, 0.15f, 1f);
        private static readonly Color Edge = new Color(0.42f, 0.36f, 0.22f, 1f);

        private static GameObject _root;
        private static CanvasGroup _group;
        private static TMP_FontAsset _font;
        private static Coroutine _fade;
        private static bool _closing;

        private static TextMeshProUGUI _status;
        private static TextMeshProUGUI _stakeLabel;
        private static TextMeshProUGUI _paidLabel;
        private static RectTransform _actionRow;

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

                Note(ping);

                SetStatus(ping == null
                    ? "The server is not answering. The machine will not take a pull."
                    : "Pull the handle.");

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
        /// Pulls, then spins to what came back.
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
            ReelView.Highlight(null);

            var host = SlotClientPlugin.Instance;

            if (host == null)
            {
                ReelView.Show(grid);
                Settled(paid, wins);
                return;
            }

            host.StartCoroutine(ReelView.Spin(host, grid, () => Settled(paid, wins)));
        }

        /// <summary>What happens when the reels stop.</summary>
        private static void Settled(long paid, JArray wins)
        {
            // Now, with the reels. Any earlier and the stash gives the answer away.
            Resync();

            if (paid > 0)
            {
                var best = wins?.FirstOrDefault() as JObject;
                var symbol = (string)best?["Symbol"] ?? "something";
                var reels = (int?)best?["Reels"] ?? 0;
                var ways = (int?)best?["Ways"] ?? 1;

                SetPaid(paid);
                SetStatus(
                    $"{reels} {symbol} on {ways} way{(ways == 1 ? string.Empty : "s")}."
                    + (wins is { Count: > 1 } ? $"  And {wins.Count - 1} more." : string.Empty));

                ReelView.Highlight(
                    Enumerable.Range(0, (int?)best?["Reels"] ?? 0).ToList());
            }
            else
            {
                SetPaid(0);
                SetStatus("Nothing. Pull again.");
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

        // ------------------------------------------------------------------ reading

        private static void ReadMachine(JObject ping)
        {
            Limits.Clear();
            Symbols.Clear();

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

            Pays.Clear();

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
                        ? [.. rows.Select(r => (string)r ?? "Bandage")]
                        : new List<string> { "Bandage", "Bandage", "Bandage" });
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

            var backdrop = NewBox("Backdrop", canvasObject.transform, new Color(0f, 0f, 0f, 0.93f));
            backdrop.anchorMin = Vector2.zero;
            backdrop.anchorMax = Vector2.one;
            backdrop.offsetMin = Vector2.zero;
            backdrop.offsetMax = Vector2.zero;

            var title = NewText("Title", canvasObject.transform, "SLOTS", 34f);
            title.rectTransform.anchoredPosition = new Vector2(0f, 320f);
            title.rectTransform.sizeDelta = new Vector2(700f, 44f);
            title.color = Gold;

            var ways = NewText("Ways", canvasObject.transform, "243 ways.  Wins pay from the left.", 19f);
            ways.rectTransform.anchoredPosition = new Vector2(0f, 284f);
            ways.rectTransform.sizeDelta = new Vector2(900f, 26f);
            ways.color = new Color(0.66f, 0.64f, 0.60f, 1f);

            // The cabinet, with the reels in it and the lever down the side.
            var cabinet = NewBox("Cabinet", canvasObject.transform, Color.white);
            cabinet.sizeDelta = new Vector2(ReelView.Width + 120f, ReelView.Height + 150f);
            cabinet.anchoredPosition = new Vector2(-60f, 70f);

            var cabinetImage = cabinet.GetComponent<Image>();
            cabinetImage.sprite = Textures.RoundedBox(14, Cabinet, Edge, 3);
            cabinetImage.type = Image.Type.Sliced;

            ReelView.Build(cabinet, Symbols);

            _paidLabel = NewText("Paid", cabinet, string.Empty, 30f);
            _paidLabel.rectTransform.anchoredPosition = new Vector2(0f, (ReelView.Height * 0.5f) + 46f);
            _paidLabel.rectTransform.sizeDelta = new Vector2(ReelView.Width, 38f);
            _paidLabel.color = Gold;

            var lever = LeverView.Build(
                canvasObject.transform,
                SlotClientPlugin.Instance,
                Pull,
                () => !ReelView.Spinning);

            ((RectTransform)lever.transform).anchoredPosition =
                new Vector2((ReelView.Width * 0.5f) + 60f, 70f);

            BuildPaytable(canvasObject.transform);

            _stakeLabel = NewText("Stake", canvasObject.transform, string.Empty, 22f);
            _stakeLabel.rectTransform.anchoredPosition = new Vector2(-60f, -180f);
            _stakeLabel.rectTransform.sizeDelta = new Vector2(760f, 30f);

            _status = NewText("Status", canvasObject.transform, string.Empty, 19f);
            _status.rectTransform.anchorMin = _status.rectTransform.anchorMax = new Vector2(0.5f, 0f);
            _status.rectTransform.pivot = new Vector2(0.5f, 0f);
            _status.rectTransform.anchoredPosition = new Vector2(0f, 108f);
            _status.rectTransform.sizeDelta = new Vector2(1400f, 30f);

            _actionRow = NewBox("Actions", canvasObject.transform, Color.clear);
            _actionRow.anchorMin = _actionRow.anchorMax = new Vector2(0.5f, 0f);
            _actionRow.pivot = new Vector2(0.5f, 0f);
            _actionRow.anchoredPosition = new Vector2(0f, 46f);
            _actionRow.sizeDelta = new Vector2(1500f, 52f);

            var strip = _actionRow.gameObject.AddComponent<HorizontalLayoutGroup>();
            strip.spacing = 10f;
            strip.childAlignment = TextAnchor.MiddleCenter;
            strip.childForceExpandWidth = false;
            strip.childForceExpandHeight = false;
            strip.childControlWidth = false;
            strip.childControlHeight = false;
        }

        /// <summary>
        /// What the machine pays, down the left-hand side.
        ///
        /// Nine rows, richest first, because that is the order anybody reads a paytable
        /// in. Multipliers on the stake rather than absolute amounts -- the stake is
        /// three currencies and a column of roubles would be wrong in two of them.
        /// </summary>
        private static void BuildPaytable(Transform parent)
        {
            const float RowHeight = 34f;

            var ordered = Symbols
                .OrderByDescending(s => Pays.TryGetValue(s, out var p) ? p[2] : 0)
                .ToList();

            if (ordered.Count == 0)
            {
                return;
            }

            var panel = NewBox("Paytable", parent, Color.white);
            panel.sizeDelta = new Vector2(300f, (ordered.Count * RowHeight) + 62f);
            panel.anchoredPosition = new Vector2(-620f, 70f);

            var frame = panel.GetComponent<Image>();
            frame.sprite = Textures.RoundedBox(10, new Color(0.11f, 0.11f, 0.13f, 1f), Edge, 2);
            frame.type = Image.Type.Sliced;

            var top = (panel.sizeDelta.y * 0.5f) - 24f;

            var heading = NewText("Heading", panel, "PAYS   3     4     5", 16f);
            heading.rectTransform.anchoredPosition = new Vector2(0f, top);
            heading.rectTransform.sizeDelta = new Vector2(270f, 20f);
            heading.color = new Color(0.62f, 0.60f, 0.56f, 1f);

            for (var i = 0; i < ordered.Count; i++)
            {
                var symbol = ordered[i];
                var y = top - 28f - (i * RowHeight);

                var face = NewBox("Face_" + symbol, panel, Color.white);
                face.sizeDelta = new Vector2(28f, 28f);
                face.anchoredPosition = new Vector2(-116f, y);

                var image = face.GetComponent<Image>();
                image.sprite = ReelView.Artwork(symbol);
                image.preserveAspect = true;
                image.raycastTarget = false;

                var pays = Pays.TryGetValue(symbol, out var p) ? p : [0, 0, 0];

                var row = NewText("Row_" + symbol, panel, $"{pays[0],4}  {pays[1],4}  {pays[2],5}", 17f);
                row.rectTransform.anchoredPosition = new Vector2(46f, y);
                row.rectTransform.sizeDelta = new Vector2(200f, RowHeight);
                row.alignment = TextAlignmentOptions.Right;
                row.color = i < 3 ? Gold : Ink;
            }
        }

        private static void Refresh()
        {
            SetStake();

            BuildActions(
            [
                Action("STAKE -", () => StepStake(-1)),
                Action("STAKE +", () => StepStake(1)),
                Action("CURRENCY", NextWallet),
                Action("SPIN", Pull),
                Action("CLOSE", Close),
            ]);
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

        // ------------------------------------------------------------------ pieces

        private static KeyValuePair<string, Action> Action(string label, Action action) =>
            new KeyValuePair<string, Action>(label, action);

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
                var box = NewBox("Button_" + action.Key, _actionRow, Color.white);
                box.sizeDelta = new Vector2(170f, 44f);

                var image = box.GetComponent<Image>();
                image.sprite = Textures.RoundedBox(6, new Color(0.16f, 0.16f, 0.17f, 1f), Edge, 2);
                image.type = Image.Type.Sliced;

                var text = NewText("Label", box, action.Key, 19f);
                text.rectTransform.anchorMin = Vector2.zero;
                text.rectTransform.anchorMax = Vector2.one;
                text.rectTransform.offsetMin = Vector2.zero;
                text.rectTransform.offsetMax = Vector2.zero;
                text.color = Ink;

                var chosen = action.Value;
                box.gameObject.AddComponent<Button>().onClick.AddListener(() => chosen());
            }
        }

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
