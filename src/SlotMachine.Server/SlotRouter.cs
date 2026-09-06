using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Utils;

namespace SlotMachine.Server;

/// <summary>
/// Registers the machine's HTTP surface.
///
/// Two routes, because a slot only does two things: say what it is, and take a pull.
/// There is no state to fetch between them.
///
/// Plain static paths, so the whole thing can be exercised with a script against a
/// running server and no game client attached.
/// </summary>
[Injectable(TypePriority = OnLoadOrder.Routers)]
public class SlotRouter(JsonUtil jsonUtil, SlotCallbacks callbacks)
    : StaticRouter(
        jsonUtil,
        [
            new RouteAction<PingRequest>(
                "/slots/ping",
                async (url, info, sessionId, output, cancellationToken) =>
                    await callbacks.Ping(info, sessionId)),

            new RouteAction<PullRequest>(
                "/slots/pull",
                async (url, info, sessionId, output, cancellationToken) =>
                    await callbacks.Pull(info, sessionId)),
        ]);
