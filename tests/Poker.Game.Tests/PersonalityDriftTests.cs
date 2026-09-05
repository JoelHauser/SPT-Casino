namespace Poker.Game.Tests;

/// <summary>
/// The seats are not fixed characters. They are blends, and they move.
/// </summary>
public class PersonalityDriftTests
{
    private static HoldemRules Blinds => new() { SmallBlind = 25, BigBlind = 50, BuyIn = 5_000 };

    private static PokerPersonality Named(string name) =>
        PokerPersonality.Cast.Single(p => p.Name == name);

    private sealed class Listener(Func<PokerContext, HoldemDecision>? decide = null) : IPokerAgent
    {
        public List<HandOutcome> Heard { get; } = [];

        public HoldemDecision Decide(PokerContext context) =>
            decide?.Invoke(context)
            ?? (context.Options.Moves.Contains(HoldemMove.Check) ? HoldemDecision.Check : HoldemDecision.Call);

        public void HandEnded(HandOutcome outcome) => Heard.Add(outcome);
    }

    [Fact]
    public void BlendingTwoCharactersLandsBetweenThem()
    {
        var rock = Named("Rock");
        var maniac = Named("Maniac");

        var half = rock.Blend(maniac, 0.5);

        Assert.Equal((rock.Tightness + maniac.Tightness) / 2, half.Tightness, 6);
        Assert.Equal((rock.Aggression + maniac.Aggression) / 2, half.Aggression, 6);
    }

    [Fact]
    public void BlendingAllTheWayGivesTheOtherCharacterBack()
    {
        var rock = Named("Rock");
        var maniac = Named("Maniac");

        var all = rock.Blend(maniac, 1.0);

        Assert.Equal(maniac.Tightness, all.Tightness, 6);
        Assert.Equal(maniac.Aggression, all.Aggression, 6);
        Assert.Equal(maniac.Steadiness, all.Steadiness, 6);
    }

    [Fact]
    public void ImprovisedCharactersAreDrawnFromAContinuumRatherThanAList()
    {
        // The named cast are landmarks, not the population. A table filled from here
        // should not keep producing the same handful of people.
        var rng = new Random(6);
        var made = Enumerable.Range(0, 40).Select(_ => PokerPersonality.Improvise(rng)).ToList();

        Assert.All(made, person =>
        {
            Assert.InRange(person.Tightness, 0, 1);
            Assert.InRange(person.Aggression, 0, 1);
            Assert.InRange(person.Steadiness, 0, 1);
        });

        var clones = made.Count(person => PokerPersonality.Cast.Any(known =>
            Math.Abs(known.Tightness - person.Tightness) < 0.001
            && Math.Abs(known.Aggression - person.Aggression) < 0.001));

        Assert.True(clones <= 2, $"{clones} of 40 improvised characters were straight copies");

        var spread = made.Select(person => person.Aggression).ToList();
        Assert.True(spread.Max() - spread.Min() > 0.5, "improvised characters barely differ");
    }

    [Fact]
    public void ASeatIsToldHowEveryHandWentEvenWhenItFoldedEarly()
    {
        // A seat that only heard about showdowns would never notice it was being run
        // over.
        var listener = new Listener();
        var table = new HoldemTable(Blinds, seats: 2, rng: new Random(2), agents: [listener]);

        for (var hand = 0; hand < 5; hand++)
        {
            table.StartHand();

            while (table.AwaitingPlayer)
            {
                table.Act(HoldemDecision.Fold);
            }
        }

        Assert.Equal(5, listener.Heard.Count);
    }

    [Fact]
    public void ARaiseNobodyCalledIsReportedAsAWinRatherThanALoss()
    {
        // Net has to come off the stack. Winnings minus what was committed misses the
        // uncalled bet coming back, and would tell a seat it had just lost hundreds of
        // chips on a hand it actually won.
        var listener = new Listener(_ => HoldemDecision.Fold);
        var table = new HoldemTable(Blinds, seats: 2, rng: new Random(2), agents: [listener]);

        table.StartHand();

        while (table.AwaitingPlayer)
        {
            var options = table.Options();
            table.Act(options.Moves.Contains(HoldemMove.Raise)
                ? HoldemDecision.RaiseTo(options.MaxRaiseTo)
                : HoldemDecision.Check);
        }

        Assert.True(table.Player.Net > 0, $"the player netted {table.Player.Net} on a hand nobody called");
        Assert.True(listener.Heard[0].Net < 0);
    }

