namespace Poker.Game;

/// <summary>
/// Table rules for a no-limit hold'em cash game.
///
/// Chips, not currency. The engine takes an int and returns an int and has no idea
/// what a rouble is -- what a chip is worth, and what a buy-in costs in a wallet,
/// belongs with the wallet. Keeping that boundary is what makes the game testable.
/// </summary>
public sealed record HoldemRules
{
    public int SmallBlind { get; init; } = 10_000;

    public int BigBlind { get; init; } = 20_000;

    /// <summary>
    /// What everyone sits down with, in chips. A hundred big blinds is the usual
    /// cash-game stack: deep enough that position and bet sizing matter, shallow
    /// enough that all-ins actually happen.
    ///
    /// The scale is set by the chips the table is drawn with: the smallest is 10,000,
    /// so that is the small blind and every stake above it is a whole number of them.
    /// A blind that cannot be made out of chips is one the table can never show
    /// honestly.
    /// </summary>
    public int BuyIn { get; init; } = 1_000_000;

    /// <summary>
    /// The most seats a table can have, the player's included. The player chooses how
    /// many are filled; this is the ceiling on that choice.
    ///
    /// Five keeps a table feeling like a table without becoming a crowd, and the deck
    /// is nowhere near a constraint: five seats plus a five-card board is fifteen
    /// cards.
    /// </summary>
    public int MaxSeats { get; init; } = 5;
}
