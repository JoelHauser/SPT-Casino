using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.DI.Routing;
using SPTarkov.Server.Core.Models.Eft.ItemEvent;

namespace Blackjack.Server;

/// <summary>Action names the client sends. Namespaced so they cannot collide with EFT's own.</summary>
public static class BlackjackActions
{
    public const string Deal = "BlackjackDeal";

    public const string Play = "BlackjackPlay";

    /// <summary>
    /// Does nothing. Exists so the client has something harmless to send when it
    /// needs the profile changes SPT has been holding for it -- see
    /// <see cref="BlackjackItemEventCallbacks.Sync"/>.
    /// </summary>
    public const string Sync = "BlackjackSync";
}

/// <summary>
/// The transport the game client uses.
///
/// These arrive on the same endpoint EFT already uses for moving items, so the reply
/// carries the ProfileChanges the client applies to its own inventory. That is the
/// whole point: money moved through a plain static route lands in the profile but
/// leaves the client's stash view stale until it reloads.
///
/// The static routes in <see cref="BlackjackRouter"/> stay alongside this. They are
/// how the mod is tested with curl and no game attached, and they discard the change
/// record because nothing is listening for it.
/// </summary>
[Injectable(TypePriority = OnLoadOrder.Routers)]
public sealed class BlackjackItemEventRouter(BlackjackItemEventCallbacks callbacks)
    : ItemEventRouter([
        new ItemRouteAction<BlackjackDealAction>(
            BlackjackActions.Deal,
            async (url, pmcData, body, sessionId, output, cancellationToken) =>
                await callbacks.Deal(body, sessionId, output)),

        new ItemRouteAction<BlackjackPlayAction>(
            BlackjackActions.Play,
            async (url, pmcData, body, sessionId, output, cancellationToken) =>
                await callbacks.Play(body, sessionId, output)),

        new ItemRouteAction<BlackjackSyncAction>(
            BlackjackActions.Sync,
            (url, pmcData, body, sessionId, output, cancellationToken) =>
                new ValueTask<ItemEventRouterResponse>(callbacks.Sync(sessionId, output))),
    ])
{
}
