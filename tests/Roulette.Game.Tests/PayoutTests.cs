namespace Roulette.Game.Tests;

/// <summary>
/// The payouts, checked by arithmetic rather than by simulation.
///
/// Roulette is the one game in this family where the edge is exact and knowable, so
/// there is no excuse for measuring it: Poker's notes record that confirming a house
/// edge by simulation needs millions of hands to see a tenth of a point. Every
/// number below is computed from the wheel.
/// </summary>
public class PayoutTests
{
    private static readonly BetKind[] EuropeanBets =
    [
        BetKind.Straight, BetKind.Split, BetKind.Street, BetKind.Corner, BetKind.SixLine,
        BetKind.Column, BetKind.Dozen,
        BetKind.Red, BetKind.Black, BetKind.Odd, BetKind.Even, BetKind.Low, BetKind.High,
    ];

    private static Bet Any(BetKind kind, int amount = 10_000) => new(kind, DefaultSelection(kind), amount);

    private static int DefaultSelection(BetKind kind) => kind switch
    {
        BetKind.Straight => 17,
        BetKind.Split => 0,
        BetKind.Street => 1,
        BetKind.Corner => 1,
        BetKind.SixLine => 1,
        BetKind.Column or BetKind.Dozen => 1,
        _ => 0,
    };

    /// <summary>
    /// A bet's odds and the numbers it covers must multiply out to 36, on every bet
    /// on the cloth. That single identity is what makes the house edge come from the
    /// zero rather than from the paytable, and it catches both halves of the classic
    /// mistake -- wrong odds, or a bet covering the wrong count of numbers.
    /// </summary>
    [Theory]
    [InlineData(BetKind.Straight, 1, 35)]
    [InlineData(BetKind.Split, 2, 17)]
    [InlineData(BetKind.Street, 3, 11)]
    [InlineData(BetKind.Corner, 4, 8)]
    [InlineData(BetKind.SixLine, 6, 5)]
    [InlineData(BetKind.Column, 12, 2)]
    [InlineData(BetKind.Dozen, 12, 2)]
    [InlineData(BetKind.Red, 18, 1)]
    [InlineData(BetKind.Black, 18, 1)]
    [InlineData(BetKind.Odd, 18, 1)]
    [InlineData(BetKind.Even, 18, 1)]
    [InlineData(BetKind.Low, 18, 1)]
    [InlineData(BetKind.High, 18, 1)]
    public void OddsAndCoverageMultiplyOutToThirtySix(BetKind kind, int covered, int odds)
    {
        var wheel = new Wheel(WheelKind.European);

        Assert.Equal(covered, Any(kind).Covers(wheel).Distinct().Count());
        Assert.Equal(odds, Payouts.ToOne(kind));
        Assert.Equal(36, covered * (odds + 1));
    }

    /// <summary>
    /// Every European bet returns the same 2.70%, which is 1/37 -- one pocket in
    /// thirty-seven that pays nobody. Computed exactly over the whole wheel.
    /// </summary>
    [Fact]
    public void EveryEuropeanBetGivesTheHouseExactlyOnePocketIn37()
    {
        var wheel = new Wheel(WheelKind.European);

        foreach (var kind in EuropeanBets)
        {
            var bet = Any(kind, 3_700_000);
            var returned = wheel.Pockets.Sum(p => (long)Payouts.Returned(bet, wheel, p));
            var staked = (long)bet.Amount * wheel.PocketCount;
            var edge = (staked - returned) / (double)staked;

            Assert.Equal(1d / 37d, edge, 12);
        }
    }

    [Fact]
    public void TheAmericanWheelDoublesTheEdgeBecauseItDoublesTheZeroes()
    {
        var wheel = new Wheel(WheelKind.American);

        foreach (var kind in EuropeanBets)
        {
            var bet = Any(kind, 3_800_000);
            var returned = wheel.Pockets.Sum(p => (long)Payouts.Returned(bet, wheel, p));
            var staked = (long)bet.Amount * wheel.PocketCount;

            Assert.Equal(2d / 38d, (staked - returned) / (double)staked, 12);
        }
    }

