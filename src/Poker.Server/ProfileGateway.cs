using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Helpers.Profile;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Servers;

namespace Poker.Server;

/// <summary>
/// Thin wrapper over the SPT services the game flow needs. Its only job is to be an
/// interface, so the service can be tested without a running server.
/// </summary>
[Injectable]
public class ProfileGateway(ProfileHelper profileHelper, SaveServer saveServer) : IProfileGateway
{
    /// <summary>Money that is not flushed to disk did not move.</summary>
    public async Task SaveAsync(MongoId sessionId) => await saveServer.SaveProfileAsync(sessionId);

    public bool HasProfile(MongoId sessionId)
    {
        // GetPmcProfile throws on an empty id rather than returning null, so asking it
        // "is there a profile?" for an unresolved session raises instead of answering.
        // On Blackjack that turned the health check -- whose entire job is to report
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
}
