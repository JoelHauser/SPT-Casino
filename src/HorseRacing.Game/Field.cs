namespace HorseRacing.Game;

/// <summary>
/// One runner on the card.
/// </summary>
/// <param name="Number">The saddlecloth number, 1-based. What a bet names.</param>
/// <param name="Name">As printed on the board.</param>
/// <param name="Speed">
/// How fast it is over a short distance, 1 to 20. Nearly all that matters in a
/// five-furlong dash.
/// </param>
/// <param name="Stamina">
/// How well it keeps going, 1 to 20. What decides a two-mile marathon.
/// </param>
public sealed record Horse(int Number, string Name, int Speed, int Stamina);

/// <summary>
/// The stable: eight runners, each with a speed and a stamina rating.
///
/// ## Why a horse has two ratings rather than one weight
///
/// **The same eight horses run at every track, and their chances differ at each one.**
/// A runner's weight -- its share of the race -- is not stored here at all. It is
/// computed by the <see cref="Track"/> from these two ratings, and a track that
/// weights speed heavily produces a completely different card from one that weights
/// stamina.
///
/// That is the whole reason there is more than one track. Three cards of unrelated
/// horses would be three separate games sharing a panel; this way the form is worth
/// learning, because knowing LABS LIGHTNING is fast and has nothing left after a
/// furlong tells you something at all three. It wins about one race in six at the
/// dash and one in forty at the marathon.
///
/// ## Why ratings rather than probabilities, still
///
/// A field of probabilities has to sum to one, so every edit to it is an edit to every
/// other runner -- and worse, the sum is a thing that can be wrong. A card adding to
/// 0.99 is a silently rigged race and nothing about reading the numbers would show it.
/// Ratings cannot be inconsistent with each other. A track turns them into weights and
/// <see cref="Odds"/> normalises once, at the point of use.
///
/// ## Why the finishing order is drawn the way it is
///
/// A race is not eight independent coin flips: exactly one runner wins, and the one
/// that does cannot also come second. The order is drawn by weighted sampling
/// **without replacement** -- pick a winner in proportion to weight, remove it, pick
/// the second from what is left. That is the Plackett-Luce model, and it was chosen
/// over anything more elaborate because every probability a punter can bet on falls
/// out of it **exactly**, by enumeration, in microseconds -- at every track, from that
/// track's own weights.
///
/// A simulated race -- speeds, stamina, a bit of noise per furlong -- would look
/// better in a devlog and would leave nobody able to state the house edge. This repo
/// has said twice already that a game whose return is only known approximately is a
/// game whose edge nobody actually knows.
/// </summary>
public static class Field
{
    /// <summary>
    /// Eight runners.
    ///
    /// Eight rather than six or twelve, and that is the one number here worth
    /// defending. Exacta is a bet on an ordered pair, so the field size squares: six
    /// runners offer thirty exactas and twelve offer a hundred and thirty-two, which is
    /// more spots than a betting slip can show without becoming roulette's cloth. Eight
    /// gives fifty-six -- enough that the bet is worth having, few enough that every one
    /// of them fits on screen.
    /// </summary>
    public const int Count = 8;

    /// <summary>
    /// The stable, in saddlecloth order.
    ///
    /// The ratings are the only numbers in this game chosen by taste. Every price at
    /// every track is computed from them and the track's own formula.
    ///
    /// They were tuned against three constraints that <c>TrackTests</c> asserts: no two
    /// runners may tie at any track (two identical prices on one board is a choice
    /// nobody can make), no runner may fall to the weight floor, and the three tracks
    /// must not all have the same favourite.
    ///
    /// The shapes worth knowing:
    ///
    /// - GRAY GHOST and LABS LIGHTNING are sprinters. LIGHTNING is the extreme case --
    ///   16% at the dash, 2.6% at the marathon.
    /// - NIGHT RAIDER and SCAV LUCK are stayers. SCAV LUCK is LIGHTNING's mirror: 3% at
    ///   the dash, 14.5% at the marathon.
    /// - DOLLAR SIGN is the class horse. Favourite at the mile and never worse than
    ///   third choice anywhere.
    /// - LEFT BEHIND is the rag, and a card needs one: the longest price on every board
    ///   involves it.
    /// </summary>
    public static readonly IReadOnlyList<Horse> Runners =
    [
        new Horse(1, "GRAY GHOST",      20,  9),
        new Horse(2, "DOLLAR SIGN",     17, 13),
        new Horse(3, "FACTORY FLYER",   12, 16),
        new Horse(4, "NIGHT RAIDER",     8, 19),
        new Horse(5, "RESHALA'S PRIDE", 15,  6),
        new Horse(6, "SCAV LUCK",        5, 15),
        new Horse(7, "LABS LIGHTNING",  18,  3),
        new Horse(8, "LEFT BEHIND",      7,  8),
    ];

    /// <summary>
    /// The runner wearing this saddlecloth number.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// There is no such runner. A bet naming one is a bet the table cannot settle, and
    /// settling it as a loser would quietly keep the stake.
    /// </exception>
    public static Horse ByNumber(int number)
    {
        foreach (var runner in Runners)
        {
            if (runner.Number == number)
            {
                return runner;
            }
        }

        throw new ArgumentOutOfRangeException(
            nameof(number),
            number,
            $"no runner {number} on a card of {Count}.");
    }

    /// <summary>Whether this saddlecloth number is on the card.</summary>
    public static bool Has(int number)
    {
        foreach (var runner in Runners)
        {
            if (runner.Number == number)
            {
                return true;
            }
        }

        return false;
    }
}
