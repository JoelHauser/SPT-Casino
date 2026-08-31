using Blackjack.Game;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.ItemEvent;

namespace Blackjack.Server;

/// <summary>
/// The whole server-side game flow: validate what the player asked for, let the
/// table decide, then move money to match.
///
/// Depends only on <see cref="IBank"/>, <see cref="IProfileGateway"/> and
/// <see cref="TableStore"/>, so it runs -- and is tested -- with no SPT server
/// present. HTTP and logging live in <see cref="BlackjackCallbacks"/>.
/// </summary>
[Injectable]
public class BlackjackService(
    IBank bank,
    IProfileGateway profiles,
    TableStore tables,
    IStatsStore stats,
    IEscrowStore escrow)
{
    /// <summary>
    /// Test-only convenience. The throwaway response it builds is not initialised the
    /// way SPT's inventory helpers expect, so anything with a real InventoryHelper
    /// behind it must call the overload below with an EventOutputHolder response.
    /// </summary>
    public Task<BlackjackResponse> DealAsync(DealRequest request, MongoId sessionId) =>
        DealAsync(request, sessionId, new ItemEventRouterResponse());

    /// <summary>
    /// <paramref name="output"/> accumulates the inventory changes. The item-event
    /// transport returns it to the client so the stash updates itself; the static
    /// route passes a throwaway, which is why curl testing shows correct balances but
    /// a stale stash in game.
    /// </summary>
    public async Task<BlackjackResponse> DealAsync(
        DealRequest request,
        MongoId sessionId,
        ItemEventRouterResponse output)
    {
        if (!profiles.HasProfile(sessionId))
        {
            return BlackjackResponse.Failed("No PMC profile for this session.");
        }

        if (!Enum.TryParse<Wallet>(request.Wallet, ignoreCase: true, out var wallet))
        {
            return BlackjackResponse.Failed($"Unknown currency '{request.Wallet}'.");
        }

        var refund = RefundAbandonedStake(sessionId, output);

        var session = tables.For(sessionId);

        if (session.Table.Phase == RoundPhase.PlayerTurn)
        {
            return BlackjackResponse.Failed("A round is already in progress.");
        }

        // Validate the stake before taking it. Letting Deal throw after the debit
        // would pocket the money and leave no hand to win it back with.
        var limits = WalletInfo.For(wallet);
        if (!request.IgnoreMaximum && request.Wager > limits.MaxBet)
        {
            return BlackjackResponse.Failed(
                $"The table takes up to {limits.MaxBet:N0} {limits.Label} a hand.");
        }

        if (request.Wager < limits.MinBet)
        {
            return BlackjackResponse.Failed(
                $"The smallest {limits.Label} bet is {limits.MinBet:N0}.");
        }

        if (!bank.TryDebit(sessionId, wallet, request.Wager, output))
        {
            return BlackjackResponse.Failed(
                $"Not enough {wallet} -- you have {bank.GetBalance(sessionId, wallet)}.");
        }

        // Recorded before the hand is dealt: from here until settlement the money is
        // off the profile, and only this makes it recoverable.
        escrow.Hold(sessionId, wallet, request.Wager);

        session.Wallet = wallet;
        var view = session.Table.Deal(request.Wager, limits.BlackjackPayout);
        session.Staked = view.TotalWagered;

        Settle(session, view, sessionId, output);
        await profiles.SaveAsync(sessionId);

        return Success(view, sessionId, session) with { Note = refund };
    }

    public Task<BlackjackResponse> ActAsync(ActionRequest request, MongoId sessionId) =>
        ActAsync(request, sessionId, new ItemEventRouterResponse());

    public async Task<BlackjackResponse> ActAsync(
        ActionRequest request,
        MongoId sessionId,
        ItemEventRouterResponse output)
    {
        if (!profiles.HasProfile(sessionId))
        {
            return BlackjackResponse.Failed("No PMC profile for this session.");
        }

        if (!Enum.TryParse<PlayerAction>(request.Action, ignoreCase: true, out var action))
        {
            return BlackjackResponse.Failed($"Unknown action '{request.Action}'.");
        }

        var session = tables.For(sessionId);
        if (session.Table.Phase != RoundPhase.PlayerTurn)
        {
            return BlackjackResponse.Failed("No round is in progress.");
        }

        var before = session.Table.View();

        // Doubling and splitting raise the stake. Check affordability *before* the
        // engine acts -- once the hand has changed there is no way to un-split it
        // if the debit then fails.
        if (action is PlayerAction.Double or PlayerAction.Split)
        {
            var extra = before.PlayerHands[before.ActiveHandIndex].Wager;
            if (bank.GetBalance(sessionId, session.Wallet) < extra)
            {
                return Refused($"Not enough {session.Wallet} to {action}.", before, sessionId, session);
            }
        }

        RoundView view;
        try
        {
            view = action switch
            {
                PlayerAction.Hit => session.Table.Hit(),
                PlayerAction.Stand => session.Table.Stand(),
                PlayerAction.Double => session.Table.Double(),
                PlayerAction.Split => session.Table.Split(),
                _ => throw new InvalidOperationException($"Unhandled action {action}."),
            };
        }
        catch (InvalidOperationException ex)
        {
            // The engine is the authority on legality. An illegal request means the
            // client's view drifted, so hand it the real one back.
            return Refused(ex.Message, before, sessionId, session);
        }

        string? warning = null;
        var owed = view.TotalWagered - session.Staked;
        if (owed > 0 && bank.TryDebit(sessionId, session.Wallet, owed, output))
        {
            escrow.Hold(sessionId, session.Wallet, owed);
        }
        else if (owed > 0)
        {
            // Pre-checked above, so reaching here means the profile changed
            // underneath us. The adapter logs it: the player is now playing a
            // stake they did not pay.
            warning = $"Failed to collect {owed} {session.Wallet} after {action}.";
        }

        session.Staked = view.TotalWagered;

        Settle(session, view, sessionId, output);
        await profiles.SaveAsync(sessionId);

        return Success(view, sessionId, session) with { Warning = warning };
    }

    public PlayerStats Stats(MongoId sessionId) => stats.Get(sessionId);

    /// <summary>Cheap health check. Touches no money and starts no round.</summary>
    public PingResponse Ping(MongoId sessionId)
    {
        var known = profiles.HasProfile(sessionId);

        return new PingResponse
        {
            Ok = true,
            ModVersion = new ModMetadata().Version.ToString(),
            SessionId = sessionId.ToString(),
            HasProfile = known,
            Balances = known
                ? Enum.GetValues<Wallet>().ToDictionary(w => w.ToString(), w => bank.GetBalance(sessionId, w))
                : [],

            // Not gated on the profile: the limits belong to the table, not to the
            // player, and a client that cannot read them has no way to keep a bet
            // legal before sending it.
            Limits = WalletInfo.All.ToDictionary(
                w => w.Wallet.ToString(),
                w => new BetLimits { Min = w.MinBet, Max = w.MaxBet }),
        };
    }

    /// <summary>Test-only convenience -- see <see cref="DealAsync(DealRequest, MongoId)"/>.</summary>
    public BlackjackResponse State(MongoId sessionId) => State(sessionId, new ItemEventRouterResponse());

    public BlackjackResponse State(MongoId sessionId, ItemEventRouterResponse output)
    {
        if (!profiles.HasProfile(sessionId))
        {
            return BlackjackResponse.Failed("No PMC profile for this session.");
        }

        // A refund moves real items, so this needs the caller's response, not a
        // throwaway -- an abandoned stake is returned through the same path a payout is.
        var refund = RefundAbandonedStake(sessionId, output);

        var session = tables.For(sessionId);
        return Success(session.Table.View(), sessionId, session) with { Note = refund };
    }

    /// <summary>
    /// Hands back a stake whose round no longer exists.
    ///
    /// The table lives in memory and the stake does not, so a restart between the deal
    /// and the settlement leaves money owed with no hand attached to it. Refunding
    /// lazily, on next contact, avoids having to touch profiles at boot before the
    /// server has finished loading them.
    /// </summary>
    private string? RefundAbandonedStake(MongoId sessionId, ItemEventRouterResponse output)
    {
        var owed = escrow.Get(sessionId);
        if (owed is null)
        {
            return null;
        }

        // A live round still owns its stake -- only an orphaned one is refundable.
        if (tables.Has(sessionId) && tables.For(sessionId).Table.Phase == RoundPhase.PlayerTurn)
        {
            return null;
        }

        if (!Enum.TryParse<Wallet>(owed.Wallet, ignoreCase: true, out var wallet))
        {
            escrow.Release(sessionId);
            return $"Discarded an unreadable outstanding stake of {owed.Amount} '{owed.Wallet}'.";
        }

        bank.Credit(sessionId, wallet, owed.Amount, output);
        escrow.Release(sessionId);

        return $"Refunded {owed.Amount:N0} {wallet} from a round that never finished.";
    }

    private void Settle(PlayerSession session, RoundView view, MongoId sessionId, ItemEventRouterResponse output)
    {
        if (view.Phase != RoundPhase.Settled)
        {
            return;
        }

        if (view.TotalReturned > 0)
        {
            bank.Credit(sessionId, session.Wallet, view.TotalReturned, output);
        }

        var record = stats.Get(sessionId);
        record.Record(view, session.Wallet, DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        stats.Save(sessionId, record);

        // The round is over, so nothing is owed back any more.
        escrow.Release(sessionId);
        session.Staked = 0;
    }

    private BlackjackResponse Success(RoundView view, MongoId sessionId, PlayerSession session) => new()
    {
        Ok = true,
        Round = view,
        Balance = bank.GetBalance(sessionId, session.Wallet),
        Wallet = session.Wallet.ToString(),
    };

    private BlackjackResponse Refused(
        string error,
        RoundView view,
        MongoId sessionId,
        PlayerSession session) => new()
    {
        Ok = false,
        Error = error,
        Round = view,
        Balance = bank.GetBalance(sessionId, session.Wallet),
        Wallet = session.Wallet.ToString(),
    };
}
