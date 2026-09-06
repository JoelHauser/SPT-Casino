namespace Roulette.Server;

/// <summary>
/// What this table calls itself, and which version of it this is.
///
/// It used to be an `IModMetadata`. It is not one any more: the casino ships as a
/// single mod folder, and SPT allows exactly one metadata class per folder --
/// `ModLoader.LoadModMetadata` does `SingleOrDefault` and throws "Duplicate mod
/// metadata found" on the second. The one that survives is
/// <see cref="Casino.Server.ModMetadata"/>.
///
/// The version stays here rather than moving with it, because it describes the table
/// and not the download. Blackjack has been through 1.1.4 while the casino it now
/// lives in is on 1.0.0, and flattening those into one number would throw away which
/// build of the table a player is actually running.
/// </summary>
internal static class TableInfo
{
    internal const string Name = "Roulette";

    internal const string Version = "0.1.0";

    /// <summary>The SPT range the casino targets. See Casino.Server.ModMetadata.</summary>
    internal const string SptVersion = "~4.1.3";
}
