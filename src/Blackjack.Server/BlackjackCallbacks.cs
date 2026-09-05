using Blackjack.Game;
using SPTarkov.Server.Core.Models.Eft.ItemEvent;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Routers;
using SPTarkov.Server.Core.Utils;

namespace Blackjack.Server;

/// <summary>
/// HTTP adapter. Serialises what <see cref="BlackjackService"/> decided and
/// surfaces warnings to the server console. Deliberately holds no game logic --
/// everything worth testing lives one layer down, where it is reachable without
/// a running server.
/// </summary>
[Injectable]
public class BlackjackCallbacks(
    HttpResponseUtil httpResponseUtil,
    BlackjackService service,
    EventOutputHolder eventOutputHolder,
    IBank bank,
    BlackjackLog log)
{
    private static int _limitsReported;

    /// <summary>
    /// Prints the stack limits actually in force, once per server run.
    ///
    /// Deliberately not done at startup. Item mods that rewrite stack sizes can run
    /// after every OnLoad stage this mod can register for -- BarterItemsStacks lands
    /// about half a second after PostLoad + 1 -- so a number read at boot is the base
    /// database value, and printing it would be confidently wrong. By the time a
    /// request arrives the database has settled.
    ///
    /// Worth printing at all because payouts are split by these, and a limit of 1
    /// means one item per unit: a twenty-coin win needs twenty free grid cells.
    /// </summary>
    private void ReportStackLimitsOnce()
    {
        if (Interlocked.Exchange(ref _limitsReported, 1) != 0)
        {
            return;
        }

        log.Info(
            "stack limits in force: "
            + string.Join(", ", WalletInfo.All.Select(w => $"{w.Label} {bank.MaxStackSize(w.Wallet):N0}")));
    }

    /// <summary>
    /// A response object SPT's own inventory helpers can write into.
    ///
    /// It must come from EventOutputHolder, not from `new`. A fresh
    /// ItemEventRouterResponse initialises none of its properties, and
    /// RemoveItemByCount reaches straight into output.ProfileChanges[sessionId],
    /// so a hand-built one throws NullReferenceException -- *after* the items have
    /// already been taken. That failure looked like "not enough roubles" while the
    /// stake quietly left the stash.
    ///
    /// The static routes still cannot return this to the client, so the stash view
    /// stays stale until reload. That is the documented limitation of curl testing.
    /// Being unread is fine; being uninitialised is not.
    /// </summary>
    private ItemEventRouterResponse Output(MongoId sessionId) => eventOutputHolder.GetOutput(sessionId);

    public async ValueTask<string> Deal(DealRequest info, MongoId sessionId)
    {
        Received("deal", sessionId, $"{info.Wager} {info.Wallet}");
        return Respond(await service.DealAsync(info, sessionId, Output(sessionId)));
    }

    public async ValueTask<string> Act(ActionRequest info, MongoId sessionId)
    {
        Received("action", sessionId, info.Action);
        return Respond(await service.ActAsync(info, sessionId, Output(sessionId)));
    }

    public ValueTask<string> State(StateRequest info, MongoId sessionId)
    {
        Received("state", sessionId, null);
        return new ValueTask<string>(Respond(service.State(sessionId, Output(sessionId))));
    }

    public ValueTask<string> Stats(StatsRequest info, MongoId sessionId)
    {
        Received("stats", sessionId, null);
        return new ValueTask<string>(httpResponseUtil.NoBody(service.Stats(sessionId)));
    }

    public ValueTask<string> Ping(PingRequest info, MongoId sessionId)
    {
        var response = service.Ping(sessionId);

        ReportStackLimitsOnce();

        // Always logged, never gated on verbose: this is the line that tells you
        // whether the mod is reachable and whether the session resolved at all.
        log.Info(
            $"ping from session '{response.SessionId}' -- profile {(response.HasProfile ? "found" : "NOT FOUND")}"
            + (response.HasProfile ? $", {string.Join(", ", response.Balances.Select(b => $"{b.Key} {b.Value:N0}"))}" : ""));

        if (!response.HasProfile)
        {
            log.Error("no profile for that session. If the id above is blank, the session cookie did not resolve.");
        }

        return new ValueTask<string>(httpResponseUtil.NoBody(response));
    }

    private void Received(string route, MongoId sessionId, string? detail) =>
        log.Detail($"-> {route} [{sessionId}]{(detail is null ? "" : $" {detail}")}");

    private string Respond(BlackjackResponse response)
    {
        // A warning means the round went through but money did not, so it is an error
        // regardless of how quiet the log is set to be.
        if (response.Warning is not null)
        {
            log.Error(response.Warning);
        }

        // Always written, not gated on verbose: a stake reappearing needs a reason
        // beside it or it reads as a payout bug.
        if (response.Note is not null)
        {
            log.Info(response.Note);
        }

        if (!response.Ok)
        {
            log.Detail($"<- refused: {response.Error}");
        }
        else if (response.Round is not null)
        {
            var round = response.Round;
            var hands = string.Join(
                " | ",
                round.PlayerHands.Select(h => $"{string.Join(" ", h.Cards)} ({h.Value}){(h.Outcome == HandOutcome.Pending ? "" : $" {h.Outcome}")}"));

            log.Detail(
                $"<- {round.Phase} dealer [{string.Join(" ", round.Dealer.Cards)}] ({round.Dealer.Value}) "
                + $"you [{hands}] staked {round.TotalWagered:N0} returned {round.TotalReturned:N0} "
                + $"balance {response.Balance:N0} {response.Wallet}");
        }

        return httpResponseUtil.NoBody(response);
    }
}
