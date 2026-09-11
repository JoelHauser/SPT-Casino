using System;
using System.Reflection;
using Comfort.Common;
using EFT;
using EFT.UI;
using SPT.Reflection.Utils;

namespace Casino.Shared
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
        /// The action name is the one thing that genuinely differed between the three
        /// copies of this file, so it is the one thing passed in. Each must stay in
        /// step with its own server's `...Actions.Sync`.
        /// </summary>
        private sealed class SyncOperation
        {
            public SyncOperation(string action) => Action = action;

            public string Action;

            public override string ToString() => Action;
        }

        /// <summary>
        /// Asks the game to collect whatever the server has been holding for it.
        ///
        /// Safe to call when nothing has changed -- an empty set of changes applies as
        /// nothing -- so callers do not have to work out whether money moved.
        /// </summary>
        internal static void Request(string action)
        {
            try
            {
                var session = ItemUiContext.Instance?.ClientSession;

                if (session == null)
                {
                    // `ItemUiContext` is built by the inventory screens, not by the menu:
                    // walk from the task bar straight into the casino without opening a
                    // stash first and there is no instance, so this used to return here
                    // and say nothing -- by design, to avoid a log line every frame.
                    //
                    // The cost of that silence was the whole bug it was hiding. The money
                    // had already moved on the server, but nothing ever asked the client
                    // to collect the changes, so the stash on screen never budged and the
                    // table looked like it was playing for nothing. There was no warning
                    // anywhere to say so.
                    //
                    // The application always has a session while a profile is loaded, and
                    // it is the same object the inventory screens hand back.
                    session = MainAppSession();
                }

                if (session == null)
                {
                    WarnOnce(
                        "[Casino] there is no session to sync against, so the stash on screen "
                        + "will read stale until the game reloads. The money itself has moved.");

                    return;
                }

                session.SendOperationRightNow(new SyncOperation(action), new Callback(OnSynced));
            }
            catch (Exception error)
            {
                // Never let this take the table down with it. The money has already
                // moved; the worst case without it is a stash that reads stale until
                // the game reloads, which is exactly where this started.
                Host.Error($"[Casino] could not ask the game to resync: {error}");
            }
        }

        /// <summary>
        /// The running application's session, reached without naming what it hands back.
        ///
        /// **`GetClientBackEndSession` cannot be called in written-down C#, and 1.2.6
        /// shipped doing exactly that.** Its signature names a class the obfuscator has
        /// renamed to a private-use glyph (U+EA28 in the build this was found on), so
        /// writing the call puts a typeref to that name in this assembly. Mono resolves a
        /// typeref the first time the instruction using it runs, and the name is only
        /// correct for the exact `Assembly-CSharp.dll` it was compiled against -- a game
        /// update moves it and the load throws:
        ///
        ///     TypeLoadException: Could not resolve type with token 01000068 from typeref
        ///
        /// That is the same trap as the pinned `ItemFactory` token, wearing a name instead
        /// of a number, and it was worse here for two reasons. It landed in `Settled`
        /// **before** the try that guards the presentation, so a spin took the money, paid
        /// it, and then drew no win lines, no headline and no result -- and every table
        /// shares this file, so one round killed all four. And before 1.2.6 nothing had
        /// ever reached this line: the old code gave up when `ItemUiContext` was null, so
        /// the typeref sat unresolved and harmless. Adding the fallback that finally found
        /// a session is what turned a silent no-op into a crash.
        ///
        /// Reflection keeps the name out of our metadata entirely. `TarkovApplication` is
        /// a real name and so is `IBackEndSession`, so both ends can be written down; only
        /// the middle had to be described rather than named. `GetMethod` walks base types,
        /// which is where this method is actually declared.
        ///
        /// ## The interface is not called the same thing on both game builds
        ///
        /// 4.1 ships EFT 0.16.9.5-40743, where the interface declaring
        /// `SendOperationRightNow` is `EFT.IClientSession`. This branch builds against
        /// 0.16.9.0-40087, which has no such type: there the same method is declared on
        /// `IBackEndSession`, in the global namespace.
        ///
        /// **Mind the casing.** 0.16.9.5 also carries an `IBackendSession` -- lowercase
        /// "end" -- which is a different type again. The one here is `IBackEndSession`,
        /// and the two are a single keystroke apart, so a name that looks right is not
        /// evidence of anything.
        ///
        /// Found the way this file's own history says to find things: by searching the
        /// assembly for a member of the right shape -- the method name, which has
        /// survived every rename so far -- rather than by assuming the type around it
        /// kept its name.
        /// </summary>
        private static IBackEndSession MainAppSession()
        {
            var app = ClientAppUtils.GetMainApp();

            // Unity's ==, not a raw null check: a torn-down application is not null to one.
            if (app == null)
            {
                return null;
            }

            return typeof(TarkovApplication)
                .GetMethod("GetClientBackEndSession", BindingFlags.Public | BindingFlags.Instance)
                ?.Invoke(app, null) as IBackEndSession;
        }

        private static bool _warned;

        /// <summary>
        /// Says it the first time and then stops. Once per session is a bug report;
        /// once per spin is a reason to stop reading the log.
        /// </summary>
        private static void WarnOnce(string message)
        {
            if (_warned)
            {
                return;
            }

            _warned = true;
            Host.Warn(message);
        }

        private static void OnSynced(IResult result)
        {
            if (result != null && result.Failed)
            {
                Host.Warn(
                    $"[Casino] the game refused the resync: {result.Error}. "
                    + "The stash may read stale until it reloads.");
            }
        }
    }
}
