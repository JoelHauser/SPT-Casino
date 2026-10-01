using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Utils;

namespace War.Server;

/// <summary>
/// Registers the table's HTTP surface.
///
/// Plain static paths, so the whole thing can be exercised with a script against a
/// running server and no game client attached. The play stays off the item-event
/// endpoint for the same reason the slot machine's does: an item-event reply makes the
/// game apply the money the instant it lands, so the rouble counter would show the
/// result before the cards had turned over. See <see cref="WarItemEventRouter"/>.
/// </summary>
[Injectable(TypePriority = OnLoadOrder.Routers)]
public class WarRouter(JsonUtil jsonUtil, WarCallbacks callbacks)
    : StaticRouter(
        jsonUtil,
        [
            new RouteAction<PingRequest>(
                "/war/ping",
                async (url, info, sessionId, output, cancellationToken) =>
                    await callbacks.Ping(info, sessionId)),

            new RouteAction<StateRequest>(
                "/war/state",
                async (url, info, sessionId, output, cancellationToken) =>
                    await callbacks.State(info, sessionId)),

            new RouteAction<DealRequest>(
                "/war/deal",
                async (url, info, sessionId, output, cancellationToken) =>
                    await callbacks.Deal(info, sessionId)),

            new RouteAction<DecideRequest>(
                "/war/decide",
                async (url, info, sessionId, output, cancellationToken) =>
                    await callbacks.Decide(info, sessionId)),

            new RouteAction<StatsRequest>(
                "/war/stats",
                async (url, info, sessionId, output, cancellationToken) =>
                    await callbacks.Stats(info, sessionId)),
        ]);
