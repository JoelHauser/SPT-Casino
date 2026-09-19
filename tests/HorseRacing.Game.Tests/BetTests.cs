namespace HorseRacing.Game.Tests;

/// <summary>
/// What each bet covers, and what it does not.
///
/// **These assert which orders a bet wins on, never how many.** Roulette paid for that
/// lesson: two mutation faults survived its first pass because the tests counted the
/// numbers a bet covered instead of reading them, and a bet that covers the right
/// number of the wrong things passes a counting test every single time. Every
/// assertion below names the finishing order it is talking about.
/// </summary>
public class BetTests
{
    // A finishing order to hang the placings off. Runner 3 won, 5 was second, 1 third.
    private static readonly int[] Order = [3, 5, 1, 8, 2, 7, 4, 6];

    [Fact]
    public void WinIsTheRunnerThatFinishedFirstAndNobodyElse()
    {
        Assert.True(Bet.On(BetKind.Win, 3, 10_000).Covers(Order));

        // Second and third are losers on a win bet. This is the pair that a
        // place-shaped mistake in Covers would get wrong while still looking sane.
        Assert.False(Bet.On(BetKind.Win, 5, 10_000).Covers(Order));
        Assert.False(Bet.On(BetKind.Win, 1, 10_000).Covers(Order));
        Assert.False(Bet.On(BetKind.Win, 6, 10_000).Covers(Order));
    }

    [Fact]
    public void PlaceIsTheFirstTwoHomeAndStopsAtTheSecond()
    {
        Assert.True(Bet.On(BetKind.Place, 3, 10_000).Covers(Order));
        Assert.True(Bet.On(BetKind.Place, 5, 10_000).Covers(Order));

        // Runner 1 was third. An off-by-one in the placings window is exactly this.
        Assert.False(Bet.On(BetKind.Place, 1, 10_000).Covers(Order));
        Assert.False(Bet.On(BetKind.Place, 6, 10_000).Covers(Order));
    }

    [Fact]
    public void ShowIsTheFirstThreeHomeAndStopsAtTheThird()
    {
        Assert.True(Bet.On(BetKind.Show, 3, 10_000).Covers(Order));
        Assert.True(Bet.On(BetKind.Show, 5, 10_000).Covers(Order));
        Assert.True(Bet.On(BetKind.Show, 1, 10_000).Covers(Order));

        // Runner 8 was fourth, and fourth pays nothing anywhere on this board.
        Assert.False(Bet.On(BetKind.Show, 8, 10_000).Covers(Order));
        Assert.False(Bet.On(BetKind.Show, 6, 10_000).Covers(Order));
    }

    [Fact]
    public void ExactaIsOrderedAndTheReverseLoses()
    {
        Assert.True(new Bet(BetKind.Exacta, 3, 5, 10_000).Covers(Order));

        // The single most valuable assertion in this file: 5 then 3 is the same two
        // runners in the wrong order, and it must lose. An exacta settled as a
        // quinella pays this, and every balance check in the repo would still pass.
        Assert.False(new Bet(BetKind.Exacta, 5, 3, 10_000).Covers(Order));

        // Right winner, wrong second.
        Assert.False(new Bet(BetKind.Exacta, 3, 1, 10_000).Covers(Order));

        // Right pair, but they came second and third rather than first and second.
        Assert.False(new Bet(BetKind.Exacta, 5, 1, 10_000).Covers(Order));
    }

    [Fact]
    public void QuinellaIsUnorderedAndPaysBothWaysRound()
    {
        Assert.True(new Bet(BetKind.Quinella, 3, 5, 10_000).Covers(Order));
        Assert.True(new Bet(BetKind.Quinella, 5, 3, 10_000).Covers(Order));

        // Still needs both legs in the first two. 5 and 1 finished second and third.
        Assert.False(new Bet(BetKind.Quinella, 5, 1, 10_000).Covers(Order));
        Assert.False(new Bet(BetKind.Quinella, 3, 8, 10_000).Covers(Order));
    }

    [Fact]
    public void ARunnerNotOnTheCardIsNotAWellFormedBet()
    {
        Assert.False(Bet.On(BetKind.Win, 0, 10_000).IsWellFormed());
        Assert.False(Bet.On(BetKind.Win, 9, 10_000).IsWellFormed());
        Assert.False(Bet.On(BetKind.Win, -1, 10_000).IsWellFormed());
    }

    [Fact]
    public void APairBetNeedsTwoDifferentRunners()
    {
        Assert.False(new Bet(BetKind.Exacta, 4, 4, 10_000).IsWellFormed());
        Assert.False(new Bet(BetKind.Quinella, 4, 4, 10_000).IsWellFormed());
        Assert.False(new Bet(BetKind.Exacta, 4, 0, 10_000).IsWellFormed());
        Assert.False(new Bet(BetKind.Exacta, 4, 9, 10_000).IsWellFormed());

        Assert.True(new Bet(BetKind.Exacta, 4, 5, 10_000).IsWellFormed());
    }

    [Fact]
    public void ASingleRunnerBetMustLeaveTheSecondLegAtZero()
    {
        // Not pedantry. An exacta whose Kind was mistyped as Win would otherwise
        // settle as a win bet and silently throw the second leg away -- the player
        // would be paid, correctly, for a bet they did not place.
        Assert.False(new Bet(BetKind.Win, 3, 5, 10_000).IsWellFormed());
        Assert.True(new Bet(BetKind.Win, 3, 0, 10_000).IsWellFormed());
    }

    /// <summary>
    /// Every spot every board quotes is a bet the window would actually take.
    ///
    /// Checked at all three courses. They offer the same 108 spots by construction, so
    /// this ought to be redundant -- which is exactly why it is worth asserting: a
    /// course that started quoting something unbettable would otherwise be found by a
    /// player rather than by a test.
    /// </summary>
    [Fact]
    public void EveryBetOnEveryBoardIsWellFormed()
    {
        foreach (var price in Tracks.All.SelectMany(Odds.All))
        {
            Assert.True(
                new Bet(price.Kind, price.First, price.Second, 10_000).IsWellFormed(),
                $"the board quotes {price.Kind} {price.First}/{price.Second}, which the window would refuse.");
        }
    }
}
