using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.ItemEvent;

namespace Poker.Server.Tests;

/// <summary>
/// A stash that is just a dictionary.
///
/// The reason the money tests need no server at all. It behaves the way the real
/// bank does at the boundaries that matter -- refusing a debit it cannot cover,
/// never going negative -- so a service that satisfies this one is doing the
/// arithmetic right, whatever `InventoryHelper` then does with it.
/// </summary>
public sealed class FakeBank : IBank
{
    private readonly Dictionary<Wallet, int> _balances = new();

    /// <summary>Every move, in order. What the invariant test measures.</summary>
    public List<(Wallet Wallet, int Amount)> Movements { get; } = [];

    public int Debits { get; private set; }

    public int Credits { get; private set; }

    /// <summary>Set when a debit was refused, so a test can tell a refusal from a bug.</summary>
    public int RefusedDebits { get; private set; }

    public void Seed(Wallet wallet, int amount) => _balances[wallet] = amount;

    public int GetBalance(MongoId sessionId, Wallet wallet) => _balances.GetValueOrDefault(wallet);

    public bool TryDebit(MongoId sessionId, Wallet wallet, int amount, ItemEventRouterResponse output)
    {
        if (amount <= 0 || GetBalance(sessionId, wallet) < amount)
        {
            RefusedDebits++;
            return false;
        }

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

    /// <summary>Roubles stack to a million on a stock server; dollars and euros to 50,000.</summary>
    public int MaxStackSize(Wallet wallet) => wallet switch
    {
        Wallet.Roubles => 1_000_000,
        _ => 50_000,
    };
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

/// <summary>
/// Escrow without a file behind it. Keeps the last thing it was told, which is the
/// whole contract -- what the player is owed *now*, not a tally of what they staked.
/// </summary>
public sealed class FakeEscrow : IEscrowStore
{
    private readonly Dictionary<string, OutstandingStack> _held = new();

    /// <summary>Every value ever recorded, so a test can see it tracking the stack.</summary>
    public List<int> Recorded { get; } = [];

    public int Releases { get; private set; }

    public OutstandingStack? Get(MongoId sessionId) =>
        _held.GetValueOrDefault(sessionId.ToString());

    public void Record(MongoId sessionId, Wallet wallet, int chips)
    {
        _held[sessionId.ToString()] = new OutstandingStack
        {
            Wallet = wallet.ToString(),
            Chips = chips,
        };

        Recorded.Add(chips);
    }

    public void Release(MongoId sessionId)
    {
        if (_held.Remove(sessionId.ToString()))
        {
            Releases++;
        }
    }

    /// <summary>Drops the table without paying, the way a crash would.</summary>
    public void SurviveACrash() { }
}

/// <summary>Names without a database behind them.</summary>
public sealed class FakeNames : INameSource
{
    public IReadOnlyList<string> Take(int count, Random rng) =>
        Enumerable.Range(1, count).Select(index => $"Bot{index}").ToList();
}
