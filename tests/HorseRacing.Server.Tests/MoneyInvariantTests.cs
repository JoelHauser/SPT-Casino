using HorseRacing.Game;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.ItemEvent;

namespace HorseRacing.Server.Tests;

/// <summary>
/// What this table is allowed to do to a player's money.
///
/// **Written before the settlement it checks**, which is the instruction this repo has
/// carried since roulette. An end-of-run balance check misses errors that cancel, and a
/// settlement written first gets tests shaped around what it already does rather than
/// around what it owes.
///
/// ## The model these pin
///
/// One slip is one transaction. That is the whole difference between this table and the
/// other four, and it is where every invariant below comes from: a slip carries up to
/// 108 bets, and all of them are taken with **one** debit and paid with **one** credit.
/// The alternative -- taking each bet as it is read -- has a failure mode nothing here
/// could recover from, which is a slip refused on its tenth bet with nine already paid
/// for. So the whole slip is validated before any of it is taken, and there is a test
/// below that does nothing but hold that line.
/// </summary>
public class MoneyInvariantTests
{
    private static readonly MongoId Session = new("6a9b474574813708e8fc3ce5");

    /// <summary>
    /// The course these run at.
    ///
    /// One of them, because these are about the money path rather than about pricing:
    /// the debit, the escrow and the ordering are identical wherever the race is run.
    /// <c>CourseTests</c> is where the three are checked against each other.
    /// </summary>
    private const string Course = "mile";

    /// <summary>The slip ceiling in force at <see cref="Course"/>.</summary>
    private static int Ceiling => Tracks.ById(Course)!.MaxSlip;

    private const int Rich = 500_000_000;

    /// <summary>
    /// The invariant, and the only one that matters: **the wallet moved by exactly what
    /// the table said it did.**
    ///
    /// Measured from every individual movement rather than from the closing balance,
    /// because a debit one short and a credit one over net to a balance that looks
    /// right.
    /// </summary>
    [Fact]
    public async Task TheWalletMovesByWhatWasPaidLessWhatWasStaked()
    {
        var (service, bank, _, _) = Table();
        bank.Seed(Wallet.Roubles, Rich);

        var expected = 0L;

        for (var i = 0; i < 300; i++)
        {
            var reply = await service.PlaceAsync(Slip(), Session, Output());

            Assert.True(reply.Ok, reply.Error);
            expected += reply.Race!.Returned - reply.Race.Staked;
        }

        Assert.Equal(expected, bank.Moved);
        Assert.Equal(Rich + expected, bank.GetBalance(Session, Wallet.Roubles));
    }

    /// <summary>
    /// **One slip, one debit and at most one credit, however many bets are on it.**
    ///
    /// The invariant unique to this table. A slip of forty bets that moved money forty
    /// times would still balance at the end, so a net check cannot see this; only
    /// counting the movements can.
    /// </summary>
    [Fact]
    public async Task ASlipOfManyBetsIsStillOneDebitAndAtMostOneCredit()
    {
        var (service, bank, _, _) = Table();
        bank.Seed(Wallet.Roubles, Rich);

        for (var i = 0; i < 100; i++)
        {
            var before = (bank.Debits, bank.Credits);
            var reply = await service.PlaceAsync(BigSlip(), Session, Output());

            Assert.True(reply.Ok, reply.Error);
            Assert.True(reply.Race!.Settlements.Count > 10, "this test is pointless on a small slip.");

            Assert.Equal(before.Debits + 1, bank.Debits);
            Assert.InRange(bank.Credits, before.Credits, before.Credits + 1);

            // And the one credit, when there is one, is the whole slip's return rather
            // than the best bet on it.
            Assert.Equal(
                reply.Race.Settlements.Sum(s => s.Returned),
                reply.Race.Returned);
        }
    }

