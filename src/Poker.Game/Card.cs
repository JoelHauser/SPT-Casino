namespace Poker.Game;

public enum Suit
{
    Clubs,
    Diamonds,
    Hearts,
    Spades,
}

/// <summary>
/// Ranks carry their comparison value, and Ace is deliberately high at 14 --
/// the opposite of the blackjack engine this was ported from, where Ace was 1
/// and the 11 was applied by the hand.
///
/// Poker never adds ranks together, it only orders them, so the one place an
/// Ace is low is the wheel (A-2-3-4-5). That is a single special case inside
/// straight detection rather than a property of the rank, because an Ace that
/// could be either would make every comparison ambiguous.
/// </summary>
public enum Rank
{
    Two = 2,
    Three,
    Four,
    Five,
    Six,
    Seven,
    Eight,
    Nine,
    Ten,
    Jack,
    Queen,
    King,
    Ace,
}

public readonly record struct Card(Rank Rank, Suit Suit)
{
    /// <summary>
    /// Two-character wire form ("AS", "TH", "2C"). The client renders from this,
    /// so it must stay stable -- changing it silently breaks every deployed plugin.
    /// Deliberately identical to the blackjack mod's form so the card art and the
    /// parsing on the client side port across unchanged.
    /// </summary>
    public string Code => $"{RankChar}{SuitChar}";

    private char RankChar => Rank switch
    {
        Rank.Ace => 'A',
        Rank.Ten => 'T',
        Rank.Jack => 'J',
        Rank.Queen => 'Q',
        Rank.King => 'K',
        _ => (char)('0' + (int)Rank),
    };

    private char SuitChar => Suit switch
    {
        Suit.Clubs => 'C',
        Suit.Diamonds => 'D',
        Suit.Hearts => 'H',
        _ => 'S',
    };

    /// <summary>Inverse of <see cref="Code"/>, for tests and log replay.</summary>
    public static Card Parse(string code)
    {
        if (code is not { Length: 2 })
        {
            throw new FormatException($"Card code must be two characters, got '{code}'.");
        }

        var rank = char.ToUpperInvariant(code[0]) switch
        {
            'A' => Rank.Ace,
            'T' => Rank.Ten,
            'J' => Rank.Jack,
            'Q' => Rank.Queen,
            'K' => Rank.King,
            >= '2' and <= '9' => (Rank)(code[0] - '0'),
            _ => throw new FormatException($"Unknown rank in '{code}'."),
        };

        var suit = char.ToUpperInvariant(code[1]) switch
        {
            'C' => Suit.Clubs,
            'D' => Suit.Diamonds,
            'H' => Suit.Hearts,
            'S' => Suit.Spades,
            _ => throw new FormatException($"Unknown suit in '{code}'."),
        };

        return new Card(rank, suit);
    }

    /// <summary>
    /// Parses a whole hand from space-separated codes ("AS KS QS JS TS"). Exists
    /// for the evaluator tests, which are far more readable as strings than as
    /// nested constructor calls.
    /// </summary>
    public static Card[] ParseMany(string codes) =>
        [.. codes.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(Parse)];

    public override string ToString() => Code;
}
