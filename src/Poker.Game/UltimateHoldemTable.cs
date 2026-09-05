namespace Poker.Game;

/// <summary>
/// One Ultimate Texas Hold'em table: the deck, the dealer, the community cards and
/// up to <see cref="Rules.MaxSeats"/> seats.
///
/// House-banked, which is the whole reason this variant was chosen. Every seat plays
/// its own hand against the dealer and no seat can win or lose another's money, so
/// there is no pot to split and no opponent AI standing between the mod and a
/// working game. The seat-mates are company, not competition.
///
/// The engine owns the entire rule set. The transport above it decides nothing about
/// the game -- it converts <see cref="TableView"/> to JSON and moves currency.
/// </summary>
public sealed class UltimateHoldemTable
{
    private readonly Rules _rules;
    private readonly Deck _deck;
    private readonly IGameLog _log;
    private readonly ISeatAgent? _agent;
    private readonly List<Seat> _seats = [];
    private readonly List<Card> _community = [];
    private readonly List<Card> _dealer = [];

    private int _revealed;
    private HandRank? _dealerHand;
    private bool _dealerQualified;

    public UltimateHoldemTable(
        Rules? rules = null,
        int seats = 1,
        Random? rng = null,
        IGameLog? log = null,
        ISeatAgent? agent = null)
        : this(rules ?? new Rules(), new Deck(rng, log), seats, log, agent)
    {
    }

    /// <summary>Test seam: run the table against a stacked deck.</summary>
    public UltimateHoldemTable(
        Rules rules,
        Deck deck,
        int seats = 1,
        IGameLog? log = null,
        ISeatAgent? agent = null)
    {
        _rules = rules;
        _deck = deck;
        _log = log ?? GameLog.Null;
        _agent = agent;

        if (seats < 1 || seats > rules.MaxSeats)
        {
            throw new ArgumentOutOfRangeException(
                nameof(seats), seats, $"A table seats 1 to {rules.MaxSeats}, the player included.");
        }

        // Refused here rather than mid-hand. A seat with nowhere to get a decision
        // from would strand a hand halfway through, with the stake already taken.
        if (seats > 1 && agent is null)
        {
            throw new ArgumentNullException(
                nameof(agent), "Seat-mates need an agent to decide for them.");
        }

        for (var index = 0; index < seats; index++)
        {
            _seats.Add(new Seat(index, index == PlayerSeatIndex, index == PlayerSeatIndex ? "You" : $"Seat {index}"));
        }
    }

    /// <summary>
    /// Where the person at the keyboard sits. Fixed at first base so the deal order
    /// does not depend on a choice made elsewhere -- which seat the client *draws*
    /// them at is a presentation question and does not reach the engine.
    /// </summary>
    public const int PlayerSeatIndex = 0;

    public Rules Rules => _rules;

    public TablePhase Phase { get; private set; } = TablePhase.AwaitingBets;

    public IReadOnlyList<Seat> Seats => _seats;

    public Seat Player => _seats[PlayerSeatIndex];

    /// <summary>
    /// The community cards that are showing. Face-down cards are absent rather than
    /// hidden behind a flag, on the same rule the view follows -- nothing that has
    /// not been turned over can be read from here.
    /// </summary>
    public IReadOnlyList<Card> Community => _community.Take(_revealed).ToList();

    /// <summary>The street the table is waiting on, or null when no decision is due.</summary>
    public Street? CurrentStreet => Phase switch
    {
        TablePhase.PreFlop => Street.PreFlop,
        TablePhase.Flop => Street.Flop,
        TablePhase.River => Street.River,
        _ => null,
    };

    /// <summary>
    /// Deals a hand. The Blind matches the Ante and is not optional -- that pairing
    /// is what the game is built on, so it is taken rather than asked for.
    /// </summary>
    public TableView Deal(int ante, int trips = 0)
    {
        if (Phase is not (TablePhase.AwaitingBets or TablePhase.Settled))
        {
            throw new InvalidOperationException("A hand is already in progress.");
        }

        if (ante < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(ante), ante, "A hand needs an Ante.");
        }