    /// <summary>
    /// **A refused slip moves nothing whatsoever** -- including a slip that is refused
    /// on its last bet after the first thirty were perfectly good.
    ///
    /// This is the test the whole validate-then-take arrangement exists for. A service
    /// that took each bet as it read it would pass every other test in this file and
    /// fail this one, having charged for thirty bets on a race it never ran.
    /// </summary>
    [Fact]
    public async Task ASlipRefusedOnItsLastBetTakesNoMoneyForTheOthers()
    {
        var (service, bank, _, escrow) = Table();
        bank.Seed(Wallet.Roubles, Rich);

        var bets = new List<SlipEntry>();

        for (var runner = 1; runner <= Field.Count; runner++)
        {
            bets.Add(new SlipEntry { Kind = nameof(BetKind.Win), First = runner, Stake = 10_000 });
        }

        // Runner 9 does not exist. Everything above it is legal and generous.
        bets.Add(new SlipEntry { Kind = nameof(BetKind.Win), First = 9, Stake = 10_000 });

        var reply = await service.PlaceAsync(
            new PlaceRequest { Track = Course, Wallet = nameof(Wallet.Roubles), Bets = bets },
            Session,
            Output());

        Assert.False(reply.Ok);
        Assert.Equal(0, bank.Debits);
        Assert.Equal(0, bank.Credits);
        Assert.Equal(0, bank.Moved);
        Assert.Equal(Rich, bank.GetBalance(Session, Wallet.Roubles));

        // And nothing was written down either, so nothing will be "refunded" later.
        Assert.Empty(escrow.Recorded);
        Assert.Null(escrow.Get(Session));
    }

    [Theory]
    [InlineData("Win", 0, 0, 10_000)]
    [InlineData("Win", 9, 0, 10_000)]
    [InlineData("Exacta", 3, 3, 10_000)]
    [InlineData("Exacta", 3, 0, 10_000)]
    [InlineData("Quinella", 3, 99, 10_000)]
    [InlineData("Superfecta", 1, 0, 10_000)]
    [InlineData("Win", 1, 0, 0)]
    [InlineData("Win", 1, 0, -50_000)]
    [InlineData("Win", 1, 0, 9_999)]
    public async Task ABetTheTableDoesNotTakeMovesNoMoney(string kind, int first, int second, long stake)
    {
        var (service, bank, _, escrow) = Table();
        bank.Seed(Wallet.Roubles, Rich);

        var reply = await service.PlaceAsync(
            new PlaceRequest
            {
                Track = Course,
                Wallet = nameof(Wallet.Roubles),
                Bets = [new SlipEntry { Kind = kind, First = first, Second = second, Stake = stake }],
            },
            Session,
            Output());

        Assert.False(reply.Ok);
        Assert.Equal(0, bank.Moved);
        Assert.Equal(0, bank.Debits);
        Assert.Empty(escrow.Recorded);
    }

    /// <summary>
    /// An empty slip is refused **as an empty slip**, and the message says so.
    ///
    /// The assertion on the wording is not fussiness. Without the guard this is still
    /// refused, by the slip total falling under the minimum -- so the only thing that
    /// distinguishes a table that checks for an empty slip from one that merely
    /// stumbles into refusing it is what the player is told. "There is nothing on the
    /// slip" is a sentence somebody can act on; "a slip comes to at least 10,000"
    /// sends them looking for money they do not need.
    /// </summary>
    [Fact]
    public async Task AnEmptySlipIsRefusedRatherThanRunAsAFreeRace()
    {
        var (service, bank, _, _) = Table();
        bank.Seed(Wallet.Roubles, Rich);

        var reply = await service.PlaceAsync(
            new PlaceRequest { Track = Course, Wallet = nameof(Wallet.Roubles), Bets = [] }, Session, Output());

        Assert.False(reply.Ok);
        Assert.Contains("nothing on the slip", reply.Error);
        Assert.Equal(0, bank.Moved);
    }

