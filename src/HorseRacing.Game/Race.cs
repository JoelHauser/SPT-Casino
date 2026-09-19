namespace HorseRacing.Game;

/// <summary>What one bet on the slip did.</summary>
/// <param name="Bet">The bet as it was placed.</param>
/// <param name="Won">Whether the finishing order covered it.</param>
/// <param name="Returned">
/// Chips paid back, stake included. Zero on a loser -- the stake is already gone, and
/// a losing bet returning its own stake is the bug that turns a 6% edge into a
/// negative one.
/// </param>
public sealed record Settlement(Bet Bet, bool Won, int Returned);

/// <summary>
/// The result of a race: who finished where, and what the slip was worth.
/// </summary>
/// <param name="Order">
/// Every runner's saddlecloth number in finishing position. <c>Order[0]</c> won. The
/// full field is reported rather than just the placings, because the client draws
/// eight horses crossing a line and needs to know where the other five went.
/// </param>
/// <param name="Settlements">One per bet on the slip, in the order it was placed.</param>
/// <param name="Staked">The total that went in.</param>
/// <param name="Returned">The total that came back, stakes of winning bets included.</param>
public sealed record RaceResult(
    IReadOnlyList<int> Order,
    IReadOnlyList<Settlement> Settlements,
    int Staked,
    int Returned);

/// <summary>
/// Running a race and settling what was on it.
///
/// No SPT types, no Unity, no money -- chips in and chips out, the same boundary every
/// other engine in this repo draws. What a chip is worth belongs with the wallet.
/// </summary>
public static class Race
{
    /// <summary>
    /// Draws a finishing order.
    ///
    /// Weighted sampling without replacement: the winner is drawn in proportion to
    /// weight, removed, and the next drawn from what is left. That is the model
    /// <see cref="Odds"/> prices against, and the fact that both the draw and the
    /// board come from one description of the race is the only reason the stated edge
    /// is the real one.
    /// </summary>
    /// <param name="random">
    /// The source of randomness. Passed in rather than owned so a test can force a
    /// result -- and so the server can hold one seeded instance rather than newing up
    /// a <see cref="System.Random"/> per race, which on a fast machine hands
    /// consecutive races the same tick-seeded sequence.
    /// </param>
    public static int[] Draw(Random random)
    {
        ArgumentNullException.ThrowIfNull(random);

        var remaining = new List<Horse>(Field.Runners);
        var order = new int[remaining.Count];

        for (var position = 0; position < order.Length; position++)
        {
            var total = 0;

            foreach (var runner in remaining)
            {
                total += runner.Weight;
            }

            // NextDouble is [0, 1), so the cursor never reaches total and the final
            // fallback below is genuinely unreachable rather than merely unlikely.
            var cursor = random.NextDouble() * total;
            var chosen = remaining.Count - 1;

            for (var index = 0; index < remaining.Count; index++)
            {
                cursor -= remaining[index].Weight;

                if (cursor < 0.0)
                {
                    chosen = index;
                    break;
                }
            }

            order[position] = remaining[chosen].Number;
            remaining.RemoveAt(chosen);
        }

        return order;
    }

    /// <summary>
    /// Settles a slip against a finishing order.
    /// </summary>
    /// <remarks>
    /// Every bet is settled independently and none of them can see each other. That is
    /// worth stating because the obvious "optimisation" -- stop once a winner is found,
    /// since surely only one bet can win -- is false here and expensively so: a punter
    /// backing runner 3 to win, to place and to show collects all three when it wins.
    /// </remarks>
    public static RaceResult Settle(IReadOnlyList<Bet> slip, IReadOnlyList<int> order)
    {
        ArgumentNullException.ThrowIfNull(slip);
        ArgumentNullException.ThrowIfNull(order);

        var settlements = new List<Settlement>(slip.Count);
        var staked = 0;
        var returned = 0;

        foreach (var bet in slip)
        {
            var won = bet.Covers(order);
            var paid = won ? Odds.Returns(bet, order) : 0;

            settlements.Add(new Settlement(bet, won, paid));

            staked += bet.Stake;
            returned += paid;
        }

        return new RaceResult(order, settlements, staked, returned);
    }

    /// <summary>
    /// Runs a race and settles the slip against it, in one call.
    /// </summary>
    public static RaceResult Run(IReadOnlyList<Bet> slip, Random random)
        => Settle(slip, Draw(random));
}
