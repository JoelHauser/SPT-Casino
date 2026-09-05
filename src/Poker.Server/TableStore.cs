using System.Collections.Concurrent;
using Poker.Game;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Models.Common;

namespace Poker.Server;

/// <summary>One player's seat at one table, plus who else is sitting there.</summary>
public sealed class PlayerSession
{
    public required HoldemTable Table { get; init; }

    /// <summary>The characters filling the other seats, in seat order.</summary>
    public required IReadOnlyList<PokerPersonality> Characters { get; init; }

    /// <summary>Every bot's agent, kept so a busted seat can be replaced.</summary>
    public required List<BotAgent> Agents { get; init; }

    public required int BuyIn { get; init; }

    /// <summary>What the buy-in was paid in, and what the cash-out returns.</summary>
    public required Wallet Wallet { get; init; }
}

/// <summary>
/// Live tables, keyed by session.
///
/// Deliberately in memory only. A half-played hand has no business surviving a server
/// restart, and keeping it out of the profile means this mod never changes the profile
/// schema -- so uninstalling it cannot corrupt a save.
///
/// That is safe *only while the chips are notional*. Once a buy-in takes real
/// currency, the amount owed back has to be recorded somewhere that survives a crash,
/// and it has to be the player's **live stack** rather than what they sat down with.
/// </summary>
[Injectable(InjectionType.Singleton)]
public class TableStore
{
    private readonly ConcurrentDictionary<string, PlayerSession> _sessions = new();

    public PlayerSession? Get(MongoId sessionId) =>
        _sessions.TryGetValue(sessionId.ToString(), out var session) ? session : null;

    public void Set(MongoId sessionId, PlayerSession session) =>
        _sessions[sessionId.ToString()] = session;

    public void Clear(MongoId sessionId) => _sessions.TryRemove(sessionId.ToString(), out _);

    public int Count => _sessions.Count;
}
