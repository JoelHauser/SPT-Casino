using Blackjack.Game;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.ItemEvent;

namespace Blackjack.Server.Tests;

/// <summary>
/// The client keeps its own copy of the inventory and only updates it from the
/// ProfileChanges an item-event reply carries. Money moved without that record lands
/// in the profile but leaves the stash looking untouched until the game reloads --
/// which reads to a player exactly like the mod ate their winnings.
/// </summary>
public class ChangeRecordTests
{
    private readonly MongoId _session = new();
    private readonly FakeBank _bank = new();
    private readonly FakeProfiles _profiles = new();
    private readonly FakeStats _stats = new();
    private readonly FakeEscrow _escrow = new();
    private readonly TableStore _tables = new();

    private BlackjackService WithDeal(string cards)
    {
        _tables.Seed(_session, new BlackjackTable(
            new Rules { MinBet = 1, MaxBet = int.MaxValue },
            Shoe.Stacked(cards.Split(' ').Select(Card.Parse))));

        return new BlackjackService(_bank, _profiles, _tables, _stats, _escrow);
    }

    [Fact]
    public async Task TheCallersRecordReachesBothTheDebitAndThePayout()
    {
        // Player AS/KH is a natural, so this deal both takes the stake and pays out.
        var service = WithDeal("AS 9H KH 7D");
        var output = new ItemEventRouterResponse();

        await service.DealAsync(
            new DealRequest { Wager = 10_000, Wallet = nameof(Wallet.Roubles) },
            _session,
            output);

        Assert.Equal(2, _bank.Outputs.Count);
        Assert.All(_bank.Outputs, o => Assert.Same(output, o));
    }

    [Fact]
    public async Task TheRecordSurvivesAcrossActionsWithinARound()
    {
        var service = WithDeal("5S KH 6D 7C 9H");
        var dealOutput = new ItemEventRouterResponse();
        var actOutput = new ItemEventRouterResponse();

        await service.DealAsync(
            new DealRequest { Wager = 10_000, Wallet = nameof(Wallet.Roubles) },
            _session,
            dealOutput);

        // Doubling collects a second stake and then settles, so this action alone
        // moves money twice.
        await service.ActAsync(new ActionRequest { Action = nameof(PlayerAction.Double) }, _session, actOutput);

        Assert.Same(dealOutput, _bank.Outputs[0]);
        Assert.All(_bank.Outputs.Skip(1), o => Assert.Same(actOutput, o));
    }

    [Fact]
    public async Task TheStaticRouteStillWorksWithNoRecordToReturn()
    {
        // curl testing has no client listening, so the overload without a response
        // must behave identically apart from where the record goes.
        var service = WithDeal("AS 9H KH 7D");

        var response = await service.DealAsync(
            new DealRequest { Wager = 10_000, Wallet = nameof(Wallet.Roubles) },
            _session);

        Assert.True(response.Ok);
        Assert.Equal([(Wallet.Roubles, 25_000)], _bank.Credits);
        Assert.Equal(2, _bank.Outputs.Count);
    }
}
