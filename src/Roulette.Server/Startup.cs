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
        log.Banner("routes: POST /roulette/ping, /place, /remove, /clear, /spin, /state, /leave");
        log.Banner($"item event: {RouletteActions.Sync}, so the stash keeps up without a reload");
        log.Banner("single-zero European wheel -- 37 pockets, 2.70% to the house on every bet");

        // Said plainly and at boot, because this is the line that stops being true
        // quietly. It moves roubles now.
        log.Banner("THIS TABLE PLAYS FOR REAL ROUBLES. The stake leaves your stash when the");
        log.Banner("wheel turns and the return is paid back when it stops. Chips on the cloth");
        log.Banner("cost nothing until you spin.");

        // Stack limits are deliberately NOT reported here. PostLoad + 1 is not last:
        // BarterItemsStacks rewrites every one of them about half a second after this
        // line runs, so anything printed now is the base database value and wrong on
        // any server with an item mod. They are reported on first contact instead,
        // which is the earliest the answer is trustworthy.

        return Task.CompletedTask;
    }
}
