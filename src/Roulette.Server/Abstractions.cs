using SPTarkov.Server.Core.Models.Common;

namespace Roulette.Server;

/// <summary>
/// Reading the player's money.
///
/// An interface because SPT's helpers are concrete classes with non-virtual methods,
/// and depending on them directly makes the calling code impossible to test without a
/// running server. SPT's DI registers a class against every interface it implements,
/// so <see cref="Bank"/> resolves for this with no extra wiring.
///
/// **There is deliberately no way to move money on this interface yet.** This build
/// reads balances and nothing else, so "the mod cannot take your roubles" is a fact
/// about what code exists rather than a promise about what it does. Debit, credit and
/// the shortfall-to-mail path arrive together with the settlement that needs them,
/// and Poker's `Bank` is the thing to port when they do.
///
/// When they are added, every one takes an `ItemEventRouterResponse` to write the
/// change record into, and it must come from `EventOutputHolder.GetOutput` and never
/// from `new` -- a hand-built one initialises nothing and `RemoveItemByCount` reaches
/// straight into `output.ProfileChanges[sessionId]`, so it throws **after** the items
/// are already gone.
/// </summary>
public interface IBank
{
    int GetBalance(MongoId sessionId, Wallet wallet);

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
