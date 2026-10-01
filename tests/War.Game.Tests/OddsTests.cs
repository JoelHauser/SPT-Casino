namespace War.Game.Tests;

/// <summary>
/// The edge three ways: against the published figures, against an enumeration that
/// shares no arithmetic with the closed form, and against a simulation that plays the
/// real table with real burns.
/// </summary>
public class OddsTests
{
    /// <summary>
    /// The numbers the rules page quotes, and the numbers that pin the shoe at six
    /// decks. Worked by hand first: 23/311 for a tie, 7086/95790 for a tie at war.
    /// </summary>
    [Fact]
    public void SixDecksGiveThePublishedFigures()
    {
        Assert.Equal(23d / 311d, Odds.TieChance(), 12);
        Assert.Equal(7086d / 95790d, Odds.WarTieChance(), 12);

        // "around 2.88 percent"
        Assert.Equal(2.88, Odds.HouseEdgePercent(), 2);

        // Surrendering every tie: half the ante, 23 times in 311.
        Assert.Equal(-23d / 622d, Odds.MainBetAlwaysSurrender(), 12);

        // "over 18 percent"
        Assert.Equal(-58d / 311d, Odds.TieBet(), 12);
        Assert.True(Odds.TieBet() < -0.18);
    }

    /// <summary>
    /// Why six and not another count: the tie bet's edge at the other usual shoes.
    /// Written because the first draft of the comment in Rules.cs said 15.3% for one
    /// deck, which is the infinite shoe's number, not one deck's.
    /// </summary>
    [Fact]
    public void OnlySixDecksMatchOverEighteenPercentOnTheTieBet()
    {
        Assert.Equal(-18d / 51d, Odds.TieBet(1), 12);    // 35.3%
        Assert.Equal(-74d / 415d, Odds.TieBet(8), 12);   // 17.8%
        Assert.True(Odds.TieBet(8) > -0.18);
        Assert.True(Odds.TieBet(6) < -0.18);
    }

    /// <summary>Going to war is always the better choice. Surrender costs more.</summary>
    [Fact]
    public void GoingToWarBeatsSurrendering()
    {
        Assert.True(Odds.WarValue() > -0.5);
        Assert.True(Odds.MainBetAlwaysWar() > Odds.MainBetAlwaysSurrender());
    }

    /// <summary>
    /// Every combination of rank for the player's card, the dealer's, and both war
    /// cards, weighted by how many of that rank are left at each draw. No symmetry
    /// argument, no shortcut -- if the closed form is wrong this disagrees with it.
    /// Burns are left out here, which is the one claim this cannot check; the
    /// simulation below plays them.
    /// </summary>
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(6)]
    [InlineData(8)]
    public void TheClosedFormAgreesWithEnumeration(int decks)
    {
        var n = decks * 52;
        var counts = Enumerable.Repeat(decks * 4, 13).ToArray();
        var main = 0d;
        var tieBet = 0d;

        for (var p = 0; p < 13; p++)
        {
            var pP = (double)counts[p] / n;
            counts[p]--;

            for (var d = 0; d < 13; d++)
            {
                var pD = pP * counts[d] / (n - 1);

                if (p != d)
                {
                    main += pD * (p > d ? 1 : -1);
                    tieBet -= pD;
                    continue;
                }

                tieBet += pD * Rules.TiePays;
                counts[d]--;

                for (var wp = 0; wp < 13; wp++)
                {
                    var pWp = pD * counts[wp] / (n - 2);
                    counts[wp]--;

                    for (var wd = 0; wd < 13; wd++)
                    {
                        var pWd = pWp * counts[wd] / (n - 3);
                        main += pWd * (wp >= wd ? 1 : -2);
                    }

                    counts[wp]++;
                }

                counts[d]++;
            }

            counts[p]++;
        }

        Assert.Equal(Odds.MainBetAlwaysWar(decks), main, 12);
        Assert.Equal(Odds.TieBet(decks), tieBet, 12);
    }

    /// <summary>
    /// Two million rounds at the real table, burns and reshuffles included, going to war
    /// every time with a tie bet the same size as the ante. The main bet's standard
    /// deviation is about one ante a round, so the error here is around 0.0007 and the
    /// tolerance is several times that.
    /// </summary>
    [Fact]
    public void TheTablePlaysToTheComputedEdge()
    {
        const int rounds = 2_000_000;
        var table = new WarTable(new Random(20261001));
        long main = 0;
        long tie = 0;

        for (var i = 0; i < rounds; i++)
        {
            var round = table.Deal(1, 1);

            if (round.Phase == Phase.AwaitingDecision)
            {
                round = table.GoToWar();
            }

            main += round.MainReturn - round.Ante - round.Raise;
            tie += round.TieReturn - round.TieBet;
        }

        Assert.Equal(Odds.MainBetAlwaysWar(), (double)main / rounds, 0.004);
        Assert.Equal(Odds.TieBet(), (double)tie / rounds, 0.015);
    }
}
