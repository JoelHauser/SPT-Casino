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
public record ModMetadata : AbstractModMetadata
{
    /// <summary>
    /// The same GUID the client plugin declares through <c>[BepInPlugin]</c>. Both
    /// halves now agree, which they did not while the server was three mods.
    /// </summary>
    public override string ModGuid { get; init; } = "com.mybutthasarash.sptcasino";

    public override string Name { get; init; } = "SPT Casino";

    public override string Author { get; init; } = "JoelHauser";

    public override List<string>? Contributors { get; init; }

    public override SemanticVersioning.Version Version { get; init; } = new("1.2.61");

    /// <summary>
    /// 4.0.x backport: targets SPT 4.0.13. "~4.0.13" is >=4.0.13 &lt;4.1.0, so it also
    /// covers later 4.0 patches. The 4.1.x mainline build uses "~4.1.3" instead --
    /// see the 4.0.x-backport branch note in CLAUDE.md.
    ///
    /// **A hard gate.** A mod outside the range loads nothing and logs nothing, so
    /// silence at startup means this line, not a bug in the game code.
    /// </summary>
    public override SemanticVersioning.Range SptVersion { get; init; } = new("~4.0.13");

    public override List<string>? Incompatibilities { get; init; }

    public override Dictionary<string, SemanticVersioning.Range>? ModDependencies { get; init; }

    public override string? Url { get; init; } = "https://github.com/JoelHauser/SPT-Casino";

    public override string License { get; init; } = "MIT";

    /// <summary>
    /// 4.0.x has no <c>HasPrepatcher</c>; the nearest thing it asks about a mod is
    /// whether it ships bundles. The casino ships neither, so this is false either way.
    /// </summary>
    public override bool? IsBundleMod { get; init; } = false;
}
