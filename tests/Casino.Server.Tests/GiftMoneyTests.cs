using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.ItemEvent;

namespace Casino.Server.Tests;

/// <summary>
/// What the gift owes, and what it must never do.
///
/// The invariant is one sentence: **across any sequence of calls, a profile is paid
/// GiftOffer.Amount exactly once, or not at all.** Every test here is that sentence
/// under a different failure -- a stash that refuses the money, a record that will not
/// write, a bank that throws, the same profile asking twenty times.
///
/// The dangerous direction is paying twice, because invented roubles cannot be taken
/// back. The safe direction is paying nobody, which costs a player only something they
/// never had. Where those two conflict below, the tests pin the safe one.
/// </summary>
public class GiftMoneyTests
{
    private static readonly MongoId Session = new();
    private static readonly MongoId Other = new();

    /// <summary>
    /// Never from `new` in the server itself -- see <see cref="IGiftBank"/>. Here the
    /// bank is fake and never reads it, so a bare one is honest about that.
    /// </summary>
    private static ItemEventRouterResponse Output() => new();

    private static GiftService Service(FakeGiftStore store, FakeGiftBank bank) =>
        new(store, bank, new QuietLog());

    // ---------------------------------------------------------------- the happy path

    [Fact]
    public void AProfileThatHasNotBeenPaidIsOwedTheGift()
    {
        var service = Service(new FakeGiftStore(), new FakeGiftBank());

        Assert.True(service.Status(Session).Pending);
    }

    [Fact]
    public void ClaimingPaysTheAmountOnce()
    {
        var bank = new FakeGiftBank();
        var service = Service(new FakeGiftStore(), bank);

        var response = service.Claim(Session, Output());

        Assert.True(response.Granted);
        Assert.Null(response.Error);
        Assert.Equal(GiftOffer.Amount, bank.Paid);
        Assert.Equal(1, bank.Calls);
    }

    [Fact]
    public void AProfileThatHasBeenPaidIsNoLongerOwedIt()
    {
        var service = Service(new FakeGiftStore(), new FakeGiftBank());
        service.Claim(Session, Output());

        Assert.False(service.Status(Session).Pending);
    }

    // ------------------------------------------------------------- paying twice, not

    [Fact]
    public void ASecondClaimPaysNothingAndSaysSo()
    {
        var bank = new FakeGiftBank();
        var service = Service(new FakeGiftStore(), bank);

        service.Claim(Session, Output());
        var second = service.Claim(Session, Output());

        Assert.False(second.Granted);
        Assert.False(second.Pending);
        Assert.Equal(GiftOffer.Amount, bank.Paid);
        Assert.Equal(1, bank.Calls);
    }

    /// <summary>
    /// The card claims as it opens, and a double click on the tab opens it twice. This
    /// is the actual race, not a hypothetical one.
    /// </summary>
    [Fact]
    public void TwentyClaimsPayOneMillionAndNotTwenty()
    {
        var bank = new FakeGiftBank();
        var service = Service(new FakeGiftStore(), bank);

        for (var i = 0; i < 20; i++)
        {
            service.Claim(Session, Output());
        }

        Assert.Equal(GiftOffer.Amount, bank.Paid);
        Assert.Equal(1, bank.Calls);
    }

    [Fact]
    public void EveryProfileIsPaidOnItsOwnAccount()
    {
        var bank = new FakeGiftBank();
        var service = Service(new FakeGiftStore(), bank);

        service.Claim(Session, Output());
        service.Claim(Other, Output());
        service.Claim(Session, Output());
        service.Claim(Other, Output());

        Assert.Equal(2 * GiftOffer.Amount, bank.Paid);
        Assert.Equal(2, bank.Calls);
    }

    // -------------------------------------------------------------- when it goes wrong

    /// <summary>
    /// A stash so full that not one rouble landed and the message failed too. Nothing
    /// moved, so the offer goes back rather than the player being told they were paid
    /// and finding nothing.
    /// </summary>
    [Fact]
    public void APaymentThatMovedNothingLeavesTheGiftOnOffer()
    {
        var store = new FakeGiftStore();
        var bank = new FakeGiftBank { FailToPay = true };
        var service = Service(store, bank);

        var response = service.Claim(Session, Output());

        Assert.False(response.Granted);
        Assert.True(response.Pending);
        Assert.NotNull(response.Error);
        Assert.Equal(0, bank.Paid);
        Assert.Equal(1, store.Releases);
        Assert.True(service.Status(Session).Pending);
    }

