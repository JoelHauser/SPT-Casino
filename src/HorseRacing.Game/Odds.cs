namespace HorseRacing.Game;

/// <summary>A runner or a pair, and what the board pays it.</summary>
/// <param name="Kind">Which bet this is the price of.</param>
/// <param name="First">The runner backed.</param>
/// <param name="Second">The second runner, or zero for a one-runner bet.</param>
/// <param name="Chance">The exact probability this bet wins. Computed, never measured.</param>
/// <param name="Board">
/// What one chip returns in total if it wins, stake included. A board price of 1.00
/// would be a refund; the board never shows one.
/// </param>
public sealed record Price(BetKind Kind, int First, int Second, double Chance, double Board);

/// <summary>
/// The board, computed rather than measured.
///
/// This is horse racing's equivalent of roulette's 2.70%, and it is arrived at the
/// same way: by arithmetic over the model, not by running a hundred thousand races
/// and hoping the average settles down. A game whose return is only known
/// approximately is a game whose house edge nobody actually knows.
///
/// ## The arithmetic, since it is not obvious
///
/// The finishing order is drawn by weighted sampling without replacement, so an
/// ordered top-three (a, b, c) has probability
///
///     w_a / W  *  w_b / (W - w_a)  *  w_c / (W - w_a - w_b)
///
/// exactly, where W is the total weight. On a field of eight there are
/// 8 * 7 * 6 = 336 such prefixes, they are mutually exclusive, and their
/// probabilities sum to one. **Every bet this game takes is decided by the first
/// three home** -- win, place and show by construction, exacta and quinella by the
/// first two -- so walking those 336 prefixes and adding up the ones a bet covers
/// gives its exact chance. No enumeration of all 40,320 full orders is needed, and
/// no simulation.
///
/// ## Why the odds are computed with the same method that settles the bet
///
/// <see cref="Chance"/> asks <see cref="Bet.Covers"/> which prefixes win, and so does
/// the settlement. That is on purpose. The failure it removes is the one where the
/// board and the table quietly disagree about what a bet means -- a quinella priced as
/// an ordered pair and paid as an unordered one is a mispriced game that no balance
/// check would ever catch, because every individual payout is correct.
///
/// The tests check this against a Monte Carlo run anyway, because a formula that is
/// wrong in the same way as the code it describes proves nothing. That warning is
/// lifted from <c>SlotMachine.Game.Odds</c>, which earned it.
/// </summary>
public static class Odds
{
    /// <summary>
    /// The house's cut, before the board is rounded.
    ///
    /// Six percent. Between roulette's 2.70% and the slot machine's edge, and a long
    /// way kinder than any real track, where a tote takeout of 15-20% is ordinary.
    /// This is a single number applied identically to every bet on the board, which is
    /// the property worth protecting: a game where the exacta quietly carries three
    /// times the edge of the win bet is a game that punishes the players who read it
    /// most carefully.
    /// </summary>
    public const double Takeout = 0.06;

    /// <summary>
    /// The tick the board is quoted in, and paid in.
    ///
    /// Prices are rounded **down** to a whole number of these. Down rather than to the
    /// nearest, because rounding up would price some bets above fair and there is no
    /// house edge small enough to make that safe on the one bet that gets hammered.
    ///
    /// The rounding is applied to the price itself rather than to the payout, so the
    /// number on the board is the number that settles the bet. A board showing 3.60
    /// and a table paying 3.6127 is a lie that happens to be in the player's favour,
    /// and it is still a lie -- see <see cref="Realised"/>, which reports the edge the
    /// board actually carries rather than the one <see cref="Takeout"/> intended.
    /// </summary>
    public const double Tick = 0.01;

    /// <summary>
    /// The exact probability this bet wins, by enumeration over the 336 ordered
    /// top-threes.
    /// </summary>
    public static double Chance(BetKind kind, int first, int second = 0)
    {
        var bet = new Bet(kind, first, second, 0);

        if (!bet.IsWellFormed())
        {
            throw new ArgumentException($"not a bet this table takes: {kind} {first}/{second}.");
        }

        var chance = 0.0;

        foreach (var (order, probability) in Prefixes())
        {
            if (bet.Covers(order))
            {
                chance += probability;
            }
        }

        return chance;
    }

