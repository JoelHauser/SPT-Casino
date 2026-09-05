namespace Roulette.Game.Tests;

/// <summary>
/// The cloth. A split is the one bet a number cannot name on its own, and the
/// enumeration that fixes that is a contract the client sends indices into.
/// </summary>
public class LayoutTests
{
    /// <summary>
    /// 3 zero splits, 24 across the rows and 33 down the columns. If this count
    /// moves, every split bet already placed points somewhere else.
    /// </summary>
    [Fact]
    public void TheSplitOrderIsFixed()
    {
        Assert.Equal(60, Layout.Splits.Count);
        Assert.Equal((0, 1), Layout.Splits[0]);
        Assert.Equal((0, 3), Layout.Splits[2]);
        Assert.Equal((1, 2), Layout.Splits[3]);
        Assert.Equal((33, 36), Layout.Splits[^1]);
    }

    /// <summary>
    /// Both splits on 1 exist and cover different numbers. This is the whole reason
    /// splits are enumerated rather than named by their lowest number.
    /// </summary>
    [Fact]
    public void BothSplitsOnOneExistAndAreDifferent()
    {
        var wheel = new Wheel();

        var across = Layout.Splits.ToList().IndexOf((1, 2));
        var down = Layout.Splits.ToList().IndexOf((1, 4));

        Assert.True(across >= 0 && down >= 0);
        Assert.NotEqual(across, down);

        Assert.Equal([1, 2], new Bet(BetKind.Split, across, 10_000).Covers(wheel));
        Assert.Equal([1, 4], new Bet(BetKind.Split, down, 10_000).Covers(wheel));
    }

    [Fact]
    public void EverySplitIsTwoNeighboursOnTheCloth()
    {
        foreach (var (low, high) in Layout.Splits)
        {
            var neighbours = low == 0
                ? high is >= 1 and <= 3
                : high == low + 3 || (high == low + 1 && low % 3 != 0);

            Assert.True(neighbours, $"{low}-{high} is not a pair on the cloth.");
        }
    }

    [Fact]
    public void EverySplitIsListedOnlyOnce()
    {
        Assert.Equal(Layout.Splits.Count, Layout.Splits.Distinct().Count());
    }

    [Fact]
    public void TheStreetsAreTheTwelveRows()
    {
        var wheel = new Wheel();

        Assert.Equal(12, Layout.Streets.Count);

        var covered = Layout.Streets
            .SelectMany(s => new Bet(BetKind.Street, s, 10_000).Covers(wheel))
            .ToList();

        Assert.Equal(Enumerable.Range(1, 36), covered.OrderBy(n => n));
    }

    /// <summary>
    /// A corner covers the four numbers that actually meet at a point on the cloth.
    ///
    /// **Checked as geometry, not as a count.** This test used to assert four distinct
    /// numbers in 1..36 and nothing else, and mutation testing walked straight through
    /// it: changing the offsets so a corner on 1 covered 1, 2, 3, 5 -- a column of
    /// three plus a stray -- still gives four distinct numbers in range, still pays 8
    /// to 1, and still satisfies odds times coverage equals 36. Every test passed.
    ///
    /// The grid is the independent fact here. A number sits at column (n-1)/3, row
    /// (n-1)%3, so a corner has to be exactly two adjacent columns by two adjacent
    /// rows, all four cells filled. That is derived from the printed cloth rather than
    /// from the offsets in Covers, which is what makes it a check rather than an echo.
    /// </summary>
    [Fact]
    public void EveryCornerIsAPrintedSquare()
    {
        var wheel = new Wheel();

        Assert.Equal(22, Layout.Corners.Count);

        foreach (var corner in Layout.Corners)
        {
            var covered = new Bet(BetKind.Corner, corner, 10_000).Covers(wheel).ToList();

            Assert.Equal(4, covered.Distinct().Count());
            Assert.All(covered, n => Assert.InRange(n, 1, 36));

            var columns = covered.Select(n => (n - 1) / 3).Distinct().OrderBy(c => c).ToList();
            var rows = covered.Select(n => (n - 1) % 3).Distinct().OrderBy(r => r).ToList();

            Assert.True(
                columns.Count == 2 && columns[1] - columns[0] == 1,
                $"the corner on {corner} spans columns {string.Join(",", columns)}, "
                + "which is not two side by side.");

            Assert.True(
                rows.Count == 2 && rows[1] - rows[0] == 1,
                $"the corner on {corner} spans rows {string.Join(",", rows)}, "
                + "which is not two on top of each other.");

            // Two columns and two rows could still be three cells and a repeat.
            Assert.Equal(4, covered.Select(n => ((n - 1) / 3, (n - 1) % 3)).Distinct().Count());
        }
    }

