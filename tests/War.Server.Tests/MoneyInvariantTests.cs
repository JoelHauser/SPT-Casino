namespace War.Server.Tests;

/// <summary>
/// What the war table is allowed to do to a player's money.
///
/// **Written before the settlement it checks**, as this repo asks. Each test says what
/// the rules owe the player and leaves the service to get there.
///
/// ## The model these pin
///
/// The slot machine's order -- check, record, debit, settle, credit, release, save --
/// with one thing the slot machine never has: a round that stops half way. A tie
/// settles the tie bet and leaves the ante on the table until the player answers, so
/// the escrow has to say what is held across two requests, and going to war takes
/// more money in the middle of a round.
/// </summary>
public class MoneyInvariantTests
{
    private const int Rich = 500_000_000;
    private const int Ante = 10_000;

    private static readonly SPTarkov.Server.Core.Models.Common.MongoId Session = Rig.Session;

    // ---------------------------------------------------------------- the invariant

    /// <summary>
    /// The wallet moved by exactly what the table said, summed movement by movement
    /// rather than read off the closing balance, so errors that cancel cannot hide.
    /// A thousand real rounds, ties fought or surrendered alternately, a tie bet on
    /// every third.
    /// </summary>
    [Fact]
    public async Task TheWalletMovesByExactlyWhatTheRoundsSay()
    {
        var rig = new Rig(seed: 42);
        rig.Bank.Seed(Wallet.Roubles, Rich);

        var expected = 0L;
        var ties = 0;

        for (var i = 0; i < 1_000; i++)
        {
            var reply = await rig.Deal(Ante, i % 3 == 0 ? 2_000 : 0);
            Assert.True(reply.Ok, reply.Error);

            if (reply.Round!.Phase == "AwaitingDecision")
            {
                reply = await rig.Decide(ties++ % 2 == 0 ? "War" : "Surrender");
                Assert.True(reply.Ok, reply.Error);
            }

            Assert.Equal("Settled", reply.Round!.Phase);
            expected += reply.Round.Profit;
        }

        Assert.True(ties > 30, $"only {ties} ties in 1,000 rounds -- the test is not reaching the war path");
        Assert.Equal(expected, rig.Bank.Moved);
        Assert.Equal(Rich + expected, rig.Bank.GetBalance(Session, Wallet.Roubles));
        Assert.Null(rig.Escrow.Get(Session));
    }

    /// <summary>
    /// Every debit is covered by the escrow **before** it lands: at the instant money
    /// leaves the stash, the record on disk already says the table holds at least that
    /// much. The other order leaves a window where the stake is gone and nothing says so.
    /// </summary>
    [Fact]
    public async Task EveryDebitIsInEscrowBeforeItIsTaken()
    {
        var rig = new Rig(seed: 7);
        rig.Bank.Seed(Wallet.Roubles, Rich);
        var checks = 0;

        rig.Bank.BeforeDebit = amount =>
        {
            Assert.True(rig.Escrow.Held(Session) >= amount);
            checks++;
        };

        for (var i = 0; i < 300; i++)
        {
            var reply = await rig.Deal(Ante, 1_000);
            if (reply.Round!.Phase == "AwaitingDecision")
            {
                await rig.Decide("War");
            }
        }

        Assert.True(checks > 300);
    }

    // ------------------------------------------------------------- one of each round

    [Fact]
    public async Task AWinTakesTheBetOnceAndPaysItBackDoubled()
    {
        var rig = new Rig().Stack("KH", "9S");
        rig.Bank.Seed(Wallet.Roubles, Rich);

        var reply = await rig.Deal(Ante);

        Assert.Equal("Win", reply.Round!.Outcome);
        Assert.Equal([Ante], rig.Bank.Debited);
        Assert.Equal([2 * Ante], rig.Bank.Credited);
        Assert.Null(rig.Escrow.Get(Session));
        Assert.Equal(1, rig.Profiles.Saves);
    }

