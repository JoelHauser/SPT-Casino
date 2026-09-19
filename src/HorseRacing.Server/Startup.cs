using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using HorseRacing.Game;

namespace HorseRacing.Server;

/// <summary>
/// Announces the table on the server console, and only when asked.
///
/// Silent unless its verbose switch is on: `Casino.Server.Startup` prints the one line
/// the casino needs at boot, and five tables each printing a block was about thirty
/// lines of somebody's console for a game they had not opened.
/// </summary>
[Injectable(TypePriority = OnLoadOrder.PostLoad + 1)]
public class Startup(RaceLog log) : IOnLoad
{
    public Task OnLoadAsync(CancellationToken cancellationToken)
    {
        if (!log.Verbose)
        {
            return Task.CompletedTask;
        }

        log.Banner($"v{TableInfo.Version} loaded -- built for SPT {TableInfo.SptVersion}");
        log.Banner($"mod folder: {log.ModFolder}");
        log.Banner("routes: POST /races/ping, /place, /stats");
        log.Banner($"item event: {RaceActions.Sync}, so the stash keeps up without a reload");
        log.Banner(
            $"{Field.Count} runners over {Tracks.All.Count} courses, {Odds.All(Tracks.Dash).Count} spots "
            + $"on each board -- {Odds.Takeout:P2} to the house at every one of them, "
            + "computed rather than measured");

        foreach (var track in Tracks.All)
        {
            var favourite = Field.Runners.OrderByDescending(track.WeightOf).First();

            log.Banner(
                $"  {track.Name} ({track.Distance}) -- favourite {favourite.Name} at "
                + $"{Odds.BoardPrice(track, BetKind.Win, favourite.Number):F2}, "
                + $"slip max {track.MaxSlip:N0}");
        }

        log.Notice("THIS TABLE PLAYS FOR REAL MONEY. The stakes leave your stash when the");
        log.Notice("race is off, and whatever the slip paid arrives when they pass the post.");

        log.Banner("verbose logging is ON -- every race will be logged.");
        log.Banner("turn it off in horseracing.config.json once things are working.");

        return Task.CompletedTask;
    }
}
