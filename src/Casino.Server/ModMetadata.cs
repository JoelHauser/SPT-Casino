using SPTarkov.Server.Core.Models.Spt.Mod;

namespace Casino.Server;

/// <summary>
/// The one piece of metadata for the whole casino, and the reason all four server
/// assemblies can live in a single folder.
///
/// ## Why there is exactly one
///
/// SPT loads a mod folder by taking **every** .dll in it -- `ModLoader.LoadMod` calls
/// `DirectoryInfo.GetFiles()`, filters on the extension and loads each one into a
/// single `SptMod.Assemblies` -- and `RegisterSptServicesAsync` then walks that whole
/// list, so every `[Injectable]` in every assembly is registered. Four assemblies from
/// one folder is not a trick; it is what the loader already does.
///
/// What it will not tolerate is two of these. `ModLoader.LoadModMetadata` runs
/// `SingleOrDefault` over the types implementing `IModMetadata` and throws
/// "Duplicate mod metadata found for mod at path" the moment it sees a second. That is
/// the whole constraint: **one folder, one metadata, as many assemblies as you like.**
///
/// So Blackjack, Poker and Roulette no longer carry one each. They keep their own
/// version numbers, in `TableInfo`, because those describe the table rather than the
/// download.
/// </summary>
public record ModMetadata : IModMetadata
{
    /// <summary>
    /// The same GUID the client plugin declares through <c>[BepInPlugin]</c>. Both
    /// halves now agree, which they did not while the server was three mods.
    /// </summary>
    public string ModGuid { get; init; } = "com.mybutthasarash.sptcasino";

    public string Name { get; init; } = "SPT Casino";

    public string Author { get; init; } = "JoelHauser";

    public List<string>? Contributors { get; init; }

    public SemanticVersioning.Version Version { get; init; } = new("1.0.0");

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

    public string? Url { get; init; } = "https://github.com/JoelHauser/SPT-Casino";

    public string License { get; init; } = "MIT";

    public bool HasPrepatcher { get; init; }
}