    [Fact]
    public async Task ALossTakesTheBetAndPaysNothing()
    {
        var rig = new Rig().Stack("4C", "JD");
        rig.Bank.Seed(Wallet.Roubles, Rich);

        var reply = await rig.Deal(Ante);

        Assert.Equal("Lose", reply.Round!.Outcome);
        Assert.Equal([Ante], rig.Bank.Debited);
        Assert.Empty(rig.Bank.Credited);
        Assert.Null(rig.Escrow.Get(Session));
    }

    /// <summary>
    /// A tie with a tie bet: both bets taken in one debit, the tie bet paid eleven times
    /// over straight away, and the ante left on the table in escrow while the player
    /// decides.
    /// </summary>
    [Fact]
    public async Task ATiePaysTheTieBetAndHoldsTheAnte()
    {
        var rig = new Rig().Stack("7C", "7H", "2S", "3S", "4S", "QD", "5C");
        rig.Bank.Seed(Wallet.Roubles, Rich);

        var reply = await rig.Deal(Ante, 2_000);

        Assert.Equal("AwaitingDecision", reply.Round!.Phase);
        Assert.Equal([Ante + 2_000], rig.Bank.Debited);
        Assert.Equal([2_000 * 11], rig.Bank.Credited);
        Assert.Equal(Ante, rig.Escrow.Held(Session));
        Assert.Equal(1, rig.Profiles.Saves);
    }

    /// <summary>Two antes down, three back, and nothing left owing.</summary>
    [Fact]
    public async Task WinningAWarTakesTheRaiseAndPaysThreeAntes()
    {
        var rig = new Rig().Stack("7C", "7H", "2S", "3S", "4S", "QD", "5C");
        rig.Bank.Seed(Wallet.Roubles, Rich);
        await rig.Deal(Ante);

        var reply = await rig.Decide("War");

        Assert.True(reply.Ok, reply.Error);
        Assert.Equal("WarWin", reply.Round!.Outcome);
        Assert.Equal([Ante, Ante], rig.Bank.Debited);
        Assert.Equal([3 * Ante], rig.Bank.Credited);
        Assert.Equal(Ante, rig.Bank.Moved);
        Assert.Equal([Ante, Ante, 2 * Ante], rig.Escrow.Recorded);
        Assert.Null(rig.Escrow.Get(Session));
        Assert.Equal(2, rig.Profiles.Saves);
    }

    [Fact]
    public async Task LosingAWarTakesBothAntes()
    {
        var rig = new Rig().Stack("7C", "7H", "2S", "3S", "4S", "5C", "QD");
        rig.Bank.Seed(Wallet.Roubles, Rich);
        await rig.Deal(Ante);

        var reply = await rig.Decide("War");

        Assert.Equal("WarLose", reply.Round!.Outcome);
        Assert.Equal(-2 * Ante, rig.Bank.Moved);
        Assert.Empty(rig.Bank.Credited);
        Assert.Null(rig.Escrow.Get(Session));
    }

    /// <summary>Half back, nothing more taken, and the odd unit to the player.</summary>
    [Fact]
    public async Task SurrenderingPaysHalfTheAnteBack()
    {
        var rig = new Rig().Stack("7C", "7H");
        rig.Bank.Seed(Wallet.Roubles, Rich);
        await rig.Deal(10_001);

        var reply = await rig.Decide("Surrender");

        Assert.Equal("Surrender", reply.Round!.Outcome);
        Assert.Equal([10_001], rig.Bank.Debited);
        Assert.Equal([5_001], rig.Bank.Credited);
        Assert.Null(rig.Escrow.Get(Session));
    }

    // ------------------------------------------------------------------ refusals

