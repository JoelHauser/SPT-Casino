using Blackjack.Game;
using SPTarkov.Server.Core.Models.Common;

namespace Blackjack.Server.Tests;

/// <summary>
/// Drives the real <see cref="BlackjackService"/> against fake money. This is what
/// stands in for a live server: every path that moves currency is exercised here,
/// so the only thing left unverified against real SPT is <see cref="Bank"/>'s own
/// InventoryHelper calls.
/// </summary>
public class MoneyFlowTests
{
    private const int Wager = 10_000;

    private readonly MongoId _session = new();
    private readonly FakeBank _bank = new();
    private readonly FakeProfiles _profiles = new();
    private readonly TableStore _tables = new();
    private readonly FakeStats _stats = new();
    private readonly FakeEscrow _escrow = new();

    private BlackjackService Service() => new(_bank, _profiles, _tables, _stats, _escrow);

    /// <summary>Installs a table dealing a known stack, then returns the service.</summary>
    private BlackjackService WithDeal(string cards, Rules? rules = null)
    {
        _tables.Seed(_session, new BlackjackTable(rules ?? new Rules(), Shoe.Stacked(cards.Split(' ').Select(Card.Parse))));
        return Service();
    }

    private static DealRequest Bet(int wager = Wager, Wallet wallet = Wallet.Roubles) =>
        new() { Wager = wager, Wallet = wallet.ToString() };

    private static ActionRequest Act(PlayerAction action) => new() { Action = action.ToString() };

    [Fact]
    public async Task DealTakesExactlyTheWager()
    {
        // Player K/9 = 19 against dealer K/7 = 17.
        var service = WithDeal("KS KH 9D 7C");
        var response = await service.DealAsync(Bet(), _session);

        Assert.True(response.Ok);
        Assert.Equal([(Wallet.Roubles, Wager)], _bank.Debits);
    }

    [Fact]
    public async Task ACurrencyNaturalIsPaidThreeToTwo()
    {
        var service = WithDeal("AS 9H KH 7D");
        var response = await service.DealAsync(Bet(), _session);

        Assert.Equal([(Wallet.Roubles, Wager)], _bank.Debits);
        Assert.Equal([(Wallet.Roubles, 25_000)], _bank.Credits);
        Assert.Equal(1_015_000, response.Balance);
    }

    [Fact]
    public async Task ALossCreditsNothing()
    {
        // Player K/5 = 15, dealer K/7 = 17.
        var service = WithDeal("KS KH 5D 7C");
        await service.DealAsync(Bet(), _session);
        var response = await service.ActAsync(Act(PlayerAction.Stand), _session);

        Assert.Empty(_bank.Credits);
        Assert.Equal(990_000, response.Balance);
    }

    [Fact]
    public async Task APushReturnsTheStakeExactly()
    {
        var service = WithDeal("KS KH 9D 9C");
        await service.DealAsync(Bet(), _session);
        var response = await service.ActAsync(Act(PlayerAction.Stand), _session);

        Assert.Equal([(Wallet.Roubles, Wager)], _bank.Credits);
        Assert.Equal(1_000_000, response.Balance);
    }

    [Fact]
    public async Task DoubleCollectsExactlyOneAdditionalBet()
    {
        // Player 5/6 = 11, doubles into a nine for 20 against the dealer's 17.
        var service = WithDeal("5S KH 6D 7C 9H");
        await service.DealAsync(Bet(), _session);
        var response = await service.ActAsync(Act(PlayerAction.Double), _session);

        // Two separate debits of one bet each, never one debit of two.
        Assert.Equal([(Wallet.Roubles, Wager), (Wallet.Roubles, Wager)], _bank.Debits);
        Assert.Equal([(Wallet.Roubles, 40_000)], _bank.Credits);
        Assert.Equal(1_020_000, response.Balance);
    }

    [Fact]
    public async Task SplitCollectsExactlyOneAdditionalBet()
    {
        var service = WithDeal("8S KH 8D 7C 3H 9D");
        await service.DealAsync(Bet(), _session);
        await service.ActAsync(Act(PlayerAction.Split), _session);

        Assert.Equal([(Wallet.Roubles, Wager), (Wallet.Roubles, Wager)], _bank.Debits);
    }

