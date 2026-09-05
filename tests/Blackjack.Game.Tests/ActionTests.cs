using Blackjack.Game;

namespace Blackjack.Tests;

public class ActionTests
{
    [Fact]
    public void DoubleTakesExactlyOneCardAndDoublesTheStake()
    {
        // Player 5/6 = 11, dealer K/7 = 17. The double draws a nine for 20.
        var table = Deal.Table("5S KH 6D 7C 9H");
        table.Deal(Deal.Wager);
        var view = table.Double();

        Assert.Equal(RoundPhase.Settled, view.Phase);
        Assert.Equal(3, view.PlayerHands[0].Cards.Count);
        Assert.Equal(20_000, view.TotalWagered);
        Assert.Equal(40_000, view.TotalReturned);
        Assert.Equal(20_000, view.Net);
    }

    [Fact]
    public void DoublingIntoABustLosesTheDoubledStake()
    {
        // Player 8/6 = 14, so the king actually busts it. An 11 could not -- the
        // best a single card can make of 11 is 21.
        var table = Deal.Table("8S KH 6D 7C KD");
        table.Deal(Deal.Wager);
        var view = table.Double();

        Assert.Equal(HandOutcome.Bust, view.PlayerHands[0].Outcome);
        Assert.Equal(20_000, view.TotalWagered);
        Assert.Equal(0, view.TotalReturned);
    }

    [Fact]
    public void DoubleAndSplitVanishAfterTheFirstDraw()
    {
        var table = Deal.Table("5S KH 6D 7C 2H 3D");
        var dealt = table.Deal(Deal.Wager);
        Assert.Contains(PlayerAction.Double, dealt.AvailableActions);

        // Both are first-decision-only; a three-card hand can do neither.
        var afterHit = table.Hit();
        Assert.DoesNotContain(PlayerAction.Double, afterHit.AvailableActions);
        Assert.DoesNotContain(PlayerAction.Split, afterHit.AvailableActions);
        Assert.Contains(PlayerAction.Hit, afterHit.AvailableActions);
    }

    [Fact]
    public void SplittingCreatesTwoStakedHands()
    {
        // Player 8/8 against dealer K/7. Each new hand draws one card.
        var table = Deal.Table("8S KH 8D 7C 3H 9D");
        var dealt = table.Deal(Deal.Wager);
        Assert.Contains(PlayerAction.Split, dealt.AvailableActions);

        var view = table.Split();

        Assert.Equal(2, view.PlayerHands.Count);
        Assert.Equal(RoundPhase.PlayerTurn, view.Phase);
        Assert.Equal(0, view.ActiveHandIndex);
        Assert.Equal(20_000, view.TotalWagered);
        Assert.All(view.PlayerHands, hand => Assert.Equal(2, hand.Cards.Count));
        Assert.Equal(11, view.PlayerHands[0].Value);
        Assert.Equal(17, view.PlayerHands[1].Value);
    }

    [Fact]
    public void TwentyOneAfterASplitPaysEvenMoneyNotThreeToTwo()
    {
        // This is the split rule people get wrong: an ace and a ten on a split hand
        // is 21, but it is not a natural and must not pay 3:2.
        var table = Deal.Table("AS KH AD 7C KD 5H");
        table.Deal(Deal.Wager);
        var view = table.Split();

        Assert.Equal(RoundPhase.Settled, view.Phase);
        Assert.Equal(21, view.PlayerHands[0].Value);
        Assert.Equal(HandOutcome.Win, view.PlayerHands[0].Outcome);
        Assert.Equal(20_000, view.PlayerHands[0].Returned);

        // 3:2 would have returned 25,000 on that hand.
        Assert.Equal(20_000, view.TotalReturned);
        Assert.Equal(0, view.Net);
    }

    [Fact]
    public void SplitAcesGetOneCardEachAndAreForcedToStand()
    {
        var table = Deal.Table("AS KH AD 7C KD 5H");
        table.Deal(Deal.Wager);
        var view = table.Split();

        Assert.All(view.PlayerHands, hand => Assert.Equal(2, hand.Cards.Count));
        Assert.All(view.PlayerHands, hand => Assert.Equal(HandStatus.Stood, hand.Status));
        Assert.Empty(view.AvailableActions);
    }

    [Fact]
    public void SplitAcesPlayOnWhenTheOneCardRuleIsOff()
    {
        var rules = new Rules { OneCardAfterAceSplit = false };
        var table = Deal.Table("AS KH AD 7C 5D 5H", rules);
        table.Deal(Deal.Wager);
        var view = table.Split();

        Assert.Equal(RoundPhase.PlayerTurn, view.Phase);
        Assert.Contains(PlayerAction.Hit, view.AvailableActions);
    }

    [Fact]
    public void SplitLimitIsEnforced()
    {
        var rules = new Rules { MaxSplits = 1 };
        var table = Deal.Table("8S KH 8D 7C 8H 8C", rules);
        table.Deal(Deal.Wager);
        var view = table.Split();

        // Both resulting hands are pairs of eights again, but the single allowed
        // split is spent, so the option is gone.
        Assert.Equal(16, view.PlayerHands[0].Value);
        Assert.Equal(16, view.PlayerHands[1].Value);
        Assert.DoesNotContain(PlayerAction.Split, view.AvailableActions);
    }

    [Fact]
    public void DoubleAfterSplitCanBeTurnedOff()
    {
        var rules = new Rules { DoubleAfterSplit = false };
        var table = Deal.Table("8S KH 8D 7C 3H 9D", rules);
        table.Deal(Deal.Wager);
        var view = table.Split();

        Assert.DoesNotContain(PlayerAction.Double, view.AvailableActions);
    }

    [Fact]
    public void HittingToTwentyOneStandsAutomatically()
    {
        var table = Deal.Table("5S KH 6D 7C KD");
        table.Deal(Deal.Wager);
        var view = table.Hit();

        // 5/6/K = 21. Leaving the hand active would let a client hit and bust it.
        Assert.Equal(RoundPhase.Settled, view.Phase);
        Assert.Equal(21, view.PlayerHands[0].Value);
        Assert.Equal(HandOutcome.Win, view.PlayerHands[0].Outcome);
    }

    [Fact]
    public void IllegalActionsAreRejected()
    {
        var table = Deal.Table("KS KH 9D 7C");

        // Nothing is legal before a bet is placed.
        Assert.Throws<InvalidOperationException>(() => table.Hit());

        table.Deal(Deal.Wager);

        // 9/K is not a pair.
        Assert.Throws<InvalidOperationException>(() => table.Split());

        table.Stand();

        // The round is settled; the client cannot keep playing it.
        Assert.Throws<InvalidOperationException>(() => table.Hit());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(999)]
    [InlineData(500_001)]
    public void WagersOutsideTheTableLimitsAreRejected(int wager)
    {
        var table = Deal.Table("KS KH 9D 7C");

        Assert.Throws<ArgumentOutOfRangeException>(() => table.Deal(wager));
    }

    [Fact]
    public void DealingTwiceWithoutSettlingIsRejected()
    {
        var table = Deal.Table("8S KH 8D 7C 3H 9D");
        table.Deal(Deal.Wager);

        Assert.Throws<InvalidOperationException>(() => table.Deal(Deal.Wager));
    }
}
