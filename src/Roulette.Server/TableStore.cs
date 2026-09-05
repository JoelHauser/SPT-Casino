using System.Collections.Concurrent;
using Roulette.Game;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Models.Common;

namespace Roulette.Server;

/// <summary>
/// Live tables, keyed by session.
///
/// Deliberately in memory only. A cloth with chips on it has no business surviving a
/// server restart, and keeping it out of the profile means this mod never changes the
/// profile schema -- so uninstalling it cannot corrupt a save.
///
/// That is safe **only while no money moves**. Once a stake is taken from the stash,
/// what is owed back has to be recorded somewhere that survives a crash. Roulette has
/// an easier time of it than Poker did: the stake is taken when the wheel turns and
/// paid back when it stops, so escrow holds one number for the length of one spin
/// rather than a stack that moves every hand.
/// </summary>
[Injectable(InjectionType.Singleton)]
public class TableStore
{
    private readonly ConcurrentDictionary<string, RouletteTable> _tables = new();

    public RouletteTable? Get(MongoId sessionId) =>
        _tables.TryGetValue(sessionId.ToString(), out var table) ? table : null;

    /// <summary>
    /// The player's table, made on first contact.
    ///
    /// Unlike Poker there is nothing to sit down to: roulette has one seat and no
    /// opponents, so a player who opens the panel is already at the table. Nothing is
    /// staked until a chip is placed and nothing is taken until the wheel turns.
    /// </summary>
    public RouletteTable GetOrCreate(MongoId sessionId, Func<RouletteTable> make) =>
        _tables.GetOrAdd(sessionId.ToString(), _ => make());

    public void Clear(MongoId sessionId) => _tables.TryRemove(sessionId.ToString(), out _);

    public int Count => _tables.Count;
}
