namespace Poker.Game.Tests;

public class SeatMateAgentTests
{
    private static SeatContext Context(UltimateHoldemTable table, Seat seat) => new(
        seat,
        table.CurrentStreet!.Value,
        table.Community,
        table.AvailablePlayMultiples(),
        table.Rules);

    [Fact]
    public void TheCorrectPersonalityPlaysTheStrategyAndNothingElse()
    {
        // The oracle. If this drifts, the house-edge figure stops meaning anything.
        var rng = new Random(99);

        for (var hand = 0; hand < 200; hand++)
        {
            var table = new UltimateHoldemTable(rng: rng);
            table.Deal(100);

            var agent = new SeatMateAgent(SeatPersonality.Correct, new Random(1));
            var decision = agent.Decide(Context(table, table.Player));

            var expected = UthStrategy.RaisesOnHoleCards(table.Player.Cards[0], table.Player.Cards[1])
                ? SeatDecision.Play(4)
                : SeatDecision.Check;

            Assert.Equal(expected, decision);
        }
    }

    [Fact]
    public void ALooseSeatBacksHandsTheStrategyWouldCheck()
    {
        // 2-7 offsuit is the clearest check there is. A dial set to certainty turns
        // it into a raise, which is what a personality is.
        var table = new UltimateHoldemTable(new Rules(), Deck.Stacked("2S 5D 7H 9C KH 8C 2D 4S 3C"));
        table.Deal(100);

        var loose = new SeatMateAgent(new SeatPersonality("Reckless", Looseness: 1.0), new Random(1));

        Assert.False(UthStrategy.RaisesOnHoleCards(table.Player.Cards[0], table.Player.Cards[1]));
        Assert.Equal(SeatMove.Play, loose.Decide(Context(table, table.Player)).Move);
    }

    [Fact]
    public void ATimidSeatTakesTheSmallRaiseWhichIsNeverCorrect()
    {
        // 3x is strictly worse than 4x with a hand worth raising. That it is a
        // mistake is the point -- it is what makes the seat read as a person.
        var table = new UltimateHoldemTable(new Rules(), Deck.Stacked("AS 5D AD 9C KH 8C 2D 4S 3C"));
        table.Deal(100);

        var timid = new SeatMateAgent(new SeatPersonality("Nervy", Timidity: 1.0), new Random(1));

        Assert.Equal(SeatDecision.Play(3), timid.Decide(Context(table, table.Player)));
    }

    [Fact]
    public void ACautiousSeatFoldsRiversItShouldBack()
    {
        var table = new UltimateHoldemTable(new Rules(), Deck.Stacked("KS 4D 4H 9C KH 8C 2S 9D 3H"));
        table.Deal(100);
        table.Check();
        table.Check();

        var cautious = new SeatMateAgent(new SeatPersonality("Nervy", Caution: 1.0), new Random(1));

        Assert.True(UthStrategy.BetsOnRiver(table.Player.Cards, table.Community));
        Assert.Equal(SeatMove.Fold, cautious.Decide(Context(table, table.Player)).Move);
    }

    [Fact]
    public void RetuningADialDoesNotChangeHowManyNumbersASeatDraws()
    {
        // Otherwise a seeded multi-seat table deals differently the moment a
        // personality is adjusted, and every pinned deal in the suite quietly
        // becomes a test of something else.
        var table = new UltimateHoldemTable(new Rules(), Deck.Stacked("AS 5D AD 9C KH 8C 2D 4S 3C"));
        table.Deal(100);

        var first = new Random(4);
        var second = new Random(4);

        new SeatMateAgent(SeatPersonality.Correct, first).Decide(Context(table, table.Player));
        new SeatMateAgent(new SeatPersonality("Wild", 0.9, 0.9, 0.9), second).Decide(Context(table, table.Player));

        Assert.Equal(first.NextDouble(), second.NextDouble());
    }

    [Fact]
    public void ASeatMateIsHandedNothingItCouldCheatWith()
    {
        // Structural, not a promise. The context is the agent's only input, and it
        // carries this seat's cards and the board -- there is no route from it to
        // the dealer's hand, another seat's, or to any money.
        var table = new UltimateHoldemTable(new Rules(), Deck.Stacked("AS KS QS JS TS 9S 8S 7S 6S 5S 4S"), 2, agent: new SeatMateAgent());
        table.Deal(100);

        var context = Context(table, table.Seats[1]);

        Assert.Equal(table.Seats[1].Cards, context.Seat.Cards);
        Assert.Empty(context.Community);        // nothing is showing before the flop
        Assert.Equal([4, 3], context.LegalMultiples);
    }

    [Fact]
    public void ATableOfSeatMatesPlaysItselfToSettlement()
    {
        var log = new ListGameLog();
        var table = new UltimateHoldemTable(
            new Rules(), seats: 5, rng: new Random(3), log: log,
            agent: new SeatMateAgent(SeatPersonality.Cast[1], new Random(3), log));

        table.Deal(100);
        table.Play(4);

        Assert.Equal(TablePhase.Settled, table.Phase);
        Assert.All(table.Seats, seat => Assert.NotEqual(SeatOutcome.Pending, seat.Outcome));
        Assert.True(log.Mentions("Loose"), log.ToString());
    }
}
