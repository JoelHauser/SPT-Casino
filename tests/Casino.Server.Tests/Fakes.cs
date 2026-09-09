using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.ItemEvent;

namespace Casino.Server.Tests;

/// <summary>
/// A gift record with no file under it.
///
/// Wraps the real <see cref="GiftLedger"/> rather than reimplementing it, so the
/// once-only rule these tests lean on is the same code the server runs. What is faked
/// is the disk and the lock -- the two things <see cref="GiftStore"/> adds.
/// </summary>
public sealed class FakeGiftStore(bool writable = true) : IGiftStore
{
    private readonly GiftLedger _ledger = new();

    public bool Writable { get; set; } = writable;

    /// <summary>Makes the record refuse to save, the way a read-only folder does.</summary>
    public bool RefuseClaims { get; set; }

    public int Claims { get; private set; }

    public int Releases { get; private set; }

    public bool HasClaimed(string giftKey, MongoId sessionId) =>
        _ledger.HasClaimed(giftKey, sessionId.ToString());

    public bool TryClaim(string giftKey, MongoId sessionId, int amount)
    {
        if (RefuseClaims)
        {
            return false;
        }

        if (!_ledger.TryClaim(giftKey, sessionId.ToString(), amount, DateTime.UtcNow))
        {
            return false;
        }

        Claims++;
        return true;
    }

    public void Release(string giftKey, MongoId sessionId)
    {
        if (_ledger.Release(giftKey, sessionId.ToString()))
        {
            Releases++;
        }
    }
}

/// <summary>
/// A bank that counts what it was asked to pay instead of touching a profile.
///
/// The total is what the money tests assert on: the point of the gift is that this
/// number is one million per profile and never two.
/// </summary>
public sealed class FakeGiftBank : IGiftBank
{
    public int Paid { get; private set; }

    public int Calls { get; private set; }

    /// <summary>Report that nothing moved, the way a stash that refused every stack does.</summary>
    public bool FailToPay { get; set; }

    /// <summary>Report that it went to the message tab instead of the stash.</summary>
    public bool PostInstead { get; set; }

    /// <summary>Throw, which is the one case the service may put the offer back for.</summary>
    public bool Throw { get; set; }

    public GiftPayment Pay(MongoId sessionId, int amount, ItemEventRouterResponse output)
    {
        Calls++;

        if (Throw)
        {
            throw new InvalidOperationException("the stash exploded");
        }

        if (FailToPay)
        {
            return new GiftPayment(false, false);
        }

        Paid += amount;
        return new GiftPayment(true, PostInstead);
    }
}

/// <summary>Says nothing, so a failing test's output is the assertion and not the log.</summary>
public sealed class QuietLog : ICasinoLog
{
    public void Info(string message)
    {
    }

    public void Error(string message)
    {
    }
}
