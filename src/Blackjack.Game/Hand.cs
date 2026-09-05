namespace Blackjack.Game;

public enum HandStatus
{
    /// <summary>Still the player's to act on.</summary>
    Active,
    Stood,
    Bust,
    Doubled,
    Blackjack,
}

public sealed class Hand
{
    private readonly List<Card> _cards = [];

    public Hand(int wager, bool fromSplit = false)
    {
        Wager = wager;
        IsFromSplit = fromSplit;
    }

    public IReadOnlyList<Card> Cards => _cards;

    /// <summary>Doubling mutates this, so it is the amount actually at risk.</summary>
    public int Wager { get; private set; }

    /// <summary>
    /// Set when this hand is produced by (or survives) a split. Not readonly:
    /// the hand that keeps the original seat becomes a split hand at that moment.
    /// </summary>
    public bool IsFromSplit { get; internal set; }

    public HandStatus Status { get; internal set; } = HandStatus.Active;

    public HandOutcome Outcome { get; internal set; } = HandOutcome.Pending;

    /// <summary>Roubles coming back to the player: stake plus winnings, not profit.</summary>
    public int Returned { get; internal set; }

    /// <summary>
    /// Best total not exceeding 21, or the hard total once that is impossible.
    /// Only one ace can ever count as 11 -- two would be 22 -- so this needs a
    /// single conditional promotion, not a loop over the aces.
    /// </summary>
    public int Value
    {
        get
        {
            var total = 0;
            var hasAce = false;
            foreach (var card in _cards)
            {
                total += card.BaseValue;
                hasAce |= card.IsAce;
            }

            return hasAce && total + 10 <= 21 ? total + 10 : total;
        }
    }

    /// <summary>True when an ace is being counted as 11 and could still shrink.</summary>
    public bool IsSoft
    {
        get
        {
            var hard = _cards.Sum(card => card.BaseValue);
            return _cards.Any(card => card.IsAce) && hard + 10 <= 21;
        }
    }

    public bool IsBust => Value > 21;

    /// <summary>
    /// A natural: 21 on the first two cards. A 21 assembled after a split is not
    /// a blackjack and pays even money, which is why IsFromSplit is checked here.
    /// </summary>
    public bool IsBlackjack => _cards.Count == 2 && Value == 21 && !IsFromSplit;

    public bool CanSplit =>
        _cards.Count == 2 && _cards[0].BaseValue == _cards[1].BaseValue;

    internal void Add(Card card)
    {
        _cards.Add(card);
        if (IsBust)
        {
            Status = HandStatus.Bust;
        }
    }

    internal void DoubleWager() => Wager *= 2;

    /// <summary>Moves the second card out to seed a new hand when splitting.</summary>
    internal Card RemoveSecondCard()
    {
        var card = _cards[1];
        _cards.RemoveAt(1);
        return card;
    }
}
