namespace War.Game.Tests;

/// <summary>
/// Every way a round can end, each pinned on a stacked shoe and each written from the
/// rules rather than from the code: cachecreek.com/casino-war, quoted in Rules.cs.
/// </summary>
public class SettlementTests
{
    private const int Ante = 1_000;

    [Fact]
    public void AHigherCardPaysEvenMoney()
    {
        var round = Deal.Stacked("KH", "9S").Deal(Ante, 0);

        Assert.Equal(Phase.Settled, round.Phase);
        Assert.Equal(Outcome.Win, round.Outcome);
        Assert.Equal(2 * Ante, round.MainReturn);
        Assert.Equal(Ante, round.Profit);
    }

    [Fact]
    public void ALowerCardLosesTheBet()
    {
        var round = Deal.Stacked("4C", "JD").Deal(Ante, 0);

        Assert.Equal(Phase.Settled, round.Phase);
        Assert.Equal(Outcome.Lose, round.Outcome);
        Assert.Equal(0, round.Returned);
        Assert.Equal(-Ante, round.Profit);
    }

    /// <summary>An ace beats a king, and a two loses to everything.</summary>
    [Fact]
    public void TheAceIsTheHighestCard()
    {
        Assert.Equal(Outcome.Win, Deal.Stacked("AC", "KD").Deal(Ante, 0).Outcome);
        Assert.Equal(Outcome.Lose, Deal.Stacked("2C", "3D").Deal(Ante, 0).Outcome);
    }

    /// <summary>
    /// Suits never matter. Two sevens tie whatever they are, and the round stops for
    /// the player to choose.
    /// </summary>
    [Fact]
    public void TheSameRankIsATieWhateverTheSuit()
    {
        var round = Deal.Stacked("7C", "7H").Deal(Ante, 0);

        Assert.Equal(Phase.AwaitingDecision, round.Phase);
        Assert.Equal(Outcome.None, round.Outcome);
        Assert.Equal(0, round.MainReturn);
    }

    [Fact]
    public void ATieBetPaysTenToOneOnATie()
    {
        var round = Deal.Stacked("7C", "7H").Deal(Ante, 200);

        Assert.True(round.TieWon);
        Assert.Equal(200 * 11, round.TieReturn);
    }

    [Fact]
    public void ATieBetIsLostWhenTheCardsDiffer()
    {
        var won = Deal.Stacked("KH", "9S").Deal(Ante, 200);
        var lost = Deal.Stacked("4C", "JD").Deal(Ante, 200);

        Assert.Equal(0, won.TieReturn);
        Assert.Equal(0, lost.TieReturn);
        Assert.Equal(Ante - 200, won.Profit);
        Assert.Equal(-Ante - 200, lost.Profit);
    }

    /// <summary>"Surrender (forfeiting half the original bet)."</summary>
    [Fact]
    public void SurrenderingGivesHalfTheBetBack()
    {
        var table = Deal.Stacked("7C", "7H");
        table.Deal(Ante, 0);

        var round = table.Surrender();

        Assert.Equal(Phase.Settled, round.Phase);
        Assert.Equal(Outcome.Surrender, round.Outcome);
        Assert.Equal(Ante / 2, round.MainReturn);
        Assert.Equal(-Ante / 2, round.Profit);
        Assert.Equal(0, round.Raise);
    }

    /// <summary>The odd unit of an odd ante stays with the player.</summary>
    [Fact]
    public void SurrenderingAnOddBetRoundsInThePlayersFavour()
    {
        var table = Deal.Stacked("7C", "7H");
        table.Deal(1_001, 0);

        Assert.Equal(501, table.Surrender().MainReturn);
    }

    /// <summary>
    /// "If the player's second card is equal to or greater than the dealer's, they win
    /// and receive an even-money payout on their original bet, with the additional bet
    /// being returned as a push." Two antes down, three back.
    /// </summary>
    [Fact]
    public void WinningTheWarPaysOneAnteOnTwoStaked()
    {
        var table = Deal.Stacked("7C", "7H", "2S", "3S", "4S", "QD", "5C");
        table.Deal(Ante, 0);

        var round = table.GoToWar();

        Assert.Equal(Outcome.WarWin, round.Outcome);
        Assert.Equal(Ante, round.Raise);
        Assert.Equal(2 * Ante, round.Staked);
        Assert.Equal(3 * Ante, round.MainReturn);
        Assert.Equal(Ante, round.Profit);
    }

