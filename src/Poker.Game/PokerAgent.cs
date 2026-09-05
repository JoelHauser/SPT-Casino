using System.Text.Json.Serialization;

namespace Poker.Game;

/// <summary>The four streets, plus the two states a hand can be in outside them.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum HoldemStreet
{
    PreFlop,
    Flop,
    Turn,
    River,

    /// <summary>Betting is finished and the hand is being paid out.</summary>
    Showdown,

    /// <summary>Nothing in progress. The table is waiting to deal.</summary>
    Idle,
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum HoldemMove
{
    Fold,

    /// <summary>Only when nothing is owed.</summary>
    Check,

    /// <summary>Match the bet, or put the rest of the stack in trying.</summary>
    Call,

    /// <summary>Open the betting, or put it up. Carries the total to be in for.</summary>
    Raise,
}

/// <summary>
/// A decision, and the amount that goes with it.
///
/// <paramref name="To"/> is the **total this seat will have in for the street**, not
/// the extra it is adding. Poker is spoken that way -- "raise to sixty" -- and the
/// difference between the two readings is the single easiest way to build a betting
/// round that silently takes the wrong number of chips.
/// </summary>
public readonly record struct HoldemDecision(HoldemMove Move, int To = 0)
{
    public static HoldemDecision Fold => new(HoldemMove.Fold);

    public static HoldemDecision Check => new(HoldemMove.Check);

    public static HoldemDecision Call => new(HoldemMove.Call);

    public static HoldemDecision RaiseTo(int to) => new(HoldemMove.Raise, to);

    public override string ToString() =>
        Move == HoldemMove.Raise ? $"raises to {To}" : Move.ToString().ToLowerInvariant() + "s";
}

/// <summary>
/// What a seat may legally do right now, and for how much.
/// </summary>
/// <param name="Moves">Everything legal. A move outside this is refused.</param>
/// <param name="ToCall">Chips needed to match the current bet. Zero means checking is free.</param>
/// <param name="MinRaiseTo">
/// The smallest legal raise, as a total for the street. A raise has to be at least as
/// large as the last one -- otherwise a player could grind out a round in one-chip
/// increments and never let it close.
/// </param>
/// <param name="MaxRaiseTo">Everything this seat has. No-limit, so this is the stack.</param>
public sealed record BettingOptions(
    [property: JsonConverter(typeof(StringEnumListConverter<HoldemMove>))]
    IReadOnlyList<HoldemMove> Moves,
    int ToCall,
    int MinRaiseTo,
    int MaxRaiseTo);

/// <summary>
/// Everything a bot is allowed to know when it decides.
///
/// Deliberately narrow, and the narrowness is the anti-cheat. It carries this seat's
/// own cards, the board that is showing, the stacks, the betting so far and what is
/// legal -- and nothing else. There is no route from here to another seat's cards or
/// to the undealt deck, so a bot cannot cheat with information it was never handed.
/// </summary>
/// <param name="Seat">The bot's own seat, with its own hole cards.</param>
/// <param name="Street">Which street is being bet.</param>
/// <param name="Community">The board, as far as it has been turned over.</param>
/// <param name="Options">What is legal, and for how much.</param>
/// <param name="Pot">Everything in the middle, including this street's bets.</param>
/// <param name="Opponents">
/// The other seats as they can be seen from across the table: stacks, what they have
/// put in, and whether they are still in. Never their cards.
/// </param>
/// <param name="SeatsToActAfter">
/// How many live seats still have to speak after this one on this street. Zero means
/// acting last, which is worth real money -- everyone else has already told you
/// something and you have told them nothing.
///
/// Given as a count rather than as the button's position because that is the thing
/// position actually *is*, once folds are taken into account. A seat two to the
/// button's left is nominally early and is last to act if everyone between has
/// folded.
/// </param>
/// <param name="Rules">Blinds and limits.</param>
public readonly record struct PokerContext(
    HoldemSeat Seat,
    HoldemStreet Street,
    IReadOnlyList<Card> Community,
    BettingOptions Options,
    int Pot,
    IReadOnlyList<OpponentView> Opponents,
    int SeatsToActAfter,
    HoldemRules Rules);

/// <summary>One other seat, as much of it as anybody at the table can see.</summary>
public readonly record struct OpponentView(
    int Index,
    string Name,
    int Stack,
    int CommittedThisStreet,
    bool Folded,
    bool IsAllIn);

/// <summary>How a hand finished, from one seat's point of view.</summary>
/// <param name="Net">Chips won or lost. Negative is a losing hand.</param>
/// <param name="Stack">What is left in front of them afterwards.</param>
/// <param name="BuyIn">What a full stack looks like, so a loss can be judged as a share of one.</param>
/// <param name="Folded">Whether they gave the hand up rather than losing it at showdown.</param>
public readonly record struct HandOutcome(int Net, int Stack, int BuyIn, bool Folded);

/// <summary>
/// Where a bot's decision comes from.
///
/// One instance per seat, not one for the table. The parked UTH table took a single
/// agent for every seat, which made four seat-mates one person wearing four names --
/// and personality is the entire point of these.
/// </summary>
public interface IPokerAgent
{
    HoldemDecision Decide(PokerContext context);

    /// <summary>
    /// Told to every seat when a hand finishes, including seats that folded early.
    ///
    /// This is the only way a bot can carry anything from one hand to the next, and
    /// without it a seat has no memory: it plays the thousandth hand exactly as it
    /// played the first, which is the thing that most gives a table away as
    /// machinery. Ignoring it is a legitimate choice for a simple agent, so it does
    /// nothing by default.
    /// </summary>
    void HandEnded(HandOutcome outcome)
    {
    }
}
