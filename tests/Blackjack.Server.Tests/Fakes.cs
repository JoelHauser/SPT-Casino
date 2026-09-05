using Blackjack.Server;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.ItemEvent;

namespace Blackjack.Server.Tests;

/// <summary>
/// A wallet in memory. Records every movement so a test can assert not just the
/// final balance but that money moved the expected number of times -- a double
/// charged twice and a double charged once both end on the same balance if the
/// payout is also wrong.
/// </summary>
internal sealed class FakeBank : IBank
{
    private readonly Dictionary<Wallet, int> _balances =
        Enum.GetValues<Wallet>().ToDictionary(w => w, w => w switch
        {
            Wallet.Roubles => 1_000_000,
            Wallet.Dollars or Wallet.Euros => 10_000,
            _ => 0,
        });

    /// <summary>No stack limit in the fakes -- splitting is the real Bank's problem.</summary>
    public int MaxStackSize(Wallet wallet) => int.MaxValue;

    internal List<(Wallet Wallet, int Amount)> Debits { get; } = [];

    internal List<(Wallet Wallet, int Amount)> Credits { get; } = [];

    /// <summary>
    /// Every response instance handed to this bank. The whole reason the parameter
    /// exists is that it reaches the client, so a test can check it was not swapped
    /// for a throwaway on the way down.
    /// </summary>
    internal List<ItemEventRouterResponse> Outputs { get; } = [];

    /// <summary>Forces TryDebit to fail, simulating money vanishing mid-round.</summary>
    internal bool RefuseDebits { get; set; }

    internal void SetBalance(Wallet wallet, int amount) => _balances[wallet] = amount;

    public int GetBalance(MongoId sessionId, Wallet wallet) => _balances[wallet];

    public bool TryDebit(MongoId sessionId, Wallet wallet, int amount, ItemEventRouterResponse output)
    {
        if (RefuseDebits || amount <= 0 || _balances[wallet] < amount)
        {
            return false;
        }

        _balances[wallet] -= amount;
        Debits.Add((wallet, amount));
        Outputs.Add(output);
        return true;
    }

    public void Credit(MongoId sessionId, Wallet wallet, int amount, ItemEventRouterResponse output)
    {
        if (amount <= 0)
        {
            return;
        }

        _balances[wallet] += amount;
        Credits.Add((wallet, amount));
        Outputs.Add(output);
    }
}

internal sealed class FakeProfiles : IProfileGateway
{
    internal bool Exists { get; set; } = true;

    internal int Saves { get; private set; }

    public bool HasProfile(MongoId sessionId) => Exists;

    public Task SaveAsync(MongoId sessionId)
    {
        Saves++;
        return Task.CompletedTask;
    }
}

internal sealed class FakeStats : IStatsStore
{
    private readonly Dictionary<string, PlayerStats> _stats = [];

    internal int Saves { get; private set; }

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

internal sealed class FakeEscrow : IEscrowStore
{
    private readonly Dictionary<string, OutstandingStake> _held = [];

    public OutstandingStake? Get(MongoId sessionId) =>
        _held.TryGetValue(sessionId.ToString(), out var s) ? s : null;

    public void Hold(MongoId sessionId, Wallet wallet, int amount)
    {
        if (amount <= 0)
        {
            return;
        }

        var key = sessionId.ToString();
        if (_held.TryGetValue(key, out var existing))
        {
            existing.Amount += amount;
            return;
        }

        _held[key] = new OutstandingStake { Wallet = wallet.ToString(), Amount = amount };
    }

    public void Release(MongoId sessionId) => _held.Remove(sessionId.ToString());
}
