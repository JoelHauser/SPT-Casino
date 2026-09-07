using SPTarkov.Server.Core.Models.Common;

namespace SlotMachine.Server;

/// <summary>What a pull can be paid in.</summary>
public enum Wallet
{
    Roubles,
    Dollars,
    Euros,
}

/// <summary>
/// A currency, and what one pull of the handle may cost in it.
/// </summary>
/// <param name="Wallet">Which currency.</param>
/// <param name="Tpl">The item template the stash holds it as.</param>
/// <param name="Sign">A symbol for the panel.</param>
/// <param name="Label">Its name, for anything the player reads.</param>
/// <param name="MinStake">The smallest pull.</param>
/// <param name="MaxStake">The largest pull.</param>
/// <param name="Step">
/// What the stake button moves in, and **nothing more than that**. It is not a rule
/// about what the machine takes: the panel lets the stake be typed, so any whole
/// amount between the two ends is legal. See <see cref="Allows"/>.
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
    /// The three currencies the machine takes.
    ///
    /// **Spendable currency only, and that is a decision rather than an omission.** GP
    /// coins, physical bitcoin and Lega medals all stack to one item each, so a machine
    /// paying a thousand times the stake would hand back a payout measured in free grid
    /// cells rather than in money. Blackjack learned that the expensive way and it is
    /// written up in `docs/blackjack.md`.
    ///
    /// The ceilings are what makes the top prize sane. LEDX five of a kind pays a
    /// thousand times the stake, so a 50,000 rouble pull can return 50,000,000 -- and
    /// that is the number the ceiling is really choosing, not the cost of a pull.
    /// </summary>
    private static readonly Dictionary<Wallet, WalletInfo> Table = new()
    {
        [Wallet.Roubles] = new(Wallet.Roubles, RoublesTpl, "R", "Roubles", 5_000, 50_000, 5_000),
        [Wallet.Dollars] = new(Wallet.Dollars, DollarsTpl, "$", "Dollars", 50, 500, 50),
        [Wallet.Euros] = new(Wallet.Euros, EurosTpl, "E", "Euros", 50, 500, 50),
    };

    public static WalletInfo For(Wallet wallet) => Table[wallet];

    public static IEnumerable<WalletInfo> All => Table.Values;

    /// <summary>
    /// Whether a stake is one this wallet actually takes: **any whole amount between
    /// the two ends**.
    ///
    /// It used to insist on a multiple of <see cref="Step"/> as well, because the panel
    /// only offered a button that walked the steps. The panel lets the stake be typed
    /// now, and a machine that refuses 7,500 roubles for no reason a player can see is
    /// a machine that looks broken. The step survives as what the button moves by.
    ///
    /// Both ends still matter and both are still checked here rather than trusted from
    /// the panel -- a request is a thing anybody can send by hand, and the ceiling is
    /// what keeps a thousand-times payout to a sane number.
    /// </summary>
    public static bool Allows(Wallet wallet, long stake)
    {
        var info = For(wallet);

        return stake >= info.MinStake && stake <= info.MaxStake;
    }
}
