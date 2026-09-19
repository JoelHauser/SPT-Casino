namespace HorseRacing.Game.Tests;

/// <summary>
/// What a board pays, and whether the arithmetic that says so is right.
///
/// The house edge is arithmetic over the model rather than something to be discovered
/// by running races. These check both that the arithmetic says something sane and that
/// it says the truth -- **at every course**, since each weights the same horses
/// differently and a formula that is right at one is not thereby right at another.
/// </summary>
public class OddsTests
{
    public static TheoryData<string> Courses()
    {
        var data = new TheoryData<string>();

        foreach (var track in Tracks.All)
        {
            data.Add(track.Id);
        }

        return data;
    }

    private static Track Course(string id) => Tracks.ById(id)!;

    /// <summary>
    /// The cheapest possible check that the model is coherent, and the first thing
    /// worth asserting: the 336 ordered top-threes are mutually exclusive and
    /// exhaustive, so their probabilities sum to one.
    ///
    /// A weight handled twice, a runner allowed to finish in two places, or a
    /// denominator that forgot to shrink as the field did would all show up here and
    /// nowhere else nearly as loudly.
    /// </summary>
    [Theory]
    [MemberData(nameof(Courses))]
    public void EveryOrderedTopThreeTogetherAccountsForExactlyOneRace(string id)
    {
        var track = Course(id);
        var count = 0;
        var total = 0.0;

        foreach (var (_, probability) in Odds.Prefixes(track))
        {
            Assert.True(probability > 0.0, "a prefix with no chance of happening is a bug in the weights.");
            count++;
            total += probability;
        }

        Assert.Equal(Field.Count * (Field.Count - 1) * (Field.Count - 2), count);
        Assert.Equal(1.0, total, 12);
    }

    /// <summary>
    /// **The test that matters.** The closed form in <see cref="Odds"/> is checked
    /// against actually running the races, at every course.
    ///
    /// A formula derived from the same misunderstanding as the code it describes would
    /// agree with itself perfectly, so this deliberately shares nothing with it: it
    /// draws real finishing orders through <see cref="Race.Draw"/> and counts what
    /// happened. Two hundred thousand races puts the standard error on a 20% runner at
    /// about a tenth of a percentage point, so a chance wrong in any way worth caring
    /// about would not land this close.
    ///
    /// Every bet kind is checked, not just Win. Place and Show are where an off-by-one
    /// in the placings window hides, and the pair bets are where ordered and unordered
    /// get confused -- a quinella priced as an exacta is mispriced by exactly a factor
    /// of two, which no balance check would ever catch.
    /// </summary>
    [Theory]
    [InlineData("dash", BetKind.Win, 1, 0)]
    [InlineData("dash", BetKind.Show, 8, 0)]
    [InlineData("dash", BetKind.Exacta, 1, 2)]
    [InlineData("mile", BetKind.Win, 2, 0)]
    [InlineData("mile", BetKind.Place, 6, 0)]
    [InlineData("mile", BetKind.Quinella, 1, 2)]
    [InlineData("marathon", BetKind.Win, 4, 0)]
    [InlineData("marathon", BetKind.Win, 7, 0)]
    [InlineData("marathon", BetKind.Place, 1, 0)]
    [InlineData("marathon", BetKind.Show, 6, 0)]
    [InlineData("marathon", BetKind.Exacta, 4, 3)]
    [InlineData("marathon", BetKind.Quinella, 3, 7)]
    public void TheComputedChanceMatchesWhatActuallyHappens(
        string id, BetKind kind, int first, int second)
    {
        const int races = 200_000;

        var track = Course(id);
        var bet = new Bet(kind, first, second, 0);
        var random = new Random(20260919);
        var won = 0;

        for (var i = 0; i < races; i++)
        {
            if (bet.Covers(Race.Draw(track, random)))
            {
                won++;
            }
        }

        var measured = (double)won / races;
        var computed = Odds.Chance(track, kind, first, second);

        Assert.InRange(measured, computed - 0.005, computed + 0.005);
    }

    /// <summary>
    /// A quinella is the two exactas that make it up, exactly. Worth asserting on its
    /// own because it is the one identity on a board that can be checked without going
    /// anywhere near the enumeration.
    /// </summary>
    [Theory]
    [MemberData(nameof(Courses))]
    public void AQuinellaIsItsTwoExactasAddedTogether(string id)
    {
        var track = Course(id);

        foreach (var a in Field.Runners)
        {
            foreach (var b in Field.Runners)
            {
                if (a.Number >= b.Number)
                {
                    continue;
                }

                var quinella = Odds.Chance(track, BetKind.Quinella, a.Number, b.Number);
                var forward = Odds.Chance(track, BetKind.Exacta, a.Number, b.Number);
                var reverse = Odds.Chance(track, BetKind.Exacta, b.Number, a.Number);

                Assert.Equal(forward + reverse, quinella, 12);
            }
        }
    }

    /// <summary>
    /// A runner's chance of placing is at least its chance of winning, and of showing
    /// at least of placing -- a containment that a placings window built the wrong way
    /// round would invert.
    /// </summary>
    [Theory]
    [MemberData(nameof(Courses))]
    public void PlacingsContainEachOther(string id)
    {
        var track = Course(id);

        foreach (var runner in Field.Runners)
        {
            var win = Odds.Chance(track, BetKind.Win, runner.Number);
            var place = Odds.Chance(track, BetKind.Place, runner.Number);
            var show = Odds.Chance(track, BetKind.Show, runner.Number);

            Assert.True(win <= place, $"runner {runner.Number} wins more often than it places.");
            Assert.True(place <= show, $"runner {runner.Number} places more often than it shows.");
        }
    }

    /// <summary>
    /// A heavier runner is always the shorter price, at whichever course made it
    /// heavier. This is what ties the board back to the weights.
    /// </summary>
    [Theory]
    [MemberData(nameof(Courses))]
    public void AHeavierRunnerIsAlwaysTheShorterPrice(string id)
    {
        var track = Course(id);
        var byWeight = Field.Runners.OrderByDescending(track.WeightOf).ToList();

        for (var i = 1; i < byWeight.Count; i++)
        {
            var heavier = byWeight[i - 1];
            var lighter = byWeight[i];

            Assert.True(
                Odds.BoardPrice(track, BetKind.Win, heavier.Number)
                < Odds.BoardPrice(track, BetKind.Win, lighter.Number),
                $"{track.Name}: {heavier.Name} is the heavier and must be the shorter price.");
        }
    }

    /// <summary>
    /// **The same bet is a different price at a different course**, which is the whole
    /// reason the track is a parameter rather than a default.
    ///
    /// A regression here would mean the boards had silently collapsed into one, and
    /// every other test in this file would still pass.
    /// </summary>
    [Fact]
    public void TheSameBetIsPricedDifferentlyAtDifferentCourses()
    {
        var sprinter = Field.Runners.OrderByDescending(r => r.Speed - r.Stamina).First();

        var dash = Odds.BoardPrice(Tracks.Dash, BetKind.Win, sprinter.Number);
        var marathon = Odds.BoardPrice(Tracks.Marathon, BetKind.Win, sprinter.Number);

        Assert.True(
            marathon > dash * 2.0,
            $"{sprinter.Name} is {dash:F2} at the dash and {marathon:F2} at the marathon; "
            + "the boards have collapsed into one.");
    }
}
