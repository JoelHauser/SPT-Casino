namespace Casino.Server.Tests;

/// <summary>
/// The rule the whole gift rests on: a profile is paid once.
///
/// Pinned against <see cref="GiftLedger"/> directly, which is why that class has no
/// file and no clock in it. Everything here is the decision, with none of the plumbing
/// <see cref="GiftStore"/> wraps it in.
/// </summary>
public class GiftLedgerTests
{
    private const string Key = GiftOffer.Key;
    private const string Session = "6531f1d5b7d0c5a3f0a1b2c3";
    private const string Other = "6531f1d5b7d0c5a3f0a1b2c4";

    private static readonly DateTime When = new(2026, 9, 9, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void AFreshLedgerOwesEverybody()
    {
        var ledger = new GiftLedger();

        Assert.False(ledger.HasClaimed(Key, Session));
    }

    [Fact]
    public void TheFirstClaimSucceeds()
    {
        var ledger = new GiftLedger();

        Assert.True(ledger.TryClaim(Key, Session, GiftOffer.Amount, When));
        Assert.True(ledger.HasClaimed(Key, Session));
    }

    [Fact]
    public void TheSecondClaimIsRefused()
    {
        var ledger = new GiftLedger();
        ledger.TryClaim(Key, Session, GiftOffer.Amount, When);

        Assert.False(ledger.TryClaim(Key, Session, GiftOffer.Amount, When));
    }

    /// <summary>
    /// The card claims as it opens and a double click on the tab opens it twice, so
    /// "claimed twenty times in a row" is the shape of the real race rather than an
    /// invented one. Exactly one of them may win.
    /// </summary>
    [Fact]
    public void OnlyOneOfManyClaimsEverSucceeds()
    {
        var ledger = new GiftLedger();
        var granted = 0;

        for (var i = 0; i < 20; i++)
        {
            if (ledger.TryClaim(Key, Session, GiftOffer.Amount, When))
            {
                granted++;
            }
        }

        Assert.Equal(1, granted);
    }

    [Fact]
    public void OneProfileTakingItLeavesAnotherStillOwed()
    {
        var ledger = new GiftLedger();
        ledger.TryClaim(Key, Session, GiftOffer.Amount, When);

        Assert.False(ledger.HasClaimed(Key, Other));
        Assert.True(ledger.TryClaim(Key, Other, GiftOffer.Amount, When));
    }

    /// <summary>
    /// The key is what stops a later apology being blocked by this one, and what stops
    /// a version bump handing everybody another million. Both directions are asserted
    /// because only the second one is dangerous.
    /// </summary>
    [Fact]
    public void ADifferentGiftIsADifferentClaim()
    {
        var ledger = new GiftLedger();
        ledger.TryClaim(Key, Session, GiftOffer.Amount, When);

        Assert.False(ledger.HasClaimed("some-later-gift", Session));
        Assert.True(ledger.TryClaim("some-later-gift", Session, GiftOffer.Amount, When));

        // And taking the later one does not re-open this one.
        Assert.False(ledger.TryClaim(Key, Session, GiftOffer.Amount, When));
    }

    [Fact]
    public void ReleasingPutsTheOfferBack()
    {
        var ledger = new GiftLedger();
        ledger.TryClaim(Key, Session, GiftOffer.Amount, When);

        Assert.True(ledger.Release(Key, Session));
        Assert.False(ledger.HasClaimed(Key, Session));
        Assert.True(ledger.TryClaim(Key, Session, GiftOffer.Amount, When));
    }

    [Fact]
    public void ReleasingWhatWasNeverClaimedChangesNothing()
    {
        var ledger = new GiftLedger();

        Assert.False(ledger.Release(Key, Session));
        Assert.False(ledger.HasClaimed(Key, Session));
    }

    /// <summary>
    /// What a person opening gifts.json is meant to be able to read: how much, and
    /// when. The amount is recorded rather than assumed so a support question does not
    /// depend on remembering what 1.2.6 paid.
    /// </summary>
    [Fact]
    public void TheClaimRecordsWhatWasPaidAndWhen()
    {
        var ledger = new GiftLedger();
        ledger.TryClaim(Key, Session, GiftOffer.Amount, When);

        var claim = ledger.Claims[Key][Session];

        Assert.Equal(GiftOffer.Amount, claim.Amount);
        Assert.Equal(When.ToString("O"), claim.ClaimedUtc);
    }

    /// <summary>
    /// A restart reads the file back into a ledger, so a record loaded from disk has to
    /// refuse the claims it already knows about. Getting this wrong pays everybody
    /// again on every boot.
    /// </summary>
    [Fact]
    public void ALedgerRestoredFromDiskStillRefusesAPaidProfile()
    {
        var first = new GiftLedger();
        first.TryClaim(Key, Session, GiftOffer.Amount, When);

        var reloaded = new GiftLedger(first.Claims);

        Assert.True(reloaded.HasClaimed(Key, Session));
        Assert.False(reloaded.TryClaim(Key, Session, GiftOffer.Amount, When));
    }
}
