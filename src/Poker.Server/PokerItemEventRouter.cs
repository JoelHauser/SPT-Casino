using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Request;
using SPTarkov.Server.Core.Models.Eft.ItemEvent;

namespace Poker.Server;

/// <summary>Action names the client sends. Namespaced so they cannot collide with EFT's own.</summary>
public static class PokerActions
{
    public const string Sit = "PokerSit";

    public const string Deal = "PokerDeal";

    public const string Act = "PokerAct";

    public const string Leave = "PokerLeave";

    /// <summary>
    /// Does nothing to the game. Exists so the client has something harmless to send
    /// when it needs the profile changes SPT has been holding for it -- see
    /// <see cref="PokerItemEventCallbacks.Sync"/>.
    /// </summary>
    public const string Sync = "PokerSync";
}

/// <summary>
/// The transport the game client uses, and the reason a stash stays in step.
///
/// These arrive on the same endpoint EFT already uses for moving items, so the reply
/// carries the `ProfileChanges` the client applies to its own inventory. That is the
/// whole point: currency moved through a plain static route lands in the profile but
/// leaves the stash on screen stale until a reload, which reads to a player as the mod
/// eating their winnings.
///
/// The static routes in <see cref="PokerRouter"/> stay alongside this. They are how
/// the mod is exercised with a script and no game attached, and they discard the
/// change record because nothing is listening for it.
///
/// **4.0 shape.** A router names the actions it answers to and switches on them
/// itself, and what each body deserializes to is declared once at startup through
/// <see cref="Casino.Server.ItemEventActions"/> rather than by a type argument here.
/// </summary>
[Injectable(TypePriority = OnLoadOrder.PostDBModLoader)]
public sealed class PokerItemEventRouter(PokerItemEventCallbacks callbacks)
    : ItemEventRouterDefinition
{
    protected override List<HandledRoute> GetHandledRoutes() =>
    [
        new(PokerActions.Sit, false),
        new(PokerActions.Deal, false),
        new(PokerActions.Act, false),
        new(PokerActions.Leave, false),
        new(PokerActions.Sync, false),
    ];

    protected override async ValueTask<ItemEventRouterResponse> HandleItemEventInternal(
        string url,
        PmcData pmcData,
        BaseInteractionRequestData body,
        MongoId sessionID,
        ItemEventRouterResponse output) =>
        url switch
        {
            PokerActions.Sit => await callbacks.Sit((PokerSitAction)body, sessionID, output),
            PokerActions.Deal => await callbacks.Deal((PokerDealAction)body, sessionID, output),
            PokerActions.Act => await callbacks.Act((PokerActAction)body, sessionID, output),
            PokerActions.Leave => await callbacks.Leave((PokerLeaveAction)body, sessionID, output),
            PokerActions.Sync => await callbacks.Sync(sessionID, output),
            _ => throw new Exception($"PokerItemEventRouter cannot handle route {url}"),
        };
}
