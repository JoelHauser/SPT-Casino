using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.DI.Routing;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.ItemEvent;
using SPTarkov.Server.Core.Utils;

namespace Roulette.Server;

/// <summary>Action names the client sends. Namespaced so they cannot collide with EFT's own.</summary>
public static class RouletteActions
{
    /// <summary>
    /// Does nothing to the game. Exists so the client has something harmless to send
    /// when it needs the profile changes SPT has been holding for it.
    /// </summary>
    public const string Sync = "RouletteSync";
}

/// <summary>
/// The one action this mod puts on EFT's own item-event endpoint.
///
/// ## Why there is only one
///
/// SPT gives a mod two ways to be talked to. **Static routes** -- what
/// <see cref="RouletteRouter"/> serves -- are ordinary URLs answering ordinary JSON;
/// the game has no idea they happened, so currency moved through one lands in the
/// profile and leaves the stash on screen stale until a reload. **Item events** arrive
/// on `/client/game/profile/items/moving`, the endpoint the game already uses for
/// dragging things around, and the reply carries `ProfileChanges` that the client
/// applies to its own inventory without being asked.
///
/// Poker and Blackjack put their whole game on the item-event transport for exactly
/// that reason. **Roulette deliberately does not**, and the reason is the animation.
/// The wheel takes six to nine seconds and the server has already settled before the
/// first frame is drawn, so the money has moved while the ball is still rolling. If
/// the spin itself were an item event, the game would apply the profile change the
/// instant the reply landed and the rouble counter behind the table would give the
/// result away nine seconds early. That happened, and fixing it is what
/// `RoulettePanel.ResyncStash` is: the play stays on a static route and the client
/// asks for its profile changes when the ball stops.
///
/// So this transport exists here for one purpose -- to be asked. That is
/// <see cref="Sync"/>, and it is the whole of it.
///
/// **Without this the client was sending `RouletteSync` to a server that had no
/// handler for it**, which SPT answered with `[UNHANDLED EVENT] RouletteSync` at
/// error level on every single spin. It worked, because an item-event reply carries
/// the pending changes whether or not anything handled the action, but it worked by
/// accident and said so in red every time.
/// </summary>
[Injectable(TypePriority = OnLoadOrder.Routers)]
public sealed class RouletteItemEventRouter(RouletteItemEventCallbacks callbacks)
    : ItemEventRouter([
        new ItemRouteAction<RouletteSyncAction>(
            RouletteActions.Sync,
            async (url, pmcData, body, sessionId, output, cancellationToken) =>
                await callbacks.Sync(sessionId, output)),
    ]);

/// <summary>
/// Answers the sync action.
///
/// The reply's body is not the point and the client ignores it. What matters is that
/// this is an item-event response at all: SPT holds the profile changes it has made
/// for a session until the client's next item event and hands them over on that reply.
///
/// It is not quite a no-op. Reading the table is also what gives back a stake stranded
/// by an interrupted spin, and this has an output to hang that on.
/// </summary>
[Injectable]
public class RouletteItemEventCallbacks(RouletteService service, RouletteLog log)
{
    public async Task<ItemEventRouterResponse> Sync(MongoId sessionId, ItemEventRouterResponse output)
    {
        log.Detail($"sync (item event) [{sessionId}]");

        var response = await service.StateAsync(sessionId, output);

        if (response.Note is not null)
        {
            log.Info(response.Note);
        }

        return output;
    }
}
