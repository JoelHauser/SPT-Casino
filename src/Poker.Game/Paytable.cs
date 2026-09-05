namespace Poker.Game;

/// <summary>
/// What a bet pays, as odds against the stake: 3:2 is <c>Odds(3, 2)</c>.
///
/// Odds rather than a multiplier because half the table is not a whole number and
/// a double would quietly stop being exact. It also keeps the question a paytable
/// has to answer for an indivisible stake -- see <see cref="DividesExactly"/> --
/// answerable without floating point.
///
/// A push and a loss are both payouts here rather than states the caller tracks
/// separately. The Blind is the reason: it does not lose on a hand below a
/// straight, it pushes, and a settlement that treats those alike takes money it
/// was never owed.
/// </summary>
public readonly record struct Payout : IComparable<Payout>
{
    private readonly int _denominator;

    private Payout(int numerator, int denominator)
    {
        Numerator = numerator;
        _denominator = denominator;
    }

    public int Numerator { get; }

    /// <summary>
    /// Reads 1 when unset, so <c>default(Payout)</c> is a push rather than a
    /// division by zero. <see cref="Push"/> is that default, which is what keeps
    /// the two equal to each other.
    /// </summary>
    public int Denominator => _denominator == 0 ? 1 : _denominator;

    /// <summary>Stake back and nothing more.</summary>
    public static Payout Push => default;

    /// <summary>Stake gone.</summary>
    public static Payout Loss => new(-1, 1);

    public static Payout Odds(int numerator, int denominator = 1)
    {
        if (denominator < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(denominator), denominator, "Odds are stated over a positive denominator.");
        }

        if (numerator < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(numerator), numerator, $"A losing bet is {nameof(Loss)}, not negative odds.");
        }

        return new Payout(numerator, denominator);
    }

    public bool IsLoss => Numerator < 0;

    public bool IsPush => Numerator == 0;

    /// <summary>
    /// Winnings on top of the stake. Zero for a push, and zero for a loss -- a lost
    /// bet wins nothing; that the stake is gone as well is <see cref="Returned"/>'s
    /// business.
    /// </summary>
    public int Profit(int stake)
    {
        if (stake < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(stake), stake, "A stake cannot be negative.");
        }

        if (IsLoss)
        {
            return 0;
        }

        var product = (long)stake * Numerator;
        var profit = product / Denominator;

        // Half rounds up, which is how Blackjack settled its 3:2 and is the only
        // rounding a player will not argue about. It matters on the Blind's 3:2
        // and nowhere else in the standard tables.
        if (product % Denominator * 2 >= Denominator)
        {
            profit++;
        }

        // Deliberately checked. A payout too large for an int means the wallet
        // ceilings are wrong, and wrapping would pay a negative amount -- a bug
        // that reads to a player as the table taking their winnings.
        return checked((int)profit);
    }

    /// <summary>Stake plus winnings, which is what actually goes back to the player.</summary>
    public int Returned(int stake) => IsLoss ? 0 : stake + Profit(stake);

    /// <summary>
    /// Whether this pays a whole number on that stake.
    ///
    /// The question only valuables ask. One bitcoin at 3:2 settles on two and a
    /// half coins and half a coin does not exist, which is the entire reason the
    /// capped table below exists.
    /// </summary>
    public bool DividesExactly(int stake) => IsLoss || (long)stake * Numerator % Denominator == 0;

    /// <summary>Cross-multiplied, so 3:2 and 6:4 compare equal and neither divides.</summary>
    public int CompareTo(Payout other) =>
        ((long)Numerator * other.Denominator).CompareTo((long)other.Numerator * Denominator);

    public override string ToString() => this switch
    {
        { IsLoss: true } => "loses",
        { IsPush: true } => "pushes",
        { Denominator: 1 } => $"{Numerator}:1",
        _ => $"{Numerator}:{Denominator}",
    };
}

/// <summary>
/// One line of a paytable. <paramref name="RoyalOnly"/> exists because a royal
/// flush is not a category of its own -- it is an ace-high straight flush -- so
/// the royal row has to sit above the straight-flush row and claim only the hands
/// <see cref="HandRank.IsRoyal"/> is true for.
/// </summary>
public readonly record struct PaytableRow(HandCategory Category, bool RoyalOnly, Payout Payout)
{
    public static PaytableRow For(HandCategory category, Payout payout) => new(category, false, payout);

    public static PaytableRow Royal(Payout payout) => new(HandCategory.StraightFlush, true, payout);
}

