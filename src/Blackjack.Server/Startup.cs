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
        var rules = new Rules();

        // Silent unless asked. Casino.Server.Startup prints the one line the casino
        // needs at boot; everything below is a mod author's view of one table, and
        // three of them was about twenty-five lines of somebody's console for a card
        // game they had not opened yet. The same switch decides whether every request
        // gets logged while they play.
        if (!log.Verbose)
        {
            return Task.CompletedTask;
        }

        log.Banner($"mod folder: {log.ModFolder}");
        log.Banner($"stats file: {stats.FilePath} ({(stats.Writable ? "writable" : "NOT WRITABLE")})");
        log.Banner("routes: POST /blackjack/ping, /deal, /action, /state, /stats");
        log.Banner($"item events: {BlackjackActions.Deal}, {BlackjackActions.Play}");
        log.Banner(
            $"table: {rules.DeckCount} decks, dealer {(rules.DealerHitsSoft17 ? "hits" : "stands on")} soft 17, "
            + "naturals pay 3:2 in currency and even money in valuables");

        // Stack limits are deliberately NOT reported here. PostLoad + 1 is not last:
        // BarterItemsStacks rewrites every one of them about half a second after this
        // line runs, so anything printed now is the base database value and wrong on
        // any server with an item mod. They are reported on first contact instead,
        // which is the earliest moment the answer is trustworthy.

        if (!stats.Writable)
        {
            log.Error("stats cannot be written, so the record will reset every restart. Check folder permissions.");
        }

        return Task.CompletedTask;
    }
}
