namespace Casino.Server;

/// <summary>
/// Finds the data a table wrote back when it was its own mod.
///
/// Blackjack, Poker and Roulette each used to live in `user/mods/&lt;Table&gt;` and keep
/// their state in `data/` under it. They share `user/mods/Casino` now, which means two
/// things: the filenames had to stop colliding, and the old files are suddenly
/// somewhere the new code would never look.
///
/// **This matters most for escrow, which is money.** That file is the record of what
/// the house owes a player whose hand or spin was interrupted, and a player who
/// upgrades while one is outstanding would otherwise simply lose it. It is a rare case
/// -- the record is empty unless the server died mid-hand -- but "rare" is the reason
/// nobody would report it, not a reason to drop it.
///
/// Both places an upgrade can leave the old folder are searched: where it was, and the
/// `_replaced-by-SPT-Casino` folder the install script moves it to.
/// </summary>
public static class LegacyData
{
    /// <summary>Where the install script parks the mod folders it replaces.</summary>
    private const string Retired = "_replaced-by-SPT-Casino";

    /// <summary>
    /// The old file for this table, or null if there is not one.
    /// </summary>
    /// <param name="modFolder">The casino's own mod folder, as SPT reports it.</param>
    /// <param name="table">Blackjack, Poker or Roulette.</param>
    /// <param name="fileName">The name the file had then, such as escrow.json.</param>
    public static string? Find(string modFolder, string table, string fileName)
    {
        if (string.IsNullOrEmpty(modFolder))
        {
            return null;
        }

        var mods = Path.GetDirectoryName(modFolder.TrimEnd(
            Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));

        if (string.IsNullOrEmpty(mods))
        {
            return null;
        }

        // Beside user/mods rather than inside it, because SPT tries to load every
        // directory under mods and one with no assemblies in it throws.
        var user = Path.GetDirectoryName(mods);

        List<string> candidates = [Path.Combine(mods, table, "data", fileName)];

        if (!string.IsNullOrEmpty(user))
        {
            candidates.Add(Path.Combine(user, Retired, table, "data", fileName));
        }

        foreach (var candidate in candidates)
        {
            try
            {
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
            catch
            {
                // A path that cannot even be tested is not one to migrate from, and
                // this runs during construction of something the server needs.
            }
        }

        return null;
    }
}
