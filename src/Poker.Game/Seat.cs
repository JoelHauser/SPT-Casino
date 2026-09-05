namespace Poker.Game;

/// <summary>How a seat's hand finished, as one word for the table to read.</summary>
public enum SeatOutcome
{
    Pending,
    Won,
    Lost,
    Push,

    /// <summary>Gave up the Ante and Blind at the river. Trips still resolved.</summary>
    Folded,
}

/// <summary>
/// One seat's hand: its cards, its three bets, and what came back.
///
/// A seat is not a player. Seat 0 is the person at the keyboard and the rest are
/// seat-mates, but the table settles all of them the same way -- which is what makes
/// the bots exercisable by the same tests. The difference lives entirely in where
/// the decision comes from, never in how the hand is scored.
///
/// Amounts are ints with no currency attached, as everywhere in this engine. The
/// bots' numbers are notional and never leave it.
/// </summary>
public sealed class Seat
{
    private readonly List<Card> _cards = [];

    internal Seat(int index, bool isPlayer, string name)
    {
        Index = index;
        IsPlayer = isPlayer;
        Name = name;
    }

    public int Index { get; }

    /// <summary>True for the one seat whose decisions arrive from outside the engine.</summary>
    public bool IsPlayer { get; }

    public string Name { get; }

    public IReadOnlyList<Card> Cards => _cards;

    /// <summary>The mandatory bet the hand is sized from. Everything else is a multiple of it.</summary>
    public int Ante { get; internal set; }

    /// <summary>Equal to the Ante and mandatory with it. Pays on its own paytable.</summary>
    public int Blind { get; internal set; }

    /// <summary>Optional side bet, resolved on this seat's own hand even if it folds.</summary>
    public int Trips { get; internal set; }

    /// <summary>
    /// Zero until the seat bets. Made exactly once, and its size records when: 4x or
    /// 3x on the hole cards, 2x after the flop, 1x at the river.
    /// </summary>
    public int Play { get; internal set; }

    /// <summary>Set when the seat has made its Play bet and has no decisions left.</summary>
    public bool HasPlayed => Play > 0;

    public bool Folded { get; internal set; }

    /// <summary>The best five of this seat's seven, filled in at showdown.</summary>
    public HandRank? Hand { get; internal set; }

    public SeatOutcome Outcome { get; internal set; } = SeatOutcome.Pending;

    /// <summary>Stake plus winnings, not profit -- the same meaning Blackjack's Hand.Returned has.</summary>
    public int Returned { get; internal set; }

    public int Wagered => Ante + Blind + Trips + Play;

    /// <summary>
    /// Profit or loss on this hand. Negative means the house won it.
    /// </summary>
    public int Net => Returned - Wagered;

    /// <summary>Whether this seat still has a decision to make on the current street.</summary>
    public bool IsActive => !Folded && !HasPlayed;

    internal void Add(Card card) => _cards.Add(card);

    internal void ClearForNewHand()
    {
        _cards.Clear();
        Ante = 0;
        Blind = 0;
        Trips = 0;
        Play = 0;
        Folded = false;
        Hand = null;
        Outcome = SeatOutcome.Pending;
        Returned = 0;
    }

    public override string ToString() =>
        $"{Name}: {string.Join(' ', _cards)}{(Folded ? " (folded)" : string.Empty)}";
}
