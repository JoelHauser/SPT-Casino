namespace Casino.Client
{
    /// <summary>
    /// Gets the expensive drawing out of the way while the player is still looking at
    /// the menu.
    ///
    /// Only roulette needs it, and it needs it badly: the wheel is two passes over a
    /// 1024-square texture at four samples a pixel, which measured at 1274ms of the
    /// 1489ms the table took to open. Painted ahead of time it is 41ms, which is the
    /// two texture uploads and nothing else.
    ///
    /// Kept here rather than called directly from the tab so that a future table with
    /// its own expensive art has somewhere obvious to join in.
    /// </summary>
    internal static class CasinoWarmup
    {
        internal static void Begin() => Roulette.Client.RoulettePanel.Prewarm();
    }
}
