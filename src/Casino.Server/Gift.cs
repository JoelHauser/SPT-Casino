using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Request;
using SPTarkov.Server.Core.Models.Eft.ItemEvent;
using SPTarkov.Server.Core.Models.Utils;

namespace Casino.Server;

/// <summary>
/// The one-off apology payment that ships with 1.2.6.
///
/// 1.2.0 and 1.2.1 went out with a pinned metadata token in them and drew no item
/// icons on anybody's install but the one it was read off -- see "When the obfuscator
/// empties a name" in CLAUDE.md. The mod was pulled while that was fixed. This pays
/// every profile that walks in once, and says why.
///
/// ## It is keyed, not a boolean
///
/// <see cref="Key"/> is what the store records, so a later apology (there had better
/// not be one) is a new key rather than a migration, and a player who took this one
/// is not offered it again by a mod that has forgotten which gift it was. The key is
/// deliberately not the mod version: bumping to 1.2.7 must not hand everyone another
/// million.
/// </summary>
public static class GiftOffer
{
    /// <summary>
    /// What the store writes beside a session id. Frozen -- changing it re-arms the
    /// gift for every profile that has already taken it.
    /// </summary>
    public const string Key = "1.2.6-apology";

    /// <summary>One million roubles, once.</summary>
    public const int Amount = 1_000_000;

    /// <summary>Roubles. The same template id the four tables settle in.</summary>
    public static readonly MongoId Tpl = new("5449016a4bdc2d6f028b456f");
}

/// <summary>Asks whether this profile still has the gift waiting. Nothing to send.</summary>
public record GiftStatusRequest : IRequestData;

/// <summary>Takes it. Nothing to send -- the session id is the whole request.</summary>
public record GiftClaimRequest : IRequestData;

/// <summary>
/// Does nothing to the game. Exists so the client has something harmless to send when
/// it needs the profile changes SPT has been holding for it, exactly like each table's
/// own sync action -- see <see cref="CasinoActions"/>.
/// </summary>
public record CasinoSyncAction : BaseInteractionRequestData;

/// <summary>What the client is told about the gift.</summary>
public record GiftResponse
{
    /// <summary>True when there is money waiting that this profile has not taken.</summary>
    public bool Pending { get; init; }

    /// <summary>True only on the reply to the claim that actually moved it.</summary>
    public bool Granted { get; init; }

    /// <summary>How much, so the card can say the number rather than hardcode it.</summary>
    public int Amount { get; init; } = GiftOffer.Amount;

    /// <summary>
    /// True when the stash would not take it and it went to the message tab instead.
    /// The card says so, because a player who reads "paid" and finds nothing in the
    /// stash reports it as the mod eating money.
    /// </summary>
    public bool Posted { get; init; }

    /// <summary>Set only when something went wrong the player should be told about.</summary>
    public string? Error { get; init; }
}

/// <summary>Whether the money arrived, and where.</summary>
/// <param name="Paid">False only when nothing at all moved.</param>
/// <param name="Posted">True when some or all of it went to the message tab instead.</param>
public readonly record struct GiftPayment(bool Paid, bool Posted);

/// <summary>
/// The record of who has been paid.
///
/// An interface for the reason every table's <c>IBank</c> is one: the implementation
/// reaches for SPT's concrete helpers, and depending on it directly would make the one
/// piece of logic worth checking -- that nobody is paid twice -- impossible to check
/// without a running server. SPT's DI registers a class against every interface it
/// implements, so <see cref="GiftStore"/> resolves for this with no extra wiring.
/// </summary>
public interface IGiftStore
{
    /// <summary>Whether a payment can be recorded at all. False means pay nothing.</summary>
    bool Writable { get; }

    bool HasClaimed(string giftKey, MongoId sessionId);

    /// <summary>
    /// Records the claim. **False means the caller must pay nothing** -- either
    /// somebody already has, or the record would not save.
    /// </summary>
    bool TryClaim(string giftKey, MongoId sessionId, int amount);

    /// <summary>Puts the offer back. Only ever for a payment that moved nothing.</summary>
    void Release(string giftKey, MongoId sessionId);
}

/// <summary>
/// Putting the gift in the stash.
///
/// The <see cref="ItemEventRouterResponse"/> must come from
/// <c>EventOutputHolder.GetOutput</c>, never from <c>new</c>: a hand-built one
/// initialises nothing and the inventory helpers reach straight into
/// <c>output.ProfileChanges[sessionId]</c>, so they throw after the items have already
/// moved.
/// </summary>
public interface IGiftBank
{
    GiftPayment Pay(MongoId sessionId, int amount, ItemEventRouterResponse output);
}
