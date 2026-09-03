using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Comfort.Common;
using EFT;
using EFT.UI;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Events;
using UnityEngine.UI;

namespace Blackjack.Client
{
    /// <summary>
    /// Puts a BLACKJACK tab on the bar along the bottom of the menu, beside HIDEOUT and
    /// the rest.
    ///
    /// The main-menu button only exists on the main menu, so reaching the table from the
    /// hideout or the flea market means backing out first. That bar is on every
    /// out-of-raid screen, which makes a tab there the way in from anywhere.
    ///
    /// What the bar actually is, read out of 4.1.3's Assembly-CSharp:
    /// <c>EFT.UI.MenuTaskBar</c>, hanging off the public field of that name on
    /// <c>EFT.UI.PreloaderUI</c> -- a MonoBehaviourSingleton, so it is one static
    /// property away and never needs searching for. Its tabs live in a private
    /// <c>_toggleButtons</c> dictionary keyed by <see cref="EMenuType"/>, and each one is
    /// an <c>EFT.UI.AnimatedToggle</c>, which is a <see cref="Toggle"/> and not a button
    /// at all. That last fact is what most of <see cref="Neuter"/> is about.
    ///
    /// The row itself, read out of the prefab in sharedassets49:
    /// <code>
    /// TaskBar                 MenuTaskBar, Animator, VerticalLayoutGroup
    ///   Tabs                  HorizontalLayoutGroup, ToggleGroup
    ///     MainMenu            wrapper: HorizontalLayoutGroup, ToggleGroup, CanvasGroup, HoverTooltipArea
    ///       MainMenuButton    Image, HorizontalLayoutGroup, Animator, AnimatedToggle, LayoutElement
    ///         Icon            Image
    ///         Text            LocalizedText, CustomTextMeshProUGUI
    ///       NewInformation    the unread badges
    ///     Hideout             ... same shape
    ///     GroupPanel
    ///     Spacer              the empty middle: a layout element, not a coincidence
    ///     Character, Merchants, FleaMarket, EditBuild, Handbook, Chat, Watchlist, News, Settings
    /// </code>
    ///
    /// Two things follow. The toggle is on a child of a tab, not on the tab, so what
    /// gets cloned is its wrapper -- cloning the toggle's own object drops it inside
    /// the hideout tab instead of beside it. And Tabs lays its children out itself, so
    /// placing ours is a sibling index and nothing else: the game does the spacing.
    /// </summary>
    internal static class TaskBarTab
    {
        private const string TabName = "BlackjackTab";

        /// <summary>Our tab, while it lives. Unity's null check covers a destroyed one.</summary>
        private static GameObject _tab;

        /// <summary>
        /// Which end the live tab was built for, so that moving it in the F12 menu takes
        /// effect there and then rather than after the next raid.
        /// </summary>
        private static bool _builtOnRight;

        /// <summary>
        /// What the bar had to say for itself the first time, logged once. The tabs it
        /// carries are not the same on every profile -- a new one has no flea market --
        /// and this is how that shows up in a report.
        /// </summary>
        private static bool _described;

        /// <summary>Whether the clone's own scripts have been named in the log, once.</summary>
        private static bool _describedComponents;

        /// <summary>
        /// A raid, as far as anything on this side is concerned.
        ///
        /// The bar is not destroyed when a raid starts -- it belongs to PreloaderUI,
        /// which outlives everything -- so its absence cannot be the test.
        ///
        /// `GameWorld` existing is the test, and it is deliberately the **earliest**
        /// signal available rather than the most accurate one.
        ///
        /// **Playing on while a raid loads was tried and taken out.** It looked free:
        /// `GameWorld` is created when a raid starts *loading*, so testing it shuts the
        /// table the moment the player queues, and the obvious refinement was to wait for
        /// something that means the raid has actually begun -- `GameWorld.MainPlayer`
        /// being filled in, or `AbstractGame.Status` reaching `Started`. Neither fired.
        /// The table stayed up **into the raid**, and because the panel's backdrop is
        /// nearly opaque and swallows every click, that is not a cosmetic fault: it locks
        /// the player out of their own game.
        ///
        /// So the rule here is not "close when the raid starts", it is **close at the
        /// first hint of one**. Being early costs a few hands of a card game. Being late
        /// costs the raid. Do not trade one for the other again without a way to prove
        /// the replacement signal fires -- and note that neither of those two was
        /// guesswork, both were read out of the installed assembly, and they still did
        /// not work.
        /// </summary>
        internal static bool InRaid => Singleton<GameWorld>.Instantiated;

