namespace HorseRacing.Game;

/// <summary>
/// The kinds of bet the window takes.
///
/// Five, and the line was drawn deliberately. Win, Place and Show are one runner
/// each and are what anybody who has never bet a race already understands. Exacta and
/// Quinella are two runners and are where the money is. Trifecta was left off: it is
/// three hundred and thirty-six spots on a field of eight, which is a betting slip
/// nobody can read, and its price is long enough that the rounding in
/// <see cref="Odds"/> starts to matter.
/// </summary>
public enum BetKind
{
    /// <summary>The runner finishes first.</summary>
    Win,

    /// <summary>The runner finishes first or second.</summary>
    Place,

    /// <summary>The runner finishes first, second or third.</summary>
    Show,

    /// <summary>These two runners finish first and second, in the order named.</summary>
    Exacta,

    /// <summary>These two runners finish first and second, in either order.</summary>
    Quinella,
}

/// <summary>
/// One bet on the slip.
/// </summary>
/// <param name="Kind">What is being bet.</param>
/// <param name="First">
/// The runner backed. For <see cref="BetKind.Exacta"/> this is the one named to win;
/// for <see cref="BetKind.Quinella"/> it is simply the lower of the pair, since the
/// order does not mean anything there.
/// </param>
/// <param name="Second">
/// The second runner, for the two-runner bets. Zero for the rest -- not a runner
/// number, and <see cref="Field.Has"/> rejects it, which is what makes an ignored
/// field a loud mistake rather than a quiet bet on runner zero.
/// </param>
/// <param name="Stake">
/// In chips. The engine takes an int and returns an int and has no idea what a rouble
/// is -- the same boundary <c>RouletteRules</c> draws, for the same reason.
/// </param>
public sealed record Bet(BetKind Kind, int First, int Second, int Stake)
{
    /// <summary>A one-runner bet.</summary>
    public static Bet On(BetKind kind, int runner, int stake) => new(kind, runner, 0, stake);

    /// <summary>
    /// Whether this bet names two runners, and therefore uses <see cref="Second"/>.
    /// </summary>
    public bool IsPair => Kind is BetKind.Exacta or BetKind.Quinella;

    /// <summary>
    /// Whether the bet is one the table can settle at all.
    ///
    /// Checked before a stake is taken, never after the race. A bet that cannot be
    /// settled must be refused at the window, because the alternative is holding money
    /// against a result that will never match -- which looks exactly like a loss to
    /// whoever placed it.
    /// </summary>
    public bool IsWellFormed()
    {
        if (!Field.Has(First))
        {
            return false;
        }

        if (!IsPair)
        {
            // Second is unused, and must be left at zero rather than merely ignored:
            // an exacta whose Kind was mistyped as Win would otherwise settle as a
            // win bet and silently discard the second leg.
            return Second == 0;
        }

        // A runner cannot beat itself into second.
        return Field.Has(Second) && Second != First;
    }

    /// <summary>
    /// Whether this bet wins on the given finishing order.
    /// </summary>
    /// <param name="order">
    /// Saddlecloth numbers, in finishing position: <c>order[0]</c> won.
    /// </param>
    /// <remarks>
    /// **Assert which orders a bet covers, not how many.** Roulette learned this the
    /// expensive way -- two mutation faults survived a first pass because the tests
    /// counted the numbers a bet covered instead of reading them, and a bet covering
    /// the right *number* of wrong things passes that test every time. The same test
    /// applies here and the same mistake is available.
    /// </remarks>
    public bool Covers(IReadOnlyList<int> order)
    {
        ArgumentNullException.ThrowIfNull(order);

        return Kind switch
        {
            BetKind.Win => Placed(order, First, 1),
            BetKind.Place => Placed(order, First, 2),
            BetKind.Show => Placed(order, First, 3),

            BetKind.Exacta => order.Count >= 2
                && order[0] == First
                && order[1] == Second,

            // Either way round. The pair is held with First as the lower number by
            // convention, but nothing here depends on that -- a quinella built the
            // other way round settles identically, and a convention the settlement
            // relies on is a convention that eventually gets broken by a caller.
            BetKind.Quinella => order.Count >= 2
                && ((order[0] == First && order[1] == Second)
                    || (order[0] == Second && order[1] == First)),

            _ => false,
        };
    }

    /// <summary>Whether the runner finished in the first <paramref name="places"/>.</summary>
    private static bool Placed(IReadOnlyList<int> order, int runner, int places)
    {
        var limit = Math.Min(places, order.Count);

        for (var position = 0; position < limit; position++)
        {
            if (order[position] == runner)
            {
                return true;
            }
        }

        return false;
    }
}
