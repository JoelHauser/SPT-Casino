namespace Poker.Game;

/// <summary>A scored hand together with the five cards that earned the score.</summary>
public readonly record struct HandResult(HandRank Rank, IReadOnlyList<Card> Cards)
{
    public override string ToString() => $"{Rank.Describe()} ({string.Join(' ', Cards)})";
}

/// <summary>
/// Ranks five to seven cards, picking the best five when given more.
///
/// The best-of-seven search is a brute-force walk of all 21 combinations rather
/// than one of the table-driven perfect-hash evaluators. That is a deliberate
/// trade: this runs a few dozen times a hand, not a few million, so the only
/// thing worth optimising for is being obviously correct on inspection. The fast
/// evaluators are correct too, but nobody can read one and see that.
/// </summary>
public static class HandEvaluator
{
    public static HandRank Evaluate(IReadOnlyList<Card> cards, IGameLog? log = null) => Best(cards, log).Rank;

    /// <summary>Convenience for tests -- <c>HandEvaluator.Evaluate("AS KS QS JS TS")</c>.</summary>
    public static HandRank Evaluate(string codes, IGameLog? log = null) =>
        Evaluate(Card.ParseMany(codes), log);

    /// <summary>
    /// The best five of what it is given.
    ///
    /// <paramref name="log"/> records the reading, not the search: which five cards
    /// were chosen and what they came to. The 21 combinations behind that are not
    /// logged even when a log is attached -- at showdown that is a seat's worth of
    /// noise per hand, and the losing 20 explain nothing.
    /// </summary>
    public static HandResult Best(IReadOnlyList<Card> cards, IGameLog? log = null)
    {
        if (cards.Count is < 5 or > 7)
        {
            throw new ArgumentException(
                $"A hand is ranked from five to seven cards, got {cards.Count}.", nameof(cards));
        }

        if (cards.Count == 5)
        {
            var only = new HandResult(Evaluate5(cards), [.. cards]);

            if (log?.Enabled == true)
            {
                log.Write($"hand: {string.Join(' ', cards)} reads {only.Rank.Describe()}");
            }

            return only;
        }

        var n = cards.Count;
        var bestRank = default(HandRank);
        Card[] bestCards = [];
        var five = new Card[5];

        // k is fixed at five, so five nested loops say what a general combination
        // generator would say, and say it in a form that can be checked by eye.
        for (var a = 0; a < n - 4; a++)
        for (var b = a + 1; b < n - 3; b++)
        for (var c = b + 1; c < n - 2; c++)
        for (var d = c + 1; d < n - 1; d++)
        for (var e = d + 1; e < n; e++)
        {
            five[0] = cards[a];
            five[1] = cards[b];
            five[2] = cards[c];
            five[3] = cards[d];
            five[4] = cards[e];

            var rank = Evaluate5(five);
            if (bestCards.Length != 0 && rank <= bestRank)
            {
                continue;
            }

            bestRank = rank;
            bestCards = [.. five];
        }

        if (log?.Enabled == true)
        {
            log.Write(
                $"hand: best of {n} from {string.Join(' ', cards)} "
                + $"is {string.Join(' ', bestCards)} -- {bestRank.Describe()}");
        }

        return new HandResult(bestRank, bestCards);
    }

    private static HandRank Evaluate5(IReadOnlyList<Card> cards)
    {
        Span<int> counts = stackalloc int[15];
        var mask = 0;
        var isFlush = true;

        foreach (var card in cards)
        {
            counts[(int)card.Rank]++;
            mask |= 1 << (int)card.Rank;
            isFlush &= card.Suit == cards[0].Suit;
        }

        var straightHigh = StraightHigh(mask);

        // Walking down from the ace leaves every list already in descending order,
        // which is the order the kickers have to be handed over in.
        Rank? quad = null;
        Rank? trip = null;
        var pairs = new List<Rank>(2);
        var singles = new List<Rank>(5);

        for (var rank = (int)Rank.Ace; rank >= (int)Rank.Two; rank--)
        {
            switch (counts[rank])
            {
                case 4: quad = (Rank)rank; break;
                case 3: trip = (Rank)rank; break;
                case 2: pairs.Add((Rank)rank); break;
                case 1: singles.Add((Rank)rank); break;
            }
        }

        if (isFlush && straightHigh is not 0)
        {
            return HandRank.Create(HandCategory.StraightFlush, (Rank)straightHigh);
        }

        if (quad is not null)
        {
            return HandRank.Create(HandCategory.FourOfAKind, quad.Value, singles[0]);
        }

        // Five cards cannot hold trips and two pairs at once, so one pair alongside
        // trips is always the full house's pair.
        if (trip is not null && pairs.Count > 0)
        {
            return HandRank.Create(HandCategory.FullHouse, trip.Value, pairs[0]);
        }

        // A five-card flush is five distinct ranks -- one suit cannot pair -- so
        // every card is a single and the whole hand is its own tiebreaker.
        if (isFlush)
        {
            return HandRank.Create(
                HandCategory.Flush, singles[0], singles[1], singles[2], singles[3], singles[4]);
        }

        if (straightHigh is not 0)
        {
            return HandRank.Create(HandCategory.Straight, (Rank)straightHigh);
        }

        if (trip is not null)
        {
            return HandRank.Create(HandCategory.ThreeOfAKind, trip.Value, singles[0], singles[1]);
        }

        if (pairs.Count == 2)
        {
            return HandRank.Create(HandCategory.TwoPair, pairs[0], pairs[1], singles[0]);
        }

        if (pairs.Count == 1)
        {
            return HandRank.Create(
                HandCategory.Pair, pairs[0], singles[0], singles[1], singles[2]);
        }

        return HandRank.Create(
            HandCategory.HighCard, singles[0], singles[1], singles[2], singles[3], singles[4]);
    }

    /// <summary>
    /// The rank a straight tops out at, or zero for no straight.
    ///
    /// The wheel is checked last and answers Five, not Ace: A-2-3-4-5 is the
    /// weakest straight there is, and calling it ace-high would make it the
    /// strongest. That inversion is the classic straight-detection bug.
    /// </summary>
    private static int StraightHigh(int mask)
    {
        for (var high = (int)Rank.Ace; high >= (int)Rank.Six; high--)
        {
            var run = 0b11111 << (high - 4);
            if ((mask & run) == run)
            {
                return high;
            }
        }

        const int Wheel = (1 << (int)Rank.Ace)
            | (1 << (int)Rank.Five)
            | (1 << (int)Rank.Four)
            | (1 << (int)Rank.Three)
            | (1 << (int)Rank.Two);

        return (mask & Wheel) == Wheel ? (int)Rank.Five : 0;
    }
}
