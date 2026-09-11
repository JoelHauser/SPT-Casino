using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Request;
using SPTarkov.Server.Core.Models.Eft.ItemEvent;

namespace Blackjack.Server;

/// <summary>Action names the client sends. Namespaced so they cannot collide with EFT's own.</summary>
public static class BlackjackActions
{
    public const string Deal = "BlackjackDeal";

    public const string Play = "BlackjackPlay";

    /// <summary>
    /// Does nothing. Exists so the client has something harmless to send when it
    /// needs the profile changes SPT has been holding for it -- see
    /// <see cref="BlackjackItemEventCallbacks.Sync"/>.
    /// </summary>
    public const string Sync = "BlackjackSync";
}

/// <summary>
/// The transport the game client uses.
///
/// These arrive on the same endpoint EFT already uses for moving items, so the reply
/// carries the ProfileChanges the client applies to its own inventory. That is the
/// whole point: money moved through a plain static route lands in the profile but
/// leaves the client's stash view stale until it reloads.
///
/// The static routes in <see cref="BlackjackRouter"/> stay alongside this. They are
/// how the mod is tested with curl and no game attached, and they discard the change
/// record because nothing is listening for it.
///
/// **4.0 shape.** A router names the actions it answers to and switches on them
/// itself; the 4.1 form -- a list of `ItemRouteAction&lt;T&gt;` handed to a base
/// constructor -- does not exist here. Nor does its type argument, which is how 4.1
/// knew what to deserialize a body into, so that is declared once at startup through
/// <see cref="Casino.Server.ItemEventActions"/>. The casts below are what SPT's own
/// routers do, and they hold only because of those registrations.
/// </summary>
[Injectable(TypePriority = OnLoadOrder.PostDBModLoader)]
public sealed class BlackjackItemEventRouter(BlackjackItemEventCallbacks callbacks)
    : ItemEventRouterDefinition
{
    protected override List<HandledRoute> GetHandledRoutes() =>
    [
        new(BlackjackActions.Deal, false),
        new(BlackjackActions.Play, false),
        new(BlackjackActions.Sync, false),
    ];

    protected override async ValueTask<ItemEventRouterResponse> HandleItemEventInternal(
        string url,
        PmcData pmcData,
        BaseInteractionRequestData body,
        MongoId sessionID,
        ItemEventRouterResponse output) =>
        url switch
        {
            BlackjackActions.Deal => await callbacks.Deal((BlackjackDealAction)body, sessionID, output),
            BlackjackActions.Play => await callbacks.Play((BlackjackPlayAction)body, sessionID, output),
            // Sync alone is synchronous -- it hands back the output it was given.
            BlackjackActions.Sync => callbacks.Sync(sessionID, output),
            _ => throw new Exception($"BlackjackItemEventRouter cannot handle route {url}"),
        };
}
