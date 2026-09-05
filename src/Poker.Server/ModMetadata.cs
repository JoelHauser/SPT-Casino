using SPTarkov.Server.Core.Models.Spt.Mod;

namespace Poker.Server;

/// <summary>
/// Replaces the package.json that SPT 3.x server mods used. Every property must be
/// set; the loader reads this to decide whether the mod may load at all.
/// </summary>
public record ModMetadata : IModMetadata
{
    /// <summary>
    /// Declared identically by both halves of the mod -- here and by
    /// <c>[BepInPlugin]</c> on the client plugin, with no ".client" suffix on either.
    /// The Forge rejects an upload where the two disagree.
    /// </summary>
    public string ModGuid { get; init; } = "com.mybutthasarash.poker";

    public string Name { get; init; } = "Poker";

    public string Author { get; init; } = "JoelHauser";

    public List<string>? Contributors { get; init; }

    public SemanticVersioning.Version Version { get; init; } = new("1.0.2");

    /// <summary>
    /// Targets SPT 4.1.3. "~4.1.3" is >=4.1.3 &lt;4.2.0, so it also covers later 4.1
    /// patches.
    ///
    /// **A hard gate.** A mod outside the range loads nothing and logs nothing, so
    /// silence at startup means this line, not a bug in the game code.
    /// </summary>
    public SemanticVersioning.Range SptVersion { get; init; } = new("~4.1.3");

    public List<string>? Incompatibilities { get; init; }

    public Dictionary<string, SemanticVersioning.Range>? ModDependencies { get; init; }

    public string? Url { get; init; } = "https://github.com/JoelHauser/Poker-";

    public string License { get; init; } = "MIT";

    public bool HasPrepatcher { get; init; }
}
