using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.ItemEvent;
using SPTarkov.Server.Core.Routers;
using SPTarkov.Server.Core.Utils;

namespace Casino.Server;

/// <summary>
/// HTTP adapter for the casino's own routes. Serialises what <see cref="GiftService"/>
/// decided and says the interesting half of it to the server console.
///
/// Holds no logic of its own, like each table's callbacks: everything worth checking
/// lives one layer down, where it is reachable without a running server.
/// </summary>
[Injectable]
public class CasinoCallbacks(
    HttpResponseUtil httpResponseUtil,
    GiftService service,
    EventOutputHolder eventOutputHolder,
    ICasinoLog log)
{
    /// <summary>
    /// Asked once per game launch by a client that has not yet seen the card. Moves
    /// nothing, so it is not logged -- it would be a line on every menu load for
    /// every player, saying "no" for ever after the first week.
    /// </summary>
    public ValueTask<string> GiftStatus(GiftStatusRequest info, MongoId sessionId) =>
        new(httpResponseUtil.NoBody(service.Status(sessionId)));

    /// <summary>
    /// Takes the gift. Logged either way, because this is the one route here that
    /// moves money and it happens at most once per profile.
    /// </summary>
    public ValueTask<string> GiftClaim(GiftClaimRequest info, MongoId sessionId)
    {
        var response = service.Claim(sessionId, Output(sessionId));

        if (response.Error is not null)
        {
            log.Error($"gift claim for {sessionId} failed: {response.Error}");
        }
        else if (!response.Granted)
        {
            log.Info($"gift claim for {sessionId} -- already taken, nothing paid.");
        }

        return new ValueTask<string>(httpResponseUtil.NoBody(response));
    }

    /// <summary>
    /// The response the bank writes its change records into.
    ///
    /// **From EventOutputHolder, never from new.** A hand-built one initialises
    /// nothing and the inventory helpers reach straight into
    /// <c>output.ProfileChanges[sessionId]</c>, so they throw after the items have
    /// already moved. Blackjack surfaced that as "not enough roubles" while the stake
    /// had left the stash.
    /// </summary>
    private ItemEventRouterResponse Output(MongoId sessionId) => eventOutputHolder.GetOutput(sessionId);
}
