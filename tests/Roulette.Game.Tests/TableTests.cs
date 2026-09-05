namespace Roulette.Game.Tests;

/// <summary>
/// Placing, settling, and the rules that stop a player betting the house out of
/// existence.
/// </summary>
public class TableTests
{
    private static RouletteTable Table(RouletteRules? rules = null) =>
        new(rules ?? new RouletteRules(), new Random(4242));

    [Fact]
    public void ASecondChipOnTheSameSpotAddsToTheFirst()
    {
        var table = Table();

        table.Place(new Bet(BetKind.Red, 0, 10_000));
        table.Place(new Bet(BetKind.Red, 0, 20_000));

        Assert.Single(table.Bets);
        Assert.Equal(30_000, table.Bets[0].Amount);
        Assert.Equal(30_000, table.Staked);
    }

    [Fact]
    public void DifferentSpotsAreDifferentBets()
    {
        var table = Table();

        table.Place(new Bet(BetKind.Straight, 17, 10_000));
        table.Place(new Bet(BetKind.Straight, 18, 10_000));

        Assert.Equal(2, table.Bets.Count);
    }

    /// <summary>
    /// The cap has to read the whole spot, not the chip being added, or it is only a
    /// cap on the first chip and any amount can be reached one chip at a time.
    /// </summary>
    [Fact]
    public void TheCapCountsWhatIsAlreadyOnTheSpot()
    {
        var rules = new RouletteRules { MaxBet = 100_000 };
        var table = Table(rules);
        var max = rules.MaxFor(BetKind.Straight);

        table.Place(new Bet(BetKind.Straight, 17, max));

        Assert.Throws<ArgumentException>(() => table.Place(new Bet(BetKind.Straight, 17, 10_000)));
        Assert.Equal(max, table.Bets[0].Amount);
    }

    /// <summary>
    /// Every bet has the same ceiling, because there is no house maximum here -- what
    /// is left is only the point at which a payout stops fitting in an int. A player
    /// putting their whole stash on one number is the game, not something to be
    /// protected from.
    /// </summary>
    [Fact]
    public void EveryBetHasTheSameCeilingBecauseTheHouseDoesNotCap()
    {
        var rules = new RouletteRules();

        foreach (var kind in new[]
                 {
                     BetKind.Straight, BetKind.Split, BetKind.Corner, BetKind.Dozen, BetKind.Red,
                 })
        {
            Assert.Equal(rules.MaxBet, rules.MaxFor(kind));
        }
    }

    /// <summary>
    /// The ceiling that is left has to keep the biggest possible payout inside an int,
    /// or a winning straight-up bet comes back negative. Thirty-six times the whole
    /// cloth is the worst case.
    /// </summary>
    [Fact]
    public void TheCeilingKeepsTheBiggestPayoutInsideAnInt()
    {
        var rules = new RouletteRules();

        Assert.True((long)rules.MaxBet * 36 < int.MaxValue);
        Assert.True((long)rules.MaxTotalStake * 36 < int.MaxValue);
    }

    /// <summary>
    /// A million-chip stake on a single number is taken. It was not before: the old
    /// ceiling scaled down by what a bet paid, which put a straight-up maximum at
    /// 142,857 and made the largest chip on the tray unplaceable.
    /// </summary>
    [Fact]
    public void TheLargestChipCanGoOnASingleNumber()
    {
        var table = Table();

        table.Place(new Bet(BetKind.Straight, 17, 1_000_000));

        Assert.Equal(1_000_000, table.Staked);
    }

