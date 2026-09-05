using Blackjack.Game;

namespace Blackjack.Tests;

/// <summary>
/// Plays thousands of rounds picking uniformly at random from whatever the table
/// says is legal. The point is not the outcomes -- it is that no sequence of legal
/// actions can wedge the state machine, exhaust the shoe, or pay out impossibly.
/// </summary>
public class FuzzTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(1337)]
    public void RandomLegalPlayNeverBreaksTheTable(int seed)
    {
        var rng = new Random(seed);
        var table = new BlackjackTable(new Rules(), new Random(seed));

        for (var round = 0; round < 2_000; round++)
        {
            var view = table.Deal(10_000);

            var guard = 0;
            while (view.Phase == RoundPhase.PlayerTurn)
            {
                // A hand cannot legally take many more than twenty actions; if it
                // does, AdvanceHand has failed to retire a hand and we are looping.
                Assert.True(++guard < 100, "Player turn did not terminate.");

                var actions = view.AvailableActions;
                Assert.NotEmpty(actions);

                view = actions[rng.Next(actions.Count)] switch
                {
                    PlayerAction.Hit => table.Hit(),
                    PlayerAction.Stand => table.Stand(),
                    PlayerAction.Double => table.Double(),
                    PlayerAction.Split => table.Split(),
                    _ => throw new InvalidOperationException("Unknown action."),
                };
            }

            Assert.Equal(RoundPhase.Settled, view.Phase);
            Assert.Empty(view.AvailableActions);

            // Every hand must be adjudicated -- a Pending outcome after settlement
            // means a hand fell through every branch of Settle.
            Assert.All(view.PlayerHands, hand => Assert.NotEqual(HandOutcome.Pending, hand.Outcome));
            Assert.All(view.PlayerHands, hand => Assert.NotEqual(HandStatus.Active, hand.Status));

            // 2.5x is the ceiling: a natural at the default 3:2. Nothing can pay more,
            // and a hand that busted must return nothing at all.
            Assert.InRange(view.TotalReturned, 0, (int)(view.TotalWagered * 2.5));
            Assert.All(
                view.PlayerHands,
                hand => Assert.True(hand.Outcome != HandOutcome.Bust || hand.Returned == 0));
        }
    }

    [Fact]
    public void ShoeReshufflesBeforeItRunsOut()
    {
        // The shoe must never be drawn dry mid-round; Draw() throws if it is, so
        // surviving a long session at all is the assertion.
        var table = new BlackjackTable(new Rules { DeckCount = 1 }, new Random(42));

        for (var round = 0; round < 500; round++)
        {
            var view = table.Deal(10_000);
            while (view.Phase == RoundPhase.PlayerTurn)
            {
                view = table.Hit();
            }
        }
    }

    [Fact]
    public void EveryCardInAShoeIsDealtExactlyOnce()
    {
        var shoe = new Shoe(2, new Random(9));
        var seen = new Dictionary<string, int>();

        var total = shoe.TotalCards;
        Assert.Equal(104, total);

        for (var i = 0; i < total; i++)
        {
            var code = shoe.Draw().Code;
            seen[code] = seen.GetValueOrDefault(code) + 1;
        }

        Assert.Equal(52, seen.Count);
        Assert.All(seen.Values, count => Assert.Equal(2, count));
        Assert.Equal(0, shoe.Remaining);
        Assert.Throws<InvalidOperationException>(() => shoe.Draw());
    }
}
