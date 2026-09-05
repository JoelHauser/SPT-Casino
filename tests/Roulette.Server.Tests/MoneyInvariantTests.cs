using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.ItemEvent;

namespace Roulette.Server.Tests;

/// <summary>
/// What the table is allowed to do to a player's money.
///
/// **Written before the settlement it checks**, which is the instruction this repo has
/// carried since before Roulette had a server. Poker's notes are blunt about why: an
/// end-of-run balance check misses errors that cancel, and a settlement written first
/// gets tests shaped around what it already does rather than around what it owes.
///
/// ## The model these pin
///
/// Roulette is neither of its siblings. Blackjack stakes once per hand; Poker takes a
/// buy-in and hands back a live stack. Here **the cloth is intent and the spin is the
/// transaction**: chips go on and come off freely, moving nothing, and when the wheel
/// turns the whole stake leaves the wallet in one debit and the whole return arrives
/// in one credit.
///
/// That is a deliberate choice over debiting each chip as it is placed. A right-click
/// would then have to credit back, a cloth of 150 bets would be 150 item events, and
/// the window in which the player's money exists nowhere would be as long as they
/// took to decide. This way it is the length of one function call.
/// </summary>
public class MoneyInvariantTests
{
    private static readonly MongoId Session = new("6a9b474574813708e8fc3ce5");

    private const int Rich = 500_000_000;

    /// <summary>
    /// The invariant, and the only one that matters: **the wallet moved by exactly
    /// what the table said it did.**
    ///
    /// Measured from every individual movement rather than from the closing balance,
    /// because a debit that is one short and a credit that is one over net to a
    /// balance that looks right.
    /// </summary>
    [Fact]
    public async Task TheWalletMovesByTheReturnLessTheStakeAndByNothingElse()
    {
        var (service, bank, _, _) = Table();
        bank.Seed(Wallet.Roubles, Rich);

        var expected = 0;

        for (var round = 0; round < 40; round++)
        {
            Place(service, "Straight", round % 37, 10_000);
            Place(service, "Red", 0, 20_000);
            Place(service, "Dozen", 1 + (round % 3), 30_000);

            var spin = await service.SpinAsync(Session, Output());
            var last = spin.Table!.Last!;

            Assert.Equal(60_000, last.Staked);
            expected += last.Returned - last.Staked;

            // Re-opens betting for the next round, as pressing spin again does.
            await service.SpinAsync(Session, Output());
        }

        Assert.Equal(expected, bank.Moved);
        Assert.Equal(Rich + expected, bank.GetBalance(Session, Wallet.Roubles));
    }

    /// <summary>
    /// Chips on the cloth are intent. Until the wheel turns, the stash is untouched --
    /// which is what makes a right-click free and a cleared cloth cost nothing.
    /// </summary>
    [Fact]
    public void PlacingAndLiftingChipsMoveNoMoney()
    {
        var (service, bank, _, escrow) = Table();
        bank.Seed(Wallet.Roubles, Rich);

        Place(service, "Straight", 7, 100_000);
        Place(service, "Straight", 7, 100_000);
        Place(service, "Red", 0, 50_000);
        service.Remove(new RemoveRequest { Kind = "Straight", Selection = 7, Amount = 100_000 }, Session);
        service.Clear(Session);

        Assert.Empty(bank.Movements);
        Assert.Equal(0, bank.Debits);
        Assert.Equal(0, bank.Credits);
        Assert.Null(escrow.Get(Session));
    }

    /// <summary>One spin, one debit, one credit. Never two of either.</summary>
    [Fact]
    public async Task ASpinTakesTheStakeOnceAndPaysTheReturnOnce()
    {
        var (service, bank, _, _) = Table();
        bank.Seed(Wallet.Roubles, Rich);

        Place(service, "Straight", 17, 50_000);
        var spin = await service.SpinAsync(Session, Output());

        Assert.Equal(1, bank.Debits);
        Assert.Equal(50_000, -bank.Movements[0].Amount);

        // A winner is paid once; a loser is paid not at all. Either way, never twice.
        Assert.True(bank.Credits <= 1);
        Assert.Equal(spin.Table!.Last!.Returned > 0 ? 1 : 0, bank.Credits);
    }

