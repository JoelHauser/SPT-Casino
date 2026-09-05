using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace Roulette.Client
{
    /// <summary>
    /// The in-game half of Roulette.
    ///
    /// The server owns the game completely: it decides where the ball lands and
    /// settles every bet before this side has drawn a frame. The spin on screen is
    /// theatre over a result that is already decided, which is the only honest way
    /// round -- a client that decided where the ball stopped would be a client that
    /// decided how much money it won.
    /// </summary>
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public class RouletteClientPlugin : BaseUnityPlugin
    {
        // Deliberately identical to the server mod's ModGuid, and with no ".client"
        // on the end. The Forge checks that both halves declare the GUID the mod is
        // registered under, and rejects an upload where they differ.
        public const string PluginGuid = "com.mybutthasarash.roulette";
        public const string PluginName = "Roulette";
        public const string PluginVersion = "0.1.0";

        internal static ManualLogSource Log;

        /// <summary>
        /// The plugin itself, so code that is not a MonoBehaviour can still start a
        /// coroutine. The spin is one.
        /// </summary>
        internal static RouletteClientPlugin Instance;

        /// <summary>
        /// Whether ROULETTE appears on the bar along the bottom of the menu.
        ///
        /// On by default, and it is the only way in: the bar is on every out-of-raid
        /// screen, which is the difference between reaching the table from the hideout
        /// and backing out of it first.
        /// </summary>
        internal static ConfigEntry<bool> ShowTaskBarTab;

        /// <summary>
        /// Which end of the bar the tab sits on. Left by default, with MAIN MENU and
        /// HIDEOUT -- those are places you go, which is what the table is. With
        /// Blackjack and Poker installed the tabs simply sit beside each other; the row
        /// measures itself and none of them has to know about the others.
        /// </summary>
        internal static ConfigEntry<bool> TabOnRight;

        private void Awake()
        {
            Instance = this;
            Log = Logger;

            ShowTaskBarTab = Config.Bind(
                "Menu",
                "Show the task-bar tab",
                true,
                "Adds ROULETTE to the bar along the bottom of the menu, so the table opens "
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
                // Escape still closes the table without this -- Update below watches
                // for the key. What is lost is swallowing it, so the screen underneath
                // backs out as well.
                Log.LogError("[Roulette] escape will also close the screen behind the table: " + ex.Message);
            }

            // The tab is not a patch. It watches for the bar instead, because the bar
            // has to be found again after every raid and after any mod that rebuilds
            // the row, and a poll notices both without naming a method that could be
            // renamed.
            StartCoroutine(TaskBarTab.Heartbeat());

            Log.LogInfo("[Roulette] client loaded");
        }

        /// <summary>
        /// Escape closes the table -- the fallback path only.
        ///
        /// <see cref="EscapePatch"/> is how this normally happens and it is better,
        /// because a patch on the input tree consumes the command where watching the
        /// key merely races it: the screen underneath takes the same escape on the
        /// same frame and backs out too.
        /// </summary>
        private void Update()
        {
            if (!EscapePatch.Applied && Input.GetKeyDown(KeyCode.Escape))
            {
                RoulettePanel.OnEscape();
            }

            // Closed at the first hint of a raid. The panel's canvas sits at sorting
            // order 30000 behind a nearly opaque backdrop that swallows every click, so
            // a table that outlives the menu locks the player out of their own raid.
            if (RoulettePanel.IsOpen && TaskBarTab.InRaid)
            {
                RoulettePanel.Close();
            }
        }
    }
}
