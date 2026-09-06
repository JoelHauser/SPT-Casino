namespace SlotMachine.Game.Tests;

/// <summary>
/// How a pull settles: what counts as a win, what does not, and what it is worth.
/// </summary>
public class MachineTests
{
    /// <summary>
    /// A run has to start on the first reel.
    ///
    /// The rule that stops a slot paying on nearly every spin. Four LEDX on reels two
    /// to five is worth nothing at all, and a player who does not know that will think
    /// the machine robbed them.
    /// </summary>
    [Fact]
    public void ARunThatDoesNotStartOnTheFirstReelPaysNothing()
    {
        var machine = new Machine();

        // Find a stop on reel one showing no LEDX, and stops on the rest that do.
        var first = StopWithout(0, Symbol.Ledx);
        var rest = Enumerable.Range(1, Reels.Count - 1).Select(r => StopWith(r, Symbol.Ledx));

        var pull = machine.StopAt([first, .. rest], 1_000);

        Assert.DoesNotContain(pull.Wins, w => w.Symbol == Symbol.Ledx);
    }

    /// <summary>Two of a kind is a near miss, not a win.</summary>
    [Fact]
    public void TwoOfAKindPaysNothing()
    {
        Assert.Equal(0, Paytable.Of(Symbol.Ledx, 2));
        Assert.Equal(0, Paytable.Of(Symbol.Bandage, 2));
        Assert.Equal(3, Paytable.MinRun);
    }

    /// <summary>
    /// A symbol landing more than once on a reel multiplies every win running through
    /// it. That is the whole difference between 243 ways and one payline, so it is
    /// worth a test of its own rather than trusting the arithmetic in passing.
    /// </summary>
    [Fact]
    public void RepeatsOnOneReelMultiplyTheWin()
    {
        var machine = new Machine();
        var stops = Enumerable.Range(0, Reels.Count).Select(r => StopWith(r, Symbol.Bandage)).ToArray();

        var pull = machine.StopAt(stops, 1_000);
        var win = Assert.Single(pull.Wins.Where(w => w.Symbol == Symbol.Bandage));

        var expectedWays = 1;

        for (var reel = 0; reel < win.Reels; reel++)
        {
            expectedWays *= Reels.Window(reel, stops[reel]).Count(s => s == Symbol.Bandage);
        }

        Assert.Equal(expectedWays, win.Ways);
        Assert.Equal(1_000L * Paytable.Of(Symbol.Bandage, win.Reels) * win.Ways, win.Paid);
    }

    /// <summary>What is paid is the sum of what each symbol paid, and nothing else.</summary>
    [Fact]
    public void ThePayoutIsTheSumOfTheWins()
    {
        var machine = new Machine(new Random(7));

        for (var i = 0; i < 5_000; i++)
        {
            var pull = machine.Pull(500);

            Assert.Equal(pull.Wins.Sum(w => w.Paid), pull.Paid);
        }
    }

    /// <summary>
    /// The stake is not handed back on top of a win. A three-of-a-kind at 2x pays
    /// twice the stake in total, not the stake plus twice.
    /// </summary>
    [Fact]
    public void TheStakeIsNotReturnedOnTopOfAWin()
    {
        var machine = new Machine();
        var stops = Enumerable.Range(0, Reels.Count).Select(r => StopWith(r, Symbol.Ledx)).ToArray();

        var pull = machine.StopAt(stops, 1_000);
        var win = pull.Wins.First(w => w.Symbol == Symbol.Ledx);

        // The LEDX win itself, not the whole pull: those stops light other symbols up
        // as well, and asserting on the total was this test failing for the wrong
        // reason rather than finding anything.
        Assert.Equal(1_000L * Paytable.Of(Symbol.Ledx, 5) * win.Ways, win.Paid);
        Assert.Equal(pull.Paid - 1_000, pull.Profit);
    }

    /// <summary>A pull with nothing on it is refused rather than spun for free.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-100_000)]
    public void APullNeedsAStake(long stake)
    {
        var machine = new Machine();

        Assert.Throws<ArgumentOutOfRangeException>(() => machine.Pull(stake));
    }

    /// <summary>
    /// The grid the client is sent matches the stops it is told to animate to. They
    /// travel together and a client that drew its own would be free to disagree.
    /// </summary>
    [Fact]
    public void TheGridMatchesTheStops()
    {
        var machine = new Machine(new Random(11));

        for (var i = 0; i < 500; i++)
        {
            var pull = machine.Pull(100);

            for (var reel = 0; reel < Reels.Count; reel++)
            {
                Assert.Equal(Reels.Window(reel, pull.Stops[reel]), pull.Grid[reel]);
            }
        }
    }

    /// <summary>Every reel stops somewhere on its own strip.</summary>
    [Fact]
    public void EveryStopIsOnItsStrip()
    {
        var machine = new Machine(new Random(3));

        for (var i = 0; i < 2_000; i++)
        {
            var pull = machine.Pull(100);

            for (var reel = 0; reel < Reels.Count; reel++)
            {
                Assert.InRange(pull.Stops[reel], 0, Reels.Stops(reel) - 1);
            }
        }
    }

    // ------------------------------------------------------------------ helpers

    private static int StopWith(int reel, Symbol symbol)
    {
        for (var stop = 0; stop < Reels.Stops(reel); stop++)
        {
            if (Reels.Window(reel, stop).Contains(symbol))
            {
                return stop;
            }
        }

        throw new InvalidOperationException($"reel {reel} never shows {symbol}.");
    }

    private static int StopWithout(int reel, Symbol symbol)
    {
        for (var stop = 0; stop < Reels.Stops(reel); stop++)
        {
            if (!Reels.Window(reel, stop).Contains(symbol))
            {
                return stop;
            }
        }

        throw new InvalidOperationException($"reel {reel} always shows {symbol}.");
    }
}
