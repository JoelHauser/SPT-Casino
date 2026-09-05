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

    [Fact]
    public void EveryCornerIsASquareOfFour()
    {
        var wheel = new Wheel();

        Assert.Equal(22, Layout.Corners.Count);

        foreach (var corner in Layout.Corners)
        {
            var covered = new Bet(BetKind.Corner, corner, 10_000).Covers(wheel).ToList();

            Assert.Equal(4, covered.Distinct().Count());
            Assert.All(covered, n => Assert.InRange(n, 1, 36));
        }
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