        private static MenuTaskBar Bar =>
            PreloaderUI.Instantiated ? PreloaderUI.Instance?.MenuTaskBar : null;

        private static bool OnRight =>
            BlackjackClientPlugin.TabOnRight != null && BlackjackClientPlugin.TabOnRight.Value;

        private static bool Wanted =>
            BlackjackClientPlugin.ShowTaskBarTab == null || BlackjackClientPlugin.ShowTaskBarTab.Value;

        /// <summary>
        /// Watches for the bar, forever.
        ///
        /// A heartbeat rather than a Harmony patch on MenuTaskBar.Awake. The tab has to
        /// outlive raids, screen changes and any menu mod that rebuilds the row, and a
        /// poll notices all of those. It costs a static bool and a null check a second.
        /// </summary>
        internal static IEnumerator Heartbeat()
        {
            var idle = new WaitForSeconds(1f);

            while (true)
            {
                yield return idle;

                try
                {
                    Tick();
                }
                catch (Exception ex)
                {
                    // A missing tab is a disappointment. A coroutine that throws is a
                    // coroutine that never runs again, so this never rethrows.
                    BlackjackClientPlugin.Log.LogError("[Blackjack] task-bar tab: " + ex);
                }
            }
        }

        private static void Tick()
        {
            // The table cannot follow a player into a raid. The panel's canvas is
            // DontDestroyOnLoad, so nothing else would take it down, and in a co-op raid
            // the player is not the one who decides when the raid starts -- they can be
            // pulled in from the lobby with the table still open.
            if (InRaid)
            {
                if (BlackjackPanel.IsOpen)
                {
                    BlackjackPanel.Close();
                }

                return;
            }

            if (!Wanted)
            {
                Remove();
                return;
            }

            if (_tab != null)
            {
                if (_builtOnRight == OnRight)
                {
                    return;
                }

                Remove();
            }

            var bar = Bar;
            if (bar == null)
            {
                return;
            }

            Install(bar);
        }

        private static void Remove()
        {
            if (_tab != null)
            {
                UnityEngine.Object.Destroy(_tab);
            }

            _tab = null;
        }

        // ------------------------------------------------------------------ finding it

        /// <summary>
        /// The bar's own tabs, keyed by the screen each one opens.
        ///
        /// Read from the private field rather than by walking children, because the keys
        /// are the whole point: they name the hideout tab as the hideout tab on any
        /// profile, in any language, whatever the object is called.
        /// </summary>
        private static Dictionary<EMenuType, AnimatedToggle> Keyed(MenuTaskBar bar)
        {
            var field = AccessTools.Field(typeof(MenuTaskBar), "_toggleButtons");

            if (field?.GetValue(bar) is Dictionary<EMenuType, AnimatedToggle> map && map.Count > 0)
            {
                return map
                    .Where(pair => pair.Value != null && pair.Value.gameObject.activeInHierarchy)
                    .ToDictionary(pair => pair.Key, pair => pair.Value);
            }

            // The field is private, so it is allowed to be renamed under us. Falling back
            // to the children costs the keys and nothing else.
            BlackjackClientPlugin.Log.LogWarning(
                "[Blackjack] MenuTaskBar._toggleButtons could not be read; falling back to its children.");

            var found = new Dictionary<EMenuType, AnimatedToggle>();
            foreach (var toggle in bar.GetComponentsInChildren<AnimatedToggle>(true))
            {
                if (toggle != null && toggle.name != TabName && toggle.gameObject.activeInHierarchy)
                {
                    found[(EMenuType)toggle.GetInstanceID()] = toggle;
                }
            }

            return found;
        }

        // ----------------------------------------------------------------- building it

