namespace Poker.Game.Tests;

public class HoldemTableTests
{
    private sealed class Bot(Func<PokerContext, HoldemDecision> decide) : IPokerAgent
    {
        public HoldemDecision Decide(PokerContext context) => decide(context);
    }

    /// <summary>Records what it was offered and when, so a test can inspect a bot's turn.</summary>
    private sealed class Recorder(Func<PokerContext, HoldemDecision> decide) : IPokerAgent
    {
        public List<(HoldemStreet Street, BettingOptions Options)> Seen { get; } = [];

        public HoldemDecision Decide(PokerContext context)
        {
            Seen.Add((context.Street, context.Options));
            return decide(context);
        }
    }


    private static IPokerAgent Passive => new Bot(context =>
        context.Options.Moves.Contains(HoldemMove.Check) ? HoldemDecision.Check : HoldemDecision.Call);

    private static IPokerAgent Folder => new Bot(context =>
        context.Options.Moves.Contains(HoldemMove.Check) ? HoldemDecision.Check : HoldemDecision.Fold);

    private static HoldemRules Blinds => new() { SmallBlind = 25, BigBlind = 50, BuyIn = 5_000 };

    private static HoldemTable Table(int seats, params IPokerAgent[] agents) =>
        new(Blinds, seats: seats, rng: new Random(5), agents: agents);

    private static HoldemTable Stacked(string deck, int seats, params IPokerAgent[] agents) =>
        new(Blinds, Deck.Stacked(deck), seats, agents: agents);

