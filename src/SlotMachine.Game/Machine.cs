namespace SlotMachine.Game;

/// <summary>One symbol paying across a run of reels, and what it was worth.</summary>
/// <param name="Symbol">The symbol that matched.</param>
/// <param name="Reels">How many adjacent reels it ran across, from the left.</param>
/// <param name="Ways">
/// How many distinct paths it paid on: the counts on each reel of the run, multiplied.
/// Two on the first reel and one on each of the next two is two ways, not one.
/// </param>
/// <param name="Paid">Stake times the multiplier times the ways.</param>
public sealed record Win(Symbol Symbol, int Reels, int Ways, long Paid);

/// <summary>The result of one pull, settled.</summary>
/// <param name="Stops">Where each reel stopped. What the client animates to.</param>
/// <param name="Grid">
/// What is showing, as [reel][row]. Sent so the client draws the machine's own answer
/// rather than working one out from the stops and possibly disagreeing with it.
/// </param>
/// <param name="Wins">Every symbol that paid, longest run first.</param>
/// <param name="Staked">What the pull cost.</param>
/// <param name="Paid">What came back. Zero on a loss; the stake is not returned.</param>
public sealed record Pull(
    IReadOnlyList<int> Stops,
    IReadOnlyList<IReadOnlyList<Symbol>> Grid,
    IReadOnlyList<Win> Wins,
    long Staked,
    long Paid)
{
    /// <summary>Up or down on the pull. Negative is the house winning.</summary>
    public long Profit => Paid - Staked;
}

/// <summary>
/// Five reels, three rows, 243 ways.
///
/// The machine holds no money and knows nothing about currency: it takes a stake as a
/// number and returns what that stake won. What a rouble is belongs with the wallet,
/// which is the same boundary the other three tables draw and the reason this is
/// testable without a server.
///
/// **The result is decided here and nowhere else.** The client is handed the stop
/// positions and the grid and animates towards them; it never decides where a reel
/// lands. A slot that let the client pick would be a slot that let the client pick its
/// own jackpot.
/// </summary>
public sealed class Machine(Random? rng = null)
{
    private readonly Random _rng = rng ?? new Random();

    /// <summary>
    /// Pulls the handle.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The stake is not positive.</exception>
    public Pull Pull(long stake)
    {
        if (stake <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(stake), stake, "A pull needs a stake on it.");
        }

        var stops = new int[Reels.Count];

        for (var reel = 0; reel < Reels.Count; reel++)
        {
            stops[reel] = _rng.Next(Reels.Stops(reel));
        }

        return Settle(stops, stake);
    }

    /// <summary>
    /// Test seam: settle against stops chosen rather than spun, the same idea as a
    /// stacked deck. Internal because only a machine may decide a real result.
    /// </summary>
    internal Pull StopAt(IReadOnlyList<int> stops, long stake) => Settle([.. stops], stake);

    /// <summary>
    /// Works out what a set of stops paid.
    ///
    /// Static and pure, which is what lets <see cref="Odds"/> check the return of the
    /// whole machine without spinning it once.
    /// </summary>
    internal static Pull Settle(int[] stops, long stake)
    {
        var grid = new Symbol[Reels.Count][];

        for (var reel = 0; reel < Reels.Count; reel++)
        {
            grid[reel] = Reels.Window(reel, stops[reel]);
        }

        var wins = new List<Win>();

        foreach (var symbol in Paytable.Symbols)
        {
            // How many of this symbol show on each reel. A ways win multiplies these
            // together, so a symbol landing twice on one reel doubles everything
            // running through it.
            var onReel = new int[Reels.Count];

            for (var reel = 0; reel < Reels.Count; reel++)
            {
                foreach (var showing in grid[reel])
                {
                    if (showing == symbol)
                    {
                        onReel[reel]++;
                    }
                }
            }

            // The run has to start at reel one. A symbol filling reels two to five
            // pays nothing at all, which is the rule that stops a slot paying on
            // almost every spin.
            var run = 0;

            while (run < Reels.Count && onReel[run] > 0)
            {
                run++;
            }

            if (run < Paytable.MinRun)
            {
                continue;
            }

            var multiplier = Paytable.Of(symbol, run);

            if (multiplier <= 0)
            {
                continue;
            }

            var ways = 1;

            for (var reel = 0; reel < run; reel++)
            {
                ways *= onReel[reel];
            }

            wins.Add(new Win(symbol, run, ways, stake * multiplier * ways));
        }

        // Longest run first, then biggest, so the client can read the list top down
        // and show the best thing that happened.
        var ordered = wins
            .OrderByDescending(w => w.Reels)
            .ThenByDescending(w => w.Paid)
            .ToList();

        return new Pull(
            stops,
            [.. grid.Select(IReadOnlyList<Symbol> (r) => r)],
            ordered,
            stake,
            ordered.Sum(w => w.Paid));
    }
}
