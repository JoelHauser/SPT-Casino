namespace Poker.Game;

/// <summary>
/// One no-limit hold'em cash table: a button, blinds, four streets and a pot.
///
/// The player sits at seat 0 and their decisions arrive as method calls. Every other
/// seat has its own <see cref="IPokerAgent"/> and acts on its own, so the table runs
/// itself between the player's turns.
///
/// The engine owns the entire rule set. The transport above it converts a view to
/// JSON and moves currency; it decides nothing.
///
/// **The betting round is the bug-dense part of this game, not settlement.** Who acts
/// next, when a round closes, what counts as a legal raise and what an all-in
/// reopens -- those are where hold'em implementations go wrong. Settlement is
/// comparatively easy because <see cref="PotBuilder"/> already does it, side pots and
/// uncalled bets included.
/// </summary>
public sealed class HoldemTable
{
    private readonly HoldemRules _rules;
    private readonly Deck _deck;
    private readonly IGameLog _log;
    private readonly List<HoldemSeat> _seats = [];
    private readonly Dictionary<int, IPokerAgent> _agents = [];
    private readonly List<Card> _community = [];

    private int _button = -1;
    private int _actor = -1;
    private int _revealed;

    /// <summary>What a seat must have in for this street to still be in the hand.</summary>
    private int _currentBet;

    /// <summary>
    /// The size of the last full raise, which is the minimum for the next one. Without
    /// it a player could grind a round out in one-chip increments and never let it
    /// close.
    /// </summary>
    private int _lastRaiseSize;

    public HoldemTable(
        HoldemRules? rules = null,
        int seats = 2,
        Random? rng = null,
        IGameLog? log = null,
        IReadOnlyList<IPokerAgent>? agents = null,
        IReadOnlyList<string>? names = null)
        : this(rules ?? new HoldemRules(), new Deck(rng, log), seats, log, agents, names)
    {
    }

    /// <summary>Test seam: run the table against a stacked deck.</summary>
    /// <param name="names">
    /// What to call the bots, in seat order. Supplied rather than invented because the
    /// engine has no business knowing where a good name comes from -- the mod pulls
    /// them from the game's own PMC list. Short or absent, the remaining seats fall
    /// back to their number.
    /// </param>
    public HoldemTable(
        HoldemRules rules,
        Deck deck,
        int seats = 2,
        IGameLog? log = null,
        IReadOnlyList<IPokerAgent>? agents = null,
        IReadOnlyList<string>? names = null)
    {
        _rules = rules;
        _deck = deck;
        _log = log ?? GameLog.Null;

        if (seats < 2 || seats > rules.MaxSeats)
        {
            throw new ArgumentOutOfRangeException(
                nameof(seats), seats, $"A hold'em table seats 2 to {rules.MaxSeats}. One player is not a game.");
        }

        // One agent per seat, never one for the table. The parked UTH table took a
        // single agent for every seat, which made its four seat-mates one person
        // wearing four names -- and personality is the entire point of these.
        if (agents is not null && agents.Count != seats - 1)
        {
            throw new ArgumentException(
                $"A {seats}-seat table needs {seats - 1} agents, one per bot; got {agents.Count}.",
                nameof(agents));
        }

        for (var index = 0; index < seats; index++)
        {
            var isPlayer = index == PlayerSeatIndex;
            var botNumber = index - 1;

            var name = isPlayer
                ? "You"
                : names is not null && botNumber < names.Count && !string.IsNullOrWhiteSpace(names[botNumber])
                    ? names[botNumber]
                    : $"Seat {index}";

            _seats.Add(new HoldemSeat(index, isPlayer, name, rules.BuyIn));

            if (!isPlayer && agents is not null)
            {
                _agents[index] = agents[index - 1];
            }
        }
    }

    /// <summary>
    /// Where the person at the keyboard sits. Fixed, so the deal order does not depend
    /// on a choice made elsewhere; which seat the client *draws* them at is
    /// presentation and does not reach the engine.
    /// </summary>
    public const int PlayerSeatIndex = 0;

