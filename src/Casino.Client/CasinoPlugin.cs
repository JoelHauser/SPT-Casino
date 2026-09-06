using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace Casino.Client
{
    /// <summary>
    /// SPT Casino: three tables behind one door.
    ///
    /// Blackjack, Poker and Roulette were three mods with three plugins, three
    /// task-bar tabs and three Harmony patches on the same method. This is all three,
    /// with one tab that opens a lobby you pick a table from.
    ///
    /// **The tables themselves are unchanged.** Their panels are compiled in from where
    /// they already live, not copied here, and not a line of them moved -- see the
    /// project file. That was possible because none of them ever referenced the task
    /// bar, the menu icon or the escape key; the only thing they reached outside
    /// themselves for was a log and a MonoBehaviour, which <c>Shims.cs</c> now provides.
    ///
    /// Each game still talks to its own server mod on its own routes. The money paths
    /// are untouched by this merge, deliberately: they are the part that took longest
    /// to get right and the part where a mistake costs somebody roubles.
    /// </summary>
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public class CasinoPlugin : BaseUnityPlugin
    {
        public const string PluginGuid = "com.mybutthasarash.sptcasino";
        public const string PluginName = "SPT Casino";
        public const string PluginVersion = "1.0.1";

        internal static ManualLogSource Log;

        /// <summary>
        /// The plugin itself, so code that is not a MonoBehaviour can start a coroutine
        /// and find the art beside the DLL. Every table asks for this.
        /// </summary>
        internal static CasinoPlugin Instance;

        /// <summary>
        /// Whether CASINO appears on the bar along the bottom of the menu.
        ///
        /// On by default, and it is the only way in: the bar is on every out-of-raid
        /// screen, which is the difference between reaching a table from the hideout
        /// and backing out of it first.
        /// </summary>
        internal static ConfigEntry<bool> ShowTaskBarTab;

        /// <summary>
        /// Which end of the bar the tab sits on. Left by default, with MAIN MENU and
        /// HIDEOUT -- those are places you go, which is what the casino is.
        /// </summary>
        internal static ConfigEntry<bool> TabOnRight;

        private void Awake()
        {
            Instance = this;
            Log = Logger;

            // Where the art is and where to log, for the shared drawing code.
            Casino.Shared.Host.Plugin = this;
            Casino.Shared.Host.Log = Logger;

            // The tables were written against their own plugins. One plugin now, so it
            // answers to all three names. See Shims.cs.
            Roulette.Client.RouletteClientPlugin.Instance = this;
            Roulette.Client.RouletteClientPlugin.Log = Logger;
            Poker.Client.PokerClientPlugin.Instance = this;
            Poker.Client.PokerClientPlugin.Log = Logger;
            Blackjack.Client.BlackjackClientPlugin.Instance = this;
            Blackjack.Client.BlackjackClientPlugin.Log = Logger;

            ShowTaskBarTab = Config.Bind(
                "Menu",
                "Show the task-bar tab",
                true,
                "Adds CASINO to the bar along the bottom of the menu, so the tables open from "
                + "the hideout, the flea market or a trader screen and not just the main menu.");

            TabOnRight = Config.Bind(
                "Menu",
                "Put the tab on the right",
                false,
                "Sits the tab with CHARACTER and the rest instead of beside MAIN MENU and HIDEOUT. "
                + "The tab moves a second or two after this is changed.");

            Poker.Client.PokerClientPlugin.BuyIn = Config.Bind(
                "Poker",
                "Buy-in",
                1_000_000,
                new ConfigDescription(
                    "What sitting down at the poker table costs, in roubles, and the size of "
                    + "the chip stack you get for it. The blinds stay at 10,000 / 20,000 "
                    + "whatever this is set to, so a smaller buy-in is a shorter stack and a "
                    + "livelier game rather than a cheaper one. The figure in brackets is how "
                    + "many big blinds deep that leaves you.",
                    new AcceptableValueList<int>(
                        200_000,      // 10 big blinds
                        500_000,      // 25
                        1_000_000,    // 50
                        1_500_000,    // 75
                        2_000_000,    // 100
                        3_000_000,    // 150
                        4_000_000,    // 200
                        5_000_000))); // 250

            Blackjack.Client.BlackjackClientPlugin.EnforceTableMaximum = Config.Bind(
                "Blackjack",
                "Enforce the table maximum",
                true,
                "Refuses a wager above the table limit instead of quietly trimming it.");

            try
            {
                new Harmony(PluginGuid).PatchAll(typeof(CasinoEscape));
                CasinoEscape.Applied = true;
            }
            catch (System.Exception ex)
            {
                // Escape still works without this -- Update below watches for the key.
                // What is lost is swallowing it, so the screen underneath backs out too.
                Log.LogError("[Casino] escape will also close the screen behind the casino: " + ex.Message);
            }

            // The tab is not a patch. It watches for the bar instead, because the bar
            // has to be found again after every raid and after any mod that rebuilds
            // the row, and a poll notices both without naming a method that could be
            // renamed.
            StartCoroutine(CasinoTab.Heartbeat());

            Log.LogInfo($"[Casino] client loaded -- {Games.All.Count} tables");
        }

        private void Update()
        {
            if (!CasinoEscape.Applied && Input.GetKeyDown(KeyCode.Escape) && CasinoLobby.Anything)
            {
                CasinoEscape.Back();
            }

            // Shut at the first hint of a raid. The panels sit at a high sorting order
            // behind a nearly opaque backdrop that swallows every click, so a table
            // that outlives the menu locks the player out of their own raid.
            if (CasinoLobby.Anything && CasinoTab.InRaid)
            {
                CasinoLobby.CloseEverything();
            }
        }
    }
}
