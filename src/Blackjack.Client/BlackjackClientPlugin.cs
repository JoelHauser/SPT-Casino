using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace Blackjack.Client
{
    /// <summary>
    /// The in-game half of Blackjack.
    ///
    /// The server owns the game entirely -- it shuffles, deals, decides and moves the
    /// money. This side renders what it is handed and sends what the player asked for.
    /// It never sees the dealer's hole card during a hand, because the server does not
    /// send it.
    /// </summary>
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public class BlackjackClientPlugin : BaseUnityPlugin
    {
        // Deliberately identical to the server mod's ModGuid, and with no ".client"
        // on the end. The Forge checks that both halves declare the GUID the mod is
        // registered under, and rejects an upload where they differ. BepInEx keeps
        // its own plugin registry and SPT's mod GUID lives in the server metadata,
        // so the two identifiers never meet and there is nothing to collide with.
        public const string PluginGuid = "com.mybutthasarash.blackjack";
        public const string PluginName = "Blackjack";
        public const string PluginVersion = "1.1.1";

        internal static ManualLogSource Log;

        /// <summary>
        /// The plugin itself, so code that is not a MonoBehaviour can still start a
        /// coroutine -- the task-bar tab's heartbeat.
        /// </summary>
        internal static BlackjackClientPlugin Instance;

        /// <summary>
        /// Whether the table's maximum bet applies.
        ///
        /// On by default, and it is the house's only real protection. A 0.45% edge
        /// over six decks is nothing across a session; what stops a player compounding
        /// is being unable to cover a losing streak by doubling up, which a maximum of
        /// five hundred times the minimum caps at nine doubles.
        ///
        /// Turning it off is a supported answer -- it is a single-player game and the
        /// stash is the player's own -- so it lives in the F12 menu rather than in a
        /// config file that needs the server restarted.
        /// </summary>
        internal static ConfigEntry<bool> EnforceTableMaximum;

        /// <summary>
        /// Whether the table gets a tab on the bar along the bottom of the menu.
        ///
        /// On by default, and it is now the only way in at all -- the main-menu button
        /// has been removed. It is a switch rather than a certainty because the tab is
        /// grafted onto
        /// a row this mod does not own: another mod could fill that row with its own
        /// entries, and a player who does not want ours there should not have to choose
        /// between the tab and the mod.
        /// </summary>
        internal static ConfigEntry<bool> ShowTaskBarTab;

        /// <summary>
        /// Which end of the bar the tab sits on: with MAIN MENU and HIDEOUT on the left,
        /// or with CHARACTER and the rest on the right.
        ///
        /// Left by default. Those two are places you go, which is what the table is; the
        /// right-hand group is things you look at while you are somewhere.
        /// </summary>
        internal static ConfigEntry<bool> TabOnRight;

        private void Awake()
        {
            Instance = this;
            Log = Logger;

            EnforceTableMaximum = Config.Bind(
                "Table",
                "Enforce maximum bet",
                true,
                "The table's per-currency maximum: 500,000 roubles a hand, 5,000 dollars or euros, "
                + "50 GP, 10 bitcoin, 5 Lega. Turn this off to bet as much as you are carrying. "
                + "The minimum always applies.");

            ShowTaskBarTab = Config.Bind(
                "Menu",
                "Show the task-bar tab",
                true,
                "Adds BLACKJACK to the bar along the bottom of the menu, so the table opens "
                + "from the hideout, the flea market or a trader screen and not just the main menu.");

            TabOnRight = Config.Bind(
                "Menu",
                "Put the tab on the right",
                false,
                "Sits the tab with CHARACTER and the rest instead of beside MAIN MENU and HIDEOUT. "
                + "The tab moves a second or two after this is changed.");

            try
            {
                new Harmony(PluginGuid).PatchAll(typeof(EscapePatch));
                EscapePatch.Applied = true;
            }
            catch (System.Exception ex)
            {
                // Escape still closes the table without this -- Update below watches for
                // the key. What is lost is swallowing it, so the screen underneath backs
                // out as well.
                Log.LogError("[Blackjack] escape will also close the screen behind the table: " + ex.Message);
            }

            // The tab is not a patch. It watches for the bar instead, because the bar has
            // to be found again after every raid and after any mod that rebuilds the row,
            // and a poll notices both without naming a method that could be renamed.
            StartCoroutine(TaskBarTab.Heartbeat());

            Log.LogInfo("[Blackjack] client loaded");
        }

        /// <summary>
        /// Escape closes the table -- the fallback path only.
        ///
        /// <see cref="EscapePatch"/> is how this normally happens, and it is better,
        /// because a patch on the input tree consumes the command where watching the key
        /// merely races it: the screen underneath took the same escape on the same frame
        /// and backed out, so closing the table also left the stash or the hideout. This
        /// only runs if the patch would not apply, where closing the screen behind is
        /// still better than a table that cannot be closed at all.
        /// </summary>
        private void Update()
        {
            if (!EscapePatch.Applied && Input.GetKeyDown(KeyCode.Escape))
            {
                BlackjackPanel.OnEscape();
            }

            // Closed at the first hint of a raid, and closed here rather than in the tab's
            // once-a-second heartbeat, which is what it used to rely on. A poll can be up
            // to a second late, and late here is not a cosmetic fault: the panel's canvas
            // is at sorting order 30000 with a nearly opaque backdrop that swallows every
            // click, so a table that outlives the menu locks the player out of their own
            // raid. In co-op the moment is not even theirs to choose -- the host starts
            // the raid and pulls them out of the lobby with the table open.
            //
            // See TaskBarTab.InRaid for why the test is the earliest signal rather than
            // the most accurate one, and for the attempt at playing on through the
            // loading screen that had to be taken out.
            if (BlackjackPanel.IsOpen && TaskBarTab.InRaid)
            {
                BlackjackPanel.OnEscape();
            }
        }
    }
}
