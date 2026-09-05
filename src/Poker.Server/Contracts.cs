using Poker.Game;
using SPTarkov.Server.Core.Models.Eft.Common.Request;
using SPTarkov.Server.Core.Models.Utils;

namespace Poker.Server;

// Request bodies are matched case-sensitively, so these must be sent PascalCase or
// every property silently takes its default. Blackjack lost an afternoon to a wager
// of 10,000 arriving as 0.

public record PingRequest : IRequestData;

public record StateRequest : IRequestData;

public record LeaveRequest : IRequestData;

/// <summary>Sit down at a table.</summary>
public record SitRequest : IRequestData
{
    /// <summary>Seats including the player. Two to five.</summary>
    public int Seats { get; set; } = 4;

    /// <summary>
    /// Chips each seat starts with, and -- at one chip to the unit -- what the buy-in
    /// costs the player out of <see cref="Wallet"/>.
    /// </summary>
    public int BuyIn { get; set; } = 1_000_000;

    /// <summary>
    /// What the buy-in is paid in. One chip is one unit of it.
    ///
    /// That rate is why only roubles work at these stakes: a 2,000,000 chip buy-in is
    /// 2,000,000 roubles, and no other wallet is held in those numbers. Giving each
    /// wallet its own chips-per-unit rate is what would open the rest up.
    /// </summary>
    public string Wallet { get; set; } = nameof(Server.Wallet.Roubles);

    public int BigBlind { get; set; } = 20_000;

    /// <summary>Fixes the shuffle and the characters, so a hand can be got back.</summary>
    public int? Seed { get; set; }
}

/// <summary>Deal the next hand at a table already sat at.</summary>
public record DealRequest : IRequestData;

/// <summary>Fold, Check, Call or Raise. Parsed case-insensitively.</summary>
public record ActRequest : IRequestData
{
    public string Move { get; set; } = string.Empty;

    /// <summary>
    /// For a raise: the **total to be in for on this street**, not the extra being
    /// added. Poker is spoken that way, and reading it the other way is the easiest
    /// route to a betting round that takes the wrong number of chips.
    /// </summary>
    public int To { get; set; }
}

/// <summary>
/// Answers the questions that must be true before anything else is worth trying: did
/// the mod load, is the route reachable, did the session resolve to a real profile,
/// and can its money be read at all.
/// </summary>
public record PingResponse
{
    public bool Ok { get; init; } = true;

    public string ModVersion { get; init; } = string.Empty;

    /// <summary>Empty here means the session cookie did not resolve.</summary>
    public string SessionId { get; init; } = string.Empty;

    public bool HasProfile { get; init; }

    /// <summary>Read only. This build cannot move any of it.</summary>
    public Dictionary<string, int> Balances { get; init; } = [];

    /// <summary>What each wallet would take as a buy-in, once buy-ins exist.</summary>
    public Dictionary<string, BuyInLimits> Limits { get; init; } = [];

    /// <summary>
    /// False now that the buy-in is real. Kept on the wire so a client built against
    /// the earlier build still gets a truthful answer rather than an absent field.
    /// </summary>
    public bool ChipsAreNotional { get; init; }
}

public record BuyInLimits
{
    public int Min { get; init; }

    public int Max { get; init; }

    /// <summary>What one unit occupies. A limit of 1 means one item per unit.</summary>
    public int StackLimit { get; init; }
}

/// <summary>
/// What every game route returns. <see cref="Ok"/> false means the request was
/// refused before anything changed -- the client should show <see cref="Error"/> and
/// keep displaying the table it already had.
/// </summary>
public record PokerResponse
{
    public bool Ok { get; init; } = true;

    public string? Error { get; init; }

    public HoldemView? Table { get; init; }

    /// <summary>Who is sitting at the table, in seat order. Empty until sat down.</summary>
    public IReadOnlyList<string> Characters { get; init; } = [];

    /// <summary>
    /// Balance in the wallet the table is bought into, after whatever just happened.
    ///
    /// Sent explicitly because a static route does not flow through the item-event
    /// router, so the client's own inventory model is stale until it refreshes. The
    /// UI must trust this over anything it computes locally.
    /// </summary>
    public int Balance { get; init; }

    public string Wallet { get; init; } = nameof(Server.Wallet.Roubles);

    /// <summary>
    /// Something worth recording that is not a fault -- currently only a stack given
    /// back after a crash. Without it a recovered buy-in reaches the log as an
    /// unexplained credit, which looks identical to a payout bug.
    /// </summary>
    public string? Note { get; init; }

    public static PokerResponse Failed(string error) => new() { Ok = false, Error = error };
}


// The item-event shapes. Same fields as the static requests above, but derived from
// BaseInteractionRequestData so they arrive on the endpoint EFT already uses for
// moving items -- which is what lets the reply carry ProfileChanges and keep the
// stash in step. The base class already owns `Action`, which carries the event name.

public record PokerSitAction : BaseInteractionRequestData
{
    public int Seats { get; set; } = 4;

    public int BuyIn { get; set; } = 1_000_000;

    public int BigBlind { get; set; } = 20_000;

    public string Wallet { get; set; } = nameof(Server.Wallet.Roubles);
}

public record PokerDealAction : BaseInteractionRequestData;

/// <summary>Named Move because the base class already owns Action.</summary>
public record PokerActAction : BaseInteractionRequestData
{
    public string Move { get; set; } = string.Empty;

    public int To { get; set; }
}

public record PokerLeaveAction : BaseInteractionRequestData;

/// <summary>
/// Carries nothing, because it asks for nothing. Sent when the client needs the
/// profile changes the server has been holding for it.
/// </summary>
public record PokerSyncAction : BaseInteractionRequestData;
