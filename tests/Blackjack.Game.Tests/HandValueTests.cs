using Blackjack.Game;

namespace Blackjack.Tests;

public class HandValueTests
{
    private static Hand HandOf(params string[] codes)
    {
        // Wager is irrelevant to scoring, so every hand here is built at zero.
        var hand = new Hand(0);
        foreach (var code in codes)
        {
            hand.Add(Card.Parse(code));
        }

        return hand;
    }

    [Theory]
    [InlineData(20, "KS", "QH")]
    [InlineData(21, "AS", "KH")]
    [InlineData(13, "AS", "2H")]
    [InlineData(16, "AS", "KH", "5D")]
    [InlineData(12, "AS", "AH")]
    [InlineData(21, "AS", "AH", "9D")]
    [InlineData(13, "AS", "AH", "AD")]
    [InlineData(22, "KS", "QH", "2D")]
    public void ScoresHandsWithAcesCorrectly(int expected, params string[] cards)
    {
        Assert.Equal(expected, HandOf(cards).Value);
    }

    [Fact]
    public void AceCountsAsElevenOnlyWhileItFits()
    {
        Assert.True(HandOf("AS", "6H").IsSoft);

        // Soft 17 becomes hard 17 once a ten lands on it -- 11 would be 27.
        Assert.False(HandOf("AS", "6H", "KD").IsSoft);
        Assert.Equal(17, HandOf("AS", "6H", "KD").Value);
    }

    [Fact]
    public void TwentyOneOnThreeCardsIsNotABlackjack()
    {
        Assert.True(HandOf("AS", "KH").IsBlackjack);
        Assert.False(HandOf("7S", "7H", "7D").IsBlackjack);
    }

    [Fact]
    public void PairsSplitOnValueSoAnyTwoTensQualify()
    {
        Assert.True(HandOf("8S", "8H").CanSplit);
        Assert.True(HandOf("KS", "TH").CanSplit);
        Assert.False(HandOf("9S", "TH").CanSplit);
    }
}
