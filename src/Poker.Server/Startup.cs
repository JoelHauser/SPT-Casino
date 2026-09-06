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
        log.Banner("routes: POST /poker/ping, /sit, /deal, /act, /state, /leave");
        log.Banner("no-limit Texas Hold'em, up to five seats, against bots that bet back");

        // Said plainly and at boot, because from here on the mod takes real currency
        // out of a real stash and a player deserves to know that before they sit down.
        log.Banner("THIS MOD MOVES MONEY. One chip is one rouble: the buy-in is debited when");
        log.Banner("you sit down and whatever is left is paid back when you stand up.");

        // Stack limits are deliberately NOT reported here. PostLoad + 1 is not last:
        // BarterItemsStacks rewrites every one of them about half a second after this
        // line runs, so anything printed now is the base database value and wrong on
        // any server with an item mod. They are reported on first contact instead,
        // which is the earliest the answer is trustworthy.

        return Task.CompletedTask;
    }
}
