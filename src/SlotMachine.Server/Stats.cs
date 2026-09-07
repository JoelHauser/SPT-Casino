namespace SlotMachine.Server;

/// <summary>
/// Totals for one currency. Kept separate because a rouble and a dollar cannot be
/// added together -- there is no exchange rate the player would agree with. Same
/// shape as Blackjack's <c>CurrencyStats</c>, and deliberately not shared with it:
/// the two live in different DI containers (see <see cref="IStatsStore"/>), so
/// sharing the type would not save the constructor or the persistence around it,
/// only the four fields below.
/// </summary>
public class SpinCurrencyStats
{
    public int PullsPlayed { get; set; }

    public long Wagered { get; set; }

    public long Returned { get; set; }

    /// <summary>Best single pull, as profit (Paid minus Staked).</summary>
    public long BestPull { get; set; }

    /// <summary>Worst single pull, as a negative number -- the stake, on a pull that paid nothing.</summary>
    public long WorstPull { get; set; }

    public long Net => Returned - Wagered;
}

/// <summary>
/// A player's lifetime record at this machine. Persisted outside the SPT profile so
/// this mod never changes the profile schema -- see <see cref="StatsStore"/>.
/// </summary>
public class PlayerStats
{
    public int PullsPlayed { get; set; }

    /// <summary>Pulls that paid something at all, however small.</summary>
    public int Wins { get; set; }

    /// <summary>Pulls that paid nothing. Always <c>PullsPlayed - Wins</c>.</summary>
    public int Losses { get; set; }

    /// <summary>
    /// Pulls that hit the JACKPOT tier. Must stay in step with the 100x threshold
    /// <c>SlotPanel.SetPaid</c> uses for the word "JACKPOT" -- this is that same
    /// moment, counted.
    /// </summary>
    public int Jackpots { get; set; }

    /// <summary>
    /// The best Paid/Staked ratio ever hit, e.g. 134 for a 134x pull. Currency-
    /// agnostic by construction, unlike an amount -- a rouble and a dollar win are
    /// not comparable, but two multiples are.
    /// </summary>
    public double BestMultiple { get; set; }

    /// <summary>Positive for a run of paying pulls, negative for a run of dry ones.</summary>
    public int CurrentStreak { get; set; }

    public int BestStreak { get; set; }

    public long FirstPlayedUtc { get; set; }

    public long LastPlayedUtc { get; set; }

    /// <summary>Keyed by <see cref="Wallet"/> name so the JSON stays readable.</summary>
    public Dictionary<string, SpinCurrencyStats> ByCurrency { get; set; } = [];

    /// <summary>
    /// Folds one settled pull in. Pure and self-contained, so the whole of the
    /// accounting is testable without touching a file or a server -- the shape this
    /// was built to match is Blackjack.Server's own PlayerStats.Record.
    /// </summary>
    public void Record(long stake, long paid, Wallet wallet, long nowUtc)
    {
        if (stake <= 0)
        {
            throw new ArgumentException("A pull must have staked something.", nameof(stake));
        }

        PullsPlayed++;

        var profit = paid - stake;
        var multiple = (double)paid / stake;

        if (paid > 0)
        {
            Wins++;
        }
        else
        {
            Losses++;
        }

        if (multiple >= 100d)
        {
            Jackpots++;
        }

        BestMultiple = Math.Max(BestMultiple, multiple);

        // Streaks run on profit, not on Paid > 0: a pull that returns exactly the
        // stake is neither a win nor a loss worth extending a streak over, the same
        // way Blackjack resets on a push.
        CurrentStreak = profit switch
        {
            > 0 => Math.Max(CurrentStreak, 0) + 1,
            < 0 => Math.Min(CurrentStreak, 0) - 1,
            _ => 0,
        };

        BestStreak = Math.Max(BestStreak, CurrentStreak);

        var key = wallet.ToString();
        if (!ByCurrency.TryGetValue(key, out var currency))
        {
            currency = new SpinCurrencyStats();
            ByCurrency[key] = currency;
        }

        currency.PullsPlayed++;
        currency.Wagered += stake;
        currency.Returned += paid;
        currency.BestPull = Math.Max(currency.BestPull, profit);
        currency.WorstPull = Math.Min(currency.WorstPull, profit);

        if (FirstPlayedUtc == 0)
        {
            FirstPlayedUtc = nowUtc;
        }

        LastPlayedUtc = nowUtc;
    }
}
