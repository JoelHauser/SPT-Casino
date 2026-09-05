using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Enums;

namespace Blackjack.Server;

public enum Wallet
{
    Roubles,
    Dollars,
    Euros,
    GpCoins,
    Bitcoin,
    LegaMedals,
}

/// <summary>
/// What kind of thing is being staked. These are not interchangeable and the table
/// does not treat them alike.
/// </summary>
public enum WalletKind
{
    /// <summary>
    /// Spendable money, held in thousands and staked in thousands. The player thinks
    /// in amounts, so bets move by a step and the exact unit is beneath notice.
    /// </summary>
    Currency,

    /// <summary>
    /// Valuables: GP coins, bitcoin, Lega medals. Held in single figures and staked
    /// by the piece, so the player thinks in counts, not amounts. Indivisible, which
    /// is what makes a 3:2 payout awkward -- see <see cref="WalletInfo.SettlesExactly"/>.
    /// </summary>
    Valuable,
}

/// <summary>
/// Per-wallet limits and presentation.
///
/// These live here rather than in <see cref="Game.Rules"/> because the engine has no
/// concept of a currency -- it takes an int and returns an int. One pair of limits
/// cannot serve both roubles and bitcoin: a minimum of 1,000 is beneath notice in one
/// and impossible in the other.
/// </summary>
public sealed record WalletInfo(
    Wallet Wallet,
    WalletKind Kind,
    MongoId Tpl,
    string Symbol,
    string Label,
    int MinBet,
    int MaxBet,
    int Step)
{
    /// <summary>
    /// Profit multiplier on a natural, which depends on whether the stake divides.
    ///
    /// Currency pays the usual 3:2 -- rounding lands inside a rouble nobody counts.
    /// Valuables pay even money, because one bitcoin at 3:2 settles on two and a half
    /// and half a bitcoin does not exist. Rounding it either way would be a rule the
    /// player could not see.
    /// </summary>
    public double BlackjackPayout => Kind == WalletKind.Currency ? 1.5 : 1.0;

    private static readonly Dictionary<Wallet, WalletInfo> Table = new()
    {
        // The ceilings are the house's only real protection, and not for the reason
        // they look like. A 0.45% edge over six decks is nothing across a session; what
        // actually stops a player compounding is being unable to cover a losing streak
        // by doubling up, and a maximum of five hundred times the minimum caps that at
        // nine doubles. Remove the ceiling and the edge stops mattering at all.
        //
        // A player who would rather not have one can turn it off; see IgnoreMaximum.
        [Wallet.Roubles] = new(Wallet.Roubles, WalletKind.Currency, Money.ROUBLES, "₽", "Roubles", 1_000, 500_000, 1_000),
        [Wallet.Dollars] = new(Wallet.Dollars, WalletKind.Currency, Money.DOLLARS, "$", "Dollars", 10, 5_000, 10),
        [Wallet.Euros] = new(Wallet.Euros, WalletKind.Currency, Money.EUROS, "€", "Euros", 10, 5_000, 10),

        // Valuables are staked by the piece, so their ceilings are counts rather than
        // amounts: what a player could plausibly hold and be willing to lose in one go.
        [Wallet.GpCoins] = new(Wallet.GpCoins, WalletKind.Valuable, Money.GP, "GP", "GP coins", 1, 50, 1),
        [Wallet.Bitcoin] = new(Wallet.Bitcoin, WalletKind.Valuable, ItemTpl.BARTER_PHYSICAL_BITCOIN, "₿", "Bitcoin", 1, 10, 1),
        [Wallet.LegaMedals] = new(Wallet.LegaMedals, WalletKind.Valuable, ItemTpl.BARTER_LEGA_MEDAL, "LEGA", "Lega medals", 1, 5, 1),
    };

    public static WalletInfo For(Wallet wallet) => Table[wallet];

    public static IEnumerable<WalletInfo> All => Table.Values;

    public static IEnumerable<WalletInfo> OfKind(WalletKind kind) => Table.Values.Where(w => w.Kind == kind);

}
