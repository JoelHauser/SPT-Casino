using System;
using System.Collections;
using System.Globalization;
using Newtonsoft.Json.Linq;
using SPT.Common.Http;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using ProfileSync = Casino.Shared.ProfileSync;
using Textures = Casino.Shared.Textures;

namespace Casino.Client
{
    /// <summary>
    /// The apology that ships with 1.2.6, and the million roubles that come with it.
    ///
    /// 1.2.0 and 1.2.1 went out with a pinned metadata token in them, drew no item
    /// icons on any install but the one it was read off, and the mod was pulled while
    /// that was fixed -- see "When the obfuscator empties a name" in CLAUDE.md. This
    /// says so once, to each profile, and pays for the inconvenience.
    ///
    /// ## The server owns the money, this owns the card
    ///
    /// Nothing here decides whether anyone is paid. <see cref="CasinoIntro"/> keeps its
    /// own flag in a file beside the plugin, which is fine for a card that only costs a
    /// second reading if it is lost; the same arrangement for a million roubles would
    /// be a file players could delete for another million. So this asks the server what
    /// is owed and the server records what it paid. See `Casino.Server.GiftStore`.
    ///
    /// **The money moves as the card opens, not when Continue is pressed.** A player
    /// who alt-F4s over the top of it, or presses escape to shut the casino, has still
    /// been paid -- and the roubles are in the stash by the time they have read the
    /// sentence that says so. Continue is only an acknowledgement.
    /// </summary>
    internal static class CasinoGift
    {
        private const string RootName = "CasinoGiftCanvas";

        private static readonly Color Gold = new Color(0.85f, 0.72f, 0.38f, 1f);
        private static readonly Color Panel = new Color(0.09f, 0.10f, 0.11f, 0.98f);
        private static readonly Color Edge = new Color(0.45f, 0.38f, 0.22f, 1f);

        /// <summary>
        /// What the card says, with the amount dropped into it.
        ///
        /// **No double hyphens.** TMP renders them literally, so an em dash written the
        /// way it is written everywhere else in this repo comes out as two hyphens
        /// sitting in the middle of a sentence. The welcome card learned that first and
        /// its note is worth reading before editing this.
        ///
        /// Measured against the box below, like the welcome card, which shipped once
        /// with a third of its copy cut off the bottom.
        /// </summary>
        private static string[] Lines(string amount) => new[]
        {
            "The casino was taken down for a day, and you may have wondered "
            + "where it went.",
            string.Empty,
            "An icon fix that worked perfectly on the machine it was written on turned "
            + "out to work on almost nobody else's, and a couple of things came up "
            + "behind it. They are all fixed.",
            string.Empty,
            "Sorry for the wait. Have " + amount + " roubles on the house.",
        };

        /// <summary>Where the money went, appended to the copy above.</summary>
        private const string InStash = "It is in your stash already. Spend it at whichever table you like.";

        private const string InMail =
            "Your stash was too full to take all of it, so the rest is waiting in your messages.";

        private static GameObject _root;
        private static CanvasGroup _group;

        /// <summary>
        /// What the server last said about the gift, remembered for the rest of the
        /// session.
        ///
        /// Null until asked. The check costs a round trip and the tab is opened often,
        /// so it is asked at most once per game launch, and never again in that session
        /// once it has been answered or acted on. For every profile after its first
        /// visit the answer is no, so this is one request in the life of an install.
        /// </summary>
        private static bool? _pending;

        internal static bool IsOpen => _root != null && _root.activeSelf;

        /// <summary>Whether this profile still has the gift waiting.</summary>
        internal static bool ShouldShow()
        {
            if (_pending.HasValue)
            {
                return _pending.Value;
            }

            var reply = Post("/casino/gift/status", "{}");

            // A server that did not answer is not a server that said yes. Remembering
            // the failure as "no" also stops a broken route being asked on every single
            // open of the tab for the rest of the session.
            _pending = reply != null && reply.Value<bool?>("Pending") == true;

            return _pending.Value;
        }

