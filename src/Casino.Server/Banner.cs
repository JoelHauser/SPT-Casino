using System.Text;

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
///     Mod: SPT Casino version: 1.2.0 (GUID: com.mybutthasarash.sptcasino | ...) loaded
///
/// So the file keeps the fact and the console gets the flourish. Debug level was the
/// other candidate for keeping a plain copy in the file, and it is not one: the log
/// holds Information, Warning and Critical and no Debug lines at all, so a line logged
/// there would appear nowhere.
///
/// ## Why this writes ANSI rather than Spectre markup
///
/// Spectre.Console arrived with SPT 4.1 -- it is a dependency of `SPTarkov.Common`
/// there, and 4.0 installs ship no Spectre assembly at all, so the mainline build's
/// `AnsiConsole.MarkupLine` would throw `FileNotFoundException` on the first character
/// written. The codes below are what Spectre would have emitted anyway, and they are
/// the same ones 4.0's own `LogTextColor` is defined in terms of, so a console that
/// renders SPT's coloured log lines renders these.
/// </summary>
public static class Banner
{
    /// <summary>
    /// ANSI foreground codes rather than colour names, because there is no markup
    /// parser here to look a name up in. Six rather than the mainline build's eight,
    /// for the reason given on <see cref="Palette"/>.
    /// </summary>
    private static readonly int[] Cycle = [31, 33, 32, 36, 34, 35];

    /// <summary>
    /// ESC built from its code point rather than typed. A literal ESC byte in a source
    /// file is invisible in every diff and editor, does not survive a paste, and an
    /// editor that strips control characters would silently turn the colour off.
    /// </summary>
    private const char Esc = (char)0x1b;

    private static readonly string Reset = $"{Esc}[0m";

    /// <summary>
    /// Writes one line, cycling a colour per visible character.
    ///
    /// Spaces are passed through uncoloured: colouring them shifts every letter after
    /// them along the cycle for no visible gain, and it makes the rainbow drift out of
    /// step between one line and the next.
    /// </summary>
    public static void Rainbow(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        var line = new StringBuilder(text.Length * 12);
        var step = 0;

        foreach (var character in text)
        {
            if (char.IsWhiteSpace(character))
            {
                line.Append(character);
                continue;
            }

            line.Append(Esc)
                .Append('[')
                .Append(Cycle[step++ % Cycle.Length])
                .Append('m')
                .Append(character)
                .Append(Reset);
        }

        System.Console.WriteLine(line.ToString());
    }
}
