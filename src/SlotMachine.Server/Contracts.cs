using SPTarkov.Server.Core.Models.Eft.Common.Request;
using SPTarkov.Server.Core.Models.Utils;

namespace SlotMachine.Server;

/// <summary>
/// The wire.
///
/// **Every property is PascalCase and nothing on it is an enum.** SPT matches request
/// bodies case-sensitively, so a lowercase key binds nothing and the field silently
/// takes its default -- which is how a 50,000 stake arrives as 0 while looking like it
/// bound correctly. And SPT registers `EftEnumConverterFactory` into
/// `options.Converters`, which outranks a `[JsonConverter]` on an enum type, so enums
/// go over as integers unless every property carrying one is attributed. Three sibling
/// tables were caught by that; sending strings sidesteps it.
/// </summary>
public record PingRequest : IRequestData;

/// <summary>Pulls the handle once.</summary>
public record PullRequest : IRequestData
{
    /// <summary>Roubles, Dollars or Euros. Parsed by name, refused if unknown.</summary>
    public string Wallet { get; set; } = nameof(SlotMachine.Server.Wallet.Roubles);

    /// <summary>What the pull costs, in that currency.</summary>
    public long Stake { get; set; }
}

/// <summary>Does nothing to the game. See <see cref="SlotItemEventRouter"/>.</summary>
public record SlotSyncAction : BaseInteractionRequestData;

/// <summary>One symbol that paid, as the panel needs to show it.</summary>
public record WinView
{
    public string Symbol { get; init; } = string.Empty;

    /// <summary>How many reels it ran across, from the left.</summary>
    public int Reels { get; init; }

    /// <summary>How many of the 243 paths it paid on.</summary>
    public int Ways { get; init; }

    public long Paid { get; init; }
}

/// <summary>
/// The pull that just happened.
/// </summary>
public record PullView
{
    /// <summary>Where each reel stopped. **This is what the client animates to.**</summary>
    public IReadOnlyList<int> Stops { get; init; } = [];

    /// <summary>
    /// What is showing, reel by reel then row by row, as symbol names.
    ///
    /// Sent rather than left for the client to derive from the stops. It could work it
    /// out, and then it would be free to disagree with the machine about what it just
    /// paid for.
    /// </summary>
    public IReadOnlyList<IReadOnlyList<string>> Grid { get; init; } = [];

    public IReadOnlyList<WinView> Wins { get; init; } = [];

    public long Staked { get; init; }

    public long Paid { get; init; }

    public long Profit { get; init; }
}

/// <summary>Answered by every route.</summary>
public record SlotResponse
{
    public bool Ok { get; init; } = true;

    public string? Error { get; init; }

    /// <summary>
    /// Set when the reply carries something the player has to be told regardless of
    /// what they asked for -- money handed back, say. The client shows it and asks the
    /// game to resync its stash.
    /// </summary>
    public string? Note { get; init; }

    public PullView? Pull { get; init; }

    public static SlotResponse Failed(string error) => new() { Ok = false, Error = error };
}

/// <summary>What one currency will take per pull.</summary>
public record StakeLimits
{
    public int Min { get; init; }

    public int Max { get; init; }

    public int Step { get; init; }

    public string Sign { get; init; } = string.Empty;
}

/// <summary>
/// The health check. Answers "did the mod load, did the session resolve, can the money
/// be read" -- the first thing worth having and the last thing to stop working.
///
/// It also carries the paytable and the limits, so the panel draws the machine's own
/// numbers rather than a copy that can drift from them.
/// </summary>
public record PingResponse
{
    public bool Ok { get; init; } = true;

    public string ModVersion { get; init; } = string.Empty;

    public string SessionId { get; init; } = string.Empty;

    public bool HasProfile { get; init; }

    public Dictionary<string, int> Balances { get; init; } = [];

    public Dictionary<string, StakeLimits> Limits { get; init; } = [];

    /// <summary>Symbol name to its three multipliers, for three, four and five.</summary>
    public Dictionary<string, IReadOnlyList<int>> Paytable { get; init; } = [];

    /// <summary>How many ways there are. 243, and worth saying on the machine.</summary>
    public int Ways { get; init; }

    /// <summary>What the machine gives back, as a percentage. Computed, not measured.</summary>
    public double ReturnToPlayer { get; init; }

    public string? Note { get; init; }
}
