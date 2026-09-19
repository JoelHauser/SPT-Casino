namespace HorseRacing.Game.Tests;

/// <summary>
/// What the board pays, and whether the arithmetic that says so is right.
///
/// The house edge here is arithmetic over the card rather than something to be
/// discovered by running races. These check both that the arithmetic says something
/// sane and that it says the truth.
/// </summary>
public class OddsTests
{
    /// <summary>
    /// The cheapest possible check that the model is coherent, and the first thing
    /// worth asserting: the 336 ordered top-threes are mutually exclusive and
    /// exhaustive, so their probabilities sum to one.
    ///
    /// A weight handled twice, a runner allowed to finish in two places, or a
    /// denominator that forgot to shrink as the field did would all show up here and
    /// nowhere else nearly as loudly.
    /// </summary>
    [Fact]
    public void EveryOrderedTopThreeTogetherAccountsForExactlyOneRace()
    {
        var count = 0;
        var total = 0.0;

        foreach (var (_, probability) in Odds.Prefixes())
        {
            Assert.True(probability > 0.0, "a prefix with no chance of happening is a bug in the weights.");
            count++;
            total += probability;
        }

        Assert.Equal(Field.Count * (Field.Count - 1) * (Field.Count - 2), count);
        Assert.Equal(1.0, total, 12);
    }

    /// <summary>
    /// **The test that matters.** The closed form in <see cref="Odds"/> is checked
    /// against actually running the races.
    ///
    /// A formula derived from the same misunderstanding as the code it describes would
    /// agree with itself perfectly, so this deliberately shares nothing with it: it
    /// draws real finishing orders through <see cref="Race.Draw"/> and counts what
    /// happened. Two hundred thousand races puts the standard error on a 27% runner at
    /// about a tenth of a percentage point, so a chance that were wrong in any way
    /// worth caring about would not land this close.
    ///
    /// Every bet kind is checked, not just Win. Place and Show are where an
    /// off-by-one in the placings window hides, and the pair bets are where ordered
    /// and unordered get confused -- and a quinella priced as an exacta is a bet
    /// mispriced by exactly a factor of two, which no balance check would ever catch.
    /// </summary>
    [Theory]
    [InlineData(BetKind.Win, 1, 0)]
    [InlineData(BetKind.Win, 8, 0)]
    [InlineData(BetKind.Place, 1, 0)]
    [InlineData(BetKind.Place, 6, 0)]
    [InlineData(BetKind.Show, 2, 0)]
    [InlineData(BetKind.Show, 8, 0)]
    [InlineData(BetKind.Exacta, 1, 2)]
    [InlineData(BetKind.Exacta, 2, 1)]
    [InlineData(BetKind.Quinella, 1, 2)]
    [InlineData(BetKind.Quinella, 3, 7)]
    public void TheComputedChanceMatchesWhatActuallyHappens(BetKind kind, int first, int second)
    {
        const int races = 200_000;

        var bet = new Bet(kind, first, second, 0);
        var random = new Random(20260919);
        var won = 0;

        for (var i = 0; i < races; i++)
        {
            if (bet.Covers(Race.Draw(random)))
            {
                won++;
            }
        }

        var measured = (double)won / races;
        var computed = Odds.Chance(kind, first, second);

        Assert.InRange(measured, computed - 0.005, computed + 0.005);
    }

    /// <summary>
    /// A quinella is the two exactas that make it up, exactly. Worth asserting on its
    /// own because it is the one identity on this board that can be checked without
    /// going anywhere near the enumeration.
    /// </summary>
    [Fact]
    public void AQuinellaIsItsTwoExactasAddedTogether()
    {
        foreach (var a in Field.Runners)
        {
            foreach (var b in Field.Runners)
            {
                if (a.Number >= b.Number)
                {
                    continue;
                }

                var quinella = Odds.Chance(BetKind.Quinella, a.Number, b.Number);
                var forward = Odds.Chance(BetKind.Exacta, a.Number, b.Number);
                var reverse = Odds.Chance(BetKind.Exacta, b.Number, a.Number);

                Assert.Equal(forward + reverse, quinella, 12);
            }
        }
    }

    /// <summary>
    /// Exactly one runner wins, so the win chances add to one. And a runner's chance
    /// of placing is at least its chance of winning, and of showing at least of
    /// placing -- a containment that a placings window built the wrong way round
    /// would invert.
    /// </summary>
    [Fact]
    public void TheCardIsInternallyConsistent()
    {
        var wins = 0.0;

        foreach (var runner in Field.Runners)
        {
            var win = Odds.Chance(BetKind.Win, runner.Number);
            var place = Odds.Chance(BetKind.Place, runner.Number);
            var show = Odds.Chance(BetKind.Show, runner.Number);

            Assert.True(win <= place, $"runner {runner.Number} wins more often than it places.");
            Assert.True(place <= show, $"runner {runner.Number} places more often than it shows.");

            wins += win;
        }

        Assert.Equal(1.0, wins, 12);
    }

