using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.ItemEvent;
using War.Game;

namespace War.Server.Tests;

/// <summary>
/// A stash that is just a dictionary. Refuses a debit it cannot cover and never goes
/// negative, which are the two boundaries the real bank holds that matter here.
/// </summary>
public sealed class FakeBank : IBank
{
    private readonly Dictionary<Wallet, int> _balances = new();

    /// <summary>Every move, in order. What the invariant tests measure.</summary>
    public List<(Wallet Wallet, int Amount)> Movements { get; } = [];

    public int Debits { get; private set; }

    public int Credits { get; private set; }

    /// <summary>
    /// Called with each debit just before it lands, so a test can look at what the
    /// escrow held at that instant.
    /// </summary>
    public Action<int>? BeforeDebit { get; set; }

    /// <summary>The net of every movement.</summary>
    public int Moved => Movements.Sum(m => m.Amount);

    public IEnumerable<int> Debited => Movements.Where(m => m.Amount < 0).Select(m => -m.Amount);

    public IEnumerable<int> Credited => Movements.Where(m => m.Amount > 0).Select(m => m.Amount);

    public void Seed(Wallet wallet, int amount) => _balances[wallet] = amount;

    public int GetBalance(MongoId sessionId, Wallet wallet) => _balances.GetValueOrDefault(wallet);

    public bool TryDebit(MongoId sessionId, Wallet wallet, int amount, ItemEventRouterResponse output)
    {
        if (amount <= 0 || GetBalance(sessionId, wallet) < amount)
        {
            return false;
        }

        BeforeDebit?.Invoke(amount);

        _balances[wallet] = GetBalance(sessionId, wallet) - amount;
        Movements.Add((wallet, -amount));
        Debits++;

        return true;
    }

    public void Credit(MongoId sessionId, Wallet wallet, int amount, ItemEventRouterResponse output)
    {
        if (amount <= 0)
        {
            return;
        }

        _balances[wallet] = GetBalance(sessionId, wallet) + amount;
        Movements.Add((wallet, amount));
        Credits++;
    }

    public int MaxStackSize(Wallet wallet) => wallet == Wallet.Roubles ? 1_000_000 : 50_000;
}

public sealed class FakeProfiles : IProfileGateway
{
    public bool Exists { get; set; } = true;

    public int Saves { get; private set; }

    public bool HasProfile(MongoId sessionId) => Exists;

    public Task SaveAsync(MongoId sessionId)
    {
        Saves++;
        return Task.CompletedTask;
    }
}

/// <summary>Escrow without a file behind it.</summary>
public sealed class FakeEscrow : IEscrowStore
{
    private readonly Dictionary<string, OutstandingStake> _held = new();

    /// <summary>Every value ever recorded, in order.</summary>
    public List<int> Recorded { get; } = [];

    public int Releases { get; private set; }

    public OutstandingStake? Get(MongoId sessionId) => _held.GetValueOrDefault(sessionId.ToString());

    public int Held(MongoId sessionId) => Get(sessionId)?.Amount ?? 0;

    public void Record(MongoId sessionId, Wallet wallet, int amount)
    {
        _held[sessionId.ToString()] = new OutstandingStake { Wallet = wallet.ToString(), Amount = amount };
        Recorded.Add(amount);
    }

    public void Release(MongoId sessionId)
    {
        if (_held.Remove(sessionId.ToString()))
        {
            Releases++;
        }
    }

    /// <summary>Plants a stake as though a server had died holding it.</summary>
    public void Strand(MongoId sessionId, Wallet wallet, int amount) =>
        _held[sessionId.ToString()] = new OutstandingStake { Wallet = wallet.ToString(), Amount = amount };
}

public sealed class FakeRandom(int seed) : IRandomSource
{
    public Random Create() => new(seed);
}

public sealed class FakeStats : IStatsStore
{
    private readonly Dictionary<string, PlayerStats> _stats = [];

    public int Saves { get; private set; }

    public PlayerStats Get(MongoId sessionId)
    {
        var key = sessionId.ToString();
        if (!_stats.TryGetValue(key, out var stats))
        {
            stats = new PlayerStats();
            _stats[key] = stats;
        }

        return stats;
    }

    public void Save(MongoId sessionId, PlayerStats stats)
    {
        _stats[sessionId.ToString()] = stats;
        Saves++;
    }
}

public sealed class QuietLog : IWarLog
{
    public void Info(string message)
    {
    }

    public void Detail(string message)
    {
    }

    public void Error(string message)
    {
    }
}

/// <summary>One table, with everything it needs, and the fakes kept to look at.</summary>
public sealed class Rig
{
    public static readonly MongoId Session = new("6a9b474574813708e8fc3ce5");

    public Rig(int seed = 1)
    {
        Tables = new TableStore(new FakeRandom(seed));
        Service = new WarService(Bank, Profiles, Escrow, Tables, Stats, new QuietLog());
    }

    public FakeBank Bank { get; } = new();

    public FakeProfiles Profiles { get; } = new();

    public FakeEscrow Escrow { get; } = new();

    public FakeStats Stats { get; } = new();

    public TableStore Tables { get; }

    public WarService Service { get; }

    /// <summary>Seats the player at a shoe that deals exactly these cards.</summary>
    public Rig Stack(params string[] codes)
    {
        Tables.Seed(Session, new WarTable(Shoe.Stacked(codes.Select(Parse))));
        return this;
    }

    public Task<WarResponse> Deal(long ante, long tie = 0, Wallet wallet = Wallet.Roubles, bool ignoreMaximum = false) =>
        Service.DealAsync(
            new DealRequest { Wallet = wallet.ToString(), Ante = ante, TieBet = tie, IgnoreMaximum = ignoreMaximum },
            Session,
            new ItemEventRouterResponse());

    public Task<WarResponse> Decide(string choice) =>
        Service.DecideAsync(new DecideRequest { Choice = choice }, Session, new ItemEventRouterResponse());

    public Task<WarResponse> State() => Service.StateAsync(Session, new ItemEventRouterResponse());

    public PingResponse Ping() => Service.Ping(Session, new ItemEventRouterResponse());

    private static Card Parse(string code)
    {
        var rank = code[0] switch
        {
            'T' => 10,
            'J' => 11,
            'Q' => 12,
            'K' => 13,
            'A' => 14,
            var digit => digit - '0',
        };

        var suit = code[1] switch
        {
            'C' => Suit.Clubs,
            'D' => Suit.Diamonds,
            'H' => Suit.Hearts,
            _ => Suit.Spades,
        };

        return new Card(rank, suit);
    }
}
