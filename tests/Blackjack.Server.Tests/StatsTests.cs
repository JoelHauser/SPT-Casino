using Blackjack.Game;
using SPTarkov.Server.Core.Models.Common;

namespace Blackjack.Server.Tests;

public class StatsTests
{
    private const int Wager = 10_000;

    private readonly MongoId _session = new();
    private readonly FakeBank _bank = new();
    private readonly FakeProfiles _profiles = new();
    private readonly FakeStats _stats = new();
    private readonly FakeEscrow _escrow = new();
    private readonly TableStore _tables = new();

    private BlackjackService WithDeal(string cards)
    {
        _tables.Seed(_session, new BlackjackTable(new Rules(), Shoe.Stacked(cards.Split(' ').Select(Card.Parse))));
        return new BlackjackService(_bank, _profiles, _tables, _stats, _escrow);
    }

    private static DealRequest Bet() => new() { Wager = Wager, Wallet = nameof(Wallet.Roubles) };

    private static ActionRequest Act(PlayerAction a) => new() { Action = a.ToString() };

    [Fact]
    public async Task AWinIsRecordedWithItsProfit()
    {
        // Player K/9 = 19 beats dealer K/7 = 17.
        var service = WithDeal("KS KH 9D 7C");
        await service.DealAsync(Bet(), _session);
        await service.ActAsync(Act(PlayerAction.Stand), _session);

        var s = service.Stats(_session);
        Assert.Equal(1, s.RoundsPlayed);
        Assert.Equal(1, s.HandsPlayed);
        Assert.Equal(1, s.Wins);
        Assert.Equal(0, s.Losses);
        Assert.Equal(1, s.CurrentStreak);

        var roubles = s.ByCurrency[nameof(Wallet.Roubles)];
        Assert.Equal(Wager, roubles.Wagered);
        Assert.Equal(20_000, roubles.Returned);
        Assert.Equal(Wager, roubles.Net);
        Assert.Equal(Wager, roubles.BestRound);
    }

    [Fact]
    public async Task ANaturalCountsAsBothABlackjackAndAWin()
    {
        var service = WithDeal("AS 9H KH 7D");
        await service.DealAsync(Bet(), _session);

        var s = service.Stats(_session);
        Assert.Equal(1, s.Blackjacks);
        Assert.Equal(1, s.Wins);
        Assert.Equal(15_000, s.ByCurrency[nameof(Wallet.Roubles)].BestRound);
    }

    [Fact]
    public async Task ABustCountsAsBothABustAndALoss()
    {
        var service = WithDeal("KS 6H 9D 5C 8H");
        await service.DealAsync(Bet(), _session);
        await service.ActAsync(Act(PlayerAction.Hit), _session);

        var s = service.Stats(_session);
        Assert.Equal(1, s.Busts);
        Assert.Equal(1, s.Losses);
        Assert.Equal(-1, s.CurrentStreak);
        Assert.Equal(-Wager, s.ByCurrency[nameof(Wallet.Roubles)].WorstRound);
    }

    [Fact]
    public async Task ASplitCountsOneRoundButTwoHands()
    {
        // 21 on one hand, 16 on the other, against a dealer 17: one wins, one loses,
        // so the round nets zero.
        var service = WithDeal("AS KH AD 7C KD 5H");
        await service.DealAsync(Bet(), _session);
        await service.ActAsync(Act(PlayerAction.Split), _session);

        var s = service.Stats(_session);
        Assert.Equal(1, s.RoundsPlayed);
        Assert.Equal(2, s.HandsPlayed);
        Assert.Equal(1, s.Wins);
        Assert.Equal(1, s.Losses);

        // The 21 came off a split, so it is a win and must not be tallied as a natural.
        Assert.Equal(0, s.Blackjacks);

        // A round that broke even breaks the streak rather than extending it.
        Assert.Equal(0, s.CurrentStreak);
    }

    [Fact]
    public async Task CurrenciesAreTalliedSeparately()
    {
        var service = WithDeal("KS KH 9D 7C");
        await service.DealAsync(new DealRequest { Wager = 1_000, Wallet = nameof(Wallet.Dollars) }, _session);
        await service.ActAsync(Act(PlayerAction.Stand), _session);

        var s = service.Stats(_session);
        Assert.True(s.ByCurrency.ContainsKey(nameof(Wallet.Dollars)));
        Assert.False(s.ByCurrency.ContainsKey(nameof(Wallet.Roubles)));
        Assert.Equal(1_000, s.ByCurrency[nameof(Wallet.Dollars)].Net);
    }

    [Fact]
    public void StreaksRunAndReset()
    {
        var stats = new PlayerStats();

        // Three wins, then a loss, then a push.
        Feed(stats, +10_000);
        Feed(stats, +10_000);
        Feed(stats, +10_000);
        Assert.Equal(3, stats.CurrentStreak);
        Assert.Equal(3, stats.BestStreak);

        Feed(stats, -10_000);
        Assert.Equal(-1, stats.CurrentStreak);

        Feed(stats, -10_000);
        Assert.Equal(-2, stats.CurrentStreak);

        // The best streak is a high-water mark and must survive the losses.
        Assert.Equal(3, stats.BestStreak);

        Feed(stats, 0);
        Assert.Equal(0, stats.CurrentStreak);
    }

    [Fact]
    public void AnUnsettledRoundCannotBeRecorded()
    {
        var table = new BlackjackTable(new Rules(), Shoe.Stacked("8S KH 8D 7C 3H 9D".Split(' ').Select(Card.Parse)));
        table.Deal(Wager);

        // Recording mid-round would count a hand whose outcome is still Pending.
        Assert.Throws<ArgumentException>(
            () => new PlayerStats().Record(table.View(), Wallet.Roubles, 0));
    }

    [Fact]
    public async Task StatsArePersistedOnEverySettledRound()
    {
        var service = WithDeal("KS KH 9D 7C");
        await service.DealAsync(Bet(), _session);
        Assert.Equal(0, _stats.Saves);

        await service.ActAsync(Act(PlayerAction.Stand), _session);
        Assert.Equal(1, _stats.Saves);
    }

    /// <summary>Drives a settled round with a known net through the recorder.</summary>
    private static void Feed(PlayerStats stats, int net)
    {
        var cards = net switch
        {
            > 0 => "KS KH 9D 7C",   // 19 beats 17
            < 0 => "KS KH 5D 7C",   // 15 loses to 17
            _ => "KS KH 9D 9C",     // 19 pushes 19
        };

        var table = new BlackjackTable(new Rules(), Shoe.Stacked(cards.Split(' ').Select(Card.Parse)));
        table.Deal(10_000);
        var view = table.Stand();

        Assert.Equal(net, view.Net);
        stats.Record(view, Wallet.Roubles, 0);
    }
}
