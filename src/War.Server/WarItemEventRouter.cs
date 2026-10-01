using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.DI.Routing;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.ItemEvent;

namespace War.Server;

/// <summary>Action names the client sends. Namespaced so they cannot collide with EFT's own.</summary>
public static class WarActions
{
    /// <summary>
    /// Does nothing to the game. Exists so the client has something harmless to send
    /// when it wants the profile changes SPT has been holding for it. Must stay in step
    /// with the name the panel passes to <c>ProfileSync.Request</c>.
    /// </summary>
    public const string Sync = "WarSync";
}

/// <summary>
/// The one action this table puts on EFT's own item-event endpoint, for the same
/// reason every table here has one: currency moved over a static route lands in the
/// profile and leaves the stash on screen stale until the game is told to collect it.
/// The panel sends this once the cards have turned over.
/// </summary>
[Injectable(TypePriority = OnLoadOrder.Routers)]
public sealed class WarItemEventRouter(WarItemEventCallbacks callbacks)
    : ItemEventRouter([
        new ItemRouteAction<WarSyncAction>(
            WarActions.Sync,
            async (url, pmcData, body, sessionId, output, cancellationToken) =>
                await callbacks.Sync(sessionId, output)),
    ]);

/// <summary>
/// Answers the sync action. Not quite a no-op: pinging is also what gives back a stake
/// stranded by an interrupted round, and this has an output to hang that on.
/// </summary>
[Injectable]
public class WarItemEventCallbacks(WarService service, WarLog log)
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