        private static void Install(MenuTaskBar bar)
        {
            var tabs = Keyed(bar);
            var row = tabs.Values
                .OrderBy(t => ScreenRect(t.transform as RectTransform).center.x)
                .ToList();

            // Nothing laid out yet: the bar exists from the moment PreloaderUI does, but
            // on a loading screen its tabs are switched off and have no positions worth
            // measuring. The next heartbeat will find them.
            if (row.Count < 2)
            {
                return;
            }

            Describe(tabs, row);

            // A group is a run of tabs with no wide gap in it. The bar has two -- the
            // menu and the hideout on the left, the tools on the right -- and the empty
            // middle is what tells them apart. Ours joins one of them rather than landing
            // in the gap looking homeless.
            var container = Container(row);
            if (container == null)
            {
                return;
            }

            var wantRight = OnRight;
            var group = Group(row, container, wantRight);
            if (group.Count == 0)
            {
                return;
            }

            var template = PickTemplate(tabs, group, wantRight);
            var from = TabRoot(template, container);
            if (from == null)
            {
                return;
            }

            var clone = UnityEngine.Object.Instantiate(from.gameObject, container, false);
            clone.name = TabName;
            clone.SetActive(true);

            Neuter(clone);
            Silence(bar, from, clone.transform);
            Relabel(clone, from, "BLACKJACK");
            MenuIcon.Diamond(clone.transform);
            Hover(clone);

            var click = clone.AddComponent<BlackjackTabClick>();
            click.Mirror = template;

            Place(clone, group, container, wantRight);

            _tab = clone;
            _builtOnRight = wantRight;
            BlackjackClientPlugin.Log.LogInfo(
                $"[Blackjack] task-bar tab added, cloned from '{from.name}' " +
                $"on the {(wantRight ? "right" : "left")}, " +
                $"sibling {clone.transform.GetSiblingIndex()} of {container.name}.");
        }

        private static void Describe(Dictionary<EMenuType, AnimatedToggle> tabs, List<AnimatedToggle> row)
        {
            if (_described)
            {
                return;
            }

            _described = true;

            var names = row.Select(t =>
            {
                var key = tabs.FirstOrDefault(pair => pair.Value == t).Key;
                var x = Mathf.RoundToInt(ScreenRect(t.transform as RectTransform).center.x);
                return $"{key}:{t.name}@{x}";
            });

            BlackjackClientPlugin.Log.LogInfo("[Blackjack] task bar: " + string.Join(", ", names.ToArray()));
        }

        /// <summary>
        /// The half of the row ours is joining -- the tabs left of the middle gap, or the
        /// tabs right of it.
        ///
        /// The gap is a real object: a Spacer sitting between the two halves with a
        /// flexible width, which is how the row pushes one group to each end. Finding it
        /// by that flexibility rather than by measuring the distance between tabs is what
        /// survives other mods. A second and a third tab on this bar eat the spacer's
        /// width, and once enough of them have, the middle gap is no wider than the gaps
        /// between tabs -- at which point measuring says the row is one group and the
        /// BLACKJACK tab lands on the far right, next to SETTINGS.
        /// </summary>
        private static List<AnimatedToggle> Group(List<AnimatedToggle> row, Transform container, bool onRight)
        {
            var divider = Divider(container);

            if (divider >= 0)
            {
                var half = row
                    .Where(t =>
                    {
                        var index = TabRoot(t, container)?.GetSiblingIndex() ?? -1;
                        return index >= 0 && (onRight ? index > divider : index < divider);
                    })
                    .ToList();

                if (half.Count > 0)
                {
                    return half;
                }
            }

            // No flexible gap to split on: fall back to measuring, which is right for any
            // bar laid out by hand.
            var groups = Split(row);
            return onRight ? groups[groups.Count - 1] : groups[0];
        }

        /// <summary>
        /// The sibling index of the stretchy gap in the middle of the row, or -1 if the
        /// row has none. Whatever is set to soak up the leftover width is the divider,
        /// whatever it happens to be called.
        /// </summary>
        private static int Divider(Transform container)
        {
            var at = -1;
            var widest = 0f;

            for (var i = 0; i < container.childCount; i++)
            {
                var element = container.GetChild(i).GetComponent<LayoutElement>();
                if (element == null || !element.enabled)
                {
                    continue;
                }

                if (element.flexibleWidth > widest)
                {
                    widest = element.flexibleWidth;
                    at = i;
                }
            }

            return at;
        }

