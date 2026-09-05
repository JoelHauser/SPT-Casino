namespace Poker.Game;

/// <summary>
/// One seat in a hold'em hand: its chips, its cards, and what it has put in.
///
/// Unlike the parked UTH seat, this one owns a **stack**. That is the whole
/// difference between the two games on the money side: a stack is what a bet is
/// sized against, what an all-in means, and what decides which side pot a seat is
/// eligible for. Nothing about hold'em can be settled without it.
///
/// Seat 0 is the player and the rest are bots, but the table treats them
/// identically -- the only difference is where the decision comes from. Bot chips
/// are notional and never reach a profile; the player's are real currency, converted
/// at the buy-in.
/// </summary>
public sealed class HoldemSeat
{
    private readonly List<Card> _cards = [];

    internal HoldemSeat(int index, bool isPlayer, string name, int stack)
    {
        Index = index;
        IsPlayer = isPlayer;
        Name = name;
        Stack = stack;
    }

    public int Index { get; }

    /// <summary>True for the one seat whose decisions arrive from outside the engine.</summary>
    public bool IsPlayer { get; }

    public string Name { get; }

    public int Stack { get; internal set; }

    public IReadOnlyList<Card> Cards => _cards;

    /// <summary>
    /// Chips pushed forward on the current street. Reset every street, and what a
    /// call or a raise is measured against.
    /// </summary>
    public int CommittedThisStreet { get; internal set; }

    /// <summary>
    /// Chips pushed forward across the whole hand. This is what the pots are built
    /// from, and it is why a folded seat still matters -- it paid in.
    /// </summary>
    public int CommittedThisHand { get; internal set; }

    public bool Folded { get; internal set; }

    /// <summary>
    /// Whether this seat has acted since the last bet or raise. Posting a blind does
    /// not count, which is exactly what gives the big blind its option to raise when
    /// everyone has only called.
    /// </summary>
    public bool HasActed { get; internal set; }

    /// <summary>
    /// False when an all-in too small to be a full raise has come in behind a seat
    /// that already acted. Such a seat must call the extra or fold; it does not get
    /// to raise again. This is the rule most implementations get wrong.
    /// </summary>
    public bool MayRaise { get; internal set; } = true;

    /// <summary>Still in the hand, whether or not it has chips left to bet.</summary>
    public bool InHand => !Folded;

    /// <summary>In the hand with nothing left to put in.</summary>
    public bool IsAllIn => !Folded && Stack == 0;

    /// <summary>In the hand and able to make a decision.</summary>
    public bool CanAct => !Folded && Stack > 0;

    /// <summary>The best five of this seat's seven, filled in at showdown.</summary>
    public HandRank? Hand { get; internal set; }

    /// <summary>Chips taken from the pots at the end of the hand.</summary>
    public int Won { get; internal set; }

    /// <summary>
    /// What was in front of this seat before the blinds went out.
    ///
    /// The only honest way to say what a hand cost: winnings minus what was committed
    /// misses an uncalled bet coming back, and reports a raise that everybody folded
    /// to as a large loss rather than as a small win.
    /// </summary>
    public int StackAtHandStart { get; internal set; }

    /// <summary>What the hand actually made or cost, refunds included.</summary>
    public int Net => Stack - StackAtHandStart;

    internal void Add(Card card) => _cards.Add(card);

    /// <summary>
    /// Moves chips from the stack to the middle, never more than there are. Returns
    /// what actually moved, which is less than asked for when the seat is all-in --
    /// and calling for less than the bet is legal, so this is not an error.
    /// </summary>
    internal int Commit(int amount)
    {
        var moved = Math.Min(amount, Stack);

        Stack -= moved;
        CommittedThisStreet += moved;
        CommittedThisHand += moved;

        return moved;
    }

    internal void ClearForNewHand()
    {
        _cards.Clear();
        CommittedThisStreet = 0;
        CommittedThisHand = 0;
        Folded = false;
        HasActed = false;
        MayRaise = true;
        Hand = null;
        Won = 0;
    }

    internal void ClearForNewStreet()
    {
        CommittedThisStreet = 0;
        HasActed = false;
        MayRaise = true;
    }

    public override string ToString() =>
        $"{Name} ({Stack})" + (Folded ? " folded" : IsAllIn ? " all-in" : string.Empty);
}
