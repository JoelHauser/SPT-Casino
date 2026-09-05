using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.ItemEvent;

namespace Blackjack.Server;

/// <summary>
/// Everything the game logic needs to do with the player's money.
///
/// This exists as an interface because SPT's InventoryHelper and ProfileHelper are
/// concrete classes with non-virtual methods -- depending on them directly makes
/// the calling code impossible to test without a running server. SPT's DI registers
/// a class against every interface it implements, so <see cref="Bank"/> resolves
/// for this with no extra wiring.
///
/// Note it takes a session id rather than a PmcData: that keeps every SPT profile
/// model out of the game logic entirely.
/// </summary>
public interface IBank
{
    int GetBalance(MongoId sessionId, Wallet wallet);

    /// <summary>
    /// Takes money. False means nothing was touched.
    ///
    /// <paramref name="output"/> collects what changed. Handed back to the client, it
    /// is what keeps the stash view in step; discarded, the money moves on the server
    /// and the client's own copy never hears about it.
    /// </summary>
    bool TryDebit(MongoId sessionId, Wallet wallet, int amount, ItemEventRouterResponse output);

    void Credit(MongoId sessionId, Wallet wallet, int amount, ItemEventRouterResponse output);

    /// <summary>
    /// The running server's stack limit for a wallet, which item mods change. Exposed
    /// so startup can report what is actually in force rather than what is assumed.
    /// </summary>
    int MaxStackSize(Wallet wallet);
}

public interface IProfileGateway
{
    bool HasProfile(MongoId sessionId);

    /// <summary>Flushes money changes to disk. Money that is not saved did not move.</summary>
    Task SaveAsync(MongoId sessionId);
}

/// <summary>
/// Lifetime stats per profile. An interface for the same reason the others are --
/// so the accounting can be tested without a filesystem.
/// </summary>
public interface IStatsStore
{
    PlayerStats Get(MongoId sessionId);

    void Save(MongoId sessionId, PlayerStats stats);
}

/// <summary>
/// Money taken from a player whose round has not settled. See <see cref="EscrowStore"/>
/// for why an in-memory table makes this necessary.
/// </summary>
public interface IEscrowStore
{
    OutstandingStake? Get(MongoId sessionId);

    void Hold(MongoId sessionId, Wallet wallet, int amount);

    void Release(MongoId sessionId);
}