        if (trips < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(trips), trips, "A Trips bet cannot be negative.");
        }

        if (trips > 0 && !_rules.OfferTrips)
        {
            throw new InvalidOperationException("This table does not offer Trips.");
        }

        foreach (var seat in _seats)
        {
            seat.ClearForNewHand();
        }

        _community.Clear();
        _dealer.Clear();
        _revealed = 0;
        _dealerHand = null;
        _dealerQualified = false;

        _deck.Shuffle();

        Player.Ante = ante;
        Player.Blind = ante;
        Player.Trips = trips;

        // Seat-mates play the player's stakes so the table reads coherently -- one
        // game at one level. The numbers are notional either way and never reach a
        // profile, so the only thing at stake in this choice is legibility.
        foreach (var seat in _seats.Where(s => !s.IsPlayer))
        {
            seat.Ante = ante;
            seat.Blind = ante;
        }

        DealCards();

        Phase = TablePhase.PreFlop;

        if (_log.Enabled)
        {
            _log.Write(
                $"table: dealt {_seats.Count} seat(s), ante {ante}, blind {ante}"
                + (trips > 0 ? $", trips {trips}" : string.Empty));
            _log.Write($"table: {Player.Name} holds {string.Join(' ', Player.Cards)}");
        }

        return View();
    }

    /// <summary>
    /// The standard casino rotation: one card at a time round the seats, the dealer
    /// last, twice; then the five community cards off the top.
    ///
    /// Fixed and written down because it has to be. Adding a seat changes which cards
    /// every later position receives, so a test that pins a stacked deck is pinned to
    /// a seat count as well -- and if this order ever moves, every one of those tests
    /// becomes wrong at the same moment, in a way that looks like a rules bug.
    ///
    /// Procedures vary between houses -- some deal the board first, straight off the
    /// shuffler. Nothing about the game depends on which is chosen, only on it never
    /// changing afterwards.
    /// </summary>
    private void DealCards()
    {
        for (var pass = 0; pass < 2; pass++)
        {
            foreach (var seat in _seats)
            {
                seat.Add(_deck.Draw());
            }

            _dealer.Add(_deck.Draw());
        }

        for (var card = 0; card < 5; card++)
        {
            _community.Add(_deck.Draw());
        }
    }

    /// <summary>Passes for now, keeping the Ante alive and the Play cheaper later.</summary>
    public TableView Check() => Act(PlayerAction.Check, 0);

    /// <summary>
    /// Makes the Play bet at the given multiple of the Ante. Legal multiples come
    /// from the street -- 4 or 3, then 2, then 1.
    /// </summary>
    public TableView Play(int multiple) => Act(PlayerAction.Play, multiple);

    /// <summary>Gives up the Ante and the Blind. Only at the river, and Trips survives it.</summary>
    public TableView Fold() => Act(PlayerAction.Fold, 0);

    public IReadOnlyList<PlayerAction> AvailableActions()
    {
        if (!Player.IsActive)
        {
            return [];
        }

        return Phase switch
        {
            TablePhase.PreFlop or TablePhase.Flop => [PlayerAction.Check, PlayerAction.Play],

            // No third check. At the river the hand is either backed or given up,
            // which is what stops a player seeing five cards for the Ante alone.
            TablePhase.River => [PlayerAction.Play, PlayerAction.Fold],
            _ => [],
        };
    }

    /// <summary>What the Play bet costs right now, largest first.</summary>
    public IReadOnlyList<int> AvailablePlayMultiples() => LegalMultiples(CurrentStreet);

    public TableView View()
    {
        var revealAll = Phase == TablePhase.Settled;

        return new TableView(
            Phase,
            _seats.Select(seat => ToView(seat, revealAll)).ToList(),
            DealerToView(revealAll),
            _community.Take(_revealed).Select(card => card.Code).ToList(),
            AvailableActions(),
            AvailablePlayMultiples(),
            Player.Wagered,
            Player.Returned,
            _deck.Remaining);
    }

    private IReadOnlyList<int> LegalMultiples(Street? street) => street switch
    {
        Street.PreFlop => [_rules.PreFlopRaiseLarge, _rules.PreFlopRaiseSmall],
        Street.Flop => [_rules.FlopRaise],
        Street.River => [_rules.RiverRaise],
        _ => [],
    };

    private TableView Act(PlayerAction action, int multiple)
    {
        if (CurrentStreet is null)
        {
            throw new InvalidOperationException($"Cannot {action} while the table is {Phase}.");
        }

        if (!AvailableActions().Contains(action))
        {
            throw new InvalidOperationException($"{action} is not legal on the {Phase}.");
        }

        if (action == PlayerAction.Play && !LegalMultiples(CurrentStreet).Contains(multiple))
        {
            throw new ArgumentOutOfRangeException(
                nameof(multiple),
                multiple,
                $"The Play bet on the {Phase} is "
                + $"{string.Join(" or ", LegalMultiples(CurrentStreet))} times the Ante.");
        }

        Apply(Player, new SeatDecision(
            action switch
            {
                PlayerAction.Check => SeatMove.Check,
                PlayerAction.Play => SeatMove.Play,
                _ => SeatMove.Fold,
            },
            multiple));

        CloseStreet();

        return View();
    }

    /// <summary>
    /// Settles the current street: the seat-mates decide, the next cards come out,
    /// and the hand runs on to showdown once the player has nothing left to say.
    /// </summary>
    private void CloseStreet()
    {
        while (true)
        {
            var street = CurrentStreet;
            if (street is null)
            {
                return;
            }

            DecideSeatMates(street.Value);

            switch (Phase)
            {
                case TablePhase.PreFlop:
                    _revealed = 3;
                    Phase = TablePhase.Flop;
                    break;

                case TablePhase.Flop:
                    _revealed = 5;
                    Phase = TablePhase.River;
                    break;

                default:
                    Settle();
                    return;
            }

            if (_log.Enabled)
            {
                _log.Write($"table: {Phase} -- board is {string.Join(' ', _community.Take(_revealed))}");
            }

            // A player who has bet or folded has no decisions left, but the hand is
            // not over: the board still has to come out and the seat-mates still have
            // to act on it. Run it down rather than waiting for input that will never
            // arrive.
            if (Player.IsActive)
            {
                return;
            }
        }
    }

    private void DecideSeatMates(Street street)
    {
        foreach (var seat in _seats.Where(seat => !seat.IsPlayer && seat.IsActive))
        {
            var context = new SeatContext(seat, street, _community.Take(_revealed).ToList(), LegalMultiples(street), _rules);
            var decision = _agent!.Decide(context);

            Apply(seat, Legalise(seat, decision, street));
        }
    }

    /// <summary>
    /// Forces an agent's answer to be one the street allows.
    ///
    /// A bot with a bug must not be able to end a hand the player has money in, so an
    /// illegal decision is corrected rather than thrown -- but loudly, because the
    /// correction hides the fault otherwise and a bot that folds every river looks
    /// like a personality rather than a defect.
    /// </summary>
    private SeatDecision Legalise(Seat seat, SeatDecision decision, Street street)
    {
        var legal = LegalMultiples(street);

        var corrected = decision switch
        {
            { Move: SeatMove.Play } play when legal.Contains(play.Multiple) => play,
            { Move: SeatMove.Play } => SeatDecision.Play(legal[0]),
            { Move: SeatMove.Fold } when street == Street.River => decision,
            { Move: SeatMove.Fold } => SeatDecision.Check,
            _ when street == Street.River => SeatDecision.Fold,
            _ => SeatDecision.Check,
        };

        if (corrected != decision && _log.Enabled)
        {
            _log.Write($"table: {seat.Name} returned an illegal {decision} on the {street} -- taken as {corrected}");
        }

        return corrected;
    }

    private void Apply(Seat seat, SeatDecision decision)
    {
        switch (decision.Move)
        {
            case SeatMove.Play:
                seat.Play = seat.Ante * decision.Multiple;
                break;

            case SeatMove.Fold:
                seat.Folded = true;
                break;
        }

        if (_log.Enabled)
        {
            _log.Write(
                $"table: {seat.Name} {decision}"
                + (decision.Move == SeatMove.Play ? $" for {seat.Play}" : string.Empty));
        }
    }

    private void Settle()
    {
        var board = _community;

        _dealerHand = HandEvaluator.Best([.. _dealer, .. board], _log).Rank;
        _dealerQualified = _dealerHand.Value.Category >= _rules.DealerQualifies;

        if (_log.Enabled)
        {
            _log.Write(
                $"dealer: {string.Join(' ', _dealer)} -- {_dealerHand.Value.Describe()}, "
                + (_dealerQualified ? "qualifies" : "does not qualify"));
        }

        foreach (var seat in _seats)
        {
            SettleSeat(seat);
        }

        Phase = TablePhase.Settled;
    }

    private void SettleSeat(Seat seat)
    {
        seat.Hand = HandEvaluator.Best([.. seat.Cards, .. _community], _log).Rank;

        var returned = 0;

        // Trips first, and outside everything else. It is a bet on this seat's own
        // cards: the dealer's hand does not touch it and neither does folding.
        if (seat.Trips > 0)
        {
            returned += _rules.Trips.For(seat.Hand.Value, _log).Returned(seat.Trips);
        }

        if (seat.Folded)
        {
            seat.Outcome = SeatOutcome.Folded;
            seat.Returned = returned;
            LogSeat(seat);
            return;
        }

        var comparison = seat.Hand.Value.CompareTo(_dealerHand!.Value);

        // The Ante pushes whenever the dealer fails to open, whatever the comparison
        // then says -- including on a hand the seat went on to lose. That is the
        // house's concession for the Blind paytable, and reading it as "pushes when
        // the seat wins" quietly keeps money that was never the house's.
        if (!_dealerQualified)
        {
            returned += seat.Ante;
        }
        else if (comparison > 0)
        {
            returned += seat.Ante * 2;
        }
        else if (comparison == 0)
        {
            returned += seat.Ante;
        }

        // The Play bet is settled on the comparison alone. Qualification never
        // touches it.
        if (comparison > 0)
        {
            returned += seat.Play * 2;
        }
        else if (comparison == 0)
        {
            returned += seat.Play;
        }

        // The Blind pays its own table on a win and pushes on a tie. Beneath a
        // straight the table itself pushes, so a winning hand can still take nothing
        // here -- which is correct, and is not the same as losing it.
        if (comparison > 0)
        {
            returned += _rules.Blind.For(seat.Hand.Value, _log).Returned(seat.Blind);
        }
        else if (comparison == 0)
        {
            returned += seat.Blind;
        }

        seat.Outcome = comparison > 0 ? SeatOutcome.Won : comparison == 0 ? SeatOutcome.Push : SeatOutcome.Lost;
        seat.Returned = returned;
        LogSeat(seat);
    }

    private void LogSeat(Seat seat)
    {
        if (!_log.Enabled)
        {
            return;
        }

        _log.Write(
            $"{seat.Name}: {seat.Hand?.Describe()} -- {seat.Outcome}, "
            + $"staked {seat.Wagered}, returned {seat.Returned} ({seat.Net:+#;-#;0})");
    }

    private SeatView ToView(Seat seat, bool revealAll)
    {
        // A seat-mate's hole cards are absent from the payload until showdown, on the
        // same rule the dealer's are: anything sent to the client is knowable by the
        // client, so there is nothing to blank out.
        var visible = revealAll || seat.IsPlayer;

        return new SeatView(
            seat.Index,
            seat.IsPlayer,
            seat.Name,
            visible ? seat.Cards.Select(card => card.Code).ToList() : [],
            seat.Ante,
            seat.Blind,
            seat.Trips,
            seat.Play,
            seat.Folded,
            revealAll ? seat.Hand?.Describe() : null,
            seat.Outcome,
            seat.Wagered,
            seat.Returned);
    }

    private DealerView DealerToView(bool reveal) => new(
        reveal ? _dealer.Select(card => card.Code).ToList() : [],
        reveal ? _dealerHand?.Describe() : null,
        reveal && _dealerQualified);
}