/// <summary>
/// A bet's paytable, as data.
///
/// Data rather than a switch so the capped table valuables are paid on is a
/// different table and not a second code path through settlement. A rule that
/// exists twice is a rule that will disagree with itself.
///
/// Rows are strongest first and the first match wins.
/// </summary>
public sealed record Paytable(string Name, Payout Below, IReadOnlyList<PaytableRow> Rows)
{
    /// <summary>
    /// The standard Blind paytable. Pays only a straight or better and **pushes**
    /// beneath that, so a winning hand never loses its Blind.
    ///
    /// The 500:1 top row is what makes this mod's payout scale a problem worth
    /// thinking about -- see the notes on wallet ceilings.
    /// </summary>
    public static Paytable Blind { get; } = new(
        "Blind",
        Payout.Push,
        [
            PaytableRow.Royal(Payout.Odds(500)),
            PaytableRow.For(HandCategory.StraightFlush, Payout.Odds(50)),
            PaytableRow.For(HandCategory.FourOfAKind, Payout.Odds(10)),
            PaytableRow.For(HandCategory.FullHouse, Payout.Odds(3)),
            PaytableRow.For(HandCategory.Flush, Payout.Odds(3, 2)),
            PaytableRow.For(HandCategory.Straight, Payout.Odds(1)),
        ]);

    /// <summary>
    /// The Blind, capped, for stakes that cannot be divided or held in quantity.
    ///
    /// Everything above a flush pays 3:1, which drops the worst case on a hand from
    /// 511 times the Ante to 14. Bitcoin and Lega medals have a stack limit of one,
    /// so a 500:1 payout is 500 separate items needing 500 free grid cells -- past
    /// what a stash holds, past what mail rescues, and the payout is simply lost.
    ///
    /// Every row divides a single unit exactly, which the standard table's 3:2 does
    /// not. That is the second reason this table exists and not merely a lower
    /// ceiling on the first one.
    /// </summary>
    public static Paytable BlindForValuables { get; } = new(
        "Blind (valuables)",
        Payout.Push,
        [
            PaytableRow.Royal(Payout.Odds(3)),
            PaytableRow.For(HandCategory.StraightFlush, Payout.Odds(3)),
            PaytableRow.For(HandCategory.FourOfAKind, Payout.Odds(3)),
            PaytableRow.For(HandCategory.FullHouse, Payout.Odds(3)),
            PaytableRow.For(HandCategory.Flush, Payout.Odds(2)),
            PaytableRow.For(HandCategory.Straight, Payout.Odds(1)),
        ]);

    /// <summary>
    /// Trips, the side bet on the player's own hand.
    ///
    /// It ignores the dealer entirely -- it pays on a folded hand and on a losing
    /// one -- and unlike the Blind it **loses** beneath its bottom row rather than
    /// pushing. That difference is the one to get right.
    ///
    /// This is the common pay table; several others are in circulation, which is why
    /// it is written down here rather than assumed.
    /// </summary>
    public static Paytable Trips { get; } = new(
        "Trips",
        Payout.Loss,
        [
            PaytableRow.Royal(Payout.Odds(50)),
            PaytableRow.For(HandCategory.StraightFlush, Payout.Odds(40)),
            PaytableRow.For(HandCategory.FourOfAKind, Payout.Odds(30)),
            PaytableRow.For(HandCategory.FullHouse, Payout.Odds(8)),
            PaytableRow.For(HandCategory.Flush, Payout.Odds(7)),
            PaytableRow.For(HandCategory.Straight, Payout.Odds(4)),
            PaytableRow.For(HandCategory.ThreeOfAKind, Payout.Odds(3)),
        ]);

    /// <summary>What this table pays on that hand.</summary>
    public Payout For(HandRank hand, IGameLog? log = null)
    {
        foreach (var row in Rows)
        {
            if (row.Category != hand.Category || (row.RoyalOnly && !hand.IsRoyal))
            {
                continue;
            }

            if (log?.Enabled == true)
            {
                log.Write($"{Name}: {hand.Describe()} pays {row.Payout}");
            }

            return row.Payout;
        }

        if (log?.Enabled == true)
        {
            log.Write($"{Name}: {hand.Describe()} is beneath the table and {Below}");
        }

        return Below;
    }

    /// <summary>The most this table can pay, as a multiple of the stake. For ceilings.</summary>
    public Payout Best => Rows.Count == 0 ? Below : Rows.Max(row => row.Payout);

    public override string ToString() => $"{Name} (top {Best})";
}
