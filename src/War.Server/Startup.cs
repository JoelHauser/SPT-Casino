using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using War.Game;

namespace War.Server;

/// <summary>
/// Announces the table on the server console, and only when asked. Silent unless its
/// verbose switch is on, like every other table: `Casino.Server.Startup` prints the
/// one line the casino needs at boot.
/// </summary>
[Injectable(TypePriority = OnLoadOrder.PostLoad + 1)]
public class Startup(WarLog log) : IOnLoad
{
    public Task OnLoadAsync(CancellationToken cancellationToken)
    {
        if (!log.Verbose)
        {
            return Task.CompletedTask;
        }

        log.Banner($"v{TableInfo.Version} loaded -- built for SPT {TableInfo.SptVersion}");
        log.Banner($"mod folder: {log.ModFolder}");
        log.Banner("routes: POST /war/ping, /state, /deal, /decide, /stats");
        log.Banner($"item event: {WarActions.Sync}, so the stash keeps up without a reload");
        log.Banner(
            $"{Rules.Decks} decks, tie bet {Rules.TiePays} to 1 -- {Odds.HouseEdgePercent():F2}% "
            + "to the house going to war, computed rather than measured");

        log.Notice("THIS TABLE PLAYS FOR REAL MONEY. The bet leaves your stash when the");
        log.Notice("cards are dealt, and whatever they win arrives when they turn over.");

        log.Banner("verbose logging is ON -- every round will be logged.");
        log.Banner("turn it off in war.config.json once things are working.");

        return Task.CompletedTask;
    }
}
