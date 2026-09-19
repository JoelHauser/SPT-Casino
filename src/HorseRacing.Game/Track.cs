namespace HorseRacing.Game;

/// <summary>
/// One course, and the card it produces.
///
/// ## A track is a way of weighting the same eight horses
///
/// It holds no runners of its own. <see cref="WeightOf"/> turns a horse's speed and
/// stamina into its share of *this* race, and everything else -- every price on the
/// board, the draw itself -- follows from those weights. A short track multiplies
/// speed; a long one multiplies stamina; the horses never change.
///
/// ## The threshold is what makes a card interesting
///
/// Without it the weights would be raw scores like 69 and 29, a spread of barely two
/// to one, and every runner would be priced within a whisker of every other. Taking a
/// fixed amount off every score before using it stretches what is left: the same two
/// horses become 45 and 5, a spread of nine to one, which is roughly what a real card
/// looks like.
///
/// It is subtraction rather than a power curve because it keeps the weights whole
/// numbers, and whole numbers are what let <see cref="Odds"/> state a chance as an
/// exact ratio rather than a float that has already been rounded twice.
///
/// **The floor is load-bearing.** A threshold high enough to be interesting is high
/// enough to take a bad horse negative, and a negative weight is a runner that makes
/// the field's total smaller by entering it -- which would quietly corrupt every other
/// price on the board rather than failing. <c>TrackTests</c> asserts no shipped track
/// actually reaches the floor, so it is a guard rather than something in use.
/// </summary>
/// <param name="Id">Stable, lowercase, and what the wire sends. Never shown.</param>
/// <param name="Name">As printed above the track.</param>
/// <param name="Distance">"5 furlongs", "2 miles". For the player, not the code.</param>
/// <param name="Blurb">One line on what kind of race it is.</param>
/// <param name="Furlongs">
/// How long the race is, in furlongs -- eight to a mile.
///
/// **Every course is run straight, left to right**, and this is what makes them look
/// different from one another: the client draws a marker per furlong, so a two-mile
/// race is visibly more than three times the five-furlong dash rather than the same
/// picture with a different label.
///
/// An oval was tried first and looked wrong. Flattened to fit a panel far wider than
/// it is tall, a circuit reads as a stadium, and the runners bunch on the bends where
/// they are hardest to tell apart.
/// </param>
/// <param name="SpeedWeight">How much a point of speed is worth here.</param>
/// <param name="StaminaWeight">How much a point of stamina is worth here.</param>
/// <param name="Threshold">Taken off every score before it becomes a weight.</param>
/// <param name="MaxSlip">
/// The most a whole slip may cost at this track, in chips.
///
/// **Per track, because it is arithmetic rather than a house rule.** The engine counts
/// chips in an <c>int</c>, and a slip can return its stake times the longest price on
/// the board. The dash has the widest spread and therefore the longest price, so it
/// has the lowest ceiling -- not because the house is more careful there, but because
/// 1,500,000 at its longest price is already 1.89 billion and 2,000,000 would not fit.
///
/// <c>TrackTests</c> computes each track's real longest price and asserts this against
/// it, so editing a rating or a threshold cannot silently make a payout overflow.
/// </param>
/// <param name="RunSeconds">
/// How long the race takes on screen. Longer for longer races, but nothing like to
/// scale -- a two-mile marathon is roughly four times a five-furlong dash in real life
/// and twice it here, because the dash has to be long enough to watch and the marathon
/// short enough to sit through more than once.
/// </param>
public sealed record Track(
    string Id,
    string Name,
    string Distance,
    string Blurb,
    int Furlongs,
    int SpeedWeight,
    int StaminaWeight,
    int Threshold,
    int MaxSlip,
    float RunSeconds)
{
    /// <summary>
    /// The least weight a runner may carry, however badly the threshold treats it.
    ///
    /// One, not zero: a runner on zero can never win, and a spot on the board that
    /// cannot win is a spot taking money against nothing. See the class remarks.
    /// </summary>
    public const int MinWeight = 1;

    /// <summary>This horse's share of this race.</summary>
    public int WeightOf(Horse horse)
    {
        ArgumentNullException.ThrowIfNull(horse);

        var raw = (SpeedWeight * horse.Speed) + (StaminaWeight * horse.Stamina) - Threshold;

        return raw < MinWeight ? MinWeight : raw;
    }

    /// <summary>The sum of every runner's weight here. The denominator of a win chance.</summary>
    public int TotalWeight
    {
        get
        {
            var total = 0;

            foreach (var runner in Field.Runners)
            {
                total += WeightOf(runner);
            }

            return total;
        }
    }
}

/// <summary>
/// The three courses, in the order the panel shows them.
///
/// Short to long, which is also weakest-stamina-favoured to strongest. A player who
/// reads them top to bottom sees the form invert as they go down, which is the point.
/// </summary>
public static class Tracks
{
    /// <summary>
    /// Five furlongs, straight, pure speed.
    ///
    /// The widest spread of the three and therefore the longest prices, which is why it
    /// carries the lowest slip ceiling. Stamina is worth a third of a point of speed
    /// here rather than nothing at all -- a horse still has to finish.
    /// </summary>
    public static readonly Track Dash = new(
        "dash",
        "THE DASH",
        "5 furlongs",
        "Pure speed. Over five furlongs a stayer has no time to get going.",
        Furlongs: 5,
        SpeedWeight: 3,
        StaminaWeight: 1,
        Threshold: 24,
        MaxSlip: 1_500_000,
        RunSeconds: 5.5f);

    /// <summary>
    /// A mile, once round, and the only one where speed and stamina are worth close to
    /// the same. The class horse's race.
    /// </summary>
    public static readonly Track Mile = new(
        "mile",
        "THE MILE",
        "8 furlongs",
        "Speed and stamina matter about equally. The all-rounder's race.",
        Furlongs: 8,
        SpeedWeight: 5,
        StaminaWeight: 4,
        Threshold: 58,
        MaxSlip: 2_000_000,
        RunSeconds: 7.5f);

    /// <summary>
    /// Two miles, twice round, stamina three times a point of speed. The card inverts
    /// almost completely from the dash.
    /// </summary>
    public static readonly Track Marathon = new(
        "marathon",
        "THE MARATHON",
        "2 miles",
        "Stamina decides it. The sprinters are walking by the end.",
        Furlongs: 16,
        SpeedWeight: 1,
        StaminaWeight: 3,
        Threshold: 22,
        MaxSlip: 2_000_000,
        RunSeconds: 10f);

    /// <summary>Every course, shortest first.</summary>
    public static readonly IReadOnlyList<Track> All = [Dash, Mile, Marathon];

    /// <summary>
    /// The course with this id, or null if there is no such course.
    ///
    /// Null rather than a default, deliberately. A request naming a track that does not
    /// exist must be refused: quietly running it at the dash would take the player's
    /// money for a race they did not choose, and the prices they were shown would have
    /// come from a different board.
    /// </summary>
    public static Track? ById(string? id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return null;
        }

        foreach (var track in All)
        {
            if (string.Equals(track.Id, id, StringComparison.OrdinalIgnoreCase))
            {
                return track;
            }
        }

        return null;
    }
}
