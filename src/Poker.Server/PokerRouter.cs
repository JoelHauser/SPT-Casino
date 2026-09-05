using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Utils;

namespace Poker.Server;

/// <summary>
/// Registers the mod's HTTP surface.
///
/// Plain static paths, so the whole thing can be exercised with a script against a
/// running server and no game client attached -- which is the only way any of this
/// gets tested until the BepInEx plugin exists. See `scripts/smoke.ps1`.
///
/// A static route cannot update the client's own inventory model, so when money
/// starts moving these will be joined by item-event actions rather than replaced by
/// them. Two transports, one service.
/// </summary>
[Injectable(TypePriority = OnLoadOrder.Routers)]
public class PokerRouter(JsonUtil jsonUtil, PokerCallbacks callbacks)
    : StaticRouter(
        jsonUtil,
        [
            new RouteAction<PingRequest>(
                "/poker/ping",
                async (url, info, sessionId, output, cancellationToken) =>
                    await callbacks.Ping(info, sessionId)),

            new RouteAction<SitRequest>(
                "/poker/sit",
                async (url, info, sessionId, output, cancellationToken) =>
                    await callbacks.Sit(info, sessionId)),

            new RouteAction<DealRequest>(
                "/poker/deal",
                async (url, info, sessionId, output, cancellationToken) =>
                    await callbacks.Deal(info, sessionId)),

            new RouteAction<ActRequest>(
                "/poker/act",
                async (url, info, sessionId, output, cancellationToken) =>
                    await callbacks.Act(info, sessionId)),

            new RouteAction<StateRequest>(
                "/poker/state",
                async (url, info, sessionId, output, cancellationToken) =>
                    await callbacks.State(info, sessionId)),

            new RouteAction<LeaveRequest>(
                "/poker/leave",
                async (url, info, sessionId, output, cancellationToken) =>
                    await callbacks.Leave(info, sessionId)),
        ])
{
}