    /// <summary>
    /// Bets move in whole steps of 5,000, and nothing is free.
    ///
    /// 15,000 used to be refused here, on the rule that a bet had to be a whole number
    /// of the smallest chip. That rule also refused the 25,000 chip, which is a real
    /// chip in the tray, so it went -- see EveryChipInTheTrayCanBePlaced.
    ///
    /// 15,000 is legal now and the tray cannot build it: 10k and 25k are the smallest
    /// two, and no combination makes three steps. That is a gap rather than a fault.
    /// Every amount the client can produce is a sum of denominations and so is
    /// reachable by construction; 15,000 can only arrive from a hand-written request,
    /// where the worst it does is draw a pile with a remainder on the player's own
    /// money. The alternative -- teaching the engine which chip images exist -- would
    /// put the art in the rules.
    /// </summary>
    [Fact]
    public void BetsGoUpInWholeSteps()
    {
        var table = Table();

        Assert.Throws<ArgumentException>(() => table.Place(new Bet(BetKind.Red, 0, 12_345)));
        Assert.Throws<ArgumentException>(() => table.Place(new Bet(BetKind.Red, 0, 0)));
        Assert.Throws<ArgumentException>(() => table.Place(new Bet(BetKind.Red, 0, -10_000)));
    }

    [Fact]
    public void TheTopLineIsRefusedOnAEuropeanWheel()
    {
        var table = Table();

        var refused = Assert.Throws<ArgumentException>(
            () => table.Place(new Bet(BetKind.TopLine, 0, 10_000)));

        Assert.Contains("European", refused.Message);
    }

    [Fact]
    public void TheTopLineIsTakenOnAnAmericanWheel()
    {
        var table = Table(new RouletteRules { Wheel = WheelKind.American });

        table.Place(new Bet(BetKind.TopLine, 0, 10_000));

        Assert.Single(table.Bets);
    }

    [Theory]
    [InlineData(BetKind.Street, 2)]
    [InlineData(BetKind.SixLine, 2)]
    [InlineData(BetKind.Corner, 3)]
    [InlineData(BetKind.Straight, 37)]
    [InlineData(BetKind.Dozen, 4)]
    [InlineData(BetKind.Column, 0)]
    public void ASpotThatIsNotOnTheClothIsRefused(BetKind kind, int selection)
    {
        var table = Table();

        Assert.Throws<ArgumentException>(() => table.Place(new Bet(kind, selection, 10_000)));
    }

    [Fact]
    public void TheWheelWillNotTurnWithAnEmptyCloth()
    {
        Assert.Throws<InvalidOperationException>(() => Table().Spin());
    }

    [Fact]
    public void NothingCanBePlacedOnceTheWheelHasTurned()
    {
        var table = Table();
        table.Place(new Bet(BetKind.Red, 0, 10_000));
        table.Spin();

        Assert.Throws<InvalidOperationException>(() => table.Place(new Bet(BetKind.Red, 0, 10_000)));
        Assert.Throws<InvalidOperationException>(() => table.Spin());
    }

    /// <summary>
    /// The bets do not carry over. Leaving them on would stake a player's money on a
    /// spin they never asked for, which is the worst possible way to lose it.
    /// </summary>
    [Fact]
    public void TheClothIsEmptyForTheNextSpin()
    {
        var table = Table();
        table.Place(new Bet(BetKind.Red, 0, 10_000));
        table.Spin();
        table.NextSpin();

        Assert.Empty(table.Bets);
        Assert.Equal(0, table.Staked);
        Assert.Equal(SpinPhase.Betting, table.Phase);
    }

    /// <summary>
    /// The settled result has to say where on the wheel the ball is, not only which
    /// number: the wheel is not in numerical order, and the client cannot land the
    /// animation without it.
    /// </summary>
    [Fact]
    public void TheResultCarriesItsPlaceOnTheWheel()
    {
        var table = Table();
        table.Place(new Bet(BetKind.Red, 0, 10_000));

        var spin = table.SettleOn(26);

        Assert.Equal(26, spin.Result.Number);
        Assert.Equal(new Wheel().PositionOf(26), spin.Position);
        Assert.NotEqual(26, spin.Position);
    }

    [Fact]
    public void EveryBetOnTheClothIsAccountedForInTheResult()
    {
        var table = Table();
        table.Place(new Bet(BetKind.Straight, 17, 10_000));
        table.Place(new Bet(BetKind.Red, 0, 20_000));
        table.Place(new Bet(BetKind.Dozen, 2, 30_000));

        var spin = table.SettleOn(17);

        Assert.Equal(3, spin.Outcomes.Count);
        Assert.Equal(60_000, spin.Staked);
        Assert.Equal(spin.Outcomes.Sum(o => o.Bet.Amount), spin.Staked);
        Assert.Equal(spin.Outcomes.Sum(o => o.Returned), spin.Returned);
    }

