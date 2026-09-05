namespace Poker.Game;

/// <summary>
/// How one seat-mate departs from correct play.
///
/// Character is a dial on the strategy, never a second strategy. A seat that decides
/// by its own logic is a seat nobody can debug, and the moment two of them disagree
/// about the same hand there is no way to say which is wrong.
///
/// All zeroes is <see cref="Correct"/> -- the oracle. Everything else is that,
/// nudged.
/// </summary>
/// <param name="Name">Shown at the seat.</param>
/// <param name="Looseness">
/// How often the seat backs a hand the strategy would check, before the river.
/// Loose players are the ones who look like they are having fun.
/// </param>
/// <param name="Caution">
/// How often the seat folds a river the strategy would bet. This is the expensive
/// mistake in UTH and the one real players make most.
/// </param>
/// <param name="Timidity">
/// How often the seat takes the small pre-flop raise instead of the large one. Never
/// correct -- 3x is strictly worse than 4x with a hand worth raising -- which is
/// exactly why it reads as a person rather than a machine.
/// </param>
public sealed record SeatPersonality(
    string Name,
    double Looseness = 0,
    double Caution = 0,
    double Timidity = 0)
{
    /// <summary>Plays the published strategy exactly. The house-edge oracle.</summary>
    public static SeatPersonality Correct { get; } = new("Correct");

    /// <summary>A cast for a five-handed table. Seat 0 is the player, so four is enough.</summary>
    public static IReadOnlyList<SeatPersonality> Cast { get; } =
    [
        new("Steady", Looseness: 0.05, Caution: 0.05),
        new("Loose", Looseness: 0.35, Caution: 0.05, Timidity: 0.15),
        new("Nervy", Looseness: 0.05, Caution: 0.35),
        new("Reckless", Looseness: 0.50, Caution: 0.10, Timidity: 0.30),
    ];
}

/// <summary>
/// A seat-mate. Plays <see cref="UthStrategy"/>, bent by a
/// <see cref="SeatPersonality"/>.
///
/// The RNG is a constructor parameter for the same reason the deck's is: without it
/// a table cannot be pinned by a test, and a bot that cannot be pinned cannot be
/// shown to be following its own rules.
///
/// Note what this class cannot reach. Its only input is a <see cref="SeatContext"/>,
/// which carries the seat's own cards and the board -- there is no route from here to
/// the dealer's hand, the player's, another seat's, or to any money. That is
/// structural rather than a promise: a bot cannot cheat with information it was never
/// handed, and cannot touch a stash it has no reference to.
/// </summary>
public sealed class SeatMateAgent(SeatPersonality? personality = null, Random? rng = null, IGameLog? log = null)
    : ISeatAgent
{
    private readonly SeatPersonality _personality = personality ?? SeatPersonality.Correct;
    private readonly Random _rng = rng ?? new Random();
    private readonly IGameLog _log = log ?? GameLog.Null;

    public SeatPersonality Personality => _personality;

    public SeatDecision Decide(SeatContext context)
    {
        var hole = context.Seat.Cards;

        var decision = context.Street switch
        {
            Street.PreFlop => PreFlop(hole, context),
            Street.Flop => Flop(hole, context),
            _ => River(hole, context),
        };

        if (_log.Enabled)
        {
            _log.Write($"{context.Seat.Name} ({_personality.Name}): {decision}");
        }

        return decision;
    }

    private SeatDecision PreFlop(IReadOnlyList<Card> hole, SeatContext context)
    {
        // Both dials are rolled before either is consulted. Short-circuiting here
        // would make the number of draws depend on the decision, and a seeded table
        // would then deal differently the moment a dial was retuned -- which turns
        // every pinned multi-seat test into a liar.
        var loose = Rolls(_personality.Looseness, "loose");
        var timid = Rolls(_personality.Timidity, "timid");

        if (!UthStrategy.RaisesOnHoleCards(hole[0], hole[1], _log) && !loose)
        {
            return SeatDecision.Check;
        }

        // The large multiple is the only correct size, so the small one is a timid
        // seat's mistake and nothing else. Legal multiples arrive largest first,
        // which is what makes this an index rather than a search.
        var multiples = context.LegalMultiples;

        return SeatDecision.Play(timid && multiples.Count > 1 ? multiples[^1] : multiples[0]);
    }

    private SeatDecision Flop(IReadOnlyList<Card> hole, SeatContext context)
    {
        var loose = Rolls(_personality.Looseness, "loose");

        return UthStrategy.RaisesOnFlop(hole, context.Community, _log) || loose
            ? SeatDecision.Play(context.LegalMultiples[0])
            : SeatDecision.Check;
    }

    private SeatDecision River(IReadOnlyList<Card> hole, SeatContext context)
    {
        // Folding is wrong in both directions here, so both dials apply: a cautious
        // seat gives up hands it should back, a loose one backs hands it should not.
        var cautious = Rolls(_personality.Caution, "cautious");
        var loose = Rolls(_personality.Looseness, "loose");

        var bet = UthStrategy.BetsOnRiver(hole, context.Community, context.Rules, _log);
        bet = bet ? !cautious : loose;

        return bet ? SeatDecision.Play(context.LegalMultiples[0]) : SeatDecision.Fold;
    }

    /// <summary>
    /// Rolls a dial. Always draws, even at zero, so that a personality cannot change
    /// how many numbers come out of the RNG -- otherwise every seat after the first
    /// one on a seeded table would deal differently the moment a dial was retuned.
    /// </summary>
    private bool Rolls(double chance, string why)
    {
        var roll = _rng.NextDouble();
        var hit = roll < chance;

        if (hit && _log.Enabled)
        {
            _log.Write($"  ...being {why} ({roll:F2} under {chance:F2})");
        }

        return hit;
    }
}
