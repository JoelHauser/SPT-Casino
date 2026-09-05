using Blackjack.Game;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;

namespace Blackjack.Server;

/// <summary>
/// Announces the mod on the server console at boot.
///
/// The most important thing here is not any single line -- it is that the block
/// appears at all. A mod rejected by the SptVersion gate loads nothing and logs
/// nothing, so silence at startup means the gate, not a bug in the game code.
/// </summary>
[Injectable(TypePriority = OnLoadOrder.PostLoad + 1)]
public class Startup(BlackjackLog log, StatsStore stats) : IOnLoad
{
    public Task OnLoadAsync(CancellationToken cancellationToken)
    {
        var metadata = new ModMetadata();
        var rules = new Rules();

        log.Success($"v{metadata.Version} loaded -- built for SPT {metadata.SptVersion}");
        log.Info($"mod folder: {log.ModFolder}");
        log.Info($"stats file: {stats.FilePath} ({(stats.Writable ? "writable" : "NOT WRITABLE")})");
        log.Info("routes: POST /blackjack/ping, /deal, /action, /state, /stats");
        log.Info($"item events: {BlackjackActions.Deal}, {BlackjackActions.Play}");
        log.Info(
            $"table: {rules.DeckCount} decks, dealer {(rules.DealerHitsSoft17 ? "hits" : "stands on")} soft 17, "
            + "naturals pay 3:2 in currency and even money in valuables");

        // Stack limits are deliberately NOT reported here. PostLoad + 1 is not last:
        // BarterItemsStacks rewrites every one of them about half a second after this
        // line runs, so anything printed now is the base database value and wrong on
        // any server with an item mod. They are reported on first contact instead,
        // which is the earliest moment the answer is trustworthy.

        if (log.Verbose)
        {
            log.Info("verbose logging is ON -- every request and every rouble will be logged.");
            log.Info("turn it off in config.json once things are working.");
        }

        if (!stats.Writable)
        {
            log.Error("stats cannot be written, so the record will reset every restart. Check folder permissions.");
        }

        return Task.CompletedTask;
    }
}
