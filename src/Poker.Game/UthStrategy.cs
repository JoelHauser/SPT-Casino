namespace Poker.Game;

/// <summary>
/// Correct play, as a lookup.
///
/// Ultimate Texas Hold'em has a published strategy and no search behind it: the
/// right answer at each of the three decision points is a rule over the hole cards
/// and the board. This is the widely circulated simple strategy, which gives up a
/// hundredth of a percent or so against the full one and is a table a person can
/// check by eye -- the same trade the evaluator makes.
///
/// It does two jobs, and the second is why it is worth getting right:
///
/// 1. It is the brain behind the seat-mates. <see cref="SeatMateAgent"/> loosens it
///    for character rather than replacing it.
/// 2. It is the oracle the house edge is measured against. A settlement bug shows up
///    as a table that no longer returns what correct play should return, and nothing
///    but correct play can detect that.
///
/// Every decision is logged with the rule that produced it. A bot that raises for a
/// reason nobody can read is indistinguishable from a bot that raises at random.
/// </summary>
public static class UthStrategy
{
    /// <summary>
    /// On the hole cards: bet the large multiple, or check and get a cheaper look.
    ///
    /// Raise with any pair from threes up, any ace, and the better broadway holdings
    /// -- suited hands qualify a rank or two lower than their offsuit twins, which is
    /// the flush equity showing up in the table.
    /// </summary>
    public static bool RaisesOnHoleCards(Card first, Card second, IGameLog? log = null)
    {
        var high = (int)(first.Rank > second.Rank ? first.Rank : second.Rank);
        var low = (int)(first.Rank > second.Rank ? second.Rank : first.Rank);
        var suited = first.Suit == second.Suit;

        var (raise, why) = Decide();

        if (log?.Enabled == true)
        {
            log.Write($"strategy: {first}{second} -- {why}");
        }

        return raise;

        (bool, string) Decide()
        {
            // A pair of twos is the one pair that is not worth four times the ante:
            // it is beaten by any board pair and improves too rarely to pay for the
            // size of the bet.
            if (high == low)
            {
                return high >= (int)Rank.Three
                    ? (true, "pair of threes or better, raise 4x")
                    : (false, "pair of twos, check");
            }

            if (high == (int)Rank.Ace)
            {
                return (true, "any ace, raise 4x");
            }

            return high switch
            {
                (int)Rank.King => suited || low >= (int)Rank.Five
                    ? (true, suited ? "suited king, raise 4x" : "king with a five or better, raise 4x")
                    : (false, "offsuit king with a weak kicker, check"),

                (int)Rank.Queen => (suited ? low >= (int)Rank.Six : low >= (int)Rank.Eight)
                    ? (true, "queen with a live kicker, raise 4x")
                    : (false, "queen too weak to raise, check"),

                (int)Rank.Jack => (suited ? low >= (int)Rank.Eight : low == (int)Rank.Ten)
                    ? (true, "jack with a live kicker, raise 4x")
                    : (false, "jack too weak to raise, check"),

                (int)Rank.Ten => suited && low >= (int)Rank.Eight
                    ? (true, "suited ten-eight or better, raise 4x")
                    : (false, "ten too weak to raise, check"),

                _ => (false, "nothing worth four times the ante, check"),
            };
        }
    }

    /// <summary>
    /// On the flop: bet twice the ante, or check into the river.
    ///
    /// Two pair or better always. A pair made with a hole card otherwise -- except
    /// the one made by pairing the board's lowest card, which is too often already
    /// beaten. Four to a flush counts when the hole card in it is a ten or better,
    /// because a low flush draw that fills can still lose to the dealer's.
    /// </summary>
    public static bool RaisesOnFlop(
        IReadOnlyList<Card> hole,
        IReadOnlyList<Card> board,
        IGameLog? log = null)
    {
        var rank = HandEvaluator.Evaluate([.. hole, .. board]);

        var (raise, why) = Decide();

        if (log?.Enabled == true)
        {
            log.Write($"strategy: {string.Join(' ', hole)} on {string.Join(' ', board)} -- {why}");
        }

        return raise;

        (bool, string) Decide()
        {
            if (rank.Category >= HandCategory.TwoPair)
            {
                return (true, $"{rank.Describe()}, raise 2x");
            }

            // A pocket pair is hidden by construction -- it is nowhere on the board --
            // so the lowest-card exception below cannot apply to it.
            if (hole[0].Rank == hole[1].Rank)
            {
                return (true, $"pocket pair of {hole[0].Rank}s, raise 2x");
            }

            var paired = PairedWithBoard(hole, board);
            if (paired is not null)
            {
                var lowest = board.Min(card => card.Rank);
                return paired.Value > lowest
                    ? (true, $"hidden pair of {paired}s, raise 2x")
                    : (false, "hidden pair, but on the board's lowest card, check");
            }

            return FourToAFlush(hole, board)
                ? (true, "four to a flush with a high hole card, raise 2x")
                : (false, "nothing worth twice the ante, check");
        }
    }

