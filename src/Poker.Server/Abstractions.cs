using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.ItemEvent;

namespace Poker.Server;

/// <summary>
/// Reading the player's money.
///
/// An interface because SPT's helpers are concrete classes with non-virtual methods,
/// and depending on them directly makes the calling code impossible to test without a
/// running server. SPT's DI registers a class against every interface it implements,
/// so <see cref="Bank"/> resolves for this with no extra wiring.
///
/// Every method takes an <see cref="ItemEventRouterResponse"/> to write the change
/// record into. It must come from `EventOutputHolder.GetOutput`, never from `new` --
/// a hand-built one initialises nothing and `RemoveItemByCount` reaches straight into
/// `output.ProfileChanges[sessionId]`, so it throws **after** the items are already
/// gone. On Blackjack that surfaced as "not enough roubles" while the stake had left
/// the stash.
/// </summary>
public interface IBank
{
    int GetBalance(MongoId sessionId, Wallet wallet);

    /// <summary>
    /// Takes money. False means nothing was touched, so the caller must not deal a
    /// hand it cannot collect on.
    /// </summary>
    bool TryDebit(MongoId sessionId, Wallet wallet, int amount, ItemEventRouterResponse output);

    /// <summary>
    /// Pays money back, splitting it across stacks and posting anything the stash
    /// refuses as mail rather than losing it.
    /// </summary>
    void Credit(MongoId sessionId, Wallet wallet, int amount, ItemEventRouterResponse output);

    /// <summary>
    /// The running server's stack limit for a wallet, which item mods change.
    ///
    /// Read live, never assumed: the base database says roubles stack to 1,000,000
    /// and dollars and euros to 50,000, while BarterItemsStacks raises all three.
    /// Both are correct on different servers.
    ///
    /// Still worth reading live now that only currency is stakeable. The limit is what
    /// decides how many stacks a payout splits into, an item mod can set it to
    /// anything, and a limit of zero makes the splitting loop take zero each pass and
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

/// <summary>What the player is owed back, and in what.</summary>
public class OutstandingStack
{
    public string Wallet { get; set; } = nameof(Server.Wallet.Roubles);

    /// <summary>The player's **live stack**, not what they sat down with.</summary>
    public int Chips { get; set; }

    public long SatDownAtUtc { get; set; }
}

/// <summary>
/// Records what the table owes the player while they are sitting at it.
///
/// Blackjack's escrow held a *stake* until a hand settled, and that is not enough
/// here. A hold'em session takes the buy-in once and then hands the player a stack
/// that moves every hand, so what is owed back is a number that changes -- and a
/// crash has to return **what they actually have**, not what they arrived with.
/// Recording the buy-in and stopping would quietly refund a player who had lost most
/// of it, and rob one who had doubled up.
/// </summary>
public interface IEscrowStore
{
    OutstandingStack? Get(MongoId sessionId);

    /// <summary>
    /// Writes down the stack as it stands. Called on sitting down and after every
    /// hand, because between those two moments the number is already stale.
    /// </summary>
    void Record(MongoId sessionId, Wallet wallet, int chips);

    /// <summary>Nothing is owed any more -- the player has been paid out.</summary>
    void Release(MongoId sessionId);
}

/// <summary>
/// What the game flow needs from the log.
///
/// An interface for the same reason the bank is one. `PokerLog` reaches for the mod
/// folder, a config file and SPT's own logger in its constructor, none of which exist
/// in a test -- and a service that cannot be built without a server is a service
/// whose money path cannot be tested before it moves any.
/// </summary>
public interface IPokerLog
{
    void Info(string message);

    void Detail(string message);

    void Error(string message);

    /// <summary>The sink the engine writes its own reasoning to.</summary>
    Poker.Game.IGameLog ForEngine();
}

/// <summary>
/// Where the bots' names come from.
///
/// A seam for the same reason <see cref="IBank"/> is one: the real implementation
/// reads the game's own PMC nickname list out of the database, and a test wanting a
/// named table should not have to stand a database up to get one.
/// </summary>
public interface INameSource
{
    /// <summary>
    /// Distinct names for one table, in seat order. Fewer than asked for is allowed
    /// -- the table falls back to numbering whatever it does not receive -- so a
    /// missing or unreadable name list costs the flavour and nothing else.
    /// </summary>
    IReadOnlyList<string> Take(int count, Random rng);
}