    /// <summary>
    /// A war the player cannot pay for is refused, takes nothing, and leaves the tie
    /// exactly where it was -- surrender is still on the table, and the escrow is back
    /// to holding the ante alone.
    /// </summary>
    [Fact]
    public async Task AWarThatCannotBeAffordedLeavesTheTieStanding()
    {
        var rig = new Rig().Stack("7C", "7H");
        rig.Bank.Seed(Wallet.Roubles, Ante + 5_000);
        await rig.Deal(Ante);

        var refused = await rig.Decide("War");

        Assert.False(refused.Ok);
        Assert.Equal("AwaitingDecision", refused.Round!.Phase);
        Assert.Equal([Ante], rig.Bank.Debited);
        Assert.Equal(Ante, rig.Escrow.Held(Session));

        var surrendered = await rig.Decide("Surrender");

        Assert.True(surrendered.Ok, surrendered.Error);
        Assert.Equal(-Ante / 2, rig.Bank.Moved);
    }

    [Fact]
    public async Task ANewHandIsRefusedWhileATieIsWaiting()
    {
        var rig = new Rig().Stack("7C", "7H", "KH", "9S");
        rig.Bank.Seed(Wallet.Roubles, Rich);
        await rig.Deal(Ante);

        var reply = await rig.Deal(Ante);

        Assert.False(reply.Ok);
        Assert.Equal("AwaitingDecision", reply.Round!.Phase);
        Assert.Single(rig.Bank.Debited);
        Assert.Equal(Ante, rig.Escrow.Held(Session));
    }

    [Theory]
    [InlineData("War")]
    [InlineData("Surrender")]
    public async Task ThereIsNothingToDecideWithoutATie(string choice)
    {
        var rig = new Rig().Stack("KH", "9S");
        rig.Bank.Seed(Wallet.Roubles, Rich);
        await rig.Deal(Ante);
        var before = rig.Bank.Movements.Count;

        var reply = await rig.Decide(choice);

        Assert.False(reply.Ok);
        Assert.Equal(before, rig.Bank.Movements.Count);
    }

    [Fact]
    public async Task AnAnswerThatIsNotWarOrSurrenderIsRefused()
    {
        var rig = new Rig().Stack("7C", "7H");
        rig.Bank.Seed(Wallet.Roubles, Rich);
        await rig.Deal(Ante);

        var reply = await rig.Decide("Run away");

        Assert.False(reply.Ok);
        Assert.Equal(Ante, rig.Escrow.Held(Session));
        Assert.Single(rig.Bank.Movements);
    }

