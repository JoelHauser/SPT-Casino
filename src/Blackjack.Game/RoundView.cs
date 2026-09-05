using System.Text.Json.Serialization;

namespace Blackjack.Game;

// These three cross the wire. Without the converter System.Text.Json writes their
// integer values, so a client sees phase 1 rather than "PlayerTurn" -- which reads
// as a magic number, compares equal to nothing sensible, and fails silently rather
// than loudly. The client plugin should never have to know the ordering.

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum RoundPhase
{
    /// <summary>No hand in progress -- the table is waiting for a bet.</summary>
    AwaitingBet,
    PlayerTurn,
    DealerTurn,
    Settled,
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum PlayerAction
{
    Hit,
    Stand,
    Double,
    Split,
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum HandOutcome
{
    Pending,
    Win,
    Lose,
    Push,
    Blackjack,
    Bust,
}

/// <summary>
/// What one hand looks like to the client. Sent over the wire, so it carries the
/// derived values (total, soft, outcome) rather than making the client recompute
/// rules it should not know.
/// </summary>
// The [property: JsonConverter] attributes below are load-bearing, and the ones on
// the enum declarations above are not enough on their own. System.Text.Json resolves
// converters in this order: a property attribute, then options.Converters, then a
// type attribute. SPT registers EftEnumConverterFactory into options.Converters, so
// it outranks anything declared on the enum itself -- which is why these types kept
// serialising as integers until the attributes moved onto the properties.
public sealed record HandView(
    IReadOnlyList<string> Cards,
    int Value,
    bool IsSoft,
    int Wager,
    [property: JsonConverter(typeof(JsonStringEnumConverter))] HandStatus Status,
    [property: JsonConverter(typeof(JsonStringEnumConverter))] HandOutcome Outcome,
    int Returned);

/// <summary>
/// The complete snapshot handed back after every action. This is the only thing
/// the client ever sees -- see <see cref="BlackjackTable.View"/> for why the
/// dealer's hole card is absent from it during the player's turn.
/// </summary>
public sealed record RoundView(
    [property: JsonConverter(typeof(JsonStringEnumConverter))] RoundPhase Phase,
    IReadOnlyList<HandView> PlayerHands,
    HandView Dealer,
    int ActiveHandIndex,
    [property: JsonConverter(typeof(StringEnumListConverter<PlayerAction>))]
    IReadOnlyList<PlayerAction> AvailableActions,
    int TotalWagered,
    int TotalReturned,
    int ShoeRemaining)
{
    /// <summary>Profit or loss for the round. Negative means the house won.</summary>
    public int Net => TotalReturned - TotalWagered;
}
