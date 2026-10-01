namespace War.Game;

/// <summary>
/// The house rules, as Cache Creek prints them (cachecreek.com/casino-war):
///
/// - Aces are high.
/// - The main bet pays even money.
/// - The optional tie bet pays 10 to 1 when the first two cards are the same rank.
/// - On a tie the player either **surrenders**, forfeiting half the main bet, or
///   **goes to war** by matching it. The dealer burns three cards and deals one more
///   to each side. If the player's card is equal to or higher than the dealer's, the
///   raise wins even money and the original bet is returned as a push; otherwise
///   both are lost.
///
/// The page does not say how many decks. Six is the usual shoe, and it is the only
/// common count its own figures agree with: the "over 18 percent" it quotes for the
/// tie bet is 18.65% at six decks, against 35.3% at one and 17.8% at eight. Its 2.88%
/// for the main bet is six decks too. See <see cref="Odds"/>, and the test that holds
/// all four numbers.
/// </summary>
public static class Rules
{
    public const int Decks = 6;

    /// <summary>The tie bet's odds. It returns this many times the bet, plus the bet.</summary>
    public const int TiePays = 10;

    /// <summary>Cards the dealer burns before the war is dealt.</summary>
    public const int WarBurn = 3;

    /// <summary>
    /// The most cards one round can use: two to deal, three burned, two for the war.
    /// </summary>
    public const int MostCardsPerRound = 2 + WarBurn + 2;

    /// <summary>
    /// The cut card. A fresh shoe is shuffled before a round starts with fewer than
    /// this left -- a quarter of the shoe -- so a round can never run out mid-war.
    /// </summary>
    public const int ReshuffleBelow = Decks * 52 / 4;
}
