using System;
using Comfort.Common;
using EFT.UI;

namespace Poker.Client
{
    /// <summary>
    /// Tells the game to pick up the money the server has just moved.
    ///
    /// Ported from Blackjack, where it was written after the money path was already
    /// working and the symptom was still there. The table talks to the server over its
    /// own routes, and currency moved that way lands in the profile but leaves the
    /// running game none the wiser: the stash on screen still shows roubles that have
    /// already gone.
    ///
    /// **That is not only a display fault.** The client goes on believing in stacks the
    /// server has deleted, so the next time the player drags one in their stash the
    /// game sends an operation naming an item that is no longer there, and the server
    /// answers
    ///
    ///     Unable to merge stacks as destination item: ... cannot be found
    ///
    /// SPT holds the profile changes it has made for a session until the client's next
    /// item event, and hands them back on that reply. So the fix is not to re-send the
    /// money -- it has already moved, correctly -- but to give the client a reason to
    /// ask. This sends an item event that does nothing at all, purely so the reply
    /// carries the changes the game then applies to its own inventory.
    ///
    /// Deliberately not a rewrite of how the buy-in is paid. The money path works and
    /// is covered by tests on both transports; what was missing was the client being
    /// told.
    /// </summary>
    internal static class ProfileSync
    {
        /// <summary>
        /// The event body. A public field named exactly as the server reads it: SPT
        /// matches item-event actions case-sensitively, and this is the shape EFT's own
        /// operations take, so the game's serialiser writes it unchanged.
        ///
        /// Must stay in step with `PokerActions.Sync` on the server.
        /// </summary>
        private sealed class SyncOperation
        {
            public string Action = "PokerSync";

            public override string ToString() => Action;
        }

        /// <summary>
        /// Asks the game to collect whatever the server has been holding for it.
        ///
        /// Safe to call when nothing has changed -- an empty set of changes applies as
        /// nothing -- so callers do not have to work out whether money moved.
        /// </summary>
        internal static void Request()
        {
            try
            {
                var session = ItemUiContext.Instance?.ClientSession;
                if (session == null)
                {
                    // No session outside the menu, which is the only place the table
                    // opens. Nothing to sync to, and nothing worth logging every frame.
                    return;
                }

                session.SendOperationRightNow(new SyncOperation(), new Callback(OnSynced));
            }
            catch (Exception error)
            {
                // Never let this take the table down with it. The money has already
                // moved; the worst case without it is a stash that reads stale until
                // the game reloads, which is exactly where this started.
                PokerClientPlugin.Log.LogError($"[Poker] could not ask the game to resync: {error}");
            }
        }

        private static void OnSynced(IResult result)
        {
            if (result != null && result.Failed)
            {
                PokerClientPlugin.Log.LogWarning(
                    $"[Poker] the game refused the resync: {result.Error}. "
                    + "The stash may read stale until it reloads.");
            }
        }
    }
}
