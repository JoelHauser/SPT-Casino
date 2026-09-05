namespace Poker.Game.Tests;

public class BotAgentTests
{
    private static HoldemRules Blinds => new() { SmallBlind = 25, BigBlind = 50, BuyIn = 5_000 };

    private static double Equity(string hole, string board, int opponents, int samples = 600, int seed = 4) =>
        HandEquity.Estimate(
            Card.ParseMany(hole), Card.ParseMany(board), opponents, new Random(seed), samples);

    [Fact]
    public void APairOfAcesWinsAboutFiveTimesInSixAgainstOne()
    {
        // The best starting hand there is, and the number every poker player knows.
        Assert.InRange(Equity("AS AD", "", 1), 0.78, 0.92);
    }

    [Fact]
    public void TheWorstStartingHandIsStillNoWorseThanAboutOneInThree()
    {
        // Seven-deuce offsuit. Bad, but not hopeless -- which is exactly why a bot
        // that folds it every time and a bot that plays it every time both look
        // wrong, and why the price matters more than the cards.
        Assert.InRange(Equity("7S 2D", "", 1), 0.28, 0.42);
    }

    [Fact]
    public void MoreOpponentsMeansLessEquityForTheSameCards()
    {
        // The single most important thing a bot has to know that a chart cannot tell
        // it: aces against one player and aces against four are different hands.
        var heads = Equity("AS AD", "", 1);
        var full = Equity("AS AD", "", 4);

        Assert.True(full < heads - 0.15, $"aces went from {heads:P0} to {full:P0}");
    }

    [Fact]
    public void AHandNothingCanBeatIsWorthEverything()
    {
        Assert.Equal(1.0, Equity("AS KS", "QS JS TS 2C 3D", 3));
    }

    [Fact]
    public void PlayingABoardEverybodySharesIsWorthHalfThePot()
    {
        // A royal on the board: nobody can win and nobody can lose, so every hand
        // ties. Ties count as half a pot because that is what splitting one is --
        // scoring them as losses would have every bot folding a guaranteed chop.
        Assert.Equal(0.5, Equity("2C 3D", "AS KS QS JS TS", 2));
    }

    /// <summary>
    /// Counts what a bot actually did, hand after hand.
    ///
    /// Decisions facing a bet are counted separately, and that separation is the
    /// whole measurement. Across every decision a seat makes, most are free checks in
    /// pots nobody bet, which drowns the differences: measured that way a rock and a
    /// calling station folded 15% and 11% and looked like the same player. **How
    /// often a seat folds when it is actually asked for money** is the number that
    /// tells them apart, and it is what a person at the table would notice too.
    /// </summary>
    private sealed class Counter(IPokerAgent inner) : IPokerAgent
    {
        public Dictionary<HoldemMove, int> Moves { get; } = [];

        public Dictionary<HoldemMove, int> FacingABet { get; } = [];

        public int Total => Moves.Values.Sum();

        public int Asked => FacingABet.Values.Sum();

        /// <summary>Share of the decisions taken when there was something to call.</summary>
        public double WhenAsked(HoldemMove move) =>
            Asked == 0 ? 0 : FacingABet.GetValueOrDefault(move) / (double)Asked;

        public HoldemDecision Decide(PokerContext context)
        {
            var facing = context.Options.ToCall > 0;
            var decision = inner.Decide(context);

            Moves[decision.Move] = Moves.GetValueOrDefault(decision.Move) + 1;

            if (facing)
            {
                FacingABet[decision.Move] = FacingABet.GetValueOrDefault(decision.Move) + 1;
            }

            return decision;
        }

        // Forwarded, and it must be. A wrapper that swallows this measures a bot with
        // its memory disconnected -- which is not the bot the game runs, and it made
        // these tests quietly disagree with the real table.
        public void HandEnded(HandOutcome outcome) => inner.HandEnded(outcome);
    }

    /// <summary>
    /// Sits a character down for a few dozen hands and watches what it does.
    ///
    /// Stacks are topped back up between hands. This measures *style*, and a seat
    /// that busts out stops producing decisions -- the maniac ran out of chips a
    /// third of the way through and its sample went with it, which says something
    /// true about maniacs but nothing about how it plays.
    /// </summary>
    private static Counter Behaviour(PokerPersonality personality, int hands = 60, int seed = 17, int seats = 3)
    {
        var counter = new Counter(new BotAgent(personality, new Random(seed), samples: 40));

        var agents = new List<IPokerAgent> { counter };
        for (var foil = 0; foil < seats - 2; foil++)
        {
            agents.Add(new BotAgent(PokerPersonality.Balanced, new Random(seed + 1 + foil), samples: 40));
        }

        var table = new HoldemTable(Blinds, seats: seats, rng: new Random(seed + 2), agents: agents);

        for (var hand = 0; hand < hands; hand++)
        {
            foreach (var seat in table.Seats)
            {
                seat.Stack = Blinds.BuyIn;
            }

            table.StartHand();

            while (table.AwaitingPlayer)
            {
                var options = table.Options();
                table.Act(options.Moves.Contains(HoldemMove.Check) ? HoldemDecision.Check : HoldemDecision.Call);
            }
        }

        return counter;
    }

