using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.ItemEvent;
using SPTarkov.Server.Core.Routers;
using SPTarkov.Server.Core.Utils;

namespace Roulette.Server;

/// <summary>
/// HTTP adapter. Serialises what <see cref="RouletteService"/> decided and surfaces
/// anything worth seeing to the server console.
///
/// Deliberately holds no game logic -- everything worth testing lives one layer down,
/// where it is reachable without a running server.
/// </summary>
[Injectable]
public class RouletteCallbacks(
    HttpResponseUtil httpResponseUtil,
    RouletteService service,
    EventOutputHolder eventOutputHolder,
    IBank bank,
    RouletteLog log)
{
    private static int _limitsReported;

    public ValueTask<string> Ping(PingRequest info, MongoId sessionId)
    {
        var response = service.Ping(sessionId);

        ReportStackLimitsOnce(response.HasProfile);

        // Always logged, never gated on verbose: this is the line that tells you
        // whether the mod is reachable and whether the session resolved at all.
        log.Info(
            $"ping from session '{response.SessionId}' -- profile {(response.HasProfile ? "found" : "NOT FOUND")}"
            + (response.HasProfile
                ? $", {string.Join(", ", response.Balances.Select(b => $"{b.Key} {b.Value:N0}"))}"
                : string.Empty));

        if (!response.HasProfile)
        {
            log.Error("no profile for that session. If the id above is blank, the session cookie did not resolve.");
        }

        return new ValueTask<string>(httpResponseUtil.NoBody(response));
    }

    public ValueTask<string> Place(PlaceRequest info, MongoId sessionId)
    {
        Received("place", sessionId, $"{info.Amount:N0} on {info.Kind} {info.Selection}");
        return new ValueTask<string>(Respond(service.Place(info, sessionId)));
    }

    public ValueTask<string> Remove(RemoveRequest info, MongoId sessionId)
    {
        Received("remove", sessionId, $"{info.Amount:N0} off {info.Kind} {info.Selection}");
        return new ValueTask<string>(Respond(service.Remove(info, sessionId)));
    }

    public ValueTask<string> Clear(ClearRequest info, MongoId sessionId)
    {
        Received("clear", sessionId, null);
        return new ValueTask<string>(Respond(service.Clear(sessionId)));
    }

    public async ValueTask<string> Spin(SpinRequest info, MongoId sessionId)
    {
        Received("spin", sessionId, null);
        return Respond(await service.SpinAsync(sessionId, Output(sessionId)));
    }

    public async ValueTask<string> State(StateRequest info, MongoId sessionId)
    {
        Received("state", sessionId, null);
        return Respond(await service.StateAsync(sessionId, Output(sessionId)));
    }

    public async ValueTask<string> Leave(ClearRequest info, MongoId sessionId)
    {
        Received("leave", sessionId, null);
        return Respond(await service.LeaveAsync(sessionId, Output(sessionId)));
    }

    /// <summary>
    /// The response the bank writes its change records into.
    ///
    /// **From `EventOutputHolder`, never from `new`.** A hand-built one initialises
    /// nothing and `RemoveItemByCount` reaches straight into
    /// `output.ProfileChanges[sessionId]`, so it throws after the items are already
    /// gone. On Blackjack that surfaced as "not enough roubles" while the stake had
    /// left the stash.
    /// </summary>
    private ItemEventRouterResponse Output(MongoId sessionId) => eventOutputHolder.GetOutput(sessionId);

    private string Respond(RouletteResponse response)
    {
        // A refusal is logged plainly. It is nearly always the engine telling the
        // client its picture has drifted, and that is worth seeing while the client is
        // being written.
        if (!response.Ok)
        {
            log.Info($"refused: {response.Error}");
        }

        return httpResponseUtil.NoBody(response);
    }

    private void Received(string route, MongoId sessionId, string? detail) =>
        log.Detail($"{route} [{sessionId}]{(detail is null ? string.Empty : $" -- {detail}")}");

    /// <summary>
    /// Reports the live stack limits once, on first contact.
    ///
    /// Not at startup: `PostLoad` is not last, and BarterItemsStacks rewrites every
    /// limit about half a second later, so anything printed at boot is the base
    /// database value and wrong on any server with an item mod. First contact is the
    /// earliest the answer is trustworthy.
    /// </summary>
    private void ReportStackLimitsOnce(bool hasProfile)
    {
        if (!hasProfile || Interlocked.Exchange(ref _limitsReported, 1) == 1)
        {
            return;
        }

        foreach (var wallet in WalletInfo.All)
        {
            log.Info($"{wallet.Label} stacks to {bank.MaxStackSize(wallet.Wallet):N0} on this server.");
        }
    }
}
