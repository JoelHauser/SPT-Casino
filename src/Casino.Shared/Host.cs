using System.IO;
using BepInEx;
using BepInEx.Logging;

namespace Casino.Shared
{
    /// <summary>
    /// The two things the shared drawing code needs from whoever is hosting it.
    ///
    /// These files used to live once per table and reach for that table's own plugin
    /// by name, which is most of why they could not simply be shared. There are only
    /// ever two such reaches: where the art is, and where to log. Both are set once at
    /// startup.
    ///
    /// Deliberately tolerant of never being set. <see cref="Textures"/> touches neither
    /// and is pure arithmetic; the card and chip faces fall back to something drawn
    /// rather than failing, and a null log is a no-op. A shared file that throws
    /// because a host forgot to introduce itself would be worse than the duplication
    /// it replaced.
    /// </summary>
    internal static class Host
    {
        internal static BaseUnityPlugin Plugin;

        internal static ManualLogSource Log;

        /// <summary>
        /// The folder the art sits in, which is the one the plugin was loaded from.
        /// Falls back to the working directory, where it will find nothing and every
        /// caller already copes with that.
        /// </summary>
        internal static string AssetFolder =>
            Path.GetDirectoryName(Plugin?.Info?.Location ?? ".") ?? ".";

        internal static void Warn(string message) => Log?.LogWarning(message);

        internal static void Error(string message) => Log?.LogError(message);
    }
}
