namespace War.Game;

public enum Suit
{
    Clubs,
    Diamonds,
    Hearts,
    Spades,
}

/// <summary>
/// One card. War only ever asks one question of it -- which is higher -- so the rank
/// is a plain number with the ace at the top: 2 is the lowest, 14 is the ace.
///
/// Suits never matter to the game. They are carried because the art does, and a
/// table that dealt the same suit every time would look like it was cheating.
/// </summary>
public readonly record struct Card(int Rank, Suit Suit)
{
    public const int Lowest = 2;

    public const int Ace = 14;

    /// <summary>
    /// Two characters, rank then suit: "TD" is the ten of diamonds, "AS" the ace of
    /// spades. The same codes Blackjack and Poker send, and the names of the card
    /// images beside the plugin, so the panel draws these with no table of its own.
    /// </summary>
    public string Code => $"{RankChar(Rank)}{SuitChar(Suit)}";

    public override string ToString() => Code;

    private static char RankChar(int rank) => rank switch
    {
        >= 2 and <= 9 => (char)('0' + rank),
        10 => 'T',
        11 => 'J',
        12 => 'Q',
        13 => 'K',
        14 => 'A',
        _ => throw new ArgumentOutOfRangeException(nameof(rank), rank, "A rank runs from 2 to 14."),
    };

    private static char SuitChar(Suit suit) => suit switch
    {
        Suit.Clubs => 'C',
        Suit.Diamonds => 'D',
        Suit.Hearts => 'H',
        Suit.Spades => 'S',
        _ => throw new ArgumentOutOfRangeException(nameof(suit), suit, null),
    };
}
