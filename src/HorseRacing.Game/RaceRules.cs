namespace HorseRacing.Game;

/// <summary>
/// Table rules.
///
/// Chips, not currency. The engine takes an int and returns an int and has no idea
/// what a rouble is -- what a chip is worth belongs with the wallet. Keeping that
/// boundary is what makes the rules testable, and it is the same boundary Roulette,
/// Poker and Blackjack draw.
/// </summary>
public sealed record RaceRules
{
    /// <summary>
    /// The smallest bet the window takes.
    ///
    /// Ten thousand, the same as roulette's, because a player moving between the two
    /// tables should not have to relearn what a small bet is.
    /// </summary>
    public int MinBet { get; init; } = 10_000;

    /// <summary>
    /// The step every bet moves in.
    ///
    /// Five thousand, matching roulette -- and for the reason roulette had to write
    /// down twice: the step is not the minimum. A stake stepper that moves in units of
    /// the minimum cannot reach 25,000, and a table that refuses a stake a player can
    /// see on their own slip is a table that looks broken.
    /// </summary>
    public int Step { get; init; } = 5_000;

    /// <summary>
    /// The most that may ride on one bet.
    ///
    /// **This is not a table limit, it is an arithmetic one**, exactly as roulette's
    /// is -- there is deliberately no house maximum here. What makes this number much
    /// smaller than roulette's fifty million is that this game's longest price is much
    /// longer. Roulette's best is 36 to 1. The longest exacta on this card pays a
    /// little over 750 for one, because it asks for the two slowest runners home in
    /// order, and the engine counts chips in an <c>int</c>.
    ///
    /// Two million times that price is about 1.5 billion, comfortably inside
    /// <see cref="int.MaxValue"/>. Three million would not be. <c>OddsTests</c>
    /// asserts the real longest price on the real card against this number rather
    /// than trusting the paragraph above, because the moment somebody edits a weight
    /// in <see cref="Field"/> the arithmetic here changes and the prose does not.
    ///
    /// Raising it means moving the engine to <c>long</c> first, and
    /// <see cref="MaxTotalStake"/> has the same problem.
    /// </summary>
    public int MaxBet { get; init; } = 2_000_000;

    /// <summary>
    /// The most that may be on the slip in total.
    ///
    /// Bounded by the same sum. Every bet can return up to the longest price times its
    /// own stake, so the whole slip can return the longest price times this -- and the
    /// slip's total is what the server hands to the bank as one number.
    /// </summary>
    public int MaxTotalStake { get; init; } = 2_000_000;

    /// <summary>
    /// How many bets may be on the slip at once.
    ///
    /// The board offers 24 single-runner spots, 56 exactas and 28 quinellas: 108 in
    /// all. This allows every one of them, which is the only limit that never tells a
    /// player their own slip is illegal. It exists to stop a runaway client filling
    /// memory, not to limit anyone's game.
    /// </summary>
    public int MaxBets { get; init; } = 108;

    /// <summary>
    /// The ceiling for a given bet. The same for every kind, because the house is not
    /// protecting itself here -- see <see cref="MaxBet"/>.
    /// </summary>
    public int MaxFor(BetKind kind) => MaxBet;

    /// <summary>
    /// Whether a single bet is one the window will take, ignoring the rest of the slip.
    /// </summary>
    public bool Accepts(Bet bet)
    {
        ArgumentNullException.ThrowIfNull(bet);

        if (!bet.IsWellFormed())
        {
            return false;
        }

        if (bet.Stake < MinBet || bet.Stake > MaxFor(bet.Kind))
        {
            return false;
        }

        return bet.Stake % Step == 0;
    }
}
