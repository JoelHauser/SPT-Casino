namespace Poker.Game.Tests;

public class PaytableTests
{
    private static HandRank Hand(string codes) => HandEvaluator.Evaluate(codes);

    /// <summary>One hand of each category, weakest first. The royal sits last.</summary>
    private static readonly string[] Ascending =
    [
        "AS KD 9C 5H 2S",   // high card
        "AS AD 9C 5H 2S",   // pair
        "AS AD 9C 9H 2S",   // two pair
        "AS AD AC 9H 2S",   // trips
        "9S 8D 7C 6H 5S",   // straight
        "AS QS 9S 5S 2S",   // flush
        "AS AD AC 9H 9S",   // full house
        "AS AD AC AH 9S",   // quads
        "9S 8S 7S 6S 5S",   // straight flush
        "AS KS QS JS TS",   // royal
    ];

    [Fact]
    public void ARoyalPaysFiveHundredToOneOnTheBlind()
    {
        Assert.Equal(Payout.Odds(500), Paytable.Blind.For(Hand("AS KS QS JS TS")));
    }

    [Fact]
    public void AStraightFlushThatIsNotARoyalPaysFiftyToOne()
    {
        // The row above it claims only ace-high straight flushes. If RoyalOnly were
        // ignored this would pay 500:1, which is a ten-fold overpayment on the
        // second most common way the top of the table is reached.
        Assert.Equal(Payout.Odds(50), Paytable.Blind.For(Hand("9S 8S 7S 6S 5S")));
    }

    [Fact]
    public void TheBlindPushesBeneathAStraightRatherThanLosing()
    {
        var payout = Paytable.Blind.For(Hand("AS AD AC 9H 2S"));

        Assert.True(payout.IsPush);
        Assert.Equal(1_000, payout.Returned(1_000));
    }

    [Fact]
    public void TripsLosesBeneathThreeOfAKindRatherThanPushing()
    {
        // The difference between the two tables' floors is real money. Trips is a
        // bet on your own hand that simply misses; the Blind is not.
        var payout = Paytable.Trips.For(Hand("AS AD 9C 9H 2S"));

        Assert.True(payout.IsLoss);
        Assert.Equal(0, payout.Returned(1_000));
    }

    [Fact]
    public void TripsPaysOnAHandTheDealerWouldHaveBeaten()
    {
        // Nothing about the dealer reaches this table. Worth pinning, because the
        // obvious refactor is to settle all three bets through one comparison.
        Assert.Equal(Payout.Odds(3), Paytable.Trips.For(Hand("2S 2D 2C 7H 5S")));
    }

    [Fact]
    public void AFlushPaysThreeToTwoAndRoundsHalfUp()
    {
        var payout = Paytable.Blind.For(Hand("AS QS 9S 5S 2S"));

        // 25 at 3:2 is 37.5. Half goes up, as Blackjack settled its naturals.
        Assert.Equal(38, payout.Profit(25));
        Assert.Equal(63, payout.Returned(25));
    }

    [Fact]
    public void TheCappedTableStopsAtThreeToOneAboveAFlush()
    {
        var capped = Paytable.BlindForValuables;

        Assert.Equal(Payout.Odds(3), capped.For(Hand("AS KS QS JS TS")));
        Assert.Equal(Payout.Odds(3), capped.For(Hand("AS AD AC AH 9S")));
        Assert.Equal(Payout.Odds(2), capped.For(Hand("AS QS 9S 5S 2S")));
        Assert.Equal(Payout.Odds(1), capped.For(Hand("9S 8D 7C 6H 5S")));
    }

    [Fact]
    public void EveryRowOfTheCappedTableSettlesAWholeUnit()
    {
        // The point of the capped table, and not merely that it is smaller. A
        // single bitcoin cannot be paid 3:2 -- half a coin does not exist.
        Assert.All(
            Paytable.BlindForValuables.Rows,
            row => Assert.True(row.Payout.DividesExactly(1), row.Payout.ToString()));

        Assert.False(Paytable.Blind.For(Hand("AS QS 9S 5S 2S")).DividesExactly(1));
    }

    [Theory]
    [InlineData("Blind")]
    [InlineData("Blind (valuables)")]
    [InlineData("Trips")]
    public void NoTableEverPaysLessForABetterHand(string name)
    {
        // A mistyped row is invisible in every single-hand test around it and shows
        // up only as one hand paying worse than a hand it beats.
        var table = new[] { Paytable.Blind, Paytable.BlindForValuables, Paytable.Trips }
            .Single(t => t.Name == name);

        var previous = table.For(Hand(Ascending[0]));

        foreach (var codes in Ascending.Skip(1))
        {
            var payout = table.For(Hand(codes));
            Assert.True(
                payout.CompareTo(previous) >= 0,
                $"{codes} pays {payout}, worse than the weaker hand before it at {previous}.");

            previous = payout;
        }
    }

    [Fact]
    public void TheStandardTableRisksFiveHundredAndElevenAntesAndTheCappedOneFourteen()
    {
        // The two numbers the wallet ceilings are set from.
        Assert.Equal(511, new Rules().WorstCaseReturnPerAnte);
        Assert.Equal(14, new Rules { Blind = Paytable.BlindForValuables }.WorstCaseReturnPerAnte);
    }

    [Fact]
    public void APayoutTooLargeForAnIntThrowsRatherThanWrapping()
    {
        // Wrapping would pay a negative amount, which reads to a player as the
        // table confiscating a royal. Better to fail loudly at the ceiling.
        Assert.Throws<OverflowException>(() => Payout.Odds(500).Profit(int.MaxValue / 100));
    }

    [Fact]
    public void AnUnsetPayoutIsAPushRatherThanADivideByZero()
    {
        var unset = default(Payout);

        Assert.Equal(Payout.Push, unset);
        Assert.Equal(1, unset.Denominator);
        Assert.Equal(500, unset.Returned(500));
    }

    [Fact]
    public void NegativeOddsAreRefusedSoALossHasOnlyOneSpelling()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Payout.Odds(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => Payout.Odds(3, 0));
    }

    [Fact]
    public void ThePaytableRecordsWhichRowItMatched()
    {
        var log = new ListGameLog();

        Paytable.Blind.For(Hand("AS KS QS JS TS"), log);
        Paytable.Trips.For(Hand("AS KD 9C 5H 2S"), log);

        Assert.True(log.Mentions("Royal flush pays 500:1"), log.ToString());
        Assert.True(log.Mentions("beneath the table and loses"), log.ToString());
    }
}
