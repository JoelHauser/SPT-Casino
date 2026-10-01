using System.Collections.Concurrent;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Models.Common;
using War.Game;

namespace War.Server;

/// <summary>One player's seat: their table, and the currency the current round is in.</summary>
public sealed class Seat
{
    public required WarTable Table { get; init; }

    public Wallet Wallet { get; set; } = Wallet.Roubles;
}

/// <summary>
/// Live tables, keyed by session.
///
/// The slot machine has no store at all, because nothing lasts between its requests.
/// War has one thing that does: a tie. The deal answers, the ante stays on the table,
/// and the next request is the player saying whether to fight for it -- so the table
/// has to still be there.
///
/// Deliberately in memory only, like Blackjack's. A half-played hand has no business
/// surviving a server restart, and keeping it out of the profile means this mod never
/// changes the profile schema. What the restart does strand is the stake, and that is
/// the escrow's job: see <see cref="EscrowStore"/>.
/// </summary>
[Injectable(InjectionType.Singleton)]
public class TableStore(IRandomSource random)
{
    private readonly ConcurrentDictionary<string, Seat> _seats = new();

    public Seat For(MongoId sessionId) =>
        _seats.GetOrAdd(sessionId.ToString(), _ => new Seat { Table = new WarTable(random.Create()) });

    /// <summary>
    /// Whether this player has a tie waiting. Asked without creating a seat, which
    /// <see cref="For"/> would do as a side effect.
    /// </summary>
    public bool HasOpenRound(MongoId sessionId) =>
        _seats.TryGetValue(sessionId.ToString(), out var seat) && seat.Table.Phase == Phase.AwaitingDecision;

    /// <summary>
    /// Test seam: seats a player at a table with a known shoe, so a test can force a
    /// tie. A real game never calls this.
    /// </summary>
    public Seat Seed(MongoId sessionId, WarTable table)
    {
        var seat = new Seat { Table = table };
        _seats[sessionId.ToString()] = seat;
        return seat;
    }

    /// <summary>Drops a seat, abandoning any round on it. What a server restart does.</summary>
    public void Clear(MongoId sessionId) => _seats.TryRemove(sessionId.ToString(), out _);
}