    public HoldemRules Rules => _rules;

    public HoldemStreet Street { get; private set; } = HoldemStreet.Idle;

    public IReadOnlyList<HoldemSeat> Seats => _seats;

    public HoldemSeat Player => _seats[PlayerSeatIndex];

    /// <summary>The board, as far as it has been turned over.</summary>
    public IReadOnlyList<Card> Community => _community.Take(_revealed).ToList();

    /// <summary>Everything in the middle, including chips bet on the current street.</summary>
    public int Pot => _seats.Sum(seat => seat.CommittedThisHand);

    public int Button => _button;

    /// <summary>The seat to act, or null when nothing is waiting on a decision.</summary>
    public HoldemSeat? Actor =>
        _actor >= 0 && Street is not (HoldemStreet.Idle or HoldemStreet.Showdown) ? _seats[_actor] : null;

    /// <summary>True when the table is waiting on the person at the keyboard.</summary>
    public bool AwaitingPlayer => Actor?.IsPlayer == true;

    /// <summary>Total chips at the table. Constant across a hand -- see the tests.</summary>
    public int ChipsInPlay => _seats.Sum(seat => seat.Stack) + Pot;

    /// <summary>
    /// Deals a hand: moves the button, posts the blinds, deals the hole cards, then
    /// runs the bots until the player has a decision or the hand is over.
    /// </summary>
    public void StartHand()
    {
        if (Street is not (HoldemStreet.Idle or HoldemStreet.Showdown))
        {
            throw new InvalidOperationException("A hand is already in progress.");
        }

        // A seat with no chips cannot post a blind or call one. Busting and being
        // replaced is a table-management question and does not belong in the middle
        // of a deal.
        var broke = _seats.Where(seat => seat.Stack <= 0).ToList();
        if (broke.Count > 0)
        {
            throw new InvalidOperationException(
                $"{string.Join(", ", broke.Select(s => s.Name))} has no chips. Re-seat before dealing.");
        }

        foreach (var seat in _seats)
        {
            seat.ClearForNewHand();
            seat.StackAtHandStart = seat.Stack;
        }

        _community.Clear();
        _revealed = 0;
        _button = _button < 0 ? 0 : SeatAfter(_button);
        _deck.Shuffle();

        Street = HoldemStreet.PreFlop;
        PostBlinds();
        DealHoleCards();

        _actor = NextWhoCanAct(FirstToActPreFlop);

        if (_log.Enabled)
        {
            _log.Write(
                $"hand: button on {_seats[_button].Name}, blinds {_rules.SmallBlind}/{_rules.BigBlind}, "
                + $"{_seats.Count} seats, {ChipsInPlay} chips in play");
            _log.Write($"hand: {Player.Name} holds {string.Join(' ', Player.Cards)}");
        }

        Run();
    }

    /// <summary>
    /// Buys a broke seat back in, optionally with somebody new sitting down in it.
    ///
    /// Kept out of <see cref="StartHand"/> on purpose. Who rebuys, who walks away and
    /// who takes the empty chair is a policy question -- a cash game tops everyone up,
    /// a tournament does not, and the mod will want a stranger to sit down rather than
    /// the same bot to reappear with a fresh stack. The engine refuses to deal to an
    /// empty seat and lets the caller decide what that means.
    ///
    /// **These chips are created.** Nothing else in the engine makes a chip out of
    /// nothing, so anything counting them has to be told.
    /// </summary>
    public void Reseat(int seatIndex, int chips, IPokerAgent? replacement = null, string? name = null)
    {
        if (Street is not (HoldemStreet.Idle or HoldemStreet.Showdown))
        {
            throw new InvalidOperationException("A seat cannot be bought back in the middle of a hand.");
        }

        if (chips <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(chips), chips, "A seat needs chips to play.");
        }

        var seat = _seats[seatIndex];

        if (seat.Stack > 0)
        {
            throw new InvalidOperationException($"{seat.Name} still has {seat.Stack} and is not out.");
        }

