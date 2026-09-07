namespace SlotMachine.Game;

/// <summary>
/// What each run of matching symbols pays, as a multiple of the stake.
///
/// ## 243 ways, not paylines
///
/// There are no lines to choose and none to draw. A win is any symbol appearing on
/// **three or more adjacent reels starting from the leftmost**, in any row, and it
/// pays once for every distinct path through those symbols. Three rows on five reels
/// is 3^5 = 243 possible paths, which is where the number comes from.
///
/// The practical difference from paylines is that a symbol landing twice on one reel
/// doubles every win running through it, so the counts multiply: two on reel one,
/// one on reel two, three on reel three is 2 x 1 x 3 = six ways, all paid.
///
/// ## Read as a multiple of the whole stake
///
/// One spin buys all 243 ways, so the payout is a multiple of what was staked rather
/// than of some per-line fraction. A five-symbol keycard at 1000x on a 50,000 rouble spin
/// pays 20,000,000.
/// </summary>
public static class Paytable
{
    /// <summary>The shortest run that pays. Two of a kind is a near miss, not a win.</summary>
    public const int MinRun = 3;

    /// <summary>
    /// Multiplier for a symbol and a run length.
    ///
    /// **These are not taste, they are solved.** Every symbol's contribution to the
    /// return is `multiplier x expected ways x probability the run stops there`, and
    /// <see cref="Odds"/> computes all three exactly, so the multipliers were chosen to
    /// land the return where a real machine sits rather than picked and hoped for.
    ///
    /// The first draft was picked and hoped for. It paid **681%** -- the low symbols
    /// looked modest at payline scale, but a ways win multiplies by how many times the
    /// symbol landed on each reel, and the cheapest symbol alone was giving back
    /// three times the stake. That is what a computed return is for.
    ///
    /// Low symbols pay very little, which surprises people reading a paytable and is
    /// exactly right for 243 ways: they land constantly, so a five of a kind on the
    /// commonest symbol is worth about what it cost.
    /// </summary>
    public static int Of(Symbol symbol, int run) => run switch
    {
        3 => symbol switch
        {
            Symbol.Cola => 1,
            Symbol.Salewa => 1,
            Symbol.Moonshine => 1,
            Symbol.Tetriz => 1,
            Symbol.Watch => 1,
            Symbol.Rooster => 2,
            Symbol.Gpu => 5,
            Symbol.Bitcoin => 10,
            Symbol.Keycard => 25,
            _ => 0,
        },
        4 => symbol switch
        {
            Symbol.Cola => 1,
            Symbol.Salewa => 1,
            Symbol.Moonshine => 2,
            Symbol.Tetriz => 2,
            Symbol.Watch => 2,
            Symbol.Rooster => 4,
            Symbol.Gpu => 20,
            Symbol.Bitcoin => 50,
            Symbol.Keycard => 150,
            _ => 0,
        },
        5 => symbol switch
        {
            Symbol.Cola => 1,
            Symbol.Salewa => 2,
            Symbol.Moonshine => 2,
            Symbol.Tetriz => 5,
            Symbol.Watch => 5,
            Symbol.Rooster => 12,
            Symbol.Gpu => 80,
            Symbol.Bitcoin => 250,
            Symbol.Keycard => 1000,
            _ => 0,
        },
        _ => 0,
    };

    /// <summary>Every symbol, in paytable order.</summary>
    public static IReadOnlyList<Symbol> Symbols { get; } =
        [.. Enum.GetValues<Symbol>()];
}
