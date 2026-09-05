namespace Poker.Game;

/// <summary>
/// A single 52-card deck with an injectable <see cref="Random"/>.
///
/// Deliberately simpler than the blackjack shoe this replaces. A shoe exists so
/// several decks can be dealt down to a cut card, which matters only because
/// blackjack is beatable by tracking what has gone. House-banked poker is dealt
/// from a full deck every hand and shuffled between, so there is no penetration,
/// no cut card, and no composition to track.
///
/// The RNG is a constructor parameter purely so tests can force a known deal --
/// production callers should pass an unseeded Random.
/// </summary>
public sealed class Deck
{
    private readonly List<Card> _cards;
    private readonly Random _rng;
    private readonly IGameLog _log;
    private readonly bool _stacked;
    private int _next;

    public Deck(Random? rng = null, IGameLog? log = null)
    {
        _rng = rng ?? new Random();
        _log = log ?? GameLog.Null;
        _cards = new List<Card>(52);
        foreach (Suit suit in Enum.GetValues<Suit>())
        {
            foreach (Rank rank in Enum.GetValues<Rank>())
            {
                _cards.Add(new Card(rank, suit));
            }
        }

        Shuffle();
    }

    /// <summary>
    /// A deck that deals the given cards in exactly this order and is never
    /// shuffled. Exists so tests can pin a deal; a real game must not use it.
    /// </summary>
    public static Deck Stacked(IEnumerable<Card> cards, IGameLog? log = null) => new(cards, log);

    /// <summary>Convenience overload -- <c>Deck.Stacked("AS KS QS ...")</c>.</summary>
    public static Deck Stacked(string codes, IGameLog? log = null) => new(Card.ParseMany(codes), log);

    private Deck(IEnumerable<Card> cards, IGameLog? log)
    {
        _cards = [.. cards];
        _rng = new Random(0);
        _log = log ?? GameLog.Null;
        _stacked = true;

        if (_log.Enabled)
        {
            // Says "stacked" explicitly. A stacked deck that reached a real game is
            // the one deck fault that cannot be seen in the cards themselves.
            _log.Write($"deck: stacked with {_cards.Count} card(s) -- {string.Join(' ', _cards)}");
        }
    }

    public int TotalCards => _cards.Count;

    public int Remaining => _cards.Count - _next;

    public int DealtCount => _next;

    public void Shuffle()
    {
        // A stacked deck exists to deal a fixed sequence; shuffling it would
        // silently destroy whatever a test was pinning.
        if (_stacked)
        {
            if (_log.Enabled)
            {
                _log.Write("deck: shuffle refused -- this deck is stacked");
            }

            return;
        }

        var dealt = _next;

        // Fisher-Yates over the whole list, cursor reset. Undealt cards from the
        // previous hand fold back in, which is what a real shuffle does.
        for (var i = _cards.Count - 1; i > 0; i--)
        {
            var j = _rng.Next(i + 1);
            (_cards[i], _cards[j]) = (_cards[j], _cards[i]);
        }

        _next = 0;

        if (_log.Enabled)
        {
            _log.Write($"deck: shuffled {_cards.Count} card(s), {dealt} dealt card(s) folded back in");
        }
    }

    public Card Draw()
    {
        // Running dry mid-hand would deal cards the player already saw, so treat
        // it as a hard failure rather than silently reshuffling underneath a hand.
        if (_next >= _cards.Count)
        {
            // Logged as well as thrown. By the time the exception surfaces the deck
            // is gone, and how many cards a hand asked for is the whole diagnosis.
            if (_log.Enabled)
            {
                _log.Write($"deck: EXHAUSTED after {_next} card(s) -- a hand asked for more than the deck holds");
            }

            throw new InvalidOperationException("Deck exhausted.");
        }

        var card = _cards[_next++];

        if (_log.Enabled)
        {
            _log.Write($"deck: dealt {card} ({Remaining} left)");
        }

        return card;
    }

    public Card[] Draw(int count)
    {
        var drawn = new Card[count];
        for (var i = 0; i < count; i++)
        {
            drawn[i] = Draw();
        }

        return drawn;
    }
}
