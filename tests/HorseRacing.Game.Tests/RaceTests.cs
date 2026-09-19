namespace HorseRacing.Game.Tests;

/// <summary>
/// Running a race, and what the slip was worth when it finished.
///
/// These are the engine's money invariants. They are written here, against chips,
/// before anything knows what a rouble is -- the settlement that moves real money sits
/// on top of this and gets its own set.
/// </summary>
public class RaceTests
{
    /// <summary>
    /// These are about the settlement rather than about pricing, so they all run at one
    /// course. Which one does not matter and that is the point: the money rules are the
    /// same wherever the race is run. <c>OddsTests</c> and <c>TrackTests</c> are where
    /// the three boards are compared against each other.
    /// </summary>
    private static readonly Track Track = Tracks.Mile;

    /// <summary>
    /// A race finishes with every runner somewhere and none of them twice.
    ///
    /// The failure this catches is the one that would make the whole game quietly
    /// wrong: sampling *with* replacement instead of without. Every price on the board
    /// is computed from a model where a runner that has won cannot also come second,
    /// and a draw that let it would be a game paying exacta prices on quinella odds.
    /// </summary>
    [Fact]
    public void EveryRaceFinishesWithTheWholeFieldInSomeOrder()
    {
        var random = new Random(20260919);

        for (var i = 0; i < 20_000; i++)
        {
            var order = Race.Draw(Track, random);

            Assert.Equal(Field.Count, order.Length);
            Assert.Equal(Field.Count, order.Distinct().Count());

            foreach (var number in order)
            {
                Assert.True(Field.Has(number), $"runner {number} is not on the card.");
            }
        }
    }

    /// <summary>
    /// Given enough races, every runner wins at least once and finishes last at least
    /// once. A runner that can never win is a spot on the board taking money against
    /// nothing, and the shortest price would hide it from a chance-based test.
    /// </summary>
    [Fact]
    public void EveryRunnerCanWinAndEveryRunnerCanLose()
    {
        var random = new Random(20260919);
        var won = new HashSet<int>();
        var last = new HashSet<int>();

        for (var i = 0; i < 100_000; i++)
        {
            var order = Race.Draw(Track, random);
            won.Add(order[0]);
            last.Add(order[^1]);
        }

        Assert.Equal(Field.Count, won.Count);
        Assert.Equal(Field.Count, last.Count);
    }

    /// <summary>
    /// A losing bet returns nothing at all -- not its stake.
    ///
    /// Stated on its own because it is the single most expensive thing to get wrong
    /// here. The stake has already left the player's stash by the time a race runs, so
    /// a loser returning its own stake is not a rounding error, it is a game with no
    /// house edge whatsoever.
    /// </summary>
    [Fact]
    public void ALosingBetReturnsNothing()
    {
        int[] order = [3, 5, 1, 8, 2, 7, 4, 6];

        var slip = new[]
        {
            Bet.On(BetKind.Win, 6, 100_000),
            Bet.On(BetKind.Place, 7, 100_000),
            Bet.On(BetKind.Show, 4, 100_000),
            new Bet(BetKind.Exacta, 5, 3, 100_000),
            new Bet(BetKind.Quinella, 2, 7, 100_000),
        };

        var result = Race.Settle(Track, slip, order);

        Assert.Equal(500_000, result.Staked);
        Assert.Equal(0, result.Returned);
        Assert.All(result.Settlements, s => Assert.False(s.Won));
        Assert.All(result.Settlements, s => Assert.Equal(0, s.Returned));
    }

    /// <summary>
    /// Backing one runner to win, to place and to show collects all three when it
    /// wins.
    ///
    /// This is the invariant that forbids the obvious and wrong optimisation -- stop
    /// at the first winning bet, since surely only one can win. A punter who covered
    /// their horse three ways and was paid once would be robbed by a shortcut that
    /// looks like tidiness.
    /// </summary>
    [Fact]
    public void BetsAreSettledIndependentlyAndOneRunnerCanPayThreeTimes()
    {
        int[] order = [3, 5, 1, 8, 2, 7, 4, 6];

        var slip = new[]
        {
            Bet.On(BetKind.Win, 3, 100_000),
            Bet.On(BetKind.Place, 3, 100_000),
            Bet.On(BetKind.Show, 3, 100_000),
        };

        var result = Race.Settle(Track, slip, order);

        Assert.All(result.Settlements, s => Assert.True(s.Won));
        Assert.All(result.Settlements, s => Assert.True(s.Returned > 0));

        var expected =
            Odds.Returns(Track, slip[0], order) + Odds.Returns(Track, slip[1], order) + Odds.Returns(Track, slip[2], order);

        Assert.Equal(expected, result.Returned);
    }