    [Fact]
    public void ARockFoldsFarMoreOftenThanACallingStation()
    {
        var rock = Behaviour(PokerPersonality.Cast.Single(p => p.Name == "Rock"));
        var station = Behaviour(PokerPersonality.Cast.Single(p => p.Name == "Station"));

        Assert.True(
            rock.WhenAsked(HoldemMove.Fold) > station.WhenAsked(HoldemMove.Fold) + 0.20,
            $"asked for money, rock folded {rock.WhenAsked(HoldemMove.Fold):P0} "
            + $"and the station folded {station.WhenAsked(HoldemMove.Fold):P0}");
    }

    [Fact]
    public void AManiacRaisesFarMoreOftenThanARock()
    {
        var maniac = Behaviour(PokerPersonality.Cast.Single(p => p.Name == "Maniac"));
        var rock = Behaviour(PokerPersonality.Cast.Single(p => p.Name == "Rock"));

        Assert.True(
            maniac.WhenAsked(HoldemMove.Raise) > rock.WhenAsked(HoldemMove.Raise) + 0.10,
            $"facing a bet, the maniac raised {maniac.WhenAsked(HoldemMove.Raise):P0} "
            + $"and the rock raised {rock.WhenAsked(HoldemMove.Raise):P0}");
    }

    [Fact]
    public void ACallingStationCallsMoreThanItRaises()
    {
        // The defining trait, and the reason the type exists at a real table: it pays
        // hands off rather than playing them.
        var station = Behaviour(PokerPersonality.Cast.Single(p => p.Name == "Station"));

        Assert.True(
            station.WhenAsked(HoldemMove.Call) > station.WhenAsked(HoldemMove.Raise) * 4,
            $"station called {station.WhenAsked(HoldemMove.Call):P0}, "
            + $"raised {station.WhenAsked(HoldemMove.Raise):P0}");
    }

    [Fact]
    public void EveryCharacterInTheCastPlaysDifferentlyFromEveryOther()
    {
        // The point of having eight of them. If two produce the same mix of actions
        // then one of them is not worth a seat.
        var profiles = PokerPersonality.Cast
            .Select(personality => (personality.Name, Counter: Behaviour(personality)))
            .ToList();

        foreach (var (name, counter) in profiles)
        {
            Assert.True(counter.Asked > 15, $"{name} was barely asked for money ({counter.Asked} times)");
        }

        var signatures = profiles
            .Select(p => (
                p.Name,
                Fold: p.Counter.WhenAsked(HoldemMove.Fold),
                Call: p.Counter.WhenAsked(HoldemMove.Call),
                Raise: p.Counter.WhenAsked(HoldemMove.Raise)))
            .ToList();

        foreach (var left in signatures)
        {
            foreach (var right in signatures.Where(other => other.Name != left.Name))
            {
                // The whole action distribution, not two corners of it. Two seats that
                // fold alike can still differ in whether they call or raise the rest.
                var apart = Math.Abs(left.Fold - right.Fold)
                    + Math.Abs(left.Call - right.Call)
                    + Math.Abs(left.Raise - right.Raise);
                Assert.True(
                    apart > 0.12,
                    $"{left.Name} (fold {left.Fold:P0} call {left.Call:P0} raise {left.Raise:P0}) and "
                    + $"{right.Name} (fold {right.Fold:P0} call {right.Call:P0} raise {right.Raise:P0}) play almost identically");
            }
        }
    }

    /// <summary>Records the decision together with how many opponents were still live.</summary>
    private sealed class LiveCounter(IPokerAgent inner) : IPokerAgent
    {
        public List<(int Live, bool Facing, HoldemMove Move)> Records { get; } = [];

        public HoldemDecision Decide(PokerContext context)
        {
            var live = context.Opponents.Count(other => !other.Folded);
            var facing = context.Options.ToCall > 0;
            var decision = inner.Decide(context);

            Records.Add((live, facing, decision.Move));
            return decision;
        }

        /// <summary>How often it bet into a pot nobody had bet, against this many opponents.</summary>
        public double BetsInto(int fewest, int most)
        {
            var chances = Records.Where(r => !r.Facing && r.Live >= fewest && r.Live <= most).ToList();
            return chances.Count == 0 ? 0 : chances.Count(r => r.Move == HoldemMove.Raise) / (double)chances.Count;
        }

        public int Chances(int fewest, int most) =>
            Records.Count(r => !r.Facing && r.Live >= fewest && r.Live <= most);
    }

