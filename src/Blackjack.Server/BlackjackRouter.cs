using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Utils;

namespace Blackjack.Server;

/// <summary>
/// Registers the mod's HTTP surface. Routes are plain static paths, so they can be
/// exercised with curl against a running server without the game client attached.
/// </summary>
[Injectable(TypePriority = OnLoadOrder.Routers)]
public class BlackjackRouter(JsonUtil jsonUtil, BlackjackCallbacks callbacks)
    : StaticRouter(
        jsonUtil,
        [
            new RouteAction<PingRequest>(
                "/blackjack/ping",
                async (url, info, sessionId, output, cancellationToken) =>
                    await callbacks.Ping(info, sessionId)),

            new RouteAction<DealRequest>(
                "/blackjack/deal",
                async (url, info, sessionId, output, cancellationToken) =>
                    await callbacks.Deal(info, sessionId)),

            new RouteAction<ActionRequest>(
                "/blackjack/action",
                async (url, info, sessionId, output, cancellationToken) =>
                    await callbacks.Act(info, sessionId)),

            new RouteAction<StateRequest>(
                "/blackjack/state",
                async (url, info, sessionId, output, cancellationToken) =>
                    await callbacks.State(info, sessionId)),

            new RouteAction<StatsRequest>(
                "/blackjack/stats",
                async (url, info, sessionId, output, cancellationToken) =>
                    await callbacks.Stats(info, sessionId)),
        ])
{
}
