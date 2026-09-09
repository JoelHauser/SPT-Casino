namespace Casino.Server;

/// <summary>What was paid to one profile, and when.</summary>
public record GiftClaim
{
    /// <summary>How much actually moved. Recorded rather than assumed, so a support
    /// question can be answered from the file instead of from memory.</summary>
    public int Amount { get; set; }

    /// <summary>
    /// UTC, ISO-8601. A local timestamp in a file players send in is worse than
    /// useless: it looks precise and is unanchored.
    /// </summary>
    public string ClaimedUtc { get; set; } = string.Empty;
}

/// <summary>
/// Who has been paid what, and the rule that each profile is paid once.
///
/// **No file, no SPT types, no clock of its own** -- the same split the four tables
/// keep between their `.Game` rules and their `.Server` plumbing, and for the same
/// reason: this is the part that decides whether a million roubles moves, so it has to
/// be checkable without a running server. <see cref="GiftStore"/> is this plus a file
/// and a lock, and holds no decisions of its own.
///
/// Not thread-safe by itself. Its owner serialises access -- see the note on
/// <see cref="GiftStore"/>'s gate, which is what actually makes two simultaneous
/// claims safe.
/// </summary>
public class GiftLedger
{
    /// <summary>Gift key -> session id -> what was paid.</summary>
    private readonly Dictionary<string, Dictionary<string, GiftClaim>> _claims;

    public GiftLedger(Dictionary<string, Dictionary<string, GiftClaim>>? existing = null)
    {
        _claims = existing ?? [];
    }

    /// <summary>The whole record, for whoever is writing it out.</summary>
    public Dictionary<string, Dictionary<string, GiftClaim>> Claims => _claims;

    /// <summary>Whether this profile has already taken this gift.</summary>
    public bool HasClaimed(string giftKey, string sessionId) =>
        _claims.TryGetValue(giftKey, out var bySession) && bySession.ContainsKey(sessionId);

    /// <summary>
    /// Records that this profile is taking the gift, or refuses because it already
    /// has. False means the caller must pay nothing.
    /// </summary>
    /// <param name="utcNow">
    /// Passed in rather than read here, so a test can pin it. Nothing depends on the
    /// value; it exists to be read by a person looking at the file.
    /// </param>
    public bool TryClaim(string giftKey, string sessionId, int amount, DateTime utcNow)
    {
        if (!_claims.TryGetValue(giftKey, out var bySession))
        {
            bySession = [];
            _claims[giftKey] = bySession;
        }

        if (bySession.ContainsKey(sessionId))
        {
            return false;
        }

        bySession[sessionId] = new GiftClaim
        {
            Amount = amount,
            ClaimedUtc = utcNow.ToString("O"),
        };

        return true;
    }

    /// <summary>
    /// Puts the offer back. True when there was a claim to undo.
    ///
    /// Only correct for a payment that provably moved nothing at all. Money that
    /// reached the message tab instead of the stash is still the player's and must
    /// never come through here.
    /// </summary>
    public bool Release(string giftKey, string sessionId) =>
        _claims.TryGetValue(giftKey, out var bySession) && bySession.Remove(sessionId);
}
