using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.DI.Routing;

namespace Poker.Server;

/// <summary>Action names the client sends. Namespaced so they cannot collide with EFT's own.</summary>
public static class PokerActions
{
    public const string Sit = "PokerSit";

    public const string Deal = "PokerDeal";

    public const string Act = "PokerAct";

    public const string Leave = "PokerLeave";

    /// <summary>
    /// Does nothing to the game. Exists so the client has something harmless to send
    /// when it needs the profile changes SPT has been holding for it -- see
    /// <see cref="PokerItemEventCallbacks.Sync"/>.
    /// </summary>
    public const string Sync = "PokerSync";
}

/// <summary>
/// The transport the game client uses, and the reason a stash stays in step.
///
/// These arrive on the same endpoint EFT already uses for moving items, so the reply
/// carries the `ProfileChanges` the client applies to its own inventory. That is the
/// whole point: currency moved through a plain static route lands in the profile but
/// leaves the stash on screen stale until a reload, which reads to a player as the mod
/// eating their winnings.
///
/// The static routes in <see cref="PokerRouter"/> stay alongside this. They are how
/// the mod is exercised with a script and no game attached, and they discard the
/// change record because nothing is listening for it.
/// </summary>
[Injectable(TypePriority = OnLoadOrder.Routers)]
public sealed class PokerItemEventRouter(PokerItemEventCallbacks callbacks)
    : ItemEventRouter([
        new ItemRouteAction<PokerSitAction>(
            PokerActions.Sit,
            async (url, pmcData, body, sessionId, output, cancellationToken) =>
                await callbacks.Sit(body, sessionId, output)),

        new ItemRouteAction<PokerDealAction>(
            PokerActions.Deal,
            async (url, pmcData, body, sessionId, output, cancellationToken) =>
                await callbacks.Deal(body, sessionId, output)),

        new ItemRouteAction<PokerActAction>(
            PokerActions.Act,
            async (url, pmcData, body, sessionId, output, cancellationToken) =>
                await callbacks.Act(body, sessionId, output)),

        new ItemRouteAction<PokerLeaveAction>(
            PokerActions.Leave,
            async (url, pmcData, body, sessionId, output, cancellationToken) =>
                await callbacks.Leave(body, sessionId, output)),

        new ItemRouteAction<PokerSyncAction>(
            PokerActions.Sync,
            (url, pmcData, body, sessionId, output, cancellationToken) =>
                new ValueTask<SPTarkov.Server.Core.Models.Eft.ItemEvent.ItemEventRouterResponse>(
                    callbacks.Sync(sessionId, output))),
    ])
{
}
