namespace Poker.Game.Tests;

public class UltimateHoldemTableTests
{
    /// <summary>A seat-mate that answers however the test tells it to.</summary>
    private sealed class TestAgent(Func<SeatContext, SeatDecision> decide) : ISeatAgent
    {
        public SeatDecision Decide(SeatContext context) => decide(context);
    }

    private static ISeatAgent AlwaysChecks => new TestAgent(context =>
        context.Street == Street.River ? SeatDecision.Fold : SeatDecision.Check);

    /// <summary>
    /// A table on a pinned deck. Cards go round the seats and the dealer twice, then
    /// five to the board -- so for one seat the order is player, dealer, player,
    /// dealer, then the board.
    /// </summary>
    private static UltimateHoldemTable Table(string deck, int seats = 1, Rules? rules = null, ISeatAgent? agent = null) =>
        new(rules ?? new Rules(), Deck.Stacked(deck), seats, agent: agent ?? (seats > 1 ? AlwaysChecks : null));

    [Fact]
    public void ATableSeatsBetweenOneAndFivePlayerIncluded()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new UltimateHoldemTable(seats: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new UltimateHoldemTable(seats: 6, agent: AlwaysChecks));

        Assert.Equal(5, new UltimateHoldemTable(seats: 5, agent: AlwaysChecks).Seats.Count);
    }

    [Fact]
    public void SeatMatesWithNowhereToGetADecisionAreRefusedBeforeAnyMoneyMoves()
    {
        // Refused at construction rather than mid-hand, where the stake would
        // already have been taken and the hand could not finish.
        Assert.Throws<ArgumentNullException>(() => new UltimateHoldemTable(seats: 3));
    }

    [Fact]
    public void TheBlindMatchesTheAnteWithoutBeingAskedFor()
    {
        var table = Table("TS 2D JS 5C 9H 8C 7D 2S 3C");

        table.Deal(100);

        Assert.Equal(100, table.Player.Ante);
        Assert.Equal(100, table.Player.Blind);
    }

    [Fact]
    public void TheDealGoesRoundTheSeatsTwiceAndThenTheBoard()
    {
        // The order is fixed and pinned here on purpose: if it ever moves, every
        // stacked-deck test in this file becomes wrong at the same moment, and the
        // failures read as rules bugs rather than as a changed deal.
        var table = Table("AS KS QS JS TS 9S 8S 7S 6S 5S 4S 3S 2S", seats: 3);

        var view = table.Deal(100);

        Assert.Equal("AS TS", string.Join(' ', table.Seats[0].Cards));
        Assert.Equal("KS 9S", string.Join(' ', table.Seats[1].Cards));
        Assert.Equal("QS 8S", string.Join(' ', table.Seats[2].Cards));
        Assert.Equal(TablePhase.PreFlop, view.Phase);
    }

    [Fact]
    public void ThePlayCostsFourOrThreeTimesTheAnteOnTheHoleCards()
    {
        var table = Table("TS 2D JS 5C 9H 8C 7D 2S 3C");
        table.Deal(100);

        Assert.Equal([PlayerAction.Check, PlayerAction.Play], table.AvailableActions());
        Assert.Equal([4, 3], table.AvailablePlayMultiples());
    }

    [Fact]
    public void ThePlayCostsTwiceTheAnteOnTheFlopAndOnceAtTheRiver()
    {
        var table = Table("TS 2D JS 5C 9H 8C 7D 2S 3C");
        table.Deal(100);

        table.Check();
        Assert.Equal(TablePhase.Flop, table.Phase);
        Assert.Equal([2], table.AvailablePlayMultiples());

        table.Check();
        Assert.Equal(TablePhase.River, table.Phase);
        Assert.Equal([1], table.AvailablePlayMultiples());
    }

    [Fact]
    public void TheRiverOffersNoThirdCheck()
    {
        // Without this the Ante buys five cards and a free look, and the house edge
        // goes with it.
        var table = Table("TS 2D JS 5C 9H 8C 7D 2S 3C");
        table.Deal(100);
        table.Check();
        table.Check();

        Assert.Equal([PlayerAction.Play, PlayerAction.Fold], table.AvailableActions());
        Assert.Throws<InvalidOperationException>(() => table.Check());
    }

    [Fact]
    public void APlayBetAtTheWrongSizeIsRefused()
    {
        var table = Table("TS 2D JS 5C 9H 8C 7D 2S 3C");
        table.Deal(100);

        Assert.Throws<ArgumentOutOfRangeException>(() => table.Play(2));
    }

    [Fact]
    public void BettingEarlyRunsTheRestOfTheBoardOutWithoutFurtherInput()
    {
        // The player has nothing left to say once the Play is made, but the hand is
        // not over -- the board still has to come out.
        var table = Table("TS 2D JS 5C 9H 8C 7D 2S 3C");
        table.Deal(100);

        var view = table.Play(4);

        Assert.Equal(TablePhase.Settled, view.Phase);
        Assert.Equal(5, view.Community.Count);
        Assert.Empty(view.AvailableActions);
    }

    [Fact]
    public void NoHoleCardButThePlayersIsInTheViewUntilShowdown()
    {
        // Absent, not blanked. Anything sent to the client is knowable by the client.
        var table = Table("AS KS QS JS TS 9S 8S 7S 6S 5S 4S 3S 2S", seats: 3);

        var view = table.Deal(100);

        Assert.Equal(2, view.Seats[0].Cards.Count);
        Assert.Empty(view.Seats[1].Cards);
        Assert.Empty(view.Seats[2].Cards);
        Assert.Empty(view.Dealer.Cards);
        Assert.Empty(view.Community);

        var settled = table.Play(4);

        Assert.Equal(2, settled.Seats[1].Cards.Count);
        Assert.Equal(2, settled.Dealer.Cards.Count);
    }

    [Fact]
    public void AStraightBeatingAQualifiedDealerPaysAllThreeBets()
    {
        // Player TS JS, dealer 2D 5C, board 9H 8C 7D 2S 3C.
        // Player has a jack-high straight; the dealer has a pair of twos and opens.
        var table = Table("TS 2D JS 5C 9H 8C 7D 2S 3C");
        table.Deal(100);

        var view = table.Play(4);

        Assert.Equal(SeatOutcome.Won, table.Player.Outcome);

        //   Ante  100 at 1:1  -> 200
        //   Play  400 at 1:1  -> 800
        //   Blind 100 at 1:1  -> 200   (a straight is the bottom row of the table)
        Assert.Equal(1_200, view.PlayerReturned);
        Assert.Equal(600, view.PlayerWagered);
        Assert.Equal(600, view.PlayerNet);
    }

    [Fact]
    public void AWinningHandBeneathAStraightPushesTheBlindRatherThanPayingIt()
    {
        // Player KD 8S, dealer 3C 9D, board KH 8C 3D 5S 2H.
        // Two pair beats the dealer's pair of threes, but the Blind starts at a
        // straight -- so it comes back rather than paying, which is not the same as
        // losing it.
        var table = Table("KD 3C 8S 9D KH 8C 3D 5S 2H");
        table.Deal(100);

        var view = table.Play(4);

        Assert.Equal(SeatOutcome.Won, table.Player.Outcome);
        Assert.Equal(1_100, view.PlayerReturned);   // 200 ante + 800 play + 100 blind back
    }

    [Fact]
    public void TheAntePushesWhenTheDealerFailsToOpenEvenOnAHandTheSeatLost()
    {
        // Player 3H 4D, dealer AD 7C, board 2C 5D 9H JS QC.
        // Queen high against ace high: the seat loses. The dealer has no pair, so the
        // Ante still comes back -- reading this as "pushes when the seat wins" keeps
        // money that was never the house's.
        var table = Table("3H AD 4D 7C 2C 5D 9H JS QC");
        table.Deal(100);
        table.Check();
        table.Check();

        var view = table.Play(1);

        Assert.Equal(SeatOutcome.Lost, table.Player.Outcome);
        Assert.False(view.Dealer.Qualified);
        Assert.Equal(100, view.PlayerReturned);     // the Ante, and nothing else
        Assert.Equal(300, view.PlayerWagered);
    }

    [Fact]
    public void ATiePushesEveryBetAndNeverConsultsTheBlindPaytable()
    {
        // The board is a royal flush, so the seat and the dealer both play it and
        // neither wins. The Blind pushes at its stake -- a tie must not reach the
        // 500:1 row, which is the most expensive mistake this file could make.
        var table = Table("2C 4H 3D 5C AS KS QS JS TS");
        table.Deal(100);

        var view = table.Play(4);

        Assert.Equal(SeatOutcome.Push, table.Player.Outcome);
        Assert.Equal(600, view.PlayerReturned);
        Assert.Equal(600, view.PlayerWagered);
        Assert.Equal(0, view.PlayerNet);
    }

    [Fact]
    public void FoldingGivesUpTheAnteAndTheBlindButNotTheTrips()
    {
        // Player 7H 3D, dealer AD KC, board 7C 7D 2S 9H JC -- trip sevens, folded.
        // Trips is a bet on the seat's own cards and the fold does not reach it.
        var table = Table("7H AD 3D KC 7C 7D 2S 9H JC");
        table.Deal(100, trips: 50);
        table.Check();
        table.Check();

        var view = table.Fold();

        Assert.Equal(SeatOutcome.Folded, table.Player.Outcome);
        Assert.Equal(200, view.PlayerReturned);     // 50 back plus 3:1 on the trips
        Assert.Equal(250, view.PlayerWagered);
    }

    [Fact]
    public void ASeatMateSettlesOnItsOwnCardsAndTakesNothingFromThePlayer()
    {
        // Three seats, one board, one dealer. Every seat is scored independently --
        // there is no pot, so a seat-mate winning cannot cost the player anything.
        var table = Table("AS KS QS JS TS 9S 8S 7S 6S 5S 4S 3S 2S", seats: 3);
        table.Deal(100);

        var view = table.Play(4);

        Assert.All(view.Seats, seat => Assert.NotEqual(SeatOutcome.Pending, seat.Outcome));
        Assert.Equal(600, view.PlayerWagered);
        Assert.Equal(table.Player.Wagered, view.PlayerWagered);
        Assert.Equal(table.Player.Returned, view.PlayerReturned);
    }

    [Fact]
    public void ThePlayersTotalsNeverIncludeASeatMatesMoney()
    {
        // The seat-mates' numbers are notional. Summing every seat into the totals
        // would have the server credit a real stash for a bot's win.
        var table = new UltimateHoldemTable(seats: 5, rng: new Random(7), agent: AlwaysChecks);

        var view = table.Deal(100);
        var settled = table.Play(4);

        Assert.Equal(5, view.Seats.Count);
        Assert.Equal(600, settled.PlayerWagered);
        Assert.True(
            settled.Seats.Sum(seat => seat.Wagered) > settled.PlayerWagered,
            "the seat-mates should be staking something of their own");
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void NoHandEverReturnsMoreThanTheRulesSayItCan(int seed)
    {
        // The ceiling the wallet limits will be set from. A settlement that can
        // exceed it would size every payout wrongly, and no single pinned hand
        // would show it.
        var rules = new Rules();
        var rng = new Random(seed);

        for (var hand = 0; hand < 500; hand++)
        {
            var table = new UltimateHoldemTable(rules, seats: 3, rng: rng, agent: AlwaysChecks);
            var ante = rng.Next(1, 1_000);
            var trips = rng.Next(2) == 0 ? rng.Next(1, 100) : 0;

            table.Deal(ante, trips);

            // Walk the streets the way a player would, betting at a random point.
            while (table.Phase is TablePhase.PreFlop or TablePhase.Flop or TablePhase.River)
            {
                if (table.Phase == TablePhase.River)
                {
                    table.Play(1);
                }
                else if (rng.Next(3) == 0)
                {
                    table.Play(table.AvailablePlayMultiples()[0]);
                }
                else
                {
                    table.Check();
                }
            }

            var ceiling = (long)ante * rules.WorstCaseReturnPerAnte
                + rules.Trips.Best.Returned(trips);

            Assert.True(
                table.Player.Returned <= ceiling,
                $"returned {table.Player.Returned} against a ceiling of {ceiling}");
        }
    }

    [Fact]
    public void TheTableRecordsWhatEverySeatDidAndWhy()
    {
        var log = new ListGameLog();
        var table = new UltimateHoldemTable(
            new Rules(), Deck.Stacked("TS 2D JS 5C 9H 8C 7D 2S 3C", log), 1, log);

        table.Deal(100);
        table.Play(4);

        Assert.True(log.Mentions("plays 4x"), log.ToString());
        Assert.True(log.Mentions("qualifies"), log.ToString());
        Assert.True(log.Mentions("Straight, jack high"), log.ToString());
    }
}
