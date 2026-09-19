using SPTarkov.Server.Core.Models.Eft.Common.Request;
using SPTarkov.Server.Core.Models.Utils;

namespace HorseRacing.Server;

/// <summary>
/// The wire.
///
/// **Every property is PascalCase and nothing on it is an enum.** SPT matches request
/// bodies case-sensitively, so a lowercase key binds nothing and the field silently
/// takes its default -- which is how a 50,000 stake arrives as 0 while looking like it
/// bound correctly. And SPT registers `EftEnumConverterFactory` into
/// `options.Converters`, which outranks a `[JsonConverter]` on an enum type, so enums
/// go over as integers unless every property carrying one is attributed. Four sibling
/// tables were caught by that; sending strings sidesteps it.
///
/// That applies to <see cref="SlipEntry.Kind"/>, which is an enum everywhere except
/// here.
/// </summary>
public record PingRequest : IRequestData;

/// <summary>One bet on the slip, as the panel sends it.</summary>
public record SlipEntry
{
    /// <summary>Win, Place, Show, Exacta or Quinella. Parsed by name, refused if unknown.</summary>
    public string Kind { get; set; } = string.Empty;

    /// <summary>The runner backed, by saddlecloth number.</summary>
    public int First { get; set; }

    /// <summary>The second runner for a pair bet, or zero.</summary>
    public int Second { get; set; }

    /// <summary>What is on it, in the slip's currency.</summary>
    public long Stake { get; set; }
}

/// <summary>Puts a slip on and runs the race.</summary>
public record PlaceRequest : IRequestData
{
    /// <summary>
    /// Which course, by id: "dash", "mile" or "marathon".
    ///
    /// **Refused if it is not one of them, rather than defaulted.** The three courses
    /// price the same bet differently -- runner 7 is 5.78 at the dash and 36.28 at the
    /// marathon -- so quietly running a slip at the wrong one would pay from a board
    /// the player was never shown.
    /// </summary>
    public string Track { get; set; } = string.Empty;

    /// <summary>Roubles, Dollars or Euros. Parsed by name, refused if unknown.</summary>
    public string Wallet { get; set; } = nameof(HorseRacing.Server.Wallet.Roubles);

    /// <summary>
    /// The bets. **One currency and one course for the whole slip**, which is why both
    /// are up here rather than on each entry: a slip half in roubles and half at the
    /// marathon has no total and no board.
    /// </summary>
    public List<SlipEntry> Bets { get; set; } = [];

    /// <summary>
    /// Whether the player has turned the maximum off in the F12 menu.
    ///
    /// Sent on every race rather than only when true, so the request says plainly what
    /// was asked for. The minimum is not affected, and neither -- on this table alone
    /// -- is the arithmetic ceiling. See <see cref="WalletInfo.AllowsSlip"/>.
    /// </summary>
    public bool IgnoreMaximum { get; set; }
}

/// <summary>Does nothing to the game. See <see cref="RaceItemEventRouter"/>.</summary>
public record RaceSyncAction : BaseInteractionRequestData;

/// <summary>Asks for the lifetime record. Nothing to send -- the session id is enough.</summary>
public record StatsRequest : IRequestData;

/// <summary>One runner as a given course prices it.</summary>
public record RunnerView
{
    public int Number { get; init; }

    public string Name { get; init; } = string.Empty;

    /// <summary>Its speed rating, 1-20. Shown so the form is readable rather than magic.</summary>
    public int Speed { get; init; }

    /// <summary>Its stamina rating, 1-20.</summary>
    public int Stamina { get; init; }

    /// <summary>Its exact chance of winning **at this course**.</summary>
    public double Chance { get; init; }
}

/// <summary>One spot on a board and what it pays.</summary>
public record PriceView
{
    public string Kind { get; init; } = string.Empty;

    public int First { get; init; }

    public int Second { get; init; }

    public double Chance { get; init; }

    /// <summary>Total returned per unit staked, stake included.</summary>
    public double Price { get; init; }
}

