using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Models.Spt.Tables;

namespace Poker.Server;

/// <summary>
/// Names the bots out of the game's own PMC nickname list.
///
/// The usec and bear bot types each carry the same 619 nicknames -- the ones a
/// player meets in a raid -- so a seat called Terkoiz or WillDaPope reads as
/// somebody who plays this game rather than as "Seat 2".
///
/// Non-ASCII names are skipped. The scav lists are Cyrillic and the PMC list has a
/// few too, and the panel borrows whatever font the menu happens to have loaded --
/// which is not guaranteed to have the glyphs. A name that renders as boxes is
/// worse than a numbered seat.
/// </summary>
[Injectable]
public class BotNames(BotTable bots) : INameSource
{
    /// <summary>
    /// Read once. The list does not change while the server is up, and filtering six
    /// hundred names on every hand is work for nothing.
    /// </summary>
    private List<string>? _pool;

    public IReadOnlyList<string> Take(int count, Random rng)
    {
        if (count <= 0)
        {
            return [];
        }

        var pool = Pool();
        if (pool.Count == 0)
        {
            return [];
        }

        // Distinct within a table: two seats sharing a name is worse than a numbered
        // one, because it reads as a bug rather than as a coincidence.
        var chosen = new List<string>(count);
        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Bounded rather than looping until it finds enough: a pool smaller than the
        // table would otherwise spin forever.
        for (var attempt = 0; attempt < count * 20 && chosen.Count < count; attempt++)
        {
            var name = pool[rng.Next(pool.Count)];
            if (taken.Add(name))
            {
                chosen.Add(name);
            }
        }

        return chosen;
    }

    private List<string> Pool()
    {
        if (_pool is not null)
        {
            return _pool;
        }

        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Types is nullable on the model, and a mod is in no position to promise the
        // bot database was loaded. Falling through to an empty pool costs the names
        // and nothing else.
        var types = bots.Types;

        foreach (var type in new[] { "usec", "bear" })
        {
            if (types is null || !types.TryGetValue(type, out var bot) || bot?.FirstNames is null)
            {
                continue;
            }

            foreach (var name in bot.FirstNames)
            {
                if (Usable(name))
                {
                    names.Add(name.Trim());
                }
            }
        }

        return _pool = [.. names];
    }

    /// <summary>
    /// Short, present, and drawable in the font the panel borrowed. The length cap
    /// keeps a seat label from pushing the stack off its row.
    /// </summary>
    private static bool Usable(string? name) =>
        !string.IsNullOrWhiteSpace(name)
        && name.Trim().Length <= 16
        && name.Trim().All(char.IsAscii);
}
