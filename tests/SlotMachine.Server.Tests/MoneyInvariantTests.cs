using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.ItemEvent;

namespace SlotMachine.Server.Tests;

/// <summary>
/// What the machine is allowed to do to a player's money.
///
/// **Written before the settlement it checks**, which is the instruction this repo has
/// carried since before there was a slot. An end-of-run balance check misses errors
/// that cancel, and a settlement written first gets tests shaped around what it
/// already does rather than around what it owes.
///
/// ## The model these pin
///
/// A slot is the simplest of the four: there is no table to sit at and nothing to
/// build up. One pull is one transaction -- the stake leaves, the reels settle, what
/// they paid arrives. There is no state between pulls at all, which is why there is no
/// table store here and why the escrow window is the shortest in the casino.
/// </summary>
public class MoneyInvariantTests
{
    private static readonly MongoId Session = new("6a9b474574813708e8fc3ce5");

    private const int Rich = 500_000_000;

    /// <summary>
    /// The invariant, and the only one that matters: **the wallet moved by exactly what
    /// the machine said it did.**
    ///
    /// Measured from every individual movement rather than from the closing balance,
    /// because a debit one short and a credit one over net to a balance that looks
    /// right.
    /// </summary>
    [Fact]
    public async Task TheWalletMovesByWhatWasPaidLessWhatWasStaked()
    {
        var (service, bank, _, _) = Machine();
        bank.Seed(Wallet.Roubles, Rich);

        var expected = 0L;

        for (var i = 0; i < 300; i++)
        {
            var reply = await service.PullAsync(Request(10_000), Session, Output());

            Assert.True(reply.Ok, reply.Error);
            expected += reply.Pull!.Paid - reply.Pull.Staked;
        }

        Assert.Equal(expected, bank.Moved);
        Assert.Equal(Rich + expected, bank.GetBalance(Session, Wallet.Roubles));
    }

    /// <summary>One pull, one debit. A losing pull credits nothing at all.</summary>
    [Fact]
    public async Task EveryPullTakesTheStakeOnceAndPaysAtMostOnce()
    {
        var (service, bank, _, _) = Machine();
        bank.Seed(Wallet.Roubles, Rich);

        for (var i = 0; i < 200; i++)
        {
            var before = bank.Debits;
            var reply = await service.PullAsync(Request(10_000), Session, Output());

            Assert.Equal(before + 1, bank.Debits);
            Assert.True(reply.Pull!.Paid >= 0);
        }
    }

    /// <summary>
    /// A stake the wallet cannot cover takes nothing at all, and the reels do not turn.
    /// Half of it going and the pull failing is the shape of the bug this pins.
    /// </summary>
    [Fact]
    public async Task APullThatCannotBeAffordedTakesNothing()
    {
        var (service, bank, _, escrow) = Machine();
        bank.Seed(Wallet.Roubles, 4_000);

        var reply = await service.PullAsync(Request(10_000), Session, Output());

        Assert.False(reply.Ok);
        Assert.Empty(bank.Movements);
        Assert.Null(escrow.Get(Session));
        Assert.Equal(4_000, bank.GetBalance(Session, Wallet.Roubles));
    }

    /// <summary>
    /// Stakes outside what the wallet takes are refused before anything moves.
    ///
    /// The panel offers a button that walks the steps, so none of these can be reached
    /// by playing. A request is a thing anybody can send by hand.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-5_000)]
    [InlineData(1)]
    [InlineData(4_999)]
    [InlineData(7_500)]
    [InlineData(50_001)]
    [InlineData(1_000_000)]
    public async Task AStakeTheWalletDoesNotTakeIsRefused(long stake)
    {
        var (service, bank, _, escrow) = Machine();
        bank.Seed(Wallet.Roubles, Rich);

        var reply = await service.PullAsync(Request(stake), Session, Output());

        Assert.False(reply.Ok);
        Assert.Empty(bank.Movements);
        Assert.Null(escrow.Get(Session));
    }

