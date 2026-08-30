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
        public const string PluginVersion = "1.0.0";

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

            new Harmony(PluginGuid).PatchAll(typeof(MenuButtonPatch));

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
