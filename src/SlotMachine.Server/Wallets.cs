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
/// What the stake moves in. A slot has a stake button rather than a text box, so the
/// steps are what that button walks through.
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
    /// Whether a stake is one this wallet actually takes.
    ///
    /// Checked on the server rather than trusted from the panel: the panel offers a
    /// button that walks the steps, and a request is a thing anybody can send by hand.
    /// </summary>
    public static bool Allows(Wallet wallet, long stake)
    {
        var info = For(wallet);

        return stake >= info.MinStake
            && stake <= info.MaxStake
            && stake % info.Step == 0;
    }
}
