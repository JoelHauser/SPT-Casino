using System.Text;
using Spectre.Console;

namespace Casino.Server;

/// <summary>
/// The three lines the casino prints when the server starts, a letter at a time.
///
/// ## Why this does not go through the logger
///
/// `ISptLogger.LogWithColor` takes one colour for a whole line, which is as far as it
/// goes -- and embedding markup in the message does not work either, because
/// `ConsoleLogHandler.GetColorizedText` runs `Markup.Escape` over it first and the
/// tags would print as text. A letter at a time means writing to the console directly.
///
/// ## What that costs, and why it is affordable
///
/// These lines no longer reach `spt*.log`. That would matter -- the version is the
/// first thing worth knowing when somebody reports a problem -- except SPT already
/// writes it there itself:
///
///     Mod: SPT Casino version: 1.0.1 (GUID: com.mybutthasarash.sptcasino | ...) loaded
///
/// So the file keeps the fact and the console gets the flourish. Debug level was the
/// other candidate for keeping a plain copy in the file, and it is not one: the log
/// holds Information, Warning and Critical and no Debug lines at all, so a line logged
/// there would appear nowhere.
/// </summary>
public static class Banner
{
    /// <summary>
    /// Markup names rather than <see cref="Color"/> values, because these are written
    /// straight into a markup string. Spectre knows all eight by name.
    /// </summary>
    private static readonly string[] Cycle =
    [
        "red", "orange1", "yellow", "green", "aqua", "dodgerblue1", "purple", "magenta1",
    ];

    /// <summary>
    /// Writes one line, cycling a colour per visible character.
    ///
    /// Spaces are passed through uncoloured: colouring them shifts every letter after
    /// them along the cycle for no visible gain, and it makes the rainbow drift out of
    /// step between one line and the next.
    ///
    /// Every character is escaped individually. The lines start with "[Blackjack]" and
    /// a bare bracket in markup is the opening of a tag, so without escaping the first
    /// thing printed would be a parse error.
    /// </summary>
    public static void Rainbow(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        var markup = new StringBuilder(text.Length * 20);
        var step = 0;

        foreach (var character in text)
        {
            if (char.IsWhiteSpace(character))
            {
                markup.Append(character);
                continue;
            }

            markup.Append('[')
                  .Append(Cycle[step++ % Cycle.Length])
                  .Append(']')
                  .Append(Markup.Escape(character.ToString()))
                  .Append("[/]");
        }

        try
        {
            AnsiConsole.MarkupLine(markup.ToString());
        }
        catch
        {
            // A console that will not take markup is not a reason to fail a mod load,
            // and there is no logger to fall back to that would render this any better.
            System.Console.WriteLine(text);
        }
    }
}
