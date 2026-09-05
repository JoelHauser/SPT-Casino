using Blackjack.Game;

namespace Blackjack.Server;

/// <summary>
/// Totals for one currency. Kept separate because a rouble and a dollar cannot be
/// added together -- there is no exchange rate the player would agree with.
/// </summary>
public class CurrencyStats
{
    public int RoundsPlayed { get; set; }

    public long Wagered { get; set; }

    public long Returned { get; set; }

    /// <summary>Best single round, as profit. Zero if the player has never won one.</summary>
    public int BestRound { get; set; }

    /// <summary>Worst single round, as a negative number.</summary>
    public int WorstRound { get; set; }

    public long Net => Returned - Wagered;
}

/// <summary>
/// A player's lifetime record. Persisted outside the SPT profile so this mod never
/// changes the profile schema -- see <see cref="StatsStore"/>.
/// </summary>
public class PlayerStats
{
    public int RoundsPlayed { get; set; }

    /// <summary>Higher than RoundsPlayed once splits are involved.</summary>
    public int HandsPlayed { get; set; }

    public int Wins { get; set; }

    public int Losses { get; set; }

    public int Pushes { get; set; }

    public int Blackjacks { get; set; }

    public int Busts { get; set; }

    /// <summary>Positive for a winning run, negative for a losing one.</summary>
    public int CurrentStreak { get; set; }

    public int BestStreak { get; set; }

    public long FirstPlayedUtc { get; set; }

    public long LastPlayedUtc { get; set; }

    /// <summary>Keyed by <see cref="Wallet"/> name so the JSON stays readable.</summary>
    public Dictionary<string, CurrencyStats> ByCurrency { get; set; } = [];

    /// <summary>
    /// Folds a settled round in. Pure and self-contained, so the whole of the
    /// accounting is testable without touching a file or a server.
    /// </summary>
    public void Record(RoundView view, Wallet wallet, long nowUtc)
    {
        if (view.Phase != RoundPhase.Settled)
        {
            throw new ArgumentException("Only a settled round can be recorded.", nameof(view));
        }

        RoundsPlayed++;
        HandsPlayed += view.PlayerHands.Count;

        foreach (var hand in view.PlayerHands)
        {
            switch (hand.Outcome)
            {
                case HandOutcome.Blackjack:
                    Blackjacks++;
                    Wins++;
                    break;
                case HandOutcome.Win:
                    Wins++;
                    break;
                case HandOutcome.Bust:
                    Busts++;
                    Losses++;
                    break;
                case HandOutcome.Lose:
                    Losses++;
                    break;
                case HandOutcome.Push:
                    Pushes++;
                    break;
            }
        }

        var key = wallet.ToString();
        if (!ByCurrency.TryGetValue(key, out var currency))
        {
            currency = new CurrencyStats();
            ByCurrency[key] = currency;
        }

        currency.RoundsPlayed++;
        currency.Wagered += view.TotalWagered;
        currency.Returned += view.TotalReturned;
        currency.BestRound = Math.Max(currency.BestRound, view.Net);
        currency.WorstRound = Math.Min(currency.WorstRound, view.Net);

        // Streaks run on rounds, not hands -- a split that wins one and loses the
        // other is one round the player broke even on, not a win and a loss.
        CurrentStreak = view.Net switch
        {
            > 0 => Math.Max(CurrentStreak, 0) + 1,
            < 0 => Math.Min(CurrentStreak, 0) - 1,
            _ => 0,
        };

        BestStreak = Math.Max(BestStreak, CurrentStreak);

        if (FirstPlayedUtc == 0)
        {
            FirstPlayedUtc = nowUtc;
        }

        LastPlayedUtc = nowUtc;
    }
}
