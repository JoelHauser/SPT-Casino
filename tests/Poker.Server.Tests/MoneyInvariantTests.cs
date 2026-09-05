using Poker.Game;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.ItemEvent;

namespace Poker.Server.Tests;

/// <summary>
/// What the wallet did, against what the table said happened.
///
/// Ported from Blackjack, where it earned its place: it plays whole sessions and
/// checks after every one that the money which actually moved equals the result the
/// engine reported. An end-of-run balance check would miss errors that cancel, and
/// a single scripted session would miss the ones that need a particular shape of hand
/// to appear at all.
///
/// **Written before the settlement it checks**, deliberately -- that is the whole
/// lesson from the sibling project. A money test added afterwards is a test written
/// to agree with whatever the code already does.
/// </summary>
public class MoneyInvariantTests
{
    private static readonly MongoId Session = new("6a8cd3a7e0b8272790f41285");

    private sealed class SilentLog : IPokerLog
    {
        public void Info(string message)
        {
        }

        public void Detail(string message)
        {
        }

        public void Error(string message)
        {
        }

        public IGameLog ForEngine() => GameLog.Null;
    }

    private sealed record Harness(
        PokerService Service,
        FakeBank Bank,
        FakeEscrow Escrow,
        FakeProfiles Profiles,
        TableStore Tables);

    private static Harness Build(int roubles = 20_000_000)
    {
        var bank = new FakeBank();
        bank.Seed(Wallet.Roubles, roubles);

        var escrow = new FakeEscrow();
        var profiles = new FakeProfiles();
        var tables = new TableStore();

        return new Harness(
            new PokerService(bank, profiles, tables, escrow, new FakeNames(), new SilentLog()),
            bank,
            escrow,
            profiles,
            tables);
    }

    private static ItemEventRouterResponse Output() => new();

    private static SitRequest Sit(int seats = 4, int buyIn = 2_000_000, int bigBlind = 20_000, int seed = 1) =>
        new() { Seats = seats, BuyIn = buyIn, BigBlind = bigBlind, Seed = seed };

    /// <summary>Plays one hand out, checking and calling. Returns false once busted.</summary>
    private static bool PlayHand(Harness harness)
    {
        var dealt = harness.Service.Deal(Session);

        if (!dealt.Ok)
        {
            return false;
        }

        var guard = 0;

        while (dealt.Table?.AwaitingPlayer == true && guard++ < 60)
        {
            var moves = dealt.Table.Options!.Moves;

            dealt = harness.Service.Act(
                new ActRequest
                {
                    Move = moves.Contains(HoldemMove.Check) ? "Check" : "Call",
                },
                Session);
        }

        return true;
    }

    [Fact]
    public async Task SittingDownTakesTheBuyInAndNothingElse()
    {
        var harness = Build();
        var before = harness.Bank.GetBalance(Session, Wallet.Roubles);

        var response = await harness.Service.SitAsync(Sit(), Session, Output());

        Assert.True(response.Ok, response.Error);
        Assert.Equal(before - 2_000_000, harness.Bank.GetBalance(Session, Wallet.Roubles));
        Assert.Equal(1, harness.Bank.Debits);
        Assert.Equal(0, harness.Bank.Credits);
    }

