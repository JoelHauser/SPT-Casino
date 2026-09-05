using Roulette.Game;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Models.Common;

namespace Roulette.Server;

/// <summary>
/// The whole server-side game flow: validate what was asked for, let the table
/// decide, hand back a view.
///
/// Depends only on <see cref="IBank"/>, <see cref="IProfileGateway"/> and
/// <see cref="TableStore"/>, so it runs -- and can be tested -- with no SPT server
/// present. HTTP and logging live in <see cref="RouletteCallbacks"/>.
///
/// **No money moves in this build.** The stake is a number in memory and a winning
/// spin pays nothing. That is a deliberate stopping point rather than an oversight,
/// and the same one Poker made: a mod that cannot move money cannot lose any, so the
/// parts that load, route and play get proven against a real profile before the
/// settlement -- the part that cost Blackjack the most -- is written.
/// </summary>
[Injectable]
public class RouletteService(IBank bank, IProfileGateway profiles, TableStore tables, RouletteLog log)
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

    public RouletteResponse State(MongoId sessionId) => Success(sessionId);

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
    /// Turns the wheel.
    ///
    /// The result is decided here and nowhere else, and it is decided before the
    /// client has drawn a frame. What the client does with it is presentation: it is
    /// handed the pocket and its position on the wheel and spins an animation that
    /// lands there.
    ///
    /// The cloth is cleared for the next spin on the *next* request rather than here,
    /// so the settled table -- including what every bet did -- can be looked at for as
    /// long as the player wants.
    /// </summary>
    public RouletteResponse Spin(MongoId sessionId)
    {
        var table = Table(sessionId);

        // A settled table is re-opened here rather than by its own route: a player
        // pressing spin again plainly means "another one", and making them clear the
        // last result first is a button that exists only to be pressed.
        if (table.Phase == SpinPhase.Settled)
        {
            table.NextSpin();
            return Success(sessionId);
        }

        try
        {
            var spin = table.Spin();

            log.Info(
                $"the ball landed in {spin.Result} [{sessionId}] -- "
                + $"{spin.Staked:N0} staked, {spin.Returned:N0} back");
        }
        catch (InvalidOperationException ex)
        {
            return Success(sessionId) with { Ok = false, Error = ex.Message };
        }

        return Success(sessionId);
    }

    /// <summary>Forgets the table entirely, chips and all. Nothing was taken, so nothing is owed.</summary>
    public RouletteResponse Leave(MongoId sessionId)
    {
        tables.Clear(sessionId);
        log.Detail($"left the table [{sessionId}]");

        return new RouletteResponse();
    }

    private RouletteTable Table(MongoId sessionId) =>
        tables.GetOrCreate(sessionId, () =>
        {
            log.Detail($"opened a table [{sessionId}]");
            return new RouletteTable(new RouletteRules(), new Random(), log.ForEngine());
        });

    private RouletteResponse Success(MongoId sessionId) =>
        new() { Table = TableView.Of(Table(sessionId)) };
}
