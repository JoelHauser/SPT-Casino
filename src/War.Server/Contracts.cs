using SPTarkov.Server.Core.Models.Eft.Common.Request;
using SPTarkov.Server.Core.Models.Utils;

namespace War.Server;

/// <summary>
/// The wire.
///
/// **Every property is PascalCase and nothing on it is an enum.** SPT matches request
/// bodies case-sensitively, so a lowercase key binds nothing and the field silently
/// takes its default. And SPT's own enum converter outranks a `[JsonConverter]` on an
/// enum type, so enums go over as integers unless every property carrying one is
/// attributed. Sending strings sidesteps both, as every other table here does.
/// </summary>
public record PingRequest : IRequestData;

/// <summary>Asks where the round stands. Sent when the table is opened.</summary>
public record StateRequest : IRequestData;

/// <summary>Asks for the lifetime record.</summary>
public record StatsRequest : IRequestData;

/// <summary>Deals a hand.</summary>
public record DealRequest : IRequestData
{
    /// <summary>Roubles, Dollars or Euros. Parsed by name, refused if unknown.</summary>
    public string Wallet { get; set; } = nameof(War.Server.Wallet.Roubles);

    /// <summary>The main bet.</summary>
    public long Ante { get; set; }

    /// <summary>The optional tie bet. Zero for none.</summary>
    public long TieBet { get; set; }

    /// <summary>
    /// Whether the player has turned the maximum bet off in the F12 menu. Sent on
    /// every deal rather than only when true, so the request says plainly what was
    /// asked for. The minimum is not affected.
    /// </summary>
    public bool IgnoreMaximum { get; set; }
}

/// <summary>Answers a tie: "War" or "Surrender".</summary>
public record DecideRequest : IRequestData
{
    public string Choice { get; set; } = string.Empty;
}

/// <summary>Does nothing to the game. See <see cref="WarItemEventRouter"/>.</summary>
public record WarSyncAction : SPTarkov.Server.Core.Models.Eft.Common.Request.BaseInteractionRequestData;

/// <summary>A round, as the panel draws it. Card codes are "TD", "AS" and so on.</summary>
public record RoundWire
{
    /// <summary>AwaitingBet, AwaitingDecision or Settled.</summary>
    public string Phase { get; init; } = "AwaitingBet";

    /// <summary>None, Win, Lose, Surrender, WarWin or WarLose.</summary>
    public string Outcome { get; init; } = "None";

    public string? PlayerCard { get; init; }

    public string? DealerCard { get; init; }

    public string? PlayerWarCard { get; init; }

    public string? DealerWarCard { get; init; }

    public int Burned { get; init; }

    public bool Shuffled { get; init; }

    public long Ante { get; init; }

    public long TieBet { get; init; }

    public long Raise { get; init; }

    public long TieReturn { get; init; }

    public long MainReturn { get; init; }

    public long Staked { get; init; }

    public long Returned { get; init; }

    public long Profit { get; init; }
}

/// <summary>Answered by every route that plays.</summary>
public record WarResponse
{
    public bool Ok { get; init; } = true;

    public string? Error { get; init; }

    /// <summary>
    /// Set when the reply carries something the player has to be told regardless of
    /// what they asked for -- money handed back from an interrupted round, say.
    /// </summary>
    public string? Note { get; init; }

    public RoundWire? Round { get; init; }

    /// <summary>The currency the round is being played in.</summary>
    public string Wallet { get; init; } = nameof(War.Server.Wallet.Roubles);

    /// <summary>What the player holds of it, after this reply's money has moved.</summary>
    public long Balance { get; init; }

    public static WarResponse Failed(string error) => new() { Ok = false, Error = error };
}

/// <summary>What one currency takes per bet.</summary>
public record BetLimits
{
    public int Min { get; init; }

    public int Max { get; init; }

    public int Step { get; init; }

    public string Sign { get; init; } = string.Empty;
}

/// <summary>
/// The health check, and where the panel gets the table's own numbers -- the limits,
/// the tie bet's odds and the edges -- so it cannot advertise something the table does
/// not do.
/// </summary>
public record PingResponse
{
    public bool Ok { get; init; } = true;

    public string ModVersion { get; init; } = string.Empty;

    public string SessionId { get; init; } = string.Empty;

    public bool HasProfile { get; init; }

    public Dictionary<string, int> Balances { get; init; } = [];

    public Dictionary<string, BetLimits> Limits { get; init; } = [];

    /// <summary>The ceiling the F12 switch cannot lift.</summary>
    public int AbsoluteMax { get; init; }

    public int Decks { get; init; }

    public int TiePays { get; init; }

    /// <summary>The house edge going to war on every tie, as a percentage.</summary>
    public double HouseEdgeWar { get; init; }

    /// <summary>The house edge surrendering every tie, as a percentage.</summary>
    public double HouseEdgeSurrender { get; init; }

    /// <summary>The house edge on the tie bet, as a percentage.</summary>
    public double HouseEdgeTieBet { get; init; }

    public string? Note { get; init; }
}
