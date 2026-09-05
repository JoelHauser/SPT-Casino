using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Enums;

namespace Poker.Server;

/// <summary>
/// What a player can sit down with.
///
/// **Spendable currency only.** GP coins, physical bitcoin and Lega medals were here
/// and were dropped deliberately -- see the note below before adding anything that is
/// not money.
/// </summary>
public enum Wallet
{
    Roubles,
    Dollars,
    Euros,
}

/// <summary>
/// Per-wallet limits and presentation.
///
/// These live here rather than in the engine because the engine has no concept of a
/// currency -- it takes an int and returns an int. One pair of limits cannot serve
/// both roubles and dollars: a buy-in of 100,000 is unremarkable in one and absurd
/// in the other.
///
/// ### Valuables were removed on purpose, and it deleted a class of problem
///
/// GP coins, physical bitcoin and Lega medals used to be stakeable, and a
/// <c>WalletKind</c> enum existed to mark them as something the table should not treat
/// like money. Nothing ever read it.
///
/// They cost far more than they were worth. **Bitcoin and Lega medals have a
/// <c>StackMaxSize</c> of 1** -- one item per unit, one grid cell each -- so a five-
/// seat table paying out a doubled-up buy-in hands back a pile of coins measured in
/// free grid cells rather than in roubles. That single fact drove the buy-in ceilings,
/// forced a separate capped paytable back when this was Ultimate Texas Hold'em, and
/// was the reason the payout scale was this mod's hardest open problem.
///
/// It also could never have been exercised: all three read zero on both of the test
/// profiles, so the riskiest payout path in the mod had no way to be tested.
///
/// **Do not add a non-stacking item back as a wallet** without deciding first what a
/// payout of forty individually-gridded items is supposed to do to a full stash.
/// </summary>
public sealed record WalletInfo(
    Wallet Wallet,
    MongoId Tpl,
    string Symbol,
    string Label,
    int MinBuyIn,
    int MaxBuyIn)
{
    /// <summary>
    /// The ceilings are set from what a session can hand back, not from what a hand
    /// can pay. A pot cannot exceed the chips at the table, so the most that can come
    /// off a table is roughly the buy-in times the number of seats.
    ///
    /// Roubles reach 5,000,000 because the chip buy-in is 2,000,000 and one chip is
    /// one rouble; a lower cap makes the table ask for money the wallet refuses, which
    /// is exactly the contradiction this number was raised to fix.
    /// </summary>
    private static readonly Dictionary<Wallet, WalletInfo> Table = new()
    {
        [Wallet.Roubles] = new(Wallet.Roubles, Money.ROUBLES, "R", "Roubles", 100_000, 5_000_000),
        [Wallet.Dollars] = new(Wallet.Dollars, Money.DOLLARS, "$", "Dollars", 100, 5_000),
        [Wallet.Euros] = new(Wallet.Euros, Money.EUROS, "E", "Euros", 100, 5_000),
    };

    public static WalletInfo For(Wallet wallet) => Table[wallet];

    public static IEnumerable<WalletInfo> All => Table.Values;
}
