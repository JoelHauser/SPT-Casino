using Blackjack.Game;
using SPTarkov.Server.Core.Models.Common;

namespace Blackjack.Server.Tests;

public class WalletTests
{
    private readonly MongoId _session = new();
    private readonly FakeBank _bank = new();
    private readonly FakeProfiles _profiles = new();
    private readonly FakeStats _stats = new();
    private readonly FakeEscrow _escrow = new();
    private readonly TableStore _tables = new();

    private BlackjackService WithDeal(string cards)
    {
        _tables.Seed(_session, new BlackjackTable(
            new Rules { MinBet = 1, MaxBet = int.MaxValue },
            Shoe.Stacked(cards.Split(' ').Select(Card.Parse))));

        return new BlackjackService(_bank, _profiles, _tables, _stats, _escrow);
    }

    [Fact]
    public void EveryWalletHasLimitsAndATemplate()
    {
        foreach (var wallet in Enum.GetValues<Wallet>())
        {
            var info = WalletInfo.For(wallet);

            Assert.False(info.Tpl.IsEmpty, $"{wallet} has no template id.");
            Assert.True(info.MinBet > 0, $"{wallet} allows a zero bet.");
            Assert.True(info.MaxBet >= info.MinBet, $"{wallet} limits are inverted.");

            // A ceiling that is not a real number is not a ceiling, and the table
            // maximum only does its job while it is finite.
            Assert.NotEqual(int.MaxValue, info.MaxBet);
            Assert.False(string.IsNullOrWhiteSpace(info.Symbol));
        }
    }

    /// <summary>
    /// The ping carries the table's limits as well as the player's money, because the
    /// client has to be able to offer a legal bet rather than one the table is about
    /// to refuse -- an ALL IN of 200 GP coins at a table that takes 50 reads as a
    /// broken button, not as a rule.
    ///
    /// The limits are the house's, so they are reported whether or not the session
    /// resolved to a profile.
    /// </summary>
    [Fact]
    public void ThePingReportsWhatTheTableWillTake()
    {
        var service = WithDeal("KS KH 9D 7C");

        foreach (var known in new[] { true, false })
        {
            _profiles.Exists = known;

            var ping = service.Ping(_session);

            Assert.Equal(Enum.GetValues<Wallet>().Length, ping.Limits.Count);

            foreach (var wallet in Enum.GetValues<Wallet>())
            {
                var info = WalletInfo.For(wallet);
                var limits = ping.Limits[wallet.ToString()];

                Assert.Equal(info.MinBet, limits.Min);
                Assert.Equal(info.MaxBet, limits.Max);
            }
        }
    }

    [Fact]
    public void CurrencyAndValuablesAreSeparateSets()
    {
        var currency = WalletInfo.OfKind(WalletKind.Currency).Select(w => w.Wallet).ToList();
        var valuables = WalletInfo.OfKind(WalletKind.Valuable).Select(w => w.Wallet).ToList();

        Assert.Equal([Wallet.Roubles, Wallet.Dollars, Wallet.Euros], currency);
        Assert.Equal([Wallet.GpCoins, Wallet.Bitcoin, Wallet.LegaMedals], valuables);

        // Valuables are staked by the piece, so they all step by one.
        Assert.All(WalletInfo.OfKind(WalletKind.Valuable), w => Assert.Equal(1, w.Step));
        Assert.All(WalletInfo.OfKind(WalletKind.Valuable), w => Assert.Equal(1, w.MinBet));
    }

    [Fact]
    public void TemplateIdsAreDistinct()
    {
        // A copy-paste in the wallet table would silently make one currency pay out
        // in another, which the money tests could never catch.
        var tpls = WalletInfo.All.Select(w => w.Tpl.ToString()).ToList();

        Assert.Equal(tpls.Count, tpls.Distinct().Count());
    }

    /// <summary>
    /// The table maximum is what actually protects the house.
    ///
    /// A 0.45% edge over six decks is nothing across a session. What stops a player
    /// compounding is being unable to cover a losing streak by doubling up, and a
    /// ceiling of five hundred times the minimum caps that at nine doubles. Being
    /// able to afford the bet is not enough.
    /// </summary>
    [Theory]
    [InlineData(Wallet.Bitcoin, 11)]
    [InlineData(Wallet.LegaMedals, 6)]
    [InlineData(Wallet.GpCoins, 51)]
    [InlineData(Wallet.Dollars, 5001)]
    [InlineData(Wallet.Roubles, 5_000_000)]
    public async Task StakesAboveTheTableMaximumAreRefused(Wallet wallet, int wager)
    {
        var service = WithDeal("KS KH 9D 7C");
        _bank.SetBalance(wallet, 10_000_000);

        var response = await service.DealAsync(
            new DealRequest { Wager = wager, Wallet = wallet.ToString() },
            _session);

        Assert.False(response.Ok);
        Assert.Contains("takes up to", response.Error);
        Assert.Empty(_bank.Debits);
    }