    [Fact]
    public async Task TheBotsNeverTouchTheBank()
    {
        // Their chips are notional. Twenty hands of betting, raising and busting
        // between four seats must not move the wallet by a single rouble.
        var harness = Build();
        await harness.Service.SitAsync(Sit(), Session, Output());

        var afterBuyIn = harness.Bank.GetBalance(Session, Wallet.Roubles);

        for (var hand = 0; hand < 20 && PlayHand(harness); hand++)
        {
        }

        Assert.Equal(afterBuyIn, harness.Bank.GetBalance(Session, Wallet.Roubles));
        Assert.Equal(1, harness.Bank.Debits);
        Assert.Equal(0, harness.Bank.Credits);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public async Task WhatTheWalletDidEqualsWhatTheTableSaidHappened(int seed)
    {
        // The invariant. Across a whole session -- buy in, play until the hands run
        // out or the chips do, stand up -- the change in the wallet has to equal the
        // change in the stack. Anything else is money created or destroyed.
        var harness = Build();
        var before = harness.Bank.GetBalance(Session, Wallet.Roubles);

        var sat = await harness.Service.SitAsync(Sit(seed: seed), Session, Output());
        Assert.True(sat.Ok, sat.Error);

        for (var hand = 0; hand < 25 && PlayHand(harness); hand++)
        {
        }

        var stack = harness.Tables.Get(Session)!.Table.Player.Stack;

        var left = await harness.Service.LeaveAsync(Session, Output());
        Assert.True(left.Ok, left.Error);

        var after = harness.Bank.GetBalance(Session, Wallet.Roubles);

        Assert.Equal(stack - 2_000_000, after - before);
        Assert.Null(harness.Escrow.Get(Session));
    }

    [Fact]
    public async Task StandingUpGivesBackExactlyWhatIsInFrontOfYou()
    {
        var harness = Build();
        await harness.Service.SitAsync(Sit(), Session, Output());

        for (var hand = 0; hand < 10 && PlayHand(harness); hand++)
        {
        }

        var stack = harness.Tables.Get(Session)!.Table.Player.Stack;
        var before = harness.Bank.GetBalance(Session, Wallet.Roubles);

        await harness.Service.LeaveAsync(Session, Output());

        Assert.Equal(before + stack, harness.Bank.GetBalance(Session, Wallet.Roubles));
    }

    [Fact]
    public async Task EscrowFollowsTheStackRatherThanTheBuyIn()
    {
        // The difference between this and Blackjack's escrow, and the reason it had to
        // be rewritten rather than ported. Recording the buy-in and stopping would
        // refund a player who had lost most of it and rob one who had doubled up.
        var harness = Build();
        await harness.Service.SitAsync(Sit(), Session, Output());

        for (var hand = 0; hand < 12 && PlayHand(harness); hand++)
        {
        }

        var stack = harness.Tables.Get(Session)!.Table.Player.Stack;

        Assert.Equal(stack, harness.Escrow.Get(Session)!.Chips);
        Assert.True(
            harness.Escrow.Recorded.Distinct().Count() > 1,
            "escrow never changed, so it is recording the buy-in rather than the stack");
    }

    [Fact]
    public async Task ACrashMidSessionGivesBackWhatThePlayerActuallyHad()
    {
        var harness = Build();
        await harness.Service.SitAsync(Sit(), Session, Output());

        for (var hand = 0; hand < 12 && PlayHand(harness); hand++)
        {
        }

        var stack = harness.Tables.Get(Session)!.Table.Player.Stack;
        var before = harness.Bank.GetBalance(Session, Wallet.Roubles);

        // The table is in memory and does not survive a restart. The stack is on disk
        // and must.
        harness.Tables.Clear(Session);

        var recovered = await harness.Service.LeaveAsync(Session, Output());

        Assert.Equal(before + stack, harness.Bank.GetBalance(Session, Wallet.Roubles));
        Assert.NotNull(recovered.Note);
        Assert.Null(harness.Escrow.Get(Session));
    }

    [Fact]
    public async Task AnAbandonedStackIsGivenBackBeforeAnotherBuyInIsTaken()
    {
        // Otherwise the two are indistinguishable in the stash and the older one is
        // silently kept.
        var harness = Build();
        await harness.Service.SitAsync(Sit(), Session, Output());
        harness.Tables.Clear(Session);

        var response = await harness.Service.SitAsync(Sit(seed: 9), Session, Output());

        Assert.True(response.Ok, response.Error);
        Assert.NotNull(response.Note);
        Assert.Equal(2, harness.Bank.Debits);
        Assert.Equal(1, harness.Bank.Credits);
    }

    [Fact]
    public async Task AskingForTheTableGivesBackAnAbandonedStack()
    {
        // The fault this pins cost real money on a real profile. Refunding only on sit
        // and leave sounds sufficient and is not: a player who is owed a stack has no
        // reason to press either -- SIT DOWN asks for another buy-in and LEAVE says they
        // are not at a table -- so the stack sat in escrow with nothing telling them.
        // Opening the panel is the one request they make without meaning to spend
        // anything, which is why it is the one that has to hand the money back.
        var harness = Build();
        var before = harness.Bank.GetBalance(Session, Wallet.Roubles);

        await harness.Service.SitAsync(Sit(), Session, Output());

        // The table is gone and the stack is not -- a server restart, or a crash.
        harness.Tables.Clear(Session);

        var response = await harness.Service.StateAsync(Session, Output());

        Assert.NotNull(response.Note);
        Assert.Equal(before, harness.Bank.GetBalance(Session, Wallet.Roubles));
        Assert.Null(harness.Escrow.Get(Session));

        // Still not at a table: the refund is not a seat, and answering otherwise would
        // have the client draw a table that does not exist.
        Assert.False(response.Ok);
    }

    [Fact]
    public async Task AskingForTheTableTwiceDoesNotPayTwice()
    {
        var harness = Build();
        await harness.Service.SitAsync(Sit(), Session, Output());
        harness.Tables.Clear(Session);

        await harness.Service.StateAsync(Session, Output());
        var again = await harness.Service.StateAsync(Session, Output());

        Assert.Null(again.Note);
        Assert.Equal(1, harness.Bank.Credits);
    }

    [Fact]
    public async Task ASeatedPlayerAskingForTheTableIsNotRefunded()
    {
        // A live table still owns its stack. Refunding here would hand back the buy-in
        // and leave the player sitting behind chips they no longer own.
        var harness = Build();
        await harness.Service.SitAsync(Sit(), Session, Output());

        var response = await harness.Service.StateAsync(Session, Output());

        Assert.True(response.Ok, response.Error);
        Assert.Null(response.Note);
        Assert.Equal(0, harness.Bank.Credits);
        Assert.NotNull(harness.Escrow.Get(Session));
    }

    [Fact]
    public async Task ABuyInThatCannotBeAffordedTakesNothing()
    {
        var harness = Build(roubles: 500_000);

        var response = await harness.Service.SitAsync(Sit(), Session, Output());

        Assert.False(response.Ok);
        Assert.Equal(500_000, harness.Bank.GetBalance(Session, Wallet.Roubles));
        Assert.Equal(0, harness.Bank.Debits);
        Assert.Null(harness.Escrow.Get(Session));
        Assert.Null(harness.Tables.Get(Session));
    }

    [Fact]
    public async Task AWalletThatCannotCoverTheseStakesIsRefusedByName()
    {
        // One chip to the unit, so a 2,000,000 chip table cannot be bought into with
        // dollars -- they cap at 5,000. Refused with the numbers in the message rather
        // than by silently failing to debit.
        //
        // This used to be pinned with bitcoin, which made the same point far more
        // dramatically. Dollars keep the rule honest now that only currency is
        // stakeable, and are the case that actually reaches a player: roubles are the
        // only wallet these stakes admit until each one has a chips-per-unit rate.
        var harness = Build();

        var response = await harness.Service.SitAsync(
            new SitRequest { Wallet = nameof(Wallet.Dollars), BuyIn = 2_000_000, BigBlind = 20_000 },
            Session,
            Output());

        Assert.False(response.Ok);
        Assert.Contains("Dollars", response.Error);
        Assert.Equal(0, harness.Bank.Debits);
    }

    [Fact]
    public async Task ABustedPlayerIsNotToppedUpForFree()
    {
        // The console tops everybody up, which is right for a harness and wrong here:
        // these chips cost currency, so a fresh stack is a fresh buy-in.
        // Busted by playing rather than by reaching into the seat: ten big blinds
        // heads-up, calling everything, runs out of chips soon enough and does it
        // through the same path a real session would.
        var harness = Build();
        await harness.Service.SitAsync(
            Sit(seats: 2, buyIn: 200_000, bigBlind: 20_000, seed: 3), Session, Output());

        var before = harness.Bank.GetBalance(Session, Wallet.Roubles);
        var hands = 0;

        while (hands < 400 && PlayHand(harness))
        {
            hands++;
        }

        Assert.True(hands < 400, "the player never went broke, so this test proved nothing");

        var response = harness.Service.Deal(Session);

        Assert.False(response.Ok);
        Assert.Contains("buy in again", response.Error);
        Assert.Equal(before, harness.Bank.GetBalance(Session, Wallet.Roubles));
    }

    [Fact]
    public async Task MoneyIsSavedToDiskWheneverItMoves()
    {
        // Money that is not flushed did not move.
        var harness = Build();

        await harness.Service.SitAsync(Sit(), Session, Output());
        Assert.Equal(1, harness.Profiles.Saves);

        await harness.Service.LeaveAsync(Session, Output());
        Assert.Equal(2, harness.Profiles.Saves);
    }
}