/// <summary>
/// One course, its card and its whole board.
///
/// Self-contained on purpose: the panel switches course by swapping which one of these
/// it is drawing from, with nothing to re-fetch and nothing to recompute. Three of them
/// is about 30KB of JSON on a route that is already called once per open.
/// </summary>
public record CourseView
{
    public string Id { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    public string Distance { get; init; } = string.Empty;

    public string Blurb { get; init; } = string.Empty;

    /// <summary>
    /// How long the race is, in furlongs. The client draws a marker per furlong, which
    /// is what makes one course look longer than another.
    /// </summary>
    public int Furlongs { get; init; }

    /// <summary>How long the race takes on screen, in seconds.</summary>
    public double RunSeconds { get; init; }

    /// <summary>
    /// The most a whole slip may cost here, in chips.
    ///
    /// Per course, because it is arithmetic on that course's longest price rather than
    /// a house rule. The panel shows the smaller of this and the currency's own cap.
    /// </summary>
    public int MaxSlip { get; init; }

    public IReadOnlyList<RunnerView> Runners { get; init; } = [];

    /// <summary>All 108 spots at this course.</summary>
    public IReadOnlyList<PriceView> Board { get; init; } = [];
}

/// <summary>What one bet on the slip did.</summary>
public record SettlementView
{
    public string Kind { get; init; } = string.Empty;

    public int First { get; init; }

    public int Second { get; init; }

    public long Stake { get; init; }

    public bool Won { get; init; }

    public long Returned { get; init; }
}

/// <summary>The race that just ran.</summary>
public record RaceView
{
    /// <summary>Which course it was run at, echoed back so the client cannot mis-draw it.</summary>
    public string Track { get; init; } = string.Empty;

    /// <summary>
    /// Every runner's saddlecloth number in finishing position. <c>Order[0]</c> won.
    ///
    /// **This is what the client animates to.** The whole field is sent rather than
    /// only the placings, because the panel draws eight horses crossing a line and has
    /// to know where the other five went.
    /// </summary>
    public IReadOnlyList<int> Order { get; init; } = [];

    public IReadOnlyList<SettlementView> Settlements { get; init; } = [];

    public long Staked { get; init; }

    public long Returned { get; init; }

    public long Profit { get; init; }
}

/// <summary>Answered by every route.</summary>
public record RaceResponse
{
    public bool Ok { get; init; } = true;

    public string? Error { get; init; }

    /// <summary>
    /// Set when the reply carries something the player has to be told regardless of
    /// what they asked for -- money handed back, say. The client shows it and asks the
    /// game to resync its stash.
    /// </summary>
    public string? Note { get; init; }

    public RaceView? Race { get; init; }

    public static RaceResponse Failed(string error) => new() { Ok = false, Error = error };
}

/// <summary>What one currency will take.</summary>
public record StakeLimits
{
    /// <summary>The smallest single bet.</summary>
    public int Min { get; init; }

    /// <summary>
    /// The most the whole slip may come to in this currency, before the course's own
    /// ceiling is applied. See <see cref="CourseView.MaxSlip"/>.
    /// </summary>
    public int Max { get; init; }

    public int Step { get; init; }

    public string Sign { get; init; } = string.Empty;
}

/// <summary>
/// The health check. Answers "did the mod load, did the session resolve, can the money
/// be read" -- the first thing worth having and the last thing to stop working.
///
/// It also carries every course, its card and its board, so the panel draws the table's
/// own numbers rather than a copy that can drift from them. A panel holding its own
/// copy of the prices is a panel that can advertise a payout the table does not give.
/// </summary>
public record PingResponse
{
    public bool Ok { get; init; } = true;

    public string ModVersion { get; init; } = string.Empty;

    public string SessionId { get; init; } = string.Empty;

    public bool HasProfile { get; init; }

    public Dictionary<string, int> Balances { get; init; } = [];

    public Dictionary<string, StakeLimits> Limits { get; init; } = [];

    /// <summary>Every course, shortest first.</summary>
    public IReadOnlyList<CourseView> Courses { get; init; } = [];

    /// <summary>The house's cut, as a fraction. The same at every course.</summary>
    public double Takeout { get; init; }

    /// <summary>How many bets may be on one slip.</summary>
    public int MaxBets { get; init; }

    public string? Note { get; init; }
}
