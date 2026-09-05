namespace Poker.Game.Tests;

public class PotBuilderTests
{
    private static Contribution In(int seat, int amount) => new(seat, amount, Folded: false);

    private static Contribution Out(int seat, int amount) => new(seat, amount, Folded: true);

    [Fact]
    public void EqualBetsWithNoAllInsMakeOnePot()
    {
        var layout = PotBuilder.Build([In(0, 100), In(1, 100), In(2, 100)]);

        var pot = Assert.Single(layout.Pots);
        Assert.Equal(300, pot.Amount);
        Assert.Equal([0, 1, 2], pot.EligibleSeats);
        Assert.Empty(layout.Refunds);
    }

    [Fact]
    public void AFoldedSeatPaysIntoThePotButCannotWinIt()
    {
        var layout = PotBuilder.Build([In(0, 100), In(1, 100), Out(2, 100)]);

        var pot = Assert.Single(layout.Pots);
        Assert.Equal(300, pot.Amount);
        Assert.Equal([0, 1], pot.EligibleSeats);
    }

    [Fact]
    public void AShortAllInCanOnlyWinWhatItCovered()
    {
        // Seat 0 is all-in for 50; the other two go to 200.
        var layout = PotBuilder.Build([In(0, 50), In(1, 200), In(2, 200)]);

        Assert.Equal(2, layout.Pots.Count);

        Assert.Equal(150, layout.Pots[0].Amount);
        Assert.Equal([0, 1, 2], layout.Pots[0].EligibleSeats);

        Assert.Equal(300, layout.Pots[1].Amount);
        Assert.Equal([1, 2], layout.Pots[1].EligibleSeats);
    }

    [Fact]
    public void EveryAllInLevelOpensItsOwnSidePot()
    {
        var layout = PotBuilder.Build([In(0, 50), In(1, 120), In(2, 300), In(3, 300)]);

        Assert.Equal(3, layout.Pots.Count);

        Assert.Equal(200, layout.Pots[0].Amount);           // 50 x 4
        Assert.Equal([0, 1, 2, 3], layout.Pots[0].EligibleSeats);

        Assert.Equal(210, layout.Pots[1].Amount);           // 70 x 3
        Assert.Equal([1, 2, 3], layout.Pots[1].EligibleSeats);

        Assert.Equal(360, layout.Pots[2].Amount);           // 180 x 2
        Assert.Equal([2, 3], layout.Pots[2].EligibleSeats);
    }

    [Fact]
    public void ABetNobodyCoveredIsReturnedRatherThanPotted()
    {
        // Seat 1 bets 200 into a seat that is all-in for 50. Only 50 was ever in play.
        var layout = PotBuilder.Build([In(0, 50), In(1, 200)]);

        var pot = Assert.Single(layout.Pots);
        Assert.Equal(100, pot.Amount);
        Assert.Equal(150, layout.Refunds[1]);
    }

    [Fact]
    public void AFoldedBlindStillSetsTheLevelARaiseWasCalledTo()
    {
        // Seat 1 raises to 200 and everyone folds; the small blind's 10 was matched,
        // so 190 comes back and the pot is the two tens.
        var layout = PotBuilder.Build([Out(0, 10), In(1, 200)]);

        Assert.Equal(190, layout.Refunds[1]);
        var pot = Assert.Single(layout.Pots);
        Assert.Equal(20, pot.Amount);
        Assert.Equal([1], pot.EligibleSeats);
    }

    [Fact]
    public void ChipsFromALayerNobodyCanWinFallIntoTheLayerBelow()
    {
        // Seat 0 all-in for 50. Seats 1 and 2 build a side pot to 200 and then both
        // fold on a later street. Their side-pot chips were called, so they are not
        // returned -- they go to whoever wins the main pot.
        var layout = PotBuilder.Build([In(0, 50), Out(1, 200), Out(2, 200)]);

        var pot = Assert.Single(layout.Pots);
        Assert.Equal(450, pot.Amount);
        Assert.Equal([0], pot.EligibleSeats);
    }

    [Fact]
    public void SeatsThatNeverPutAnythingInAreNotEligibleForAnything()
    {
        var layout = PotBuilder.Build([In(0, 100), In(1, 100), In(2, 0)]);

        var pot = Assert.Single(layout.Pots);
        Assert.Equal(200, pot.Amount);
        Assert.DoesNotContain(2, pot.EligibleSeats);
    }

    [Fact]
    public void NobodyBettingProducesNoPot()
    {
        var layout = PotBuilder.Build([In(0, 0), In(1, 0)]);

        Assert.Empty(layout.Pots);
        Assert.Empty(layout.Refunds);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void EveryChipCommittedIsEitherPottedOrRefunded(int seed)
    {
        // The invariant that matters more than any single layout: a hand cannot
        // create or destroy chips. Checked across random stacks and fold patterns,
        // because the layouts that lose money are the ones nobody thought to write
        // a case for. An end-of-hand stack check would miss errors that cancel.
        var rng = new Random(seed);

        for (var trial = 0; trial < 500; trial++)
        {
            var seats = rng.Next(2, 7);
            var contributions = new List<Contribution>();
            for (var seat = 0; seat < seats; seat++)
            {
                contributions.Add(new Contribution(seat, rng.Next(0, 500), rng.Next(4) == 0));
            }

            var layout = PotBuilder.Build(contributions);

            var committed = contributions.Sum(c => c.Amount);
            var refunded = layout.Refunds.Values.Sum();

            Assert.Equal(committed, layout.Total + refunded);
        }
    }

    [Theory]
    [InlineData(11)]
    [InlineData(12)]
    [InlineData(13)]
    public void EveryPotHasSomebodyWhoCanWinIt(int seed)
    {
        // A pot with no eligible seat is money that settlement would silently drop.
        var rng = new Random(seed);

        for (var trial = 0; trial < 500; trial++)
        {
            var seats = rng.Next(2, 7);
            var contributions = new List<Contribution>();

            // At least one seat always reaches showdown, which is true of any real
            // hand -- everyone folding ends it before the pot is built.
            var survivor = rng.Next(seats);
            for (var seat = 0; seat < seats; seat++)
            {
                contributions.Add(new Contribution(seat, rng.Next(1, 500), seat != survivor && rng.Next(3) == 0));
            }

            var layout = PotBuilder.Build(contributions);

            Assert.All(layout.Pots, pot => Assert.NotEmpty(pot.EligibleSeats));
        }
    }
}
