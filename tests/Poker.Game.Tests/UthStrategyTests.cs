namespace Poker.Game.Tests;

/// <summary>
/// The published strategy, pinned decision by decision.
///
/// These are the tests that make the house-edge simulation mean anything. That
/// simulation can only say "correct play returns what correct play should return" --
/// it cannot tell you the strategy is correct, because a wrong strategy simply
/// produces a worse edge and the band would have to be wide enough to hide it.
/// </summary>
public class UthStrategyTests
{
    private static bool PreFlop(string codes)
    {
        var cards = Card.ParseMany(codes);
        return UthStrategy.RaisesOnHoleCards(cards[0], cards[1]);
    }

    private static bool Flop(string hole, string board) =>
        UthStrategy.RaisesOnFlop(Card.ParseMany(hole), Card.ParseMany(board));

    private static bool River(string hole, string board) =>
        UthStrategy.BetsOnRiver(Card.ParseMany(hole), Card.ParseMany(board));

    [Theory]
    // Any ace, however ragged the kicker.
    [InlineData("AS 2D", true)]
    [InlineData("AC 7H", true)]
    // Pairs from threes up. Twos are the exception: beaten by any board pair and
    // too rarely improved to be worth four antes.
    [InlineData("3S 3D", true)]
    [InlineData("2S 2D", false)]
    [InlineData("KS KD", true)]
    // Kings: any kicker when suited, a five or better when not.
    [InlineData("KS 2S", true)]
    [InlineData("KS 5D", true)]
    [InlineData("KS 4D", false)]
    // Queens: six suited, eight offsuit.
    [InlineData("QS 6S", true)]
    [InlineData("QS 5S", false)]
    [InlineData("QS 8D", true)]
    [InlineData("QS 7D", false)]
    // Jacks: eight suited, and only the ten offsuit.
    [InlineData("JS 8S", true)]
    [InlineData("JS 7S", false)]
    [InlineData("JS TD", true)]
    [InlineData("JS 9D", false)]
    // Tens: suited, eight or better, and nothing offsuit.
    [InlineData("TS 8S", true)]
    [InlineData("TS 7S", false)]
    [InlineData("TS 9D", false)]
    [InlineData("9S 8S", false)]
    public void TheFourTimesRaiseFollowsThePublishedRange(string hole, bool raises)
    {
        Assert.Equal(raises, PreFlop(hole));
    }

    [Fact]
    public void TwoPairOnTheFlopIsAlwaysWorthTwoAntes()
    {
        Assert.True(Flop("AS KD", "AH KS 2C"));
    }

    [Fact]
    public void AHiddenPairOnTheFlopIsWorthTwoAntesUnlessItIsTheBoardsLowestCard()
    {
        // Pairing the king is a hand. Pairing the deuce is a hand that is behind
        // more often than it is ahead, and the 2x bet is too big to make with it.
        Assert.True(Flop("KS 4D", "KH 8C 2S"));
        Assert.False(Flop("2D 4H", "KH 8C 2S"));
    }

    [Fact]
    public void APocketPairIsHiddenByConstructionSoTheLowCardExceptionCannotReachIt()
    {
        // Fives are lower than the board's lowest card here, but they are not a
        // pair *with* the board, so the exception does not apply.
        Assert.True(Flop("5S 5D", "KH 8C 6S"));
    }

    [Fact]
    public void FourToAFlushIsWorthTwoAntesOnlyWithAHighCardInIt()
    {
        // The draw is the same either way; what differs is what happens when it
        // fills. A low flush still loses to the dealer's.
        Assert.True(Flop("AS 7S", "KS 4S 2D"));
        Assert.False(Flop("5S 7S", "KS 4S 2D"));
    }

    [Fact]
    public void ABigHandThatMissedTheFlopEntirelyChecks()
    {
        Assert.False(Flop("AS KD", "7H 5C 2S"));
    }

    [Fact]
    public void AHiddenPairAtTheRiverIsWorthOneMoreAnte()
    {
        Assert.True(River("KS 4D", "KH 8C 2S 9D 3H"));
    }

    [Fact]
    public void AMarginalHandIsStillBackedBecauseFoldingCostsTwoAntes()
    {
        // The board's pair of sevens with an ace beside it. This loses to plenty --
        // betting is worth about -0.6 antes -- and it is still an easy call, because
        // giving up costs a flat -2. Folding is the expensive decision in this game,
        // and a rule of thumb that forgets it folds far too much: the first version
        // of this strategy did exactly that and cost six points of house edge.
        Assert.True(River("AS 5D", "7H 7D KS QC 2S"));
    }

    [Fact]
    public void PlayingABoardTheDealerCanBeatIsFolded()
    {
        // Ace high, entirely on the board, and the dealer sees the same five cards
        // with two more to improve on them. Worth about -2.12 against -2 to fold,
        // which is how close the genuine folds are.
        Assert.False(River("2C 3D", "AS KH QD JC 9S"));
    }

    [Fact]
    public void AnUnbeatableBoardIsBackedEvenThoughItCannotWin()
    {
        // A royal on the board cannot be improved on and cannot be beaten, so every
        // dealer holding ties and every bet pushes: betting is worth exactly zero.
        // Folding would hand over the Ante and the Blind for nothing at all.
        //
        // This is the case that shows why the river is computed rather than looked
        // up. Every plausible rule of thumb -- "you have nothing of your own",
        // "you are playing the board" -- folds here, and folding is the worst
        // available answer.
        Assert.True(River("2S 3D", "AS KS QS JS TS"));
    }

    [Fact]
    public void AStraightMadeWithTheHoleCardsIsBacked()
    {
        Assert.True(River("TS JS", "9H 8C 7D 2S 3C"));
    }

    [Fact]
    public void EveryDecisionSaysWhichRuleProducedIt()
    {
        // A bot that raises for a reason nobody can read is indistinguishable from
        // one that raises at random.
        var log = new ListGameLog();

        UthStrategy.RaisesOnHoleCards(Card.Parse("AS"), Card.Parse("2D"), log);
        UthStrategy.BetsOnRiver(Card.ParseMany("2C 3D"), Card.ParseMany("AS KH QD JC 9S"), null, log);

        Assert.True(log.Mentions("any ace, raise 4x"), log.ToString());

        // The river says what the decision was worth, not merely what it was. That
        // number is also what a seat-mate should hesitate over -- see the note on
        // thinking time in CLAUDE.md.
        Assert.True(log.Mentions("betting is worth"), log.ToString());
        Assert.True(log.Mentions("so fold"), log.ToString());
    }
}