    [Fact]
    public void ABlufferBetsFarLessOftenTheMoreOpponentsAreStillLive()
    {
        // Bluffing works by making everyone fold, and every extra opponent still in
        // the hand is another chance that one of them has something.
        //
        // Conditioned on **live** opponents rather than on seats, which is the whole
        // point: a five-seat table is not a five-handed pot. By the time anyone
        // checks after the flop most of the table has usually folded, so a bot
        // looking at one remaining opponent is right to bluff freely -- measuring by
        // table size showed 48% against 46% and proved nothing.
        //
        // Measured on a character built for it rather than one of the cast. The
        // maniac bets 84% of empty pots either way because at its raise bar almost
        // everything is a *value* bet; this one is tight and passive enough that
        // value betting barely fires, leaving the bluffing to be seen.
        // Loose enough to reach a flop with a crowd still in it -- at Tightness 0.9
        // it folded before the flop nearly every hand and produced eight measurable
        // chances in a hundred and fifty -- but passive enough that value betting
        // almost never fires, so what is left is bluffing.
        var bluffer = new PokerPersonality(
            "Bluffer", Tightness: 0.15, Aggression: 0.02, Bluff: 0.90, Risk: 0.30, Positional: 0.30);

        var counter = new LiveCounter(new BotAgent(bluffer, new Random(17), samples: 40));

        // Foils that call rather than fold, so pots actually reach the flop with a
        // crowd in them.
        var station = PokerPersonality.Cast.Single(p => p.Name == "Station");
        var agents = new List<IPokerAgent> { counter };
        for (var foil = 0; foil < 3; foil++)
        {
            agents.Add(new BotAgent(station, new Random(30 + foil), samples: 40));
        }

        var table = new HoldemTable(Blinds, seats: 5, rng: new Random(19), agents: agents);

        for (var hand = 0; hand < 150; hand++)
        {
            foreach (var seat in table.Seats)
            {
                seat.Stack = Blinds.BuyIn;
            }

            table.StartHand();

            while (table.AwaitingPlayer)
            {
                var options = table.Options();
                table.Act(options.Moves.Contains(HoldemMove.Check) ? HoldemDecision.Check : HoldemDecision.Call);
            }
        }

        Assert.True(counter.Chances(1, 1) > 15, $"only {counter.Chances(1, 1)} heads-up chances");
        Assert.True(counter.Chances(3, 4) > 15, $"only {counter.Chances(3, 4)} crowded chances");

        Assert.True(
            counter.BetsInto(1, 1) > counter.BetsInto(3, 4) + 0.15,
            $"bet {counter.BetsInto(1, 1):P0} against one but {counter.BetsInto(3, 4):P0} against three or four");
    }

    [Fact]
    public void TheSameSeedPlaysTheSameHandTheSameWay()
    {
        // Without this a bot cannot be pinned by a test, and a bot that cannot be
        // pinned cannot be shown to follow its own rules.
        var first = Behaviour(PokerPersonality.Cast[3], hands: 20, seed: 99);
        var second = Behaviour(PokerPersonality.Cast[3], hands: 20, seed: 99);

        Assert.Equal(first.Moves, second.Moves);
    }

    [Fact]
    public void ATableOfCharactersPlaysItselfWithoutLosingAChip()
    {
        var rng = new Random(31);
        var cast = PokerPersonality.Deal(4, rng);
        var agents = cast.Select((personality, i) => (IPokerAgent)new BotAgent(personality, new Random(i + 1), samples: 40)).ToList();

        var table = new HoldemTable(Blinds, seats: 5, rng: rng, agents: agents);
        var expected = table.ChipsInPlay;

        for (var hand = 0; hand < 40 && table.Seats.All(seat => seat.Stack > 0); hand++)
        {
            table.StartHand();

            while (table.AwaitingPlayer)
            {
                var options = table.Options();
                table.Act(options.Moves.Contains(HoldemMove.Check) ? HoldemDecision.Check : HoldemDecision.Call);
            }

            Assert.Equal(expected, table.ChipsInPlay);
        }
    }

    [Fact]
    public void ABotSaysWhatItWasThinking()
    {
        // A seat that silently does things is untestable and unwatchable. The log has
        // to carry the factors, not just the action.
        var log = new ListGameLog();
        var table = new HoldemTable(
            Blinds,
            seats: 2,
            rng: new Random(8),
            log: log,
            agents: [new BotAgent(PokerPersonality.Cast[1], new Random(3), log, samples: 40)]);

        table.StartHand();

        while (table.AwaitingPlayer)
        {
            var options = table.Options();
            table.Act(options.Moves.Contains(HoldemMove.Check) ? HoldemDecision.Check : HoldemDecision.Call);
        }

        Assert.True(log.Mentions("equity"), log.ToString());
        Assert.True(log.Mentions("price"), log.ToString());
        Assert.True(log.Mentions("bb ->"), log.ToString());
    }
}
