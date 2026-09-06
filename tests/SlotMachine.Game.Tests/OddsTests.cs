namespace SlotMachine.Game.Tests;

/// <summary>
/// What the machine gives back, and whether the sum that says so is right.
///
/// The return to player is the slot's house edge, and it is arithmetic on the reels
/// and the paytable rather than something to be discovered by spinning. These check
/// both that the arithmetic says something sane and that it says the truth.
/// </summary>
public class OddsTests
{
    /// <summary>
    /// **The test that matters.** The closed form in <see cref="Odds"/> is checked
    /// against actually pulling the handle a great many times.
    ///
    /// A formula derived from the same misunderstanding as the code it describes would
    /// agree with itself perfectly, so this deliberately shares nothing with it: it
    /// settles real pulls through <see cref="Machine"/> and averages what they paid.
    ///
    /// Two million pulls puts the standard error of the mean at well under a
    /// percentage point of stake, so a formula that were wrong in any way worth caring
    /// about -- a missing run length, a ways count that multiplied wrongly, a run that
    /// was allowed to start anywhere but the first reel -- would not land this close.
    /// </summary>
    [Fact]
    public void TheComputedReturnMatchesWhatTheMachineActuallyPays()
    {
        const int pulls = 2_000_000;
        const long stake = 100;

        var machine = new Machine(new Random(20260906));
        var paid = 0L;

        for (var i = 0; i < pulls; i++)
        {
            paid += machine.Pull(stake).Paid;
        }

        var measured = (double)paid / (pulls * stake);
        var computed = Odds.ReturnToPlayer();

        Assert.True(
            Math.Abs(measured - computed) < 0.01,
            $"computed {computed:P3} but two million pulls returned {measured:P3}.");
    }

    /// <summary>
    /// The house keeps something, and not too much of it.
    ///
    /// The band is wide on purpose: this is a design choice rather than a law, and the
    /// point of pinning it is that a paytable edit which quietly turns the machine into
    /// a money printer, or into one nobody would play twice, fails here rather than in
    /// somebody's stash.
    /// </summary>
    [Fact]
    public void TheHouseEdgeIsInTheRangeARealMachineWouldUse()
    {
        var edge = Odds.HouseEdge();

        Assert.InRange(edge, 0.02, 0.15);
    }

    /// <summary>
    /// Every symbol pays something. A row of the paytable that contributes nothing is
    /// a prize being advertised and never given.
    /// </summary>
    [Fact]
    public void EverySymbolContributesToTheReturn()
    {
        foreach (var symbol in Paytable.Symbols)
        {
            Assert.True(
                Odds.ReturnFrom(symbol) > 0,
                $"{symbol} never pays anything, so its rows of the paytable are decoration.");
        }
    }

    /// <summary>
    /// Every symbol is on every reel.
    ///
    /// Without this a five of a kind is not rare, it is impossible -- and the paytable
    /// would be advertising a prize that cannot be won. The first draft of the reels
    /// had the top symbol at zero stops on all five.
    /// </summary>
    [Fact]
    public void EverySymbolAppearsOnEveryReel()
    {
        for (var reel = 0; reel < Reels.Count; reel++)
        {
            foreach (var symbol in Paytable.Symbols)
            {
                Assert.True(
                    Reels.Of(reel).Contains(symbol),
                    $"{symbol} has no stop on reel {reel + 1}, so it can never pay five of a kind.");
            }
        }
    }

    /// <summary>
    /// The top symbol is the rarest and the bottom one the most common, on every reel.
    ///
    /// Not a detail: it is the whole shape of the game. A paytable that pays LEDX four
    /// hundred times the stake only works while LEDX is the hardest thing to land.
    /// </summary>
    [Fact]
    public void TheReelsAreOrderedFromCommonToRare()
    {
        for (var reel = 0; reel < Reels.Count; reel++)
        {
            var strip = Reels.Of(reel);

            var bandages = strip.Count(s => s == Symbol.Bandage);
            var ledx = strip.Count(s => s == Symbol.Ledx);

            Assert.True(
                bandages > ledx,
                $"reel {reel + 1} has {bandages} bandages and {ledx} LEDX, which is the wrong way round.");
        }
    }

    /// <summary>
    /// The later reels are meaner than the earlier ones.
    ///
    /// This is what keeps the big prizes rare while the first reels still tease: a long
    /// run has to survive reel five, so reel five holds the fewest good symbols.
    /// </summary>
    [Fact]
    public void TheLastReelIsTheMeanest()
    {
        var first = Odds.ExpectedCount(0, Symbol.Ledx) + Odds.ExpectedCount(0, Symbol.Gpu);
        var last = Odds.ExpectedCount(Reels.Count - 1, Symbol.Ledx) + Odds.ExpectedCount(Reels.Count - 1, Symbol.Gpu);

        Assert.True(last <= first, "the last reel is no meaner than the first, so nothing is holding the top prizes back.");
    }

    /// <summary>Each strip is the length it claims, so the odds are what they look like.</summary>
    [Fact]
    public void EveryStripIsTheDeclaredLength()
    {
        for (var reel = 0; reel < Reels.Count; reel++)
        {
            Assert.Equal(Reels.Length, Reels.Stops(reel));
        }
    }
}
