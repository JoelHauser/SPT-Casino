using System.Text.Json.Serialization;

namespace Poker.Game;

// These cross the wire. Blackjack shipped its enums as integers and regretted it --
// the client ended up comparing against magic numbers that changed silently the
// moment a value was inserted into the middle of an enum. Strings from the start
// here.
//
// The [property: JsonConverter] attributes below are load-bearing and the ones on
// the enum declarations are not enough on their own. System.Text.Json resolves
// converters in this order: a property attribute, then options.Converters, then a
// type attribute -- and SPT registers EftEnumConverterFactory into
// options.Converters, so it outranks anything declared on the enum itself. That is
// exactly how Blackjack's enums kept serialising as integers.

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum TablePhase
{
    /// <summary>No hand in progress. The table is waiting for an Ante.</summary>
    AwaitingBets,

    /// <summary>Hole cards dealt, no community card showing.</summary>
    PreFlop,

    /// <summary>Three community cards showing.</summary>
    Flop,

    /// <summary>All five showing. The last decision.</summary>
    River,

    /// <summary>Hands compared, money worked out, everything visible.</summary>
    Settled,
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum PlayerAction
{
    Check,
    Play,
    Fold,
}

/// <summary>
/// One seat as the client sees it. Carries the derived values -- the hand's reading,
/// what came back -- rather than making the client recompute rules it should not
/// know.
/// </summary>
public sealed record SeatView(
    int Index,
    bool IsPlayer,
    string Name,
    IReadOnlyList<string> Cards,
    int Ante,
    int Blind,
    int Trips,
    int Play,
    bool Folded,
    string? Hand,
    [property: JsonConverter(typeof(JsonStringEnumConverter))] SeatOutcome Outcome,
    int Wagered,
    int Returned);

/// <summary>
/// The dealer. Not a seat: it has no bets, makes no decisions, and only has to say
/// what it holds and whether that opened.
/// </summary>
public sealed record DealerView(IReadOnlyList<string> Cards, string? Hand, bool Qualified);

/// <summary>
/// The complete snapshot handed back after every action, and the only thing the
/// client ever sees.
///
/// Hidden cards are **absent** rather than blanked. Anything sent to the client is
/// knowable by the client, so a face-down card is an empty list, not a placeholder
/// string somebody can read past.
/// </summary>
public sealed record TableView(
    [property: JsonConverter(typeof(JsonStringEnumConverter))] TablePhase Phase,
    IReadOnlyList<SeatView> Seats,
    DealerView Dealer,
    IReadOnlyList<string> Community,
    [property: JsonConverter(typeof(StringEnumListConverter<PlayerAction>))]
    IReadOnlyList<PlayerAction> AvailableActions,
    IReadOnlyList<int> AvailablePlayMultiples,
    int PlayerWagered,
    int PlayerReturned,
    int DeckRemaining)
{
    /// <summary>
    /// Profit or loss for the player. Negative means the house won.
    ///
    /// The player's alone, deliberately, and the same is true of
    /// <see cref="PlayerWagered"/> and <see cref="PlayerReturned"/>. The seat-mates'
    /// numbers are notional and must never reach a debit or a credit -- summing every
    /// seat here is the single most expensive mistake available in this file.
    /// </summary>
    public int PlayerNet => PlayerReturned - PlayerWagered;
}
