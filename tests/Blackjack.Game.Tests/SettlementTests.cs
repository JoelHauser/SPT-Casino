using Blackjack.Game;

namespace Blackjack.Tests;

public class SettlementTests
{
    [Fact]
    public void NaturalPaysThreeToTwoByDefault()
    {
        // Player AS/KH = blackjack, dealer 9H/7D = 16 and never gets to draw.
        var view = Deal.Table("AS 9H KH 7D").Deal(Deal.Wager);

        Assert.Equal(RoundPhase.Settled, view.Phase);
        Assert.Equal(HandOutcome.Blackjack, view.PlayerHands[0].Outcome);
        Assert.Equal(25_000, view.TotalReturned);
        Assert.Equal(15_000, view.Net);
    }

    [Fact]
    public void APerRoundPayoutOverridesTheTableDefault()
    {
        // What the server does for valuables: same table, same shoe, even money for
        // this round because the stake cannot be halved.
        var view = Deal.Table("AS 9H KH 7D").Deal(Deal.Wager, blackjackPayout: 1.0);

        Assert.Equal(20_000, view.TotalReturned);
    }

    [Fact]
    public void SixToFivePayoutIsHonoured()
    {
        var rules = new Rules { BlackjackPayout = 1.2 };
        var view = Deal.Table("AS 9H KH 7D", rules).Deal(Deal.Wager);

        Assert.Equal(22_000, view.TotalReturned);
    }

    [Fact]
    public void DealerNaturalEndsTheRoundBeforeThePlayerActs()
    {
        // The peek is what stops a player doubling or splitting into a hand that
        // was already lost, so the round must be Settled the moment it is dealt.
        var view = Deal.Table("7S AH 7D KC").Deal(Deal.Wager);

        Assert.Equal(RoundPhase.Settled, view.Phase);
        Assert.Equal(HandOutcome.Lose, view.PlayerHands[0].Outcome);
        Assert.Empty(view.AvailableActions);
        Assert.Equal(0, view.TotalReturned);
    }

    [Fact]
    public void TwoNaturalsPush()
    {
        var view = Deal.Table("AS AH KD KC").Deal(Deal.Wager);

        Assert.Equal(HandOutcome.Push, view.PlayerHands[0].Outcome);
        Assert.Equal(Deal.Wager, view.TotalReturned);
        Assert.Equal(0, view.Net);
    }

    [Fact]
    public void PlayerBustLosesEvenWhenTheDealerWouldAlsoBust()
    {
        // The house edge lives here: the player busts first, so the dealer is never
        // asked to draw and a hand that would have gone over 21 is irrelevant.
        var table = Deal.Table("KS 6H 9D 5C 8H");
        table.Deal(Deal.Wager);
        var view = table.Hit();

        Assert.Equal(HandOutcome.Bust, view.PlayerHands[0].Outcome);
        Assert.Equal(0, view.TotalReturned);
        Assert.Equal(RoundPhase.Settled, view.Phase);
        Assert.Equal(2, view.Dealer.Cards.Count);
    }

    [Fact]
    public void EqualTotalsPush()
    {
        // Player K/9 = 19 against dealer K/9 = 19.
        var table = Deal.Table("KS KH 9D 9C");
        table.Deal(Deal.Wager);
        var view = table.Stand();

        Assert.Equal(HandOutcome.Push, view.PlayerHands[0].Outcome);
        Assert.Equal(Deal.Wager, view.TotalReturned);
    }

    [Fact]
    public void BeatingTheDealerPaysEvenMoney()
    {
        // Player K/9 = 19 against dealer K/7 = 17, which must stand.
        var table = Deal.Table("KS KH 9D 7C");
        table.Deal(Deal.Wager);
        var view = table.Stand();

        Assert.Equal(HandOutcome.Win, view.PlayerHands[0].Outcome);
        Assert.Equal(20_000, view.TotalReturned);
    }

    [Fact]
    public void DealerBustPaysEveryHandStillStanding()
    {
        // Dealer 6/9 = 15, must draw, and draws a king.
        var table = Deal.Table("KS 6H 2D 9C KH");
        table.Deal(Deal.Wager);
        var view = table.Stand();

        Assert.True(view.Dealer.Value > 21);
        Assert.Equal(HandOutcome.Win, view.PlayerHands[0].Outcome);
        Assert.Equal(20_000, view.TotalReturned);
    }
}
