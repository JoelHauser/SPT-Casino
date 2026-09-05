using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace Poker.Client
{
    /// <summary>
    /// The in-game half of Poker.
    ///
    /// The server owns the game entirely -- it shuffles, deals, runs the bots and
    /// (once the money path exists) moves the currency. This side renders what it is
    /// handed and sends what the player asked for. It never sees another seat's hole
    /// cards during a hand, because the server does not send them: the view fills in
    /// a hand only for seats that reached a showdown.
    /// </summary>
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public class PokerClientPlugin : BaseUnityPlugin
    {
        // Deliberately identical to the server mod's ModGuid, and with no ".client"
        // on the end. The Forge checks that both halves declare the GUID the mod is
        // registered under, and rejects an upload where they differ. BepInEx keeps
        // its own plugin registry and SPT's mod GUID lives in the server metadata,
        // so the two identifiers never meet and there is nothing to collide with.
        public const string PluginGuid = "com.mybutthasarash.poker";
        public const string PluginName = "Poker";
        public const string PluginVersion = "1.0.2";

        internal static ManualLogSource Log;

        /// <summary>
        /// The plugin itself, so code that is not a MonoBehaviour can still start a
        /// coroutine -- the task-bar tab's heartbeat, and the one-shot that measures the
        /// tab a frame after it is built.
        /// </summary>
        internal static PokerClientPlugin Instance;

        /// <summary>
        /// Whether POKER appears on the bar along the bottom of the menu.
        ///
        /// On by default, and it is the only way in: the bar is on every out-of-raid
        /// screen, which is the difference between reaching the table from the hideout
        /// and backing out of it first. Switching this off leaves the mod loaded with
        /// nothing to open it.
        /// </summary>
        internal static ConfigEntry<bool> ShowTaskBarTab;

        /// <summary>
        /// Which end of the bar the tab sits on: with MAIN MENU and HIDEOUT on the left,
        /// or with CHARACTER and the rest on the right.
        ///
        /// Left by default, matching Blackjack. Those two are places you go, which is
        /// what the table is; the right-hand group is things you look at while you are
        /// somewhere. With both mods installed the two tabs simply sit beside each other
        /// -- the row measures itself and neither has to know about the other.
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
                "Adds POKER to the bar along the bottom of the menu, so the table opens "
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
                Log.LogError("[Poker] escape will also close the screen behind the table: " + ex.Message);
            }

            // The tab is not a patch. It watches for the bar instead, because the bar has
            // to be found again after every raid and after any mod that rebuilds the row,
            // and a poll notices both without naming a method that could be renamed.
            StartCoroutine(TaskBarTab.Heartbeat());

            Log.LogInfo("[Poker] client loaded");
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
                PokerPanel.OnEscape();
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
            if (PokerPanel.IsOpen && TaskBarTab.InRaid)
            {
                PokerPanel.Close();
            }
        }
    }
}