    /// <summary>
    /// At the river: back the hand for one more ante, or give up the Ante and the
    /// Blind.
    ///
    /// This one is computed rather than looked up, because at the river there is
    /// nothing left to estimate. Every card is out; the only unknown is the dealer's
    /// two, and there are exactly 990 ways to draw them from the 45 cards nobody can
    /// see. Walking all of them gives the exact value of betting, and the comparison
    /// against the value of folding is then arithmetic rather than judgement.
    ///
    /// It is worth the work because folding is the expensive decision in this game.
    /// Giving up costs two antes outright, so a hand only has to be better than that
    /// to be worth one more -- which makes the fold much rarer than it looks, and
    /// makes a rule of thumb that folds too often the single most costly thing a
    /// strategy can get wrong. A hand-waved version of this rule cost six points of
    /// house edge before it was measured.
    ///
    /// Roughly four milliseconds a decision. That is nothing once a hand, and the
    /// reason the simulation in the tests runs at the size it does.
    /// </summary>
    public static bool BetsOnRiver(
        IReadOnlyList<Card> hole,
        IReadOnlyList<Card> board,
        Rules? rules = null,
        IGameLog? log = null)
    {
        rules ??= new Rules();

        var mine = HandEvaluator.Evaluate([.. hole, .. board]);

        // What the Blind pays on this hand, per unit staked. Pushing reads as zero
        // here, which is exactly right -- the Blind coming back is neither a gain
        // nor a loss.
        var blind = rules.Blind.For(mine);
        var blindProfit = blind.IsPush ? 0d : (double)blind.Numerator / blind.Denominator;

        var seen = new HashSet<Card>(hole);
        seen.UnionWith(board);

        var unseen = new List<Card>(45);
        foreach (var suit in Enum.GetValues<Suit>())
        {
            foreach (var rank in Enum.GetValues<Rank>())
            {
                var card = new Card(rank, suit);
                if (!seen.Contains(card))
                {
                    unseen.Add(card);
                }
            }
        }

        var total = 0d;
        var hands = 0;

        for (var first = 0; first < unseen.Count - 1; first++)
        {
            for (var second = first + 1; second < unseen.Count; second++)
            {
                var dealer = HandEvaluator.Evaluate([unseen[first], unseen[second], .. board]);
                var qualified = dealer.Category >= rules.DealerQualifies;
                var result = mine.CompareTo(dealer);

                // In antes, and matching how the table settles: the Ante only pays
                // when the dealer opens, the Play always resolves, and the Blind
                // pays its own table on a win and is never lost on a push.
                var value = 0d;

                if (qualified)
                {
                    value += Math.Sign(result);
                }

                value += Math.Sign(result) * rules.RiverRaise;

                value += result > 0 ? blindProfit : result < 0 ? -1 : 0;

                total += value;
                hands++;
            }
        }

        var betting = total / hands;

        // Folding is not free and never was: the Ante and the Blind both go.
        const double Folding = -2d;

        var bet = betting > Folding;

        if (log?.Enabled == true)
        {
            log.Write(
                $"strategy: {string.Join(' ', hole)} on {string.Join(' ', board)} -- {mine.Describe()}, "
                + $"betting is worth {betting:F3} antes against {Folding:F0} for folding, so {(bet ? "bet 1x" : "fold")}");
        }

        return bet;
    }

    /// <summary>The rank a hole card pairs on the board, if any.</summary>
    private static Rank? PairedWithBoard(IReadOnlyList<Card> hole, IReadOnlyList<Card> board)
    {
        Rank? best = null;

        foreach (var card in hole)
        {
            if (board.Any(onBoard => onBoard.Rank == card.Rank) && (best is null || card.Rank > best))
            {
                best = card.Rank;
            }
        }

        return best;
    }

    /// <summary>
    /// Four of one suit across the hole cards and the board, with a hole card of
    /// that suit worth having -- a ten or better.
    /// </summary>
    private static bool FourToAFlush(IReadOnlyList<Card> hole, IReadOnlyList<Card> board) =>
        Enum.GetValues<Suit>().Any(suit =>
            hole.Count(card => card.Suit == suit) + board.Count(card => card.Suit == suit) >= 4
            && hole.Any(card => card.Suit == suit && card.Rank >= Rank.Ten));
}