        /// <summary>
        /// The object the whole row hangs off: the nearest ancestor every tab shares.
        ///
        /// Found rather than named, but what it finds on 0.16.9.5 is <c>Tabs</c>, the
        /// HorizontalLayoutGroup under TaskBar. Everything about placing our tab follows
        /// from this being the thing that lays the row out.
        /// </summary>
        private static Transform Container(List<AnimatedToggle> row)
        {
            for (var step = row[0].transform.parent; step != null; step = step.parent)
            {
                if (row.All(t => t.transform.IsChildOf(step)))
                {
                    return step;
                }
            }

            return null;
        }

        /// <summary>
        /// The tab a toggle belongs to -- the wrapper sitting directly under the row,
        /// not the button the toggle is on.
        ///
        /// A tab is two objects deep: a wrapper carrying the tooltip, the canvas group
        /// and the unread badges, and a button inside it carrying the toggle, the icon
        /// and the label. Cloning the toggle's own object and parenting it where the
        /// toggle sits puts BLACKJACK inside the hideout tab, sharing its slot.
        /// </summary>
        private static Transform TabRoot(Component tab, Transform container)
        {
            if (tab == null)
            {
                return null;
            }

            var step = tab.transform;
            while (step != null && step.parent != container)
            {
                step = step.parent;
            }

            return step;
        }

        /// <summary>
        /// The tab to copy, from the group ours is joining.
        ///
        /// Same group as the destination, so the clone inherits anchors that mean the
        /// same thing where it is going -- a right-anchored tab moved to the left of the
        /// screen walks off it when the window is resized.
        ///
        /// Named preferences rather than "whichever is nearest", because the tabs are not
        /// interchangeable: the hideout's carries the produced-items and failed-items
        /// badges and the messenger's carries three more, and every one of those is a
        /// child that would come along and then never update again. The quiet ones are
        /// preferred, and <see cref="Silence"/> covers what is left.
        /// </summary>
        private static AnimatedToggle PickTemplate(
            Dictionary<EMenuType, AnimatedToggle> tabs,
            List<AnimatedToggle> group,
            bool onRight)
        {
            var order = onRight
                ? new[] { EMenuType.Handbook, EMenuType.Trade, EMenuType.Player, EMenuType.RagFair, EMenuType.EditBuild }
                : new[] { EMenuType.Hideout, EMenuType.MainMenu };

            foreach (var want in order)
            {
                if (tabs.TryGetValue(want, out var tab) && group.Contains(tab))
                {
                    return tab;
                }
            }

            // Whatever is in the group, preferring one that is not currently the selected
            // tab: an AnimatedToggle drives its look from an Animator, and a copy of the
            // lit-up tab stays lit up for ever.
            return group.FirstOrDefault(t => !t.isOn) ?? group.FirstOrDefault();
        }

