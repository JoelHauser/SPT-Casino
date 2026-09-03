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
    /// opened from and dropped the player on the main menu. From the hideout it looked
    /// like the mod was throwing you out of the hideout.
    ///
    /// **The fix is to consume the command, not to be quicker than it.** EFT routes menu
    /// input through an `EFT.InputSystem` tree of `InputNode`s, and every UI screen hangs
    /// under `UIInputRoot`. A prefix on the root's `TranslateCommand` that returns false
    /// means the root never runs and no screen below it is ever offered the command --
    /// one patch, covering the stash, the flea market, the hideout, a trader screen and
    /// anything a future build adds, because they all hang off the same root.
    ///
    /// `BlockAll` rather than `Block` as the answer we leave behind: the table is a modal
    /// window over the whole screen, so while it is up nothing underneath should be
    /// acting on input at all.
    ///
    /// Escape is `ECommand.Escape`, and `ETranslateResult` is nested inside `InputNode`.
    /// Both were read out of the installed `Assembly-CSharp.dll` rather than guessed.
    ///
    /// Poker patches the same method. Two prefixes on one method is ordinary Harmony, and
    /// only the mod whose table is open answers -- the other sees a closed panel and lets
    /// the command through untouched.
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
            AccessTools.Method(typeof(UIInputRoot), nameof(UIInputRoot.TranslateCommand));

        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        // ReSharper disable once InconsistentNaming
        private static bool BeforeCommand(ECommand command, ref InputNode.ETranslateResult __result)
        {
            if (command != ECommand.Escape || !BlackjackPanel.IsOpen)
            {
                return true;
            }

            BlackjackPanel.OnEscape();

            __result = InputNode.ETranslateResult.BlockAll;
            return false;
        }
    }
}
