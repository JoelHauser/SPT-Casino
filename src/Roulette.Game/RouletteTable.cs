using System.Text;

namespace Roulette.Game;

/// <summary>Where a spin has got to.</summary>
public enum SpinPhase
{
    /// <summary>Bets are open. Nothing has been staked yet or the last spin is done.</summary>
    Betting,

    /// <summary>The wheel has turned and the result is settled.</summary>
    Settled,
}

/// <summary>What one bet did on the spin that just happened.</summary>
/// <param name="Bet">The bet as placed.</param>
/// <param name="Won">Whether it came in.</param>
/// <param name="Returned">Stake plus winnings, or zero.</param>
public sealed record BetOutcome(Bet Bet, bool Won, int Returned);

/// <summary>
/// The result of one spin, settled.
/// </summary>
/// <param name="Result">The pocket the ball landed in.</param>
/// <param name="Position">
/// Where that pocket sits on the wheel, clockwise from the single zero. The client
/// needs it to land the animation on the right place and cannot work it out from the
/// number alone -- the wheel is not in numerical order.
/// </param>
/// <param name="Outcomes">Every bet that was on the cloth, and what it did.</param>
/// <param name="Staked">What went on the cloth.</param>
/// <param name="Returned">What came back, stake included.</param>
public sealed record SpinResult(
    Pocket Result,
    int Position,
    IReadOnlyList<BetOutcome> Outcomes,
    int Staked,
    int Returned)
{
    /// <summary>Up or down on the spin. Negative is the house winning.</summary>
    public int Profit => Returned - Staked;
}

/// <summary>
/// One player against the wheel.
///
/// House-banked, so unlike Poker there is no pot and no second player: every bet is
/// against the table and settles on its own rules. That makes the edge fixed
/// arithmetic rather than a skill gap, which is the thing Poker could not have --
/// see the notes on the mod being a faucet.
///
/// The table holds chips and knows nothing about currency. It is also the only place
/// that decides an outcome: the client is told where the ball landed and animates
/// towards it, never the other way round.
/// </summary>
public sealed class RouletteTable
{
    private readonly List<Bet> _bets = [];
    private readonly Random _rng;
    private readonly IGameLog _log;

    public RouletteTable(RouletteRules? rules = null, Random? rng = null, IGameLog? log = null)
    {
        Rules = rules ?? new RouletteRules();
        Wheel = new Wheel(Rules.Wheel);
        _rng = rng ?? new Random();
        _log = log ?? GameLog.Null;
    }

    public RouletteRules Rules { get; }

    public Wheel Wheel { get; }

    public SpinPhase Phase { get; private set; } = SpinPhase.Betting;

    /// <summary>What is on the cloth right now.</summary>
    public IReadOnlyList<Bet> Bets => _bets;

    /// <summary>The last spin, or null if the wheel has not turned yet.</summary>
    public SpinResult? Last { get; private set; }

    public int Staked => _bets.Sum(b => b.Amount);