    /// <summary>
    /// The ante and the tie bet are paid together or not at all. A balance that covers
    /// the ante but not the pair takes nothing and deals nothing.
    /// </summary>
    [Fact]
    public async Task ABetThatCannotBeAffordedTakesNothing()
    {
        var rig = new Rig();
        rig.Bank.Seed(Wallet.Roubles, Ante + 500);

        var reply = await rig.Deal(Ante, 1_000);

        Assert.False(reply.Ok);
        Assert.Empty(rig.Bank.Movements);
        Assert.Null(rig.Escrow.Get(Session));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1_000)]
    [InlineData(999)]
    [InlineData(500_001)]
    public async Task AnAnteTheWalletDoesNotTakeIsRefused(long ante)
    {
        var rig = new Rig();
        rig.Bank.Seed(Wallet.Roubles, Rich);

        var reply = await rig.Deal(ante);

        Assert.False(reply.Ok);
        Assert.Empty(rig.Bank.Movements);
        Assert.Null(rig.Escrow.Get(Session));
    }

    /// <summary>No tie bet is fine. A tie bet is held to the same ends as the ante.</summary>
    [Theory]
    [InlineData(-1)]
    [InlineData(500)]
    [InlineData(500_001)]
    public async Task ATieBetTheWalletDoesNotTakeIsRefused(long tie)
    {
        var rig = new Rig();
        rig.Bank.Seed(Wallet.Roubles, Rich);

        var reply = await rig.Deal(Ante, tie);

        Assert.False(reply.Ok);
        Assert.Empty(rig.Bank.Movements);
    }

    [Theory]
    [InlineData(500_001)]
    [InlineData(2_500_000)]
    [InlineData(WalletInfo.AbsoluteMax)]
    public async Task TheMaximumCanBeTurnedOff(long ante)
    {
        var rig = new Rig().Stack("KH", "9S");
        rig.Bank.Seed(Wallet.Roubles, Rich);

        var reply = await rig.Deal(ante, ignoreMaximum: true);

        Assert.True(reply.Ok, reply.Error);
        Assert.Equal(ante, reply.Round!.Ante);
    }

    /// <summary>
    /// Past the arithmetic ceiling nothing is taken, switch or no switch. A tie bet that
    /// big would return more than the bank can count.
    /// </summary>
    [Fact]
    public async Task TheAbsoluteCeilingDoesNotComeOff()
    {
        var rig = new Rig();
        rig.Bank.Seed(Wallet.Roubles, int.MaxValue);

        var ante = await rig.Deal(WalletInfo.AbsoluteMax + 1L, ignoreMaximum: true);
        var tie = await rig.Deal(Ante, WalletInfo.AbsoluteMax + 1L, ignoreMaximum: true);

        Assert.False(ante.Ok);
        Assert.False(tie.Ok);
        Assert.Empty(rig.Bank.Movements);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(999)]
    public async Task TurningTheMaximumOffLeavesTheMinimumAlone(long ante)
    {
        var rig = new Rig();
        rig.Bank.Seed(Wallet.Roubles, Rich);

        var reply = await rig.Deal(ante, ignoreMaximum: true);

        Assert.False(reply.Ok);
        Assert.Empty(rig.Bank.Movements);
    }

    /// <summary>
    /// A bet that roubles would take, in a currency that is not one. The first version
    /// of this staked 1, which is refused for being too small whatever the currency --
    /// so a service that quietly played it in roubles still passed. The mutation check
    /// caught that.
    /// </summary>
    [Fact]
    public async Task AnUnknownCurrencyIsRefused()
    {
        var rig = new Rig();
        rig.Bank.Seed(Wallet.Roubles, Rich);

        var reply = await rig.Service.DealAsync(
            new DealRequest { Wallet = "Bitcoin", Ante = Ante },
            Session,
            new SPTarkov.Server.Core.Models.Eft.ItemEvent.ItemEventRouterResponse());

        Assert.False(reply.Ok);
        Assert.Empty(rig.Bank.Movements);
    }

    [Fact]
    public async Task DollarsAreTakenAndPaidInDollars()
    {
        var rig = new Rig().Stack("KH", "9S");
        rig.Bank.Seed(Wallet.Dollars, 10_000);

        var reply = await rig.Deal(100, wallet: Wallet.Dollars);

        Assert.True(reply.Ok, reply.Error);
        Assert.Equal("Dollars", reply.Wallet);
        Assert.All(rig.Bank.Movements, m => Assert.Equal(Wallet.Dollars, m.Wallet));
        Assert.Equal(10_100, rig.Bank.GetBalance(Session, Wallet.Dollars));
    }

    // ------------------------------------------------------------ crashes and leaving

    /// <summary>A stake with no round behind it goes back in full, exactly once.</summary>
    [Fact]
    public async Task AStakeStrandedByACrashIsGivenBackExactlyOnce()
    {
        var rig = new Rig();
        rig.Bank.Seed(Wallet.Roubles, 0);
        rig.Escrow.Strand(Session, Wallet.Roubles, 25_000);

        var first = rig.Ping();
        await rig.State();
        rig.Ping();

        Assert.NotNull(first.Note);
        Assert.Equal([25_000], rig.Bank.Credited);
        Assert.Null(rig.Escrow.Get(Session));
    }

    /// <summary>
    /// A tie still on the table is not stranded. Opening the table again, or the game
    /// syncing, must not hand back an ante the player is still deciding about.
    /// </summary>
    [Fact]
    public async Task ATieStillWaitingIsNotRefunded()
    {
        var rig = new Rig().Stack("7C", "7H");
        rig.Bank.Seed(Wallet.Roubles, Rich);
        await rig.Deal(Ante);

        rig.Ping();
        var state = await rig.State();

        Assert.Equal("AwaitingDecision", state.Round!.Phase);
        Assert.Equal("7C", state.Round.PlayerCard);
        Assert.Equal(-Ante, rig.Bank.Moved);
        Assert.Equal(Ante, rig.Escrow.Held(Session));
    }

    /// <summary>
    /// A restart with a tie on the table loses the table and keeps the escrow. The ante
    /// comes back whole on next contact -- more than surrendering would have paid, which
    /// is the right side of the line for a fault that was the server's.
    /// </summary>
    [Fact]
    public async Task ARestartDuringATieGivesTheAnteBack()
    {
        var rig = new Rig().Stack("7C", "7H");
        rig.Bank.Seed(Wallet.Roubles, Rich);
        await rig.Deal(Ante);

        rig.Tables.Clear(Session);
        var state = await rig.State();

        Assert.NotNull(state.Note);
        Assert.Equal(0, rig.Bank.Moved);
        Assert.Null(rig.Escrow.Get(Session));
        Assert.Equal("AwaitingBet", state.Round!.Phase);
    }

    // -------------------------------------------------------------- what it reports

    /// <summary>What the reply says came back is what the wallet received.</summary>
    [Fact]
    public async Task WhatTheReplySaysWasPaidIsWhatTheWalletReceived()
    {
        var rig = new Rig(seed: 3);
        rig.Bank.Seed(Wallet.Roubles, Rich);

        for (var i = 0; i < 200; i++)
        {
            var creditedBefore = rig.Bank.Credited.Sum();
            var debitedBefore = rig.Bank.Debited.Sum();

            var reply = await rig.Deal(Ante, 1_000);
            if (reply.Round!.Phase == "AwaitingDecision")
            {
                reply = await rig.Decide("War");
            }

            Assert.Equal(reply.Round!.Returned, rig.Bank.Credited.Sum() - creditedBefore);
            Assert.Equal(reply.Round.Staked, rig.Bank.Debited.Sum() - debitedBefore);
            Assert.Equal(rig.Bank.GetBalance(Session, Wallet.Roubles), reply.Balance);
        }
    }

    /// <summary>A round goes in the record once, when it settles -- not when it ties.</summary>
    [Fact]
    public async Task ARoundIsRecordedOnceWhenItSettles()
    {
        var rig = new Rig().Stack("7C", "7H", "2S", "3S", "4S", "QD", "5C");
        rig.Bank.Seed(Wallet.Roubles, Rich);

        await rig.Deal(Ante, 1_000);
        Assert.Equal(0, rig.Stats.Get(Session).RoundsPlayed);

        await rig.Decide("War");
        var record = rig.Stats.Get(Session);

        Assert.Equal(1, record.RoundsPlayed);
        Assert.Equal(1, record.Ties);
        Assert.Equal(1, record.WarsWon);
        Assert.Equal(1, record.TieBetsWon);
        Assert.Equal(2 * Ante + 1_000, record.ByCurrency["Roubles"].Wagered);
        Assert.Equal(3 * Ante + 11_000, record.ByCurrency["Roubles"].Returned);
    }

    [Fact]
    public void ThePingCarriesTheTablesOwnNumbers()
    {
        var ping = new Rig().Ping();

        Assert.Equal(6, ping.Decks);
        Assert.Equal(10, ping.TiePays);
        Assert.Equal(2.88, ping.HouseEdgeWar, 2);
        Assert.Equal(3.70, ping.HouseEdgeSurrender, 2);
        Assert.Equal(18.65, ping.HouseEdgeTieBet, 2);
        Assert.Equal(1_000, ping.Limits["Roubles"].Min);
        Assert.Equal(500_000, ping.Limits["Roubles"].Max);
    }

    [Fact]
    public async Task NothingHappensWithoutAProfile()
    {
        var rig = new Rig();
        rig.Profiles.Exists = false;
        rig.Bank.Seed(Wallet.Roubles, Rich);

        var reply = await rig.Deal(Ante);

        Assert.False(reply.Ok);
        Assert.Empty(rig.Bank.Movements);
    }
}
