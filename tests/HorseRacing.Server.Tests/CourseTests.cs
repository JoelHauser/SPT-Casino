using HorseRacing.Game;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.ItemEvent;

namespace HorseRacing.Server.Tests;

/// <summary>
/// What the server does about there being three courses.
///
/// <c>MoneyInvariantTests</c> checks the money path at one of them. These check the
/// thing that only exists because there is more than one: that a slip is priced and
/// settled at the course it named, and at no other.
/// </summary>
public class CourseTests
{
    private static readonly MongoId Session = new("6a9b474574813708e8fc3ce5");

    private const int Rich = 500_000_000;

    /// <summary>
    /// **A course that does not exist is refused, and nothing moves.**
    ///
    /// Not defaulted to the first one. A typo would otherwise run a race the player did
    /// not choose and pay it off a board they were never shown -- and because the
    /// payout would still be internally consistent, nothing downstream would notice.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("newmarket")]
    [InlineData("DASH ")]
    [InlineData("sprint")]
    public async Task ACourseThatDoesNotExistMovesNoMoney(string track)
    {
        var (service, bank, _, escrow) = Table();
        bank.Seed(Wallet.Roubles, Rich);

        var reply = await service.PlaceAsync(
            new PlaceRequest
            {
                Track = track,
                Wallet = nameof(Wallet.Roubles),
                Bets = [new SlipEntry { Kind = nameof(BetKind.Win), First = 1, Stake = 10_000 }],
            },
            Session,
            Output());

        Assert.False(reply.Ok);
        Assert.Equal(0, bank.Moved);
        Assert.Equal(0, bank.Debits);
        Assert.Empty(escrow.Recorded);
    }

    [Theory]
    [InlineData("dash")]
    [InlineData("mile")]
    [InlineData("marathon")]
    public async Task EveryRealCourseTakesASlipAndSaysWhichOneItRan(string track)
    {
        var (service, bank, _, _) = Table();
        bank.Seed(Wallet.Roubles, Rich);

        var reply = await service.PlaceAsync(
            new PlaceRequest
            {
                Track = track,
                Wallet = nameof(Wallet.Roubles),
                Bets = [new SlipEntry { Kind = nameof(BetKind.Win), First = 1, Stake = 10_000 }],
            },
            Session,
            Output());

        Assert.True(reply.Ok, reply.Error);
        Assert.Equal(track, reply.Race!.Track);
        Assert.Equal(Field.Count, reply.Race.Order.Count);
    }

    /// <summary>
    /// **A winning bet is paid the price its own course quoted, not another's.**
    ///
    /// The test that justifies threading the track through the draw and the settlement
    /// together. Runner 7 is a sprinter: short at the dash, long at the marathon. If
    /// the settlement ever priced off a fixed board, one of these two would be wrong by
    /// a factor of six and the totals would still add up.
    /// </summary>
    [Theory]
    [InlineData("dash")]
    [InlineData("marathon")]
    public async Task AWinnerIsPaidThePriceOfTheCourseItRanAt(string id)
    {
        var track = Tracks.ById(id)!;
        var (service, bank, _, _) = Table();
        bank.Seed(Wallet.Roubles, Rich);

        // Every runner to win, so exactly one of them must come in.
        var bets = Field.Runners
            .Select(r => new SlipEntry { Kind = nameof(BetKind.Win), First = r.Number, Stake = 10_000 })
            .ToList();

        var reply = await service.PlaceAsync(
            new PlaceRequest { Track = id, Wallet = nameof(Wallet.Roubles), Bets = bets },
            Session,
            Output());

        Assert.True(reply.Ok, reply.Error);

        var winner = reply.Race!.Order[0];
        var paid = reply.Race.Settlements.Single(s => s.First == winner && s.Won);
        var expected = (long)Math.Floor(10_000 * Odds.BoardPrice(track, BetKind.Win, winner));

        Assert.Equal(expected, paid.Returned);
        Assert.Equal(expected, reply.Race.Returned);
    }

