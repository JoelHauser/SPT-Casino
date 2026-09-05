using Blackjack.Game;

namespace Blackjack.Tests;

/// <summary>
/// Builds a table whose shoe deals a known sequence. Cards go out in real dealing
/// order -- player, dealer, player, dealer -- so a stack reads the way the table
/// plays it, and anything after the fourth card is the draw pile.
/// </summary>
internal static class Deal
{
    internal const int Wager = 10_000;

    internal static BlackjackTable Table(string cards, Rules? rules = null) =>
        new(rules ?? new Rules(), Shoe.Stacked(cards.Split(' ').Select(Card.Parse)));
}