    /// <summary>
    /// **Several bets each under the minimum are refused even when they add up to more
    /// than it.**
    ///
    /// The per-bet minimum and the slip total are two different rules, and on a
    /// one-bet slip they are indistinguishable -- which is exactly how a table ends up
    /// enforcing only the total and nobody noticing. Four bets of 5,000 come to 20,000
    /// and would clear every check on the slip as a whole.
    ///
    /// Found by mutation: deleting the per-bet check left every other test in this file
    /// green.
    /// </summary>
    [Fact]
    public async Task BetsUnderTheMinimumAreRefusedEvenWhenTheSlipTotalIsFine()
    {
        var (service, bank, _, escrow) = Table();
        bank.Seed(Wallet.Roubles, Rich);

        var reply = await service.PlaceAsync(
            new PlaceRequest
            {
                Track = Course,
                Wallet = nameof(Wallet.Roubles),
                Bets =
                [
                    new SlipEntry { Kind = nameof(BetKind.Win), First = 1, Stake = 5_000 },
                    new SlipEntry { Kind = nameof(BetKind.Win), First = 2, Stake = 5_000 },
                    new SlipEntry { Kind = nameof(BetKind.Win), First = 3, Stake = 5_000 },
                    new SlipEntry { Kind = nameof(BetKind.Win), First = 4, Stake = 5_000 },
                ],
            },
            Session,
            Output());

        Assert.False(reply.Ok);
        Assert.Equal(0, bank.Moved);
        Assert.Equal(0, bank.Debits);
        Assert.Empty(escrow.Recorded);
    }

    [Fact]
    public async Task AnUnknownCurrencyIsRefusedRatherThanDefaultingToRoubles()
    {
        var (service, bank, _, _) = Table();
        bank.Seed(Wallet.Roubles, Rich);

        var reply = await service.PlaceAsync(
            new PlaceRequest
            {
                Track = Course,
                Wallet = "Doubloons",
                Bets = [new SlipEntry { Kind = nameof(BetKind.Win), First = 1, Stake = 10_000 }],
            },
            Session,
            Output());

        Assert.False(reply.Ok);
        Assert.Equal(0, bank.Moved);
        Assert.Equal(Rich, bank.GetBalance(Session, Wallet.Roubles));
    }

    /// <summary>
    /// A slip over the ceiling is refused, and waiving the maximum does not lift it
    /// past the arithmetic bound.
    ///
    /// The second half is the one that matters and is unique to this table: elsewhere
    /// the maximum is caution and a player may waive it, but here it is the width of an
    /// int. A waiver that let the total through would not hand anybody a bigger win.
    /// </summary>
    [Fact]
    public async Task TheSlipCeilingHoldsEvenWhenTheMaximumIsWaived()
    {
        var (service, bank, _, _) = Table();
        bank.Seed(Wallet.Roubles, Rich);

        var overTheHardCeiling = new PlaceRequest
        {
            Track = Course,
            Wallet = nameof(Wallet.Roubles),
            IgnoreMaximum = true,
            Bets =
            [
                new SlipEntry { Kind = nameof(BetKind.Win), First = 1, Stake = Ceiling },
                new SlipEntry { Kind = nameof(BetKind.Win), First = 2, Stake = 10_000 },
            ],
        };

        var reply = await service.PlaceAsync(overTheHardCeiling, Session, Output());

        Assert.False(reply.Ok);
        Assert.Equal(0, bank.Moved);
    }

