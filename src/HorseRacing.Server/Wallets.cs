using SPTarkov.Server.Core.Models.Common;

namespace HorseRacing.Server;

/// <summary>What a bet can be placed in.</summary>
public enum Wallet
{
    Roubles,
    Dollars,
    Euros,
}

/// <summary>
/// A currency, and what the window takes in it.
/// </summary>
/// <param name="Wallet">Which currency.</param>
/// <param name="Tpl">The item template the stash holds it as.</param>
/// <param name="Sign">A symbol for the panel.</param>
/// <param name="Label">Its name, for anything the player reads.</param>
/// <param name="MinStake">The smallest single bet on the slip.</param>
/// <param name="MaxStake">
/// **The most the whole slip may come to, not the most one bet may be.**
///
/// This is the one place horse racing differs from every other table in the casino,
/// and the difference is not cosmetic. The other four take one stake per round, so a
/// per-bet ceiling and a per-round ceiling are the same number. A slip here can carry
/// up to 108 bets, and what has to stay inside an <c>int</c> is the total that comes
/// back -- so the ceiling has to be on the sum. A per-bet limit of two million with no
/// limit on the count would let a full slip return something like a hundred and sixty
/// billion, which an int reports as a negative number.
///
/// See <c>HorseRacing.Game.RaceRules.MaxTotalStake</c>, which is the same bound stated
/// in chips, and <c>OddsTests</c>, which asserts it against the real card.
/// </param>
/// <param name="Step">
/// What the stake button moves in, and **nothing more than that**. It is not a rule
/// about what the window takes: the panel lets the stake be typed, so any whole amount
/// between the two ends is legal. See <see cref="Allows"/>.
/// </param>
public sealed record WalletInfo(
    Wallet Wallet,
    MongoId Tpl,
    string Sign,
    string Label,
    int MinStake,
    int MaxStake,
    int Step)
{
    /// <summary>Rouble template id.</summary>
    private static readonly MongoId RoublesTpl = new("5449016a4bdc2d6f028b456f");

    private static readonly MongoId DollarsTpl = new("5696686a4bdc2da3298b456a");

    private static readonly MongoId EurosTpl = new("569668774bdc2da2298b4568");

    /// <summary>
    /// The three currencies the window takes.
    ///
    /// **Spendable currency only, and that is a decision rather than an omission.** GP
    /// coins, physical bitcoin and Lega medals all stack to one item each, so a bet
    /// paying seven hundred times its stake would hand back a payout measured in free
    /// grid cells rather than in money. Blackjack learned that the expensive way and it
    /// is written up in `docs/blackjack.md`.
    ///
    /// The rouble minimum matches roulette's, because a player moving between the two
    /// tables should not have to relearn what a small bet is. The ceiling is the
    /// arithmetic one -- the longest price on this card is a little over 750 for one,
    /// and two million at that price is about 1.5 billion, which is the last round
    /// number that still fits in the int the engine counts chips in.
    /// </summary>
    private static readonly Dictionary<Wallet, WalletInfo> Table = new()
    {
        [Wallet.Roubles] = new(Wallet.Roubles, RoublesTpl, "R", "Roubles", 10_000, 2_000_000, 5_000),
        [Wallet.Dollars] = new(Wallet.Dollars, DollarsTpl, "$", "Dollars", 100, 20_000, 100),
        [Wallet.Euros] = new(Wallet.Euros, EurosTpl, "E", "Euros", 100, 20_000, 100),
    };

    public static WalletInfo For(Wallet wallet) => Table[wallet];

    public static IEnumerable<WalletInfo> All => Table.Values;

    /// <summary>
    /// The largest slip total that cannot overflow, whatever anybody has turned off.
    ///
    /// Equal to the rouble ceiling: that one is already the arithmetic bound rather
    /// than a cautious one, so there is nothing above it to unlock. Named separately
    /// anyway, because the two being the same number is a consequence and not a
    /// coincidence, and a future edit to the rouble limit must not silently move this.
    /// </summary>
    public static int HardCeiling => 2_000_000;

    /// <summary>
    /// Whether a single bet is one this wallet takes: **any whole amount at or above
    /// the minimum**.
    ///
    /// Note what this does *not* check: the ceiling. That belongs to the slip as a
    /// whole and is checked by <see cref="AllowsSlip"/>, because on this table it is a
    /// limit on the total rather than on any one bet. Splitting the two checks is what
    /// keeps a player from being told a perfectly ordinary 50,000 bet is too large when
    /// what is actually too large is the twenty other bets beside it.
    ///
    /// Checked here rather than trusted from the panel: a request is a thing anybody
    /// can send by hand.
    /// </summary>
    public static bool Allows(Wallet wallet, long stake)
    {
        // A stake of zero is a free bet at a table that pays multiples of the stake;
        // a negative one is a table that pays the player to lose.
        return stake >= For(wallet).MinStake;
    }

    /// <summary>
    /// Whether the slip as a whole is one this wallet takes.
    /// </summary>
    /// <param name="ignoreMaximum">
    /// Lifts the ceiling, and only the ceiling.
    ///
    /// **Unlike every other table in the casino, this one does not honour it past the
    /// arithmetic.** Elsewhere the maximum is the house being careful on the player's
    /// behalf, and a player may say they would rather it did not -- Blackjack's table
    /// maximum and Slots' both work that way. Here the ceiling is not caution, it is
    /// the width of an int, and a player who waives it does not get a bigger win, they
    /// get a negative one. So this raises the limit to the arithmetic bound and no
    /// further.
    /// </param>
    public static bool AllowsSlip(Wallet wallet, long total, bool ignoreMaximum = false)
    {
        var info = For(wallet);

        if (total < info.MinStake)
        {
            return false;
        }

        return total <= (ignoreMaximum ? HardCeiling : info.MaxStake);
    }
}
