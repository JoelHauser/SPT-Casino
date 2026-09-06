using Roulette.Game;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.ItemEvent;

namespace Roulette.Server;

/// <summary>
/// The whole server-side game flow: validate what was asked for, let the table
/// decide, hand back a view.
///
/// Depends only on <see cref="IBank"/>, <see cref="IProfileGateway"/> and
/// <see cref="TableStore"/>, so it runs -- and can be tested -- with no SPT server
/// present. HTTP and logging live in <see cref="RouletteCallbacks"/>.
///
/// ## The cloth is intent; the spin is the transaction
///
/// Chips go on and come off freely and **none of that touches the stash**. When the
/// wheel turns, the whole stake leaves the wallet in one debit and the whole return
/// arrives in one credit.
///
/// That is a deliberate choice over debiting each chip as it is placed. A right-click
/// would then have to credit back, a cloth of 150 bets would be 150 item events, and
/// the window in which the player's money exists nowhere would last as long as they
/// took to decide. This way it is the length of one method.
///
/// What a player can afford is still checked when a chip goes down, as a **read**.
/// Letting them build a cloth they cannot cover and refusing it at the wheel would be
/// correct and horrible.
/// </summary>
[Injectable]
public class RouletteService(
    IBank bank,
    IProfileGateway profiles,
    IEscrowStore escrow,
    TableStore tables,
    IRandomSource random,
    IRouletteLog log)
{
    /// <summary>
    /// What the table deals in.
    ///
    /// Roubles, and only roubles, for the same reason Poker settled on them: one chip
    /// is one unit, the minimum bet is 10,000 and the chips go to 1,000,000, and
    /// nothing else in the game is held in numbers like these. Dollars and euros are
    /// read and reported by `Ping` so the client can show them, but a table cannot be
    /// played in them until there is a chips-per-unit rate.
    /// </summary>
    private const Wallet Currency = Wallet.Roubles;

    /// <summary>Cheap health check. Touches nothing and starts no game.</summary>
    public PingResponse Ping(MongoId sessionId)
    {
        var known = profiles.HasProfile(sessionId);

        return new PingResponse
        {
            ModVersion = TableInfo.Version,
            SessionId = sessionId.ToString(),
            HasProfile = known,
            Balances = known
                ? Enum.GetValues<Wallet>().ToDictionary(w => w.ToString(), w => bank.GetBalance(sessionId, w))
                : [],

            // Not gated on the profile: the limits belong to the table rather than to
            // the player, and a client that cannot read them has no way to offer a
            // legal stake before sending one.
            Limits = WalletInfo.All.ToDictionary(
                w => w.Wallet.ToString(),
                w => new StakeLimits
                {
                    Min = w.MinStake,
                    Max = w.MaxStake,

                    // Read on contact rather than at boot. PostLoad is not last:
                    // BarterItemsStacks rewrites every stack limit about half a second
                    // after startup, so anything read then is the base value and wrong
                    // on any server with an item mod.
                    StackLimit = known ? bank.MaxStackSize(w.Wallet) : 0,
                }),
        };
    }

    /// <summary>
    /// The table as it stands, and the first chance to notice a spin that never
    /// finished. See <see cref="RefundStranded"/>.
    /// </summary>
    public async Task<RouletteResponse> StateAsync(MongoId sessionId, ItemEventRouterResponse output)
    {
        var refunded = await RefundStranded(sessionId, output);

        return Success(sessionId) with { Note = refunded };
    }

    /// <summary>
    /// Puts chips on a spot.
    ///
    /// The engine is the authority on what is a legal bet, so this parses the request
    /// and hands it straight over. A refusal comes back with the table attached: the
    /// client's picture may simply have drifted, and redrawing it is the fix.
    /// </summary>
    public RouletteResponse Place(PlaceRequest request, MongoId sessionId)
    {
        // Refused by name rather than defaulting. Enum.TryParse on an unknown string
        // leaves the value at zero, which here is Straight -- so a typo would put the
        // player's money on a single number they never chose.
        if (!Enum.TryParse<BetKind>(request.Kind, ignoreCase: true, out var kind))
        {
            return RouletteResponse.Failed(
                $"There is no bet called '{request.Kind}'.");
        }

        var table = Table(sessionId);

        // Checked here, and only read. Nothing moves until the wheel turns, so this
        // is about not letting a player build a cloth the spin would then refuse --
        // which would be correct and horrible.
        var balance = bank.GetBalance(sessionId, Currency);

        if (table.Staked + request.Amount > balance)
        {
            return Success(sessionId) with
            {
                Ok = false,
                Error = $"That would put {table.Staked + request.Amount:N0} on the cloth "
                    + $"and you have {balance:N0}.",
            };
        }

        try
        {
            table.Place(new Bet(kind, request.Selection, request.Amount));
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            return Success(sessionId) with { Ok = false, Error = ex.Message };
        }

        return Success(sessionId);
    }

    /// <summary>
    /// Lifts chips off a spot. Right-clicking a square is the other half of clicking
    /// it, and a player who has stacked four chips on a number should be able to take
    /// one back rather than clearing the whole cloth.
    /// </summary>
    public RouletteResponse Remove(RemoveRequest request, MongoId sessionId)
    {
        if (!Enum.TryParse<BetKind>(request.Kind, ignoreCase: true, out var kind))
        {
            return RouletteResponse.Failed($"There is no bet called '{request.Kind}'.");
        }

        var table = Table(sessionId);

        try
        {
            table.Remove(kind, request.Selection, request.Amount);
        }
        catch (InvalidOperationException ex)
        {
            return Success(sessionId) with { Ok = false, Error = ex.Message };
        }

        return Success(sessionId);
    }

    public RouletteResponse Clear(MongoId sessionId)
    {
        var table = Table(sessionId);

        try
        {
            var back = table.ClearBets();
            log.Detail($"cleared the cloth, {back:N0} back [{sessionId}]");
        }
        catch (InvalidOperationException ex)
        {
            return Success(sessionId) with { Ok = false, Error = ex.Message };
        }

        return Success(sessionId);
    }

    /// <summary>
    /// Turns the wheel, and the only place in this mod where money moves.
    ///
    /// The order is the whole of it, and it is the order Blackjack and Poker both
    /// arrived at the hard way:
    ///
    /// 1. **Record the stake in escrow**, before anything is taken. A crash after this
    ///    and before the credit leaves a record of money the player is owed. Doing it
    ///    the other way round -- take first, record second -- leaves a window where the
    ///    stake is gone and nothing says so.
    /// 2. **Debit.** If it fails, release the escrow and refuse: nothing has moved, so
    ///    nothing is owed, and the bets stay on the cloth to be spun again.
    /// 3. **Settle**, which is arithmetic on numbers already decided.
    /// 4. **Credit the return**, then release the escrow. That order again: a crash
    ///    between them refunds a stake that was also paid out, which is the safe way
    ///    round. The other order pays nothing and forgets it was owed.
    /// 5. **Save.** Money that is not flushed to disk did not move.
    /// </summary>
    public async Task<RouletteResponse> SpinAsync(MongoId sessionId, ItemEventRouterResponse output)
    {
        var refunded = await RefundStranded(sessionId, output);
        var table = Table(sessionId);

        // A settled table is re-opened here rather than by its own route: a player
        // pressing spin again plainly means "another one", and making them clear the
        // last result first is a button that exists only to be pressed. It must move
        // no money -- re-running the last spin's settlement is the most obvious way to
        // pay a winner twice.
        if (table.Phase == SpinPhase.Settled)
        {
            table.NextSpin();
            return Success(sessionId) with { Note = refunded };
        }

        var stake = table.Staked;

        if (stake <= 0)
        {
            return Success(sessionId) with
            {
                Ok = false,
                Error = "Nothing is on the cloth.",
                Note = refunded,
            };
        }

        // 1. Recorded before it is taken.
        escrow.Record(sessionId, Currency, stake);

        // 2. Taken. A refusal here has touched nothing.
        if (!bank.TryDebit(sessionId, Currency, stake, output))
        {
            escrow.Release(sessionId);

            var balance = bank.GetBalance(sessionId, Currency);
            log.Info($"spin refused [{sessionId}] -- {stake:N0} staked, {balance:N0} held");

            return Success(sessionId) with
            {
                Ok = false,
                Error = $"You need {stake:N0} to spin that and you have {balance:N0}.",
                Note = refunded,
            };
        }

        SpinResult spin;

        try
        {
            // 3. Decided here and nowhere else. What the client does with it is
            // presentation: it is handed the pocket and its position and animates
            // towards it.
            spin = table.Spin();
        }
        catch (InvalidOperationException ex)
        {
            // The stake is already gone, so it has to come back rather than be
            // reported as a failed spin. This should be unreachable -- the phase and
            // the empty cloth are both checked above -- which is exactly why it pays
            // back rather than trusting that.
            log.Error($"the wheel would not turn after {stake:N0} was taken: {ex.Message}");
            bank.Credit(sessionId, Currency, stake, output);
            escrow.Release(sessionId);
            await profiles.SaveAsync(sessionId);

            return Success(sessionId) with { Ok = false, Error = ex.Message, Note = refunded };
        }

        // 4. Paid, then released. Never the other way round.
        if (spin.Returned > 0)
        {
            bank.Credit(sessionId, Currency, spin.Returned, output);
        }

        escrow.Release(sessionId);

        // 5. On disk, or it did not happen.
        await profiles.SaveAsync(sessionId);

        log.Info(
            $"the ball landed in {spin.Result} [{sessionId}] -- "
            + $"{spin.Staked:N0} staked, {spin.Returned:N0} back, {spin.Profit:N0} on the spin");

        return Success(sessionId) with { Note = refunded };
    }

    /// <summary>
    /// Gives back a stake left behind by a spin that never finished.
    ///
    /// The only way to hold a record here is for the server to have died between the
    /// debit and the credit, so what it holds is money the player paid for a spin they
    /// never saw the end of. It goes back in full.
    ///
    /// **Released before it is paid would be wrong; released after, and only if the
    /// credit was reached, is right.** And it must happen at most once -- refunding
    /// twice is worse than not refunding at all, because nobody reports it.
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
            : Currency;

        bank.Credit(sessionId, wallet, owed.Amount, output);
        escrow.Release(sessionId);
        await profiles.SaveAsync(sessionId);

        log.Info($"gave back {owed.Amount:N0} {wallet} from a spin that never finished [{sessionId}]");

        return $"A spin was interrupted before it paid out. {owed.Amount:N0} has been returned.";
    }

    /// <summary>
    /// Forgets the table entirely, chips and all.
    ///
    /// Chips still sitting on the cloth were never taken from the stash, so there is
    /// nothing to give back and giving something back would be minting money. The only
    /// thing owed on the way out is a stake stranded by an interrupted spin, which is
    /// what <see cref="RefundStranded"/> is for.
    /// </summary>
    public async Task<RouletteResponse> LeaveAsync(MongoId sessionId, ItemEventRouterResponse output)
    {
        var refunded = await RefundStranded(sessionId, output);

        tables.Clear(sessionId);
        log.Detail($"left the table [{sessionId}]");

        return new RouletteResponse { Note = refunded };
    }

    private RouletteTable Table(MongoId sessionId) =>
        tables.GetOrCreate(sessionId, () =>
        {
            log.Detail($"opened a table [{sessionId}]");
            return new RouletteTable(new RouletteRules(), random.Create(), log.ForEngine());
        });

    private RouletteResponse Success(MongoId sessionId) =>
        new() { Table = TableView.Of(Table(sessionId)) };
}
