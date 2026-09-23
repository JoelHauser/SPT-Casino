using System.Collections.Concurrent;
using SPTarkov.Server.Core.Models.Common;

namespace Casino.Server;

/// <summary>
/// One request at a time per player, across every table.
///
/// SPT serves requests concurrently and nothing in it serialises them per profile, so
/// two casino requests from the same session run side by side against the same
/// <c>Inventory.Items</c> -- a plain <c>List</c>, which is not safe to add to from two
/// threads. On 2026-09-22 that corrupted two players' profiles: AUTO on the slots sent
/// the next pull while the previous one's sync was still being answered, both credited
/// the stash at once, and the list came out with a null in it. SPT's own launcher then
/// threw on that null listing profiles, which locked **every** player on the server
/// out, not just the one playing.
///
/// It also closes a double-pay. Every table refunds a "stranded" stake on ping or sync
/// by reading escrow, and escrow holds a record for the whole of a live pull. A ping
/// landing mid-pull saw that record, took it for a crash, and paid the stake back while
/// the pull was still going to pay out too. Behind this gate the ping waits, the pull
/// releases its escrow, and there is nothing to refund.
///
/// Static, so a slot pull and a race slip for the same player cannot overlap either --
/// they share the stash. **Not re-entrant**: take it only at a service's public entry
/// point, never in a helper that another gated method calls.
/// </summary>
public static class SessionGate
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> Gates = new();

    public static async Task<Held> EnterAsync(MongoId sessionId)
    {
        var gate = For(sessionId);
        await gate.WaitAsync();
        return new Held(gate);
    }

    /// <summary>For the synchronous entry points -- Ping and its siblings.</summary>
    public static Held Enter(MongoId sessionId)
    {
        var gate = For(sessionId);
        gate.Wait();
        return new Held(gate);
    }

    private static SemaphoreSlim For(MongoId sessionId) =>
        Gates.GetOrAdd(sessionId.ToString(), _ => new SemaphoreSlim(1, 1));

    public readonly struct Held(SemaphoreSlim gate) : IDisposable
    {
        public void Dispose() => gate.Release();
    }
}
