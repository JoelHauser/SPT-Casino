using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using EFT.UI;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace Blackjack.Client
{
    /// <summary>
    /// Puts a BLACKJACK button on the main menu, beside the ones already there.
    ///
    /// The menu rather than the hideout, deliberately. The Rest Space was the obvious
    /// home, and EFT even has a game-disc system sitting in it, but the part that can
    /// play a game needs Rest Space 2, a generator and burning fuel, which locks a new
    /// profile out of the mod entirely. A menu button works on a profile five minutes
    /// old.
    ///
    /// The button is a clone of one of the menu's own, and that is what makes it fit
    /// alongside other menu mods rather than in spite of them. See <see cref="Install"/>.
    /// </summary>
    [HarmonyPatch]
    internal static class MenuButtonPatch
    {
        private const string ButtonName = "BlackjackButton";

        /// <summary>
        /// Both of the moments the menu is built, looked up by name at load rather than
        /// named in an attribute.
        ///
        /// <c>nameof(MenuScreen.Awake)</c> does not compile against every EFT build: on
        /// 0.16.9.5 Awake is private and Show's controller argument is an obfuscated
        /// nested type with no name to write down. Neither is a problem for Harmony,
        /// which is happy with a MethodBase -- it was only ever a problem for the
        /// compiler. Asking at runtime also means a build that renames one of them
        /// costs the button rather than the whole plugin.
        /// </summary>
        private static IEnumerable<MethodBase> TargetMethods()
        {
            var awake = AccessTools.Method(typeof(MenuScreen), "Awake");
            if (awake != null)
            {
                yield return awake;
            }

            // Every overload: which one the game calls varies by build, and patching a
            // Show that is never called costs nothing.
            foreach (var show in AccessTools.GetDeclaredMethods(typeof(MenuScreen))
                         .Where(m => m.Name == "Show"))
            {
                yield return show;
            }
        }

        [HarmonyPostfix]
        // ReSharper disable once InconsistentNaming
        private static void AfterScreenBuilt(MenuScreen __instance) => Schedule(__instance);

        /// <summary>
        /// Rebuilds at the end of the frame rather than immediately.
        ///
        /// This is the whole integration story with menu mods. MoxoPixel's Menu
        /// Overhaul restyles the main menu from a hardcoded list of five buttons --
        /// PlayButton, CharacterButton, TradeButton, HideoutButton, ExitButtonGroup --
        /// hiding each one's background, activating its icon and nudging it sideways by
        /// a per-button offset from its own config. A sixth button cannot be in that
        /// list, and asking to be added to it is not something this mod can do.
        ///
        /// It does not have to be. Waiting until every other Awake and Show handler has
        /// run means the button we copy has already been restyled, so the copy inherits
        /// the styling exactly -- background hidden, icon state, label size, whatever
        /// the other mod decided. Cloning early got a vanilla-looking button sitting
        /// next to five restyled ones, which is what looked wrong.
        ///
        /// It also means this needs no knowledge of that mod at all: anything that
        /// restyles the hideout button, now or later, is inherited for free.
        /// </summary>
        private static void Schedule(MenuScreen screen)
        {
            if (screen == null || BlackjackClientPlugin.Instance == null)
            {
                return;
            }

            BlackjackClientPlugin.Instance.StartCoroutine(InstallAtEndOfFrame(screen));
        }

        private static IEnumerator InstallAtEndOfFrame(MenuScreen screen)
        {
            yield return new WaitForEndOfFrame();

            try
            {
                Install(screen);
            }
            catch (Exception ex)
            {
                // A missing button is a disappointment. A menu that fails to build is a
                // game that does not start, so this never rethrows.
                BlackjackClientPlugin.Log.LogError("[Blackjack] could not add the menu button: " + ex);
            }
        }

        private static void Install(MenuScreen screen)
        {
            if (screen == null)
            {
                return;
            }

            var template = FindTemplate(screen);
            if (template == null)
            {
                BlackjackClientPlugin.Log.LogWarning(
                    "[Blackjack] no menu button to clone from; the menu's layout has changed.");
                return;
            }

            // Thrown away and cloned again rather than adjusted in place. Whatever
            // another mod did to the template between then and now is inherited by
            // copying it afresh, and there is no state of ours to get out of step.
            var existing = FindOurs(screen);
            if (existing != null)
            {
                UnityEngine.Object.DestroyImmediate(existing.gameObject);
            }

            var clone = UnityEngine.Object.Instantiate(template.gameObject, template.transform.parent, false);
            clone.name = ButtonName;
            clone.transform.SetSiblingIndex(template.transform.GetSiblingIndex() + 1);

            var button = clone.GetComponent<DefaultUIButton>();
            if (button == null)
            {
                BlackjackClientPlugin.Log.LogWarning("[Blackjack] the clone has no DefaultUIButton.");
                UnityEngine.Object.Destroy(clone);
                return;
            }

            Relabel(button, "BLACKJACK");
            MenuIcon.Diamond(button);
            button.Interactable = true;
            Wire(button);
            Follow(button, template);

            BlackjackClientPlugin.Log.LogInfo($"[Blackjack] menu button added, cloned from '{template.name}'");
        }

        /// <summary>
        /// Renames the button without undoing anyone's styling.
        ///
        /// SetHeaderText re-applies the button's own font size, which throws away a
        /// size another mod set on the label. Putting it back afterwards keeps our
        /// button the same size as its neighbours.
        /// </summary>
        private static void Relabel(DefaultUIButton button, string text)
        {
            var label = button.GetComponentInChildren<TextMeshProUGUI>(true);
            var size = label != null ? label.fontSize : 0f;

            button.SetHeaderText(text);

            if (label != null && size > 0f)
            {
                label.fontSize = size;
            }
        }

        /// <summary>
        /// Attaches the click handler.
        ///
        /// Not a UnityEngine.UI.Button: EFT's DefaultUIButton descends from
        /// ButtonFeedback, which implements IPointerClickHandler itself and exposes a
        /// plain UnityEvent field called OnClick. Looking for a Button component finds
        /// nothing, which is exactly what the first attempt did -- the button appeared,
        /// looked right, and did nothing at all when clicked.
        ///
        /// Clearing first matters as much as adding: a clone carries the original's
        /// listeners, so without this BLACKJACK would open the hideout.
        /// </summary>
        private static void Wire(DefaultUIButton button)
        {
            var field = AccessTools.Field(typeof(DefaultUIButton), "OnClick");
            if (field?.GetValue(button) is not UnityEvent onClick)
            {
                BlackjackClientPlugin.Log.LogWarning(
                    "[Blackjack] DefaultUIButton has no OnClick event; the button will do nothing.");
                return;
            }

            onClick.RemoveAllListeners();
            onClick.AddListener(OnClicked);
        }

        /// <summary>
        /// Sits our button one row under the template, matching whatever position and
        /// size it currently has -- including any sideways offset a menu mod applied,
        /// since that is baked into the template by the time this runs.
        /// </summary>
        private static void Follow(DefaultUIButton ours, DefaultUIButton template)
        {
            var mine = ours.GetComponent<RectTransform>();
            var theirs = template.GetComponent<RectTransform>();
            if (mine == null || theirs == null)
            {
                return;
            }

            // If the menu arranges its buttons with a layout group, it will place this
            // one too, and anything set here would be overwritten on the next rebuild
            // anyway. Sibling order is the only thing worth saying in that case.
            if (ours.transform.parent != null &&
                ours.transform.parent.GetComponent<LayoutGroup>() != null)
            {
                ours.transform.SetSiblingIndex(template.transform.GetSiblingIndex() + 1);
                return;
            }

            mine.anchorMin = theirs.anchorMin;
            mine.anchorMax = theirs.anchorMax;
            mine.pivot = theirs.pivot;
            mine.sizeDelta = theirs.sizeDelta;
            mine.localScale = theirs.localScale;

            // One row below the lowest button, measured in world space.
            //
            // Local positions cannot be compared here. The exit entry is a group with
            // the button nested inside it, so its anchoredPosition is relative to that
            // group rather than to the menu, and reading them as if they shared a
            // parent put BLACKJACK straight on top of EXIT. World positions are the
            // only ones that mean the same thing for every button.
            var rows = WorldRows(template, ours);
            if (rows.Count == 0)
            {
                mine.anchoredPosition = theirs.anchoredPosition;
                return;
            }

            var gap = MedianGap(rows, theirs);
            var lowest = rows[rows.Count - 1];

            mine.position = new Vector3(theirs.position.x, lowest - gap, theirs.position.z);
            ours.transform.SetAsLastSibling();
        }

        /// <summary>
        /// The world Y of every visible menu button, ours excluded, highest first.
        ///
        /// Rows closer together than a few pixels are treated as one, since a button
        /// and a label of its own can sit at almost the same height without being two
        /// entries.
        /// </summary>
        private static List<float> WorldRows(DefaultUIButton template, DefaultUIButton ours)
        {
            var parent = template.transform.parent;
            if (parent == null)
            {
                return new List<float>();
            }

            var ys = parent.GetComponentsInChildren<DefaultUIButton>(true)
                .Where(b => b != null && b.name != ButtonName && b != ours && b.gameObject.activeInHierarchy)
                .Select(b => b.GetComponent<RectTransform>())
                .Where(r => r != null)
                .Select(r => r.position.y)
                .OrderByDescending(y => y)
                .ToList();

            var rows = new List<float>();
            foreach (var y in ys)
            {
                if (rows.Count == 0 || Mathf.Abs(rows[rows.Count - 1] - y) > 4f)
                {
                    rows.Add(y);
                }
            }

            return rows;
        }

        /// <summary>
        /// The spacing between adjacent rows: the median of the real gaps, not the
        /// first one found. The first is whichever odd pair comes back first, and this
        /// menu has an exit entry that is a group rather than a plain button.
        /// </summary>
        private static float MedianGap(List<float> rows, RectTransform template)
        {
            var gaps = new List<float>();
            for (var i = 1; i < rows.Count; i++)
            {
                var gap = Mathf.Abs(rows[i - 1] - rows[i]);
                if (gap > 1f)
                {
                    gaps.Add(gap);
                }
            }

            if (gaps.Count > 0)
            {
                gaps.Sort();
                return gaps[gaps.Count / 2];
            }

            // Nothing to measure: fall back to the template's own height in world units.
            var corners = new Vector3[4];
            template.GetWorldCorners(corners);
            var height = Mathf.Abs(corners[1].y - corners[0].y);
            return height > 1f ? height : 46f;
        }

        private static DefaultUIButton FindOurs(MenuScreen screen) =>
            screen.GetComponentsInChildren<DefaultUIButton>(true)
                .FirstOrDefault(b => b != null && b.name == ButtonName);

        /// <summary>
        /// A button to copy. The hideout button, because it is always present and never
        /// contextual -- the play button changes with matchmaking state and the exit
        /// button is a group rather than a plain button in at least one menu mod.
        /// </summary>
        private static DefaultUIButton FindTemplate(MenuScreen screen)
        {
            var buttons = screen.GetComponentsInChildren<DefaultUIButton>(true)
                .Where(b => b != null && b.name != ButtonName)
                .ToList();

            if (buttons.Count == 0)
            {
                return null;
            }

            return buttons.FirstOrDefault(b => b.name.IndexOf("hideout", StringComparison.OrdinalIgnoreCase) >= 0)
                   ?? buttons.FirstOrDefault(b => b.name.IndexOf("trade", StringComparison.OrdinalIgnoreCase) >= 0)
                   ?? buttons[0];
        }

        private static void OnClicked()
        {
            BlackjackClientPlugin.Log.LogInfo("[Blackjack] menu button clicked");
            BlackjackPanel.Toggle();
        }
    }
}