    /// <summary>
    /// **A slip at the ceiling cannot pay out a negative number.**
    ///
    /// The overflow test, driven through the real service rather than the engine. The
    /// bet is on the longest price on the board and the stake is exactly at the
    /// ceiling. Whether it comes in is up to the draw and does not matter: what is
    /// being checked is that *nothing* on the reply can come back negative, which is
    /// what an overflow would look like from here.
    /// </summary>
    [Fact]
    public async Task TheBiggestPayableSlipComesBackPositive()
    {
        var (service, bank, _, _) = Table();
        bank.Seed(Wallet.Roubles, Rich);

        var longest = Odds.All(Tracks.ById(Course)!).OrderByDescending(p => p.Board).First();

        var reply = await service.PlaceAsync(
            new PlaceRequest
            {
                Track = Course,
                Wallet = nameof(Wallet.Roubles),
                Bets =
                [
                    new SlipEntry
                    {
                        Kind = longest.Kind.ToString(),
                        First = longest.First,
                        Second = longest.Second,
                        Stake = Ceiling,
                    },
                ],
            },
            Session,
            Output());

        Assert.True(reply.Ok, reply.Error);
        Assert.True(reply.Race!.Returned >= 0, $"the slip returned {reply.Race.Returned}.");
        Assert.True(reply.Race.Staked > 0);

        // Whether it won is up to the draw; what must never happen is a win that
        // reports as a loss because it wrapped.
        foreach (var settled in reply.Race.Settlements)
        {
            Assert.True(settled.Returned >= 0, "a settlement came back negative, which is an overflow.");
            Assert.Equal(settled.Won, settled.Returned > 0);
        }
    }

    /// <summary>
    /// A player who cannot cover the slip is refused, nothing moves, and the escrow is
    /// not left holding a record of money that never left.
    /// </summary>
    [Fact]
    public async Task ASlipThePlayerCannotCoverLeavesNoEscrowBehind()
    {
        var (service, bank, _, escrow) = Table();
        bank.Seed(Wallet.Roubles, 15_000);

        var reply = await service.PlaceAsync(
            new PlaceRequest
            {
                Track = Course,
                Wallet = nameof(Wallet.Roubles),
                Bets =
                [
                    new SlipEntry { Kind = nameof(BetKind.Win), First = 1, Stake = 10_000 },
                    new SlipEntry { Kind = nameof(BetKind.Win), First = 2, Stake = 10_000 },
                ],
            },
            Session,
            Output());

        Assert.False(reply.Ok);
        Assert.Equal(0, bank.Moved);
        Assert.Equal(15_000, bank.GetBalance(Session, Wallet.Roubles));

        // Recorded before the debit was attempted, then released when it failed.
        Assert.Single(escrow.Recorded);
        Assert.Null(escrow.Get(Session));
    }

    /// <summary>
    /// **The stake is written down before it is taken, and released only after the
    /// payout.** The ordering is the entire point of the escrow.
    /// </summary>
    [Fact]
    public async Task TheStakeIsRecordedBeforeItMovesAndReleasedAfterThePayout()
    {
        var (service, bank, _, escrow) = Table();
        bank.Seed(Wallet.Roubles, Rich);

        var reply = await service.PlaceAsync(Slip(), Session, Output());

        Assert.True(reply.Ok, reply.Error);
        Assert.Equal(reply.Race!.Staked, escrow.Recorded.Single());
        Assert.Equal(1, escrow.Releases);
        Assert.Null(escrow.Get(Session));
    }

    /// <summary>
    /// A stake stranded by a server that died mid-race comes back in full, **once**.
    ///
    /// Refunding twice is worse than not refunding at all, because nobody reports it.
    /// </summary>
    [Fact]
    public void AStrandedStakeIsGivenBackExactlyOnce()
    {
        var (service, bank, _, escrow) = Table();
        bank.Seed(Wallet.Roubles, Rich);
        escrow.Strand(Session, Wallet.Roubles, 250_000);

        var first = service.Ping(Session, Output());

        Assert.NotNull(first.Note);
        Assert.Equal(Rich + 250_000, bank.GetBalance(Session, Wallet.Roubles));

        var second = service.Ping(Session, Output());

        Assert.Null(second.Note);
        Assert.Equal(Rich + 250_000, bank.GetBalance(Session, Wallet.Roubles));
    }

    /// <summary>
    /// A stranded stake is given back in the currency it was taken in, not in roubles.
    /// </summary>
    [Fact]
    public void AStrandedStakeComesBackInItsOwnCurrency()
    {
        var (service, bank, _, escrow) = Table();
        bank.Seed(Wallet.Roubles, Rich);
        bank.Seed(Wallet.Dollars, 1_000);
        escrow.Strand(Session, Wallet.Dollars, 400);

        service.Ping(Session, Output());

        Assert.Equal(1_400, bank.GetBalance(Session, Wallet.Dollars));
        Assert.Equal(Rich, bank.GetBalance(Session, Wallet.Roubles));
    }

