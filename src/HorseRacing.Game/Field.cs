namespace HorseRacing.Game;

/// <summary>
/// One runner on the card.
/// </summary>
/// <param name="Number">The saddlecloth number, 1-based. What a bet names.</param>
/// <param name="Name">As printed on the board.</param>
/// <param name="Weight">
/// The runner's chance, unnormalised. Only ratios matter: doubling every weight in
/// the field changes nothing. See <see cref="Field"/> for why this is a weight and
/// not a probability.
/// </param>
public sealed record Horse(int Number, string Name, int Weight);

/// <summary>
/// The card: eight runners and what each one's chance is.
///
/// ## Why weights rather than probabilities
///
/// A field of probabilities has to sum to one, and every edit to it is therefore an
/// edit to every other runner. Worse, the sum is a thing that can be wrong -- a card
/// adding to 0.99 is a silently rigged race, and nothing about reading the numbers
/// would show it. Weights cannot be inconsistent with each other. They are normalised
/// once, at the point of use, by <see cref="Odds"/>.
///
/// The same reasoning is why the slot machine's reels are strips of symbols rather
/// than a table of frequencies.
///
/// ## Why the finishing order is drawn this way
///
/// A race is not eight independent coin flips: exactly one runner wins, and the one
/// that does cannot also come second. The order is drawn by weighted sampling
/// **without replacement** -- pick a winner in proportion to weight, remove it, pick
/// the second from what is left, and so on. That is the Plackett-Luce model, and the
/// reason it was chosen over anything more elaborate is that every probability a
/// punter can bet on falls out of it **exactly**, by enumeration, in microseconds.
///
/// A simulated horse race -- speeds, stamina, a bit of noise per furlong -- would look
/// better in a devlog and would leave nobody able to state the house edge. This repo
/// has said twice already that a game whose return is only known approximately is a
/// game whose edge nobody actually knows. See <c>SlotMachine.Game.Odds</c>.
/// </summary>
public static class Field
{
    /// <summary>
    /// Eight runners.
    ///
    /// Eight rather than six or twelve, and that is the one number here worth
    /// defending. Exacta is a bet on an ordered pair, so the field size squares:
    /// six runners offer thirty exactas and twelve offer a hundred and thirty-two,
    /// which is more spots than a betting slip can show without becoming roulette's
    /// cloth. Eight gives fifty-six -- enough that the bet is worth having, few
    /// enough that every one of them fits on screen.
    /// </summary>
    public const int Count = 8;

    /// <summary>
    /// The card, in saddlecloth order.
    ///
    /// The weights run 30 down to 3, which is deliberate and is the whole character of
    /// the game: the favourite wins a little over a quarter of the time and the rag
    /// wins about one race in thirty-seven. A flat field would make every bet the same
    /// bet wearing a different number, and there would be no reason to read the board.
    ///
    /// These are the only numbers in the game that were chosen by taste rather than
    /// derived. Everything else -- every price on the board -- is computed from them.
    /// </summary>
    public static readonly IReadOnlyList<Horse> Runners =
    [
        new Horse(1, "GRAY GHOST",      30),
        new Horse(2, "DOLLAR SIGN",     24),
        new Horse(3, "FACTORY FLYER",   18),
        new Horse(4, "NIGHT RAIDER",    14),
        new Horse(5, "RESHALA'S PRIDE", 10),
        new Horse(6, "SCAV LUCK",        7),
        new Horse(7, "LABS LIGHTNING",   5),
        new Horse(8, "LEFT BEHIND",      3),
    ];

    /// <summary>The sum of every runner's weight. The denominator of a win chance.</summary>
    public static int TotalWeight
    {
        get
        {
            var total = 0;

            foreach (var runner in Runners)
            {
                total += runner.Weight;
            }

            return total;
        }
    }

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
