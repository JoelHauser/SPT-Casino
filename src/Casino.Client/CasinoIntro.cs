using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SPT.Common.Http;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Textures = Roulette.Client.Textures;

namespace Casino.Client
{
    /// <summary>
    /// The card that greets a player the first time they walk in.
    ///
    /// Modelled on the note the hideout shows a new account: read it once, press
    /// continue, never see it again. It is drawn here rather than borrowed from the
    /// game -- EFT has `MessageWindow` and `InfoWindow` and both are obfuscated types
    /// that move between patches, and 4.1.5 landed the morning this was written. The
    /// tables draw everything themselves for the same reason.
    ///
    /// ## Once per account, not once per install
    ///
    /// Which is what "a new account" means. The flag is keyed on the session id, which
    /// SPT's own <see cref="RequestHandler"/> knows, and kept in a file beside the
    /// plugin. A second profile on the same install gets its own welcome; a player who
    /// reinstalls the mod does not get it again unless they clear the file.
    ///
    /// It is deliberately not a BepInEx config entry. Those are global, so one profile
    /// reading this would silently mark it read for every other one.
    /// </summary>
    internal static class CasinoIntro
    {
        private const string RootName = "CasinoIntroCanvas";
        private const string FileName = "seen.txt";

        private static readonly Color Gold = new Color(0.85f, 0.72f, 0.38f, 1f);
        private static readonly Color Panel = new Color(0.09f, 0.10f, 0.11f, 0.98f);
        private static readonly Color Edge = new Color(0.45f, 0.38f, 0.22f, 1f);

        private static readonly string[] Lines =
        {
            "You have found the back room.",
            string.Empty,
            "Three tables run here, and they take the same roubles you would spend on "
            + "ammunition. Blackjack against the dealer, no-limit hold'em against the "
            + "regulars, and a single-zero roulette wheel.",
            string.Empty,
            "The money is real. A stake leaves your stash when you commit it and the "
            + "winnings are paid straight back into it -- there is no separate balance "
            + "and nothing to cash out. If your stash is too full to take a payout, it "
            + "arrives in the post instead.",
            string.Empty,
            "The house edge is real too, and it does not get tired. Roulette keeps 2.70% "
            + "of everything staked on it, forever, and the other two are not charity "
            + "either. Play with what you can afford to lose in a raid.",
            string.Empty,
            "Escape leaves a table and brings you back here. Good luck.",
        };

        private static GameObject _root;

        internal static bool IsOpen => _root != null && _root.activeSelf;

        /// <summary>Whether this profile has walked in before.</summary>
        internal static bool ShouldShow()
        {
            try
            {
                var session = Session();

                return !string.IsNullOrEmpty(session) && !Seen().Contains(session);
            }
            catch (Exception ex)
            {
                // Never a reason to block the door. A player who cannot be identified
                // gets the lobby, not an error.
                CasinoPlugin.Log.LogWarning("[Casino] could not read the welcome flag: " + ex.Message);
                return false;
            }
        }

        internal static void Open(Action onContinue)
        {
            try
            {
                Build(onContinue);
            }
            catch (Exception ex)
            {
                // If the welcome will not draw, go straight through to the lobby rather
                // than leaving the player looking at a tab that does nothing.
                CasinoPlugin.Log.LogError("[Casino] could not show the welcome: " + ex);
                onContinue?.Invoke();
            }
        }

        internal static void Close()
        {
            if (_root != null)
            {
                UnityEngine.Object.Destroy(_root);
                _root = null;
            }
        }

        // ------------------------------------------------------------------ the flag

        private static string Session()
        {
            try
            {
                return RequestHandler.SessionId;
            }
            catch
            {
                return null;
            }
        }

        private static string Path()
        {
            var beside = System.IO.Path.GetDirectoryName(
                CasinoPlugin.Instance?.Info?.Location ?? ".") ?? ".";

            return System.IO.Path.Combine(beside, FileName);
        }

        private static HashSet<string> Seen()
        {
            var path = Path();

            return File.Exists(path)
                ? new HashSet<string>(File.ReadAllLines(path).Where(l => !string.IsNullOrWhiteSpace(l)))
                : new HashSet<string>();
        }

