using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.ItemEvent;
using SPTarkov.Server.Core.Routers;
using SPTarkov.Server.Core.Utils;

namespace Poker.Server;

/// <summary>
/// HTTP adapter. Serialises what <see cref="PokerService"/> decided and surfaces
/// anything worth seeing to the server console.
///
/// Deliberately holds no game logic -- everything worth testing lives one layer down,
/// where it is reachable without a running server.
/// </summary>
[Injectable]
public class PokerCallbacks(
    HttpResponseUtil httpResponseUtil,
    PokerService service,
    EventOutputHolder eventOutputHolder,
    IBank bank,
    PokerLog log)
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

    public async ValueTask<string> Sit(SitRequest info, MongoId sessionId)
    {
        Received("sit", sessionId, $"{info.Seats} seats, {info.BuyIn} {info.Wallet}, blind {info.BigBlind}");
        return Respond(await service.SitAsync(info, sessionId, Output(sessionId)));
    }

    public ValueTask<string> Deal(DealRequest info, MongoId sessionId)
    {
        Received("deal", sessionId, null);
        return new ValueTask<string>(Respond(service.Deal(sessionId)));
    }

    public ValueTask<string> Act(ActRequest info, MongoId sessionId)
    {
        Received("act", sessionId, $"{info.Move}{(info.To > 0 ? $" to {info.To}" : string.Empty)}");
        return new ValueTask<string>(Respond(service.Act(info, sessionId)));
    }

    public async ValueTask<string> State(StateRequest info, MongoId sessionId)
    {
        Received("state", sessionId, null);

        // An output, for a request that reads. Asking for the table is the one thing a
        // player does without meaning to spend anything, so it is where an abandoned
        // stack gets given back -- and giving it back moves items. See
        // PokerService.StateAsync.
        return Respond(await service.StateAsync(sessionId, Output(sessionId)));
    }

    public async ValueTask<string> Leave(LeaveRequest info, MongoId sessionId)
    {
        Received("leave", sessionId, null);
        return Respond(await service.LeaveAsync(sessionId, Output(sessionId)));
    }

    /// <summary>
    /// A response object SPT's own inventory helpers can write into.
    ///
    /// It must come from EventOutputHolder, not from `new`. A fresh
    /// ItemEventRouterResponse initialises none of its properties, and
    /// RemoveItemByCount reaches straight into output.ProfileChanges[sessionId], so a
    /// hand-built one throws NullReferenceException -- *after* the items have already
    /// been taken. On Blackjack that failure reported itself as "not enough roubles"
    /// while the stake had quietly left the stash.
    ///
    /// A static route cannot return this to the client, so the stash view stays stale
    /// until reload. Being unread is fine; being uninitialised is not.
    /// </summary>
    private ItemEventRouterResponse Output(MongoId sessionId) => eventOutputHolder.GetOutput(sessionId);

    /// <summary>
    /// Prints the stack limits actually in force, once per server run.
    ///
    /// Deliberately not done at startup. Item mods that rewrite stack sizes can run
    /// after every OnLoad stage this mod can register for -- BarterItemsStacks lands
    /// about half a second after PostLoad + 1 -- so a number read at boot is the base
    /// database value and would be confidently wrong. By the time a request arrives
    /// the database has settled.
    ///
    /// Worth printing because a limit of 1 means one item per unit: a forty-coin win
    /// needs forty free grid cells, and that is what will size the buy-in ceilings.
    /// </summary>
    private void ReportStackLimitsOnce(bool hasProfile)
    {
        if (!hasProfile || Interlocked.Exchange(ref _limitsReported, 1) != 0)
        {
            return;
        }

        log.Info(
            "stack limits in force: "
            + string.Join(", ", WalletInfo.All.Select(w => $"{w.Label} {bank.MaxStackSize(w.Wallet):N0}")));
    }

    private void Received(string route, MongoId sessionId, string? detail) =>
        log.Detail($"-> {route} [{sessionId}]{(detail is null ? string.Empty : $" {detail}")}");

    private string Respond(PokerResponse response)
    {
        // Always written, not gated on verbose: a stack reappearing needs a reason
        // beside it or it reads as a payout bug.
        if (response.Note is not null)
        {
            log.Info(response.Note);
        }

        if (!response.Ok)
        {
            log.Detail($"<- refused: {response.Error}");
        }
        else if (response.Table is { } view)
        {
            var seats = string.Join(
                " | ",
                view.Seats.Select(seat =>
                    $"{seat.Name} {seat.Stack}{(seat.Folded ? " folded" : string.Empty)}"));

            log.Detail($"<- {view.Street} pot {view.Pot} board [{string.Join(" ", view.Community)}] {seats}");
        }

        return httpResponseUtil.NoBody(response);
    }
}
