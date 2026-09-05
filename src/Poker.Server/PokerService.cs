using Poker.Game;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.ItemEvent;

namespace Poker.Server;

/// <summary>
/// The whole server-side game flow: validate what was asked for, let the table
/// decide, hand back a view.
///
/// Depends only on <see cref="IBank"/>, <see cref="IProfileGateway"/> and
/// <see cref="TableStore"/>, so it runs -- and can be tested -- with no SPT server
/// present. HTTP and logging live in <see cref="PokerCallbacks"/>.
///
/// **One chip is one unit of the wallet the table is bought into.** Sitting down
/// debits the buy-in, standing up credits whatever is left, and between those two the
/// player's stack is the only record of what they are owed -- which is why every hand
/// writes it to escrow.
/// </summary>
[Injectable]
public class PokerService(
    IBank bank,
    IProfileGateway profiles,
    TableStore tables,
    IEscrowStore escrow,
    INameSource names,
    IPokerLog log)
{
    /// <summary>Cheap health check. Touches nothing and starts no game.</summary>
    public PingResponse Ping(MongoId sessionId)
    {
        var known = profiles.HasProfile(sessionId);

        return new PingResponse
        {
            ModVersion = new ModMetadata().Version.ToString(),
            SessionId = sessionId.ToString(),
            HasProfile = known,
            Balances = known
                ? Enum.GetValues<Wallet>().ToDictionary(w => w.ToString(), w => bank.GetBalance(sessionId, w))
                : [],

            // Not gated on the profile: the limits belong to the table rather than to
            // the player, and a client that cannot read them has no way to offer a
            // legal buy-in before sending one.
            Limits = WalletInfo.All.ToDictionary(
                w => w.Wallet.ToString(),
                w => new BuyInLimits
                {
                    Min = w.MinBuyIn,
                    Max = w.MaxBuyIn,
                    StackLimit = known ? bank.MaxStackSize(w.Wallet) : 0,
                }),
        };
    }

    public async Task<PokerResponse> SitAsync(SitRequest request, MongoId sessionId, ItemEventRouterResponse output)
    {
        if (!profiles.HasProfile(sessionId))
        {
            return PokerResponse.Failed("No PMC profile for this session.");
        }

        if (tables.Get(sessionId) is not null)
        {
            return PokerResponse.Failed("You are already at a table. Leave it first and take your chips.");
        }

        if (!Enum.TryParse<Wallet>(request.Wallet, ignoreCase: true, out var wallet))
        {
            return PokerResponse.Failed($"Unknown currency '{request.Wallet}'.");
        }

        // Anything owed from a session that never finished goes back before another
        // buy-in is taken, or the two would be indistinguishable in the stash.
        var note = RefundAbandoned(sessionId, output);

        if (request.Seats is < 2 or > 5)
        {
            return PokerResponse.Failed("A table seats 2 to 5, the player included.");
        }

        if (request.BigBlind < 2)
        {
            return PokerResponse.Failed("The big blind has to be at least 2, so the small blind is a whole chip.");
        }

        if (request.BuyIn < request.BigBlind * 10)
        {
            return PokerResponse.Failed(
                $"A buy-in of {request.BuyIn} is under ten big blinds. There would be nothing to play with.");
        }

        // One chip to the unit, so the table's chip buy-in has to sit inside what the
        // wallet will take. At these stakes that is roubles and nothing else -- the
        // rest are simply not held in numbers like these, and giving each wallet a
        // chips-per-unit rate is what would open them up.
        var limits = WalletInfo.For(wallet);

        if (request.BuyIn > limits.MaxBuyIn || request.BuyIn < limits.MinBuyIn)
        {
            return PokerResponse.Failed(
                $"A {request.BuyIn:N0} chip buy-in cannot be paid in {limits.Label}, which takes "
                + $"{limits.MinBuyIn:N0} to {limits.MaxBuyIn:N0}.");
        }

        // Validated before a chip is taken. Letting the table throw after the debit
        // would pocket the buy-in and seat nobody.
        if (!bank.TryDebit(sessionId, wallet, request.BuyIn, output))
        {
            return PokerResponse.Failed(
                $"Not enough {limits.Label} -- you have {bank.GetBalance(sessionId, wallet):N0} "
                + $"and the buy-in is {request.BuyIn:N0}.");
        }

        // Recorded the instant the money is gone. From here until the player stands
        // up, this file is the only thing that knows they are owed anything.
        escrow.Record(sessionId, wallet, request.BuyIn);

        var rules = new HoldemRules
        {
            SmallBlind = request.BigBlind / 2,
            BigBlind = request.BigBlind,
            BuyIn = request.BuyIn,
        };

        var seed = request.Seed ?? Environment.TickCount;
        var rng = new Random(seed);
        var engineLog = log.ForEngine();

        // Improvised rather than picked off the list, so no two tables are alike.
        var characters = Enumerable.Range(0, request.Seats - 1)
            .Select(_ => PokerPersonality.Improvise(rng))
            .ToList();

        var agents = characters
            .Select((character, index) => new BotAgent(character, new Random(seed + index + 1), engineLog))
            .ToList();

        // One name per bot, from the game's own PMC list. Fewer than asked for is
        // fine -- the table numbers whatever it does not get.
        var seatNames = names.Take(request.Seats - 1, rng);

        var table = new HoldemTable(
            rules,
            request.Seats,
            rng,
            engineLog,
            agents.Cast<IPokerAgent>().ToList(),
            seatNames);

        tables.Set(sessionId, new PlayerSession
        {
            Table = table,
            Characters = characters,
            Agents = agents,
            BuyIn = request.BuyIn,
            Wallet = wallet,
        });

        await profiles.SaveAsync(sessionId);

        log.Info(
            $"seat taken [{sessionId}] -- {request.Seats} seats, blinds {rules.SmallBlind}/{rules.BigBlind}, "
            + $"{request.BuyIn} chips each, seed {seed}");

        foreach (var character in characters)
        {
            log.Detail($"  {character}");
        }

        return Success(sessionId) with { Note = note };
    }

    public PokerResponse Deal(MongoId sessionId)
    {
        var session = tables.Get(sessionId);

        if (session is null)
        {
            return PokerResponse.Failed("You are not at a table. Sit down first.");
        }

        if (session.Table.Street is not (HoldemStreet.Idle or HoldemStreet.Showdown))
        {
            return PokerResponse.Failed("A hand is already in progress.");
        }

        // A busted bot is replaced by somebody new, which is the difference between a
        // table and a treadmill. The player is **not** topped up: their chips cost
        // real currency, so a fresh stack is a fresh buy-in and has to be asked for.
        if (session.Table.Player.Stack <= 0)
        {
            return PokerResponse.Failed("You are out of chips. Leave the table and buy in again.");
        }

        Reseat(session);

        try
        {
            session.Table.StartHand();
        }
        catch (InvalidOperationException ex)
        {
            return PokerResponse.Failed(ex.Message);
        }

        RecordStack(session, sessionId);

        return Success(sessionId);
    }

    public PokerResponse Act(ActRequest request, MongoId sessionId)
    {
        var session = tables.Get(sessionId);

        if (session is null)
        {
            return PokerResponse.Failed("You are not at a table.");
        }

        if (!session.Table.AwaitingPlayer)
        {
            return PokerResponse.Failed("It is not your turn.");
        }

        if (!Enum.TryParse<HoldemMove>(request.Move, ignoreCase: true, out var move))
        {
            return PokerResponse.Failed($"Unknown move '{request.Move}'. Fold, Check, Call or Raise.");
        }

        try
        {
            session.Table.Act(new HoldemDecision(move, request.To));
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentOutOfRangeException)
        {
            // The engine is the authority on legality. A refusal means the client's
            // view drifted, so hand it the real one back rather than a bare error.
            return Success(sessionId) with { Ok = false, Error = ex.Message };
        }

        RecordStack(session, sessionId);

        return Success(sessionId);
    }

    /// <summary>
    /// The table as it stands, and **the place an abandoned stack is given back**.
    ///
    /// It refunds because it is the only request a player makes without meaning to
    /// spend anything: opening the panel. `SitAsync` and `LeaveAsync` refund too, but
    /// a player who is owed a stack has no reason to press either -- SIT DOWN asks for
    /// another two million and LEAVE says they are not at a table -- so before this,
    /// the money sat in escrow with nothing telling them it was there. It is not lost
    /// either way, but "the mod took my roubles" is what it looks like.
    ///
    /// This is why it takes an output. `State` was a pure read with nothing to hang
    /// item changes off, which is exactly why the refund was not here; the static
    /// callback can ask `EventOutputHolder` for one the same way `sit` and `leave`
    /// already do.
    /// </summary>
    public async Task<PokerResponse> StateAsync(MongoId sessionId, ItemEventRouterResponse output)
    {
        var note = RefundAbandoned(sessionId, output);

        if (note is not null)
        {
            await profiles.SaveAsync(sessionId);
        }

        if (tables.Get(sessionId) is null)
        {
            return new PokerResponse { Ok = false, Error = "You are not at a table.", Note = note };
        }

        var view = Success(sessionId);

        // A record, so the note goes on with `with` rather than by assignment -- Note is
        // init-only, which is what keeps a response from being edited after the fact.
        return note is null ? view : view with { Note = note };
    }

    /// <summary>
    /// Stands up and takes the chips. Whatever is in front of the player converts back
    /// at one to the unit, however much or little that is.
    /// </summary>
    public async Task<PokerResponse> LeaveAsync(MongoId sessionId, ItemEventRouterResponse output)
    {
        var session = tables.Get(sessionId);

        if (session is null)
        {
            // Not at a table, but a stack may still be owed from a session that never
            // finished -- which is exactly the case this has to handle.
            var recovered = RefundAbandoned(sessionId, output);
            await profiles.SaveAsync(sessionId);

            return new PokerResponse { Note = recovered };
        }

        if (session.Table.Street is not (HoldemStreet.Idle or HoldemStreet.Showdown))
        {
            return PokerResponse.Failed("Finish the hand before you stand up.");
        }

        var chips = session.Table.Player.Stack;

        if (chips > 0)
        {
            bank.Credit(sessionId, session.Wallet, chips, output);
        }

        // Released only after the credit. A crash between the two leaves the stack
        // recorded and refundable, which is the safe way round -- the other order
        // pays nothing and forgets it was owed.
        escrow.Release(sessionId);
        tables.Clear(sessionId);

        await profiles.SaveAsync(sessionId);

        var net = chips - session.BuyIn;
        log.Info(
            $"left the table [{sessionId}] with {chips:N0} {session.Wallet} "
            + $"against a {session.BuyIn:N0} buy-in ({net:+#;-#;0})");

        return new PokerResponse
        {
            Balance = bank.GetBalance(sessionId, session.Wallet),
            Wallet = session.Wallet.ToString(),
        };
    }

    /// <summary>
    /// Writes the player's stack down after every hand.
    ///
    /// The stack is the only record of what they are owed and it changes every hand,
    /// so anything less often is already wrong. This is the whole difference between
    /// giving back what somebody has and giving back what they arrived with.
    /// </summary>
    private void RecordStack(PlayerSession session, MongoId sessionId) =>
        escrow.Record(sessionId, session.Wallet, session.Table.Player.Stack);

    /// <summary>
    /// Gives back a stack whose table no longer exists.
    ///
    /// The table lives in memory and the buy-in does not, so a restart mid-session
    /// leaves currency owed with nothing to play it on. Refunding lazily, on next
    /// contact, avoids touching profiles at boot before the server has finished
    /// loading them.
    /// </summary>
    private string? RefundAbandoned(MongoId sessionId, ItemEventRouterResponse output)
    {
        var owed = escrow.Get(sessionId);

        if (owed is null)
        {
            return null;
        }

        // A live table still owns its stack. Only an orphan is refundable.
        if (tables.Get(sessionId) is not null)
        {
            return null;
        }

        if (!Enum.TryParse<Wallet>(owed.Wallet, ignoreCase: true, out var wallet))
        {
            escrow.Release(sessionId);
            return $"Discarded an unreadable outstanding stack of {owed.Chips} '{owed.Wallet}'.";
        }

        if (owed.Chips > 0)
        {
            bank.Credit(sessionId, wallet, owed.Chips, output);
        }

        escrow.Release(sessionId);

        return $"Gave back {owed.Chips:N0} {wallet} from a session that never finished.";
    }

    private void Reseat(PlayerSession session)
    {
        foreach (var seat in session.Table.Seats.Where(seat => seat.Stack <= 0).ToList())
        {
            if (seat.IsPlayer)
            {
                // Handled by the caller, which refuses to deal. Topping the player up
                // here would create currency out of nothing.
                continue;
            }

            var newcomer = PokerPersonality.Improvise(new Random(Environment.TickCount + seat.Index));
            var agent = new BotAgent(newcomer, new Random(Environment.TickCount + seat.Index + 7), log.ForEngine());

            session.Agents[seat.Index - 1] = agent;
            session.Table.Reseat(seat.Index, session.BuyIn, agent);

            log.Info($"{seat.Name} went broke; {newcomer.Name} sits down.");
        }
    }

    private PokerResponse Success(MongoId sessionId)
    {
        var session = tables.Get(sessionId);

        return new PokerResponse
        {
            Table = session is null ? null : HoldemView.Of(session.Table),
            Characters = session?.Characters.Select(character => character.Name).ToList() ?? [],
            Balance = session is null ? 0 : bank.GetBalance(sessionId, session.Wallet),
            Wallet = (session?.Wallet ?? Wallet.Roubles).ToString(),
        };
    }
}