        /// <summary>
        /// Remembers that this profile has read it.
        ///
        /// Written when Continue is pressed rather than when the card is drawn, so a
        /// player who alt-F4s over the top of it is shown it again. It is the only
        /// thing in the mod that explains what the money does.
        /// </summary>
        private static void Remember()
        {
            try
            {
                var session = Session();

                if (string.IsNullOrEmpty(session))
                {
                    return;
                }

                var seen = Seen();

                if (seen.Add(session))
                {
                    File.WriteAllLines(Path(), seen.ToArray());
                }
            }
            catch (Exception ex)
            {
                // Worst case the player is welcomed twice, which is a great deal better
                // than a mod that will not open because it could not write a file.
                CasinoPlugin.Log.LogWarning("[Casino] could not record the welcome: " + ex.Message);
            }
        }

        // ------------------------------------------------------------------ drawing

        private static void Build(Action onContinue)
        {
            Close();

            var canvasObject = new GameObject(
                RootName, typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));

            UnityEngine.Object.DontDestroyOnLoad(canvasObject);
            _root = canvasObject;

            var canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            // Over the lobby, which may already be behind it.
            canvas.sortingOrder = 2950;

            var scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 1f;

            var backdrop = CasinoLobby.NewBox("Backdrop", canvasObject.transform, new Color(0f, 0f, 0f, 0.94f));
            backdrop.anchorMin = Vector2.zero;
            backdrop.anchorMax = Vector2.one;
            backdrop.offsetMin = Vector2.zero;
            backdrop.offsetMax = Vector2.zero;

            var card = CasinoLobby.NewBox("Card", canvasObject.transform, Color.white);
            card.sizeDelta = new Vector2(920f, 560f);

            var face = card.GetComponent<Image>();
            face.sprite = Textures.RoundedBox(12, Panel, Edge, 2);
            face.type = Image.Type.Sliced;

            var pip = CasinoLobby.NewBox("Pip", card, Color.white);
            pip.sizeDelta = new Vector2(64f, 64f);
            pip.anchoredPosition = new Vector2(0f, 208f);
            pip.GetComponent<Image>().sprite = Textures.Suit('S', Gold);
            pip.GetComponent<Image>().raycastTarget = false;

            var title = CasinoLobby.NewText("Title", card, "THE CASINO", 34f);
            title.rectTransform.sizeDelta = new Vector2(820f, 44f);
            title.rectTransform.anchoredPosition = new Vector2(0f, 148f);
            title.color = Gold;

            var body = CasinoLobby.NewText("Body", card, string.Join("\n", Lines), 20f);
            body.rectTransform.sizeDelta = new Vector2(800f, 300f);
            body.rectTransform.anchoredPosition = new Vector2(0f, -20f);
            body.alignment = TextAlignmentOptions.TopLeft;
            body.enableWordWrapping = true;
            body.lineSpacing = 6f;

            BuildContinue(card, onContinue);
        }

        private static void BuildContinue(Transform parent, Action onContinue)
        {
            var box = CasinoLobby.NewBox("Continue", parent, Color.white);
            box.sizeDelta = new Vector2(220f, 50f);
            box.anchoredPosition = new Vector2(0f, -228f);

            var image = box.GetComponent<Image>();
            image.sprite = Textures.RoundedBox(6, new Color(0.18f, 0.16f, 0.10f, 1f), Gold, 2);
            image.type = Image.Type.Sliced;

            var text = CasinoLobby.NewText("Label", box, "CONTINUE", 22f);
            text.rectTransform.anchorMin = Vector2.zero;
            text.rectTransform.anchorMax = Vector2.one;
            text.rectTransform.offsetMin = Vector2.zero;
            text.rectTransform.offsetMax = Vector2.zero;
            text.color = Gold;

            box.gameObject.AddComponent<Button>().onClick.AddListener(() =>
            {
                Remember();
                Close();
                onContinue?.Invoke();
            });
        }
    }
}
