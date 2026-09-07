using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.ItemEvent;

namespace SlotMachine.Server.Tests;

/// <summary>
/// Pins <see cref="PlayerStats.Record"/> directly, the same way Blackjack.Server.Tests'
/// StatsTests pins its own accounting -- pure and self-contained, so none of this needs
/// a seeded machine or a specific pull outcome to land on.
/// </summary>
public class StatsTests
{
    private static readonly MongoId Session = new();

    [Fact]
    public void AWinIsRecordedWithItsProfit()
    {
        var stats = new PlayerStats();
        stats.Record(10_000, 25_000, Wallet.Roubles, 0);

        Assert.Equal(1, stats.PullsPlayed);
        Assert.Equal(1, stats.Wins);
        Assert.Equal(0, stats.Losses);
        Assert.Equal(1, stats.CurrentStreak);
        Assert.Equal(2.5, stats.BestMultiple);

        var roubles = stats.ByCurrency[nameof(Wallet.Roubles)];
        Assert.Equal(10_000, roubles.Wagered);
        Assert.Equal(25_000, roubles.Returned);
        Assert.Equal(15_000, roubles.Net);
        Assert.Equal(15_000, roubles.BestPull);
    }

    [Fact]
    public void APullThatPaysNothingIsALoss()
    {
        var stats = new PlayerStats();
        stats.Record(10_000, 0, Wallet.Roubles, 0);

        Assert.Equal(0, stats.Wins);
        Assert.Equal(1, stats.Losses);
        Assert.Equal(-1, stats.CurrentStreak);
        Assert.Equal(-10_000, stats.ByCurrency[nameof(Wallet.Roubles)].WorstPull);
    }

    [Fact]
    public void AHundredTimesTheStakeIsAJackpot()
    {
        var stats = new PlayerStats();
        stats.Record(5_000, 500_000, Wallet.Roubles, 0);

        Assert.Equal(1, stats.Jackpots);
        Assert.Equal(100d, stats.BestMultiple);
    }

    [Fact]
    public void NinetyNineTimesIsNotYetAJackpot()
    {
        var stats = new PlayerStats();
        stats.Record(5_000, 495_000, Wallet.Roubles, 0);

        Assert.Equal(0, stats.Jackpots);
    }

    [Fact]
    public void StreaksRunAndReset()
    {
        var stats = new PlayerStats();

        Feed(stats, 10_000, 20_000);
        Feed(stats, 10_000, 15_000);
        Feed(stats, 10_000, 12_000);
        Assert.Equal(3, stats.CurrentStreak);
        Assert.Equal(3, stats.BestStreak);

        Feed(stats, 10_000, 0);
        Assert.Equal(-1, stats.CurrentStreak);

        Feed(stats, 10_000, 0);
        Assert.Equal(-2, stats.CurrentStreak);

        // The best streak is a high-water mark and must survive the losses.
        Assert.Equal(3, stats.BestStreak);

        // Paid back exactly the stake: not a loss, but not a streak-worthy win
        // either, the same way Blackjack resets on a push.
        Feed(stats, 10_000, 10_000);
        Assert.Equal(0, stats.CurrentStreak);
    }

    [Fact]
    public void APullMustHaveStakedSomething()
    {
        Assert.Throws<ArgumentException>(() => new PlayerStats().Record(0, 0, Wallet.Roubles, 0));
    }

    [Fact]
    public void CurrenciesAreTalliedSeparately()
    {
        var stats = new PlayerStats();
        stats.Record(1_000, 2_000, Wallet.Dollars, 0);

        Assert.True(stats.ByCurrency.ContainsKey(nameof(Wallet.Dollars)));
        Assert.False(stats.ByCurrency.ContainsKey(nameof(Wallet.Roubles)));
        Assert.Equal(1_000, stats.ByCurrency[nameof(Wallet.Dollars)].Net);
    }

    [Fact]
    public async Task StatsArePersistedOnEveryPull()
    {
        var bank = new FakeBank();
        bank.Seed(Wallet.Roubles, 500_000_000);
        var stats = new FakeStats();

        var service = new SlotService(
            bank, new FakeProfiles(), new FakeEscrow(), new FakeRandom(20260906), stats, new QuietLog());

        Assert.Equal(0, stats.Saves);

        await service.PullAsync(
            new PullRequest { Wallet = nameof(Wallet.Roubles), Stake = 10_000 },
            Session,
            new ItemEventRouterResponse());

        Assert.Equal(1, stats.Saves);
    }

    /// <summary>Drives one pull of a known stake and payout through the recorder.</summary>
    private static void Feed(PlayerStats stats, long stake, long paid) =>
        stats.Record(stake, paid, Wallet.Roubles, 0);
}