        /// <summary>
        /// Takes the game's behaviour off the clone while keeping its looks.
        ///
        /// The tabs are <c>AnimatedToggle</c>, which is a <see cref="Toggle"/>. That
        /// matters twice over: a toggle handles its own click without needing a listener
        /// we could clear, and a toggle in a group turns the others off when it comes on,
        /// so a live copy would deselect whatever screen the player is actually looking
        /// at. Disabling it settles both -- a disabled Behaviour is skipped by the event
        /// system, and Toggle.OnDisable leaves its group on the way out.
        ///
        /// Disabled rather than destroyed, because destroying a component another one
        /// requires fails loudly and leaves the clone half dismantled.
        ///
        /// What stays is anything that draws or lays out. The test is the component's
        /// type, not its namespace: the labels on this bar are
        /// <c>CustomTextMeshProUGUI</c>, which is BSG's own class in no namespace at all,
        /// and a namespace test switches the tab's own text off.
        /// </summary>
        private static void Neuter(GameObject clone)
        {
            var stopped = new List<string>();

            // An Animator is a Behaviour and not a MonoBehaviour, so the loop below never
            // saw it and it went on running on a tab whose toggle no longer drives it.
            // What it animates on this bar is the tab's own look -- and on a copy with
            // nothing telling it which state to be in, it settles wherever its default
            // state puts it rather than where an unselected tab sits. Frozen instead:
            // Instantiate copied the template's current values, and the template is
            // picked unselected, so freezing keeps exactly the resting look. The hover
            // highlight below is what gives the tab its feedback back.
            foreach (var animator in clone.GetComponentsInChildren<Animator>(true))
            {
                if (animator != null)
                {
                    animator.enabled = false;
                }
            }

            foreach (var component in clone.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (component == null)
                {
                    continue;
                }

                // The tooltip is worth keeping and re-pointing: it holds a reference to
                // the shared SimpleTooltip that survives being cloned, so this is a real
                // hover tooltip for free.
                if (component is HoverTooltipArea tooltip)
                {
                    tooltip.SetMessageText("Blackjack", true);
                    continue;
                }

                if (Decoration(component))
                {
                    ClearEvents(component);
                    continue;
                }

                // Off before disabled. A toggle switched off while it is still enabled
                // tells its group and its graphic; one that is only disabled leaves the
                // selected-tab mark showing on a tab that is not selected.
                if (component is Toggle toggle)
                {
                    toggle.isOn = false;
                    if (toggle.graphic != null)
                    {
                        toggle.graphic.gameObject.SetActive(false);
                    }
                }

                ClearEvents(component);
                component.enabled = false;
                stopped.Add(component.GetType().FullName);
            }

            if (!_describedComponents && stopped.Count > 0)
            {
                _describedComponents = true;
                BlackjackClientPlugin.Log.LogInfo(
                    "[Blackjack] switched off on the cloned tab: " +
                    string.Join(", ", stopped.Distinct().ToArray()));
            }
        }

        /// <summary>
        /// Anything whose job is to draw or to lay out, which is the half of a cloned tab
        /// worth having. <see cref="Graphic"/> covers every image and label including
        /// BSG's own subclass of TextMeshProUGUI.
        /// </summary>
        private static bool Decoration(MonoBehaviour component) =>
            component is Graphic ||
            component is ILayoutElement ||
            component is ILayoutController ||
            component is Mask ||
            component is RectMask2D;

        /// <summary>
        /// Switches off the little unread-count badges the clone brought with it.
        ///
        /// MenuTaskBar holds each of them in a field of its own -- produced items, failed
        /// items, new messages, attachments, friend requests, hideout nodes, news -- and
        /// drives them on the originals. A copy is driven by nothing, so whatever it was
        /// showing at the moment it was cloned is what it shows for ever: a hideout tab
        /// copied while a craft was waiting keeps that badge until the game restarts.
        ///
        /// Found by asking the bar for its own fields rather than by guessing at child
        /// names, then matched across to the clone by the path they sit at.
        /// </summary>
        private static void Silence(MenuTaskBar bar, Transform template, Transform clone)
        {
            foreach (var field in typeof(MenuTaskBar)
                         .GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                object value;
                try
                {
                    value = field.GetValue(bar);
                }
                catch (Exception)
                {
                    continue;
                }

                GameObject[] badges;
                if (value is GameObject one)
                {
                    badges = new[] { one };
                }
                else if (value is GameObject[] many)
                {
                    badges = many;
                }
                else
                {
                    continue;
                }

                foreach (var badge in badges)
                {
                    if (badge == null)
                    {
                        continue;
                    }

                    var path = PathUnder(template, badge.transform);
                    if (path == null)
                    {
                        continue;
                    }

                    var ours = path.Length == 0 ? clone : clone.Find(path);
                    if (ours != null)
                    {
                        ours.gameObject.SetActive(false);
                    }
                }
            }
        }

        /// <summary>
        /// Where <paramref name="child"/> sits under <paramref name="root"/>, or null if
        /// it is not under it at all.
        /// </summary>
        private static string PathUnder(Transform root, Transform child)
        {
            var parts = new List<string>();

            for (var step = child; step != null; step = step.parent)
            {
                if (step == root)
                {
                    parts.Reverse();
                    return string.Join("/", parts.ToArray());
                }

                parts.Add(step.name);
            }

            return null;
        }

