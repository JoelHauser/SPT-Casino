namespace Poker.Game.Tests;

public class CategoryRecognitionTests
{
    [Theory]
    [InlineData("AS KS QS JS TS", HandCategory.StraightFlush)]
    [InlineData("9H 8H 7H 6H 5H", HandCategory.StraightFlush)]
    [InlineData("7C 7D 7H 7S KD", HandCategory.FourOfAKind)]
    [InlineData("4C 4D 4H 9S 9D", HandCategory.FullHouse)]
    [InlineData("AD JD 8D 5D 2D", HandCategory.Flush)]
    [InlineData("9C 8D 7H 6S 5C", HandCategory.Straight)]
    [InlineData("QC QD QH 7S 2C", HandCategory.ThreeOfAKind)]
    [InlineData("JC JD 3H 3S KC", HandCategory.TwoPair)]
    [InlineData("6C 6D KH 9S 2C", HandCategory.Pair)]
    [InlineData("AC QD 9H 5S 3C", HandCategory.HighCard)]
    public void EachCategoryIsRecognised(string cards, HandCategory expected) =>
        Assert.Equal(expected, HandEvaluator.Evaluate(cards).Category);

    [Fact]
    public void CategoriesRankInTheOrderTheEnumDeclaresThem()
    {
        // One representative of each, weakest first. Pinning the whole chain in one
        // test means a category that lands in the wrong band cannot hide behind a
        // neighbour it still happens to beat.
        string[] ascending =
        [
            "AC QD 9H 5S 3C",   // high card
            "6C 6D KH 9S 2C",   // pair
            "JC JD 3H 3S KC",   // two pair
            "QC QD QH 7S 2C",   // trips
            "9C 8D 7H 6S 5C",   // straight
            "AD JD 8D 5D 2D",   // flush
            "4C 4D 4H 9S 9D",   // full house
            "7C 7D 7H 7S KD",   // quads
            "AS KS QS JS TS",   // straight flush
        ];

        for (var i = 1; i < ascending.Length; i++)
        {
            var weaker = HandEvaluator.Evaluate(ascending[i - 1]);
            var stronger = HandEvaluator.Evaluate(ascending[i]);

            Assert.True(stronger > weaker, $"{stronger.Describe()} should beat {weaker.Describe()}");
        }
    }
}

public class StraightTests
{
    [Fact]
    public void AceLowStraightIsFiveHighNotAceHigh()
    {
        var wheel = HandEvaluator.Evaluate("AC 2D 3H 4S 5C");

        Assert.Equal(HandCategory.Straight, wheel.Category);
        Assert.Equal(Rank.Five, wheel.Kickers[0]);
    }

    [Fact]
    public void TheWheelIsTheWeakestStraight()
    {
        var wheel = HandEvaluator.Evaluate("AC 2D 3H 4S 5C");
        var sixHigh = HandEvaluator.Evaluate("2C 3D 4H 5S 6C");

        Assert.True(sixHigh > wheel);
    }

    [Fact]
    public void AceHighStraightIsBroadway()
    {
        var broadway = HandEvaluator.Evaluate("AC KD QH JS TC");

        Assert.Equal(HandCategory.Straight, broadway.Category);
        Assert.Equal(Rank.Ace, broadway.Kickers[0]);
    }

    [Fact]
    public void RanksDoNotWrapAroundTheAce()
    {
        // Q-K-A-2-3 is a hand people expect to be a straight exactly once.
        Assert.Equal(HandCategory.HighCard, HandEvaluator.Evaluate("QC KD AH 2S 3C").Category);
    }

    [Fact]
    public void FourToAStraightIsNotAStraight()
    {
        Assert.Equal(HandCategory.HighCard, HandEvaluator.Evaluate("KC QD JH TS 2C").Category);
        Assert.Equal(HandCategory.HighCard, HandEvaluator.Evaluate("AC KD QH JS 9C").Category);
    }

    [Fact]
    public void SuitedWheelIsAStraightFlush()
    {
        var steelWheel = HandEvaluator.Evaluate("AH 2H 3H 4H 5H");

        Assert.Equal(HandCategory.StraightFlush, steelWheel.Category);
        Assert.Equal(Rank.Five, steelWheel.Kickers[0]);
        Assert.False(steelWheel.IsRoyal);
    }

    [Fact]
    public void OnlyAnAceHighStraightFlushIsRoyal()
    {
        Assert.True(HandEvaluator.Evaluate("AS KS QS JS TS").IsRoyal);
        Assert.False(HandEvaluator.Evaluate("KS QS JS TS 9S").IsRoyal);
    }
}

public class TiebreakTests
{
    [Fact]
    public void HigherPairBeatsLowerPairRegardlessOfKickers()
    {
        var kings = HandEvaluator.Evaluate("KC KD 4H 3S 2C");
        var queens = HandEvaluator.Evaluate("QC QD AH KS JC");

        Assert.True(kings > queens);
    }

    [Fact]
    public void EqualPairsAreSeparatedByTheirKickersInOrder()
    {
        var better = HandEvaluator.Evaluate("9C 9D AH 7S 4C");
        var worse = HandEvaluator.Evaluate("9H 9S AC 7D 3C");

        // Identical down to the last kicker, so the four beats the three.
        Assert.True(better > worse);
    }