    /// <summary>
    /// Puts a bet on the cloth.
    ///
    /// Bets on the same spot stack rather than replacing each other, which is what
    /// happens at a table when a second chip is put down and is also what makes the
    /// client's job easy: it sends what the player just did, not the whole cloth.
    /// </summary>
    /// <exception cref="InvalidOperationException">The wheel has already turned.</exception>
    /// <exception cref="ArgumentException">The bet is not one the table takes.</exception>
    public void Place(Bet bet)
    {
        if (Phase != SpinPhase.Betting)
        {
            throw new InvalidOperationException("The wheel has turned. Clear the last spin first.");
        }

        Validate(bet);

        var existing = _bets.FindIndex(b => b.Kind == bet.Kind && b.Selection == bet.Selection);
        var stakeOnSpot = bet.Amount + (existing >= 0 ? _bets[existing].Amount : 0);

        // Checked against the total on the spot rather than the chip just added, or
        // the cap is only a cap on the first chip.
        var max = Rules.MaxFor(bet.Kind);
        if (stakeOnSpot > max)
        {
            throw new ArgumentException(
                $"{bet.Kind} takes at most {max:N0}; that would make {stakeOnSpot:N0}.", nameof(bet));
        }

        if (Staked + bet.Amount > Rules.MaxTotalStake)
        {
            throw new ArgumentException(
                $"The table takes at most {Rules.MaxTotalStake:N0} on one spin.", nameof(bet));
        }

        if (existing >= 0)
        {
            _bets[existing] = _bets[existing] with { Amount = stakeOnSpot };
        }
        else
        {
            if (_bets.Count >= Rules.MaxBets)
            {
                throw new ArgumentException(
                    $"There is only room for {Rules.MaxBets} bets on the cloth.", nameof(bet));
            }

            _bets.Add(bet);
        }

        if (_log.Enabled)
        {
            _log.Write($"placed {bet.Amount:N0} on {Describe(bet)}, {Staked:N0} on the cloth");
        }
    }

    /// <summary>
    /// Takes chips back off one spot.
    ///
    /// Reduces rather than removes, because a spot can be built up a chip at a time and
    /// taking the lot off would be a surprising answer to picking one back up. Asking
    /// for more than is there takes what is there rather than refusing -- a player
    /// lifting a chip bigger than the pile means "take it off", not "do nothing".
    /// </summary>
    /// <returns>What came back off the cloth.</returns>
    public int Remove(BetKind kind, int selection, int amount)
    {
        if (Phase != SpinPhase.Betting)
        {
            throw new InvalidOperationException("The wheel has turned.");
        }

        var index = _bets.FindIndex(b => b.Kind == kind && b.Selection == selection);

        if (index < 0)
        {
            return 0;
        }

        var taken = Math.Min(amount <= 0 ? _bets[index].Amount : amount, _bets[index].Amount);
        var left = _bets[index].Amount - taken;

        if (left <= 0)
        {
            _bets.RemoveAt(index);
        }
        else
        {
            _bets[index] = _bets[index] with { Amount = left };
        }

        if (_log.Enabled)
        {
            _log.Write($"took {taken:N0} off {Describe(new Bet(kind, selection, taken))}, {Staked:N0} left");
        }

        return taken;
    }

    /// <summary>Takes everything back off the cloth. Only while bets are open.</summary>
    public int ClearBets()
    {
        if (Phase != SpinPhase.Betting)
        {
            throw new InvalidOperationException("The wheel has turned.");
        }

        var back = Staked;
        _bets.Clear();

        if (_log.Enabled)
        {
            _log.Write($"cleared the cloth, {back:N0} back");
        }

        return back;
    }

    /// <summary>
    /// Turns the wheel and settles every bet.
    ///
    /// The result is decided here and nowhere else. What the client does with it is
    /// presentation: it is handed the pocket and its position and spins an animation
    /// that lands there, which is why <see cref="SpinResult.Position"/> is part of
    /// the result rather than something the client works out.
    /// </summary>
    public SpinResult Spin()
    {
        if (Phase != SpinPhase.Betting)
        {
            throw new InvalidOperationException("The wheel has already turned.");
        }

        if (_bets.Count == 0)
        {
            throw new InvalidOperationException("Nothing is on the cloth.");
        }

        return Settle(Wheel.Spin(_rng));
    }

    /// <summary>
    /// Test seam: settle against a pocket chosen rather than spun, the same idea as
    /// a stacked deck. Internal because only a table may decide a real result.
    /// </summary>
    internal SpinResult SettleOn(int number) => Settle(Wheel.PocketFor(number));