        /// <summary>
        /// Empties every UnityEvent the component has, whatever it is called.
        ///
        /// By reflection because the field names are not ours to know: a UI Button keeps
        /// its listeners in m_OnClick, a Toggle in m_OnValueChanged, EFT's own buttons in
        /// a plain field called OnClick. Anything that fires when clicked is a second
        /// answer to a click we are about to claim.
        /// </summary>
        private static void ClearEvents(MonoBehaviour component)
        {
            var fields = component.GetType()
                .GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

            foreach (var field in fields)
            {
                if (!typeof(UnityEventBase).IsAssignableFrom(field.FieldType))
                {
                    continue;
                }

                try
                {
                    (field.GetValue(component) as UnityEventBase)?.RemoveAllListeners();
                }
                catch (Exception)
                {
                    // A field that will not be read is a field that cannot fire either.
                }
            }
        }

        /// <summary>
        /// Renames the tab and sizes it to the name, so BLACKJACK takes a BLACKJACK-sized
        /// slot on the bar rather than the slot of whatever it was cloned from.
        ///
        /// The label is driven by a LocalizedText that <see cref="Neuter"/> has already
        /// switched off, which is what stops the text reverting to HIDEOUT the next time
        /// the bar is shown or the language is changed.
        ///
        /// **None of this is why the tab came out too wide** -- that was the icon, and it
        /// is fixed in <see cref="MenuIcon"/>. Worth stating plainly, because a tab twice
        /// the width of its neighbours looks like a text-fitting fault and this is the
        /// method anybody would come to first. Poker logged the widths and settled it:
        /// the template's label was 16pt at 64.6 wide and the clone's 16pt at 48.3, while
        /// the icon went from 25 to 160. The label was never involved.
        ///
        /// What is left here is defence, and each line is still worth having:
        ///
        /// - **TMP auto-sizing rescales the letters rather than the box.** This bar's
        ///   labels do not use it, but a label that did would fill the rect its old name
        ///   needed by growing the type -- so the template's size is copied and
        ///   auto-sizing switched off rather than trusted to stay off.
        /// - **The size is set in both directions.** The old code only ever widened, so a
        ///   name that fitted with room to spare kept the width of the tab it was copied
        ///   from. It never fired here -- this bar sizes its tabs from their contents and
        ///   the hint is unset -- but it would on a bar that did not.
        /// - **The chrome is measured on the template**, its tab width less its own label
        ///   width, rather than on the clone where the padding gets counted twice: once
        ///   in the measurement and again by the hint that already sits inside it.
        /// </summary>
        private static void Relabel(GameObject clone, Transform template, string text)
        {
            var label = clone.GetComponentInChildren<TextMeshProUGUI>(true);
            if (label == null)
            {
                return;
            }

            var original = template.GetComponentInChildren<TextMeshProUGUI>(true);

            // Read from the template before our own text is set, because auto-sizing
            // rewrites fontSize as it fits and reading it afterwards reads whatever this
            // label had just decided for BLACKJACK rather than what the bar is set in.
            if (original != null && original.fontSize > 0f)
            {
                label.enableAutoSizing = false;
                label.fontSize = original.fontSize;
            }

            label.text = text;
            label.enabled = true;

            var root = (RectTransform)clone.transform;
            var rect = label.rectTransform;

            // What the tab is wider than its label by: the icon, the gaps and the
            // padding. Taken from the template, where both numbers describe a tab the
            // bar has actually laid out.
            var theirs = original != null ? original.rectTransform.rect.width : 0f;
            var chrome = Mathf.Max(0f, ((RectTransform)template).rect.width - theirs);
            var needed = label.GetPreferredValues(text).x;
            var wanted = needed + chrome;

            // On the button the label sits on, not on the tab: the wrapper's own
            // LayoutElement, where there is one, belongs to the badges. Set in both
            // directions -- a hint left at the old name's width is exactly the bug.
            var hint = label.GetComponentInParent<LayoutElement>();
            if (hint != null)
            {
                if (hint.preferredWidth > 0f)
                {
                    hint.preferredWidth = wanted;
                }

                if (hint.minWidth > 0f)
                {
                    hint.minWidth = wanted;
                }
            }

            // Anything that measures its own contents has now been told what they are,
            // and a size set here would only be overwritten by its next pass.
            if (clone.GetComponent<ContentSizeFitter>() != null ||
                clone.GetComponentInParent<LayoutGroup>() != null)
            {
                return;
            }

            rect.sizeDelta = new Vector2(rect.sizeDelta.x + (needed - rect.rect.width), rect.sizeDelta.y);
            root.sizeDelta = new Vector2(root.sizeDelta.x + (wanted - root.rect.width), root.sizeDelta.y);
        }