    [Fact]
    public void IdenticalHandsOfDifferentSuitsTie()
    {
        // Suits never break a tie in poker, so these must compare equal rather
        // than merely close -- a split pot depends on it.
        Assert.Equal(HandEvaluator.Evaluate("AC KD 9H 5S 3C"), HandEvaluator.Evaluate("AH KS 9C 5D 3H"));
    }

    [Fact]
    public void QuadsAreSeparatedByTheirKicker()
    {
        var better = HandEvaluator.Evaluate("7C 7D 7H 7S AD");
        var worse = HandEvaluator.Evaluate("7C 7D 7H 7S KD");

        Assert.True(better > worse);
    }

    [Fact]
    public void FullHouseIsRankedByTheTripsBeforeThePair()
    {
        var ninesFullOfTwos = HandEvaluator.Evaluate("9C 9D 9H 2S 2C");
        var eightsFullOfAces = HandEvaluator.Evaluate("8C 8D 8H AS AC");

        Assert.True(ninesFullOfTwos > eightsFullOfAces);
    }

    [Fact]
    public void TwoPairIsRankedByTheHigherPairFirst()
    {
        var acesAndTwos = HandEvaluator.Evaluate("AC AD 2H 2S 5C");
        var kingsAndQueens = HandEvaluator.Evaluate("KC KD QH QS 5C");

        Assert.True(acesAndTwos > kingsAndQueens);
    }

    [Fact]
    public void FlushesAreComparedCardByCardFromTheTop()
    {
        var better = HandEvaluator.Evaluate("AD QD 9D 5D 3D");
        var worse = HandEvaluator.Evaluate("AH QH 9H 5H 2H");

        Assert.True(better > worse);
    }
}

public class BestOfSevenTests
{
    [Fact]
    public void SevenCardsAreRankedByTheirBestFive()
    {
        // Two extra cards that make nothing; the flush is the hand.
        var result = HandEvaluator.Best(Card.ParseMany("AD KD 9D 5D 3D 7C 2S"));

        Assert.Equal(HandCategory.Flush, result.Rank.Category);
        Assert.Equal(5, result.Cards.Count);
        Assert.All(result.Cards, card => Assert.Equal(Suit.Diamonds, card.Suit));
    }

    [Fact]
    public void ThePlayingFiveIsReturnedNotJustTheScore()
    {
        var result = HandEvaluator.Best(Card.ParseMany("AC AD AH 4S 4C 9D 2S"));

        Assert.Equal(HandCategory.FullHouse, result.Rank.Category);
        Assert.Equal(
            ["4C", "4S", "AC", "AD", "AH"],
            result.Cards.Select(c => c.Code).Order().ToArray());
    }

    [Fact]
    public void ASixCardHandIsAlsoRankedByItsBestFive()
    {
        var result = HandEvaluator.Best(Card.ParseMany("AC KD QH JS TC 2D"));

        Assert.Equal(HandCategory.Straight, result.Rank.Category);
        Assert.Equal(Rank.Ace, result.Rank.Kickers[0]);
    }

    [Fact]
    public void TheBestFiveIsPreferredOverALongerRunOfTheSameCategory()
    {
        // Six to a straight: the nine-high run must lose to the ten-high one.
        var result = HandEvaluator.Best(Card.ParseMany("TC 9D 8H 7S 6C 5D 2S"));

        Assert.Equal(HandCategory.Straight, result.Rank.Category);
        Assert.Equal(Rank.Ten, result.Rank.Kickers[0]);
    }

    [Fact]
    public void SevenCardsHoldingBothAFlushAndAStraightRankAsTheFlush()
    {
        var result = HandEvaluator.Best(Card.ParseMany("9H 8H 7H 2H 5H 6C 5C"));

        Assert.Equal(HandCategory.Flush, result.Rank.Category);
    }

    [Theory]
    [InlineData(4)]
    [InlineData(8)]
    public void FewerThanFiveOrMoreThanSevenCardsIsRefused(int count)
    {
        var cards = new Deck(new Random(1)).Draw(count);

        Assert.Throws<ArgumentException>(() => HandEvaluator.Best(cards));
    }
}

public class DescriptionTests
{
    [Theory]
    [InlineData("AS KS QS JS TS", "Royal flush")]
    [InlineData("9H 8H 7H 6H 5H", "Straight flush, nine high")]
    [InlineData("7C 7D 7H 7S KD", "Four of a kind, sevens")]
    [InlineData("4C 4D 4H 9S 9D", "Full house, fours over nines")]
    [InlineData("AD JD 8D 5D 2D", "Flush, ace high")]
    [InlineData("9C 8D 7H 6S 5C", "Straight, nine high")]
    [InlineData("QC QD QH 7S 2C", "Three of a kind, queens")]
    [InlineData("JC JD 3H 3S KC", "Two pair, jacks and threes")]
    [InlineData("6C 6D KH 9S 2C", "Pair of sixes")]
    [InlineData("AC QD 9H 5S 3C", "Ace high")]
    public void HandsDescribeThemselvesTheWayTheyAreCalledAtTheTable(string cards, string expected) =>
        Assert.Equal(expected, HandEvaluator.Evaluate(cards).Describe());
}
