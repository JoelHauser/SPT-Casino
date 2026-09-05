using System;
using System.Collections.Generic;

namespace Casino.Client
{
    /// <summary>
    /// One table in the casino.
    ///
    /// The seam a future game is added through: write the panel, implement this, add a
    /// line to <see cref="Casino.Games"/>. No second tab, no second GUID, no second
    /// plugin, and nothing in the lobby or the escape key needs to know it happened.
    ///
    /// Deliberately small. Everything here is either something the lobby draws or
    /// something the escape key needs, and the three existing tables satisfied all of
    /// it already -- each has had a static Open, Close and IsOpen since before there
    /// was a casino to put them in.
    /// </summary>
    internal interface ICasinoGame
    {
        /// <summary>As printed on the lobby tile.</summary>
        string Name { get; }

        /// <summary>
        /// The table's artwork on the lobby tile, as a filename beside the plugin.
        /// </summary>
        string Icon { get; }

        /// <summary>
        /// The card suit to draw if <see cref="Icon"/> is not on disk. A tile with
        /// nothing on it is worse than a tile with the wrong shape on it.
        /// </summary>
        char Pip { get; }

        /// <summary>One line under the name. What the game is, not how to play it.</summary>
        string Blurb { get; }

        bool IsOpen { get; }

        void Open();

        void Close();
    }

    /// <summary>
    /// The games this build knows about, in the order the lobby shows them.
    ///
    /// Blackjack first because it is the one everybody knows, roulette last because it
    /// is the newest. Nothing depends on the order but the lobby.
    /// </summary>
    internal static class Games
    {
        internal static readonly IReadOnlyList<ICasinoGame> All = new ICasinoGame[]
        {
            new Table(
                "BLACKJACK",
                "tile-blackjack.png",
                'D',
                "Twenty-one against the dealer.",
                () => Blackjack.Client.BlackjackPanel.IsOpen,
                Blackjack.Client.BlackjackPanel.Open,
                Blackjack.Client.BlackjackPanel.Close),

            new Table(
                "POKER",
                "tile-poker.png",
                'S',
                "No-limit hold'em against the house's regulars.",
                () => Poker.Client.PokerPanel.IsOpen,
                Poker.Client.PokerPanel.Open,
                Poker.Client.PokerPanel.Close),

            new Table(
                "ROULETTE",
                "tile-roulette.png",
                'H',
                "A single-zero wheel. 2.70% to the house, every bet.",
                () => Roulette.Client.RoulettePanel.IsOpen,
                Roulette.Client.RoulettePanel.Open,
                Roulette.Client.RoulettePanel.Close),
        };

        /// <summary>The table the player is at, or null if they are in the lobby.</summary>
        internal static ICasinoGame Playing()
        {
            foreach (var game in All)
            {
                if (game.IsOpen)
                {
                    return game;
                }
            }

            return null;
        }

        /// <summary>Shuts every table. Used when the casino closes, and when a raid starts.</summary>
        internal static void CloseAll()
        {
            foreach (var game in All)
            {
                if (game.IsOpen)
                {
                    game.Close();
                }
            }
        }

        /// <summary>
        /// A game, described by three delegates onto a panel that already existed.
        ///
        /// The tables are static classes and stay that way. Wrapping them here rather
        /// than making each implement the interface is what kept the merge from
        /// touching a line of code that moves money.
        /// </summary>
        private sealed class Table : ICasinoGame
        {
            private readonly Func<bool> _isOpen;
            private readonly Action _open;
            private readonly Action _close;

            internal Table(
                string name,
                string icon,
                char pip,
                string blurb,
                Func<bool> isOpen,
                Action open,
                Action close)
            {
                Name = name;
                Icon = icon;
                Pip = pip;
                Blurb = blurb;
                _isOpen = isOpen;
                _open = open;
                _close = close;
            }

            public string Name { get; }

            public string Icon { get; }

            public char Pip { get; }

            public string Blurb { get; }

            public bool IsOpen => _isOpen();

            public void Open() => _open();

            public void Close() => _close();
        }
    }
}
