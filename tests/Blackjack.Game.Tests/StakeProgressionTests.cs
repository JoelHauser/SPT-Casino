using Blackjack.Game;

namespace Blackjack.Tests;

/// <summary>
/// The server collects money by debiting <c>TotalWagered - alreadyStaked</c> after
/// every action. That arithmetic is only correct if the table raises TotalWagered
/// by exactly one bet per double and per split, so these pin the progression the
/// callbacks rely on. A bug here charges or refunds the player the wrong amount.
/// </summary>
public class StakeProgressionTests
{
    [Fact]
    public void DealStakesExactlyOneBet()
    {
        var view = Deal.Table("KS KH 9D 7C").Deal(Deal.Wager);

        Assert.Equal(10_000, view.TotalWagered);
    }

    [Fact]
    public void DoubleRaisesTheStakeByOneMoreBet()
    {
        var table = Deal.Table("8S KH 6D 7C 2H");
        var dealt = table.Deal(Deal.Wager);
        Assert.Equal(10_000, dealt.TotalWagered);

        var doubled = table.Double();
        Assert.Equal(20_000, doubled.TotalWagered);
    }

    [Fact]
    public void SplitRaisesTheStakeByOneMoreBet()
    {
        var table = Deal.Table("8S KH 8D 7C 3H 9D");
        table.Deal(Deal.Wager);
        var split = table.Split();

        // Two hands at one bet each, not one hand at two bets.
        Assert.Equal(20_000, split.TotalWagered);
        Assert.All(split.PlayerHands, hand => Assert.Equal(10_000, hand.Wager));
    }

    [Fact]
    public void DoublingAfterASplitStakesThreeBets()
    {
        var table = Deal.Table("8S KH 8D 7C 3H 9D 5C");
        table.Deal(Deal.Wager);
        var split = table.Split();
        Assert.Equal(20_000, split.TotalWagered);

        // Hand one is 8/3 = 11, an obvious double.
        var doubled = table.Double();
        Assert.Equal(30_000, doubled.TotalWagered);
    }

    [Fact]
    public void EachSuccessiveSplitAddsOneBet()
    {
        // 8/8 splits, the first hand draws another eight and splits again.
        var table = Deal.Table("8S KH 8D 7C 8H 4D 9C 5H");
        table.Deal(Deal.Wager);

        var first = table.Split();
        Assert.Equal(20_000, first.TotalWagered);

        var second = table.Split();
        Assert.Equal(30_000, second.TotalWagered);
        Assert.Equal(3, second.PlayerHands.Count);
    }

    [Fact]
    public void StakeNeverDropsDuringARound()
    {
        // The server would have to refund mid-round if it ever did, and it has no
        // code path to do that.
        var rng = new Random(11);
        var table = new BlackjackTable(new Rules(), new Random(11));

        for (var round = 0; round < 500; round++)
        {
            var view = table.Deal(10_000);
            var staked = view.TotalWagered;

            while (view.Phase == RoundPhase.PlayerTurn)
            {
                var actions = view.AvailableActions;
                view = actions[rng.Next(actions.Count)] switch
                {
                    PlayerAction.Hit => table.Hit(),
                    PlayerAction.Stand => table.Stand(),
                    PlayerAction.Double => table.Double(),
                    PlayerAction.Split => table.Split(),
                    _ => throw new InvalidOperationException(),
                };

                Assert.True(view.TotalWagered >= staked, "Stake decreased mid-round.");
                staked = view.TotalWagered;
            }

            // Every bet must be a whole multiple of the opening wager.
            Assert.Equal(0, staked % 10_000);
        }
    }
}