    [Fact]
    public void AnUnshakeableSeatPlaysTheSameAfterLosingAsBefore()
    {
        var steady = Named("Shark") with { Steadiness = 1.0 };
        var bot = new BotAgent(steady, new Random(1), samples: 20);

        for (var hand = 0; hand < 6; hand++)
        {
            bot.HandEnded(new HandOutcome(-4_000, 1_000, 5_000, Folded: false));
        }

        Assert.Equal(steady, bot.Current);
    }

    [Fact]
    public void AGamblerSteamsAfterLosingAndACarefulPlayerShutsDown()
    {
        // The two real reactions to a bad run. Which one a seat has falls out of
        // whether it is a gambler rather than being a dial of its own.
        var gambler = new BotAgent(Named("Gambler"), new Random(1), samples: 20);
        var careful = new BotAgent(Named("Rock") with { Steadiness = 0.2 }, new Random(1), samples: 20);

        for (var hand = 0; hand < 4; hand++)
        {
            gambler.HandEnded(new HandOutcome(-3_000, 500, 5_000, Folded: false));
            careful.HandEnded(new HandOutcome(-3_000, 500, 5_000, Folded: false));
        }

        Assert.True(gambler.Mood < -0.3, $"the gambler is only at {gambler.Mood:F2}");

        Assert.True(
            gambler.Current.Tightness < gambler.Personality.Tightness,
            "a steaming gambler should be looser than it started");
        Assert.True(
            gambler.Current.Aggression > gambler.Personality.Aggression,
            "a steaming gambler should be swinging harder");

        Assert.True(
            careful.Current.Tightness > careful.Personality.Tightness,
            "a rattled careful player should tighten up");
        Assert.True(
            careful.Current.Aggression < careful.Personality.Aggression,
            "a rattled careful player should stop betting");
    }

    [Fact]
    public void WinningMakesEverybodyBolderWhoeverTheyAre()
    {
        // Confidence is not a personality type, so this one is not split by Risk.
        var rock = new BotAgent(Named("Rock") with { Steadiness = 0.2 }, new Random(1), samples: 20);

        for (var hand = 0; hand < 3; hand++)
        {
            rock.HandEnded(new HandOutcome(3_000, 8_000, 5_000, Folded: false));
        }

        Assert.True(rock.Mood > 0.3, $"the rock is only at {rock.Mood:F2}");
        Assert.True(rock.Current.Tightness < rock.Personality.Tightness);
        Assert.True(rock.Current.Aggression > rock.Personality.Aggression);
    }

    [Fact]
    public void AMoodComesBackToLevelWhenNothingMoreHappens()
    {
        // Otherwise an hour-old bad beat is carried for the rest of the night.
        var bot = new BotAgent(Named("Maniac"), new Random(1), samples: 20);

        bot.HandEnded(new HandOutcome(-5_000, 0, 5_000, Folded: false));
        var worst = bot.Mood;

        for (var hand = 0; hand < 8; hand++)
        {
            bot.HandEnded(new HandOutcome(0, 5_000, 5_000, Folded: true));
        }

        Assert.True(worst < -0.5, $"a lost stack only moved it to {worst:F2}");
        Assert.True(bot.Mood > worst + 0.4, $"still at {bot.Mood:F2} after eight quiet hands");
    }

    [Fact]
    public void FoldingASmallBlindIsNotABadBeat()
    {
        // Nobody tilts off a folded small blind, and a streak counter that thinks
        // otherwise has every seat steaming within an orbit.
        var bot = new BotAgent(Named("Gambler"), new Random(1), samples: 20);

        for (var hand = 0; hand < 6; hand++)
        {
            bot.HandEnded(new HandOutcome(-25, 4_975, 5_000, Folded: true));
        }

        Assert.Equal(0, bot.LosingStreak);
        Assert.True(Math.Abs(bot.Mood) < 0.2, $"folding blinds moved the mood to {bot.Mood:F2}");
    }
}
