namespace HorseRacing.Server;

/// <summary>
/// What this table calls itself, and which version of it this is.
///
/// Not an `IModMetadata`: the casino ships as one mod folder and SPT allows exactly
/// one metadata class in it. See <see cref="Casino.Server.ModMetadata"/>.
/// </summary>
internal static class TableInfo
{
    internal const string Name = "Horse Racing";

    /// <summary>
    /// The table's own version, which is not the casino's. It shipped at 1.0.0 in
    /// casino 1.3.0 -- see the root CLAUDE.md, "One folder, seven assemblies".
    /// </summary>
    internal const string Version = "1.0.0";

    /// <summary>The SPT range the casino targets. See Casino.Server.ModMetadata.</summary>
    internal const string SptVersion = "~4.1.3";
}
