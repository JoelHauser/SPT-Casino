using SPTarkov.Server.Core.Models.Logging;

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
///
/// Six colours rather than the mainline build's eight: Spectre.Console arrived with SPT
/// 4.1, and 4.0's logger takes a `LogTextColor`, which is the eight ANSI foreground
/// codes and a gray. Black, white and gray are no use in a rainbow on a console of
/// unknown background, so the cycle is what is left.
/// </summary>
public static class Palette
{
    private static readonly LogTextColor[] Rainbow =
    [
        LogTextColor.Red,
        LogTextColor.Yellow,
        LogTextColor.Green,
        LogTextColor.Cyan,
        LogTextColor.Blue,
        LogTextColor.Magenta,
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
    public static LogTextColor Next() =>
        Rainbow[(Interlocked.Increment(ref _next) & 0x7FFFFFFF) % Rainbow.Length];
}