    /// <summary>An unknown currency is refused by name rather than defaulting to one.</summary>
    [Fact]
    public async Task AnUnknownCurrencyIsRefused()
    {
        var (service, bank, _, _) = Machine();
        bank.Seed(Wallet.Roubles, Rich);

        var reply = await service.PullAsync(
            new PullRequest { Wallet = "Bitcoin", Stake = 10_000 }, Session, Output());

        Assert.False(reply.Ok);
        Assert.Empty(bank.Movements);
    }

    /// <summary>
    /// Escrow is written before the money is taken and released only after it is paid.
    /// A crash anywhere in between leaves a record of a stake the player is owed.
    /// </summary>
    [Fact]
    public async Task EscrowHoldsTheStakeAcrossThePullAndIsReleasedAfterwards()
    {
        var (service, _, _, escrow) = Machine();
        var (_, bank, _, _) = (service, default(FakeBank)!, default(FakeProfiles)!, escrow);

        var (svc, money, _, held) = Machine();
        money.Seed(Wallet.Roubles, Rich);

        await svc.PullAsync(Request(20_000), Session, Output());

        Assert.Equal([20_000], held.Recorded);
        Assert.Equal(1, held.Releases);
        Assert.Null(held.Get(Session));
    }

    /// <summary>
    /// A stake stranded by a crash is given back on the next contact, and given back
    /// **once**. Refunding twice is worse than not refunding at all, because nobody
    /// reports it.
    /// </summary>
    [Fact]
    public async Task AStakeStrandedByACrashIsGivenBackExactlyOnce()
    {
        var (service, bank, _, escrow) = Machine();
        bank.Seed(Wallet.Roubles, 0);
        escrow.Strand(Session, Wallet.Roubles, 35_000);

        var first = service.Ping(Session, Output());

        Assert.Equal(35_000, bank.GetBalance(Session, Wallet.Roubles));
        Assert.Null(escrow.Get(Session));
        Assert.NotNull(first.Note);

        service.Ping(Session, Output());
        service.Ping(Session, Output());

        Assert.Equal(35_000, bank.GetBalance(Session, Wallet.Roubles));
        Assert.Equal(1, bank.Credits);
    }

    /// <summary>Money that is not flushed to disk did not move.</summary>
    [Fact]
    public async Task EveryPullIsFlushedToDisk()
    {
        var (service, bank, profiles, _) = Machine();
        bank.Seed(Wallet.Roubles, Rich);

        await service.PullAsync(Request(10_000), Session, Output());

        Assert.True(profiles.Saves >= 1, "a pull moved money and never saved it.");
    }

    /// <summary>
    /// What the player is told they won is what the wallet was actually given.
    ///
    /// The reply and the credit come from the same settlement, and this is what keeps
    /// them that way: a panel showing a win the stash never received is the worst kind
    /// of bug, because the player is the only one who notices and nobody believes them.
    /// </summary>
    [Fact]
    public async Task WhatTheReplySaysWasPaidIsWhatTheWalletReceived()
    {
        var (service, bank, _, _) = Machine();
        bank.Seed(Wallet.Roubles, Rich);

        for (var i = 0; i < 500; i++)
        {
            var before = bank.Movements.Count;
            var reply = await service.PullAsync(Request(10_000), Session, Output());

            var credited = bank.Movements
                .Skip(before)
                .Where(m => m.Amount > 0)
                .Sum(m => (long)m.Amount);

            Assert.Equal(reply.Pull!.Paid, credited);
        }
    }

    // ------------------------------------------------------------------ helpers

    private static (SlotService Service, FakeBank Bank, FakeProfiles Profiles, FakeEscrow Escrow) Machine()
    {
        var bank = new FakeBank();
        var profiles = new FakeProfiles();
        var escrow = new FakeEscrow();

        var service = new SlotService(bank, profiles, escrow, new FakeRandom(20260906), new QuietLog());

        return (service, bank, profiles, escrow);
    }

    private static PullRequest Request(long stake) =>
        new() { Wallet = nameof(Wallet.Roubles), Stake = stake };

    /// <summary>
    /// A real one comes from `EventOutputHolder.GetOutput`. The fake bank never reads
    /// it, so a bare instance is honest here in a way it would not be in the server.
    /// </summary>
    private static ItemEventRouterResponse Output() => new();
}
