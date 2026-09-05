using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.ItemEvent;

namespace Poker.Server;

/// <summary>
/// The item-event half of the transport.
///
/// Identical game flow to <see cref="PokerCallbacks"/> -- the same service, the same
/// decisions -- and different only in what it hands back. A static route returns JSON
/// and the client's stash goes stale; this returns the `ItemEventRouterResponse` SPT
/// itself filled in, so the inventory updates without a reload.
///
/// **An item-event reply carries `ProfileChanges` and nothing else.** The table has to
/// ride along in `ExtensionData` or the client would need a second request for it, and
/// a second request is a second chance for the two to disagree.
/// </summary>
[Injectable]
public class PokerItemEventCallbacks(PokerService service, IPokerLog log)
{
    /// <summary>
    /// The key the table hangs off in the reply. Namespaced, because everything in
    /// `ExtensionData` shares one bag with whatever else the server put there.
    /// </summary>
    private const string Payload = "poker";

    public async ValueTask<ItemEventRouterResponse> Sit(
        PokerSitAction body,
        MongoId sessionId,
        ItemEventRouterResponse output)
    {
        var request = new SitRequest
        {
            Seats = body.Seats,
            BuyIn = body.BuyIn,
            BigBlind = body.BigBlind,
            Wallet = body.Wallet,
        };

        log.Detail($"-> sit (item event) [{sessionId}] {body.Seats} seats, {body.BuyIn} {body.Wallet}");

        return Attach(output, await service.SitAsync(request, sessionId, output));
    }

    public ValueTask<ItemEventRouterResponse> Deal(
        PokerDealAction body,
        MongoId sessionId,
        ItemEventRouterResponse output)
    {
        log.Detail($"-> deal (item event) [{sessionId}]");

        return new ValueTask<ItemEventRouterResponse>(Attach(output, service.Deal(sessionId)));
    }

    public ValueTask<ItemEventRouterResponse> Act(
        PokerActAction body,
        MongoId sessionId,
        ItemEventRouterResponse output)
    {
        log.Detail($"-> act (item event) [{sessionId}] {body.Move}");

        return new ValueTask<ItemEventRouterResponse>(
            Attach(output, service.Act(new ActRequest { Move = body.Move, To = body.To }, sessionId)));
    }

    public async ValueTask<ItemEventRouterResponse> Leave(
        PokerLeaveAction body,
        MongoId sessionId,
        ItemEventRouterResponse output)
    {
        log.Detail($"-> leave (item event) [{sessionId}]");

        return Attach(output, await service.LeaveAsync(sessionId, output));
    }

    /// <summary>
    /// Asks for the table, and carries the profile changes back.
    ///
    /// The client sends this when it wants the profile changes the server has been
    /// holding for it -- after money has moved through a static route, say. The reply
    /// carries them by virtue of being an item-event reply at all.
    ///
    /// It is no longer quite a no-op: reading the table is also what gives back an
    /// abandoned stack, and this transport has had an output to hang that on all along.
    /// See PokerService.StateAsync.
    /// </summary>
    public async Task<ItemEventRouterResponse> Sync(MongoId sessionId, ItemEventRouterResponse output)
    {
        log.Detail($"-> sync (item event) [{sessionId}]");

        return Attach(output, await service.StateAsync(sessionId, output));
    }

    private ItemEventRouterResponse Attach(ItemEventRouterResponse output, PokerResponse response)
    {
        if (response.Note is not null)
        {
            log.Info(response.Note);
        }

        if (!response.Ok)
        {
            log.Detail($"<- refused: {response.Error}");
        }

        output.ExtensionData ??= new Dictionary<string, object>();
        output.ExtensionData[Payload] = response;

        return output;
    }
}
