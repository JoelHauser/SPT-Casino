namespace War.Game.Tests;

/// <summary>Builds stacked shoes out of readable card codes.</summary>
internal static class Deal
{
    /// <summary>
    /// A table whose shoe deals exactly these cards, in order: player, dealer, then
    /// for a war three burns, the player's war card and the dealer's.
    /// </summary>
    internal static WarTable Stacked(params string[] codes) =>
        new(Shoe.Stacked(codes.Select(Parse)));

    internal static Card Parse(string code)
    {
        var rank = code[0] switch
        {
            'T' => 10,
            'J' => 11,
            'Q' => 12,
            'K' => 13,
            'A' => 14,
            var digit => digit - '0',
        };

        var suit = code[1] switch
        {
            'C' => Suit.Clubs,
            'D' => Suit.Diamonds,
            'H' => Suit.Hearts,
            'S' => Suit.Spades,
            _ => throw new ArgumentException($"No suit '{code[1]}'.", nameof(code)),
        };

        return new Card(rank, suit);
    }
}
