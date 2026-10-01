using War.Game;

namespace War.Server;

/// <summary>
/// Totals for one currency. Kept separate because a rouble and a dollar cannot be
/// added together -- there is no exchange rate the player would agree with.
/// </summary>
public class WarCurrencyStats
{
    public int RoundsPlayed { get; set; }

    public long Wagered { get; set; }

    public long Returned { get; set; }

    /// <summary>Best single round, as profit.</summary>
    public long BestRound { get; set; }

    /// <summary>Worst single round, as a negative number.</summary>
    public long WorstRound { get; set; }

    public long Net => Returned - Wagered;
}

/// <summary>
/// A player's lifetime record at the war table. Persisted outside the SPT profile so
/// this mod never changes the profile schema -- see <see cref="StatsStore"/>.
/// </summary>
public class PlayerStats
{
    public int RoundsPlayed { get; set; }

    /// <summary>Rounds won on the deal.</summary>
    public int Wins { get; set; }

    /// <summary>Rounds lost on the deal.</summary>
    public int Losses { get; set; }

    /// <summary>Ties on the deal, however they were answered.</summary>
    public int Ties { get; set; }

    public int WarsWon { get; set; }

    public int WarsLost { get; set; }

    public int Surrenders { get; set; }

    /// <summary>Tie bets that paid.</summary>
    public int TieBetsWon { get; set; }

    /// <summary>Positive for a run of profitable rounds, negative for a run of losing ones.</summary>
    public int CurrentStreak { get; set; }

    public int BestStreak { get; set; }

    public long FirstPlayedUtc { get; set; }

    public long LastPlayedUtc { get; set; }

    /// <summary>Keyed by <see cref="Wallet"/> name so the JSON stays readable.</summary>
    public Dictionary<string, WarCurrencyStats> ByCurrency { get; set; } = [];

    /// <summary>
    /// Folds one settled round in. Pure, so the accounting is testable without a file
    /// or a server. A round still waiting on a tie is refused: it has not finished
    /// costing or paying yet.
    /// </summary>
    public void Record(RoundView round, Wallet wallet, long nowUtc)
    {
        if (round.Phase != Phase.Settled)
        {
            throw new ArgumentException("Only a settled round can be recorded.", nameof(round));
        }

        RoundsPlayed++;

        switch (round.Outcome)
        {
            case Outcome.Win:
                Wins++;
                break;
            case Outcome.Lose:
                Losses++;
                break;
            case Outcome.Surrender:
                Ties++;
                Surrenders++;
                break;
            case Outcome.WarWin:
                Ties++;
                WarsWon++;
                break;
            case Outcome.WarLose:
                Ties++;
                WarsLost++;
                break;
        }

        if (round.TieWon)
        {
            TieBetsWon++;
        }

        // On profit, so a round that lost the main bet and won a bigger tie bet counts
        // as the win it was.
        CurrentStreak = round.Profit switch
        {
            > 0 => Math.Max(CurrentStreak, 0) + 1,
            < 0 => Math.Min(CurrentStreak, 0) - 1,
            _ => 0,
        };

        BestStreak = Math.Max(BestStreak, CurrentStreak);

        var key = wallet.ToString();
        if (!ByCurrency.TryGetValue(key, out var currency))
        {
            currency = new WarCurrencyStats();
            ByCurrency[key] = currency;
        }

        currency.RoundsPlayed++;
        currency.Wagered += round.Staked;
        currency.Returned += round.Returned;
        currency.BestRound = Math.Max(currency.BestRound, round.Profit);
        currency.WorstRound = Math.Min(currency.WorstRound, round.Profit);

        if (FirstPlayedUtc == 0)
        {
            FirstPlayedUtc = nowUtc;
        }

        LastPlayedUtc = nowUtc;
    }
}