        /// <summary>
        /// A highlight to light up under the pointer.
        ///
        /// EFT's own hover feedback is an Animator driven by the toggle this clone has
        /// had switched off, so without this the tab is the only dead-feeling thing on
        /// the bar. Behind the content, never in front of it, and it never eats a click.
        /// </summary>
        private static void Hover(GameObject clone)
        {
            var glow = new GameObject("Hover", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
            glow.transform.SetParent(clone.transform, false);
            glow.transform.SetSiblingIndex(0);

            // The tab is itself a horizontal layout group, so without this the highlight
            // would be laid out as one more thing in the row and shove the button along
            // instead of sitting behind it.
            glow.GetComponent<LayoutElement>().ignoreLayout = true;

            var rect = (RectTransform)glow.transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            var image = glow.GetComponent<Image>();
            image.color = new Color(1f, 1f, 1f, 0.12f);
            image.raycastTarget = false;

            glow.SetActive(false);
        }

        /// <summary>
        /// Puts the tab on the end of its group: after the last one on the left, before
        /// the first one on the right, so it never lands in the empty middle -- which on
        /// this bar is a real object called Spacer -- and never covers a neighbour.
        /// </summary>
        private static void Place(GameObject clone, List<AnimatedToggle> group, Transform container, bool onRight)
        {
            var neighbour = TabRoot(onRight ? group[0] : group[group.Count - 1], container);
            if (neighbour == null)
            {
                return;
            }

            if (clone.transform.parent != container)
            {
                clone.transform.SetParent(container, false);
            }

            // The row lays its own children out -- Tabs is a HorizontalLayoutGroup -- so
            // an index is the entire instruction and the spacing is the game's own. Any
            // position set here would be overwritten on its next pass anyway.
            if (container.GetComponent<LayoutGroup>() != null)
            {
                clone.transform.SetSiblingIndex(
                    onRight ? neighbour.GetSiblingIndex() : neighbour.GetSiblingIndex() + 1);
                return;
            }

            // Nothing lays the row out, so we do. Kept for a build whose bar is placed by
            // hand: measured centre to centre, and from our own width rather than the
            // neighbour's, because BLACKJACK is a wider tab than most.
            var rect = (RectTransform)clone.transform;
            var edge = ScreenRect((RectTransform)neighbour);
            var mine = ScreenRect(rect);
            var step = Spacing(group, container);

            var shift = onRight
                ? -(edge.width + mine.width) * 0.5f - step
                : (edge.width + mine.width) * 0.5f + step;

            rect.position = new Vector3(
                neighbour.position.x + shift,
                neighbour.position.y,
                neighbour.position.z);

            clone.transform.SetSiblingIndex(neighbour.GetSiblingIndex());
        }

        /// <summary>
        /// Splits the row into groups wherever the gap between two tabs is more than
        /// twice the usual one. Measured rather than assumed, because a menu mod can
        /// respace the bar and an ultrawide screen stretches the middle.
        /// </summary>
        private static List<List<AnimatedToggle>> Split(List<AnimatedToggle> row)
        {
            var gaps = new List<float>();
            for (var i = 1; i < row.Count; i++)
            {
                gaps.Add(Mathf.Abs(
                    ScreenRect(row[i].transform as RectTransform).center.x -
                    ScreenRect(row[i - 1].transform as RectTransform).center.x));
            }

            var sorted = gaps.OrderBy(g => g).ToList();
            var typical = sorted.Count > 0 ? sorted[sorted.Count / 2] : 0f;

            var groups = new List<List<AnimatedToggle>> { new List<AnimatedToggle> { row[0] } };
            for (var i = 1; i < row.Count; i++)
            {
                if (typical > 0f && gaps[i - 1] > typical * 2f)
                {
                    groups.Add(new List<AnimatedToggle>());
                }

                groups[groups.Count - 1].Add(row[i]);
            }

            return groups;
        }

        /// <summary>
        /// The gap the bar leaves between one tab and the next: the median of the real
        /// edge-to-edge gaps within the group, so a restyled bar keeps its own rhythm. A
        /// group of one has nothing to measure and gets a tenth of a tab.
        /// </summary>
        private static float Spacing(List<AnimatedToggle> group, Transform container)
        {
            var gaps = new List<float>();

            for (var i = 1; i < group.Count; i++)
            {
                var left = ScreenRect(TabRoot(group[i - 1], container) as RectTransform);
                var right = ScreenRect(TabRoot(group[i], container) as RectTransform);
                var gap = right.xMin - left.xMax;
                if (gap > 0f)
                {
                    gaps.Add(gap);
                }
            }

            if (gaps.Count == 0)
            {
                return Mathf.Max(6f, ScreenRect(TabRoot(group[0], container) as RectTransform).width * 0.1f);
            }

            gaps.Sort();
            return gaps[gaps.Count / 2];
        }

        private static Rect ScreenRect(RectTransform rect)
        {
            if (rect == null)
            {
                return new Rect();
            }

            var corners = new Vector3[4];
            rect.GetWorldCorners(corners);

            var min = new Vector2(
                Mathf.Min(corners[0].x, corners[2].x),
                Mathf.Min(corners[0].y, corners[2].y));
            var max = new Vector2(
                Mathf.Max(corners[0].x, corners[2].x),
                Mathf.Max(corners[0].y, corners[2].y));

            // The menu's canvases are screen-space overlay, so world corners are already
            // pixels. Reading a wrong number here only ever costs us the tab's position.
            return new Rect(min, max - min);
        }
    }

