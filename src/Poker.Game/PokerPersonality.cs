namespace Poker.Game;

/// <summary>
/// What kind of player a seat is.
///
/// Five dials, and between them they cover the range real players actually occupy.
/// The classic taxonomy is two of these crossed -- tight or loose, passive or
/// aggressive -- which gives the rock, the grinder, the calling station and the
/// maniac. The other three add the texture that makes two players of the same type
/// still feel different: whether they bluff, whether they will put a stack in, and
/// whether they notice where they are sitting.
///
/// Every dial runs 0 to 1 and every one of them is *applied to the same decision
/// procedure*. That is deliberate. A seat that decides by its own logic is a seat
/// nobody can debug, and when two of them disagree about a hand there is no way to
/// say which one is wrong.
/// </summary>
/// <param name="Name">Shown at the seat.</param>
/// <param name="Tightness">
/// How much better than the pot price a hand must be before this seat will put money
/// in. A rock wants a wide margin; a calling station will take almost any price.
/// </param>
/// <param name="Aggression">
/// Whether it raises or merely calls, and how large it bets when it does. This is the
/// dial that decides whether a hand is *played* or *paid off*.
/// </param>
/// <param name="Bluff">How often it bets a hand that cannot win if it is called.</param>
/// <param name="Risk">
/// Willingness to get a whole stack in. High risk shoves short stacks and jams draws;
/// low risk keeps pots small and folds rather than gamble.
/// </param>
/// <param name="Positional">
/// How much acting last is worth to it. Good players lean on position heavily; weak
/// ones play the same hand the same way wherever they are sitting, which is the most
/// reliable way to spot one.
/// </param>
/// <param name="Steadiness">
/// How little results move the rest of the dials. At 1 a seat plays the same way
/// after losing three stacks as it did on the first hand; at 0 it is all temperament
/// and no memory of what it meant to be.
///
/// Which *way* a seat tilts is not a dial of its own -- it falls out of
/// <paramref name="Risk"/>. Gamblers steam: they loosen up and start swinging to get
/// it back. Careful players do the opposite and shut down. Both are real and the two
/// are what a table actually looks like after a big pot.
/// </param>
public sealed record PokerPersonality(
    string Name,
    double Tightness,
    double Aggression,
    double Bluff,
    double Risk,
    double Positional,
    double Steadiness = 0.5)
{
    /// <summary>Nothing exaggerated. A reference point rather than a character.</summary>
    public static PokerPersonality Balanced { get; } = new("Balanced", 0.50, 0.50, 0.20, 0.50, 0.50);

    /// <summary>
    /// The table. Deliberately spread wide: the point is that the seats do not play
    /// alike, and a player should be able to tell them apart after a few orbits
    /// without ever being told which is which.
    /// </summary>
    public static IReadOnlyList<PokerPersonality> Cast { get; } =
    [
        // Waits all night for a hand and then bets it like an apology. Easy to play
        // against once you notice, which is the point of having one at the table.
        new("Rock", Tightness: 0.95, Aggression: 0.15, Bluff: 0.02, Risk: 0.15, Positional: 0.30, Steadiness: 0.85),

        // The competent regular: few hands, played hard, and very aware of where it
        // is sitting.
        new("Grinder", Tightness: 0.75, Aggression: 0.70, Bluff: 0.25, Risk: 0.50, Positional: 0.85, Steadiness: 0.8),

        // Calls everything, raises nothing, bluffs never. Impossible to bluff and
        // impossible to lose much to.
        new("Station", Tightness: 0.18, Aggression: 0.08, Bluff: 0.02, Risk: 0.35, Positional: 0.10, Steadiness: 0.7),

        // Plays every hand and bets every street. Will hand over a stack and take one
        // back twenty minutes later.
        new("Maniac", Tightness: 0.10, Aggression: 0.95, Bluff: 0.62, Risk: 0.85, Positional: 0.40, Steadiness: 0.1),

        // The best seat at the table: tight enough, aggressive, bluffs credibly, and
        // punishes position.
        new("Shark", Tightness: 0.68, Aggression: 0.82, Bluff: 0.42, Risk: 0.60, Positional: 0.95, Steadiness: 0.92),

        // Here for the night out. Average everything and no idea where the button is.
        new("Tourist", Tightness: 0.40, Aggression: 0.64, Bluff: 0.18, Risk: 0.45, Positional: 0.12, Steadiness: 0.35),

        // Would rather find out now. Loves a shove and does not much mind the price.
        new("Gambler", Tightness: 0.28, Aggression: 0.78, Bluff: 0.35, Risk: 0.98, Positional: 0.25, Steadiness: 0.05),

        // There was an eighth here, an "Owl" -- tight, patient, enormous when it
        // finally played. It came out folding 74% and raising 2%, which is the Rock
        // with a different name on it, and its real trait was about bet *sizing*
        // rather than about how often it acted. Seven characters that genuinely
        // differ beat eight where two are the same person, and only four of them are
        // ever at a table at once.
    ];

    /// <summary>
    /// Picks distinct characters for a table, so no two seats are the same person.
    /// That was the parked UTH table's mistake and it is the one thing that most
    /// stops a table reading as a room full of players.
    /// </summary>
    public static IReadOnlyList<PokerPersonality> Deal(int count, Random rng)
    {
        if (count > Cast.Count)
        {
            throw new ArgumentOutOfRangeException(
                nameof(count), count, $"There are only {Cast.Count} characters to go round.");
        }

        return [.. Cast.OrderBy(_ => rng.Next()).Take(count)];
    }

    /// <summary>
    /// Mixes two characters. <paramref name="towards"/> of 0 is this one unchanged, 1
    /// is the other, and anything between is a person who is a bit of both.
    ///
    /// This works only because every character runs the same procedure and differs
    /// only in its numbers. Eight separate decision procedures could not be blended
    /// at all -- there would be nothing to interpolate. It is the whole return on
    /// having built it as dials.
    /// </summary>
    public PokerPersonality Blend(PokerPersonality other, double towards, string? name = null)
    {
        var mix = Math.Clamp(towards, 0, 1);

        double Mix(double from, double to) => from + ((to - from) * mix);

        return new PokerPersonality(
            name ?? (mix < 0.5 ? Name : other.Name),
            Mix(Tightness, other.Tightness),
            Mix(Aggression, other.Aggression),
            Mix(Bluff, other.Bluff),
            Mix(Risk, other.Risk),
            Mix(Positional, other.Positional),
            Mix(Steadiness, other.Steadiness));
    }

    /// <summary>Nudges every dial, keeping each inside its range.</summary>
    public PokerPersonality Shift(
        double tightness = 0,
        double aggression = 0,
        double bluff = 0,
        double risk = 0,
        double positional = 0) =>
        this with
        {
            Tightness = Math.Clamp(Tightness + tightness, 0, 1),
            Aggression = Math.Clamp(Aggression + aggression, 0, 1),
            Bluff = Math.Clamp(Bluff + bluff, 0, 1),
            Risk = Math.Clamp(Risk + risk, 0, 1),
            Positional = Math.Clamp(Positional + positional, 0, 1),
        };

    /// <summary>
    /// Invents somebody. Two archetypes crossed at a random weight, then jittered, so
    /// the seats are drawn from a continuum rather than from a list of eight.
    ///
    /// The named cast are landmarks, not the population. A table filled from here
    /// gets a player who is mostly a grinder with a streak of gambler in them, which
    /// is what actual people are like -- and means the same eight names never turn up
    /// playing the same eight ways.
    /// </summary>
    public static PokerPersonality Improvise(Random rng, string? name = null)
    {
        var first = Cast[rng.Next(Cast.Count)];
        var second = Cast[rng.Next(Cast.Count)];

        var blended = first.Blend(second, rng.NextDouble(), name ?? Nickname(first, second, rng));

        double Jitter() => (rng.NextDouble() - 0.5) * 0.16;

        return blended.Shift(Jitter(), Jitter(), Jitter(), Jitter(), Jitter()) with
        {
            Steadiness = Math.Clamp(blended.Steadiness + Jitter(), 0, 1),
        };
    }

    private static string Nickname(PokerPersonality first, PokerPersonality second, Random rng) =>
        first.Name == second.Name ? first.Name : $"{first.Name}/{second.Name}";

    public override string ToString() =>
        $"{Name} (tight {Tightness:F2}, aggr {Aggression:F2}, bluff {Bluff:F2}, "
        + $"risk {Risk:F2}, pos {Positional:F2}, steady {Steadiness:F2})";
}