        /// <summary>
        /// Claims the gift and shows what happened to it.
        ///
        /// Never throws its way out: a card that will not draw must not leave the
        /// player looking at a tab that does nothing, so every failure ends up at
        /// <paramref name="onContinue"/> the same way Continue does.
        /// </summary>
        internal static void Open(Action onContinue)
        {
            // Asked once and then not again for the rest of the session, whatever
            // happens below.
            //
            // **Never reset, not even on a failure.** The tempting version puts it back
            // so a failed claim can be retried on the next open, and that turns a
            // half-finished upgrade -- new plugin, old server assemblies, so the route
            // is not there -- into a failed request and a log line every single time
            // the tab is pressed. Nothing is lost by waiting: the server records the
            // payment, so a gift that did not get paid is still on offer next launch.
            _pending = false;

            try
            {
                var reply = Post("/casino/gift/claim", "{}");

                if (reply == null)
                {
                    // The claim never reached the server, so nothing was paid and
                    // nothing was recorded. Say nothing and let them in.
                    onContinue?.Invoke();
                    return;
                }

                var granted = reply.Value<bool?>("Granted") == true;
                var error = reply.Value<string>("Error");

                if (!granted)
                {
                    if (!string.IsNullOrEmpty(error))
                    {
                        CasinoPlugin.Log.LogWarning("[Casino] the gift was not paid: " + error);
                    }

                    // Nothing was paid, so there is nothing to congratulate anybody
                    // about. Straight through to the lobby.
                    onContinue?.Invoke();
                    return;
                }

                // The money is in the profile but the running game has not been told,
                // so the rouble count behind the menu is stale until it asks. Same
                // fix, and the same reason, as every table's settlement.
                ProfileSync.Request("CasinoSync");

                Build(reply.Value<int?>("Amount") ?? 1000000, reply.Value<bool?>("Posted") == true, onContinue);
            }
            catch (Exception ex)
            {
                CasinoPlugin.Log.LogError("[Casino] could not show the gift: " + ex);
                onContinue?.Invoke();
            }
        }

        internal static void Close()
        {
            if (_root != null)
            {
                UnityEngine.Object.Destroy(_root);
                _root = null;
                _group = null;
            }
        }

        // ------------------------------------------------------------------ the wire

        private static JObject Post(string route, string json)
        {
            try
            {
                var body = RequestHandler.PostJson(route, json);

                return string.IsNullOrEmpty(body) ? null : JObject.Parse(body);
            }
            catch (Exception ex)
            {
                // A failed request must not take the menu down with it, and must not
                // stop the casino opening. The caller treats null as "no gift".
                CasinoPlugin.Log.LogError("[Casino] " + route + " failed: " + ex.Message);
                return null;
            }
        }

        // ------------------------------------------------------------------ drawing

        /// <summary>
        /// Continue: put the lobby up underneath, then fade this away.
        ///
        /// **The order is the fix**, and it is the welcome card's. Destroying the card
        /// first and asking the lobby to fade in from nothing leaves several frames
        /// where the only thing drawn is the menu, so the casino appears to blink out
        /// and come back.
        /// </summary>
        private static void Dismiss(Action onContinue)
        {
            // Underneath, solid, before anything is taken away.
            onContinue?.Invoke();

            var host = CasinoPlugin.Instance;

            if (host == null || _group == null)
            {
                Close();
                return;
            }

            host.StartCoroutine(FadeOut());
        }

        private static IEnumerator FadeOut()
        {
            const float seconds = 0.18f;
            var group = _group;
            var root = _root;

            for (var t = 0f; t < seconds; t += Time.unscaledDeltaTime)
            {
                if (group == null)
                {
                    yield break;
                }

                group.alpha = Mathf.Lerp(1f, 0f, t / seconds);
                yield return null;
            }

            // Only tears down what this coroutine was fading. Reopening while it is on
            // its way out would otherwise destroy the new one.
            if (_root == root)
            {
                Close();
            }
            else if (root != null)
            {
                UnityEngine.Object.Destroy(root);
            }
        }