    /// <summary>Money that is not flushed to disk did not move.</summary>
    [Fact]
    public async Task EverySettledRaceIsSavedToDisk()
    {
        var (service, bank, profiles, _) = Table();
        bank.Seed(Wallet.Roubles, Rich);

        for (var i = 1; i <= 25; i++)
        {
            await service.PlaceAsync(Slip(), Session, Output());
            Assert.Equal(i, profiles.Saves);
        }
    }

    /// <summary>A session with no profile is refused before anything is looked at.</summary>
    [Fact]
    public async Task NoProfileMeansNoRace()
    {
        var (service, bank, profiles, escrow) = Table();
        bank.Seed(Wallet.Roubles, Rich);
        profiles.Exists = false;

        var reply = await service.PlaceAsync(Slip(), Session, Output());

        Assert.False(reply.Ok);
        Assert.Equal(0, bank.Moved);
        Assert.Empty(escrow.Recorded);
    }

    /// <summary>
    /// Over many races the house takes roughly its stated cut and certainly not the
    /// opposite.
    ///
    /// Deliberately loose: this is not a test of the arithmetic -- <c>OddsTests</c>
    /// does that exactly -- it is a test that the *service* wired the arithmetic up the
    /// right way round. A settlement that paid winners and losers alike, or that
    /// credited the stake back on top of the return, would show up here as a house edge
    /// with the wrong sign, which is the one thing a loose bound still catches.
    /// </summary>
    [Fact]
    public async Task OverManyRacesTheHouseIsAheadAndTheEdgeIsAboutWhatItClaims()
    {
        var (service, bank, _, _) = Table();
        bank.Seed(Wallet.Roubles, Rich);

        var staked = 0L;
        var returned = 0L;

        for (var i = 0; i < 4_000; i++)
        {
            var reply = await service.PlaceAsync(Slip(), Session, Output());

            Assert.True(reply.Ok, reply.Error);
            staked += reply.Race!.Staked;
            returned += reply.Race.Returned;
        }

        var edge = 1.0 - ((double)returned / staked);

        Assert.InRange(edge, 0.0, 0.20);
        Assert.Equal(Rich + (returned - staked), bank.GetBalance(Session, Wallet.Roubles));
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

    /// <summary>A small, ordinary slip: one win bet and one each way.</summary>
    private static PlaceRequest Slip() => new()
    {
        Track = Course,
        Wallet = nameof(Wallet.Roubles),
        Bets =
        [
            new SlipEntry { Kind = nameof(BetKind.Win), First = 1, Stake = 10_000 },
            new SlipEntry { Kind = nameof(BetKind.Place), First = 4, Stake = 10_000 },
            new SlipEntry { Kind = nameof(BetKind.Exacta), First = 2, Second = 3, Stake = 10_000 },
        ],
    };

    /// <summary>Every win, place and show on the card -- 24 bets on one slip.</summary>
    private static PlaceRequest BigSlip()
    {
        var bets = new List<SlipEntry>();

        foreach (var kind in new[] { BetKind.Win, BetKind.Place, BetKind.Show })
        {
            for (var runner = 1; runner <= Field.Count; runner++)
            {
                bets.Add(new SlipEntry { Kind = kind.ToString(), First = runner, Stake = 10_000 });
            }
        }

        return new PlaceRequest { Track = Course, Wallet = nameof(Wallet.Roubles), Bets = bets };
    }

    /// <summary>
    /// A real one comes from `EventOutputHolder.GetOutput`. The fake bank never reads
    /// it, so a bare instance is honest here in a way it would not be in the server.
    /// </summary>
    private static ItemEventRouterResponse Output() => new();
}
