using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Helpers.Profile;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Servers;

namespace Blackjack.Server;

/// <summary>
/// Thin wrapper over the two SPT services the game flow needs. Its only job is to
/// be an interface, so <see cref="BlackjackService"/> can be tested without one.
/// </summary>
[Injectable]
public class ProfileGateway(ProfileHelper profileHelper, SaveServer saveServer) : IProfileGateway
{
    public bool HasProfile(MongoId sessionId)
    {
        // GetPmcProfile throws on an empty id rather than returning null, so asking it
        // "is there a profile?" for an unresolved session raises instead of answering.
        // That turned /blackjack/ping -- the health check whose whole job is to report
        // exactly this -- into a 500, which says nothing about the cause.
        if (sessionId == MongoId.Empty())
        {
            return false;
        }

        try
        {
            return profileHelper.GetPmcProfile(sessionId) is not null;
        }
        catch
        {
            // An id that resolves to nothing is a normal answer here, not a fault.
            return false;
        }
    }

    public async Task SaveAsync(MongoId sessionId) => await saveServer.SaveProfileAsync(sessionId);
}
