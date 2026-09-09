using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.DI.Routing;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.ItemEvent;
using SPTarkov.Server.Core.Routers;
using SPTarkov.Server.Core.Utils;

namespace Casino.Server;

/// <summary>
/// Action names the client sends on EFT's own item-event endpoint. Namespaced so they
/// cannot collide with the game's, or with the four tables' own.
/// </summary>
public static class CasinoActions
{
    /// <summary>
    /// Does nothing to the game. Exists so the client has something harmless to send
    /// when it needs the profile changes SPT has been holding for it -- the same
    /// arrangement, and the same reason, as every table's own sync.
    /// </summary>
    public const string Sync = "CasinoSync";
}

/// <summary>
/// The casino's own HTTP surface, which until the gift it did not have one of.
///
/// Two routes, both about the gift: ask whether it is waiting, and take it. Plain
/// static paths, so the whole thing can be exercised against a running server with no
/// game client attached -- the same property every table's router was written for.
/// </summary>
[Injectable(TypePriority = OnLoadOrder.Routers)]
public class CasinoRouter(JsonUtil jsonUtil, CasinoCallbacks callbacks)
    : StaticRouter(
        jsonUtil,
        [
            new RouteAction<GiftStatusRequest>(
                "/casino/gift/status",
                async (url, info, sessionId, output, cancellationToken) =>
                    await callbacks.GiftStatus(info, sessionId)),

            new RouteAction<GiftClaimRequest>(
                "/casino/gift/claim",
                async (url, info, sessionId, output, cancellationToken) =>
                    await callbacks.GiftClaim(info, sessionId)),
        ]);

/// <summary>
/// The one action the casino itself puts on the item-event endpoint.
///
/// **The claim deliberately stays on the static route.** It has a reply the card has
/// to read -- whether the money moved, and whether it went to the message tab -- and
/// an item-event reply is an <c>ItemEventRouterResponse</c>, which has nowhere to
/// carry that. So the claim answers over the static route and the client sends this
/// straight afterwards to collect the profile changes, which is what makes the rouble
/// count behind the menu move without a reload.
/// </summary>
[Injectable(TypePriority = OnLoadOrder.Routers)]
public sealed class CasinoItemEventRouter(CasinoItemEventCallbacks callbacks)
    : ItemEventRouter([
        new ItemRouteAction<CasinoSyncAction>(
            CasinoActions.Sync,
            async (url, pmcData, body, sessionId, output, cancellationToken) =>
                await callbacks.Sync(sessionId, output)),
    ]);

/// <summary>
/// Answers the sync action.
///
/// The reply's body is not the point and the client ignores it. What matters is that
/// this is an item-event response at all: SPT holds the profile changes it has made
/// for a session until the client's next item event and hands them over on that reply.
/// </summary>
[Injectable]
public class CasinoItemEventCallbacks
{
    public Task<ItemEventRouterResponse> Sync(MongoId sessionId, ItemEventRouterResponse output) =>
        Task.FromResult(output);
}
