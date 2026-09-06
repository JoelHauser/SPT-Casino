using Spectre.Console;

namespace Casino.Server;

/// <summary>
/// The colours the startup block is printed in.
///
/// The three tables each print their own block, one after another, and they are one
/// mod -- so the cycle is shared rather than one per table. Whichever order SPT happens
/// to construct them in, the lines come out as a single run of colour down the console
/// instead of three that each start over.
///
/// Console only. Colour never reaches `spt*.log`, which is worth knowing before
/// wondering why it cannot be seen in the file afterwards.
/// </summary>
public static class Palette
{
    private static readonly Color[] Rainbow =
    [
        Color.Red,
        Color.Orange1,
        Color.Yellow,
        Color.Green,
        Color.Aqua,
        Color.DodgerBlue1,
        Color.Purple,
        Color.Magenta1,
    ];

    private static int _next = -1;

    /// <summary>
    /// The next colour along.
    ///
    /// Interlocked because the tables are constructed by the DI container and there is
    /// no promise about which thread does it. Masked rather than modulo'd so it stays
    /// correct when the counter eventually wraps past int.MaxValue -- which it will not
    /// in a server's lifetime, but a counter that is wrong only after a very long time
    /// is the worst kind to leave.
    /// </summary>
    public static Color Next() =>
        Rainbow[(Interlocked.Increment(ref _next) & 0x7FFFFFFF) % Rainbow.Length];
}