    [Fact]
    public async Task DoubleIsRefusedWhenItCannotBeCoveredAndTheHandIsUntouched()
    {
        var service = WithDeal("5S KH 6D 7C 9H");
        _bank.SetBalance(Wallet.Roubles, Wager);

        var dealt = await service.DealAsync(Bet(), _session);
        Assert.True(dealt.Ok);

        // The wager consumed the entire balance, so there is nothing left to double.
        var response = await service.ActAsync(Act(PlayerAction.Double), _session);

        Assert.False(response.Ok);
        Assert.Contains("Not enough", response.Error);

        // Critically, the round must be exactly as it was -- still the player's turn,
        // still two cards, still one bet staked.
        Assert.Equal(RoundPhase.PlayerTurn, response.Round!.Phase);
        Assert.Equal(2, response.Round.PlayerHands[0].Cards.Count);
        Assert.Equal(Wager, response.Round.TotalWagered);
        Assert.Single(_bank.Debits);
    }

    [Fact]
    public async Task InsufficientFundsRefusesWithoutDebiting()
    {
        var service = WithDeal("KS KH 9D 7C");
        _bank.SetBalance(Wallet.Roubles, 500);

        var response = await service.DealAsync(Bet(), _session);

        Assert.False(response.Ok);
        Assert.Empty(_bank.Debits);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(999)]
    public async Task WagersBelowTheFloorRefuseBeforeAnyDebit(int wager)
    {
        var service = WithDeal("KS KH 9D 7C");
        var response = await service.DealAsync(Bet(wager), _session);

        Assert.False(response.Ok);

        // The ordering that matters: validation happens before the money moves, so a
        // rejected wager cannot leave the player short with no hand to play.
        Assert.Empty(_bank.Debits);
    }

    [Fact]
    public async Task DollarsAndEurosSettleInTheirOwnCurrency()
    {
        var service = WithDeal("AS 9H KH 7D");
        var response = await service.DealAsync(Bet(1_000, Wallet.Dollars), _session);

        Assert.True(response.Ok);
        Assert.Equal(nameof(Wallet.Dollars), response.Wallet);
        Assert.Equal([(Wallet.Dollars, 1_000)], _bank.Debits);
        Assert.Equal([(Wallet.Dollars, 2_500)], _bank.Credits);

        // Roubles must be untouched.
        Assert.Equal(1_000_000, _bank.GetBalance(_session, Wallet.Roubles));
    }

    [Fact]
    public async Task AnUncollectableStakeIsReportedButTheRoundStillResolves()
    {
        var service = WithDeal("5S KH 6D 7C 9H");
        await service.DealAsync(Bet(), _session);

        // Balance says the double is affordable, but the debit itself fails --
        // the profile changed underneath us between check and collect.
        _bank.RefuseDebits = true;
        var response = await service.ActAsync(Act(PlayerAction.Double), _session);

        Assert.True(response.Ok);
        Assert.NotNull(response.Warning);
        Assert.Contains("Failed to collect", response.Warning);
    }

    [Fact]
    public async Task MalformedRequestsAreRefused()
    {
        var service = WithDeal("KS KH 9D 7C");

        Assert.False((await service.DealAsync(new DealRequest { Wallet = "Bitcoin", Wager = Wager }, _session)).Ok);
        Assert.False((await service.ActAsync(new ActionRequest { Action = "Fold" }, _session)).Ok);

        // No round in progress yet.
        Assert.False((await service.ActAsync(Act(PlayerAction.Hit), _session)).Ok);

        await service.DealAsync(Bet(), _session);
        Assert.False((await service.DealAsync(Bet(), _session)).Ok);

        Assert.Empty(_bank.Credits);
    }

    [Fact]
    public async Task AMissingProfileRefusesEverything()
    {
        var service = WithDeal("KS KH 9D 7C");
        _profiles.Exists = false;

        Assert.False((await service.DealAsync(Bet(), _session)).Ok);
        Assert.False((await service.ActAsync(Act(PlayerAction.Hit), _session)).Ok);
        Assert.False(service.State(_session).Ok);
        Assert.Empty(_bank.Debits);
    }

    [Fact]
    public async Task EveryMoneyMovingRequestIsPersisted()
    {
        var service = WithDeal("KS KH 5D 7C");
        await service.DealAsync(Bet(), _session);
        await service.ActAsync(Act(PlayerAction.Stand), _session);

        // Money that is not saved did not move -- a crash before the flush would
        // hand the player their stake back for free.
        Assert.Equal(2, _profiles.Saves);
    }
}