    [Fact]
    public void LosingTheWarLosesBothBets()
    {
        var table = Deal.Stacked("7C", "7H", "2S", "3S", "4S", "5C", "QD");
        table.Deal(Ante, 0);

        var round = table.GoToWar();

        Assert.Equal(Outcome.WarLose, round.Outcome);
        Assert.Equal(0, round.MainReturn);
        Assert.Equal(-2 * Ante, round.Profit);
    }

    /// <summary>"Equal to or greater" -- a tie at war is a win, not a second war.</summary>
    [Fact]
    public void ATieAtWarGoesToThePlayer()
    {
        var table = Deal.Stacked("7C", "7H", "2S", "3S", "4S", "9D", "9C");
        table.Deal(Ante, 0);

        var round = table.GoToWar();

        Assert.Equal(Phase.Settled, round.Phase);
        Assert.Equal(Outcome.WarWin, round.Outcome);
        Assert.Equal(3 * Ante, round.MainReturn);
    }

    /// <summary>
    /// Three burned, then one each. If the burn were skipped the player would get the
    /// two of spades and lose; if it burned four, the dealer would get the queen.
    /// </summary>
    [Fact]
    public void TheDealerBurnsExactlyThreeCardsBeforeTheWar()
    {
        var table = Deal.Stacked("7C", "7H", "2S", "3S", "4S", "KD", "5C", "QD");
        table.Deal(Ante, 0);

        var round = table.GoToWar();

        Assert.Equal(3, round.Burned);
        Assert.Equal("KD", round.PlayerWarCard?.Code);
        Assert.Equal("5C", round.DealerWarCard?.Code);
    }

    /// <summary>
    /// The tie bet is settled on the first two cards and nothing after them. Losing the
    /// war does not take it back; winning the war does not pay it twice.
    /// </summary>
    [Fact]
    public void TheTieBetIsSettledBeforeTheWarAndNotAgain()
    {
        var lost = Deal.Stacked("7C", "7H", "2S", "3S", "4S", "5C", "QD");
        lost.Deal(Ante, 100);
        var afterLoss = lost.GoToWar();

        var won = Deal.Stacked("7C", "7H", "2S", "3S", "4S", "QD", "5C");
        won.Deal(Ante, 100);
        var afterWin = won.GoToWar();

        Assert.Equal(1_100, afterLoss.TieReturn);
        Assert.Equal(1_100, afterWin.TieReturn);
        Assert.Equal(1_100 - (2 * Ante) - 100, afterLoss.Profit);
        Assert.Equal(1_100 + (3 * Ante) - (2 * Ante) - 100, afterWin.Profit);
    }

    [Fact]
    public void ANewHandCannotBeDealtOverAnUndecidedTie()
    {
        var table = Deal.Stacked("7C", "7H", "KH", "9S");
        table.Deal(Ante, 0);

        Assert.Throws<InvalidOperationException>(() => table.Deal(Ante, 0));
        Assert.Equal(Phase.AwaitingDecision, table.Phase);
    }

    [Fact]
    public void ThereIsNoWarOrSurrenderWithoutATie()
    {
        var fresh = Deal.Stacked("KH", "9S");
        Assert.Throws<InvalidOperationException>(() => fresh.GoToWar());
        Assert.Throws<InvalidOperationException>(() => fresh.Surrender());

        fresh.Deal(Ante, 0);
        Assert.Throws<InvalidOperationException>(() => fresh.GoToWar());
        Assert.Throws<InvalidOperationException>(() => fresh.Surrender());
    }

    /// <summary>A tie can be decided once. The second answer is refused.</summary>
    [Fact]
    public void ATieIsDecidedOnlyOnce()
    {
        var table = Deal.Stacked("7C", "7H", "2S", "3S", "4S", "QD", "5C");
        table.Deal(Ante, 0);
        table.GoToWar();

        Assert.Throws<InvalidOperationException>(() => table.GoToWar());
        Assert.Throws<InvalidOperationException>(() => table.Surrender());
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(-1, 0)]
    [InlineData(100, -1)]
    public void ABetOfNothingOrLessIsRefused(int ante, int tie) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => Deal.Stacked("KH", "9S").Deal(ante, tie));
}