    /// <summary>
    /// Pressing spin on a settled table opens the next one. It must not re-run the
    /// last one's money -- the most obvious way to pay a winner twice.
    /// </summary>
    [Fact]
    public async Task ReopeningBettingAfterASpinMovesNothing()
    {
        var (service, bank, _, _) = Table();
        bank.Seed(Wallet.Roubles, Rich);

        Place(service, "Straight", 17, 50_000);
        await service.SpinAsync(Session, Output());

        var moves = bank.Movements.Count;
        await service.SpinAsync(Session, Output());
        await service.SpinAsync(Session, Output());

        Assert.Equal(moves, bank.Movements.Count);
    }

    /// <summary>
    /// A stake the wallet cannot cover takes nothing at all, and the wheel does not
    /// turn. Half of it going and the spin failing is the shape of the bug this pins.
    ///
    /// The cloth is built while the player can afford it and the money goes elsewhere
    /// before they spin, which is not contrived: the panel sits over a live game, and
    /// nothing stops somebody laying out chips, buying a gun on the flea market and
    /// coming back to the wheel. The place-time check cannot see that coming, which is
    /// exactly why the spin checks again.
    /// </summary>
    [Fact]
    public async Task ASpinThatCannotBeAffordedTakesNothingAndDoesNotTurn()
    {
        var (service, bank, _, escrow) = Table();
        bank.Seed(Wallet.Roubles, 80_000);

        Place(service, "Straight", 7, 50_000);

        // Spent elsewhere between laying the chips down and spinning.
        bank.Seed(Wallet.Roubles, 30_000);

        var spin = await service.SpinAsync(Session, Output());

        Assert.False(spin.Ok);
        Assert.Empty(bank.Movements);
        Assert.Null(escrow.Get(Session));
        Assert.Equal(30_000, bank.GetBalance(Session, Wallet.Roubles));

        // And the bets are still there to be spun once the player can afford them.
        Assert.Equal(50_000, spin.Table!.Staked);
    }

    /// <summary>
    /// A chip that could never be covered is refused as it goes down, rather than at
    /// the wheel. Letting a player lay out a cloth and only then telling them would be
    /// correct and horrible. It is a read: nothing moves either way.
    /// </summary>
    [Fact]
    public void AChipTheWalletCannotCoverIsRefusedWhenItIsPlaced()
    {
        var (service, bank, _, _) = Table();
        bank.Seed(Wallet.Roubles, 60_000);

        Place(service, "Straight", 7, 50_000);

        var refused = service.Place(
            new PlaceRequest { Kind = "Black", Selection = 0, Amount = 50_000 }, Session);

        Assert.False(refused.Ok);
        Assert.Empty(bank.Movements);

        // The cloth is untouched: the refused chip did not land and the first one stays.
        Assert.Equal(50_000, refused.Table!.Staked);
    }

    /// <summary>
    /// An empty cloth is not a free spin. Nothing moves and nothing turns.
    ///
    /// The message is asserted as well as the money, and that is not padding. Mutation
    /// testing found that deleting the empty-cloth guard entirely still moves nothing,
    /// because the bank refuses a debit of zero on its own -- so the money assertions
    /// alone could not tell the guard was gone. What changes is what the player is
    /// told: "You need 0 to spin that and you have 500,000,000" instead of the truth.
    /// </summary>
    [Fact]
    public async Task NothingOnTheClothMovesNothingAndSaysSo()
    {
        var (service, bank, _, escrow) = Table();
        bank.Seed(Wallet.Roubles, Rich);

        var spin = await service.SpinAsync(Session, Output());

        Assert.False(spin.Ok);
        Assert.Empty(bank.Movements);
        Assert.Null(escrow.Get(Session));
        Assert.Equal("Nothing is on the cloth.", spin.Error);
    }

    /// <summary>
    /// Escrow is written before the money is taken and released only after it is paid
    /// back. That order is the whole point of it: a crash anywhere in between leaves a
    /// record of a stake the player is owed.
    /// </summary>
    [Fact]
    public async Task EscrowHoldsTheStakeAcrossTheSpinAndIsReleasedAfterwards()
    {
        var (service, bank, _, escrow) = Table();
        bank.Seed(Wallet.Roubles, Rich);

        Place(service, "Straight", 7, 40_000);
        Place(service, "Black", 0, 20_000);
        await service.SpinAsync(Session, Output());

        Assert.Equal([60_000], escrow.Recorded);
        Assert.Equal(1, escrow.Releases);
        Assert.Null(escrow.Get(Session));
    }

