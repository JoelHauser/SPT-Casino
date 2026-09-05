using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;

namespace Poker.Server;

/// <summary>
/// Announces the mod on the server console at boot.
///
/// The most important thing here is not any single line -- it is that the block
/// appears at all. A mod rejected by the SptVersion gate loads nothing and logs
/// nothing, so silence at startup means the gate rather than a bug in the game code.
/// </summary>
[Injectable(TypePriority = OnLoadOrder.PostLoad + 1)]
public class Startup(PokerLog log) : IOnLoad
{
    public Task OnLoadAsync(CancellationToken cancellationToken)
    {
        var metadata = new ModMetadata();

        log.Success($"v{metadata.Version} loaded -- built for SPT {metadata.SptVersion}");
        log.Info($"mod folder: {log.ModFolder}");
        log.Info("routes: POST /poker/ping, /sit, /deal, /act, /state, /leave");
        log.Info("no-limit Texas Hold'em, up to five seats, against bots that bet back");

        // Said plainly and at boot, because from here on the mod takes real currency
        // out of a real stash and a player deserves to know that before they sit down.
        log.Info("THIS MOD MOVES MONEY. One chip is one rouble: the buy-in is debited when");
        log.Info("you sit down and whatever is left is paid back when you stand up.");

        // Stack limits are deliberately NOT reported here. PostLoad + 1 is not last:
        // BarterItemsStacks rewrites every one of them about half a second after this
        // line runs, so anything printed now is the base database value and wrong on
        // any server with an item mod. They are reported on first contact instead,
        // which is the earliest the answer is trustworthy.

        if (log.Verbose)
        {
            log.Info("verbose logging is ON -- every request and every bot decision will be logged.");
            log.Info("turn it off in config.json once things are working.");
        }

        return Task.CompletedTask;
    }
}
