using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.ItemEvent;

namespace SlotMachine.Server.Tests;

/// <summary>
/// A stash that is just a dictionary.
///
/// The reason the money tests need no server at all. It behaves the way the real bank
/// does at the boundaries that matter -- refusing a debit it cannot cover, never going
/// negative -- so a service that satisfies this one is doing the arithmetic right,
/// whatever `InventoryHelper` then does with it.
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

    /// <summary>The net of every movement. Zero means the player is exactly where they started.</summary>
    public int Moved => Movements.Sum(m => m.Amount);

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
/// Escrow without a file behind it.
///
/// Records what was taken for a spin and not yet returned. Unlike Poker's, which
/// tracks a live stack that moves every hand, there is nothing to update here: the
/// window between the debit and the credit contains no other event.
/// </summary>
public sealed class FakeEscrow : IEscrowStore
{
    private readonly Dictionary<string, OutstandingStake> _held = new();

    /// <summary>Every value ever recorded, so a test can see when it was written.</summary>
    public List<int> Recorded { get; } = [];

    public int Releases { get; private set; }

    public OutstandingStake? Get(MongoId sessionId) => _held.GetValueOrDefault(sessionId.ToString());

    public void Record(MongoId sessionId, Wallet wallet, int amount)
    {
        _held[sessionId.ToString()] = new OutstandingStake
        {
            Wallet = wallet.ToString(),
            Amount = amount,
            TakenAtUtc = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
        };

        Recorded.Add(amount);
    }

    public void Release(MongoId sessionId)
    {
        if (_held.Remove(sessionId.ToString()))
        {
            Releases++;
        }
    }

    /// <summary>
    /// Plants a stake as though a server had died holding it. What the record on disk
    /// would look like to the next session, with no table and no bets to go with it.
    /// </summary>
    public void Strand(MongoId sessionId, Wallet wallet, int amount)
    {
        _held[sessionId.ToString()] = new OutstandingStake
        {
            Wallet = wallet.ToString(),
            Amount = amount,
            TakenAtUtc = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
        };
    }
}

/// <summary>Reels that land where the test says. Seeded, so a run is repeatable.</summary>
public sealed class FakeRandom(int seed) : IRandomSource
{
    public Random Create() => new(seed);
}

/// <summary>A log that says nothing, so a test run is readable.</summary>
public sealed class QuietLog : ISlotLog
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