    /// <summary>
    /// And it can be waived, because this is single player and the stash is the
    /// player's own. The request says so and the server takes it at its word.
    /// </summary>
    [Theory]
    [InlineData(Wallet.Bitcoin, 11)]
    [InlineData(Wallet.Roubles, 5_000_000)]
    public async Task TheMaximumCanBeWaived(Wallet wallet, int wager)
    {
        var service = WithDeal("KS KH 9D 7C");
        _bank.SetBalance(wallet, 10_000_000);

        var response = await service.DealAsync(
            new DealRequest { Wager = wager, Wallet = wallet.ToString(), IgnoreMaximum = true },
            _session);

        Assert.True(response.Ok, response.Error);
        Assert.Equal([(wallet, wager)], _bank.Debits);
    }

    /// <summary>
    /// Waiving the ceiling does not waive the floor. A bet of nothing is still not a
    /// bet, however the request is phrased.
    /// </summary>
    [Fact]
    public async Task WaivingTheMaximumStillRespectsTheMinimum()
    {
        var service = WithDeal("KS KH 9D 7C");
        _bank.SetBalance(Wallet.Roubles, 10_000_000);

        var response = await service.DealAsync(
            new DealRequest { Wager = 1, Wallet = nameof(Wallet.Roubles), IgnoreMaximum = true },
            _session);

        Assert.False(response.Ok);
        Assert.Empty(_bank.Debits);
    }

    /// <summary>
    /// The floor stays. A bet of nothing is not a bet, and the smallest meaningful
    /// stake differs per wallet -- one rouble is noise, one bitcoin is not.
    /// </summary>
    [Theory]
    [InlineData(Wallet.Roubles, 999)]
    [InlineData(Wallet.Dollars, 9)]
    [InlineData(Wallet.Bitcoin, 0)]
    public async Task StakesBelowTheFloorAreRefusedBeforeAnyDebit(Wallet wallet, int wager)
    {
        var service = WithDeal("KS KH 9D 7C");
        _bank.SetBalance(wallet, 1_000_000);

        var response = await service.DealAsync(
            new DealRequest { Wager = wager, Wallet = wallet.ToString() },
            _session);

        Assert.False(response.Ok);
        Assert.Empty(_bank.Debits);
    }

    [Fact]
    public async Task ASingleBitcoinCanBeStakedEvenThoughRoublesStartAtAThousand()
    {
        // The engine's own limits are deliberately wide so the per-wallet ones govern.
        // A rouble minimum of 1,000 would otherwise make every bitcoin bet illegal.
        var service = WithDeal("KS KH 9D 7C");
        _bank.SetBalance(Wallet.Bitcoin, 3);

        var response = await service.DealAsync(
            new DealRequest { Wager = 1, Wallet = nameof(Wallet.Bitcoin) },
            _session);

        Assert.True(response.Ok, response.Error);
        Assert.Equal([(Wallet.Bitcoin, 1)], _bank.Debits);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(7)]
    public async Task AValuableNaturalPaysEvenMoneySoItAlwaysDivides(int wager)
    {
        // 3:2 would settle an odd stake on half a coin, which cannot exist. Valuables
        // are dealt at even money instead, so any whole stake returns a whole payout.
        var service = WithDeal("AS 9H KH 7D");
        _bank.SetBalance(Wallet.GpCoins, 50);

        var response = await service.DealAsync(
            new DealRequest { Wager = wager, Wallet = nameof(Wallet.GpCoins) },
            _session);

        Assert.True(response.Ok, response.Error);
        Assert.Equal([(Wallet.GpCoins, wager * 2)], _bank.Credits);
    }

    [Fact]
    public async Task ACurrencyNaturalStillPaysThreeToTwo()
    {
        var service = WithDeal("AS 9H KH 7D");

        await service.DealAsync(
            new DealRequest { Wager = 10_000, Wallet = nameof(Wallet.Roubles) },
            _session);

        Assert.Equal([(Wallet.Roubles, 25_000)], _bank.Credits);
    }

    [Fact]
    public void TheNaturalRateFollowsWhatIsBeingStaked()
    {
        Assert.All(WalletInfo.OfKind(WalletKind.Currency), w => Assert.Equal(1.5, w.BlackjackPayout));
        Assert.All(WalletInfo.OfKind(WalletKind.Valuable), w => Assert.Equal(1.0, w.BlackjackPayout));
    }

    [Fact]
    public async Task ASingleBitcoinNaturalReturnsTwoNotTwoAndAHalf()
    {
        var service = WithDeal("AS 9H KH 7D");
        _bank.SetBalance(Wallet.Bitcoin, 5);

        await service.DealAsync(new DealRequest { Wager = 1, Wallet = nameof(Wallet.Bitcoin) }, _session);

        Assert.Equal([(Wallet.Bitcoin, 2)], _bank.Credits);
    }
}