    /// <summary>
    /// The top line is the one bet that breaks the 36 identity -- five numbers at 6
    /// to 1 returns 35, not 36 -- and that is exactly why it is the worst bet on
    /// either wheel at 7.89%. Worth pinning so nobody "fixes" it to 7 to 1.
    /// </summary>
    [Fact]
    public void TheTopLineIsDeliberatelyWorseThanEveryOtherBet()
    {
        var wheel = new Wheel(WheelKind.American);
        var bet = new Bet(BetKind.TopLine, 0, 3_800_000);

        Assert.Equal(5, bet.Covers(wheel).Distinct().Count());
        Assert.Equal(35, 5 * (Payouts.ToOne(BetKind.TopLine) + 1));

        var returned = wheel.Pockets.Sum(p => (long)Payouts.Returned(bet, wheel, p));
        var staked = (long)bet.Amount * wheel.PocketCount;
        var edge = (staked - returned) / (double)staked;

        Assert.Equal(3d / 38d, edge, 12);
        Assert.True(edge > 2d / 38d);
    }

    /// <summary>
    /// A winner gets its stake back on top of the winnings. Paying the winnings
    /// alone is the off-by-one that quietly keeps every stake the house ever took.
    /// </summary>
    [Fact]
    public void AWinnerGetsItsStakeBackAsWellAsTheWinnings()
    {
        var wheel = new Wheel();
        var bet = new Bet(BetKind.Straight, 17, 10_000);

        Assert.Equal(360_000, Payouts.Returned(bet, wheel, wheel.PocketFor(17)));
        Assert.Equal(350_000, Payouts.Profit(bet, wheel, wheel.PocketFor(17)));
    }

    [Fact]
    public void ALoserGetsNothingBackAtAll()
    {
        var wheel = new Wheel();
        var bet = new Bet(BetKind.Straight, 17, 10_000);

        Assert.Equal(0, Payouts.Returned(bet, wheel, wheel.PocketFor(18)));
        Assert.Equal(-10_000, Payouts.Profit(bet, wheel, wheel.PocketFor(18)));
    }

    /// <summary>
    /// Zero is not red, not black, not odd, not even, not low and not high. It is
    /// the whole house edge, and a bet that treats it as even loses the house its
    /// entire margin on half the cloth.
    /// </summary>
    [Theory]
    [InlineData(BetKind.Red)]
    [InlineData(BetKind.Black)]
    [InlineData(BetKind.Odd)]
    [InlineData(BetKind.Even)]
    [InlineData(BetKind.Low)]
    [InlineData(BetKind.High)]
    public void ZeroBeatsEveryEvenMoneyBet(BetKind kind)
    {
        var wheel = new Wheel();

        Assert.False(Any(kind).Wins(wheel, wheel.PocketFor(0)));
    }

    [Fact]
    public void TheDozensAndColumnsCoverEveryNumberBetweenThemExactlyOnce()
    {
        var wheel = new Wheel();

        foreach (var kind in new[] { BetKind.Dozen, BetKind.Column })
        {
            var covered = Enumerable.Range(1, 3)
                .SelectMany(sel => new Bet(kind, sel, 10_000).Covers(wheel))
                .ToList();

            Assert.Equal(36, covered.Count);
            Assert.Equal(Enumerable.Range(1, 36), covered.OrderBy(n => n));
        }
    }

    [Fact]
    public void RedAndBlackDivideTheNumbersBetweenThem()
    {
        var wheel = new Wheel();

        var red = Any(BetKind.Red).Covers(wheel).ToList();
        var black = Any(BetKind.Black).Covers(wheel).ToList();

        Assert.Equal(18, red.Count);
        Assert.Equal(18, black.Count);
        Assert.Empty(red.Intersect(black));
        Assert.Equal(Enumerable.Range(1, 36), red.Concat(black).OrderBy(n => n));
    }
}