    /// <summary>
    /// 17 is black and in the second dozen, so a straight-up on it wins while red
    /// loses and the dozen comes in. Three different rules resolving on one spin is
    /// where a settlement bug hides.
    /// </summary>
    [Fact]
    public void EachBetSettlesOnItsOwnRule()
    {
        var table = Table();
        table.Place(new Bet(BetKind.Straight, 17, 10_000));
        table.Place(new Bet(BetKind.Red, 0, 20_000));
        table.Place(new Bet(BetKind.Dozen, 2, 30_000));

        var spin = table.SettleOn(17);
        var by = spin.Outcomes.ToDictionary(o => o.Bet.Kind);

        Assert.True(by[BetKind.Straight].Won);
        Assert.Equal(360_000, by[BetKind.Straight].Returned);

        Assert.False(by[BetKind.Red].Won);
        Assert.Equal(0, by[BetKind.Red].Returned);

        Assert.True(by[BetKind.Dozen].Won);
        Assert.Equal(90_000, by[BetKind.Dozen].Returned);

        Assert.Equal(450_000 - 60_000, spin.Profit);
    }

    [Fact]
    public void ZeroTakesEverythingExceptABetOnZero()
    {
        var table = Table();
        table.Place(new Bet(BetKind.Red, 0, 10_000));
        table.Place(new Bet(BetKind.Black, 0, 10_000));
        table.Place(new Bet(BetKind.Even, 0, 10_000));
        table.Place(new Bet(BetKind.Low, 0, 10_000));
        table.Place(new Bet(BetKind.Straight, 0, 10_000));

        var spin = table.SettleOn(0);

        Assert.Equal(360_000, spin.Returned);
        Assert.Single(spin.Outcomes, o => o.Won);
    }

    /// <summary>
    /// Every chip in the tray is a legal bet, on its own and stacked with any other.
    ///
    /// This is the test that would have caught the 25,000 chip being unplaceable. The
    /// step used to be the smallest chip, 10,000, and 25,000 is not a multiple of it,
    /// so that chip was refused every time it was put down -- on a live table, by a
    /// player, before anything here noticed.
    /// </summary>
    [Theory]
    [InlineData(10_000)]
    [InlineData(25_000)]
    [InlineData(50_000)]
    [InlineData(100_000)]
    [InlineData(500_000)]
    [InlineData(1_000_000)]
    public void EveryChipInTheTrayCanBePlaced(int chip)
    {
        var table = Table();
        table.Place(new Bet(BetKind.Straight, 7, chip));

        Assert.Equal(chip, table.Staked);
    }

    [Fact]
    public void ChipsStackInAnyCombination()
    {
        int[] tray = [10_000, 25_000, 50_000, 100_000, 500_000, 1_000_000];
        var table = Table();
        var total = 0;

        // Every pair, on the same spot, so the running total is what gets validated
        // rather than each chip on its own.
        foreach (var first in tray)
        {
            foreach (var second in tray)
            {
                table.Place(new Bet(BetKind.Straight, 7, first));
                table.Place(new Bet(BetKind.Straight, 7, second));
                total += first + second;
            }
        }

        Assert.Equal(total, table.Staked);
    }

    [Fact]
    public void ABetUnderTheMinimumIsRefused()
    {
        var table = Table();

        Assert.Throws<ArgumentException>(() => table.Place(new Bet(BetKind.Straight, 7, 5_000)));
    }

    [Fact]
    public void ABetOffTheStepIsRefused()
    {
        var table = Table();

        Assert.Throws<ArgumentException>(() => table.Place(new Bet(BetKind.Straight, 7, 12_345)));
    }

