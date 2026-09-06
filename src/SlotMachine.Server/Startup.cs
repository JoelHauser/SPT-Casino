using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SlotMachine.Game;

namespace SlotMachine.Server;

/// <summary>
/// Announces the table on the server console, and only when asked.
///
/// Silent unless its verbose switch is on: `Casino.Server.Startup` prints the one line
/// the casino needs at boot, and four tables each printing a block was about
/// twenty-five lines of somebody's console for a game they had not opened.
/// </summary>
[Injectable(TypePriority = OnLoadOrder.PostLoad + 1)]
public class Startup(SlotLog log) : IOnLoad
{
    public Task OnLoadAsync(CancellationToken cancellationToken)
    {
        if (!log.Verbose)
        {
            return Task.CompletedTask;
        }

        log.Banner($"v{TableInfo.Version} loaded -- built for SPT {TableInfo.SptVersion}");
        log.Banner($"mod folder: {log.ModFolder}");
        log.Banner("routes: POST /slots/ping, /pull");
        log.Banner($"item event: {SlotActions.Sync}, so the stash keeps up without a reload");
        log.Banner(
            $"{Reels.Count} reels, {Reels.Rows} rows, {(int)Math.Pow(Reels.Rows, Reels.Count)} ways -- "
            + $"{Odds.ReturnToPlayer():P2} back to the player, computed rather than measured");

        log.Notice("THIS MACHINE PLAYS FOR REAL MONEY. The stake leaves your stash when you");
        log.Notice("pull, and whatever the reels paid arrives when they stop.");

        log.Banner("verbose logging is ON -- every pull will be logged.");
        log.Banner("turn it off in slots.config.json once things are working.");

        return Task.CompletedTask;
    }
}
