using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Utils;

namespace Roulette.Server;

/// <summary>
/// Registers the mod's HTTP surface.
///
/// Plain static paths, so the whole thing can be exercised with a script against a
/// running server and no game client attached -- which is the only way any of this
/// gets tested until the BepInEx plugin exists. See `scripts/smoke.ps1`.
///
/// A static route cannot update the client's own inventory model, so when money starts
/// moving these will be joined by item-event actions rather than replaced by them.
/// Two transports, one service -- a second copy of the flow is a second set of money
/// bugs.
/// </summary>
[Injectable(TypePriority = OnLoadOrder.Routers)]
public class RouletteRouter(JsonUtil jsonUtil, RouletteCallbacks callbacks)
    : StaticRouter(
        jsonUtil,
        [
            new RouteAction<PingRequest>(
                "/roulette/ping",
                async (url, info, sessionId, output, cancellationToken) =>
                    await callbacks.Ping(info, sessionId)),

            new RouteAction<PlaceRequest>(
                "/roulette/place",
                async (url, info, sessionId, output, cancellationToken) =>
                    await callbacks.Place(info, sessionId)),

            new RouteAction<RemoveRequest>(
                "/roulette/remove",
                async (url, info, sessionId, output, cancellationToken) =>
                    await callbacks.Remove(info, sessionId)),

            new RouteAction<ClearRequest>(
                "/roulette/clear",
                async (url, info, sessionId, output, cancellationToken) =>
                    await callbacks.Clear(info, sessionId)),

            new RouteAction<SpinRequest>(
                "/roulette/spin",
                async (url, info, sessionId, output, cancellationToken) =>
                    await callbacks.Spin(info, sessionId)),

            new RouteAction<StateRequest>(
                "/roulette/state",
                async (url, info, sessionId, output, cancellationToken) =>
                    await callbacks.State(info, sessionId)),

            new RouteAction<ClearRequest>(
                "/roulette/leave",
                async (url, info, sessionId, output, cancellationToken) =>
                    await callbacks.Leave(info, sessionId)),
        ])
{
}
