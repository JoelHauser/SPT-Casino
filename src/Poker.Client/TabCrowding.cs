using System.Collections.Generic;
using System.Linq;
using EFT.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Poker.Client
{
    /// <summary>
    /// Gives way when the task bar runs out of room.
    ///
    /// ## The bug this exists for
    ///
    /// The bar sizes its tabs from their contents and squeezes them all when the row is
    /// over-subscribed. Past about four mod-added tabs every label starts breaking
    /// mid-word -- HIDEOU/T, CHARACTE/R, BLACKJAC/K -- and the bar becomes unreadable.
    /// It takes Raid Review's optional menu item, PIT Fireteam's slots and one tab each
    /// from Blackjack, Poker and Roulette to get there.
    ///
    /// **We are guests on that row, so we are the ones who give way.** When the bar is
    /// tight this drops our label and leaves the pip, taking our tab from about 112
    /// units to about 40 and handing the difference back. When there is room again it
    /// takes the label back.
    ///
    /// It cannot fix the row alone and does not pretend to. What it guarantees is that
    /// this mod is no longer part of the problem.
    ///
    /// ## Measuring widths did not work, and this is why
    ///
    /// The first attempt asked whether any label was narrower than
    /// `GetPreferredValues` said it wanted. **It never fired once.** TextMeshPro does
    /// not sit at its preferred width and overflow -- it *wraps*, and once it has
    /// wrapped, the width it reports wanting is the width it has. Preferred and actual
    /// agree exactly at the moment the label is at its most broken.
    ///
    /// So ask the question the eye is actually asking: **is a single word being split
    /// across two lines?** `HIDEOUT` on two lines is a bar out of room, and no amount
    /// of width arithmetic was needed to see it. Labels of two or more words are
    /// skipped, because MAIN MENU and FLEA MARKET sit on two lines when the row is
    /// perfectly healthy -- counting those would have the tab permanently compact.
    /// </summary>
    internal static class TabCrowding
    {
        private static bool _compact;
        private static bool _announced;

        /// <summary>
        /// Set when taking the label back immediately made the row wrap again.
        ///
        /// Expanding costs exactly what collapsing saved, so on a row that is full to
        /// the pixel the two states would alternate once a second forever. Once that
        /// has been seen, no further attempt is made until the row itself changes.
        /// </summary>
        private static bool _expandBlocked;

        /// <summary>
        /// What the bar looked like when we last decided. A change in the number of
        /// tabs, or in the resolution, is the only thing that makes it worth trying to
        /// expand again.
        /// </summary>
        private static int _rowSignature;

        private static bool _justExpanded;
        private static bool _censused;

        internal static bool IsCompact => _compact;

        /// <summary>Forgets everything. Called when the tab is destroyed and rebuilt.</summary>
        internal static void Forget()
        {
            _compact = false;
            _announced = false;
            _expandBlocked = false;
            _justExpanded = false;
            _rowSignature = 0;
            _censused = false;
        }

        /// <summary>
        /// Decides whether our tab should be wearing its label, and applies it.
        ///
        /// Runs on the tab's own once-a-second heartbeat, so it follows the row as other
        /// mods add and remove tabs and as the resolution changes, without watching for
        /// either.
        /// </summary>
        internal static void Apply(GameObject tab)
        {
            if (tab == null)
            {
                return;
            }

            var label = OurLabel(tab);
            if (label == null)
            {
                return;
            }

            var labels = BarLabels(tab).ToList();

            Census(labels);

            var signature = Signature(labels);

            // The row changed under us, so whatever we concluded last time was about a
            // different bar.
            if (signature != _rowSignature)
            {
                _rowSignature = signature;
                _expandBlocked = false;
            }

            var squeezed = Squeezed(labels, tab);

            if (squeezed)
            {
                // Taking the label back is what broke it, so stop trying until the row
                // changes shape.
                if (_justExpanded)
                {
                    _expandBlocked = true;
                }

                _justExpanded = false;

                if (!_compact)
                {
                    Set(tab, label, compact: true);
                }

                return;
            }

            _justExpanded = false;

            if (_compact && !_expandBlocked)
            {
                Set(tab, label, compact: false);
                _justExpanded = true;
            }
        }

        /// <summary>
        /// Is a single word being broken across lines anywhere on the bar?
        ///
        /// Our own tab is skipped: once it is compact its label is off, so it can never
        /// be squeezed, and letting it vote would mean the row looked healthy precisely
        /// because we had already given way.
        /// </summary>
        private static bool Squeezed(IEnumerable<TextMeshProUGUI> labels, GameObject ours) =>
            labels.Any(label => !label.transform.IsChildOf(ours.transform) && Broken(label));

        private static bool Broken(TextMeshProUGUI label)
        {
            var text = label.text;

            if (string.IsNullOrWhiteSpace(text) || !label.isActiveAndEnabled)
            {
                return false;
            }

            // Two words are allowed two lines. MAIN MENU and FLEA MARKET are on two
            // lines when nothing at all is wrong.
            if (text.Trim().Contains(' '))
            {
                return false;
            }

            var info = label.textInfo;

            return info != null && info.characterCount > 0 && info.lineCount > 1;
        }

        private static void Set(GameObject tab, TextMeshProUGUI label, bool compact)
        {
            label.enabled = !compact;

            // The tab measures itself from its contents, so switching the label off is
            // enough to shrink it -- but only once something asks for the measurement
            // again.
            LayoutRebuilder.MarkLayoutForRebuild((RectTransform)tab.transform);

            _compact = compact;

            if (compact && !_announced)
            {
                // Said once rather than every second, and worth saying at all: a tab
                // that has quietly dropped its own name is otherwise a mystery to
                // whoever is looking at the bar wondering where POKER went.
                PokerClientPlugin.Log.LogInfo(
                    "[Poker] the task bar is crowded, so the tab is showing its pip without a label. "
                    + "It takes the label back when there is room.");

                _announced = true;
            }
        }

        /// <summary>
        /// Every label on the bar, both sides of the spacer.
        ///
        /// Taken from the whole `MenuTaskBar` rather than from our own group: the tabs
        /// are split into two and ours is in one of them, so scanning only our parent
        /// would miss half the row -- including most of the game's own tabs.
        /// </summary>
        private static IEnumerable<TextMeshProUGUI> BarLabels(GameObject tab)
        {
            var bar = tab.GetComponentInParent<MenuTaskBar>();

            var root = bar != null
                ? bar.transform
                : tab.transform.parent;

            return root == null
                ? []
                : root.GetComponentsInChildren<TextMeshProUGUI>(false);
        }

        /// <summary>
        /// Cheap fingerprint of the row's shape: how many labels and how wide the
        /// screen is. Both are things that change what fits.
        /// </summary>
        private static int Signature(List<TextMeshProUGUI> labels) =>
            (labels.Count * 100_003) ^ Screen.width;

        /// <summary>
        /// Logs the whole row once, the way `Measured` does for the tab itself.
        ///
        /// A layout fault is the one class of bug a compiler, a test and a screenshot
        /// are all bad at: the screenshot says it is wrong, and nothing says which box
        /// is carrying the extra. The first version of this file shipped a check that
        /// never fired, and one line of this log would have said so immediately.
        /// </summary>
        private static void Census(List<TextMeshProUGUI> labels)
        {
            if (_censused || labels.Count == 0)
            {
                return;
            }

            _censused = true;

            var wrapped = labels.Count(Broken);

            PokerClientPlugin.Log.LogInfo(
                $"[Poker] task bar: {labels.Count} labels, {wrapped} single word(s) broken across lines.");

            foreach (var label in labels)
            {
                var info = label.textInfo;

                PokerClientPlugin.Log.LogInfo(
                    $"[Poker]   '{label.text.Replace("\n", " ")}' "
                    + $"w={label.rectTransform.rect.width:0.#} "
                    + $"lines={(info == null ? -1 : info.lineCount)}"
                    + (Broken(label) ? "  <- broken" : string.Empty));
            }
        }

        private static TextMeshProUGUI OurLabel(GameObject tab) =>
            tab.GetComponentsInChildren<TextMeshProUGUI>(true).FirstOrDefault();
    }
}