    /// <summary>
    /// Odd covers the odd numbers and even the even ones.
    ///
    /// Obvious, untested, and a mutation that swapped them survived the whole suite:
    /// both cover eighteen numbers and both pay 1 to 1, so every count and every
    /// identity still held while the table paid odd bets on even results.
    /// </summary>
    [Fact]
    public void TheOddAndEvenBetsCoverWhatTheyAreCalled()
    {
        var wheel = new Wheel();

        var odd = new Bet(BetKind.Odd, 0, 10_000).Covers(wheel).ToList();
        var even = new Bet(BetKind.Even, 0, 10_000).Covers(wheel).ToList();

        Assert.Equal(18, odd.Count);
        Assert.Equal(18, even.Count);

        Assert.All(odd, n => Assert.True(n % 2 == 1, $"{n} is not odd."));
        Assert.All(even, n => Assert.True(n % 2 == 0, $"{n} is not even."));

        // Between them the whole cloth, and the zero in neither.
        Assert.Equal(36, odd.Concat(even).Distinct().Count());
        Assert.DoesNotContain(0, odd);
        Assert.DoesNotContain(0, even);
    }

    /// <summary>
    /// Every other grouped bet sits on the numbers its name claims, checked against an
    /// independent rule rather than against the offsets that produced it.
    /// </summary>
    [Fact]
    public void EveryGroupedBetCoversWhatItsNameClaims()
    {
        var wheel = new Wheel();

        Assert.All(
            new Bet(BetKind.Low, 0, 10_000).Covers(wheel),
            n => Assert.InRange(n, 1, 18));

        Assert.All(
            new Bet(BetKind.High, 0, 10_000).Covers(wheel),
            n => Assert.InRange(n, 19, 36));

        for (var dozen = 1; dozen <= 3; dozen++)
        {
            Assert.All(
                new Bet(BetKind.Dozen, dozen, 10_000).Covers(wheel),
                n => Assert.InRange(n, ((dozen - 1) * 12) + 1, dozen * 12));
        }

        // A column is every third number, so each one is congruent to its own index.
        for (var column = 1; column <= 3; column++)
        {
            var index = column;

            Assert.All(
                new Bet(BetKind.Column, column, 10_000).Covers(wheel),
                n => Assert.Equal(index % 3, n % 3));
        }

        // A street is three consecutive numbers, which is one printed column.
        foreach (var street in Layout.Streets)
        {
            var covered = new Bet(BetKind.Street, street, 10_000).Covers(wheel).ToList();

            Assert.Single(covered.Select(n => (n - 1) / 3).Distinct());
            Assert.Equal(3, covered.Select(n => (n - 1) % 3).Distinct().Count());
        }

        // Red and black are the wheel's own colours, not a list kept beside them.
        Assert.All(
            new Bet(BetKind.Red, 0, 10_000).Covers(wheel),
            n => Assert.Equal(PocketColour.Red, Wheel.ColourOf(n)));

        Assert.All(
            new Bet(BetKind.Black, 0, 10_000).Covers(wheel),
            n => Assert.Equal(PocketColour.Black, Wheel.ColourOf(n)));
    }

    [Fact]
    public void EverySixLineIsTwoWholeRows()
    {
        var wheel = new Wheel();

        Assert.Equal(11, Layout.SixLines.Count);

        foreach (var line in Layout.SixLines)
        {
            var covered = new Bet(BetKind.SixLine, line, 10_000).Covers(wheel).ToList();

            Assert.Equal(6, covered.Distinct().Count());
            Assert.All(covered, n => Assert.InRange(n, 1, 36));
        }
    }
}
