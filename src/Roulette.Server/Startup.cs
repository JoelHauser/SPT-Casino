using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;

namespace Roulette.Server;

/// <summary>
/// Announces the mod on the server console at boot.
///
/// The most important thing here is not any single line -- it is that the block
/// appears at all. A mod rejected by the SptVersion gate loads nothing and logs
/// nothing, so silence at startup means the gate rather than a bug in the game code.
/// </summary>
[Injectable(TypePriority = OnLoadOrder.PostLoad + 1)]
public class Startup(RouletteLog log) : IOnLoad
{
    public Task OnLoadAsync(CancellationToken cancellationToken)
    {
        var metadata = new ModMetadata();

        log.Success($"v{metadata.Version} loaded -- built for SPT {metadata.SptVersion}");
        log.Info($"mod folder: {log.ModFolder}");
        log.Info("routes: POST /roulette/ping, /place, /remove, /clear, /spin, /state, /leave");
        log.Info("single-zero European wheel -- 37 pockets, 2.70% to the house on every bet");

        // Said plainly and at boot, because a stash that never changes otherwise reads
        // as the mod being broken rather than as the mod being unfinished.
        log.Info("THE CHIPS ARE NOT CURRENCY IN THIS BUILD. Nothing is taken from your stash");
        log.Info("and nothing is paid into it. The money path is not written yet.");

        // Stack limits are deliberately NOT reported here. PostLoad + 1 is not last:
        // BarterItemsStacks rewrites every one of them about half a second after this
        // line runs, so anything printed now is the base database value and wrong on
        // any server with an item mod. They are reported on first contact instead,
        // which is the earliest the answer is trustworthy.

        if (log.Verbose)
        {
            log.Info("verbose logging is ON -- every request and every spin will be logged.");
            log.Info("turn it off in config.json once things are working.");
        }

        return Task.CompletedTask;
    }
}
