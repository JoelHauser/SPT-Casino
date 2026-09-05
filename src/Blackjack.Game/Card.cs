namespace Blackjack.Game;

public enum Suit
{
    Clubs,
    Diamonds,
    Hearts,
    Spades,
}

/// <summary>
/// Ranks carry their pip value so face cards collapse naturally, but Ace is
/// deliberately 1 here -- the 11 is applied by <see cref="Hand"/>, which is the
/// only place that can see the whole hand and decide whether 11 would bust.
/// </summary>
public enum Rank
{
    Ace = 1,
    Two,
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
}

public readonly record struct Card(Rank Rank, Suit Suit)
{
    /// <summary>Value with an Ace counted low. Never returns 11.</summary>
    public int BaseValue => Rank >= Rank.Ten ? 10 : (int)Rank;

    public bool IsAce => Rank == Rank.Ace;

    /// <summary>
    /// Two-character wire form ("AS", "TH", "2C"). The client renders from this,
    /// so it must stay stable -- changing it silently breaks every deployed plugin.
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

    public override string ToString() => Code;
}