    /// <summary>
    /// A losing spin pays nothing, and still has to release what it was holding.
    /// Escrow left behind on a loss is money the mod thinks it owes forever, and the
    /// next session hands it over.
    /// </summary>
    [Fact]
    public async Task ALosingSpinPaysNothingAndStillReleasesEscrow()
    {
        var (service, bank, _, escrow) = Table();
        bank.Seed(Wallet.Roubles, Rich);

        // A single number loses 36 times in 37, so a loss arrives almost at once.
        // Asserted rather than assumed: the loop fails the test if one never comes.
        var lost = false;

        for (var attempt = 0; attempt < 200 && !lost; attempt++)
        {
            Place(service, "Straight", 7, 10_000);
            var spin = await service.SpinAsync(Session, Output());
            lost = spin.Table!.Last!.Returned == 0;
            await service.SpinAsync(Session, Output());
        }

        Assert.True(lost, "200 straight-up bets without a single loss is not roulette.");
        Assert.Null(escrow.Get(Session));
        Assert.Equal(escrow.Recorded.Count, escrow.Releases);
    }

    /// <summary>
    /// A stake stranded by a crash is given back on the next contact, and given back
    /// **once**. Refunding it twice is worse than not refunding it at all, because
    /// nobody reports it.
    /// </summary>
    [Fact]
    public async Task AStakeStrandedByACrashIsGivenBackExactlyOnce()
    {
        var (service, bank, _, escrow) = Table();
        bank.Seed(Wallet.Roubles, 0);
        escrow.Strand(Session, Wallet.Roubles, 75_000);

        var first = await service.StateAsync(Session, Output());

        Assert.True(first.Ok);
        Assert.Equal(75_000, bank.GetBalance(Session, Wallet.Roubles));
        Assert.Null(escrow.Get(Session));

        await service.StateAsync(Session, Output());
        await service.StateAsync(Session, Output());

        Assert.Equal(75_000, bank.GetBalance(Session, Wallet.Roubles));
        Assert.Equal(1, bank.Credits);
    }

    /// <summary>Money that is not flushed to disk did not move.</summary>
    [Fact]
    public async Task EveryMoveIsFlushedToDisk()
    {
        var (service, bank, profiles, _) = Table();
        bank.Seed(Wallet.Roubles, Rich);

        Place(service, "Straight", 7, 10_000);
        await service.SpinAsync(Session, Output());

        Assert.True(profiles.Saves >= 1, "a spin moved money and never saved it.");
    }

    /// <summary>
    /// Leaving mid-spin cannot be a way to keep the stake. Nothing is owed once a
    /// spin has settled, so walking away moves nothing.
    /// </summary>
    [Fact]
    public async Task LeavingAfterASettledSpinOwesNothing()
    {
        var (service, bank, _, escrow) = Table();
        bank.Seed(Wallet.Roubles, Rich);

        Place(service, "Straight", 7, 10_000);
        await service.SpinAsync(Session, Output());

        var moves = bank.Movements.Count;
        await service.LeaveAsync(Session, Output());

        Assert.Equal(moves, bank.Movements.Count);
        Assert.Null(escrow.Get(Session));
    }

    /// <summary>
    /// Chips left on the cloth when the player walks away were never taken, so there
    /// is nothing to give back -- and giving something back would be minting money.
    /// </summary>
    [Fact]
    public async Task LeavingWithChipsStillOnTheClothRefundsNothing()
    {
        var (service, bank, _, _) = Table();
        bank.Seed(Wallet.Roubles, Rich);

        Place(service, "Straight", 7, 250_000);
        await service.LeaveAsync(Session, Output());

        Assert.Empty(bank.Movements);
        Assert.Equal(Rich, bank.GetBalance(Session, Wallet.Roubles));
    }

    // ------------------------------------------------------------------ helpers

    private static (RouletteService Service, FakeBank Bank, FakeProfiles Profiles, FakeEscrow Escrow) Table()
    {
        var bank = new FakeBank();
        var profiles = new FakeProfiles();
        var escrow = new FakeEscrow();

        var service = new RouletteService(
            bank, profiles, escrow, new TableStore(), new FakeRandom(20260905), new QuietLog());

        return (service, bank, profiles, escrow);
    }

    private static void Place(RouletteService service, string kind, int selection, int amount)
    {
        var reply = service.Place(
            new PlaceRequest { Kind = kind, Selection = selection, Amount = amount }, Session);

        Assert.True(reply.Ok, reply.Error);
    }

    /// <summary>
    /// A real one comes from `EventOutputHolder.GetOutput`. The fake bank never reads
    /// it, so a bare instance is honest here in a way it would not be in the server.
    /// </summary>
    private static ItemEventRouterResponse Output() => new();
}
