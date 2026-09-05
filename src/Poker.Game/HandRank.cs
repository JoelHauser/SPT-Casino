namespace Poker.Game;

/// <summary>
/// The nine hand categories, ordered weakest to strongest so the enum compares
/// the way the hands do.
///
/// A royal flush is not a category -- it is a straight flush with an Ace high,
/// and <see cref="HandRank.IsRoyal"/> says so. Making it its own value would put
/// a gap in the ordering that every comparison would have to know about, for a
/// distinction only the paytable cares about.
/// </summary>
public enum HandCategory
{
    HighCard = 1,
    Pair,
    TwoPair,
    ThreeOfAKind,
    Straight,
    Flush,
    FullHouse,
    FourOfAKind,
    StraightFlush,
}

/// <summary>
/// A scored five-card hand: the category plus the kickers that break ties inside
/// it, in descending order of significance.
///
/// Everything is packed into one int -- the category in the top nibble and up to
/// five ranks in the nibbles below it. That is what makes <see cref="CompareTo"/>
/// a single integer comparison instead of a category check followed by a walk
/// down two kicker lists, which is the traditional place a poker evaluator gets
/// subtly wrong. Ranks reach 14 and so fit a nibble exactly.
///
/// Unused kicker slots are zero, which is correct rather than merely convenient:
/// a hand with fewer significant kickers has already been separated by the ones
/// that came before, so what sits in the empty slots can never decide anything.
/// </summary>
public readonly record struct HandRank : IComparable<HandRank>
{
    private const int KickerCount = 5;
    private const int KickerBits = 4;
    private const int CategoryShift = KickerCount * KickerBits;

    private readonly int _score;

    private HandRank(int score) => _score = score;

    public HandCategory Category => (HandCategory)(_score >> CategoryShift);

    /// <summary>
    /// The tiebreakers, most significant first, with trailing empties trimmed.
    /// A full house is [trips, pair]; a flush is all five cards descending.
    /// </summary>
    public IReadOnlyList<Rank> Kickers
    {
        get
        {
            var kickers = new List<Rank>(KickerCount);
            for (var slot = 0; slot < KickerCount; slot++)
            {
                var shift = (KickerCount - 1 - slot) * KickerBits;
                var value = (_score >> shift) & 0xF;
                if (value == 0)
                {
                    break;
                }

                kickers.Add((Rank)value);
            }

            return kickers;
        }
    }

    /// <summary>Ace-high straight flush. Only the paytable cares.</summary>
    public bool IsRoyal => Category == HandCategory.StraightFlush && Kickers[0] == Rank.Ace;

    internal static HandRank Create(HandCategory category, params ReadOnlySpan<Rank> kickers)
    {
        if (kickers.Length > KickerCount)
        {
            throw new ArgumentException($"A hand has at most {KickerCount} kickers.", nameof(kickers));
        }

        var score = (int)category << CategoryShift;
        for (var slot = 0; slot < kickers.Length; slot++)
        {
            var shift = (KickerCount - 1 - slot) * KickerBits;
            score |= (int)kickers[slot] << shift;
        }

        return new HandRank(score);
    }

    public int CompareTo(HandRank other) => _score.CompareTo(other._score);

    public static bool operator >(HandRank left, HandRank right) => left.CompareTo(right) > 0;

    public static bool operator <(HandRank left, HandRank right) => left.CompareTo(right) < 0;

    public static bool operator >=(HandRank left, HandRank right) => left.CompareTo(right) >= 0;

    public static bool operator <=(HandRank left, HandRank right) => left.CompareTo(right) <= 0;

    /// <summary>
    /// How the hand reads on the table -- "Full house, kings over threes".
    /// Named from the kickers rather than the cards, so it is correct for a hand
    /// picked out of seven.
    /// </summary>
    public string Describe()
    {
        var k = Kickers;

        return Category switch
        {
            HandCategory.StraightFlush when IsRoyal => "Royal flush",
            HandCategory.StraightFlush => $"Straight flush, {Name(k[0])} high",
            HandCategory.FourOfAKind => $"Four of a kind, {Plural(k[0])}",
            HandCategory.FullHouse => $"Full house, {Plural(k[0])} over {Plural(k[1])}",
            HandCategory.Flush => $"Flush, {Name(k[0])} high",
            HandCategory.Straight => $"Straight, {Name(k[0])} high",
            HandCategory.ThreeOfAKind => $"Three of a kind, {Plural(k[0])}",
            HandCategory.TwoPair => $"Two pair, {Plural(k[0])} and {Plural(k[1])}",
            HandCategory.Pair => $"Pair of {Plural(k[0])}",
            _ => $"{char.ToUpperInvariant(Name(k[0])[0])}{Name(k[0])[1..]} high",
        };
    }

    public override string ToString() => Describe();

    private static string Name(Rank rank) => rank switch
    {
        Rank.Ace => "ace",
        Rank.King => "king",
        Rank.Queen => "queen",
        Rank.Jack => "jack",
        Rank.Ten => "ten",
        Rank.Nine => "nine",
        Rank.Eight => "eight",
        Rank.Seven => "seven",
        Rank.Six => "six",
        Rank.Five => "five",
        Rank.Four => "four",
        Rank.Three => "three",
        _ => "two",
    };

    private static string Plural(Rank rank) => rank == Rank.Six ? "sixes" : Name(rank) + "s";
}
