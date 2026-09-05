namespace Blackjack.Game;

/// <summary>
/// One seat at one table: the shoe, the dealer, and the player hands. Owns the
/// entire rule set, so the transport layer above it decides nothing about the
/// game -- it only converts <see cref="RoundView"/> to JSON and moves roubles.
/// </summary>
public sealed class BlackjackTable
{
    private readonly Rules _rules;
    private readonly Shoe _shoe;
    private readonly List<Hand> _hands = [];
    private Hand _dealer = new(0);
    private int _splitsUsed;
    private double _roundBlackjackPayout;

    public BlackjackTable(Rules? rules = null, Random? rng = null)
    {
        _rules = rules ?? new Rules();
        _shoe = new Shoe(_rules.DeckCount, rng);
    }

    /// <summary>Test seam: run the table against a stacked shoe.</summary>
    public BlackjackTable(Rules rules, Shoe shoe)
    {
        _rules = rules;
        _shoe = shoe;
    }

    public Rules Rules => _rules;

    public RoundPhase Phase { get; private set; } = RoundPhase.AwaitingBet;

    public int ActiveHandIndex { get; private set; }

    /// <summary>
    /// Total currently at risk. The caller debits the difference between this
    /// before and after an action, which is how doubling and splitting collect
    /// their extra stake without the engine knowing what a rouble is.
    /// </summary>
    public int TotalWagered => _hands.Sum(hand => hand.Wager);

    public int TotalReturned => _hands.Sum(hand => hand.Returned);

    /// <summary>
    /// Deals a round. <paramref name="blackjackPayout"/> overrides the table default
    /// for this round only -- the caller varies it by what is being staked, and one
    /// shoe serves every currency, so it cannot live on the table.
    /// </summary>
    public RoundView Deal(int wager, double? blackjackPayout = null)
    {
        if (Phase is not (RoundPhase.AwaitingBet or RoundPhase.Settled))
        {
            throw new InvalidOperationException("A round is already in progress.");
        }

        if (wager < _rules.MinBet || wager > _rules.MaxBet)
        {
            throw new ArgumentOutOfRangeException(
                nameof(wager),
                wager,
                $"Wager must be between {_rules.MinBet} and {_rules.MaxBet}.");
        }

        _hands.Clear();
        _dealer = new Hand(0);
        _splitsUsed = 0;
        ActiveHandIndex = 0;
        _roundBlackjackPayout = blackjackPayout ?? _rules.BlackjackPayout;

        // Only ever reshuffle between rounds. Doing it mid-hand would change the
        // composition of a shoe the player has already seen cards from.
        if (_shoe.NeedsShuffle(_rules.ShufflePenetration))
        {
            _shoe.Shuffle();
        }

        var hand = new Hand(wager);
        _hands.Add(hand);
        Phase = RoundPhase.PlayerTurn;

        hand.Add(_shoe.Draw());
        _dealer.Add(_shoe.Draw());
        hand.Add(_shoe.Draw());
        _dealer.Add(_shoe.Draw());

        // The dealer peeks on a ten or ace showing. Resolving here means a player
        // never doubles or splits into a hand that was already lost.
        if (_dealer.IsBlackjack)
        {
            hand.Status = hand.IsBlackjack ? HandStatus.Blackjack : HandStatus.Stood;
            FinishRound();
            return View();
        }

        if (hand.IsBlackjack)
        {
            hand.Status = HandStatus.Blackjack;
            FinishRound();
        }

        return View();
    }

    public RoundView Hit()
    {
        var hand = RequireActionable(PlayerAction.Hit);
        hand.Add(_shoe.Draw());

        // Stand on 21 automatically -- hitting it is never correct, and leaving the
        // hand active invites a client to send a drawing action that busts it.
        if (hand.Status == HandStatus.Active && hand.Value == 21)
        {
            hand.Status = HandStatus.Stood;
        }

        AdvanceHand();
        return View();
    }

    public RoundView Stand()
    {
        var hand = RequireActionable(PlayerAction.Stand);
        hand.Status = HandStatus.Stood;
        AdvanceHand();
        return View();
    }

    public RoundView Double()
    {
        var hand = RequireActionable(PlayerAction.Double);
        hand.DoubleWager();
        hand.Add(_shoe.Draw());

        // Add() flips the status to Bust on its own; only a surviving hand stands.
        if (hand.Status == HandStatus.Active)
        {
            hand.Status = HandStatus.Doubled;
        }

        AdvanceHand();
        return View();
    }

    public RoundView Split()
    {
        var hand = RequireActionable(PlayerAction.Split);

        var moved = hand.RemoveSecondCard();
        var splitAces = moved.IsAce;

        var newHand = new Hand(hand.Wager, fromSplit: true);
        newHand.Add(moved);

        // The original hand is a split hand too now -- without this, a ten landing
        // on it would score as a natural and pay 3:2.
        hand.IsFromSplit = true;
        _hands.Insert(ActiveHandIndex + 1, newHand);
        _splitsUsed++;

        hand.Add(_shoe.Draw());
        newHand.Add(_shoe.Draw());

        if (splitAces && _rules.OneCardAfterAceSplit)
        {
            StandUnlessResplittable(hand);
            StandUnlessResplittable(newHand);
        }

        AdvanceHand();
        return View();
    }

