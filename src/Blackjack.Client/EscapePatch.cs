using System.Collections.Generic;
using System.Reflection;
using EFT.InputSystem;
using HarmonyLib;

namespace Blackjack.Client
{
    /// <summary>
    /// Escape closes the table and stops there.
    ///
    /// The table is our window floating over one of the game's screens, and the game has
    /// no idea it exists. Watching for the key in `Update` closed the table but did not
    /// stop the key: the stash or the flea market underneath took the same escape on the
    /// same frame and backed out too, so closing the table also left the screen it was
    /// opened from. From the hideout it read as the mod throwing you out of the hideout.
    ///
    /// **The command has to be taken out of the frame's list, not answered.** EFT's input
    /// system is a tree of `InputNode`s under an `InputTree`, and
    /// `InputNodeAbstract.TranslateInput(commands, ref axes, ref cursor)` is what walks
    /// it: each node is handed the same `List&lt;ECommand&gt;` and recurses into its
    /// children. Removing Escape from that list before the root recurses means no screen
    /// below is ever offered it.
    ///
    /// `InputTree` is the root and does **not** override `TranslateInput`, so patching the
    /// abstract base's implementation is patching the root -- one patch for the stash, the
    /// flea market, the hideout, a trader screen and anything a future build adds. Nodes
    /// that do override it are reached afterwards, by which point the command is gone.
    ///
    /// **The first attempt patched `UIInputRoot.TranslateCommand` and did nothing at all**,
    /// which is worth writing down because it looked like the obvious hook: it is the root
    /// of the UI input tree and its name says it translates commands. Its entire body is
    /// `return ETranslateResult.Ignore` -- a stub. Nothing calls it to any effect, so the
    /// patch applied cleanly, logged no error, and left escape working exactly as before
    /// while disabling the key-watching fallback that had at least been closing the table.
    /// **Read the IL of a method before hanging behaviour off its name.**
    ///
    /// Poker patches the same method. Two prefixes on one method is ordinary Harmony, and
    /// only the mod whose table is open takes the command.
    /// </summary>
    [HarmonyPatch]
    internal static class EscapePatch
    {
        /// <summary>
        /// Whether the patch is actually on. The plugin falls back to watching the key
        /// itself if it is not -- a table that cannot be closed with escape is worse than
        /// one that closes the screen behind it as well, and this is a method on a class
        /// a future EFT build is free to rename.
        /// </summary>
        internal static bool Applied;

        private static MethodBase TargetMethod() =>
            AccessTools.Method(typeof(InputNodeAbstract), nameof(InputNodeAbstract.TranslateInput));

        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        private static void BeforeInput(List<ECommand> commands)
        {
            if (commands == null || !BlackjackPanel.IsOpen)
            {
                return;
            }

            // Remove reports whether it was there, which is also what stops this firing
            // twice: the root strips it, and every node reached afterwards finds nothing.
            if (!commands.Remove(ECommand.Escape))
            {
                return;
            }

            BlackjackPanel.OnEscape();
        }
    }
}
