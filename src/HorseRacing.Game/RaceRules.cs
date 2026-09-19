namespace HorseRacing.Game;

/// <summary>
/// Table rules that are the same wherever the race is run.
///
/// Chips, not currency. The engine takes an int and returns an int and has no idea
/// what a rouble is -- what a chip is worth belongs with the wallet. Keeping that
/// boundary is what makes the rules testable, and it is the same boundary Roulette,
/// Poker and Blackjack draw.
///
/// **The slip ceiling is not here.** It differs per course -- see
/// <see cref="Track.MaxSlip"/> -- because it is arithmetic on that course's longest
/// price rather than a rule about how much the house will take.
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
    /// How many bets may be on the slip at once.
    ///
    /// The board offers 24 single-runner spots, 56 exactas and 28 quinellas: 108 in
    /// all, at every course. This allows every one of them, which is the only limit
    /// that never tells a player their own slip is illegal. It exists to stop a runaway
    /// client filling memory, not to limit anyone's game.
    /// </summary>
    public int MaxBets { get; init; } = 108;

    /// <summary>
    /// Whether a single bet is one the window will take at this course, ignoring the
    /// rest of the slip.
    /// </summary>
    public bool Accepts(Track track, Bet bet)
    {
        ArgumentNullException.ThrowIfNull(track);
        ArgumentNullException.ThrowIfNull(bet);

        if (!bet.IsWellFormed())
        {
            return false;
        }

        if (bet.Stake < MinBet || bet.Stake > track.MaxSlip)
        {
            return false;
        }

        return bet.Stake % Step == 0;
    }
}