    /// <summary>
    /// The favourite is favourite and the rag is the rag. A sanity check on the card
    /// itself rather than on the arithmetic, and the one test that would notice
    /// somebody editing a weight in <see cref="Field"/> by accident.
    /// </summary>
    [Fact]
    public void AHeavierRunnerIsAlwaysTheShorterPrice()
    {
        for (var i = 1; i < Field.Runners.Count; i++)
        {
            var longer = Field.Runners[i - 1];
            var shorter = Field.Runners[i];

            Assert.True(
                longer.Weight > shorter.Weight,
                "the card is written favourite-first; a flat or inverted pair means it was edited without reading it.");

            Assert.True(
                Odds.BoardPrice(BetKind.Win, longer.Number) < Odds.BoardPrice(BetKind.Win, shorter.Number),
                $"runner {longer.Number} is the heavier and must be the shorter price.");
        }
    }

    /// <summary>
    /// **The house edge is the single number the game is built around, so it is
    /// checked on every one of the 108 spots rather than on a sample.**
    ///
    /// The lower bound is the one that matters: a spot priced below the takeout is a
    /// spot the house loses money on over time, and it would be the spot anyone
    /// reading the board would find. The upper bound catches the opposite failure --
    /// a tick coarse enough to quietly double the edge on the short-priced favourite,
    /// which is where rounding down bites hardest because the price is small.
    /// </summary>
    [Fact]
    public void NoSpotOnTheBoardIsPricedBelowTheTakeoutOrFarAboveIt()
    {
        foreach (var price in Odds.All())
        {
            var realised = Odds.Realised(price.Kind, price.First, price.Second);

            Assert.True(
                realised >= Odds.Takeout - 1e-9,
                $"{price.Kind} {price.First}/{price.Second} carries {realised:P4}, under the {Odds.Takeout:P2} takeout.");

            Assert.True(
                realised <= Odds.Takeout + 0.01,
                $"{price.Kind} {price.First}/{price.Second} carries {realised:P4}, more than a point over the takeout.");
        }
    }

    /// <summary>
    /// The board offers what the slip claims it does: 24 single-runner spots, 56
    /// exactas, 28 quinellas. <see cref="RaceRules.MaxBets"/> is that total, and a
    /// mismatch means a player can build a legal slip the window refuses.
    /// </summary>
    [Fact]
    public void TheBoardIsTheHundredAndEightSpotsTheRulesAllow()
    {
        var all = Odds.All();

        Assert.Equal(Field.Count * 3, all.Count(p => !new Bet(p.Kind, p.First, p.Second, 0).IsPair));
        Assert.Equal(Field.Count * (Field.Count - 1), all.Count(p => p.Kind == BetKind.Exacta));
        Assert.Equal(Field.Count * (Field.Count - 1) / 2, all.Count(p => p.Kind == BetKind.Quinella));

        Assert.Equal(new RaceRules().MaxBets, all.Count);

        // No spot quoted twice. A duplicated quinella is two places on the slip that
        // take the same money for the same bet.
        Assert.Equal(all.Count, all.Select(p => (p.Kind, p.First, p.Second)).Distinct().Count());
    }

    /// <summary>
    /// **The arithmetic behind <see cref="RaceRules.MaxBet"/>, asserted against the
    /// real card rather than against the paragraph that explains it.**
    ///
    /// The moment somebody edits a weight in <see cref="Field"/>, the longest price
    /// moves and the prose in <c>RaceRules</c> does not. This is the test that
    /// notices. A stake at the ceiling times the longest price on the board must still
    /// fit in the <c>int</c> the engine counts chips in -- an overflow here does not
    /// pay a big win, it pays a negative one.
    /// </summary>
    [Fact]
    public void TheMaximumStakeAtTheLongestPriceStillFitsInAnInt()
    {
        var rules = new RaceRules();
        var longest = Odds.All().Max(p => p.Board);

        Assert.True(longest > 1.0, "a board whose longest price is a refund is not a board.");

        var biggest = (long)Math.Floor(rules.MaxBet * longest);

        Assert.True(
            biggest <= int.MaxValue,
            $"{rules.MaxBet:N0} at {longest:F2} is {biggest:N0}, past int.MaxValue. Lower MaxBet or move the engine to long.");

        // And the ceiling is not absurdly conservative either -- doubling it should be
        // what breaks, so that MaxBet is demonstrably near the real limit rather than
        // a round number somebody liked.
        Assert.True(
            (long)Math.Floor(rules.MaxBet * 2.0 * longest) > int.MaxValue,
            "MaxBet is far below what the arithmetic actually allows; it should be the binding limit or not exist.");
    }
}
