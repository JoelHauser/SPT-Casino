using Blackjack.Game;
using SPTarkov.Server.Core.Models.Common;

namespace Blackjack.Server.Tests;

public class MoneyInvariantTests
{
    /// <summary>
    /// Plays random legal actions for a long session and checks, after every single
    /// round, that the money the service actually moved equals the profit or loss
    /// the engine reported.
    ///
    /// This is the assertion that would have caught a double being charged twice, a
    /// split hand being staked but never collected, or a payout going out on a hand
    /// that lost -- none of which a balance check at the end would notice, because
    /// the errors can cancel.
    /// </summary>
    [Theory]
    [InlineData(3)]
    [InlineData(17)]
    [InlineData(2024)]
    public async Task MoneyMovedMatchesTheEngineNetEveryRound(int seed)
    {
        var rng = new Random(seed);
        var bank = new FakeBank();
        var profiles = new FakeProfiles();
        var tables = new TableStore();
        var session = new MongoId();

        tables.Seed(session, new BlackjackTable(new Rules(), new Random(seed)));
        bank.SetBalance(Wallet.Roubles, 100_000_000);

        var service = new BlackjackService(bank, profiles, tables, new FakeStats(), new FakeEscrow());
        var previousBalance = bank.GetBalance(session, Wallet.Roubles);

        for (var round = 0; round < 400; round++)
        {
            var debitsBefore = bank.Debits.Count;
            var creditsBefore = bank.Credits.Count;

            var response = await service.DealAsync(
                new DealRequest { Wager = 10_000, Wallet = nameof(Wallet.Roubles) },
                session);

            Assert.True(response.Ok, response.Error);

            while (response.Round!.Phase == RoundPhase.PlayerTurn)
            {
                var actions = response.Round.AvailableActions;
                var pick = actions[rng.Next(actions.Count)];

                response = await service.ActAsync(new ActionRequest { Action = pick.ToString() }, session);
                Assert.True(response.Ok, response.Error);
            }

            var view = response.Round!;
            Assert.Equal(RoundPhase.Settled, view.Phase);

            var debited = bank.Debits.Skip(debitsBefore).Sum(entry => entry.Amount);
            var credited = bank.Credits.Skip(creditsBefore).Sum(entry => entry.Amount);

            // The service must have taken exactly what the table staked and paid
            // back exactly what it returned.
            Assert.Equal(view.TotalWagered, debited);
            Assert.Equal(view.TotalReturned, credited);
            Assert.Equal(view.Net, credited - debited);

            var balance = bank.GetBalance(session, Wallet.Roubles);
            Assert.Equal(previousBalance + view.Net, balance);
            Assert.True(balance >= 0, "Balance went negative.");
            previousBalance = balance;

            // No stake may be left held once a round is over; the next deal would
            // otherwise collect it a second time.
            Assert.Equal(0, tables.For(session).Staked);
        }
    }
}
