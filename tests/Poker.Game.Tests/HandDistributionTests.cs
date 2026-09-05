namespace Poker.Game.Tests;

/// <summary>
/// Deals every five-card hand there is and counts the categories.
///
/// This is the test that actually proves the evaluator. Hand-written cases pin the
/// rules someone thought to write down; the frequencies of the 2,598,960 distinct
/// five-card hands are published, settled arithmetic, and an evaluator that
/// misreads even one hand in the deck lands off them. It has caught, in other
/// people's evaluators: the wheel counted as ace-high, wrap-around straights,
/// straight flushes double-counted as both, and trips-plus-pair falling through
/// to two pair.
///
/// Roughly two seconds. Worth every one of them -- see the note in CLAUDE.md
/// about distrusting a suite that passes first time.
/// </summary>
public class HandDistributionTests
{
    private const int DistinctFiveCardHands = 2_598_960;

    [Fact]
    public void EveryFiveCardHandInTheDeckFallsWhereTheMathSaysItShould()
    {
        var deck = FullDeck();
        var counts = new Dictionary<HandCategory, int>();
        var royals = 0;
        var total = 0;

        var five = new Card[5];
        for (var a = 0; a < 48; a++)
        for (var b = a + 1; b < 49; b++)
        for (var c = b + 1; c < 50; c++)
        for (var d = c + 1; d < 51; d++)
        for (var e = d + 1; e < 52; e++)
        {
            five[0] = deck[a];
            five[1] = deck[b];
            five[2] = deck[c];
            five[3] = deck[d];
            five[4] = deck[e];

            var rank = HandEvaluator.Evaluate(five);
            counts[rank.Category] = counts.GetValueOrDefault(rank.Category) + 1;
            if (rank.IsRoyal)
            {
                royals++;
            }

            total++;
        }

        Assert.Equal(DistinctFiveCardHands, total);

        // Straights and flushes here exclude straight flushes, which is the
        // convention the published figures use.
        Assert.Equal(40, counts[HandCategory.StraightFlush]);
        Assert.Equal(624, counts[HandCategory.FourOfAKind]);
        Assert.Equal(3_744, counts[HandCategory.FullHouse]);
        Assert.Equal(5_108, counts[HandCategory.Flush]);
        Assert.Equal(10_200, counts[HandCategory.Straight]);
        Assert.Equal(54_912, counts[HandCategory.ThreeOfAKind]);
        Assert.Equal(123_552, counts[HandCategory.TwoPair]);
        Assert.Equal(1_098_240, counts[HandCategory.Pair]);
        Assert.Equal(1_302_540, counts[HandCategory.HighCard]);

        Assert.Equal(4, royals);
    }

    [Fact]
    public void EveryHandDescribesItselfWithoutThrowing()
    {
        // Describe indexes into Kickers per category. A category that reports fewer
        // kickers than its description reads would throw here and nowhere else,
        // because the panel is the only caller and there is no panel yet.
        var deck = FullDeck();
        var five = new Card[5];

        for (var a = 0; a < 48; a++)
        for (var b = a + 1; b < 49; b++)
        for (var c = b + 1; c < 50; c++)
        for (var d = c + 1; d < 51; d++)
        for (var e = d + 1; e < 52; e++)
        {
            five[0] = deck[a];
            five[1] = deck[b];
            five[2] = deck[c];
            five[3] = deck[d];
            five[4] = deck[e];

            Assert.False(string.IsNullOrWhiteSpace(HandEvaluator.Evaluate(five).Describe()));
        }
    }

    private static Card[] FullDeck()
    {
        var cards = new List<Card>(52);
        foreach (Suit suit in Enum.GetValues<Suit>())
        {
            foreach (Rank rank in Enum.GetValues<Rank>())
            {
                cards.Add(new Card(rank, suit));
            }
        }

        return [.. cards];
    }
}
