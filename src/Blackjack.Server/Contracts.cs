using Blackjack.Game;
using SPTarkov.Server.Core.Models.Eft.Common.Request;
using SPTarkov.Server.Core.Models.Utils;

namespace Blackjack.Server;

public record DealRequest : IRequestData
{
    public string Wallet { get; set; } = nameof(Server.Wallet.Roubles);

    public int Wager { get; set; }

    /// <summary>
    /// Set when the player has turned the table maximum off.
    ///
    /// Taken at the client's word deliberately. This is single player: the person
    /// sending it owns the server it is sent to, and the setting lives in the
    /// BepInEx menu because that is where they will look for it rather than in a
    /// JSON file that needs a restart. Nothing is being defended against here.
    ///
    /// The minimum is not waivable. A bet of nothing is not a bet.
    /// </summary>
    public bool IgnoreMaximum { get; set; }
}

public record ActionRequest : IRequestData
{
    /// <summary>Hit, Stand, Double or Split. Parsed case-insensitively.</summary>
    public string Action { get; set; } = string.Empty;
}

public record StateRequest : IRequestData;

public record StatsRequest : IRequestData;

public record PingRequest : IRequestData;

/// <summary>
/// Answers the questions that must be true before a bet is worth attempting: did the
/// mod load, is the route reachable, did the session resolve to a real profile, and
/// can its money be read at all.
/// </summary>
public record PingResponse
{
    public bool Ok { get; init; } = true;

    public string ModVersion { get; init; } = string.Empty;

    /// <summary>Empty here means the session cookie did not resolve.</summary>
    public string SessionId { get; init; } = string.Empty;

    public bool HasProfile { get; init; }

    public Dictionary<string, int> Balances { get; init; } = [];

    /// <summary>
    /// What each wallet will take in a hand. Sent with the balances because the
    /// client has to be able to offer a legal bet: without these it can only offer
    /// the whole balance and let the table refuse it, which reads as a broken button
    /// rather than as a rule.
    /// </summary>
    public Dictionary<string, BetLimits> Limits { get; init; } = [];
}

/// <summary>
/// The table's ceiling and floor for one wallet. The player's own holdings are not
/// part of this -- these are the house's rules and are the same for everyone.
/// </summary>
public record BetLimits
{
    public int Min { get; init; }

    public int Max { get; init; }
}

/// <summary>
/// What every route returns. <see cref="Ok"/> false means the request was refused
/// before anything changed -- the client should show <see cref="Error"/> and keep
/// displaying the round it already had.
/// </summary>
public record BlackjackResponse
{
    public bool Ok { get; init; } = true;

    public string? Error { get; init; }

    public RoundView? Round { get; init; }

    /// <summary>
    /// Set when the round proceeded but something went wrong behind it -- notably a
    /// stake that could not be collected. The request still succeeded; the server
    /// operator needs to know, the player does not.
    /// </summary>
    public string? Warning { get; init; }

    /// <summary>
    /// Something worth recording that is not a fault, currently only a refunded
    /// stake. Without it a recovered stake reaches the log as an unexplained credit,
    /// which looks identical to a payout bug.
    ///
    /// The service carries no logger of its own -- that is what keeps it testable
    /// without a server -- so it reports here and the transport writes the line.
    /// </summary>
    public string? Note { get; init; }

    /// <summary>
    /// Balance in the wallet the round is denominated in, after settlement.
    ///
    /// Sent explicitly because a custom static route does not flow through the
    /// ItemEventRouter, so the client's own inventory model is stale until it
    /// refreshes. The UI must trust this number over anything it computes locally.
    /// </summary>
    public int Balance { get; init; }

    public string Wallet { get; init; } = nameof(Server.Wallet.Roubles);

    public static BlackjackResponse Failed(string error) => new() { Ok = false, Error = error };
}

/// <summary>
/// The deal, as an item-event action. Sent to the same endpoint the client already
/// uses for moving items, so the response carries ProfileChanges and the stash
/// updates without a reload.
/// </summary>
public record BlackjackDealAction : BaseInteractionRequestData
{
    public string Wallet { get; set; } = nameof(Server.Wallet.Roubles);

    public int Wager { get; set; }
}

/// <summary>
/// Hit, Stand, Double or Split. Named Move because the base class already owns
/// Action, which carries the event name itself.
/// </summary>
public record BlackjackPlayAction : BaseInteractionRequestData
{
    public string Move { get; set; } = string.Empty;
}