    private SpinResult Settle(Pocket result)
    {
        var outcomes = _bets
            .Select(bet => new BetOutcome(bet, bet.Wins(Wheel, result), Payouts.Returned(bet, Wheel, result)))
            .ToList();

        var spin = new SpinResult(
            result,
            Wheel.PositionOf(result.Number),
            outcomes,
            _bets.Sum(b => b.Amount),
            outcomes.Sum(o => o.Returned));

        Last = spin;
        Phase = SpinPhase.Settled;

        if (_log.Enabled)
        {
            _log.Write(
                $"the ball landed in {result} at position {spin.Position}; "
                + $"{spin.Staked:N0} staked, {spin.Returned:N0} back");
        }

        return spin;
    }

    /// <summary>
    /// Clears the cloth and opens betting again. The bets do not carry over: leaving
    /// them on would stake a player's money on a spin they never asked for.
    /// </summary>
    public void NextSpin()
    {
        _bets.Clear();
        Phase = SpinPhase.Betting;
    }

    private void Validate(Bet bet)
    {
        if (bet.Amount <= 0)
        {
            throw new ArgumentException("A bet needs chips on it.", nameof(bet));
        }

        if (bet.Amount < Rules.MinBet)
        {
            throw new ArgumentException(
                $"The smallest bet is {Rules.MinBet:N0}; {bet.Amount:N0} is less.", nameof(bet));
        }

        if (bet.Amount % Rules.Step != 0)
        {
            throw new ArgumentException(
                $"Bets go up in {Rules.Step:N0}; {bet.Amount:N0} does not.", nameof(bet));
        }

        var ok = bet.Kind switch
        {
            BetKind.Straight => IsPocket(bet.Selection),

            // An index into the enumerated splits rather than a number. See Layout.
            BetKind.Split => bet.Selection >= 0 && bet.Selection < Layout.Splits.Count,

            // A street is a row, so it starts on one.
            BetKind.Street => Layout.Streets.Contains(bet.Selection),

            // A corner needs a square, so not on the bottom row and not in the last
            // column.
            BetKind.Corner => Layout.Corners.Contains(bet.Selection),

            // Six lines start on a row and need a whole row after them.
            BetKind.SixLine => Layout.SixLines.Contains(bet.Selection),

            BetKind.Column or BetKind.Dozen => bet.Selection is >= 1 and <= 3,

            BetKind.TopLine => Rules.Wheel == WheelKind.American,

            _ => true,
        };

        if (!ok)
        {
            throw new ArgumentException(
                $"{bet.Kind} cannot be placed on {bet.Selection}"
                + (bet.Kind == BetKind.TopLine ? " -- there is no top line on a European wheel." : "."),
                nameof(bet));
        }
    }

    private bool IsPocket(int number) =>
        number is >= 0 and <= 36
        || (number == Pocket.DoubleZero && Rules.Wheel == WheelKind.American);

    /// <summary>A bet in words, for the log and the table-side reading.</summary>
    public static string Describe(Bet bet) => bet.Kind switch
    {
        BetKind.Straight => $"{(bet.Selection == Pocket.DoubleZero ? "00" : bet.Selection.ToString())} straight up",
        BetKind.Split => $"the {Layout.Splits[bet.Selection].Low}-{Layout.Splits[bet.Selection].High} split",
        BetKind.Street => $"the street from {bet.Selection}",
        BetKind.Corner => $"the corner on {bet.Selection}",
        BetKind.SixLine => $"the six line from {bet.Selection}",
        BetKind.Column => $"column {bet.Selection}",
        BetKind.Dozen => bet.Selection switch
        {
            1 => "1 to 12",
            2 => "13 to 24",
            _ => "25 to 36",
        },
        BetKind.TopLine => "the top line",
        _ => bet.Kind.ToString().ToLowerInvariant(),
    };

    public override string ToString()
    {
        var text = new StringBuilder();
        text.Append(Phase == SpinPhase.Betting ? "betting" : "settled");
        text.Append($", {_bets.Count} bet(s), {Staked:N0} staked");

        if (Last is not null)
        {
            text.Append($", last {Last.Result}");
        }

        return text.ToString();
    }
}