        if (replacement is not null && seat.IsPlayer)
        {
            throw new InvalidOperationException("The player's seat cannot be handed to an agent.");
        }

        var replaced = _seats[seatIndex] = new HoldemSeat(seatIndex, seat.IsPlayer, name ?? seat.Name, chips);

        if (replacement is not null)
        {
            _agents[seatIndex] = replacement;
        }

        if (_log.Enabled)
        {
            _log.Write(
                $"table: {replaced.Name} sits down with {chips}"
                + (replacement is not null ? " (a new face)" : " (bought back in)"));
        }
    }

    /// <summary>What the seat to act may legally do, and for how much.</summary>
    public BettingOptions Options()
    {
        var seat = Actor ?? throw new InvalidOperationException($"Nothing to decide while the table is {Street}.");

        return OptionsFor(seat);
    }

    /// <summary>
    /// The seat-to-act's own view of the table -- exactly what a bot in that seat
    /// would be handed.
    ///
    /// Safe to expose: the context carries only that seat's cards and what anybody at
    /// the table can see. It exists so the console can drive the player's seat with
    /// an agent, and it is what a hint feature would read.
    /// </summary>
    public PokerContext ContextForActor()
    {
        var seat = Actor ?? throw new InvalidOperationException($"Nothing to decide while the table is {Street}.");

        return ContextFor(seat);
    }

    private PokerContext ContextFor(HoldemSeat seat) => new(
        seat,
        Street,
        Community,
        OptionsFor(seat),
        Pot,
        _seats.Where(other => other.Index != seat.Index)
            .Select(other => new OpponentView(
                other.Index, other.Name, other.Stack, other.CommittedThisStreet, other.Folded, other.IsAllIn))
            .ToList(),
        SeatsToActAfter(seat),
        _rules);

    /// <summary>The player's decision. Bots then act until it is the player's turn again.</summary>
    public void Act(HoldemDecision decision)
    {
        var seat = Actor ?? throw new InvalidOperationException($"Nothing to decide while the table is {Street}.");

        if (!seat.IsPlayer)
        {
            throw new InvalidOperationException($"It is {seat.Name}'s turn, not yours.");
        }

        Apply(seat, decision, OptionsFor(seat));
        AdvanceActor();
        Run();
    }

    private int FirstToActPreFlop
    {
        get
        {
            // Heads-up is the exception that catches everybody: the button posts the
            // small blind and acts first before the flop, then acts last on every
            // street after it. With three or more, the button is last before the flop
            // and the seat left of the big blind opens.
            var (smallBlind, bigBlind) = BlindSeats;
            return _seats.Count == 2 ? smallBlind : SeatAfter(bigBlind);
        }
    }

    /// <summary>After the flop the small blind opens, and heads-up that is the big blind.</summary>
    private int FirstToActAfterFlop => _seats.Count == 2 ? BlindSeats.BigBlind : BlindSeats.SmallBlind;

    private (int SmallBlind, int BigBlind) BlindSeats =>
        _seats.Count == 2
            ? (_button, SeatAfter(_button))
            : (SeatAfter(_button), SeatAfter(SeatAfter(_button)));

    private void PostBlinds()
    {
        var (smallBlind, bigBlind) = BlindSeats;

        _seats[smallBlind].Commit(_rules.SmallBlind);
        _seats[bigBlind].Commit(_rules.BigBlind);

        // Read the bet off the table rather than from the rules, so a blind posted
        // short by an all-in stack does not leave the round chasing a number nobody
        // actually put out.
        _currentBet = _seats.Max(seat => seat.CommittedThisStreet);
        _lastRaiseSize = _rules.BigBlind;

        // Posting is not acting. That is exactly what leaves the big blind an option
        // to raise when the table has only called round to them.
        foreach (var seat in _seats)
        {
            seat.HasActed = false;
        }
    }

    /// <summary>
    /// One card at a time round the table starting left of the button, twice.
    ///
    /// Fixed and written down because a stacked-deck test is pinned to a seat count as
    /// well as to an order: change either and every pinned deal breaks at once, in a
    /// way that reads as a rules bug rather than as a changed deal.
    /// </summary>
    private void DealHoleCards()
    {
        for (var pass = 0; pass < 2; pass++)
        {
            for (var offset = 1; offset <= _seats.Count; offset++)
            {
                _seats[(_button + offset) % _seats.Count].Add(_deck.Draw());
            }
        }

        for (var card = 0; card < 5; card++)
        {
            _community.Add(_deck.Draw());
        }
    }

    private BettingOptions OptionsFor(HoldemSeat seat)
    {
        var toCall = Math.Max(0, _currentBet - seat.CommittedThisStreet);
        var maxTo = seat.CommittedThisStreet + seat.Stack;

        // The smallest legal raise, capped at everything the seat has: going all-in
        // is always allowed even when it falls short of a full raise.
        var minTo = Math.Min(_currentBet + _lastRaiseSize, maxTo);

        var moves = new List<HoldemMove> { HoldemMove.Fold };
        moves.Add(toCall == 0 ? HoldemMove.Check : HoldemMove.Call);

        // A seat can only raise if it has chips beyond the call, and if the action is
        // still open to it -- an all-in too small to be a full raise closes it.
        if (seat.MayRaise && maxTo > _currentBet)
        {
            moves.Add(HoldemMove.Raise);
        }

        return new BettingOptions(moves, Math.Min(toCall, seat.Stack), minTo, maxTo);
    }

    private void Apply(HoldemSeat seat, HoldemDecision decision, BettingOptions options)
    {
        if (!options.Moves.Contains(decision.Move))
        {
            throw new InvalidOperationException(
                $"{decision.Move} is not legal for {seat.Name}; {string.Join(", ", options.Moves)} are.");
        }

        switch (decision.Move)
        {
            case HoldemMove.Fold:
                seat.Folded = true;
                break;

            case HoldemMove.Check:
                break;

            case HoldemMove.Call:
                seat.Commit(options.ToCall);
                break;

            case HoldemMove.Raise:
                if (decision.To < options.MinRaiseTo || decision.To > options.MaxRaiseTo)
                {
                    throw new ArgumentOutOfRangeException(
                        nameof(decision),
                        decision.To,
                        $"A raise here is between {options.MinRaiseTo} and {options.MaxRaiseTo}.");
                }

                seat.Commit(decision.To - seat.CommittedThisStreet);
                Reopen(seat);
                break;
        }

        seat.HasActed = true;

        if (_log.Enabled)
        {
            _log.Write(
                $"  {seat.Name} {decision} -- in for {seat.CommittedThisStreet} this street, "
                + $"{seat.Stack} behind, pot {Pot}");
        }
    }

    /// <summary>
    /// Works out what a raise did to everybody else's right to act.
    ///
    /// A full raise reopens the betting for every seat still in, including those that
    /// had already acted. An all-in **too small to be a full raise** does not: seats
    /// that have already acted owe the difference and may call it or fold, but they
    /// do not get to raise again. Missing that distinction is how a short all-in
    /// turns into an unlimited raising war between two other players.
    /// </summary>
    private void Reopen(HoldemSeat raiser)
    {
        var raiseSize = raiser.CommittedThisStreet - _currentBet;
        var full = raiseSize >= _lastRaiseSize;

        _currentBet = raiser.CommittedThisStreet;

        if (full)
        {
            _lastRaiseSize = raiseSize;
        }

        foreach (var seat in _seats.Where(other => other.InHand && other.Index != raiser.Index))
        {
            if (full)
            {
                seat.HasActed = false;
                seat.MayRaise = true;
            }
            else if (seat.HasActed)
            {
                seat.MayRaise = false;
            }
        }

        if (_log.Enabled && !full)
        {
            _log.Write(
                $"  ...{raiser.Name} is all-in for {raiseSize} over the bet, short of a full raise "
                + $"({_lastRaiseSize}), so the action does not reopen");
        }
    }

    /// <summary>
    /// Runs the table until the player has something to decide or the hand is over.
    /// </summary>
    private void Run()
    {
        while (true)
        {
            if (_seats.Count(seat => seat.InHand) <= 1)
            {
                Finish();
                return;
            }

            if (RoundClosed())
            {
                if (!NextStreet())
                {
                    Finish();
                    return;
                }

                continue;
            }

            if (_actor < 0 || !_seats[_actor].CanAct)
            {
                AdvanceActor();
                continue;
            }

            var seat = _seats[_actor];
            if (seat.IsPlayer)
            {
                return;
            }

            var context = ContextFor(seat);
            var decision = _agents[seat.Index].Decide(context);

            Apply(seat, Legalise(seat, decision, context.Options), context.Options);
            AdvanceActor();
        }
    }

    /// <summary>
    /// Forces an agent's answer into something legal.
    ///
    /// A bot with a bug must not be able to end a hand the player has chips in, so an
    /// illegal decision is corrected rather than thrown -- but loudly, because the
    /// correction hides the fault otherwise, and a bot that folds every hand reads as
    /// a personality rather than a defect.
    /// </summary>
    private HoldemDecision Legalise(HoldemSeat seat, HoldemDecision decision, BettingOptions options)
    {
        var corrected = decision;

        if (!options.Moves.Contains(decision.Move))
        {
            corrected = options.Moves.Contains(HoldemMove.Check) ? HoldemDecision.Check : HoldemDecision.Fold;
        }
        else if (decision.Move == HoldemMove.Raise)
        {
            var to = Math.Clamp(decision.To, options.MinRaiseTo, options.MaxRaiseTo);
            corrected = HoldemDecision.RaiseTo(to);
        }

        if (corrected != decision && _log.Enabled)
        {
            _log.Write($"  {seat.Name} returned an illegal {decision} -- taken as {corrected}");
        }

        return corrected;
    }

    private bool RoundClosed()
    {
        var actors = _seats.Where(seat => seat.CanAct).ToList();

        if (actors.Count == 0)
        {
            return true;
        }

        // With one seat left able to act, there is nobody to bet against. It only has
        // to match what is already out there -- or fold, which it gets the chance to
        // do while it still owes chips.
        if (actors.Count == 1)
        {
            return actors[0].CommittedThisStreet >= _currentBet;
        }

        return actors.All(seat => seat.HasActed && seat.CommittedThisStreet == _currentBet);
    }

    private bool NextStreet()
    {
        if (Street == HoldemStreet.River)
        {
            return false;
        }

        foreach (var seat in _seats)
        {
            seat.ClearForNewStreet();
        }

        _currentBet = 0;
        _lastRaiseSize = _rules.BigBlind;

        Street = Street switch
        {
            HoldemStreet.PreFlop => HoldemStreet.Flop,
            HoldemStreet.Flop => HoldemStreet.Turn,
            _ => HoldemStreet.River,
        };

        _revealed = Street switch
        {
            HoldemStreet.Flop => 3,
            HoldemStreet.Turn => 4,
            _ => 5,
        };

        _actor = NextWhoCanAct(FirstToActAfterFlop);

        if (_log.Enabled)
        {
            _log.Write($"{Street}: {string.Join(' ', Community)} -- pot {Pot}");
        }

        return true;
    }

    private void Finish()
    {
        Street = HoldemStreet.Showdown;
        _actor = -1;

        var live = _seats.Where(seat => seat.InHand).ToList();

        // Hands are only read when there is something to compare. A pot everybody
        // folded out of is won without showing, and on a hand that ended early the
        // board is not even complete.
        if (live.Count > 1)
        {
            _revealed = 5;

            foreach (var seat in live)
            {
                seat.Hand = HandEvaluator.Best([.. seat.Cards, .. _community], _log).Rank;
            }
        }

        AwardPots();
    }

    private void AwardPots()
    {
        var layout = PotBuilder.Build(
            _seats.Select(seat => new Contribution(seat.Index, seat.CommittedThisHand, seat.Folded)).ToList(),
            _log);

        foreach (var (seat, amount) in layout.Refunds)
        {
            _seats[seat].Stack += amount;

            if (_log.Enabled)
            {
                _log.Write($"  {_seats[seat].Name} takes back {amount} nobody called");
            }
        }

        foreach (var pot in layout.Pots)
        {
            var contenders = pot.EligibleSeats.Select(index => _seats[index]).ToList();

            var winners = contenders.Count == 1
                ? contenders
                : contenders
                    .GroupBy(seat => seat.Hand!.Value)
                    .OrderByDescending(group => group.Key)
                    .First()
                    .ToList();

            Share(pot.Amount, winners);
        }

        // Told before the commitments are cleared, so a seat can see what the hand
        // actually cost it. Every seat hears about it, including the ones that folded
        // on the first street -- giving a hand up is a result too, and a seat that
        // only heard about showdowns would never notice it was being run over.
        foreach (var seat in _seats)
        {
            if (_agents.TryGetValue(seat.Index, out var agent))
            {
                agent.HandEnded(new HandOutcome(seat.Net, seat.Stack, _rules.BuyIn, seat.Folded));
            }
        }

        foreach (var seat in _seats)
        {
            seat.CommittedThisStreet = 0;
            seat.CommittedThisHand = 0;
        }

        if (_log.Enabled)
        {
            foreach (var seat in _seats.Where(seat => seat.Won > 0))
            {
                _log.Write($"  {seat.Name} wins {seat.Won}" + (seat.Hand is { } hand ? $" with {hand.Describe()}" : string.Empty));
            }
        }
    }

    /// <summary>
    /// Splits a pot, and places the chips that do not divide.
    ///
    /// An odd chip goes to the first winner left of the button, which is the house
    /// rule everywhere and, more importantly, is *a* rule -- dropping the remainder
    /// would quietly destroy chips, and a table that loses one chip a hand is a table
    /// whose books stop balancing by the end of a session.
    /// </summary>
    private void Share(int amount, IReadOnlyList<HoldemSeat> winners)
    {
        var each = amount / winners.Count;
        var remainder = amount % winners.Count;

        foreach (var seat in winners)
        {
            seat.Stack += each;
            seat.Won += each;
        }

        var ordered = winners
            .OrderBy(seat => (seat.Index - _button - 1 + _seats.Count) % _seats.Count)
            .ToList();

        for (var i = 0; i < remainder; i++)
        {
            ordered[i].Stack++;
            ordered[i].Won++;
        }
    }

    /// <summary>
    /// How many seats still able to act come after this one on this street.
    ///
    /// Counted from the seat that opens the street rather than from the button,
    /// because that is what position actually amounts to once folds are in: a seat
    /// nominally in early position is last to speak if everyone between has passed.
    /// </summary>
    private int SeatsToActAfter(HoldemSeat seat)
    {
        var opener = Street == HoldemStreet.PreFlop ? FirstToActPreFlop : FirstToActAfterFlop;

        var order = Enumerable.Range(0, _seats.Count)
            .Select(offset => _seats[(opener + offset) % _seats.Count])
            .Where(other => other.CanAct)
            .ToList();

        var place = order.FindIndex(other => other.Index == seat.Index);

        return place < 0 ? 0 : order.Count - 1 - place;
    }

    private void AdvanceActor() => _actor = NextWhoCanAct(SeatAfter(_actor));

    private int SeatAfter(int index) => (index + 1) % _seats.Count;

    /// <summary>The first seat from here that can make a decision, or -1 if none can.</summary>
    private int NextWhoCanAct(int from)
    {
        for (var offset = 0; offset < _seats.Count; offset++)
        {
            var index = (from + offset) % _seats.Count;
            if (_seats[index].CanAct)
            {
                return index;
            }
        }

        return -1;
    }
}