    /// <summary>
    /// The click, the hover under it, and the greying-out around it.
    ///
    /// Its own component rather than a listener on something borrowed, because every
    /// borrowed thing on the clone has been switched off on purpose.
    /// </summary>
    internal sealed class BlackjackTabClick : MonoBehaviour, IPointerClickHandler, IPointerEnterHandler, IPointerExitHandler
    {
        /// <summary>
        /// A real tab to take our cue from.
        ///
        /// The bar greys itself out at times when a screen change would be wrong --
        /// SetTaskBarInteractable, loading, preparing a raid -- by walking its own
        /// dictionary of tabs, which ours is not in. Copying a neighbour's state means
        /// the tab dims and stops answering exactly when the rest of the row does,
        /// without having to know why.
        /// </summary>
        internal AnimatedToggle Mirror;

        private Transform _glow;
        private CanvasGroup _group;

        private void Awake()
        {
            _glow = transform.Find("Hover");
            _group = gameObject.GetComponent<CanvasGroup>();
            if (_group == null)
            {
                _group = gameObject.AddComponent<CanvasGroup>();
            }

            // A tab wrapper ships with its canvas group at alpha 0.3 and interactable
            // false -- that is the locked-feature look, and MenuTaskBar turns it on for
            // the tabs it knows about as the profile unlocks them. It does not know about
            // this one, so a clone stays greyed out and swallows its own clicks.
            _group.alpha = 1f;
            _group.interactable = true;
            _group.blocksRaycasts = true;
        }

        private void Update()
        {
            if (Mirror == null || _group == null)
            {
                return;
            }

            _group.alpha = Mirror.interactable ? 1f : 0.4f;
        }

        private bool Live => Mirror == null || Mirror.interactable;

        public void OnPointerClick(PointerEventData eventData)
        {
            // The bar is hidden rather than destroyed by a raid, so this is worth
            // checking rather than assuming a hidden tab cannot be clicked.
            if (!Live || TaskBarTab.InRaid)
            {
                return;
            }

            BlackjackClientPlugin.Log.LogInfo("[Blackjack] task-bar tab clicked");
            BlackjackPanel.Toggle();
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (_glow != null && Live)
            {
                _glow.gameObject.SetActive(true);
            }
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            if (_glow != null)
            {
                _glow.gameObject.SetActive(false);
            }
        }
    }
}
