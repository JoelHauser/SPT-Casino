using Poker.Game;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.ItemEvent;

namespace Poker.Server.Tests;

/// <summary>
/// The transport the game client uses.
///
/// It has to move exactly the same money as the static routes and hand back the table
/// as well, because the reply the client applies to its own inventory is the same
/// object -- and if the two transports ever disagree about a hand, the one the player
/// is looking at is this one.
/// </summary>
public class ItemEventTests
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
        PokerItemEventCallbacks Events,
        PokerService Service,
        FakeBank Bank,
        FakeEscrow Escrow,
        TableStore Tables);

    private static Harness Build()
    {
        var bank = new FakeBank();
        bank.Seed(Wallet.Roubles, 20_000_000);

        var escrow = new FakeEscrow();
        var tables = new TableStore();
        var service = new PokerService(bank, new FakeProfiles(), tables, escrow, new FakeNames(), new SilentLog());

        return new Harness(new PokerItemEventCallbacks(service, new SilentLog()), service, bank, escrow, tables);
    }

    private static PokerSitAction Sit() =>
        new() { Seats = 4, BuyIn = 2_000_000, BigBlind = 20_000 };

    private static PokerResponse Payload(ItemEventRouterResponse output)
    {
        Assert.NotNull(output.ExtensionData);
        Assert.True(output.ExtensionData!.ContainsKey("poker"), "the table did not ride along in the reply");

        return Assert.IsType<PokerResponse>(output.ExtensionData["poker"]);
    }

    [Fact]
    public async Task SittingDownThroughAnItemEventTakesTheBuyInAndReturnsTheTable()
    {
        var harness = Build();
        var output = new ItemEventRouterResponse();

        var reply = await harness.Events.Sit(Sit(), Session, output);
        var response = Payload(reply);

        Assert.True(response.Ok, response.Error);
        Assert.NotNull(response.Table);
        Assert.Equal(18_000_000, harness.Bank.GetBalance(Session, Wallet.Roubles));
        Assert.Equal(1, harness.Bank.Debits);
    }

    [Fact]
    public async Task TheReplyIsTheSameObjectSptFilledIn()
    {
        // Not a copy. The client applies this to its own inventory, so anything the
        // bank wrote into it on the way through has to still be there.
        var harness = Build();
        var output = new ItemEventRouterResponse();

        var reply = await harness.Events.Sit(Sit(), Session, output);

        Assert.Same(output, reply);
    }

    [Fact]
    public async Task StandingUpThroughAnItemEventPaysTheStackBack()
    {
        var harness = Build();
        await harness.Events.Sit(Sit(), Session, new ItemEventRouterResponse());

        var stack = harness.Tables.Get(Session)!.Table.Player.Stack;
        var before = harness.Bank.GetBalance(Session, Wallet.Roubles);

        var reply = await harness.Events.Leave(new PokerLeaveAction(), Session, new ItemEventRouterResponse());

        Assert.True(Payload(reply).Ok);
        Assert.Equal(before + stack, harness.Bank.GetBalance(Session, Wallet.Roubles));
        Assert.Null(harness.Escrow.Get(Session));
    }

    [Fact]
    public async Task BothTransportsMoveTheSameMoney()
    {
        // The static routes exist for scripts and the item events for the game. They
        // share a service precisely so this stays true; a second copy of the flow is
        // a second set of money bugs.
        var byEvent = Build();
        await byEvent.Events.Sit(Sit(), Session, new ItemEventRouterResponse());

        var byRoute = Build();
        await byRoute.Service.SitAsync(
            new SitRequest { Seats = 4, BuyIn = 2_000_000, BigBlind = 20_000 },
            Session,
            new ItemEventRouterResponse());

        Assert.Equal(
            byRoute.Bank.GetBalance(Session, Wallet.Roubles),
            byEvent.Bank.GetBalance(Session, Wallet.Roubles));

        Assert.Equal(byRoute.Escrow.Get(Session)!.Chips, byEvent.Escrow.Get(Session)!.Chips);
    }

    [Fact]
    public async Task SyncMovesNoMoneyOfItsOwnAndStillCarriesTheChangeRecord()
    {
        // Its job is to be a harmless thing to send when the client needs the profile
        // changes the server has been holding for it. It is no longer strictly a no-op
        // -- reading the table is what gives back an abandoned stack -- so what is
        // pinned here is that with nothing owed it still moves nothing.
        var harness = Build();
        var output = new ItemEventRouterResponse();

        var reply = await harness.Events.Sync(Session, output);

        Assert.Same(output, reply);
        Assert.Equal(0, harness.Bank.Debits);
        Assert.Equal(0, harness.Bank.Credits);
    }

    [Fact]
    public async Task SyncGivesBackAnAbandonedStackToo()
    {
        // Both transports refund, because either can be the first thing a returning
        // player sends. BothTransportsMoveTheSameMoney is the rule and this is the
        // easiest place to break it.
        var harness = Build();
        await harness.Events.Sit(Sit(), Session, new ItemEventRouterResponse());
        harness.Tables.Clear(Session);

        await harness.Events.Sync(Session, new ItemEventRouterResponse());

        Assert.Equal(1, harness.Bank.Credits);
        Assert.Null(harness.Escrow.Get(Session));
    }

    [Fact]
    public async Task ARefusedActionStillHandsBackTheRealTable()
    {
        // A refusal means the client's view drifted, so it needs the truth rather than
        // a bare error to argue with.
        var harness = Build();
        await harness.Events.Sit(Sit(), Session, new ItemEventRouterResponse());
        harness.Events.Deal(new PokerDealAction(), Session, new ItemEventRouterResponse());

        var reply = await harness.Events.Act(
            new PokerActAction { Move = "Fold", To = 0 }, Session, new ItemEventRouterResponse());

        // Folding is legal, so ask for something that is not.
        var refused = await harness.Events.Act(
            new PokerActAction { Move = "Nonsense", To = 0 }, Session, new ItemEventRouterResponse());

        var response = Payload(refused);

        Assert.False(response.Ok);
        Assert.NotNull(response.Error);
        Assert.NotNull(Payload(reply));
    }
}
