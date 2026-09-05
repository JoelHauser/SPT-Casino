using Roulette.Game;
using SPTarkov.Server.Core.Models.Eft.Common.Request;
using SPTarkov.Server.Core.Models.Utils;

namespace Roulette.Server;

/// <summary>
/// The wire.
///
/// **Every property is PascalCase and nothing on it is an enum.** SPT matches request
/// bodies case-sensitively, so a lowercase key binds nothing and the field silently
/// takes its default -- which is how a 100,000 stake arrives as 0 while looking like
/// it bound correctly. And SPT registers `EftEnumConverterFactory` into
/// `options.Converters`, which outranks a `[JsonConverter]` on an enum type, so enums
/// go over as integers unless every property carrying one is attributed. Both sibling
/// mods were caught by that; sending strings sidesteps it.
/// </summary>
/// <summary>
/// Carries nothing, because it asks for nothing. Sent on EFT's item-event endpoint
/// when the client needs the profile changes the server has been holding for it.
/// See <see cref="RouletteItemEventRouter"/>.
/// </summary>
public record RouletteSyncAction : BaseInteractionRequestData;

public record PingRequest : IRequestData;

public record StateRequest : IRequestData;

public record ClearRequest : IRequestData;

public record SpinRequest : IRequestData;

/// <summary>Takes chips back off one spot. The same shape as placing them.</summary>
public record RemoveRequest : IRequestData
{
    public string Kind { get; set; } = string.Empty;

    public int Selection { get; set; }

    /// <summary>How much to lift. Zero or less takes the whole pile.</summary>
    public int Amount { get; set; }
}

/// <summary>Puts one chip -- or several -- on one spot.</summary>
public record PlaceRequest : IRequestData
{
    /// <summary>
    /// The bet, by name: Straight, Split, Street, Corner, SixLine, Column, Dozen,
    /// Red, Black, Odd, Even, Low, High, TopLine. Parsed case-insensitively, and an
    /// unknown name is refused by name rather than silently defaulting to the first
    /// member of the enum -- which would put money on a straight-up bet nobody asked
    /// for.
    /// </summary>
    public string Kind { get; set; } = string.Empty;

    /// <summary>
    /// Which spot, and what it means depends on the kind. See <see cref="Bet"/>: a
    /// number for Straight, which one for Column and Dozen, the lowest number covered
    /// for Street, Corner and SixLine, and an **index into the enumerated splits** for
    /// Split. Ignored by the even-money bets and the top line.
    /// </summary>
    public int Selection { get; set; }

    /// <summary>Chips. A whole number of the table minimum.</summary>
    public int Amount { get; set; }
}

/// <summary>
/// Answered by every route.
///
/// A refusal still carries the table. The client redraws what came back rather than
/// arguing with the server about whose picture is right, which is the rule both
/// siblings settled on.
/// </summary>
public record RouletteResponse
{
    public bool Ok { get; init; } = true;

    public string? Error { get; init; }

    public TableView? Table { get; init; }

    /// <summary>
    /// Set when the reply carries something the player has to be told regardless of
    /// what they asked for -- money handed back, say. The client shows it and, if it
    /// says money moved, asks the game to resync its stash.
    /// </summary>
    public string? Note { get; init; }

    public static RouletteResponse Failed(string error) => new() { Ok = false, Error = error };
}

/// <summary>
/// The health check. Answers "did the mod load, did the session resolve, can the
/// money be read" -- the first thing worth having and the last thing to stop working.
/// </summary>
public record PingResponse
{
    public bool Ok { get; init; } = true;

    public string ModVersion { get; init; } = string.Empty;

    public string SessionId { get; init; } = string.Empty;

    public bool HasProfile { get; init; }

    public Dictionary<string, int> Balances { get; init; } = [];

    public Dictionary<string, StakeLimits> Limits { get; init; } = [];

    /// <summary>
    /// True while the mod has no way to move currency at all. The client says so on
    /// screen: a stash that never changes otherwise reads as the mod being broken.
    /// </summary>
    public bool MoneyIsNotMovedYet { get; init; } = true;
}

public record StakeLimits
{
    public int Min { get; init; }

    public int Max { get; init; }

    /// <summary>
    /// The live stack limit, read off the running server rather than assumed. Zero
    /// when there is no profile to read it against.
    /// </summary>
    public int StackLimit { get; init; }
}
