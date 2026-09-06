namespace SlotMachine.Game;

/// <summary>
/// What can land on a reel.
///
/// Nine symbols in three tiers. The tiers are not decoration: a slot's whole feel is
/// how often something *nearly* happens, and that comes from the low symbols being
/// common enough to line up constantly while the high ones almost never do.
///
/// Ordered low to high so the paytable reads down the enum.
/// </summary>
public enum Symbol
{
    /// <summary>Low. The filler that makes near misses happen.</summary>
    Bandage,

    Crackers,

    Round,

    /// <summary>Mid.</summary>
    Screwdriver,

    Wires,

    GreenBattery,

    /// <summary>High.</summary>
    GpCoin,

    Gpu,

    /// <summary>The top symbol. One stop on every reel and no more.</summary>
    Ledx,
}

/// <summary>
/// The five reels, as the strips of symbols they actually are.
///
/// **A slot machine's odds live here and nowhere else.** There is no random number
/// deciding whether you win; there are five independent stop positions, and what they
/// pay falls out of what is written on the strips. Changing a payout changes the
/// return; changing a strip changes it more.
///
/// Reel one is kindest and reel five is meanest, which is the oldest trick in the
/// trade: a five-symbol win has to survive the last reel, so thinning the good symbols
/// there keeps the top prizes rare while the first three reels still tease constantly.
///
/// **Every symbol appears at least once on every reel.** Otherwise its five-of-a-kind
/// is not merely rare, it is impossible, and a paytable would be advertising a prize
/// that cannot be won. The first draft of this file had exactly that fault: the LEDX
/// row was zero everywhere.
/// </summary>
public static class Reels
{
    /// <summary>How many reels.</summary>
    public const int Count = 5;

    /// <summary>Three rows visible, which is what makes 243 ways rather than one line.</summary>
    public const int Rows = 3;

    /// <summary>
    /// Stops per reel. Equal across all five, which the odds do not require but the
    /// arithmetic in <see cref="Odds"/> is far easier to check when they are.
    /// </summary>
    public const int Length = 30;

    private static readonly Symbol[][] Strips =
    [
        //       bandage crackers round screw wires batt  gp  gpu ledx
        Strip(5, 5, 4, 4, 4, 3, 2, 2, 1),
        Strip(5, 5, 4, 4, 4, 3, 2, 2, 1),
        Strip(5, 5, 5, 4, 4, 3, 2, 1, 1),
        Strip(6, 5, 5, 4, 4, 3, 1, 1, 1),
        Strip(6, 6, 5, 4, 4, 2, 1, 1, 1),
    ];

    /// <summary>The strip on one reel, from stop zero.</summary>
    public static IReadOnlyList<Symbol> Of(int reel) => Strips[reel];

    /// <summary>How many stops that reel has.</summary>
    public static int Stops(int reel) => Strips[reel].Length;

    /// <summary>
    /// The three symbols showing on a reel when it stops at a position.
    ///
    /// Wraps, because a reel is a loop. Stopping at the last stop shows the last
    /// symbol and then the first two, exactly as a physical reel would.
    /// </summary>
    public static Symbol[] Window(int reel, int stop)
    {
        var strip = Strips[reel];
        var window = new Symbol[Rows];

        for (var row = 0; row < Rows; row++)
        {
            var at = ((stop + row) % strip.Length + strip.Length) % strip.Length;
            window[row] = strip[at];
        }

        return window;
    }

    /// <summary>
    /// One strip, built from how many of each symbol it holds.
    ///
    /// Written as counts rather than as a hand-typed list of thirty symbols, because a
    /// list is impossible to check by eye and a count is impossible to get wrong by
    /// one.
    ///
    /// The order within a strip does not affect the odds at all -- only how the reel
    /// looks while it turns -- so the symbols are dealt out at a stride rather than
    /// left in runs, which stops identical symbols showing as bands of one colour.
    /// Seven and thirty share no factor, so the stride visits every stop before it
    /// repeats.
    /// </summary>
    private static Symbol[] Strip(
        int bandage, int crackers, int round, int screwdriver,
        int wires, int battery, int gp, int gpu, int ledx)
    {
        var counts = new (Symbol Symbol, int Count)[]
        {
            (Symbol.Bandage, bandage),
            (Symbol.Crackers, crackers),
            (Symbol.Round, round),
            (Symbol.Screwdriver, screwdriver),
            (Symbol.Wires, wires),
            (Symbol.GreenBattery, battery),
            (Symbol.GpCoin, gp),
            (Symbol.Gpu, gpu),
            (Symbol.Ledx, ledx),
        };

        var pool = new List<Symbol>();

        foreach (var (symbol, count) in counts)
        {
            if (count < 1)
            {
                throw new ArgumentException(
                    $"{symbol} has no stop on this reel, so its five of a kind could never be won.");
            }

            for (var i = 0; i < count; i++)
            {
                pool.Add(symbol);
            }
        }

        if (pool.Count != Length)
        {
            throw new ArgumentException(
                $"a strip must have {Length} stops; these counts make {pool.Count}.");
        }

        // A taken[] rather than checking the strip for an empty value: default(Symbol)
        // is Bandage, a real symbol, so an unwritten slot and a bandage are the same
        // thing to look at. The first draft of this method used that as its marker.
        var strip = new Symbol[pool.Count];
        var taken = new bool[pool.Count];
        var at = 0;

        foreach (var symbol in pool)
        {
            while (taken[at])
            {
                at = (at + 1) % pool.Count;
            }

            strip[at] = symbol;
            taken[at] = true;
            at = (at + 7) % pool.Count;
        }

        return strip;
    }
}
