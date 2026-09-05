using Blackjack.Game;

namespace Blackjack.Tests;

public class DealerTests
{
    [Fact]
    public void StandsOnSoftSeventeenByDefault()
    {
        // Dealer A/6. Drawing here would be the hit-soft-17 rule, which is off.
        var table = Deal.Table("KS AH 9D 6C 5H");
        table.Deal(Deal.Wager);
        var view = table.Stand();

        Assert.Equal(2, view.Dealer.Cards.Count);
        Assert.Equal(17, view.Dealer.Value);
        Assert.Equal(HandOutcome.Win, view.PlayerHands[0].Outcome);
    }

    [Fact]
    public void DrawsToSoftSeventeenWhenTheRuleIsOn()
    {
        var rules = new Rules { DealerHitsSoft17 = true };
        var table = Deal.Table("KS AH 9D 6C 3H", rules);
        table.Deal(Deal.Wager);
        var view = table.Stand();

        // A/6/3 = 20, which now beats the player's 19.
        Assert.Equal(3, view.Dealer.Cards.Count);
        Assert.Equal(20, view.Dealer.Value);
        Assert.Equal(HandOutcome.Lose, view.PlayerHands[0].Outcome);
    }

    [Fact]
    public void StandsOnHardSeventeenEvenWithTheSoftRuleOn()
    {
        // The rule is specifically about *soft* 17; a hard 17 always stands.
        var rules = new Rules { DealerHitsSoft17 = true };
        var table = Deal.Table("KS KH 9D 7C 5H", rules);
        table.Deal(Deal.Wager);
        var view = table.Stand();

        Assert.Equal(2, view.Dealer.Cards.Count);
        Assert.Equal(17, view.Dealer.Value);
    }

    [Fact]
    public void HoleCardIsWithheldUntilTheDealerPlays()
    {
        var table = Deal.Table("KS KH 5D 7C 4H");
        var dealt = table.Deal(Deal.Wager);

        // Only the upcard is present. The hole card is absent from the payload
        // entirely -- anything sent to the client is knowable by the client.
        Assert.Single(dealt.Dealer.Cards);

        // Cards go out player, dealer, player, dealer -- so the upcard is the
        // second card in the stack, not the first.
        Assert.Equal("KH", dealt.Dealer.Cards[0]);
        Assert.Equal(10, dealt.Dealer.Value);

        var settled = table.Stand();
        Assert.Equal(2, settled.Dealer.Cards.Count);
        Assert.Equal(17, settled.Dealer.Value);
    }

    [Fact]
    public void AceUpcardShowsAsElevenBeforeTheReveal()
    {
        var table = Deal.Table("KS AH 5D 6C 4H");
        var dealt = table.Deal(Deal.Wager);

        Assert.Equal(11, dealt.Dealer.Value);
        Assert.True(dealt.Dealer.IsSoft);
    }

    [Fact]
    public void AFreshTableCanBeViewedBeforeAnythingIsDealt()
    {
        // The client asks for state as soon as the panel opens, before any bet. An
        // empty dealer hand must describe itself rather than reaching for a card.
        var view = Deal.Table("KS KH 9D 7C").View();

        Assert.Equal(RoundPhase.AwaitingBet, view.Phase);
        Assert.Empty(view.Dealer.Cards);
        Assert.Equal(0, view.Dealer.Value);
        Assert.Empty(view.PlayerHands);
        Assert.Empty(view.AvailableActions);
    }
}
