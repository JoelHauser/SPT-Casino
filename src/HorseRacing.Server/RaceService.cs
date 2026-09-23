using HorseRacing.Game;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.ItemEvent;

namespace HorseRacing.Server;

/// <summary>
/// The whole server-side game: check the slip, run the race, hand back what it paid.
///
/// **There is no state between races.** No table to sit at, nothing built up, nothing
/// to abandon -- one slip is one transaction: the stakes leave, the race runs, what it
/// paid arrives. That is Slots' arrangement rather than Poker's, and it is why there
/// is no table store here.
///
/// **Three courses, one set of horses.** A slip names a course and every price on it
/// comes from that course's board; the same bet is a different price at a different
/// course, so the track is resolved once, up front, and threaded through the draw and
/// the settlement together. Nothing here can price against one card and pay against
/// another.
///
/// The other way this differs from every other table: **a slip is many bets and one
/// transaction.** The stakes are summed and taken once, and the returns are summed and
/// paid once. Taking them bet by bet would leave a partially-paid slip on any failure
/// halfway down, which is a state nothing here knows how to recover from.
///
/// Depends only on <see cref="IBank"/>, <see cref="IProfileGateway"/>,
/// <see cref="IEscrowStore"/>, <see cref="IStatsStore"/> and <see cref="IRandomSource"/>,
/// so it runs -- and is tested -- with no SPT server present.
/// </summary>
[Injectable]
public class RaceService(
    IBank bank,
    IProfileGateway profiles,
    IEscrowStore escrow,
    IRandomSource random,
    IStatsStore stats,
    IRaceLog log)
{
    private readonly Random _random = random.Create();
    private readonly RaceRules _rules = new();

    /// <summary>
    /// Cheap health check, and where the panel gets the table's own numbers.
    ///
    /// The card, the board, the limits and the takeout all travel from here rather
    /// than being written into the client, so the panel cannot advertise a price the
    /// table does not pay.
    /// </summary>
    public PingResponse Ping(MongoId sessionId, ItemEventRouterResponse output)
    {
        // Refunds from escrow, so it must not run beside a slip -- see SessionGate.
        using var gate = Casino.Server.SessionGate.Enter(sessionId);

        var known = profiles.HasProfile(sessionId);
        var refunded = RefundStranded(sessionId, output).GetAwaiter().GetResult();

        return new PingResponse
        {
            ModVersion = TableInfo.Version,
            SessionId = sessionId.ToString(),
            HasProfile = known,
            Note = refunded,
            Takeout = Odds.Takeout,
            MaxBets = _rules.MaxBets,

            Balances = known
                ? Enum.GetValues<Wallet>().ToDictionary(w => w.ToString(), w => bank.GetBalance(sessionId, w))
                : [],

            Limits = WalletInfo.All.ToDictionary(
                w => w.Wallet.ToString(),
                w => new StakeLimits { Min = w.MinStake, Max = w.MaxStake, Step = w.Step, Sign = w.Sign }),

            Courses = [.. Tracks.All.Select(Describe)],
        };
    }

    /// <summary>
    /// One course as the panel needs it: its card, its whole board, and how to draw it.
    ///
    /// Computed per request rather than cached. Three courses at 108 spots each is 324
    /// enumerations of 336 prefixes, which sounds like a lot and is about a millisecond
    /// -- and a cache is a second copy of the prices that can go stale against the
    /// weights, which is the one failure this whole table is arranged to avoid.
    /// </summary>
    private static CourseView Describe(Track track) => new()
    {
        Id = track.Id,
        Name = track.Name,
        Distance = track.Distance,
        Blurb = track.Blurb,
        Furlongs = track.Furlongs,
        RunSeconds = track.RunSeconds,
        MaxSlip = track.MaxSlip,

        Runners =
        [
            .. Field.Runners.Select(r => new RunnerView
            {
                Number = r.Number,
                Name = r.Name,
                Speed = r.Speed,
                Stamina = r.Stamina,
                Chance = Odds.Chance(track, BetKind.Win, r.Number),
            }),
        ],

        Board =
        [
            .. Odds.All(track).Select(p => new PriceView
            {
                Kind = p.Kind.ToString(),
                First = p.First,
                Second = p.Second,
                Chance = p.Chance,
                Price = p.Board,
            }),
        ],
    };

    /// <summary>
    /// Runs a race against a slip, and the only place in this table where money moves.
    ///
    /// The order is the whole of it, and it is the one the other four arrived at the
    /// hard way:
    ///
    /// 1. **Check first.** An unknown currency, a malformed bet, a stake the wallet
    ///    does not take, a slip over the ceiling, a balance that will not cover it --
    ///    all refused before anything is recorded. **The whole slip is validated
    ///    before any of it is taken**, so a bad tenth bet cannot leave the first nine
    ///    paid for.
    /// 2. **Record the total in escrow**, before it is taken. A crash after this and
    ///    before the credit leaves a record of money the player is owed. The other way
    ///    round leaves a window where the stakes are gone and nothing says so.
    /// 3. **Debit, once, for the whole slip.** If it fails, release the escrow and
    ///    refuse: nothing has moved.
    /// 4. **Run the race**, which is the draw and the settlement.
    /// 5. **Credit what it paid**, then release the escrow. That order again: a crash
    ///    between them refunds stakes that were also paid out, which is the safe way
    ///    round. The other pays nothing and forgets it was owed.
    /// 6. **Save.** Money that is not flushed to disk did not move.
    /// </summary>
    public async Task<RaceResponse> PlaceAsync(
        PlaceRequest request, MongoId sessionId, ItemEventRouterResponse output)
    {
        using var gate = await Casino.Server.SessionGate.EnterAsync(sessionId);

        if (!profiles.HasProfile(sessionId))
        {
            return RaceResponse.Failed("No PMC profile for this session.");
        }

        // Refused by name rather than defaulting. Enum.TryParse on an unknown string
        // leaves the value at zero, which here is Roubles -- so a typo would spend a
        // currency the player never chose.
        if (!Enum.TryParse<Wallet>(request.Wallet, ignoreCase: true, out var wallet))
        {
            return RaceResponse.Failed($"There is nothing called '{request.Wallet}' to bet with.");
        }

        // **The course, for exactly the same reason, and it matters more here.** A
        // wallet typo spends the wrong currency; a course typo pays from the wrong
        // board. Runner 7 is 5.78 at the dash and 36.28 at the marathon, so defaulting
        // would hand a player a price they were never shown -- in either direction.
        var track = Tracks.ById(request.Track);

        if (track is null)
        {
            return RaceResponse.Failed($"There is no course here called '{request.Track}'.");
        }

        var info = WalletInfo.For(wallet);
        var refunded = await RefundStranded(sessionId, output);

        if (request.Bets.Count == 0)
        {
            return Refused(refunded, "There is nothing on the slip.");
        }

        if (request.Bets.Count > _rules.MaxBets)
        {
            return Refused(refunded, $"A slip takes at most {_rules.MaxBets} bets.");
        }

        // 1. The whole slip, before any of it is taken.
        var slip = new List<Bet>(request.Bets.Count);
        var total = 0L;

        foreach (var entry in request.Bets)
        {
            if (!Enum.TryParse<BetKind>(entry.Kind, ignoreCase: true, out var kind))
            {
                return Refused(refunded, $"There is no bet here called '{entry.Kind}'.");
            }

            if (!WalletInfo.Allows(wallet, entry.Stake))
            {
                return Refused(
                    refunded,
                    $"A bet in {info.Label} is at least {info.MinStake:N0}.");
            }

            var bet = new Bet(kind, entry.First, entry.Second, (int)entry.Stake);

            if (!bet.IsWellFormed())
            {
                return Refused(refunded, $"That is not a bet this table takes: {kind} on {entry.First}/{entry.Second}.");
            }

            slip.Add(bet);
            total += entry.Stake;
        }

        if (!WalletInfo.AllowsSlip(wallet, total, track.MaxSlip, request.IgnoreMaximum))
        {
            var cap = WalletInfo.CapFor(wallet, track.MaxSlip, request.IgnoreMaximum);

            return Refused(
                refunded,
                $"A slip at {track.Name} in {info.Label} comes to at most {cap:N0}, and this one is {total:N0}.");
        }

        var staked = (int)total;

        // 2. Recorded before it is taken.
        escrow.Record(sessionId, wallet, staked);

        // 3. Taken once, for the whole slip. A refusal here has touched nothing.
        if (!bank.TryDebit(sessionId, wallet, staked, output))
        {
            escrow.Release(sessionId);

            var balance = bank.GetBalance(sessionId, wallet);
            log.Info($"slip refused [{sessionId}] -- {staked:N0} {wallet}, {balance:N0} held");

            return Refused(refunded, $"That slip costs {staked:N0} {info.Label} and you have {balance:N0}.");
        }

        // 4. Decided here and nowhere else. What the client does with it is
        // presentation: it is handed the finishing order and animates towards it.
        var result = Race.Run(track, slip, _random);

        // 5. Paid, then released. Never the other way round.
        if (result.Returned > 0)
        {
            bank.Credit(sessionId, wallet, result.Returned, output);
        }

        var record = stats.Get(sessionId);
        record.Record(
            result.Staked,
            result.Returned,
            slip.Count,
            result.Order[0],
            wallet,
            DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        stats.Save(sessionId, record);

        escrow.Release(sessionId);

        // 6. On disk, or it did not happen.
        await profiles.SaveAsync(sessionId);

        var winner = Field.ByNumber(result.Order[0]);

        if (result.Returned > 0)
        {
            log.Info(
                $"paid {result.Returned:N0} {wallet} [{sessionId}] -- {track.Name}, "
                + $"{winner.Number} {winner.Name} won, {slip.Count} bet(s), {result.Staked:N0} staked");
        }
        else
        {
            log.Detail(
                $"nothing [{sessionId}] -- {track.Name}, {winner.Number} {winner.Name} won, "
                + $"{result.Staked:N0} {wallet}");
        }

        return new RaceResponse { Note = refunded, Race = View(result) };
    }

    public PlayerStats Stats(MongoId sessionId) => stats.Get(sessionId);

    private static RaceResponse Refused(string? note, string error)
        => new() { Ok = false, Note = note, Error = error };

    /// <summary>
    /// Gives back stakes left behind by a race that never finished.
    ///
    /// The only way to hold a record here is for the server to have died between the
    /// debit and the credit, so what it holds is money the player paid for a race they
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

        log.Info($"gave back {owed.Amount:N0} {wallet} from a race that never finished [{sessionId}]");

        return $"A race was interrupted before it paid out. {owed.Amount:N0} has been returned.";
    }

    private static RaceView View(RaceResult result) => new()
    {
        Track = result.Track.Id,
        Order = result.Order,
        Staked = result.Staked,
        Returned = result.Returned,
        Profit = result.Returned - result.Staked,
        Settlements =
        [
            .. result.Settlements.Select(s => new SettlementView
            {
                Kind = s.Bet.Kind.ToString(),
                First = s.Bet.First,
                Second = s.Bet.Second,
                Stake = s.Bet.Stake,
                Won = s.Won,
                Returned = s.Returned,
            }),
        ],
    };
}
