using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.ItemEvent;

namespace War.Server;

/// <summary>
/// Reading the player's money. The same interface as the slot machine's.
///
/// An interface because SPT's helpers are concrete classes with non-virtual methods,
/// and depending on them directly makes the calling code impossible to test without a
/// running server. SPT's DI registers a class against every interface it implements,
/// so <see cref="Bank"/> resolves for this with no extra wiring.
///
/// Every method that moves money takes an <see cref="ItemEventRouterResponse"/> to
/// write the change record into. It must come from `EventOutputHolder.GetOutput`,
/// never from `new` -- a hand-built one initialises nothing and `RemoveItemByCount`
/// reaches straight into `output.ProfileChanges[sessionId]`, so it throws **after**
/// the items are already gone.
/// </summary>
public interface IBank
{
    int GetBalance(MongoId sessionId, Wallet wallet);

    /// <summary>
    /// Takes money. False means nothing was touched, so the caller must not deal a
    /// hand it cannot pay out on.
    /// </summary>
    bool TryDebit(MongoId sessionId, Wallet wallet, int amount, ItemEventRouterResponse output);

    /// <summary>
    /// Pays money back, splitting it across stacks and posting anything the stash
    /// refuses as mail rather than losing it.
    /// </summary>
    void Credit(MongoId sessionId, Wallet wallet, int amount, ItemEventRouterResponse output);

    /// <summary>
    /// The running server's stack limit for a wallet, which item mods change. Clamped
    /// to at least 1: a limit of zero makes a splitting loop take zero each pass and
    /// hang a server thread rather than fail.
    /// </summary>
    int MaxStackSize(Wallet wallet);
}

public interface IProfileGateway
{
    bool HasProfile(MongoId sessionId);

    /// <summary>Flushes changes to disk. Money that is not saved did not move.</summary>
    Task SaveAsync(MongoId sessionId);
}

/// <summary>
/// The mod's own logging, as an interface so the service can be given a quiet one in
/// a test rather than a real server's console.
/// </summary>
public interface IWarLog
{
    void Info(string message);

    void Detail(string message);

    void Error(string message);
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

/// <summary>What the table is holding of the player's money, and in what.</summary>
public class OutstandingStake
{
    public string Wallet { get; set; } = nameof(Server.Wallet.Roubles);

    /// <summary>Everything the table holds right now. Not a running total.</summary>
    public int Amount { get; set; }

    public long TakenAtUtc { get; set; }
}

/// <summary>
/// Records what the table holds of the player's money while a round is open. See
/// <see cref="EscrowStore"/> for why <see cref="Record"/> replaces rather than adds.
/// </summary>
public interface IEscrowStore
{
    OutstandingStake? Get(MongoId sessionId);

    void Record(MongoId sessionId, Wallet wallet, int amount);

    void Release(MongoId sessionId);
}

/// <summary>
/// Where the shoe gets its randomness. An interface so a test can seed it, and so no
/// money test depends on which card happened to come out.
/// </summary>
public interface IRandomSource
{
    Random Create();
}