    public IReadOnlyList<PlayerAction> AvailableActions()
    {
        if (Phase != RoundPhase.PlayerTurn || ActiveHandIndex >= _hands.Count)
        {
            return [];
        }

        var hand = _hands[ActiveHandIndex];
        if (hand.Status != HandStatus.Active)
        {
            return [];
        }

        var actions = new List<PlayerAction> { PlayerAction.Stand };

        if (hand.Value < 21)
        {
            actions.Add(PlayerAction.Hit);
        }

        // Doubling and splitting are first-decision-only; both need a pristine
        // two-card hand.
        if (hand.Cards.Count == 2 && (!hand.IsFromSplit || _rules.DoubleAfterSplit))
        {
            actions.Add(PlayerAction.Double);
        }

        if (CanSplitHand(hand))
        {
            actions.Add(PlayerAction.Split);
        }

        return actions;
    }

    public RoundView View()
    {
        var revealDealer = Phase is RoundPhase.DealerTurn or RoundPhase.Settled;

        return new RoundView(
            Phase,
            _hands.Select(ToView).ToList(),
            DealerView(revealDealer),
            ActiveHandIndex,
            AvailableActions(),
            TotalWagered,
            TotalReturned,
            _shoe.Remaining);
    }

    private bool CanSplitHand(Hand hand)
    {
        if (!hand.CanSplit || _splitsUsed >= _rules.MaxSplits)
        {
            return false;
        }

        // Re-splitting aces is a separate permission from splitting them once.
        return !hand.Cards[0].IsAce || !hand.IsFromSplit || _rules.AllowResplitAces;
    }

    private void StandUnlessResplittable(Hand hand)
    {
        if (!CanSplitHand(hand))
        {
            hand.Status = HandStatus.Stood;
        }
    }

    private Hand RequireActionable(PlayerAction action)
    {
        if (Phase != RoundPhase.PlayerTurn)
        {
            throw new InvalidOperationException($"Cannot {action} while the round is {Phase}.");
        }

        if (!AvailableActions().Contains(action))
        {
            throw new InvalidOperationException($"{action} is not legal on the current hand.");
        }

        return _hands[ActiveHandIndex];
    }

    private void AdvanceHand()
    {
        while (ActiveHandIndex < _hands.Count && _hands[ActiveHandIndex].Status != HandStatus.Active)
        {
            ActiveHandIndex++;
        }

        if (ActiveHandIndex >= _hands.Count)
        {
            FinishRound();
        }
    }

    private void FinishRound()
    {
        Phase = RoundPhase.DealerTurn;

        // The dealer only draws when a live hand can still be beaten. If every hand
        // busted or won outright with a natural, the house has nothing to play for.
        var anyLive = _hands.Any(hand => hand.Status is HandStatus.Stood or HandStatus.Doubled);
        if (anyLive && !_dealer.IsBlackjack)
        {
            PlayDealer();
        }

        Settle();
        Phase = RoundPhase.Settled;
    }

    private void PlayDealer()
    {
        while (true)
        {
            var value = _dealer.Value;
            if (value < 17 || (value == 17 && _dealer.IsSoft && _rules.DealerHitsSoft17))
            {
                _dealer.Add(_shoe.Draw());
                continue;
            }

            break;
        }

        _dealer.Status = _dealer.IsBust ? HandStatus.Bust : HandStatus.Stood;
    }

    private void Settle()
    {
        var dealerNatural = _dealer.IsBlackjack;
        var dealerValue = _dealer.Value;

        foreach (var hand in _hands)
        {
            if (hand.Status == HandStatus.Bust)
            {
                hand.Outcome = HandOutcome.Bust;
                hand.Returned = 0;
                continue;
            }

            if (dealerNatural)
            {
                // A natural beats a non-natural 21, so this cannot fall through to
                // the numeric comparison below -- that would score it a push.
                hand.Outcome = hand.Status == HandStatus.Blackjack ? HandOutcome.Push : HandOutcome.Lose;
                hand.Returned = hand.Outcome == HandOutcome.Push ? hand.Wager : 0;
                continue;
            }

            if (hand.Status == HandStatus.Blackjack)
            {
                hand.Outcome = HandOutcome.Blackjack;
                hand.Returned = hand.Wager
                    + (int)Math.Round(hand.Wager * _roundBlackjackPayout, MidpointRounding.AwayFromZero);
                continue;
            }

            if (_dealer.IsBust || hand.Value > dealerValue)
            {
                hand.Outcome = HandOutcome.Win;
                hand.Returned = hand.Wager * 2;
            }
            else if (hand.Value == dealerValue)
            {
                hand.Outcome = HandOutcome.Push;
                hand.Returned = hand.Wager;
            }
            else
            {
                hand.Outcome = HandOutcome.Lose;
                hand.Returned = 0;
            }
        }
    }

    private static HandView ToView(Hand hand) => new(
        hand.Cards.Select(card => card.Code).ToList(),
        hand.Value,
        hand.IsSoft,
        hand.Wager,
        hand.Status,
        hand.Outcome,
        hand.Returned);

    private HandView DealerView(bool reveal)
    {
        // Before the first deal there is no dealer hand to describe. The client asks
        // for state the moment the panel opens, so this path is hit on every visit.
        if (reveal || _dealer.Cards.Count == 0)
        {
            return ToView(_dealer);
        }

        // The hole card is omitted from the payload entirely rather than blanked
        // out. Anything sent to the client is knowable by the client.
        var upcard = _dealer.Cards[0];
        return new HandView(
            [upcard.Code],
            upcard.IsAce ? 11 : upcard.BaseValue,
            upcard.IsAce,
            0,
            HandStatus.Active,
            HandOutcome.Pending,
            0);
    }
}
