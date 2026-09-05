using Blackjack.Game;
using SPTarkov.Server.Core.Models.Common;

namespace Blackjack.Server.Tests;

/// <summary>
/// The table is in memory and the stake is not. These cover the gap: money that has
/// left the profile with no round left to win it back.
/// </summary>
public class EscrowTests
{
    private const int Wager = 10_000;

    private readonly MongoId _session = new();
    private readonly FakeBank _bank = new();
    private readonly FakeProfiles _profiles = new();
    private readonly FakeStats _stats = new();
    private readonly FakeEscrow _escrow = new();
    private readonly TableStore _tables = new();

    private BlackjackService Service() => new(_bank, _profiles, _tables, _stats, _escrow);

    private BlackjackService WithDeal(string cards)
    {
        _tables.Seed(_session, new BlackjackTable(
            new Rules { MinBet = 1, MaxBet = int.MaxValue },
            Shoe.Stacked(cards.Split(' ').Select(Card.Parse))));

        return Service();
    }

    private static DealRequest Bet() => new() { Wager = Wager, Wallet = nameof(Wallet.Roubles) };

    [Fact]
    public async Task AStakeIsHeldFromTheDealUntilSettlement()
    {
        // Player K/5 = 15 against dealer K/7 = 17, so the round needs an action.
        var service = WithDeal("KS KH 5D 7C");
        await service.DealAsync(Bet(), _session);

        var held = _escrow.Get(_session);
        Assert.NotNull(held);
        Assert.Equal(Wager, held.Amount);
        Assert.Equal(nameof(Wallet.Roubles), held.Wallet);

        await service.ActAsync(new ActionRequest { Action = nameof(PlayerAction.Stand) }, _session);

        // Settled, so nothing is owed back any more.
        Assert.Null(_escrow.Get(_session));
    }

    [Fact]
    public async Task DoublingAddsToTheHeldStakeRatherThanReplacingIt()
    {
        var service = WithDeal("5S KH 6D 7C 9H");
        await service.DealAsync(Bet(), _session);
        Assert.Equal(Wager, _escrow.Get(_session)!.Amount);

        // Mid-action the player is exposed for two bets, not one. Replacing rather
        // than accumulating would under-refund them by half.
        var table = _tables.For(_session).Table;
        table.Double();
        _escrow.Hold(_session, Wallet.Roubles, Wager);

        Assert.Equal(20_000, _escrow.Get(_session)!.Amount);
    }

    [Fact]
    public async Task AStakeStrandedByARestartIsRefundedOnNextContact()
    {
        var service = WithDeal("KS KH 5D 7C");
        await service.DealAsync(Bet(), _session);
        Assert.Single(_bank.Debits);

        // The server restarts: tables are in memory and vanish, escrow is on disk and
        // does not. That is precisely the state that loses a player's money.
        _tables.Clear(_session);

        var response = Service().State(_session);

        Assert.True(response.Ok);
        Assert.Equal([(Wallet.Roubles, Wager)], _bank.Credits);
        Assert.Null(_escrow.Get(_session));
        Assert.Equal(1_000_000, _bank.GetBalance(_session, Wallet.Roubles));

        // The refund has to explain itself, or the log shows a credit with no cause
        // and it reads as a payout bug.
        Assert.NotNull(response.Note);
        Assert.Contains("never finished", response.Note);
    }

    [Fact]
    public async Task ALiveRoundIsNotRefundedUnderneathThePlayer()
    {
        var service = WithDeal("KS KH 5D 7C");
        await service.DealAsync(Bet(), _session);

        // The round is still in progress, so the stake is not abandoned -- refunding
        // here would hand back money the player is still playing for.
        var response = service.State(_session);

        Assert.Equal(RoundPhase.PlayerTurn, response.Round!.Phase);
        Assert.Empty(_bank.Credits);
        Assert.NotNull(_escrow.Get(_session));
    }

    [Fact]
    public async Task ARefundHappensBeforeTheNextDealTakesAFreshStake()
    {
        var service = WithDeal("KS KH 5D 7C");
        await service.DealAsync(Bet(), _session);
        _tables.Clear(_session);

        // Deal again after the "restart". The stranded stake must come back first,
        // otherwise the player quietly funds two rounds and plays one.
        _tables.Seed(_session, new BlackjackTable(
            new Rules { MinBet = 1, MaxBet = int.MaxValue },
            Shoe.Stacked("KS KH 9D 7C".Split(' ').Select(Card.Parse))));

        await Service().DealAsync(Bet(), _session);

        Assert.Equal([(Wallet.Roubles, Wager)], _bank.Credits);
        Assert.Equal(2, _bank.Debits.Count);

        // Net effect of the abandoned round: nothing. One refund, one fresh stake.
        Assert.Equal(1_000_000 - Wager, _bank.GetBalance(_session, Wallet.Roubles));
    }

    [Fact]
    public async Task AStrandedValuableComesBackInItsOwnCurrency()
    {
        var service = WithDeal("KS KH 5D 7C");
        _bank.SetBalance(Wallet.Bitcoin, 4);

        await service.DealAsync(new DealRequest { Wager = 2, Wallet = nameof(Wallet.Bitcoin) }, _session);
        _tables.Clear(_session);

        Service().State(_session);

        Assert.Equal([(Wallet.Bitcoin, 2)], _bank.Credits);
        Assert.Equal(4, _bank.GetBalance(_session, Wallet.Bitcoin));
    }

    [Fact]
    public void NothingHappensWhenNothingIsOwed()
    {
        var service = WithDeal("KS KH 5D 7C");

        service.State(_session);

        Assert.Empty(_bank.Credits);
        Assert.Empty(_bank.Debits);
    }
}