    /// <summary>
    /// What one chip returns in total if this bet wins, stake included.
    ///
    /// Fair would be <c>1 / chance</c>. The board pays <c>(1 - Takeout) / chance</c>,
    /// rounded down to a <see cref="Tick"/>.
    /// </summary>
    public static double BoardPrice(BetKind kind, int first, int second = 0)
    {
        var chance = Chance(kind, first, second);

        // Unreachable on the shipped card -- every runner has a positive weight, so
        // every bet on the board has a positive chance. Guarded anyway because the
        // alternative is an infinite price, and an infinite price paid on an int stake
        // is not a big win, it is an overflow.
        if (chance <= 0.0)
        {
            return 0.0;
        }

        var fair = (1.0 - Takeout) / chance;
        var ticks = Math.Floor(fair / Tick);

        return ticks * Tick;
    }

    /// <summary>
    /// What the punter is actually paid, in chips, stake included. Zero if the bet lost.
    ///
    /// Floors, for the same reason the price does: a fraction of a chip cannot be paid,
    /// and inventing one is inventing money. At a ten-thousand minimum this costs a
    /// winner less than one part in a million of their return.
    /// </summary>
    public static int Returns(Bet bet, IReadOnlyList<int> order)
    {
        ArgumentNullException.ThrowIfNull(bet);
        ArgumentNullException.ThrowIfNull(order);

        if (!bet.Covers(order))
        {
            return 0;
        }

        var price = BoardPrice(bet.Kind, bet.First, bet.Second);

        return (int)Math.Floor(bet.Stake * price);
    }

    /// <summary>
    /// The edge the board actually carries on this bet, after the price was rounded
    /// down to a tick.
    ///
    /// Always at least <see cref="Takeout"/> and never much above it. This exists so
    /// that the number can be *checked* rather than asserted: the rounding is a real
    /// effect on a real player's money, and a test that only ever compares against
    /// <see cref="Takeout"/> would not notice a tick coarse enough to double the edge
    /// on the short-priced favourite.
    /// </summary>
    public static double Realised(BetKind kind, int first, int second = 0)
        => 1.0 - (Chance(kind, first, second) * BoardPrice(kind, first, second));

    /// <summary>The whole board, in the order the slip shows it.</summary>
    public static IReadOnlyList<Price> All()
    {
        var prices = new List<Price>();

        foreach (var kind in new[] { BetKind.Win, BetKind.Place, BetKind.Show })
        {
            foreach (var runner in Field.Runners)
            {
                prices.Add(Quote(kind, runner.Number, 0));
            }
        }

        foreach (var first in Field.Runners)
        {
            foreach (var second in Field.Runners)
            {
                if (first.Number != second.Number)
                {
                    prices.Add(Quote(BetKind.Exacta, first.Number, second.Number));
                }

                // Quinella is unordered, so it is quoted once per pair rather than
                // twice. Quoting it both ways round would put two spots on the slip
                // that take the same money and pay the same price, which is how a
                // player ends up believing they are two different bets.
                if (first.Number < second.Number)
                {
                    prices.Add(Quote(BetKind.Quinella, first.Number, second.Number));
                }
            }
        }

        return prices;
    }

    private static Price Quote(BetKind kind, int first, int second)
        => new(kind, first, second, Chance(kind, first, second), BoardPrice(kind, first, second));

    /// <summary>
    /// Every ordered top-three and its exact probability.
    ///
    /// 336 of them on a field of eight. Their probabilities sum to one, which is worth
    /// knowing because it is the cheapest possible check that the model is coherent --
    /// and it is the first thing the tests assert.
    /// </summary>
    internal static IEnumerable<(int[] Order, double Probability)> Prefixes()
    {
        var total = (double)Field.TotalWeight;

        foreach (var a in Field.Runners)
        {
            var afterA = total - a.Weight;

            foreach (var b in Field.Runners)
            {
                if (b.Number == a.Number)
                {
                    continue;
                }

                var afterB = afterA - b.Weight;

                foreach (var c in Field.Runners)
                {
                    if (c.Number == a.Number || c.Number == b.Number)
                    {
                        continue;
                    }

                    var probability =
                        (a.Weight / total)
                        * (b.Weight / afterA)
                        * (c.Weight / afterB);

                    yield return ([a.Number, b.Number, c.Number], probability);
                }
            }
        }
    }
}
