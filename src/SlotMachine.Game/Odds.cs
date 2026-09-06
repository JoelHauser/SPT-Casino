namespace SlotMachine.Game;

/// <summary>What one symbol gives back, per unit staked.</summary>
/// <param name="Symbol">The symbol.</param>
/// <param name="Return">Its share of the return to player, as a fraction of the stake.</param>
public sealed record SymbolReturn(Symbol Symbol, double Return);

/// <summary>
/// What the machine gives back, computed rather than measured.
///
/// This is the slot's equivalent of roulette's 2.70%: a number that falls out of the
/// reels and the paytable by arithmetic, not something to be discovered by spinning it
/// a million times and hoping the average settles. A slot whose return is only known
/// approximately is a slot whose house edge nobody actually knows.
///
/// ## The arithmetic, since it is not obvious
///
/// A ways win pays `multiplier x (counts on each reel of the run, multiplied)`. For a
/// symbol to pay across exactly the first `L` reels, every one of those reels must show
/// at least one of it and reel `L + 1` must show none.
///
/// The reels are independent, and a count of zero contributes zero to a product, so
/// `E[count x 1{count > 0}]` is just `E[count]`. That collapses the whole thing to:
///
///     E[win from symbol s] = SUM over L of
///         pay(s, L) x PRODUCT of E[count on reel i] for i &lt; L
///                   x P(reel L shows none of s)        (or 1, when L is five)
///
/// Both `E[count]` and `P(none)` are exact and cheap: a reel has thirty stops, so both
/// are found by looking at all thirty. No enumeration of the 24,300,000 combinations
/// of five reels is needed, and no simulation.
///
/// `SlotMachine.Game.Tests` checks this against a Monte Carlo run anyway, because a
/// formula that is wrong in the same way as the code it describes proves nothing.
/// </summary>
public static class Odds
{
    /// <summary>
    /// Expected number of this symbol showing on this reel, per spin.
    ///
    /// Every stop on the strip appears in exactly <see cref="Reels.Rows"/> of the
    /// windows, so this is the symbol's share of the strip times the rows -- but it is
    /// computed by walking the stops rather than by that shortcut, so it stays right
    /// if the window ever stops being a simple run of consecutive stops.
    /// </summary>
    public static double ExpectedCount(int reel, Symbol symbol)
    {
        var stops = Reels.Stops(reel);
        var total = 0;

        for (var stop = 0; stop < stops; stop++)
        {
            foreach (var showing in Reels.Window(reel, stop))
            {
                if (showing == symbol)
                {
                    total++;
                }
            }
        }

        return (double)total / stops;
    }

    /// <summary>Probability that this reel shows none of this symbol.</summary>
    public static double ProbabilityOfNone(int reel, Symbol symbol)
    {
        var stops = Reels.Stops(reel);
        var empty = 0;

        for (var stop = 0; stop < stops; stop++)
        {
            if (!Reels.Window(reel, stop).Contains(symbol))
            {
                empty++;
            }
        }

        return (double)empty / stops;
    }

    /// <summary>What one symbol contributes to the return, per unit staked.</summary>
    public static double ReturnFrom(Symbol symbol)
    {
        var total = 0.0;

        for (var run = Paytable.MinRun; run <= Reels.Count; run++)
        {
            var multiplier = Paytable.Of(symbol, run);

            if (multiplier <= 0)
            {
                continue;
            }

            var ways = 1.0;

            for (var reel = 0; reel < run; reel++)
            {
                ways *= ExpectedCount(reel, symbol);
            }

            // A run of five needs nothing after it; a shorter one has to be stopped by
            // the next reel, or it would be counted again as the longer run.
            var stopped = run == Reels.Count ? 1.0 : ProbabilityOfNone(run, symbol);

            total += multiplier * ways * stopped;
        }

        return total;
    }

    /// <summary>
    /// The return to player: what comes back per unit staked, across every symbol.
    ///
    /// One minus this is the house edge, and unlike a card game it is the same on every
    /// pull regardless of how anybody plays. There is nothing to play well at.
    /// </summary>
    public static double ReturnToPlayer() =>
        Paytable.Symbols.Sum(ReturnFrom);

    /// <summary>What the house keeps, per unit staked.</summary>
    public static double HouseEdge() => 1.0 - ReturnToPlayer();

    /// <summary>Every symbol's share of the return, biggest first.</summary>
    public static IReadOnlyList<SymbolReturn> Breakdown() =>
        [.. Paytable.Symbols
            .Select(s => new SymbolReturn(s, ReturnFrom(s)))
            .OrderByDescending(s => s.Return)];
}
