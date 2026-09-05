namespace Blackjack.Game;

/// <summary>
/// A multi-deck shoe with an injectable <see cref="Random"/>. The RNG is a
/// constructor parameter purely so tests can force a known deal -- production
/// callers should pass an unseeded Random.
/// </summary>
public sealed class Shoe
{
    private readonly List<Card> _cards;
    private readonly Random _rng;
    private readonly bool _stacked;
    private int _next;

    public Shoe(int deckCount, Random? rng = null)
    {
        if (deckCount < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(deckCount), deckCount, "A shoe needs at least one deck.");
        }

        _rng = rng ?? new Random();
        _cards = new List<Card>(deckCount * 52);
        for (var deck = 0; deck < deckCount; deck++)
        {
            foreach (Suit suit in Enum.GetValues<Suit>())
            {
                foreach (Rank rank in Enum.GetValues<Rank>())
                {
                    _cards.Add(new Card(rank, suit));
                }
            }
        }

        Shuffle();
    }

    /// <summary>
    /// A shoe that deals the given cards in exactly this order and is never
    /// shuffled. Exists so tests can pin a deal; a real game must not use it.
    /// </summary>
    public static Shoe Stacked(IEnumerable<Card> cards) => new(cards);

    private Shoe(IEnumerable<Card> cards)
    {
        _cards = [.. cards];
        _rng = new Random(0);
        _stacked = true;
    }

    public int TotalCards => _cards.Count;

    public int Remaining => _cards.Count - _next;

    public int DealtCount => _next;

    public void Shuffle()
    {
        // A stacked shoe exists to deal a fixed sequence; shuffling it would
        // silently destroy whatever a test was pinning.
        if (_stacked)
        {
            return;
        }

        // Fisher-Yates. Shuffling the whole list and resetting the cursor means
        // undealt cards from the previous shoe are folded back in, which is what
        // a real reshuffle does.
        for (var i = _cards.Count - 1; i > 0; i--)
        {
            var j = _rng.Next(i + 1);
            (_cards[i], _cards[j]) = (_cards[j], _cards[i]);
        }

        _next = 0;
    }

    public Card Draw()
    {
        // Running dry mid-round would deal cards the player already saw, so treat
        // it as a hard failure rather than silently reshuffling underneath a hand.
        if (_next >= _cards.Count)
        {
            throw new InvalidOperationException("Shoe exhausted.");
        }

        return _cards[_next++];
    }

    /// <summary>
    /// True once the cut card is reached. Checked between rounds only -- never
    /// mid-hand, or the shoe would change composition while a hand is live.
    /// </summary>
    public bool NeedsShuffle(double penetration) => !_stacked && _next >= _cards.Count * penetration;
}
