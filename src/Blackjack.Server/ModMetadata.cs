using SPTarkov.Server.Core.Models.Spt.Mod;

namespace Blackjack.Server;

/// <summary>
/// Replaces the package.json that SPT 3.x server mods used. Every property must
/// be set; the loader reads this to decide whether the mod may load at all.
/// </summary>
public record ModMetadata : IModMetadata
{
    public string ModGuid { get; init; } = "com.mybutthasarash.blackjack";
    public string Name { get; init; } = "Blackjack";
    public string Author { get; init; } = "JoelHauser";
    public List<string>? Contributors { get; init; }
    public SemanticVersioning.Version Version { get; init; } = new("1.1.0");

    // Targets SPT 4.1.3. "~4.1.3" is >=4.1.3 <4.2.0, so it also covers later 4.1
    // patches. This is a hard gate -- the server refuses to load a mod whose range
    // excludes the running version, so "~4.0.0" would have been rejected outright.
    public SemanticVersioning.Range SptVersion { get; init; } = new("~4.1.3");

    public List<string>? Incompatibilities { get; init; }
    public Dictionary<string, SemanticVersioning.Range>? ModDependencies { get; init; }
    public string? Url { get; init; } = "https://github.com/JoelHauser/Blackjack";
    public string License { get; init; } = "MIT";
    public bool HasPrepatcher { get; init; } = false;
}
