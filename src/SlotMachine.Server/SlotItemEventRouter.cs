using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Request;
using SPTarkov.Server.Core.Models.Eft.ItemEvent;

namespace SlotMachine.Server;

/// <summary>Action names the client sends. Namespaced so they cannot collide with EFT's own.</summary>
public static class SlotActions
{
    /// <summary>
    /// Does nothing to the game. Exists so the client has something harmless to send
    /// when it needs the profile changes SPT has been holding for it.
    /// </summary>
    public const string Sync = "SlotsSync";
}

/// <summary>
/// The one action this table puts on EFT's own item-event endpoint.
///
/// Static routes cannot update the running game's inventory: currency moved through
/// one lands in the profile and leaves the stash on screen stale until a reload, which
/// reads to a player as the mod eating their money. An item-event reply carries the
/// `ProfileChanges` the client applies to its own inventory, so the client sends this
/// when it wants them.
///
/// **The play deliberately stays on the static route.** The reels take a few seconds to
/// settle, and an item-event pull would have the game apply the profile change the
/// instant the reply landed -- so the rouble counter behind the machine would show the
/// win before the reels stopped. Roulette learned that with its wheel; the fix is for
/// the client to decide when to ask, which is what this is.
/// </summary>
[Injectable(TypePriority = OnLoadOrder.PostDBModLoader)]
public sealed class SlotItemEventRouter(SlotItemEventCallbacks callbacks)
    : ItemEventRouterDefinition
{
    protected override List<HandledRoute> GetHandledRoutes() =>
    [
        new(SlotActions.Sync, false),
    ];

    protected override async ValueTask<ItemEventRouterResponse> HandleItemEventInternal(
        string url,
        PmcData pmcData,
        BaseInteractionRequestData body,
        MongoId sessionID,
        ItemEventRouterResponse output) =>
        url switch
        {
            SlotActions.Sync => await callbacks.Sync(sessionID, output),
            _ => throw new Exception($"SlotItemEventRouter cannot handle route {url}"),
        };
}

/// <summary>
/// Answers the sync action.
///
/// The reply's body is not the point and the client ignores it. What matters is that
/// this is an item-event response at all: SPT holds the profile changes it has made
/// for a session until the client's next item event and hands them over on that reply.
///
/// It is not quite a no-op. Pinging is also what gives back a stake stranded by an
/// interrupted pull, and this has an output to hang that on.
/// </summary>
[Injectable]
public class SlotItemEventCallbacks(SlotService service, SlotLog log)
{
    public Task<ItemEventRouterResponse> Sync(MongoId sessionId, ItemEventRouterResponse output)
    {
        log.Detail($"sync (item event) [{sessionId}]");

        var response = service.Ping(sessionId, output);

        if (response.Note is not null)
        {
            log.Info(response.Note);
        }

        return Task.FromResult(output);
    }
}
