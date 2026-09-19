namespace HorseRacing.Server;

/// <summary>
/// Totals for one currency. Kept separate because a rouble and a dollar cannot be
/// added together -- there is no exchange rate the player would agree with. Same shape
/// as Slots' <c>SpinCurrencyStats</c> and Blackjack's <c>CurrencyStats</c>, and
/// deliberately not shared with either: they live in different DI containers (see
/// <see cref="IStatsStore"/>), so sharing the type would save the fields below and
/// neither the constructor nor the persistence around it.
/// </summary>
public class RaceCurrencyStats
{
    public int RacesPlayed { get; set; }

    /// <summary>Every bet on every slip, added up.</summary>
    public int BetsPlaced { get; set; }

    public long Wagered { get; set; }

    public long Returned { get; set; }

    /// <summary>Best single race, as profit (returned minus staked across the slip).</summary>
    public long BestRace { get; set; }

    /// <summary>Worst single race, as a negative number -- a slip that returned nothing.</summary>
    public long WorstRace { get; set; }

    public long Net => Returned - Wagered;
}

/// <summary>
/// A player's lifetime record at this table. Persisted outside the SPT profile so this
/// mod never changes the profile schema -- see <see cref="StatsStore"/>.
/// </summary>
public class PlayerStats
{
    public int RacesPlayed { get; set; }

    /// <summary>Races where the slip returned something at all, however small.</summary>
    public int Wins { get; set; }

    /// <summary>Races where the slip returned nothing. Always <c>RacesPlayed - Wins</c>.</summary>
    public int Losses { get; set; }

    /// <summary>
    /// Slips that returned a hundred times what they cost or more.
    ///
    /// A real thing to hit here rather than a token: the longest exacta on the card
    /// pays over 750 for one, so a single lucky bet clears this on its own.
    /// </summary>
    public int BigWins { get; set; }

    /// <summary>
    /// The best returned/staked ratio ever hit on one slip. Currency-agnostic by
    /// construction, unlike an amount -- a rouble win and a dollar win are not
    /// comparable, but two multiples are.
    /// </summary>
    public double BestMultiple { get; set; }

    /// <summary>
    /// How many times each runner has won, keyed by saddlecloth number.
    ///
    /// The one statistic this table has that the others cannot: a form guide. It is
    /// also the cheapest check on the card from the player's own side -- a runner that
    /// never wins over a few hundred races is a runner with a broken weight, and they
    /// would notice long before anybody reading the code did.
    /// </summary>
    public Dictionary<string, int> WinsByRunner { get; set; } = [];

    /// <summary>Positive for a run of paying slips, negative for a run of dry ones.</summary>
    public int CurrentStreak { get; set; }

    public int BestStreak { get; set; }

    public long FirstPlayedUtc { get; set; }

    public long LastPlayedUtc { get; set; }

    /// <summary>Keyed by <see cref="Wallet"/> name so the JSON stays readable.</summary>
    public Dictionary<string, RaceCurrencyStats> ByCurrency { get; set; } = [];

    /// <summary>
    /// Folds one settled race in. Pure and self-contained, so the whole of the
    /// accounting is testable without touching a file or a server.
    /// </summary>
    /// <param name="staked">The slip's total.</param>
    /// <param name="returned">What came back, winning stakes included.</param>
    /// <param name="bets">How many bets were on the slip.</param>
    /// <param name="winner">The saddlecloth number that won, for the form guide.</param>
    public void Record(long staked, long returned, int bets, int winner, Wallet wallet, long nowUtc)
    {
        if (staked <= 0)
        {
            throw new ArgumentException("A race must have staked something.", nameof(staked));
        }

        if (bets <= 0)
        {
            throw new ArgumentException("A slip with no bets on it is not a race.", nameof(bets));
        }

        RacesPlayed++;

        var profit = returned - staked;
        var multiple = (double)returned / staked;

        if (returned > 0)
        {
            Wins++;
        }
        else
        {
            Losses++;
        }

        if (multiple >= 100d)
        {
            BigWins++;
        }

        BestMultiple = Math.Max(BestMultiple, multiple);

        var runner = winner.ToString();
        WinsByRunner[runner] = WinsByRunner.TryGetValue(runner, out var seen) ? seen + 1 : 1;

        // Streaks run on profit, not on returned > 0: a slip that hands back exactly
        // what it cost is neither a win nor a loss worth extending a streak over, the
        // same way Blackjack resets on a push. It is reachable here in a way it is not
        // at a slot machine -- backing a short-priced favourite to show can return
        // almost precisely the stake.
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
            currency = new RaceCurrencyStats();
            ByCurrency[key] = currency;
        }

        currency.RacesPlayed++;
        currency.BetsPlaced += bets;
        currency.Wagered += staked;
        currency.Returned += returned;
        currency.BestRace = Math.Max(currency.BestRace, profit);
        currency.WorstRace = Math.Min(currency.WorstRace, profit);

        if (FirstPlayedUtc == 0)
        {
            FirstPlayedUtc = nowUtc;
        }

        LastPlayedUtc = nowUtc;
    }
}
