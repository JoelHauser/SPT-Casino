namespace War.Game;

/// <summary>
/// Six decks, shuffled together, dealt from the top.
///
/// A real shoe rather than a fresh random card per draw, because that is the game the
/// odds are quoted for: the first two cards are drawn without replacement, which is
/// what makes a tie 23 in 311 rather than 1 in 13.
/// </summary>
public sealed class Shoe
{
    private readonly Random _rng;
    private readonly int _decks;
    private readonly List<Card> _cards = [];
    private readonly bool _stacked;
    private int _next;

    public Shoe(int decks, Random rng)
    {
        if (decks < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(decks), decks, "A shoe needs at least one deck.");
        }

        _decks = decks;
        _rng = rng;
        Shuffle();
    }

    private Shoe(IEnumerable<Card> order)
    {
        _rng = new Random(0);
        _decks = 1;
        _cards.AddRange(order);
        _stacked = true;
    }

    /// <summary>
    /// A shoe that deals exactly these cards in this order and never shuffles.
    ///
    /// Test seam, the same shape as Blackjack's: the only way to pin a tie, a war or a
    /// war that ties again, without depending on a seed that happens to produce one.
    /// </summary>
    internal static Shoe Stacked(IEnumerable<Card> order) => new(order);

    /// <summary>How many cards a full shoe holds.</summary>
    public int Size => _stacked ? _cards.Count : _decks * 52;

    public int Remaining => _cards.Count - _next;

    /// <summary>Whether the cut card has come out. Checked only between rounds.</summary>
    public bool NeedsShuffle => !_stacked && Remaining < Rules.ReshuffleBelow;

    public Card Draw()
    {
        if (_next >= _cards.Count)
        {
            // Unreachable from a table, which shuffles between rounds well before this.
            // A stacked shoe that runs dry is a test asking for more than it stacked.
            throw new InvalidOperationException("The shoe is empty.");
        }

        return _cards[_next++];
    }

    /// <summary>Takes cards off the top and puts them in the discard, face down.</summary>
    public void Burn(int count)
    {
        for (var i = 0; i < count; i++)
        {
            Draw();
        }
    }

    public void Shuffle()
    {
        if (_stacked)
        {
            return;
        }

        _cards.Clear();

        for (var deck = 0; deck < _decks; deck++)
        {
            foreach (var suit in Enum.GetValues<Suit>())
            {
                for (var rank = Card.Lowest; rank <= Card.Ace; rank++)
                {
                    _cards.Add(new Card(rank, suit));
                }
            }
        }

        // Fisher-Yates. Every order equally likely, which a sort on a random key is not.
        for (var i = _cards.Count - 1; i > 0; i--)
        {
            var j = _rng.Next(i + 1);
            (_cards[i], _cards[j]) = (_cards[j], _cards[i]);
        }

        _next = 0;
    }
}
