using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Utils;

namespace HorseRacing.Server;

/// <summary>
/// Registers the table's HTTP surface.
///
/// Three routes, because a race meeting only does three things: say what is running,
/// take a slip, and report the record. There is no state to fetch between them -- the
/// board never changes, so the panel reads it once on open.
///
/// Plain static paths, so the whole thing can be exercised with a script against a
/// running server and no game client attached. See the root `CLAUDE.md` for how --
/// the bodies are zlib-compressed, which is the trap.
/// </summary>
[Injectable(TypePriority = OnLoadOrder.Routers)]
public class RaceRouter(JsonUtil jsonUtil, RaceCallbacks callbacks)
    : StaticRouter(
        jsonUtil,
        [
            new RouteAction<PingRequest>(
                "/races/ping",
                async (url, info, sessionId, output, cancellationToken) =>
                    await callbacks.Ping(info, sessionId)),

            new RouteAction<PlaceRequest>(
                "/races/place",
                async (url, info, sessionId, output, cancellationToken) =>
                    await callbacks.Place(info, sessionId)),

            new RouteAction<StatsRequest>(
                "/races/stats",
                async (url, info, sessionId, output, cancellationToken) =>
                    await callbacks.Stats(info, sessionId)),
        ]);
