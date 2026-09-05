namespace Roulette.Game;

/// <summary>
/// Table rules.
///
/// Chips, not currency. The engine takes an int and returns an int and has no idea
/// what a rouble is -- what a chip is worth belongs with the wallet. Keeping that
/// boundary is what makes the rules testable, and it is the same boundary Poker and
/// Blackjack draw.
/// </summary>
public sealed record RouletteRules
{
    /// <summary>
    /// Single zero by default. A European wheel is a 2.70% house edge against an
    /// American wheel's 5.26%, and there is no reason to give a single player the
    /// worse of the two.
    /// </summary>
    public WheelKind Wheel { get; init; } = WheelKind.European;

    /// <summary>
    /// The smallest bet the table takes, which is the smallest chip.
    /// </summary>
    public int MinBet { get; init; } = 10_000;

    /// <summary>
    /// The step every bet moves in.
    ///
    /// **Not the same number as <see cref="MinBet"/>, and conflating them cost the
    /// 25,000 chip.** The tray holds 10k, 25k, 50k, 100k, 500k and 1M. Requiring
    /// every stake to be a whole number of the smallest chip sounds right and is not:
    /// 25,000 is not a multiple of 10,000, so the 25k chip was refused by the table
    /// every time it was put down, and no combination of chips could be corrected
    /// into a legal bet either.
    ///
    /// The step is the greatest common divisor of the denominations -- 5,000 -- which
    /// is the largest number every reachable stake is a multiple of. Below the
    /// minimum it is unreachable anyway, since there is no 5,000 chip to place.
    ///
    /// A stake that cannot be built out of chips is one the table can never show
    /// honestly, which is the lesson Poker learned by drawing a 30,000 pot as a 25k
    /// chip with 5,000 stranded. This is the same lesson from the other end.
    /// </summary>
    public int Step { get; init; } = 5_000;

    /// <summary>
    /// The most that may ride on one bet.
    ///
    /// **This is not a table limit, it is an arithmetic one.** There is deliberately no
    /// house maximum here -- a player betting their whole stash on a single number is
    /// the point of the game, not something to be protected from. What is left is the
    /// only ceiling that cannot be argued with: a straight-up bet returns thirty-six
    /// times its stake, and the engine counts chips in an <c>int</c>, so a stake above
    /// about 59 million would overflow on the way back and pay out a negative number.
    ///
    /// Fifty million leaves room under that and is far past any stash the game holds,
    /// so in practice nothing is capped. Raising it means moving the whole engine to
    /// <c>long</c> first -- <see cref="MaxTotalStake"/> has the same problem.
    /// </summary>
    public int MaxBet { get; init; } = 50_000_000;

    /// <summary>
    /// The most that may be on the cloth in total.
    ///
    /// Bounded for the same reason and by the same sum: every bet can return
    /// thirty-six times its stake, so the whole cloth can return thirty-six times this.
    /// Above about 59 million that total stops fitting in an int.
    /// </summary>
    public int MaxTotalStake { get; init; } = 50_000_000;

    /// <summary>
    /// How many bets may be on the cloth at once.
    ///
    /// A full cloth offers about a hundred and fifty spots and a player may reasonably
    /// want a lot of them covered, so this is generous. It exists to stop a runaway
    /// client filling memory, not to limit anyone's game.
    /// </summary>
    public int MaxBets { get; init; } = 150;

    /// <summary>
    /// The ceiling for a given bet. The same for every kind, because the house is not
    /// protecting itself here -- see <see cref="MaxBet"/>.
    /// </summary>
    public int MaxFor(BetKind kind) => MaxBet;
}
