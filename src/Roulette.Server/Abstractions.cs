using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.ItemEvent;

namespace Roulette.Server;

/// <summary>
/// Reading the player's money.
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
/// the items are already gone. On Blackjack that surfaced as "not enough roubles"
/// while the stake had left the stash.
/// </summary>
public interface IBank
{
    int GetBalance(MongoId sessionId, Wallet wallet);

    /// <summary>
    /// Takes money. False means nothing was touched, so the caller must not turn a
    /// wheel it cannot pay out on.
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
    /// It decides how many stacks a payout splits into, an item mod can set it to
    /// anything, and a limit of zero makes a splitting loop take zero each pass and
    /// hang a server thread rather than fail -- so clamp it to at least 1.
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
public interface IRouletteLog
{
    void Info(string message);

    void Detail(string message);

    void Error(string message);

    /// <summary>The sink the engine writes its own reasoning to.</summary>
    Roulette.Game.IGameLog ForEngine();
}


/// <summary>What the table is holding of the player's money, and in what.</summary>
public class OutstandingStake
{
    public string Wallet { get; set; } = nameof(Server.Wallet.Roubles);

    /// <summary>What was taken for a spin and has not been returned.</summary>
    public int Amount { get; set; }

    public long TakenAtUtc { get; set; }
}

/// <summary>
/// Records what the table owes the player while a spin is in flight.
///
/// **Roulette's escrow is Blackjack's, not Poker's.** Poker takes one buy-in and
/// hands back a live stack that moves every hand, so what it holds has to be
/// re-recorded constantly. Here the money is out of the wallet only between the
/// debit and the credit of a single spin, and what is owed in that window is
/// exactly what was taken -- it cannot drift, because nothing happens in between.
///
/// The window is short but it is not zero, and it is the only window in this mod
/// where the player's money exists nowhere. A server killed inside it has taken
/// the stake and paid nothing, and without a record on disk there is no way to
/// know it ever happened.
/// </summary>
public interface IEscrowStore
{
    OutstandingStake? Get(MongoId sessionId);

    void Record(MongoId sessionId, Wallet wallet, int amount);

    void Release(MongoId sessionId);
}

/// <summary>
/// Where the wheel gets its randomness.
///
/// An interface so a test can seed it. The alternative -- letting the service call
/// `new Random()` -- makes every money test depend on which pocket the ball happened
/// to find, which is the one thing a money test must not care about.
/// </summary>
public interface IRandomSource
{
    Random Create();
}
