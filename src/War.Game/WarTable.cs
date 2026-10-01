namespace War.Game;

public enum Phase
{
    /// <summary>Nothing on the table. The next thing is a bet.</summary>
    AwaitingBet,

    /// <summary>The first two cards tied. The player must surrender or go to war.</summary>
    AwaitingDecision,

    /// <summary>The round is over and paid. The next thing is a bet.</summary>
    Settled,
}

public enum Outcome
{
    /// <summary>Not decided yet.</summary>
    None,

    /// <summary>The player's card beat the dealer's on the deal.</summary>
    Win,

    /// <summary>The dealer's card beat the player's on the deal.</summary>
    Lose,

    /// <summary>A tie, given up. Half the bet comes back.</summary>
    Surrender,

    /// <summary>A tie, fought, and the player's war card was equal or higher.</summary>
    WarWin,

    /// <summary>A tie, fought, and the dealer's war card was higher.</summary>
    WarLose,
}

/// <summary>Where a round stands, and every number the money path needs from it.</summary>
public sealed record RoundView
{
    public Phase Phase { get; init; }

    public Outcome Outcome { get; init; }

    public Card? PlayerCard { get; init; }

    public Card? DealerCard { get; init; }

    public Card? PlayerWarCard { get; init; }

    public Card? DealerWarCard { get; init; }

    /// <summary>How many cards were burned before the war. Zero unless there was one.</summary>
    public int Burned { get; init; }

    /// <summary>True when this round opened a freshly shuffled shoe.</summary>
    public bool Shuffled { get; init; }

    public int Ante { get; init; }

    public int TieBet { get; init; }

    /// <summary>What going to war cost: the ante again. Zero unless there was a war.</summary>
    public int Raise { get; init; }

    /// <summary>
    /// What the tie bet returned, stake included: eleven times it on a tie, nothing
    /// otherwise. Settled the moment the first two cards are seen, so it is known even
    /// while the war is still to be decided.
    /// </summary>
    public int TieReturn { get; init; }

    /// <summary>
    /// What the ante and the raise returned, stake included. Zero until the round is
    /// settled.
    /// </summary>
    public int MainReturn { get; init; }

    public bool TieWon => TieReturn > 0;

    /// <summary>Everything the player put down this round.</summary>
    public int Staked => Ante + TieBet + Raise;

    /// <summary>Everything that came back this round.</summary>
    public int Returned => TieReturn + MainReturn;

    public int Profit => Returned - Staked;
}

/// <summary>
/// One player's seat at the war table.
///
/// The game is all here and nothing about money is: this is handed amounts and says
/// what comes back, and the server moves the currency to match. It never sees a
/// stash, so it can be played a million times in a test in a second.
///
/// Each seat has its own shoe. There is no one else at the table, and a shoe shared
/// across players would let one player's war change what the next card is for
/// another.
/// </summary>
public sealed class WarTable
{
    private readonly Shoe _shoe;
    private RoundView _round = new() { Phase = Phase.AwaitingBet };

    public WarTable(Random rng)
        : this(new Shoe(Rules.Decks, rng))
    {
    }

    internal WarTable(Shoe shoe) => _shoe = shoe;

    public Phase Phase => _round.Phase;

    public int CardsLeft => _shoe.Remaining;

    public RoundView View() => _round;

    /// <summary>
    /// Deals one card each.
    ///
    /// A win or a loss settles here. A tie settles the tie bet and leaves the main bet
    /// standing, waiting for <see cref="GoToWar"/> or <see cref="Surrender"/>.
    /// </summary>
    public RoundView Deal(int ante, int tieBet)
    {
        if (_round.Phase == Phase.AwaitingDecision)
        {
            throw new InvalidOperationException("The last hand tied. Go to war or surrender first.");
        }

        if (ante <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(ante), ante, "The main bet must be more than nothing.");
        }

        if (tieBet < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(tieBet), tieBet, "A tie bet cannot be negative.");
        }

        // Between rounds only. A war is never dealt from a shoe shuffled halfway through
        // it, and the cut card sits far enough up that no round can run past the end.
        var shuffled = false;
        if (_shoe.NeedsShuffle)
        {
            _shoe.Shuffle();
            shuffled = true;
        }

        var player = _shoe.Draw();
        var dealer = _shoe.Draw();
        var tied = player.Rank == dealer.Rank;

        _round = new RoundView
        {
            Phase = tied ? Phase.AwaitingDecision : Phase.Settled,
            Outcome = tied ? Outcome.None : (player.Rank > dealer.Rank ? Outcome.Win : Outcome.Lose),
            PlayerCard = player,
            DealerCard = dealer,
            Shuffled = shuffled,
            Ante = ante,
            TieBet = tieBet,
            TieReturn = tied ? tieBet * (Rules.TiePays + 1) : 0,

            // Even money on a win: the ante back and as much again.
            MainReturn = !tied && player.Rank > dealer.Rank ? ante * 2 : 0,
        };

        return _round;
    }

    /// <summary>
    /// Matches the ante and plays one more card each, after burning three.
    ///
    /// The player wins on **equal or higher**. That is the house rule this table
    /// follows, and it is the generous reading: some casinos treat a tie at war as a
    /// further tie, and some pay a bonus on it. Here it is simply a win.
    ///
    /// Winning returns the ante as a push and pays the raise even money -- three times
    /// the ante back, for two put down. Losing returns nothing.
    /// </summary>
    public RoundView GoToWar()
    {
        RequireDecision();

        _shoe.Burn(Rules.WarBurn);

        var player = _shoe.Draw();
        var dealer = _shoe.Draw();
        var won = player.Rank >= dealer.Rank;
        var raise = _round.Ante;

        _round = _round with
        {
            Phase = Phase.Settled,
            Outcome = won ? Outcome.WarWin : Outcome.WarLose,
            PlayerWarCard = player,
            DealerWarCard = dealer,
            Burned = Rules.WarBurn,
            Raise = raise,
            MainReturn = won ? _round.Ante + (raise * 2) : 0,
        };

        return _round;
    }

    /// <summary>
    /// Gives the tie up. Half the ante is forfeit and half comes back.
    ///
    /// An odd ante cannot be halved, so the odd unit stays with the player: what comes
    /// back is the ante less half of it rounded down. One rouble in the player's favour
    /// on an odd bet is the right way round for a house to be wrong.
    /// </summary>
    public RoundView Surrender()
    {
        RequireDecision();

        _round = _round with
        {
            Phase = Phase.Settled,
            Outcome = Outcome.Surrender,
            MainReturn = _round.Ante - (_round.Ante / 2),
        };

        return _round;
    }

    private void RequireDecision()
    {
        if (_round.Phase != Phase.AwaitingDecision)
        {
            throw new InvalidOperationException("There is no tie to decide.");
        }
    }
}
