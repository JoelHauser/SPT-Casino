namespace Poker.Game;

/// <summary>The three points at which a seat may bet. There are no others.</summary>
public enum Street
{
    /// <summary>Hole cards only. Play costs 4x or 3x the Ante.</summary>
    PreFlop,

    /// <summary>Three community cards showing. Play costs 2x.</summary>
    Flop,

    /// <summary>All five showing. The last chance: 1x, or fold.</summary>
    River,
}

public enum SeatMove
{
    /// <summary>Bet nothing and wait for the next street. Not available at the river.</summary>
    Check,

    /// <summary>Make the Play bet, at one of the multiples the street allows.</summary>
    Play,

    /// <summary>Give up the Ante and the Blind. Only available at the river.</summary>
    Fold,
}

/// <summary>What a seat decided, and at what size.</summary>
public readonly record struct SeatDecision(SeatMove Move, int Multiple = 0)
{
    public static SeatDecision Check => new(SeatMove.Check);

    public static SeatDecision Fold => new(SeatMove.Fold);

    public static SeatDecision Play(int multiple) => new(SeatMove.Play, multiple);

    public override string ToString() =>
        Move == SeatMove.Play ? $"plays {Multiple}x" : Move == SeatMove.Fold ? "folds" : "checks";
}

/// <summary>
/// Everything a seat is allowed to know when it decides.
///
/// Deliberately narrow. It carries this seat's own cards, the community cards that
/// are showing, and what the street permits -- and nothing about the dealer's hand,
/// the player's hand, or any other seat. A bot that could see those would be
/// cheating, and the cheapest way to guarantee it cannot is to never hand it the
/// information.
/// </summary>
public readonly record struct SeatContext(
    Seat Seat,
    Street Street,
    IReadOnlyList<Card> Community,
    IReadOnlyList<int> LegalMultiples,
    Rules Rules);

/// <summary>
/// Where a seat-mate's decision comes from.
///
/// The one seam the bots need. The human's decisions arrive as method calls on the
/// table; every other seat's arrive through this. Keeping it an interface is what
/// lets a test script a seat exactly and lets the real strategy arrive later without
/// the table changing.
///
/// Implementations must be deterministic given the same context and the same RNG,
/// or a table cannot be pinned by a test.
/// </summary>
public interface ISeatAgent
{
    SeatDecision Decide(SeatContext context);
}