    /// <summary>
    /// The totals on the result are the sums of what is on it, and nothing else. A
    /// total worked out separately from the settlements it claims to add up is a total
    /// that can disagree with them.
    /// </summary>
    [Fact]
    public void TheTotalsAreExactlyTheSumOfTheSettlements()
    {
        var random = new Random(20260919);
        var rules = new RaceRules();

        for (var i = 0; i < 5_000; i++)
        {
            var slip = new[]
            {
                Bet.On(BetKind.Win, 1 + (i % Field.Count), rules.MinBet),
                Bet.On(BetKind.Place, 1 + ((i + 3) % Field.Count), rules.MinBet * 2),
                new Bet(BetKind.Exacta, 1 + (i % Field.Count), 1 + ((i + 1) % Field.Count), rules.MinBet),
            };

            var result = Race.Run(Track, slip, random);

            Assert.Equal(result.Settlements.Sum(s => s.Bet.Stake), result.Staked);
            Assert.Equal(result.Settlements.Sum(s => s.Returned), result.Returned);

            // A settlement that won pays something, and one that did not pays nothing.
            // The two halves of the record can never disagree.
            Assert.All(
                result.Settlements,
                s => Assert.Equal(s.Won, s.Returned > 0));
        }
    }

    /// <summary>
    /// The engine never pays more than the board promised, and never less.
    ///
    /// Checked over every spot rather than a sample, because this is where the floor
    /// in <see cref="Odds.Returns"/> lives and a floor applied in the wrong place is
    /// invisible until somebody stakes an odd number.
    /// </summary>
    [Fact]
    public void AWinnerIsPaidExactlyTheBoardPriceFlooredToAChip()
    {
        const int stake = 137_000;

        foreach (var price in Odds.All(Track))
        {
            var bet = new Bet(price.Kind, price.First, price.Second, stake);

            // An order this bet is known to cover: the board's own first two, then
            // the rest of the field in any order.
            var order = CoveringOrder(bet);

            Assert.True(bet.Covers(order), $"{price.Kind} {price.First}/{price.Second} could not be made to win.");
            Assert.Equal((int)Math.Floor(stake * price.Board), Odds.Returns(Track, bet, order));
        }
    }

    /// <summary>
    /// A stake at the ceiling on the longest price on the board does not overflow.
    ///
    /// <c>OddsTests</c> asserts the arithmetic; this asserts the engine actually
    /// behaves, because the two are only the same thing if <see cref="Odds.Returns"/>
    /// does its multiplication in the width the arithmetic assumed.
    /// </summary>
    [Fact]
    public void TheBiggestPossibleWinIsStillAPositiveNumber()
    {
        var rules = new RaceRules();
        var longest = Odds.All(Track).OrderByDescending(p => p.Board).First();

        var bet = new Bet(longest.Kind, longest.First, longest.Second, Track.MaxSlip);
        var order = CoveringOrder(bet);
        var paid = Odds.Returns(Track, bet, order);

        Assert.True(paid > 0, $"the biggest win on the board came back as {paid}.");
        Assert.True(paid > Track.MaxSlip, "a winning bet must return more than it staked.");
        Assert.Equal((int)Math.Floor(Track.MaxSlip * longest.Board), paid);
    }

    [Fact]
    public void TheWindowRefusesAStakeOffTheStep()
    {
        var rules = new RaceRules();

        Assert.True(rules.Accepts(Track, Bet.On(BetKind.Win, 1, 25_000)));
        Assert.False(rules.Accepts(Track, Bet.On(BetKind.Win, 1, 12_345)));
        Assert.False(rules.Accepts(Track, Bet.On(BetKind.Win, 1, rules.MinBet - rules.Step)));
        Assert.False(rules.Accepts(Track, Bet.On(BetKind.Win, 1, Track.MaxSlip + rules.Step)));
        Assert.False(rules.Accepts(Track, Bet.On(BetKind.Win, 9, rules.MinBet)));
    }

    /// <summary>
    /// A finishing order this bet wins on: its named runners first, then everybody
    /// else. Built rather than hardcoded so it keeps working as the card changes.
    /// </summary>
    private static int[] CoveringOrder(Bet bet)
    {
        var front = bet.IsPair ? new[] { bet.First, bet.Second } : [bet.First];
        var rest = Field.Runners
            .Select(r => r.Number)
            .Where(n => !front.Contains(n));

        return [.. front, .. rest];
    }
}
