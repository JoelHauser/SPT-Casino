namespace Poker.Game.Tests;

/// <summary>
/// The log is a test surface, not decoration. These pin what the engine records
/// about its own decisions -- see <see cref="IGameLog"/> for why that is worth
/// pinning separately from the return value.
/// </summary>
public class GameLogTests
{
    [Fact]
    public void TheDefaultSinkReportsItselfDisabledSoCallSitesCanSkipTheWork()
    {
        // The whole cost argument rests on this. If Null ever reports enabled, every
        // guarded call site starts formatting strings it will throw away -- which is
        // minutes added to the 2.6 million hands in HandDistributionTests.
        Assert.False(GameLog.Null.Enabled);
    }

    [Fact]
    public void AnEngineWithNoSinkAttachedStillRuns()
    {
        // The log is optional everywhere. Nothing may require one to work.
        var deck = new Deck(new Random(1));
        deck.Draw(5);

        var layout = PotBuilder.Build([new Contribution(0, 100, false), new Contribution(1, 100, false)]);

        Assert.Equal(200, layout.Total);
    }

    [Fact]
    public void ADeckSaysWhatItDealtAndWhatWasLeft()
    {
        var log = new ListGameLog();
        var deck = Deck.Stacked("AS KH", log);

        deck.Draw();

        Assert.True(log.Mentions("dealt AS"), log.ToString());
        Assert.True(log.Mentions("1 left"), log.ToString());
    }

    [Fact]
    public void AStackedDeckSaysSoRatherThanLookingLikeAShuffledOne()
    {
        // A stacked deck in a real game is the one deck fault that cannot be seen in
        // the cards themselves, so it has to announce itself.
        var log = new ListGameLog();

        Deck.Stacked("AS KH QD", log).Shuffle();

        Assert.True(log.Mentions("stacked"), log.ToString());
    }

    [Fact]
    public void ARefundSaysWhichSeatGotItBackAndWhy()
    {
        var log = new ListGameLog();

        // Seat 1 bets 200 into a seat all-in for 50; 150 was never matched.
        PotBuilder.Build([new Contribution(0, 50, false), new Contribution(1, 200, false)], log);

        Assert.True(log.Mentions("refunding 150 to seat 1"), log.ToString());
    }

    [Fact]
    public void ALayerNobodyCanWinSaysWhereItsChipsWent()
    {
        // The failure this guards against is silent: chips from a layer whose seats
        // all folded are real money, and a layout that drops them still balances if
        // the drop happens before the total is taken.
        var log = new ListGameLog();

        PotBuilder.Build(
            [
                new Contribution(0, 50, false),
                new Contribution(1, 200, true),
                new Contribution(2, 200, true),
            ],
            log);

        Assert.True(log.Mentions("no seat left to win it"), log.ToString());
        Assert.True(log.Mentions("settled into 1 pot"), log.ToString());
    }

    [Fact]
    public void EachSidePotIsRecordedWithTheSeatsThatCanWinIt()
    {
        var log = new ListGameLog();

        PotBuilder.Build(
            [
                new Contribution(0, 50, false),
                new Contribution(1, 200, false),
                new Contribution(2, 200, false),
            ],
            log);

        Assert.True(log.Mentions("winnable by [0, 1, 2]"), log.ToString());
        Assert.True(log.Mentions("winnable by [1, 2]"), log.ToString());
    }

    [Fact]
    public void TheEvaluatorRecordsTheFiveCardsItChoseOutOfSeven()
    {
        // Which five were picked is the part a person checks at a showdown. The 20
        // losing combinations are deliberately not logged.
        var log = new ListGameLog();

        HandEvaluator.Evaluate("9S 8S 7S 6S 5S 2C 3D", log);

        Assert.True(log.Mentions("9S 8S 7S 6S 5S"), log.ToString());
        Assert.True(log.Mentions("Straight flush"), log.ToString());
    }
}
