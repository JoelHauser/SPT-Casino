using SPTarkov.Server.Core.Models.Common;

namespace War.Server;

/// <summary>What a hand can be played in.</summary>
public enum Wallet
{
    Roubles,
    Dollars,
    Euros,
}

/// <summary>A currency, and what one hand may be played for in it.</summary>
/// <param name="Wallet">Which currency.</param>
/// <param name="Tpl">The item template the stash holds it as.</param>
/// <param name="Sign">A symbol for the panel.</param>
/// <param name="Label">Its name, for anything the player reads.</param>
/// <param name="MinBet">The smallest bet, main or tie.</param>
/// <param name="MaxBet">The largest bet, main or tie, unless the player lifts it.</param>
/// <param name="Step">What the panel's buttons move by. Not a rule about what is taken.</param>
public sealed record WalletInfo(
    Wallet Wallet,
    MongoId Tpl,
    string Sign,
    string Label,
    int MinBet,
    int MaxBet,
    int Step)
{
    /// <summary>
    /// The ceiling that does **not** come off, whatever the F12 menu says.
    ///
    /// Not a house limit; an arithmetic one. The bank counts in `int`, a tie bet can
    /// return eleven times itself and a war takes the ante twice, so a bet has to stay
    /// small enough for every one of those to fit. A hundred million times eleven is
    /// 1.1 billion, inside `int.MaxValue` with room to spare. The slot machine takes
    /// any long when its cap is lifted and casts it, which is a bug this table does not
    /// copy.
    /// </summary>
    public const int AbsoluteMax = 100_000_000;

    private static readonly MongoId RoublesTpl = new("5449016a4bdc2d6f028b456f");

    private static readonly MongoId DollarsTpl = new("5696686a4bdc2da3298b456a");

    private static readonly MongoId EurosTpl = new("569668774bdc2da2298b4568");

    /// <summary>
    /// Blackjack's limits, because it is the other even-money table here and a player
    /// moving between the two should not find one ten times the other.
    ///
    /// Spendable currency only, as at the slot machine: GP coins, bitcoin and Lega
    /// medals stack to one item each, and a tie bet paying eleven of them is eleven
    /// free grid cells rather than money.
    /// </summary>
    private static readonly Dictionary<Wallet, WalletInfo> Table = new()
    {
        [Wallet.Roubles] = new(Wallet.Roubles, RoublesTpl, "R", "Roubles", 1_000, 500_000, 5_000),
        [Wallet.Dollars] = new(Wallet.Dollars, DollarsTpl, "$", "Dollars", 10, 5_000, 50),
        [Wallet.Euros] = new(Wallet.Euros, EurosTpl, "E", "Euros", 10, 5_000, 50),
    };

    public static WalletInfo For(Wallet wallet) => Table[wallet];

    public static IEnumerable<WalletInfo> All => Table.Values;

    /// <summary>
    /// Whether a bet is one this wallet takes: any whole amount from the minimum to the
    /// maximum, or past the maximum when the player has switched it off -- but never
    /// past <see cref="AbsoluteMax"/>.
    ///
    /// Checked here rather than trusted from the panel: a request is a thing anybody
    /// can send by hand.
    /// </summary>
    public static bool Allows(Wallet wallet, long bet, bool ignoreMaximum = false)
    {
        var info = For(wallet);
        var ceiling = ignoreMaximum ? AbsoluteMax : info.MaxBet;

        return bet >= info.MinBet && bet <= ceiling;
    }
}