    [Fact]
    public void ATableNeedsAtLeastTwoSeatsAndAnAgentForEachBot()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new HoldemTable(seats: 1));

        // One agent for every seat was the parked UTH table's mistake: it made four
        // seat-mates one person wearing four names.
        Assert.Throws<ArgumentException>(() => new HoldemTable(Blinds, seats: 3, agents: [Passive]));
    }

    [Fact]
    public void HeadsUpTheButtonPostsTheSmallBlindAndActsFirstBeforeTheFlop()
    {
        // The exception that catches everybody. With three or more the button is last
        // before the flop; heads-up it is first.
        var table = Table(2, Passive);
        table.StartHand();

        Assert.Equal(0, table.Button);
        Assert.Equal(25, table.Seats[0].CommittedThisStreet);
        Assert.Equal(50, table.Seats[1].CommittedThisStreet);
        Assert.True(table.AwaitingPlayer);
        Assert.Equal(25, table.Options().ToCall);
    }

    [Fact]
    public void HeadsUpTheBigBlindActsFirstOnEveryStreetAfterTheFlop()
    {
        // Pinned by making the big blind bet the flop. If the order were wrong the
        // player would be asked first, with nothing to call -- which is why simply
        // asserting that the player is asked proves nothing at all here.
        var bigBlind = new Bot(context => context.Street == HoldemStreet.Flop
            && context.Options.Moves.Contains(HoldemMove.Raise)
                ? HoldemDecision.RaiseTo(context.Options.MinRaiseTo)
                : context.Options.Moves.Contains(HoldemMove.Check)
                    ? HoldemDecision.Check
                    : HoldemDecision.Call);

        var table = Table(2, bigBlind);
        table.StartHand();

        table.Act(HoldemDecision.Call);

        Assert.Equal(HoldemStreet.Flop, table.Street);
        Assert.Equal(3, table.Community.Count);
        Assert.True(table.AwaitingPlayer);
        Assert.True(table.Options().ToCall > 0, "the big blind should have bet before the player was asked");
    }

    [Fact]
    public void WithThreeSeatsTheSmallBlindOpensAfterTheFlop()
    {
        // Pre-flop the button is last and the seat after the big blind opens; after
        // the flop the small blind opens and the button is last. Two different
        // orders, and using one for both is invisible while every bot only checks.
        var smallBlind = new Recorder(context => context.Options.Moves.Contains(HoldemMove.Check)
            ? HoldemDecision.Check
            : HoldemDecision.Call);

        var table = new HoldemTable(Blinds, seats: 3, rng: new Random(5), agents: [smallBlind, Passive]);

        table.StartHand();
        table.Act(HoldemDecision.Call);      // the button calls, pre-flop

        // The small blind has now been asked twice: once pre-flop after the button,
        // and once as the first seat on the flop.
        var flop = smallBlind.Seen.Where(seen => seen.Street == HoldemStreet.Flop).ToList();

        Assert.Single(flop);
        Assert.Equal(0, flop[0].Options.ToCall);
    }

    [Fact]
    public void WithThreeSeatsTheBlindsSitLeftOfTheButtonAndTheSeatAfterThemOpens()
    {
        var table = Table(3, Passive, Passive);
        table.StartHand();

        Assert.Equal(0, table.Seats[0].CommittedThisStreet);   // the button posts nothing
        Assert.Equal(25, table.Seats[1].CommittedThisStreet);
        Assert.Equal(50, table.Seats[2].CommittedThisStreet);

        // Three-handed, the seat after the big blind is the button, so it is the
        // player who opens.
        Assert.True(table.AwaitingPlayer);
    }

    [Fact]
    public void TheBigBlindStillHasAnOptionWhenEveryoneOnlyCalls()
    {
        // Posting a blind is not acting. Without that distinction the round closes on
        // the big blind's forced chips and it never gets to raise.
        var bigBlind = new Recorder(_ => HoldemDecision.Check);
        var table = new HoldemTable(Blinds, seats: 3, rng: new Random(5), agents: [Passive, bigBlind]);

        table.StartHand();
        table.Act(HoldemDecision.Call);

        // The street matters and is the whole test. Without it this passes on the
        // big blind's *flop* options, which also have nothing to call and a raise
        // available -- so a table that closed the pre-flop round on the forced blind
        // would look correct.
        var offered = bigBlind.Seen.Where(seen => seen.Street == HoldemStreet.PreFlop).ToList();

        Assert.Single(offered);
        Assert.Equal(0, offered[0].Options.ToCall);
        Assert.Contains(HoldemMove.Check, offered[0].Options.Moves);
        Assert.Contains(HoldemMove.Raise, offered[0].Options.Moves);
    }

    [Fact]
    public void ARaiseMustBeAtLeastAsLargeAsTheOneBeforeIt()
    {
        var table = Table(2, Passive);
        table.StartHand();

        // The blind counts as the opening raise, so the first raise is to 100.
        Assert.Equal(100, table.Options().MinRaiseTo);
        Assert.Throws<ArgumentOutOfRangeException>(() => table.Act(HoldemDecision.RaiseTo(75)));

        table.Act(HoldemDecision.RaiseTo(150));

        // Asserted on the stack rather than on what is out on the street: the bot
        // called and the flop came, which clears the street's commitments.
        Assert.Equal(5_000 - 150, table.Seats[0].Stack);
        Assert.Equal(300, table.Pot);
    }

    [Fact]
    public void AnAllInTooSmallToBeAFullRaiseDoesNotReopenTheBetting()
    {
        // The player opens to 200, seat 1 calls, and seat 2 is short enough that its
        // whole stack is only 60 more -- an all-in over the bet, but well short of a
        // full raise, which would be to 350.
        //
        // The player and seat 1 have both acted already. They owe the extra 60 and may
        // call it or fold, but they do not get to raise again. Miss this and a short
        // all-in turns into an unlimited raising war between the other two.
        var allIn = new Bot(context => HoldemDecision.RaiseTo(context.Options.MaxRaiseTo));

        var table = new HoldemTable(
            Blinds, seats: 3, rng: new Random(5), agents: [Passive, allIn]);

        table.Seats[2].Stack = 260;      // posts 50 as the blind, 210 behind

        table.StartHand();
        table.Act(HoldemDecision.RaiseTo(200));

        var facing = table.Options();

        Assert.Equal(60, facing.ToCall);
        Assert.DoesNotContain(HoldemMove.Raise, facing.Moves);
        Assert.Contains(HoldemMove.Call, facing.Moves);
        Assert.Contains(HoldemMove.Fold, facing.Moves);
    }

    [Fact]
    public void AFullRaiseDoesReopenTheBettingForSeatsThatAlreadyActed()
    {
        // The contrast that makes the test above mean something: seat 2 has the chips
        // for a genuine raise, so the player gets its own raise back.
        var raiser = new Bot(context =>
            context.Options.Moves.Contains(HoldemMove.Raise)
                ? HoldemDecision.RaiseTo(context.Options.MinRaiseTo)
                : HoldemDecision.Call);

        var table = new HoldemTable(Blinds, seats: 3, rng: new Random(5), agents: [Passive, raiser]);

        table.StartHand();
        table.Act(HoldemDecision.RaiseTo(200));

        var facing = table.Options();

        Assert.True(facing.ToCall > 0);
        Assert.Contains(HoldemMove.Raise, facing.Moves);
    }

    [Fact]
    public void ABrokeSeatIsBoughtBackInBetweenHandsAndNotDuringOne()
    {
        var table = Table(3, Passive, Passive);
        table.Seats[1].Stack = 0;

        var before = table.ChipsInPlay;
        table.Reseat(1, 5_000);

        // Deliberately the one place in the engine that makes chips out of nothing.
        // Anything counting them has to be told, which is why it is a call rather
        // than something StartHand does quietly.
        Assert.Equal(before + 5_000, table.ChipsInPlay);
        Assert.Equal(5_000, table.Seats[1].Stack);

        table.StartHand();
        Assert.Throws<InvalidOperationException>(() => table.Reseat(1, 5_000));
    }

    [Fact]
    public void ASeatThatStillHasChipsCannotBuyMore()
    {
        // Otherwise a bot could quietly top itself up and the faucet would run
        // without anybody deciding it should.
        var table = Table(3, Passive, Passive);

        Assert.Throws<InvalidOperationException>(() => table.Reseat(1, 5_000));
    }

    [Fact]
    public void SomebodyNewCanTakeTheEmptyChairButNeverThePlayers()
    {
        var table = Table(3, Passive, Passive);
        table.Seats[2].Stack = 0;

        var newcomer = new Bot(_ => HoldemDecision.Fold);
        table.Reseat(2, 5_000, newcomer, "Stranger");

        Assert.Equal("Stranger", table.Seats[2].Name);

        table.Seats[0].Stack = 0;
        Assert.Throws<InvalidOperationException>(() => table.Reseat(0, 5_000, newcomer));
    }

    [Fact]
    public void ASeatCannotPutInMoreThanItHas()
    {
        // Tested directly because no caller currently asks for too much -- the
        // options already cap it. That makes the clamp unreachable defensive code,
        // and unreachable defensive code is exactly what stops being true later.
        var table = Table(2, Passive);
        var seat = table.Seats[1];
        seat.Stack = 40;

        var moved = seat.Commit(500);

        Assert.Equal(40, moved);
        Assert.Equal(0, seat.Stack);
        Assert.Equal(40, seat.CommittedThisHand);
    }

    [Fact]
    public void EveryoneFoldingHandsTheBlindsOverAndGivesBackWhatNobodyCalled()
    {
        var table = Table(3, Folder, Folder);
        var before = table.ChipsInPlay;

        table.StartHand();
        table.Act(HoldemDecision.RaiseTo(400));

        // Both fold. The raise past the blinds was never called, so it comes back
        // rather than being counted as a pot that was won.
        Assert.Equal(HoldemStreet.Showdown, table.Street);
        Assert.Equal(before, table.ChipsInPlay);
        Assert.Equal(5_000 + 75, table.Seats[0].Stack);
    }

    [Fact]
    public void AnOddChipGoesToTheFirstWinnerLeftOfTheButton()
    {
        // The board is a royal, so everyone left in plays it and ties. Seat 1 folds
        // its small blind into the pot, which makes the pot odd.
        //
        // Dropping the remainder instead would quietly destroy a chip a hand, and a
        // table whose books stop balancing over a session is a bug nobody can see
        // until the numbers are far apart.
        var table = Stacked(
            "2C 3D 4H 5S 6C 7D AS KS QS JS TS",
            3,
            new Bot(_ => HoldemDecision.Fold),
            Passive);

        table.StartHand();
        table.Act(HoldemDecision.Call);

        while (table.AwaitingPlayer)
        {
            table.Act(HoldemDecision.Check);
        }

        Assert.Equal(125, table.Seats[0].Won + table.Seats[2].Won);
        Assert.Equal(62, table.Seats[0].Won);
        Assert.Equal(63, table.Seats[2].Won);   // seat 2 is nearer the button's left
    }

    [Fact]
    public void AShortStackAllInMakesASidePotItCannotWin()
    {
        var table = new HoldemTable(
            Blinds, seats: 3, rng: new Random(11), agents: [Passive, Passive]);

        table.Seats[1].Stack = 300;

        var before = table.ChipsInPlay;
        table.StartHand();
        table.Act(HoldemDecision.RaiseTo(1_000));

        while (table.AwaitingPlayer)
        {
            var options = table.Options();
            table.Act(options.Moves.Contains(HoldemMove.Check) ? HoldemDecision.Check : HoldemDecision.Call);
        }

        // Seat 1 is in for 300 and cannot win a chip more than 300 from each of the
        // others, however the hand turns out.
        Assert.Equal(before, table.ChipsInPlay);
        Assert.True(table.Seats[1].Won <= 900, $"seat 1 won {table.Seats[1].Won} from a 300 stack");
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void ChipsAreNeitherCreatedNorDestroyed(int seats)
    {
        // The invariant hold'em has and the parked UTH game did not. Every hand starts
        // with a known number of chips at the table and has to end with the same
        // number -- and the ways to break it are all in the betting round rather than
        // in settlement: an uncalled bet kept, a side pot paid twice, an odd chip
        // dropped on a split.
        var rng = new Random(seats * 31);

        IPokerAgent Wild() => new Bot(context =>
        {
            var options = context.Options;
            var roll = rng.Next(10);

            if (roll < 2 && options.Moves.Contains(HoldemMove.Fold) && options.ToCall > 0)
            {
                return HoldemDecision.Fold;
            }

            if (roll < 4 && options.Moves.Contains(HoldemMove.Raise))
            {
                return HoldemDecision.RaiseTo(rng.Next(options.MinRaiseTo, options.MaxRaiseTo + 1));
            }

            return options.Moves.Contains(HoldemMove.Check) ? HoldemDecision.Check : HoldemDecision.Call;
        });

        var agents = Enumerable.Range(0, seats - 1).Select(_ => Wild()).ToArray();
        var table = new HoldemTable(Blinds, seats: seats, rng: rng, agents: agents);

        var expected = table.ChipsInPlay;

        for (var hand = 0; hand < 300 && table.Seats.All(seat => seat.Stack > 0); hand++)
        {
            table.StartHand();

            while (table.AwaitingPlayer)
            {
                var options = table.Options();
                var roll = rng.Next(10);

                if (roll < 2 && options.ToCall > 0)
                {
                    table.Act(HoldemDecision.Fold);
                }
                else if (roll < 4 && options.Moves.Contains(HoldemMove.Raise))
                {
                    table.Act(HoldemDecision.RaiseTo(rng.Next(options.MinRaiseTo, options.MaxRaiseTo + 1)));
                }
                else
                {
                    table.Act(options.Moves.Contains(HoldemMove.Check) ? HoldemDecision.Check : HoldemDecision.Call);
                }
            }

            Assert.Equal(expected, table.ChipsInPlay);
            Assert.All(table.Seats, seat => Assert.True(seat.Stack >= 0, $"{seat.Name} went negative"));
        }
    }

    [Fact]
    public void TheTableRecordsEveryActionAndWhatItCost()
    {
        var log = new ListGameLog();
        var table = new HoldemTable(Blinds, Deck.Stacked("2C 3D 4H 5S AS KS QS JS TS"), 2, log, [Passive]);

        table.StartHand();
        table.Act(HoldemDecision.RaiseTo(150));

        while (table.AwaitingPlayer)
        {
            table.Act(HoldemDecision.Check);
        }

        Assert.True(log.Mentions("raises to 150"), log.ToString());
        Assert.True(log.Mentions("button on"), log.ToString());
        Assert.True(log.Mentions("Flop"), log.ToString());
    }
}
