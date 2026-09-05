using System;
using Comfort.Common;
using EFT.UI;

namespace Blackjack.Client
{
    /// <summary>
    /// Tells the game to pick up the money the server has just moved.
    ///
    /// The table talks to the server over its own routes, and money moved that way
    /// lands in the profile but leaves the running game none the wiser: the stash on
    /// screen still shows the roubles that have already gone. That is not only a
    /// display fault. The client goes on believing in stacks the server has deleted,
    /// so the next time the player drags one in their stash the game sends an
    /// operation naming an item that is no longer there, and the server answers
    ///
    ///     Unable to merge stacks as destination item: ... cannot be found
    ///
    /// SPT holds the profile changes it has made for a session until the client's
    /// next item event, and hands them back on that reply. So the fix is not to
    /// re-send the money -- it has already moved, correctly -- but to give the client
    /// a reason to ask. This sends an item event that does nothing at all, purely so
    /// the reply carries the changes the game then applies to its own inventory.
    ///
    /// Deliberately not a rewrite of how bets are placed. The money path works and is
    /// covered by tests; what was missing was the client being told.
    /// </summary>
    internal static class ProfileSync
    {
        /// <summary>
        /// The event body. A public field named exactly as the server reads it: SPT
        /// matches item-event actions case-sensitively, and this is the shape EFT's
        /// own operations take, so the game's serialiser writes it unchanged.
        /// </summary>
        private sealed class SyncOperation
        {
            public string Action = "BlackjackSync";

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
                // Never let this take the table down with it. The hand is already
                // settled and the money already moved; the worst case without it is a
                // stash view that is stale until the game reloads, which is exactly
                // where this started.
                BlackjackClientPlugin.Log.LogError($"Could not ask the game to resync: {error}");
            }
        }

        private static void OnSynced(IResult result)
        {
            if (result != null && result.Failed)
            {
                BlackjackClientPlugin.Log.LogWarning(
                    $"The game refused the resync: {result.Error}. The stash may read stale until it reloads.");
            }
        }
    }
}