    /// <summary>
    /// The ping carries all three courses, each with its own card and its own board.
    /// </summary>
    [Fact]
    public void ThePingDescribesEveryCourseAndItsWholeBoard()
    {
        var (service, bank, _, _) = Table();
        bank.Seed(Wallet.Roubles, Rich);

        var ping = service.Ping(Session, Output());

        Assert.Equal(Tracks.All.Count, ping.Courses.Count);

        foreach (var course in ping.Courses)
        {
            var track = Tracks.ById(course.Id);

            Assert.NotNull(track);
            Assert.Equal(Field.Count, course.Runners.Count);
            Assert.Equal(new RaceRules().MaxBets, course.Board.Count);
            Assert.Equal(track!.MaxSlip, course.MaxSlip);
            Assert.False(string.IsNullOrWhiteSpace(course.Shape));

            // The chances it quotes are that course's, not some other course's.
            foreach (var runner in course.Runners)
            {
                Assert.Equal(
                    Odds.Chance(track, BetKind.Win, runner.Number),
                    runner.Chance,
                    12);
            }
        }

        // And the three are genuinely different boards rather than three copies.
        var firstPrices = ping.Courses.Select(c => c.Board[0].Price).ToList();
        Assert.Equal(firstPrices.Count, firstPrices.Distinct().Count());
    }

    /// <summary>
    /// **Each course enforces its own ceiling, and waiving the maximum cannot lift it.**
    ///
    /// The dash carries the widest spread and therefore the longest price, so its
    /// ceiling is the lowest. A slip that is legal at the mile can be too big at the
    /// dash, and that is the point rather than an inconsistency.
    /// </summary>
    [Fact]
    public async Task TheDashCeilingIsLowerThanTheMileAndBothAreEnforced()
    {
        Assert.True(
            Tracks.Dash.MaxSlip < Tracks.Mile.MaxSlip,
            "the dash has the longest price on any board and so must have the lowest ceiling.");

        var (service, bank, _, _) = Table();
        bank.Seed(Wallet.Roubles, Rich);

        var overTheDash = Tracks.Dash.MaxSlip + 5_000;

        var atTheMile = await service.PlaceAsync(
            new PlaceRequest
            {
                Track = "mile",
                Wallet = nameof(Wallet.Roubles),
                Bets = [new SlipEntry { Kind = nameof(BetKind.Win), First = 1, Stake = overTheDash }],
            },
            Session,
            Output());

        Assert.True(atTheMile.Ok, atTheMile.Error);

        var atTheDash = await service.PlaceAsync(
            new PlaceRequest
            {
                Track = "dash",
                IgnoreMaximum = true,
                Wallet = nameof(Wallet.Roubles),
                Bets = [new SlipEntry { Kind = nameof(BetKind.Win), First = 1, Stake = overTheDash }],
            },
            Session,
            Output());

        Assert.False(atTheDash.Ok);
        Assert.Contains("THE DASH", atTheDash.Error);
    }

    /// <summary>
    /// A slip at each course's own ceiling cannot come back negative.
    ///
    /// The overflow check, run at every course rather than one, because each has a
    /// different longest price and a different ceiling and the two have to match up.
    /// </summary>
    [Theory]
    [InlineData("dash")]
    [InlineData("mile")]
    [InlineData("marathon")]
    public async Task TheBiggestPayableSlipAtEachCourseComesBackPositive(string id)
    {
        var track = Tracks.ById(id)!;
        var (service, bank, _, _) = Table();
        bank.Seed(Wallet.Roubles, Rich);

        var longest = Odds.All(track).OrderByDescending(p => p.Board).First();

        var reply = await service.PlaceAsync(
            new PlaceRequest
            {
                Track = id,
                Wallet = nameof(Wallet.Roubles),
                Bets =
                [
                    new SlipEntry
                    {
                        Kind = longest.Kind.ToString(),
                        First = longest.First,
                        Second = longest.Second,
                        Stake = track.MaxSlip,
                    },
                ],
            },
            Session,
            Output());

        Assert.True(reply.Ok, reply.Error);
        Assert.True(reply.Race!.Returned >= 0, $"{track.Name} returned {reply.Race.Returned}.");

        foreach (var settled in reply.Race.Settlements)
        {
            Assert.True(settled.Returned >= 0, "a settlement came back negative, which is an overflow.");
        }
    }

    private static (RaceService Service, FakeBank Bank, FakeProfiles Profiles, FakeEscrow Escrow) Table()
    {
        var bank = new FakeBank();
        var profiles = new FakeProfiles();
        var escrow = new FakeEscrow();

        var service = new RaceService(
            bank, profiles, escrow, new FakeRandom(20260919), new FakeStats(), new QuietLog());

        return (service, bank, profiles, escrow);
    }

    private static ItemEventRouterResponse Output() => new();
}
