namespace Poker.Game;

/// <summary>
/// How often this hand wins, estimated by dealing the rest of the deck out at random
/// a few hundred times.
///
/// This is the number every other judgement hangs off. A poker decision is a
/// comparison between what a call costs and how often the hand wins, and without a
/// believable equity figure a bot can only follow a chart -- which is exactly what
/// makes chart-following bots feel like furniture.
///
/// Monte Carlo rather than a lookup table on purpose. A table would be faster and
/// would have to be built for a fixed number of opponents, whereas this handles two
/// seats or five, any board, at any street, in about the same code. The expensive
/// half already exists: <see cref="HandEvaluator"/> is exhaustively verified, and
/// the only thing this adds is dealing.
///
/// Opponents are drawn uniformly from the unseen cards, which is the honest thing to
/// do and also the humble one: it assumes nothing about what an opponent would have
/// bet with. Modelling ranges would make the bots sharper and is a much later
/// problem.
/// </summary>
public static class HandEquity
{
    /// <summary>
    /// Enough samples to be steady to about a point, which is finer than any decision
    /// here needs. The error falls with the square root of this, so doubling it buys
    /// very little and costs twice as much.
    /// </summary>
    public const int DefaultSamples = 240;

    /// <summary>
    /// The share of the pot this hand expects to win: 1.0 is certain, 0.5 is a coin
    /// flip. Ties count as half, because that is what splitting a pot is.
    /// </summary>
    public static double Estimate(
        IReadOnlyList<Card> hole,
        IReadOnlyList<Card> board,
        int opponents,
        Random rng,
        int samples = DefaultSamples,
        IGameLog? log = null)
    {
        if (opponents < 1)
        {
            return 1.0;
        }

        var seen = new HashSet<Card>(hole);
        seen.UnionWith(board);

        var unseen = new List<Card>(52);
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

        var deck = unseen.ToArray();
        var needed = ((5 - board.Count) + (opponents * 2));

        if (needed > deck.Length)
        {
            throw new ArgumentOutOfRangeException(
                nameof(opponents), opponents, "More cards are needed than the deck has left.");
        }

        // Reused across samples so a few hundred rollouts do not allocate a few
        // hundred arrays. This runs on every bot decision on every street.
        var full = new Card[5];
        var mine = new Card[7];
        var theirs = new Card[7];

        for (var i = 0; i < board.Count; i++)
        {
            full[i] = board[i];
        }

        var score = 0.0;

        for (var sample = 0; sample < samples; sample++)
        {
            // Partial Fisher-Yates: shuffle only the cards this rollout needs, and
            // leave the rest of the array alone. Shuffling all 45 to look at 9 of
            // them is most of the cost of the estimate.
            for (var i = 0; i < needed; i++)
            {
                var j = rng.Next(i, deck.Length);
                (deck[i], deck[j]) = (deck[j], deck[i]);
            }

            var next = 0;
            for (var i = board.Count; i < 5; i++)
            {
                full[i] = deck[next++];
            }

            mine[0] = hole[0];
            mine[1] = hole[1];
            full.CopyTo(mine, 2);

            var ours = HandEvaluator.Evaluate(mine);
            var best = default(HandRank);
            var first = true;

            for (var opponent = 0; opponent < opponents; opponent++)
            {
                theirs[0] = deck[next++];
                theirs[1] = deck[next++];
                full.CopyTo(theirs, 2);

                var rank = HandEvaluator.Evaluate(theirs);
                if (first || rank > best)
                {
                    best = rank;
                    first = false;
                }
            }

            score += ours > best ? 1.0 : ours < best ? 0.0 : 0.5;
        }

        var equity = score / samples;

        if (log?.Enabled == true)
        {
            log.Write(
                $"    equity {equity:P0} for {string.Join(' ', hole)} on "
                + $"[{string.Join(' ', board)}] against {opponents} ({samples} rollouts)");
        }

        return equity;
    }
}
