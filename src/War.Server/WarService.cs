using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.ItemEvent;
using War.Game;

namespace War.Server;

/// <summary>
/// The whole server-side game: check what was asked for, let the table decide, move
/// money to match.
///
/// The slot machine's order, which all the tables here arrived at:
///
/// 1. **Check first.** Nothing is recorded on a refusal.
/// 2. **Record in escrow, before the debit.** A crash after this leaves a record of
///    money owed; the other order leaves a window where the stake is gone and nothing
///    says so.
/// 3. **Debit.** A failure puts the escrow back and refuses: nothing moved.
/// 4. **Settle** -- the cards.
/// 5. **Credit, then release.** A crash between them refunds a stake that was also
///    paid, which is the safe way round.
/// 6. **Save.** Money not flushed to disk did not move.
///
/// What War adds is a round that stops in the middle. A tie settles the tie bet,
/// leaves the ante on the table and waits; the escrow is rewritten to say so, and the
/// next request -- war or surrender -- picks it up from there. See
/// <see cref="EscrowStore"/> for what is recorded when.
///
/// Depends only on interfaces and the in-memory <see cref="TableStore"/>, so it runs --
/// and is tested -- with no SPT server present.
/// </summary>
[Injectable]
public class WarService(
    IBank bank,
    IProfileGateway profiles,
    IEscrowStore escrow,
    TableStore tables,
    IStatsStore stats,
    IWarLog log)
{
    /// <summary>
    /// Cheap health check, and where the panel gets the table's own numbers. Also gives
    /// back a stake stranded by a round the server lost, which is why it takes an output.
    /// </summary>
    public PingResponse Ping(MongoId sessionId, ItemEventRouterResponse output)
    {
        // The WarSync item event lands here too, and it refunds from escrow -- so it must
        // never run beside a deal that is holding a live stake. See SessionGate.
        using var gate = Casino.Server.SessionGate.Enter(sessionId);

        var known = profiles.HasProfile(sessionId);
        var refunded = known ? RefundStranded(sessionId, output).GetAwaiter().GetResult() : null;

        return new PingResponse
        {
            ModVersion = TableInfo.Version,
            SessionId = sessionId.ToString(),
            HasProfile = known,
            Note = refunded,
            AbsoluteMax = WalletInfo.AbsoluteMax,
            Decks = Rules.Decks,
            TiePays = Rules.TiePays,
            HouseEdgeWar = Odds.HouseEdgePercent(),
            HouseEdgeSurrender = -Odds.MainBetAlwaysSurrender() * 100d,
            HouseEdgeTieBet = -Odds.TieBet() * 100d,

            Balances = known
                ? Enum.GetValues<Wallet>().ToDictionary(w => w.ToString(), w => bank.GetBalance(sessionId, w))
                : [],

            // Not gated on the profile: the limits belong to the table, and a panel that
            // cannot read them has no way to keep a bet legal before sending it.
            Limits = WalletInfo.All.ToDictionary(
                w => w.Wallet.ToString(),
                w => new BetLimits { Min = w.MinBet, Max = w.MaxBet, Step = w.Step, Sign = w.Sign }),
        };
    }

    /// <summary>
    /// Where the round stands. The panel asks this when it opens, so a tie left waiting
    /// on an earlier visit is shown again rather than forgotten.
    /// </summary>
    public async Task<WarResponse> StateAsync(MongoId sessionId, ItemEventRouterResponse output)
    {
        using var gate = await Casino.Server.SessionGate.EnterAsync(sessionId);

        if (!profiles.HasProfile(sessionId))
        {
            return WarResponse.Failed("No PMC profile for this session.");
        }

        var refunded = await RefundStranded(sessionId, output);
        var seat = tables.For(sessionId);

        return Reply(seat, sessionId) with { Note = refunded };
    }

    public async Task<WarResponse> DealAsync(DealRequest request, MongoId sessionId, ItemEventRouterResponse output)
    {
        using var gate = await Casino.Server.SessionGate.EnterAsync(sessionId);

        if (!profiles.HasProfile(sessionId))
        {
            return WarResponse.Failed("No PMC profile for this session.");
        }

        // Refused by name rather than defaulting. Enum.TryParse on an unknown string
        // leaves the value at zero, which here is Roubles -- so a typo would spend a
        // currency the player never chose.
        if (!Enum.TryParse<Wallet>(request.Wallet, ignoreCase: true, out var wallet)
            || !Enum.IsDefined(wallet))
        {
            return WarResponse.Failed($"There is nothing called '{request.Wallet}' to play with.");
        }

        var refunded = await RefundStranded(sessionId, output);
        var seat = tables.For(sessionId);

        if (seat.Table.Phase == Phase.AwaitingDecision)
        {
            return Reply(seat, sessionId) with
            {
                Ok = false,
                Note = refunded,
                Error = "The last hand tied. Go to war or surrender before dealing another.",
            };
        }

        var info = WalletInfo.For(wallet);
        var ceiling = request.IgnoreMaximum ? WalletInfo.AbsoluteMax : info.MaxBet;

        if (!WalletInfo.Allows(wallet, request.Ante, request.IgnoreMaximum))
        {
            return Refuse(seat, sessionId, refunded, $"The table takes {info.MinBet:N0} to {ceiling:N0} {info.Label} a hand.");
        }

        if (request.TieBet != 0 && !WalletInfo.Allows(wallet, request.TieBet, request.IgnoreMaximum))
        {
            return Refuse(
                seat, sessionId, refunded, $"A tie bet is {info.MinBet:N0} to {ceiling:N0} {info.Label}, or nothing.");
        }

        // Both are under the absolute ceiling, so neither the sum nor anything the
        // round can return overflows an int. See WalletInfo.AbsoluteMax.
        var ante = (int)request.Ante;
        var tie = (int)request.TieBet;
        var total = ante + tie;

        // 2. Recorded before it is taken.
        escrow.Record(sessionId, wallet, total);

        // 3. Taken, both bets together. A refusal here has touched nothing.
        if (!bank.TryDebit(sessionId, wallet, total, output))
        {
            escrow.Release(sessionId);

            var balance = bank.GetBalance(sessionId, wallet);
            return Refuse(
                seat, sessionId, refunded, $"That hand costs {total:N0} {info.Label} and you have {balance:N0}.");
        }

        // 4. The cards.
        seat.Wallet = wallet;
        var round = seat.Table.Deal(ante, tie);

        // 5. The tie bet is settled on these two cards whatever happens next, so it is
        // paid now. If the round is over, so is everything else.
        if (round.TieReturn > 0)
        {
            bank.Credit(sessionId, wallet, round.TieReturn, output);
        }

        if (round.Phase == Phase.Settled)
        {
            Finish(seat, round, sessionId, output);
        }
        else
        {
            // A tie. The tie bet is paid and the ante is still the table's, so that is
            // what the record now says. Written after the tie bet's credit: a crash in
            // between refunds the tie bet as well as paying it, the safe way round.
            escrow.Record(sessionId, wallet, ante);
            log.Detail($"tie [{sessionId}] -- {round.PlayerCard} and {round.DealerCard}, {ante:N0} {wallet} waiting");
        }

        // 6. On disk, or it did not happen.
        await profiles.SaveAsync(sessionId);

        return Reply(seat, sessionId) with { Note = refunded };
    }

    /// <summary>Answers a tie: "War" or "Surrender". Nothing else is a choice.</summary>
    public async Task<WarResponse> DecideAsync(DecideRequest request, MongoId sessionId, ItemEventRouterResponse output)
    {
        using var gate = await Casino.Server.SessionGate.EnterAsync(sessionId);

        if (!profiles.HasProfile(sessionId))
        {
            return WarResponse.Failed("No PMC profile for this session.");
        }

        if (!tables.HasOpenRound(sessionId))
        {
            var refunded = await RefundStranded(sessionId, output);
            return Reply(tables.For(sessionId), sessionId) with
            {
                Ok = false,
                Note = refunded,
                Error = "There is no tie to decide.",
            };
        }

        var seat = tables.For(sessionId);
        var wallet = seat.Wallet;
        var ante = seat.Table.View().Ante;

        RoundView round;

        if (string.Equals(request.Choice, "War", StringComparison.OrdinalIgnoreCase))
        {
            // Checked before anything is recorded, so a player short of the raise finds
            // the tie exactly as they left it, surrender still available.
            var balance = bank.GetBalance(sessionId, wallet);
            if (balance < ante)
            {
                return Refuse(
                    seat, sessionId, null,
                    $"Going to war costs another {ante:N0} {WalletInfo.For(wallet).Label} and you have {balance:N0}. "
                    + "You can still surrender.");
            }

            escrow.Record(sessionId, wallet, ante * 2);

            if (!bank.TryDebit(sessionId, wallet, ante, output))
            {
                // The balance moved between the check and the debit. Put the record
                // back to what the table still holds.
                escrow.Record(sessionId, wallet, ante);
                return Refuse(seat, sessionId, null, "The raise could not be taken. You can still surrender.");
            }

            round = seat.Table.GoToWar();
        }
        else if (string.Equals(request.Choice, "Surrender", StringComparison.OrdinalIgnoreCase))
        {
            round = seat.Table.Surrender();
        }
        else
        {
            return Refuse(seat, sessionId, null, $"'{request.Choice}' is not an answer to a tie. Go to war or surrender.");
        }

        Finish(seat, round, sessionId, output);
        await profiles.SaveAsync(sessionId);

        return Reply(seat, sessionId);
    }

    public PlayerStats Stats(MongoId sessionId) => stats.Get(sessionId);

    /// <summary>
    /// Pays what the ante and the raise won, records the round, and releases the escrow
    /// -- in that order. The tie bet is never paid here; it was paid on the deal.
    /// </summary>
    private void Finish(Seat seat, RoundView round, MongoId sessionId, ItemEventRouterResponse output)
    {
        if (round.MainReturn > 0)
        {
            bank.Credit(sessionId, seat.Wallet, round.MainReturn, output);
        }

        var record = stats.Get(sessionId);
        record.Record(round, seat.Wallet, DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        stats.Save(sessionId, record);

        escrow.Release(sessionId);

        if (round.Profit > 0)
        {
            log.Info($"{round.Outcome} [{sessionId}] -- paid {round.Returned:N0} {seat.Wallet} on {round.Staked:N0}");
        }
        else
        {
            log.Detail($"{round.Outcome} [{sessionId}] -- {round.Profit:N0} {seat.Wallet}");
        }
    }

    /// <summary>
    /// Gives back a stake whose round no longer exists -- a server that died, or was
    /// restarted, with money on the table. A tie still waiting owns its stake and is
    /// never refunded here.
    ///
    /// Goes back in full and at most once. A restart mid-tie therefore returns the whole
    /// ante, more than surrendering would have; that is the right side of the line for a
    /// fault that was the server's and not the player's.
    /// </summary>
    private async Task<string?> RefundStranded(MongoId sessionId, ItemEventRouterResponse output)
    {
        var owed = escrow.Get(sessionId);

        if (owed is null || tables.HasOpenRound(sessionId))
        {
            return null;
        }

        if (owed.Amount <= 0)
        {
            escrow.Release(sessionId);
            return null;
        }

        if (!Enum.TryParse<Wallet>(owed.Wallet, ignoreCase: true, out var wallet) || !Enum.IsDefined(wallet))
        {
            escrow.Release(sessionId);
            log.Error($"discarded an unreadable outstanding stake of {owed.Amount:N0} '{owed.Wallet}' [{sessionId}]");
            return null;
        }

        bank.Credit(sessionId, wallet, owed.Amount, output);
        escrow.Release(sessionId);
        await profiles.SaveAsync(sessionId);

        log.Info($"gave back {owed.Amount:N0} {wallet} from a round that never finished [{sessionId}]");

        return $"A hand was interrupted before it was settled. {owed.Amount:N0} {WalletInfo.For(wallet).Label} has been returned.";
    }

    private WarResponse Refuse(Seat seat, MongoId sessionId, string? note, string error) =>
        Reply(seat, sessionId) with { Ok = false, Note = note, Error = error };

    private WarResponse Reply(Seat seat, MongoId sessionId) => new()
    {
        Round = Wire(seat.Table.View()),
        Wallet = seat.Wallet.ToString(),
        Balance = bank.GetBalance(sessionId, seat.Wallet),
    };

    private static RoundWire Wire(RoundView round) => new()
    {
        Phase = round.Phase.ToString(),
        Outcome = round.Outcome.ToString(),
        PlayerCard = round.PlayerCard?.Code,
        DealerCard = round.DealerCard?.Code,
        PlayerWarCard = round.PlayerWarCard?.Code,
        DealerWarCard = round.DealerWarCard?.Code,
        Burned = round.Burned,
        Shuffled = round.Shuffled,
        Ante = round.Ante,
        TieBet = round.TieBet,
        Raise = round.Raise,
        TieReturn = round.TieReturn,
        MainReturn = round.MainReturn,
        Staked = round.Staked,
        Returned = round.Returned,
        Profit = round.Profit,
    };
}
