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
        /// coroutine. The menu button needs one to wait a frame for other menu mods to
        /// finish before it copies what they left behind.
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
        /// On by default, because it is the only way in that works from the hideout, the
        /// flea market or a trader screen -- the main-menu button is only on the main
        /// menu. It is a switch rather than a certainty because the tab is grafted onto
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
                new Harmony(PluginGuid).PatchAll(typeof(MenuButtonPatch));
            }
            catch (System.Exception ex)
            {
                // The menu button is the second way in, not the only one. A patch that
                // will not apply on this build must not take the task-bar tab down with
                // it, and the tab is not a patch at all.
                Log.LogError("[Blackjack] the main-menu button could not be installed: " + ex.Message);
            }

            // The tab is not a patch. It watches for the bar instead, because the bar has
            // to be found again after every raid and after any mod that rebuilds the row,
            // and a poll notices both without naming a method that could be renamed.
            StartCoroutine(TaskBarTab.Heartbeat());

            Log.LogInfo("[Blackjack] client loaded");
        }

        /// <summary>
        /// Escape closes the table.
        ///
        /// Watched here rather than patched into EFT's own input handling: the table
        /// is our window, not one of the game's screens, so nothing in the game knows
        /// to close it. The key is only acted on while the table is open, so this
        /// cannot interfere with escape anywhere else.
        /// </summary>
        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                BlackjackPanel.OnEscape();
            }
        }
    }
}
