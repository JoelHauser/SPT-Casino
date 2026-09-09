using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.ItemEvent;

namespace Casino.Server;

/// <summary>
/// Decides whether the gift is owed, and pays it exactly once.
///
/// The client is told what is pending and is never believed about what was paid --
/// <see cref="GiftStore"/> is the only record that counts. A player who deletes the
/// plugin's own <c>seen.txt</c> gets the welcome card again, which costs nothing; the
/// same trick against this one would be worth a million roubles a go, so the flag it
/// reads lives on the server.
///
/// ## The order, which is the whole design
///
/// Claim first, pay second. Marking the gift taken before the money moves means the
/// worst case of a crash in the middle is a player who is owed a million and did not
/// get it -- recoverable, logged, and visible in <c>gifts.json</c>. The other order
/// makes the worst case a player who can mint roubles by pulling the plug at the right
/// moment, which is not recoverable at all.
/// </summary>
[Injectable]
public class GiftService(IGiftStore store, IGiftBank bank, ICasinoLog log)
{
    /// <summary>
    /// Whether this profile still has the gift waiting. Moves no money and is safe to
    /// ask as often as the client likes.
    /// </summary>
    public GiftResponse Status(MongoId sessionId)
    {
        if (!store.Writable)
        {
            // Silent to the player rather than an error on the card. The gift is a
            // bonus; a profile that never learns it existed has lost nothing it had,
            // and the reason is already sitting in the server console.
            return new GiftResponse { Pending = false };
        }

        return new GiftResponse { Pending = !store.HasClaimed(GiftOffer.Key, sessionId) };
    }

    /// <summary>
    /// Takes the gift, or explains why there is nothing to take.
    ///
    /// Safe to call twice: the second call finds the claim already recorded and pays
    /// nothing. That is not a theoretical concern -- the card claims as it opens, and
    /// a double click on the tab opens it twice.
    /// </summary>
    public GiftResponse Claim(MongoId sessionId, ItemEventRouterResponse output)
    {
        if (!store.Writable)
        {
            return new GiftResponse
            {
                Pending = false,
                Error = "The casino could not record the gift, so it has not been paid.",
            };
        }

        if (!store.TryClaim(GiftOffer.Key, sessionId, GiftOffer.Amount))
        {
            // Either somebody already took it, or the record would not write. Both
            // mean the same thing to the player and neither is worth two messages.
            return new GiftResponse { Pending = false };
        }

        GiftPayment payment;

        try
        {
            payment = bank.Pay(sessionId, GiftOffer.Amount, output);
        }
        catch (Exception ex)
        {
            // The bank handles its own failures and this should not be reachable, so
            // if it is, something further down changed. Put the offer back -- an
            // exception out of Pay is the one case where nothing provably moved.
            store.Release(GiftOffer.Key, sessionId);
            log.Error($"the gift payment threw, so it stays on offer -- {ex.Message}");

            return new GiftResponse
            {
                Pending = true,
                Error = "Something went wrong paying the gift. It is still waiting for you.",
            };
        }

        if (!payment.Paid)
        {
            // Nothing landed in the stash and nothing reached the message tab either.
            // The offer goes back, because the alternative is a player who was told
            // they were paid and has nothing.
            store.Release(GiftOffer.Key, sessionId);

            return new GiftResponse
            {
                Pending = true,
                Error = "The gift could not be paid into your stash. It is still waiting for you.",
            };
        }

        return new GiftResponse { Granted = true, Posted = payment.Posted };
    }
}
