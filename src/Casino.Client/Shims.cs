using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;

// Each table used to live in its own plugin and reached for it by name: a log to write
// to, and a MonoBehaviour to start coroutines on and to locate its art beside. There is
// one plugin now, so these stand in its place -- same names, same namespaces, so not a
// line of the three tables had to change.
//
// They are deliberately the whole of the seam. Everything else those files touch is
// their own: their panel, their views, their API client. The tables never knew about
// the task bar, the menu icon or the escape key, which is why the entrance could be
// lifted out from under them without taking anything with it.
//
// `Instance` is a BaseUnityPlugin rather than the old concrete type, which is all the
// code ever needed of it: `Info.Location` to find the art, and `StartCoroutine` to run
// an animation.
namespace Roulette.Client
{
    internal static class RouletteClientPlugin
    {
        internal static BaseUnityPlugin Instance;

        internal static ManualLogSource Log;
    }
}

namespace Poker.Client
{
    internal static class PokerClientPlugin
    {
        internal static BaseUnityPlugin Instance;

        internal static ManualLogSource Log;
    }
}

namespace Blackjack.Client
{
    internal static class BlackjackClientPlugin
    {
        internal static BaseUnityPlugin Instance;

        internal static ManualLogSource Log;

        /// <summary>
        /// Blackjack's one table setting, kept because the panel reads it. Bound by
        /// <see cref="Casino.Client.CasinoPlugin"/> along with the casino's own.
        /// </summary>
        internal static ConfigEntry<bool> EnforceTableMaximum;
    }
}
