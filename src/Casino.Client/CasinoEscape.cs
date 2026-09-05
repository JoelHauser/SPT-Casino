using System.Collections.Generic;
using System.Reflection;
using EFT.InputSystem;
using HarmonyLib;

namespace Casino.Client
{
    /// <summary>
    /// Escape, and where it takes you.
    ///
    /// One patch. Each of the three mods used to install its own on the same method,
    /// which worked only because each checked its own panel and no two were ever open
    /// at once. It could not survive a lobby: with somewhere to go *back* to, escape
    /// has to know what is on top, and three patches that each know about one panel
    /// cannot answer that between them.
    ///
    /// The order is the feature:
    ///
    /// 1. At a table, it leaves the table and shows the lobby.
    /// 2. In the lobby, or on the welcome card, it closes the casino.
    /// 3. Otherwise it does nothing and the key goes through untouched.
    ///
    /// ## Why the command is removed rather than answered
    ///
    /// The table is our window floating over one of the game's screens, and the game
    /// has no idea it exists. Watching for the key in `Update` closed the table but did
    /// not stop the key: the stash or the flea market underneath took the same escape
    /// on the same frame and backed out too, so closing the table also left the screen
    /// it was opened from. From the hideout it read as the mod throwing you out of the
    /// hideout.
    ///
    /// EFT's input system is a tree of `InputNode`s under an `InputTree`, and
    /// `InputNodeAbstract.TranslateInput(commands, ref axes, ref cursor)` is what walks
    /// it: each node is handed the same `List&lt;ECommand&gt;` and recurses into its
    /// children. Removing Escape from that list before the root recurses means no
    /// screen below is ever offered it. `InputTree` is the root and does **not**
    /// override `TranslateInput`, so patching the abstract base is patching the root --
    /// one patch for the stash, the flea market, the hideout, a trader screen and
    /// anything a future build adds.
    ///
    /// **The first attempt patched `UIInputRoot.TranslateCommand` and did nothing at
    /// all**, which is worth keeping because it looked like the obvious hook: it is the
    /// root of the UI input tree and its name says it translates commands. Its entire
    /// body is `return ETranslateResult.Ignore` -- a stub. Read the IL of a method
    /// before hanging behaviour off its name.
    /// </summary>
    [HarmonyPatch]
    internal static class CasinoEscape
    {
        /// <summary>
        /// Whether the patch is actually on. The plugin falls back to watching the key
        /// itself if it is not -- a casino that cannot be closed with escape is worse
        /// than one that closes the screen behind it as well.
        /// </summary>
        internal static bool Applied;

        private static MethodBase TargetMethod() =>
            AccessTools.Method(typeof(InputNodeAbstract), nameof(InputNodeAbstract.TranslateInput));

        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        private static void BeforeInput(List<ECommand> commands)
        {
            if (commands == null || !CasinoLobby.Anything)
            {
                return;
            }

            // Remove reports whether it was there, which is also what stops this firing
            // twice: the root strips it, and every node reached afterwards finds nothing.
            if (!commands.Remove(ECommand.Escape))
            {
                return;
            }

            Back();
        }

        /// <summary>One step out. Table to lobby, lobby to gone.</summary>
        internal static void Back()
        {
            var playing = Games.Playing();

            if (playing != null)
            {
                CasinoLobby.Leave(playing);
                return;
            }

            CasinoLobby.CloseEverything();
        }
    }
}
