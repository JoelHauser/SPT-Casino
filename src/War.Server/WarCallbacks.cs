using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.ItemEvent;
using SPTarkov.Server.Core.Routers;
using SPTarkov.Server.Core.Utils;

namespace War.Server;

/// <summary>
/// HTTP adapter. Serialises what <see cref="WarService"/> decided and surfaces
/// anything worth seeing to the server console. Holds no game logic.
/// </summary>
[Injectable]
public class WarCallbacks(
    HttpResponseUtil httpResponseUtil,
    WarService service,
    EventOutputHolder eventOutputHolder,
    WarLog log)
{
    public ValueTask<string> Ping(PingRequest info, MongoId sessionId)
    {
        var response = service.Ping(sessionId, Output(sessionId));

        log.Detail(
            $"ping from session '{response.SessionId}' -- profile {(response.HasProfile ? "found" : "NOT FOUND")}");

        if (!response.HasProfile)
        {
            log.Error("no profile for that session. If the id above is blank, the session cookie did not resolve.");
        }

        return new ValueTask<string>(httpResponseUtil.NoBody(response));
    }

    public async ValueTask<string> State(StateRequest info, MongoId sessionId) =>
        httpResponseUtil.NoBody(await service.StateAsync(sessionId, Output(sessionId)));

    public async ValueTask<string> Deal(DealRequest info, MongoId sessionId)
    {
        log.Detail($"deal [{sessionId}] -- {info.Ante:N0} + tie {info.TieBet:N0} {info.Wallet}");

        var response = await service.DealAsync(info, sessionId, Output(sessionId));

        if (!response.Ok)
        {
            log.Detail($"refused: {response.Error}");
        }

        return httpResponseUtil.NoBody(response);
    }

    public async ValueTask<string> Decide(DecideRequest info, MongoId sessionId)
    {
        log.Detail($"decide [{sessionId}] -- {info.Choice}");

        var response = await service.DecideAsync(info, sessionId, Output(sessionId));

        if (!response.Ok)
        {
            log.Detail($"refused: {response.Error}");
        }

        return httpResponseUtil.NoBody(response);
    }

    public ValueTask<string> Stats(StatsRequest info, MongoId sessionId) =>
        new(httpResponseUtil.NoBody(service.Stats(sessionId)));

    /// <summary>
    /// The response the bank writes its change records into. **From
    /// `EventOutputHolder`, never from `new`** -- see <see cref="IBank"/>.
    /// </summary>
    private ItemEventRouterResponse Output(MongoId sessionId) => eventOutputHolder.GetOutput(sessionId);
}
