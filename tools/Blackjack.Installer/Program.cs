using System.IO.Compression;
using System.Reflection;

namespace Blackjack.Installer;

/// <summary>
/// Puts Blackjack into an SPT install.
///
/// The mod is two halves that go to two different places -- a server mod under
/// SPT_Runtime\user\mods and a BepInEx plugin under BepInEx\plugins -- and the
/// archive is laid out so extracting it into the SPT folder puts both where they
/// belong. This does the same thing, and checks the folder is really an SPT
/// install first, because extracting into the wrong place is the failure that
/// looks exactly like the mod not working.
/// </summary>
internal static class Program
{
    private const string Version = "1.0";

    private static int Main(string[] args)
    {
        Console.Title = $"Blackjack {Version} installer";
        Console.WriteLine();
        Console.WriteLine($"  Blackjack {Version} for SPT 4.1.x");
        Console.WriteLine("  ---------------------------------");
        Console.WriteLine();

        try
        {
            var target = ResolveTarget(args);
            if (target is null)
            {
                return Fail("No SPT folder given.");
            }

            if (!LooksLikeSpt(target))
            {
                Console.WriteLine($"  That folder does not look like an SPT install: {target}");
                Console.WriteLine("  Expected to find SPT_Runtime\\SPT.Server.exe inside it.");
                Console.WriteLine();
                Console.Write("  Install anyway? (y/N) ");

                if (!string.Equals(Console.ReadLine()?.Trim(), "y", StringComparison.OrdinalIgnoreCase))
                {
                    return Fail("Cancelled.");
                }
            }

            var written = Install(target);

            Console.WriteLine();
            Console.WriteLine($"  Installed {written} file(s) into {target}");
            Console.WriteLine();
            Console.WriteLine("  Start the server. \"Blackjack\" should appear in the mod list, and");
            Console.WriteLine("  a BLACKJACK entry on the game's main menu.");

            return Done(0);
        }
        catch (Exception ex)
        {
            Console.WriteLine();
            Console.WriteLine("  " + ex.Message);
            return Done(1);
        }
    }

    /// <summary>
    /// The SPT folder: given on the command line, or the one this is sitting in, or
    /// asked for. Sitting in the folder is the common case -- people download an
    /// installer into the game directory and run it there.
    /// </summary>
    private static string? ResolveTarget(string[] args)
    {
        if (args.Length > 0 && !string.IsNullOrWhiteSpace(args[0]))
        {
            return Path.GetFullPath(args[0].Trim('"'));
        }

        var here = AppContext.BaseDirectory;
        if (LooksLikeSpt(here))
        {
            Console.WriteLine($"  Found an SPT install here: {here}");
            Console.Write("  Install into it? (Y/n) ");

            var answer = Console.ReadLine()?.Trim();
            if (string.IsNullOrEmpty(answer) || answer.Equals("y", StringComparison.OrdinalIgnoreCase))
            {
                return here;
            }
        }

        Console.WriteLine("  Drag your SPT folder onto this window, or type the path.");
        Console.Write("  SPT folder: ");

        var typed = Console.ReadLine()?.Trim().Trim('"');
        return string.IsNullOrWhiteSpace(typed) ? null : Path.GetFullPath(typed);
    }

    private static bool LooksLikeSpt(string folder) =>
        File.Exists(Path.Combine(folder, "SPT_Runtime", "SPT.Server.exe"));

    /// <summary>
    /// Extracts the payload over the target. Overwrites, because installing on top of
    /// an older copy is the normal case and leaving stale files behind is how a mod
    /// half-updates.
    /// </summary>
    private static int Install(string target)
    {
        using var payload = Assembly.GetExecutingAssembly().GetManifestResourceStream("payload.zip")
            ?? throw new InvalidOperationException("This installer has no mod inside it, which should be impossible.");

        using var archive = new ZipArchive(payload, ZipArchiveMode.Read);

        var written = 0;
        foreach (var entry in archive.Entries)
        {
            if (string.IsNullOrEmpty(entry.Name))
            {
                continue;
            }

            var destination = Path.GetFullPath(Path.Combine(target, entry.FullName));

            // An archive entry that climbs out of the target with ..\ would write
            // anywhere on the disk. This one is ours, but an installer that does not
            // check is a bad habit rather than a safe one.
            if (!destination.StartsWith(Path.GetFullPath(target), StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"Refusing to write outside the target: {entry.FullName}");
            }

            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            entry.ExtractToFile(destination, overwrite: true);

            Console.WriteLine($"    {entry.FullName}");
            written++;
        }

        return written;
    }

    private static int Fail(string message)
    {
        Console.WriteLine("  " + message);
        return Done(1);
    }

    private static int Done(int code)
    {
        Console.WriteLine();
        Console.Write("  Press enter to close. ");
        Console.ReadLine();
        return code;
    }
}
