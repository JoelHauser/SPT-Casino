namespace War.Game.Tests;

public class CardAndShoeTests
{
    /// <summary>
    /// The codes are the art's file names. A code the panel cannot find a picture for
    /// still draws, but as a plain drawn card, which would look like a different deck.
    /// </summary>
    [Theory]
    [InlineData(2, Suit.Clubs, "2C")]
    [InlineData(9, Suit.Hearts, "9H")]
    [InlineData(10, Suit.Diamonds, "TD")]
    [InlineData(11, Suit.Spades, "JS")]
    [InlineData(12, Suit.Hearts, "QH")]
    [InlineData(13, Suit.Clubs, "KC")]
    [InlineData(14, Suit.Spades, "AS")]
    public void ACardsCodeIsRankThenSuit(int rank, Suit suit, string code) =>
        Assert.Equal(code, new Card(rank, suit).Code);

    [Fact]
    public void AcesAreHigh()
    {
        Assert.True(Deal.Parse("AS").Rank > Deal.Parse("KS").Rank);
        Assert.True(Deal.Parse("2S").Rank < Deal.Parse("3S").Rank);
    }

    /// <summary>
    /// Six whole decks: 312 cards, 24 of each rank, six of each exact card. A shoe one
    /// card short of a rank shifts the tie chance, and with it every edge in Odds.
    /// </summary>
    [Fact]
    public void AFreshShoeIsSixWholeDecks()
    {
        var shoe = new Shoe(Rules.Decks, new Random(1));
        var cards = Enumerable.Range(0, shoe.Size).Select(_ => shoe.Draw()).ToList();

        Assert.Equal(312, cards.Count);
        Assert.All(cards.GroupBy(c => c.Rank), g => Assert.Equal(24, g.Count()));
        Assert.Equal(13, cards.Select(c => c.Rank).Distinct().Count());
        Assert.All(cards.GroupBy(c => c.Code), g => Assert.Equal(6, g.Count()));
    }

    [Fact]
    public void TheShoeIsActuallyShuffled()
    {
        var a = new Shoe(Rules.Decks, new Random(1));
        var b = new Shoe(Rules.Decks, new Random(2));

        var first = Enumerable.Range(0, 20).Select(_ => a.Draw().Code);
        var second = Enumerable.Range(0, 20).Select(_ => b.Draw().Code);

        Assert.NotEqual(first, second);
    }

    /// <summary>
    /// The table reshuffles between rounds once the cut card is out, and only then.
    /// Played to well past one shoe, no round ever finds it empty.
    /// </summary>
    [Fact]
    public void TheTableReshufflesBeforeTheShoeCanRunOut()
    {
        var table = new WarTable(new Random(7));
        var shuffles = 0;

        for (var i = 0; i < 2_000; i++)
        {
            var round = table.Deal(10, 0);

            if (round.Shuffled)
            {
                shuffles++;
            }

            if (round.Phase == Phase.AwaitingDecision)
            {
                table.GoToWar();
            }

            Assert.True(table.CardsLeft >= Rules.ReshuffleBelow - Rules.MostCardsPerRound);
        }

        Assert.True(shuffles > 5, $"only {shuffles} shuffles in 2,000 rounds");
    }
}
