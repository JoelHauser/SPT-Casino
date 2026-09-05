namespace Roulette.Game;

/// <summary>What colour a pocket is painted.</summary>
public enum PocketColour
{
    Green,
    Red,
    Black,
}

/// <summary>
/// One pocket on the wheel.
///
/// <see cref="Number"/> is the number painted on it. The double zero on an American
/// wheel is <see cref="DoubleZero"/> rather than 0, because it is a *different*
/// pocket that loses to the same bets -- treating both as 0 makes a straight-up bet
/// on zero pay out twice as often as it should, which is the whole of the American
/// house edge arriving in the wrong place.
/// </summary>
public readonly record struct Pocket(int Number, PocketColour Colour)
{
    /// <summary>
    /// The number standing for the double zero. Deliberately not -1: it is an index
    /// into bet selections in places, and a negative number there reads as an error
    /// rather than as a pocket.
    /// </summary>
    public const int DoubleZero = 37;

    public bool IsZero => Number == 0 || Number == DoubleZero;

    /// <summary>What is painted on the pocket, which is not always the number.</summary>
    public string Label => Number == DoubleZero ? "00" : Number.ToString();

    public override string ToString() => $"{Label} {Colour.ToString().ToLowerInvariant()}";
}

/// <summary>Which wheel is in the room.</summary>
public enum WheelKind
{
    /// <summary>Single zero, 37 pockets. A 2.70% house edge on every bet.</summary>
    European,

    /// <summary>Double zero, 38 pockets. 5.26%, and worse on the top line.</summary>
    American,
}

/// <summary>
/// The wheel: which pockets exist and, just as importantly, what order they are
/// physically in.
///
/// **The order is not decoration.** It is what the client spins to, so a wheel drawn
/// in ascending order would land the ball in the wrong place on screen while the
/// result stayed correct -- a bug that looks like an animation fault and is a data
/// one. The published orders are reproduced here and pinned by tests.
///
/// Both wheels are data rather than code. Nothing branches on <see cref="Kind"/>
/// except the odds on the top line, which exists only on one of them, so switching a
/// table from European to American is a rules change and not a rewrite.
/// </summary>
public sealed class Wheel
{
    /// <summary>
    /// Clockwise from the single zero, as the pockets actually sit on a European
    /// wheel. Reds and blacks alternate all the way round and low and high numbers
    /// are spread deliberately; this order is why.
    /// </summary>
    private static readonly int[] EuropeanOrder =
    [
        0, 32, 15, 19, 4, 21, 2, 25, 17, 34, 6, 27, 13, 36, 11, 30, 8, 23,
        10, 5, 24, 16, 33, 1, 20, 14, 31, 9, 22, 18, 29, 7, 28, 12, 35, 3, 26,
    ];

    /// <summary>
    /// The American wheel, clockwise from the single zero. The two zeroes sit
    /// opposite each other and the numbers run in consecutive pairs across the
    /// wheel, which is a different arrangement entirely rather than the European one
    /// with a pocket added.
    /// </summary>
    private static readonly int[] AmericanOrder =
    [
        0, 28, 9, 26, 30, 11, 7, 20, 32, 17, 5, 22, 34, 15, 3, 24, 36, 13, 1,
        Pocket.DoubleZero,
        27, 10, 25, 29, 12, 8, 19, 31, 18, 6, 21, 33, 16, 4, 23, 35, 14, 2,
    ];

    /// <summary>
    /// The reds. Every other number is black, and zero is green, so one list settles
    /// the colour of the whole wheel -- and the same list serves both wheels, since
    /// the numbers are painted the same however they are arranged.
    /// </summary>
    private static readonly HashSet<int> Reds =
    [
        1, 3, 5, 7, 9, 12, 14, 16, 18, 19, 21, 23, 25, 27, 30, 32, 34, 36,
    ];

    private readonly Dictionary<int, int> _positions;

    public Wheel(WheelKind kind = WheelKind.European)
    {
        Kind = kind;

        var order = kind == WheelKind.American ? AmericanOrder : EuropeanOrder;

        Pockets = [.. order.Select(n => new Pocket(n, ColourOf(n)))];
        _positions = order.Select((n, i) => (n, i)).ToDictionary(x => x.n, x => x.i);
    }

    public WheelKind Kind { get; }

    /// <summary>The pockets in physical order, starting at the single zero.</summary>
    public IReadOnlyList<Pocket> Pockets { get; }

    /// <summary>37 on a European wheel, 38 on an American one.</summary>
    public int PocketCount => Pockets.Count;

    /// <summary>
    /// Where a number sits on the wheel, counting clockwise from the single zero.
    /// The client turns this into an angle; nothing in the rules depends on it.
    /// </summary>
    public int PositionOf(int number) =>
        _positions.TryGetValue(number, out var position)
            ? position
            : throw new ArgumentOutOfRangeException(
                nameof(number), number, $"No pocket {number} on a {Kind} wheel.");

    public Pocket PocketFor(int number) => Pockets[PositionOf(number)];

    /// <summary>
    /// Drops the ball. The only randomness in the game, and it is injected so a spin
    /// can be pinned in a test exactly as the deck is in the other two mods.
    /// </summary>
    public Pocket Spin(Random rng) => Pockets[rng.Next(Pockets.Count)];

    public static PocketColour ColourOf(int number) =>
        number == 0 || number == Pocket.DoubleZero
            ? PocketColour.Green
            : Reds.Contains(number)
                ? PocketColour.Red
                : PocketColour.Black;
}
