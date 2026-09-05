using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Enums;

namespace Roulette.Server;

/// <summary>
/// What a player can bet with.
///
/// **Spendable currency only.** Poker had GP coins, physical bitcoin and Lega medals
/// as wallets and dropped all three deliberately; read the note below before adding
/// anything that is not money.
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
/// both roubles and dollars: a stake of 100,000 is unremarkable in one and absurd in
/// the other.
///
/// ### Valuables are not wallets, and that is not an oversight
///
/// **Bitcoin and Lega medals have a <c>StackMaxSize</c> of 1** -- one item per unit,
/// one grid cell each. A straight-up bet pays 35 to 1, so a winning minimum bet in
/// bitcoin would hand back a pile of coins measured in free grid cells rather than in
/// money. Roulette makes this worse than Poker did, because a pot could never exceed
/// the chips at the table and a 35 to 1 payout has no such ceiling.
///
/// They also could never have been exercised: all three read zero on both test
/// profiles.
///
/// **Do not add a non-stacking item back as a wallet** without deciding first what a
/// payout of several hundred individually-gridded items does to a full stash.
///
/// ### One chip is one currency unit
///
/// The engine counts chips and the wallet counts money, at a rate of one to one. That
/// is why the ceilings below are the numbers they are, and it is why roubles are the
/// only wallet that really works: the minimum bet is 10,000 chips, which dollars and
/// euros cannot reach at their limits. Giving each wallet a chips-per-unit rate is
/// what would open the other two up, and it is not built.
/// </summary>
public sealed record WalletInfo(
    Wallet Wallet,
    MongoId Tpl,
    string Symbol,
    string Label,
    int MinStake,
    int MaxStake)
{
    /// <summary>
    /// The ceilings are what one spin may take off a player, which is the table's own
    /// <c>MaxTotalStake</c> expressed in a currency. A cap below that makes the table
    /// ask for money the wallet refuses -- the contradiction Poker had to fix when its
    /// chip buy-in outgrew its rouble ceiling.
    /// </summary>
    private static readonly Dictionary<Wallet, WalletInfo> Table = new()
    {
        [Wallet.Roubles] = new(Wallet.Roubles, Money.ROUBLES, "R", "Roubles", 10_000, 10_000_000),
        [Wallet.Dollars] = new(Wallet.Dollars, Money.DOLLARS, "$", "Dollars", 100, 5_000),
        [Wallet.Euros] = new(Wallet.Euros, Money.EUROS, "E", "Euros", 100, 5_000),
    };

    public static WalletInfo For(Wallet wallet) => Table[wallet];

    public static IEnumerable<WalletInfo> All => Table.Values;
}
