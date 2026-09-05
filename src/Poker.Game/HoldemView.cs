using System.Text.Json.Serialization;

namespace Poker.Game;

/// <summary>One seat as everyone at the table can see it.</summary>
/// <param name="Cards">
/// Empty unless this seat's cards may be seen. See <see cref="HoldemView.Of"/> for
/// the rule -- they are absent rather than blanked, because anything sent to the
/// client is knowable by the client.
/// </param>
public sealed record SeatSnapshot(
    int Index,
    bool IsPlayer,
    string Name,
    int Stack,
    IReadOnlyList<string> Cards,
    int CommittedThisStreet,
    int CommittedThisHand,
    bool Folded,
    bool IsAllIn,
    bool IsTurn,
    string? Hand,
    int Won);

/// <summary>
/// The whole table, as the client is allowed to see it.
///
/// This is the only thing a transport ever sends. Building it is the one place the
/// hidden-card rule is applied, so there is a single line to get right rather than
/// one per screen.
/// </summary>
public sealed record HoldemView(
    [property: JsonConverter(typeof(JsonStringEnumConverter))] HoldemStreet Street,
    IReadOnlyList<SeatSnapshot> Seats,
    IReadOnlyList<string> Community,
    int Pot,
    int Button,
    int? ActorSeat,
    bool AwaitingPlayer,
    BettingOptions? Options,
    int SmallBlind,
    int BigBlind)
{
    /// <summary>
    /// Takes a snapshot of the table.
    ///
    /// **The reveal rule is the only subtle thing here.** A seat's cards are shown to
    /// that seat, and otherwise only once it has reached a showdown -- which the
    /// engine signals by filling in <see cref="HoldemSeat.Hand"/>. Keying off the
    /// street instead leaks the winner's hole cards on every pot that ended with
    /// everybody folding, which is most of them, and is exactly the bug the terminal
    /// harness printed on the first hand it ever drew.
    /// </summary>
    public static HoldemView Of(HoldemTable table, bool showEverything = false)
    {
        var actor = table.Actor;

        return new HoldemView(
            table.Street,
            table.Seats.Select(seat => new SeatSnapshot(
                seat.Index,
                seat.IsPlayer,
                seat.Name,
                seat.Stack,
                seat.IsPlayer || showEverything || seat.Hand is not null
                    ? seat.Cards.Select(card => card.Code).ToList()
                    : [],
                seat.CommittedThisStreet,
                seat.CommittedThisHand,
                seat.Folded,
                seat.IsAllIn,
                actor?.Index == seat.Index,
                seat.Hand?.Describe(),
                seat.Won)).ToList(),
            table.Community.Select(card => card.Code).ToList(),
            table.Pot,
            table.Button,
            actor?.Index,
            table.AwaitingPlayer,
            table.AwaitingPlayer ? table.Options() : null,
            table.Rules.SmallBlind,
            table.Rules.BigBlind);
    }
}