    [Fact]
    public void LiftingAChipReducesThePileRatherThanClearingIt()
    {
        var table = Table();
        table.Place(new Bet(BetKind.Straight, 7, 30_000));

        Assert.Equal(10_000, table.Remove(BetKind.Straight, 7, 10_000));
        Assert.Equal(20_000, table.Staked);
        Assert.Single(table.Bets);
    }

    [Fact]
    public void LiftingMoreThanIsThereTakesWhatIsThere()
    {
        // A player holding a 100k chip who right-clicks a 10k pile means "take it
        // off", not "do nothing" -- and must not be handed 90k that was never staked.
        var table = Table();
        table.Place(new Bet(BetKind.Straight, 7, 10_000));

        Assert.Equal(10_000, table.Remove(BetKind.Straight, 7, 100_000));
        Assert.Empty(table.Bets);
    }

    [Fact]
    public void LiftingEverythingOffASpotRemovesTheBet()
    {
        var table = Table();
        table.Place(new Bet(BetKind.Straight, 7, 10_000));

        table.Remove(BetKind.Straight, 7, 10_000);

        Assert.Empty(table.Bets);
        Assert.Equal(0, table.Staked);
    }

    [Fact]
    public void LiftingZeroTakesTheWholePile()
    {
        var table = Table();
        table.Place(new Bet(BetKind.Straight, 7, 40_000));

        Assert.Equal(40_000, table.Remove(BetKind.Straight, 7, 0));
        Assert.Empty(table.Bets);
    }

    [Fact]
    public void LiftingFromAnEmptySpotDoesNothing()
    {
        var table = Table();
        table.Place(new Bet(BetKind.Straight, 7, 10_000));

        Assert.Equal(0, table.Remove(BetKind.Straight, 8, 10_000));
        Assert.Equal(10_000, table.Staked);
    }

    [Fact]
    public void LiftingTouchesOnlyTheSpotAskedFor()
    {
        // Split selections are indices, and straight-up bets are numbers. The two
        // share a value space, so a bet must be matched on its kind as well.
        var table = Table();
        table.Place(new Bet(BetKind.Straight, 3, 10_000));
        table.Place(new Bet(BetKind.Split, 3, 10_000));

        table.Remove(BetKind.Straight, 3, 10_000);

        var left = Assert.Single(table.Bets);
        Assert.Equal(BetKind.Split, left.Kind);
    }

    [Fact]
    public void NothingComesOffOnceTheWheelHasTurned()
    {
        var table = Table();
        table.Place(new Bet(BetKind.Straight, 7, 10_000));
        table.Spin();

        Assert.Throws<InvalidOperationException>(() => table.Remove(BetKind.Straight, 7, 10_000));
    }

    [Fact]
    public void ClearingTheClothGivesBackExactlyWhatWasOnIt()
    {
        var table = Table();
        table.Place(new Bet(BetKind.Red, 0, 10_000));
        table.Place(new Bet(BetKind.Straight, 7, 20_000));

        Assert.Equal(30_000, table.ClearBets());
        Assert.Empty(table.Bets);
    }

    [Fact]
    public void TheTableTakesOnlySoMuchInTotal()
    {
        var rules = new RouletteRules { MaxTotalStake = 50_000 };
        var table = Table(rules);

        table.Place(new Bet(BetKind.Red, 0, 40_000));

        Assert.Throws<ArgumentException>(() => table.Place(new Bet(BetKind.Black, 0, 20_000)));
    }

    [Fact]
    public void ThereIsOnlySoMuchRoomOnTheCloth()
    {
        var rules = new RouletteRules { MaxBets = 3 };
        var table = Table(rules);

        for (var n = 1; n <= 3; n++)
        {
            table.Place(new Bet(BetKind.Straight, n, 10_000));
        }

        Assert.Throws<ArgumentException>(() => table.Place(new Bet(BetKind.Straight, 4, 10_000)));

        // A fourth chip on a spot already covered is still fine -- it is not a new bet.
        table.Place(new Bet(BetKind.Straight, 1, 10_000));
        Assert.Equal(3, table.Bets.Count);
    }
}
