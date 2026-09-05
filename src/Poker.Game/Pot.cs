namespace Poker.Game;

/// <summary>What one seat put into the pot over the whole hand.</summary>
/// <param name="Seat">Seat index, used to name eligibility rather than to order anything.</param>
/// <param name="Amount">Chips committed across every street of this hand.</param>
/// <param name="Folded">Folded seats still pay what they put in; they just cannot win it back.</param>
public readonly record struct Contribution(int Seat, int Amount, bool Folded);

/// <summary>
/// One pot and the seats allowed to win it. A hand with no all-ins produces
/// exactly one of these.
/// </summary>
public sealed record Pot(int Amount, IReadOnlyList<int> EligibleSeats)
{
    public override string ToString() =>
        $"{Amount} for [{string.Join(", ", EligibleSeats)}]";
}

/// <summary>
/// The pots a hand settles into, plus any bet that was returned uncalled.
/// </summary>
public sealed record PotLayout(IReadOnlyList<Pot> Pots, IReadOnlyDictionary<int, int> Refunds)
{
    public int Total => Pots.Sum(pot => pot.Amount);
}

/// <summary>
/// Splits what everyone committed into a main pot and however many side pots the
/// all-ins require.
///
/// This is the piece of hold'em most likely to quietly lose or invent money, and
/// it is the reason this project cannot reuse blackjack's settlement: there, one
/// stake belonged to one hand and came back multiplied. Here, several stacks of
/// different sizes are pooled and a short stack can only win the part of the pot
/// it could actually cover.
///
/// Kept free of <see cref="Seat"/> on purpose. Taking plain contributions means the
/// whole thing is exercised from a table of numbers, without dealing a hand.
/// </summary>
public static class PotBuilder
{
    /// <summary>
    /// Splits the contributions into pots.
    ///
    /// <paramref name="log"/> records each decision -- what was refunded, what each
    /// layer came to and who could win it. This is money code, so the log is a test
    /// surface rather than a convenience: a layout can total correctly with two
    /// layers' eligibility swapped, and only the reasoning shows it.
    /// </summary>
    public static PotLayout Build(IReadOnlyList<Contribution> contributions, IGameLog? log = null)
    {
        log ??= GameLog.Null;

        var refunds = new Dictionary<int, int>();
        var committed = contributions.ToDictionary(c => c.Seat, c => c.Amount);

        if (log.Enabled)
        {
            log.Write(
                "pot: building from "
                + string.Join(", ", contributions.Select(c => $"seat {c.Seat} {c.Amount}{(c.Folded ? " (folded)" : string.Empty)}")));
        }

        // An uncalled bet is returned before any pot is built. Without this, a raise
        // nobody covered would be won by its own maker -- money in and straight back
        // out, but reported as a pot they won, which makes every stat downstream wrong.
        //
        // The second-highest commitment is the most anyone actually matched, so
        // whatever the top seat put in beyond it was never in play. Folded seats count
        // here: a blind that folds still set a level the raiser was called to.
        var ordered = contributions.OrderByDescending(c => c.Amount).ToList();
        if (ordered.Count > 0)
        {
            var top = ordered[0];
            var matched = ordered.Count > 1 ? ordered[1].Amount : 0;
            if (top.Amount > matched)
            {
                refunds[top.Seat] = top.Amount - matched;
                committed[top.Seat] = matched;

                if (log.Enabled)
                {
                    log.Write(
                        $"pot: refunding {top.Amount - matched} to seat {top.Seat} -- "
                        + $"bet {top.Amount} but only {matched} was ever matched");
                }
            }
        }

        // Each distinct commitment is a ceiling somebody could not bet past, so it
        // closes a pot. Walking them in order peels the pot off in layers.
        var levels = committed.Values.Where(amount => amount > 0).Distinct().Order().ToList();

        var pots = new List<Pot>();
        var previous = 0;

        foreach (var level in levels)
        {
            var contributors = committed.Where(pair => pair.Value >= level).Select(pair => pair.Key).ToList();
            var amount = (level - previous) * contributors.Count;
            previous = level;

            if (amount == 0)
            {
                continue;
            }

            var eligible = contributors
                .Where(seat => !contributions.First(c => c.Seat == seat).Folded)
                .Order()
                .ToList();

            if (log.Enabled)
            {
                log.Write(
                    $"pot: layer to {level} is {amount} from {contributors.Count} seat(s), "
                    + $"winnable by [{string.Join(", ", eligible)}]");
            }

            pots.Add(new Pot(amount, eligible));
        }

        return new PotLayout(Collapse(pots, log), refunds);
    }

    /// <summary>
    /// Folds the chips of a layer nobody can win into the layer below it.
    ///
    /// A layer loses every eligible seat when all of its contributors fold above a
    /// short stack's all-in. Those chips are not returned -- they were called -- so
    /// they belong to whoever wins the pot those players were contesting, which is
    /// the layer beneath. Leaving the empty layer in place would strand real money
    /// in a pot with no winner, and the hand would settle for less than it collected.
    /// </summary>
    private static List<Pot> Collapse(List<Pot> pots, IGameLog log)
    {
        var collapsed = new List<Pot>();

        foreach (var pot in pots)
        {
            if (pot.EligibleSeats.Count > 0)
            {
                collapsed.Add(pot);
                continue;
            }

            if (collapsed.Count > 0)
            {
                var last = collapsed[^1];
                collapsed[^1] = last with { Amount = last.Amount + pot.Amount };

                if (log.Enabled)
                {
                    log.Write(
                        $"pot: {pot.Amount} had no seat left to win it -- folded into the layer below, "
                        + $"now {collapsed[^1].Amount} for [{string.Join(", ", last.EligibleSeats)}]");
                }
            }
            else
            {
                // Nothing beneath to fold into, so hold it and let the next layer
                // with a live seat absorb it.
                collapsed.Add(pot);
            }
        }

        // A leading unwinnable layer, if one survived, joins the first real pot.
        if (collapsed.Count > 1 && collapsed[0].EligibleSeats.Count == 0)
        {
            var orphan = collapsed[0];
            collapsed.RemoveAt(0);
            collapsed[0] = collapsed[0] with { Amount = collapsed[0].Amount + orphan.Amount };

            if (log.Enabled)
            {
                log.Write(
                    $"pot: leading layer of {orphan.Amount} had no winner -- absorbed into the first real pot");
            }
        }

        if (log.Enabled)
        {
            log.Write($"pot: settled into {collapsed.Count} pot(s) -- {string.Join(" | ", collapsed)}");
        }

        return collapsed;
    }
}
