namespace Roulette.Game.Tests;

/// <summary>
/// The wheel is data, and data is exactly what goes wrong quietly. A transposed
/// pair in the order lands the ball in the wrong place on screen while the result
/// stays correct, which reads as an animation bug for as long as anyone looks.
/// </summary>
public class WheelTests
{
    [Theory]
    [InlineData(WheelKind.European, 37)]
    [InlineData(WheelKind.American, 38)]
    public void TheWheelHasThePocketsItShould(WheelKind kind, int expected)
    {
        Assert.Equal(expected, new Wheel(kind).PocketCount);
    }

    [Theory]
    [InlineData(WheelKind.European)]
    [InlineData(WheelKind.American)]
    public void EveryNumberAppearsExactlyOnce(WheelKind kind)
    {
        var numbers = new Wheel(kind).Pockets.Select(p => p.Number).ToList();

        Assert.Equal(numbers.Count, numbers.Distinct().Count());
    }

    [Theory]
    [InlineData(WheelKind.European)]
    [InlineData(WheelKind.American)]
    public void EighteenRedsEighteenBlacksAndTheZeroesAreGreen(WheelKind kind)
    {
        var wheel = new Wheel(kind);

        Assert.Equal(18, wheel.Pockets.Count(p => p.Colour == PocketColour.Red));
        Assert.Equal(18, wheel.Pockets.Count(p => p.Colour == PocketColour.Black));
        Assert.All(
            wheel.Pockets.Where(p => p.IsZero),
            p => Assert.Equal(PocketColour.Green, p.Colour));
    }

    /// <summary>
    /// A European wheel alternates red and black the whole way round, straight
    /// through the zero: 26 black, 0, 32 red. Nothing in the rules needs this, which
    /// is precisely why it is worth a test -- it is the property a mistyped number
    /// breaks first.
    /// </summary>
    [Fact]
    public void TheEuropeanWheelAlternatesColoursTheWholeWayRound()
    {
        var coloured = new Wheel(WheelKind.European).Pockets.Where(p => !p.IsZero).ToList();

        for (var i = 0; i < coloured.Count; i++)
        {
            var next = coloured[(i + 1) % coloured.Count];

            Assert.True(
                coloured[i].Colour != next.Colour,
                $"{coloured[i]} and {next} are both {next.Colour}.");
        }
    }

    /// <summary>
    /// An American wheel alternates too, **except at the zeroes**, and that is not a
    /// flaw in the order: each zero is deliberately flanked by a matching pair -- 0
    /// between two blacks, 00 between two reds. Asserting plain alternation here
    /// fails against a correct wheel, which is how this test started out.
    /// </summary>
    [Fact]
    public void TheAmericanWheelBreaksItsColoursAtTheZeroes()
    {
        var wheel = new Wheel(WheelKind.American);
        var pockets = wheel.Pockets;

        foreach (var (zero, expected) in new[]
                 {
                     (0, PocketColour.Black),
                     (Pocket.DoubleZero, PocketColour.Red),
                 })
        {
            var at = wheel.PositionOf(zero);
            var before = pockets[(at - 1 + pockets.Count) % pockets.Count];
            var after = pockets[(at + 1) % pockets.Count];

            Assert.Equal(expected, before.Colour);
            Assert.Equal(expected, after.Colour);
        }

        // Everywhere else it alternates, so a mistyped number is still caught.
        for (var i = 0; i < pockets.Count; i++)
        {
            var next = pockets[(i + 1) % pockets.Count];

            if (!pockets[i].IsZero && !next.IsZero)
            {
                Assert.True(
                    pockets[i].Colour != next.Colour,
                    $"{pockets[i]} and {next} are both {next.Colour}.");
            }
        }
    }

    /// <summary>
    /// Pins the published order at both ends and at the zero. Reordering the wheel
    /// moves where the client lands the ball, so this is a contract and not a
    /// preference.
    /// </summary>
    [Fact]
    public void TheEuropeanOrderIsThePublishedOne()
    {
        var wheel = new Wheel(WheelKind.European);

        Assert.Equal(0, wheel.Pockets[0].Number);
        Assert.Equal(32, wheel.Pockets[1].Number);
        Assert.Equal(26, wheel.Pockets[^1].Number);
        Assert.Equal(0, wheel.PositionOf(0));
        Assert.Equal(36, wheel.PositionOf(26));
    }

    [Fact]
    public void TheAmericanWheelPutsTheZeroesOpposite()
    {
        var wheel = new Wheel(WheelKind.American);
        var apart = Math.Abs(wheel.PositionOf(Pocket.DoubleZero) - wheel.PositionOf(0));

        Assert.Equal(wheel.PocketCount / 2, apart);
    }

    /// <summary>
    /// The double zero is its own pocket. Folding it onto 0 would make a straight-up
    /// bet on zero come in twice as often as it should, which is the American house
    /// edge arriving in the wrong player's pocket.
    /// </summary>
    [Fact]
    public void TheDoubleZeroIsNotTheZero()
    {
        var wheel = new Wheel(WheelKind.American);

        Assert.NotEqual(wheel.PositionOf(0), wheel.PositionOf(Pocket.DoubleZero));
        Assert.Equal("00", wheel.PocketFor(Pocket.DoubleZero).Label);
    }

    [Fact]
    public void AEuropeanWheelHasNoDoubleZero()
    {
        var wheel = new Wheel(WheelKind.European);

        Assert.Throws<ArgumentOutOfRangeException>(() => wheel.PositionOf(Pocket.DoubleZero));
    }

    /// <summary>
    /// Every pocket comes up, and the spin reads the injected source rather than one
    /// of its own -- the same seam a stacked deck gives the other two mods.
    /// </summary>
    [Fact]
    public void EveryPocketCanComeUp()
    {
        var wheel = new Wheel();
        var rng = new Random(1234);

        var seen = Enumerable.Range(0, 200_000)
            .Select(_ => wheel.Spin(rng).Number)
            .Distinct()
            .Count();

        Assert.Equal(wheel.PocketCount, seen);
    }

    /// <summary>
    /// Two wheels on the same seed spin the same way, which is what lets a reported
    /// spin be got back. The randomness has to come from the injected source and
    /// nowhere else for that to hold.
    /// </summary>
    [Fact]
    public void TheSameSeedSpinsTheSameWay()
    {
        var wheel = new Wheel();

        var first = Spins(wheel, new Random(7));
        var again = Spins(wheel, new Random(7));
        var other = Spins(wheel, new Random(8));

        Assert.Equal(first, again);
        Assert.NotEqual(first, other);
    }

    private static List<int> Spins(Wheel wheel, Random rng) =>
        [.. Enumerable.Range(0, 50).Select(_ => wheel.Spin(rng).Number)];
}
