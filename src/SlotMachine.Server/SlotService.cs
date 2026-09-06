using SlotMachine.Game;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.ItemEvent;

namespace SlotMachine.Server;

/// <summary>
/// The whole server-side game: check what was asked for, let the machine decide, hand
/// back what it paid.
///
/// **There is no state between pulls.** No table to sit at, no chips built up, nothing
/// to abandon -- which is why there is no store here and why this is the shortest of
/// the four. One pull is one transaction: the stake leaves, the reels settle, what they
/// paid arrives.
///
/// Depends only on <see cref="IBank"/>, <see cref="IProfileGateway"/>,
/// <see cref="IEscrowStore"/> and <see cref="IRandomSource"/>, so it runs -- and is
/// tested -- with no SPT server present.
/// </summary>
[Injectable]
public class SlotService(
    IBank bank,
    IProfileGateway profiles,
    IEscrowStore escrow,
    IRandomSource random,
    ISlotLog log)
{
    private readonly Machine _machine = new(random.Create());

    /// <summary>
    /// Cheap health check, and where the panel gets the machine's own numbers.
    ///
    /// The paytable, the limits and the return all travel from here rather than being
    /// written into the client, so the panel cannot advertise a payout the machine does
    /// not give.
    /// </summary>
    public PingResponse Ping(MongoId sessionId, ItemEventRouterResponse output)
    {
        var known = profiles.HasProfile(sessionId);
        var refunded = RefundStranded(sessionId, output).GetAwaiter().GetResult();

        return new PingResponse
        {
            ModVersion = TableInfo.Version,
            SessionId = sessionId.ToString(),
            HasProfile = known,
            Note = refunded,
            Ways = (int)Math.Pow(Reels.Rows, Reels.Count),
            ReturnToPlayer = Odds.ReturnToPlayer(),

            Balances = known
                ? Enum.GetValues<Wallet>().ToDictionary(w => w.ToString(), w => bank.GetBalance(sessionId, w))
                : [],

            Limits = WalletInfo.All.ToDictionary(
                w => w.Wallet.ToString(),
                w => new StakeLimits { Min = w.MinStake, Max = w.MaxStake, Step = w.Step, Sign = w.Sign }),

            Paytable = Paytable.Symbols.ToDictionary(
                s => s.ToString(),
                s => (IReadOnlyList<int>)[Paytable.Of(s, 3), Paytable.Of(s, 4), Paytable.Of(s, 5)]),
        };
    }

    /// <summary>
    /// Pulls the handle, and the only place in this table where money moves.
    ///
    /// The order is the whole of it, and it is the one the other three arrived at the
    /// hard way:
    ///
    /// 1. **Check first.** An unknown currency, a stake the wallet does not take, a
    ///    balance that will not cover it -- all refused before anything is recorded.
    /// 2. **Record the stake in escrow**, before it is taken. A crash after this and
    ///    before the credit leaves a record of money the player is owed. The other way
    ///    round leaves a window where the stake is gone and nothing says so.
    /// 3. **Debit.** If it fails, release the escrow and refuse: nothing has moved.
    /// 4. **Settle**, which is the reels landing and the paytable being read.
    /// 5. **Credit what it paid**, then release the escrow. That order again: a crash
    ///    between them refunds a stake that was also paid out, which is the safe way
    ///    round. The other pays nothing and forgets it was owed.
    /// 6. **Save.** Money that is not flushed to disk did not move.
    /// </summary>
    public async Task<SlotResponse> PullAsync(
        PullRequest request, MongoId sessionId, ItemEventRouterResponse output)
    {
        if (!profiles.HasProfile(sessionId))
        {
            return SlotResponse.Failed("No PMC profile for this session.");
        }

        // Refused by name rather than defaulting. Enum.TryParse on an unknown string
        // leaves the value at zero, which here is Roubles -- so a typo would spend a
        // currency the player never chose.
        if (!Enum.TryParse<Wallet>(request.Wallet, ignoreCase: true, out var wallet))
        {
            return SlotResponse.Failed($"There is nothing called '{request.Wallet}' to play with.");
        }

        var info = WalletInfo.For(wallet);
        var refunded = await RefundStranded(sessionId, output);

        if (!WalletInfo.Allows(wallet, request.Stake))
        {
            return new SlotResponse
            {
                Ok = false,
                Note = refunded,
                Error = $"A pull in {info.Label} costs {info.MinStake:N0} to {info.MaxStake:N0}, "
                    + $"in steps of {info.Step:N0}.",
            };
        }

        var stake = (int)request.Stake;

        // 2. Recorded before it is taken.
        escrow.Record(sessionId, wallet, stake);

        // 3. Taken. A refusal here has touched nothing.
        if (!bank.TryDebit(sessionId, wallet, stake, output))
        {
            escrow.Release(sessionId);

            var balance = bank.GetBalance(sessionId, wallet);
            log.Info($"pull refused [{sessionId}] -- {stake:N0} {wallet}, {balance:N0} held");

            return new SlotResponse
            {
                Ok = false,
                Note = refunded,
                Error = $"That pull costs {stake:N0} {info.Label} and you have {balance:N0}.",
            };
        }

        // 4. Decided here and nowhere else. What the client does with it is
        // presentation: it is handed the stops and animates towards them.
        var pull = _machine.Pull(stake);

        // 5. Paid, then released. Never the other way round.
        if (pull.Paid > 0)
        {
            bank.Credit(sessionId, wallet, (int)pull.Paid, output);
        }

        escrow.Release(sessionId);

        // 6. On disk, or it did not happen.
        await profiles.SaveAsync(sessionId);

        if (pull.Paid > 0)
        {
            var best = pull.Wins[0];
            log.Info(
                $"paid {pull.Paid:N0} {wallet} [{sessionId}] -- {best.Reels} {best.Symbol} "
                + $"on {best.Ways} way(s), {stake:N0} staked");
        }
        else
        {
            log.Detail($"nothing [{sessionId}] -- {stake:N0} {wallet}");
        }

        return new SlotResponse { Note = refunded, Pull = View(pull) };
    }

    /// <summary>
    /// Gives back a stake left behind by a pull that never finished.
    ///
    /// The only way to hold a record here is for the server to have died between the
    /// debit and the credit, so what it holds is money the player paid for a pull they
    /// never saw the end of. It goes back in full, and at most once -- refunding twice
    /// is worse than not refunding at all, because nobody reports it.
    /// </summary>
    private async Task<string?> RefundStranded(MongoId sessionId, ItemEventRouterResponse output)
    {
        var owed = escrow.Get(sessionId);

        if (owed is null || owed.Amount <= 0)
        {
            // A zero record is still a record, and leaving it would keep this path
            // running on every request for the rest of the session.
            if (owed is not null)
            {
                escrow.Release(sessionId);
            }

            return null;
        }

        var wallet = Enum.TryParse<Wallet>(owed.Wallet, ignoreCase: true, out var parsed)
            ? parsed
            : Wallet.Roubles;

        bank.Credit(sessionId, wallet, owed.Amount, output);
        escrow.Release(sessionId);
        await profiles.SaveAsync(sessionId);

        log.Info($"gave back {owed.Amount:N0} {wallet} from a pull that never finished [{sessionId}]");

        return $"A pull was interrupted before it paid out. {owed.Amount:N0} has been returned.";
    }

    private static PullView View(Pull pull) => new()
    {
        Stops = pull.Stops,
        Grid = [.. pull.Grid.Select(IReadOnlyList<string> (reel) => [.. reel.Select(s => s.ToString())])],
        Staked = pull.Staked,
        Paid = pull.Paid,
        Profit = pull.Profit,
        Wins =
        [
            .. pull.Wins.Select(w => new WinView
            {
                Symbol = w.Symbol.ToString(),
                Reels = w.Reels,
                Ways = w.Ways,
                Paid = w.Paid,
            }),
        ],
    };
}