    /// <summary>
    /// And it is genuinely still claimable afterwards -- releasing the claim without
    /// being able to take it again would be the same as losing it.
    /// </summary>
    [Fact]
    public void AGiftPutBackCanBeTakenOnTheNextTry()
    {
        var store = new FakeGiftStore();
        var bank = new FakeGiftBank { FailToPay = true };
        var service = Service(store, bank);

        service.Claim(Session, Output());

        bank.FailToPay = false;
        var second = service.Claim(Session, Output());

        Assert.True(second.Granted);
        Assert.Equal(GiftOffer.Amount, bank.Paid);
    }

    [Fact]
    public void ABankThatThrowsLeavesTheGiftOnOffer()
    {
        var store = new FakeGiftStore();
        var service = Service(store, new FakeGiftBank { Throw = true });

        var response = service.Claim(Session, Output());

        Assert.False(response.Granted);
        Assert.True(response.Pending);
        Assert.Equal(1, store.Releases);
        Assert.True(service.Status(Session).Pending);
    }

    /// <summary>
    /// Money in the message tab is the player's money. It must never come back on
    /// offer, or the mail and a second payment together are worth two million.
    /// </summary>
    [Fact]
    public void MoneyPostedToTheMessageTabCountsAsPaid()
    {
        var store = new FakeGiftStore();
        var bank = new FakeGiftBank { PostInstead = true };
        var service = Service(store, bank);

        var response = service.Claim(Session, Output());

        Assert.True(response.Granted);
        Assert.True(response.Posted);
        Assert.Equal(0, store.Releases);
        Assert.False(service.Status(Session).Pending);

        // And asking again pays nothing more.
        service.Claim(Session, Output());
        Assert.Equal(GiftOffer.Amount, bank.Paid);
    }

    // ------------------------------------------------------- a record that cannot save

    /// <summary>
    /// A read-only mod folder. Paying without being able to record it would pay again
    /// on every open for ever, so nothing is paid at all -- and the player is not
    /// shown a card offering something that will not arrive.
    /// </summary>
    [Fact]
    public void NothingIsPaidWhenTheRecordCannotBeWritten()
    {
        var bank = new FakeGiftBank();
        var service = Service(new FakeGiftStore(writable: false), bank);

        Assert.False(service.Status(Session).Pending);

        var response = service.Claim(Session, Output());

        Assert.False(response.Granted);
        Assert.False(response.Pending);
        Assert.NotNull(response.Error);
        Assert.Equal(0, bank.Paid);
        Assert.Equal(0, bank.Calls);
    }

    /// <summary>
    /// The write failing on the claim itself, rather than being known bad up front.
    /// Same rule: no record, no money.
    /// </summary>
    [Fact]
    public void NothingIsPaidWhenTheClaimFailsToRecord()
    {
        var bank = new FakeGiftBank();
        var store = new FakeGiftStore { RefuseClaims = true };
        var service = Service(store, bank);

        var response = service.Claim(Session, Output());

        Assert.False(response.Granted);
        Assert.Equal(0, bank.Paid);
        Assert.Equal(0, bank.Calls);
    }

    // --------------------------------------------------------------- what it is worth

    /// <summary>
    /// The number the mod page promises. Pinned so a stray edit to the constant is a
    /// failing test rather than a surprise on somebody's stash.
    /// </summary>
    [Fact]
    public void TheGiftIsOneMillionRoubles()
    {
        Assert.Equal(1_000_000, GiftOffer.Amount);
        Assert.Equal("5449016a4bdc2d6f028b456f", GiftOffer.Tpl.ToString());
    }

    /// <summary>
    /// The response carries the amount so the card can print the number instead of
    /// hardcoding a second copy of it.
    /// </summary>
    [Fact]
    public void TheReplyCarriesTheAmountForTheCardToPrint()
    {
        var service = Service(new FakeGiftStore(), new FakeGiftBank());

        Assert.Equal(GiftOffer.Amount, service.Claim(Session, Output()).Amount);
    }
}