        private static void Build(int amount, bool posted, Action onContinue)
        {
            Close();

            var canvasObject = new GameObject(
                RootName, typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));

            UnityEngine.Object.DontDestroyOnLoad(canvasObject);
            _root = canvasObject;

            var canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            // Above the welcome card as well as the lobby. A brand new profile on 1.2.6
            // gets both, and this one is drawn while the welcome is still fading out.
            canvas.sortingOrder = 2960;

            var scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 1f;

            _group = canvasObject.AddComponent<CanvasGroup>();
            _group.alpha = 1f;

            var backdrop = CasinoLobby.NewBox("Backdrop", canvasObject.transform, new Color(0f, 0f, 0f, 0.94f));
            backdrop.anchorMin = Vector2.zero;
            backdrop.anchorMax = Vector2.one;
            backdrop.offsetMin = Vector2.zero;
            backdrop.offsetMax = Vector2.zero;

            // The welcome card's geometry, because this is the same card in the same
            // room and two boxes of different sizes doing the same job would read as an
            // accident. Its body holds eleven lines at 20pt; this copy runs to nine.
            var card = CasinoLobby.NewBox("Card", canvasObject.transform, Color.white);
            card.sizeDelta = new Vector2(880f, 620f);

            var face = card.GetComponent<Image>();
            face.sprite = Textures.RoundedBox(12, Panel, Edge, 2);
            face.type = Image.Type.Sliced;

            var pip = CasinoLobby.NewBox("Pip", card, Color.white);
            pip.sizeDelta = new Vector2(64f, 64f);
            pip.anchoredPosition = new Vector2(0f, 232f);
            pip.GetComponent<Image>().sprite = Textures.Suit('D', Gold);
            pip.GetComponent<Image>().raycastTarget = false;

            var title = CasinoLobby.NewText("Title", card, "ON THE HOUSE", 34f);
            title.rectTransform.sizeDelta = new Vector2(780f, 44f);
            title.rectTransform.anchoredPosition = new Vector2(0f, 168f);
            title.color = Gold;

            var copy = string.Join("\n", Lines(Amount(amount)))
                + "\n\n" + (posted ? InMail : InStash);

            var body = CasinoLobby.NewText("Body", card, copy, 20f);
            body.rectTransform.sizeDelta = new Vector2(740f, 330f);
            body.rectTransform.anchoredPosition = new Vector2(0f, -34f);
            body.alignment = TextAlignmentOptions.TopLeft;
            body.enableWordWrapping = true;
            body.lineSpacing = 8f;

            // Shrinks rather than clips if a future edit runs long, or a player's font
            // is wider than the one this was measured against.
            body.enableAutoSizing = true;
            body.fontSizeMax = 20f;
            body.fontSizeMin = 15f;

            BuildContinue(card, onContinue);
        }

        /// <summary>
        /// Grouped with spaces rather than by the machine's culture. A player on a
        /// German locale would otherwise read "1.000.000 roubles" in the middle of an
        /// English sentence.
        /// </summary>
        private static string Amount(int value) =>
            value.ToString("#,0", CultureInfo.InvariantCulture).Replace(",", " ");

        private static void BuildContinue(Transform parent, Action onContinue)
        {
            var box = CasinoLobby.NewBox("Continue", parent, Color.white);
            box.sizeDelta = new Vector2(220f, 50f);
            box.anchoredPosition = new Vector2(0f, -252f);

            var image = box.GetComponent<Image>();
            image.sprite = Textures.RoundedBox(6, new Color(0.18f, 0.16f, 0.10f, 1f), Gold, 2);
            image.type = Image.Type.Sliced;

            var text = CasinoLobby.NewText("Label", box, "THANKS", 22f);
            text.rectTransform.anchorMin = Vector2.zero;
            text.rectTransform.anchorMax = Vector2.one;
            text.rectTransform.offsetMin = Vector2.zero;
            text.rectTransform.offsetMax = Vector2.zero;
            text.color = Gold;

            box.gameObject.AddComponent<Button>().onClick.AddListener(() => Dismiss(onContinue));
        }
    }
}
