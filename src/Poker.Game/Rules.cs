namespace Poker.Game;

/// <summary>
/// Table rules for Ultimate Texas Hold'em. Defaults are the standard game.
///
/// Every value here is exposed as mod config eventually, so the engine must never
/// assume a default is in force -- read the rule, do not hardcode the number it
/// usually holds.
///
/// Bet sizes are absent on purpose. The engine takes an int and returns an int and
/// has no idea what a rouble is; what a wallet will accept as an Ante belongs with
/// the wallet, exactly as it does in Blackjack.
/// </summary>
public sealed record Rules
{
    /// <summary>
    /// Pays on the player's own hand, only on a straight or better, and pushes
    /// beneath that rather than losing.
    ///
    /// Swapped for <see cref="Paytable.BlindForValuables"/> when the stake is
    /// something indivisible. That is a per-hand decision made by the caller, the
    /// same way Blackjack passes a natural's payout into <c>Deal</c>: one table
    /// serves every wallet, so the rate cannot live on the table.
    /// </summary>
    public Paytable Blind { get; init; } = Paytable.Blind;

    /// <summary>Side bet on the player's own hand, indifferent to the dealer.</summary>
    public Paytable Trips { get; init; } = Paytable.Trips;

    /// <summary>
    /// Whether the Trips bet is offered at all. Off for indivisible stakes, where a
    /// 50:1 top row has the same problem the Blind's 500:1 does.
    /// </summary>
    public bool OfferTrips { get; init; } = true;

    /// <summary>
    /// What the Play bet costs, as a multiple of the Ante, at each point it can be
    /// made. Pre-flop offers a choice of two; the later streets offer one.
    ///
    /// The shrinking size is the whole game: betting early costs four times the
    /// Ante but is the only way to be paid for a hand that is already good, and
    /// waiting is cheaper but lets the board catch up.
    /// </summary>
    public int PreFlopRaiseLarge { get; init; } = 4;

    public int PreFlopRaiseSmall { get; init; } = 3;

    public int FlopRaise { get; init; } = 2;

    public int RiverRaise { get; init; } = 1;

    /// <summary>
    /// The category the dealer needs to qualify. Below it the Ante pushes rather
    /// than winning, which is the house's compensation for the Blind paytable.
    /// </summary>
    public HandCategory DealerQualifies { get; init; } = HandCategory.Pair;

    /// <summary>
    /// The most seats a table can have, the player's included. The player chooses
    /// how many are filled; this is the ceiling on that choice.
    ///
    /// Five is a table that reads as a table without becoming a crowd. Nothing in
    /// the rules cares -- seats do not interact -- so the real limits are screen
    /// width and the deck: five seats plus the dealer and the board is 17 cards.
    /// </summary>
    public int MaxSeats { get; init; } = 5;

    /// <summary>
    /// The most a hand can return, as a multiple of the Ante, if everything lands.
    ///
    /// Ante at 1:1 returns 2, the largest Play at 1:1 returns twice itself, and the
    /// Blind returns its stake plus the top of its table. Trips is excluded -- it is
    /// staked separately and is not a multiple of the Ante.
    ///
    /// This exists to be read when setting wallet ceilings. A payout has to arrive
    /// as items in a stash, so the ceiling is a question about grid cells, not about
    /// what the house can afford.
    /// </summary>
    public int WorstCaseReturnPerAnte =>
        2 + (2 * PreFlopRaiseLarge) + Blind.Best.Returned(1);
}
