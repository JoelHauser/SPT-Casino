namespace HorseRacing.Game.Tests;

/// <summary>
/// The three courses, and the constraints the ratings were tuned against.
///
/// **These are what stop somebody editing a rating and quietly breaking a card.** The
/// ratings in <see cref="Field"/> and the thresholds in <see cref="Tracks"/> were
/// chosen by hand and then checked against everything below; a change to either that
/// violates one of these makes a board that is wrong in a way nobody would see by
/// looking at it.
/// </summary>
public class TrackTests
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

    [Theory]
    [MemberData(nameof(Courses))]
    public void NoTwoRunnersCarryTheSameWeight(string id)
    {
        var track = Course(id);
        var weights = Field.Runners.Select(track.WeightOf).ToList();

        // Two identical weights are two identical prices, which is a choice the player
        // cannot make on any grounds at all -- and it looks like a bug in the card.
        Assert.Equal(weights.Count, weights.Distinct().Count());
    }

    [Theory]
    [MemberData(nameof(Courses))]
    public void NoRunnerFallsToTheWeightFloor(string id)
    {
        var track = Course(id);

        // The floor exists so a badly-treated horse cannot go negative, which would
        // make the field's total smaller by entering it and corrupt every other price.
        // No shipped track should actually reach it: a card where two horses are both
        // clamped to 1 has invented a tie that the ratings did not ask for.
        foreach (var runner in Field.Runners)
        {
            Assert.True(
                track.WeightOf(runner) > Track.MinWeight,
                $"{runner.Name} is clamped to the floor at {track.Name}; the threshold is too high.");
        }
    }

    [Theory]
    [MemberData(nameof(Courses))]
    public void EveryRunnerHasSomeChanceAndTheChancesSumToOne(string id)
    {
        var track = Course(id);
        var total = 0.0;

        foreach (var runner in Field.Runners)
        {
            var chance = Odds.Chance(track, BetKind.Win, runner.Number);

            Assert.True(chance > 0.0, $"{runner.Name} cannot win at {track.Name}.");
            total += chance;
        }

        Assert.Equal(1.0, total, 12);
    }

    /// <summary>
    /// **The point of having more than one track.** If every course had the same
    /// favourite they would be one race with three backdrops.
    /// </summary>
    [Fact]
    public void TheCoursesDoNotAllHaveTheSameFavourite()
    {
        var favourites = Tracks.All
            .Select(t => Field.Runners.OrderByDescending(t.WeightOf).First().Number)
            .ToList();

        Assert.Equal(favourites.Count, favourites.Distinct().Count());
    }

    /// <summary>
    /// A sprinter is better over five furlongs than over two miles, and a stayer is the
    /// other way round. Stated as a property of the model rather than of the numbers,
    /// so it survives a retune.
    /// </summary>
    [Fact]
    public void SpeedIsWorthMoreOverTheSprintAndStaminaOverTheMarathon()
    {
        var sprinter = Field.Runners.OrderByDescending(r => r.Speed - r.Stamina).First();
        var stayer = Field.Runners.OrderByDescending(r => r.Stamina - r.Speed).First();

        var sprinterDash = Odds.Chance(Tracks.Dash, BetKind.Win, sprinter.Number);
        var sprinterMarathon = Odds.Chance(Tracks.Marathon, BetKind.Win, sprinter.Number);

        var stayerDash = Odds.Chance(Tracks.Dash, BetKind.Win, stayer.Number);
        var stayerMarathon = Odds.Chance(Tracks.Marathon, BetKind.Win, stayer.Number);

        Assert.True(
            sprinterDash > sprinterMarathon,
            $"{sprinter.Name} is the fastest horse on the card and should prefer the dash.");

        Assert.True(
            stayerMarathon > stayerDash,
            $"{stayer.Name} is the toughest horse on the card and should prefer the marathon.");
    }

    /// <summary>
    /// **The house takes the same cut wherever the race is run.**
    ///
    /// A course quietly carrying a bigger edge would punish exactly the players who
    /// compare the boards most carefully, and nothing on screen would say so.
    /// </summary>
    [Theory]
    [MemberData(nameof(Courses))]
    public void NoSpotOnAnyBoardIsPricedBelowTheTakeoutOrFarAboveIt(string id)
    {
        var track = Course(id);

        foreach (var price in Odds.All(track))
        {
            var realised = Odds.Realised(track, price.Kind, price.First, price.Second);

            Assert.True(
                realised >= Odds.Takeout - 1e-9,
                $"{track.Name}: {price.Kind} {price.First}/{price.Second} carries {realised:P4}, under the takeout.");

            Assert.True(
                realised <= Odds.Takeout + 0.01,
                $"{track.Name}: {price.Kind} {price.First}/{price.Second} carries {realised:P4}, a point over.");
        }
    }

    [Theory]
    [MemberData(nameof(Courses))]
    public void EveryBoardOffersTheSameHundredAndEightSpots(string id)
    {
        var all = Odds.All(Course(id));

        Assert.Equal(new RaceRules().MaxBets, all.Count);
        Assert.Equal(all.Count, all.Select(p => (p.Kind, p.First, p.Second)).Distinct().Count());
    }

    /// <summary>
    /// **The arithmetic behind each course's own <see cref="Track.MaxSlip"/>.**
    ///
    /// The ceiling is per track because the longest price is: the dash has the widest
    /// spread and so the longest shot, and 2,000,000 at its price would not fit in the
    /// int the engine counts chips in. Asserted against the real board rather than
    /// against the prose that explains it, because editing a rating moves the number
    /// and does not move the prose.
    /// </summary>
    [Theory]
    [MemberData(nameof(Courses))]
    public void TheSlipCeilingAtItsOwnLongestPriceStillFitsInAnInt(string id)
    {
        var track = Course(id);
        var longest = Odds.LongestPrice(track);

        Assert.True(longest > 1.0, "a board whose longest price is a refund is not a board.");

        var biggest = (long)Math.Floor(track.MaxSlip * longest);

        Assert.True(
            biggest <= int.MaxValue,
            $"{track.Name}: {track.MaxSlip:N0} at {longest:F2} is {biggest:N0}, past int.MaxValue.");

        // And the ceiling is not absurdly conservative either -- doubling it should be
        // what breaks, so MaxSlip is demonstrably near the real limit rather than a
        // round number somebody liked.
        Assert.True(
            (long)Math.Floor(track.MaxSlip * 2.0 * longest) > int.MaxValue,
            $"{track.Name}: MaxSlip is far below what the arithmetic allows.");
    }

    [Fact]
    public void ACourseThatDoesNotExistIsRefusedRatherThanDefaulted()
    {
        // Null rather than a default. Quietly running a race at the dash because the id
        // was misspelled would take money for a race the player did not choose, priced
        // off a board they were never shown.
        Assert.Null(Tracks.ById("newmarket"));
        Assert.Null(Tracks.ById(""));
        Assert.Null(Tracks.ById(null));

        Assert.Same(Tracks.Dash, Tracks.ById("dash"));
        Assert.Same(Tracks.Mile, Tracks.ById("MILE"));
    }

    [Fact]
    public void EveryCourseHasADistinctIdAndAShape()
    {
        Assert.Equal(Tracks.All.Count, Tracks.All.Select(t => t.Id).Distinct().Count());
        Assert.Equal(Tracks.All.Count, Tracks.All.Select(t => t.Name).Distinct().Count());

        foreach (var track in Tracks.All)
        {
            Assert.True(track.Laps >= 1, $"{track.Name} runs {track.Laps} laps.");
            Assert.True(track.RunSeconds > 0f);

            // A straight is run once by definition -- there is nothing to go round.
            if (track.Shape == TrackShape.Straight)
            {
                Assert.Equal(1, track.Laps);
            }
        }
    }
}
