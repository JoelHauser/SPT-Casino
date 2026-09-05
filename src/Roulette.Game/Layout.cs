namespace Roulette.Game;

/// <summary>
/// The betting cloth: every spot a chip can go on, enumerated once.
///
/// The cloth is three columns of twelve running 1..36, with the row 1,2,3 nearest
/// the zero. Streets, corners and six lines can each be named by the lowest number
/// they cover, because on that grid the lowest number fixes the shape.
///
/// **A split cannot.** A chip on the line beside 1 covers 1 and 2; a chip on the
/// line below it covers 1 and 4. Both are "the split on 1", and paying one when the
/// player asked for the other is a silent money bug that only shows up on the
/// numbers that differ. So splits are enumerated here and a bet names one by index
/// -- which is also what the client wants, since it draws these spots and can send
/// back the one that was clicked.
/// </summary>
public static class Layout
{
    /// <summary>
    /// Every legal split, in a fixed order: the zero splits first, then the pairs
    /// across each row, then the pairs down each column.
    ///
    /// The order is part of the contract. A bet carries an index into this list, so
    /// reordering it silently repoints every split bet ever placed --
    /// <c>TheSplitOrderIsFixed</c> pins the ends and the count against that.
    /// </summary>
    public static IReadOnlyList<(int Low, int High)> Splits { get; } = BuildSplits();

    /// <summary>The lowest number of each row of three: 1, 4, 7 ... 34.</summary>
    public static IReadOnlyList<int> Streets { get; } =
        [.. Enumerable.Range(0, 12).Select(r => (r * 3) + 1)];

    /// <summary>
    /// The top-left number of each block of four. Not the bottom row of the cloth
    /// and not the far column, because a corner needs a square under and beside it.
    /// </summary>
    public static IReadOnlyList<int> Corners { get; } =
        [.. Enumerable.Range(1, 32).Where(n => n % 3 != 0)];

    /// <summary>The lowest number of each pair of rows: 1, 4 ... 31.</summary>
    public static IReadOnlyList<int> SixLines { get; } =
        [.. Enumerable.Range(0, 11).Select(r => (r * 3) + 1)];

    private static List<(int, int)> BuildSplits()
    {
        var splits = new List<(int, int)>();

        // The zero shares a line with the first row. On an American wheel the double
        // zero has its own neighbours too, but those spots are part of that cloth's
        // top box and are not offered here -- the top line covers the same ground.
        for (var n = 1; n <= 3; n++)
        {
            splits.Add((0, n));
        }

        // Across a row: 1-2, 2-3, 4-5, 5-6 ... Numbers at the end of a row have no
        // neighbour to the right.
        for (var n = 1; n <= 35; n++)
        {
            if (n % 3 != 0)
            {
                splits.Add((n, n + 1));
            }
        }

        // Down a column: 1-4, 2-5 ... 33-36.
        for (var n = 1; n <= 33; n++)
        {
            splits.Add((n, n + 3));
        }

        return splits;
    }
}
