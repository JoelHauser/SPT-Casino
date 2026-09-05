using System.Collections.Concurrent;
using System.Reflection;
using SPTarkov.Common.Models.Logging;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Helpers.Server;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Utils;

namespace Blackjack.Server;

/// <summary>A stake taken from a player whose round has not settled.</summary>
public class OutstandingStake
{
    public string Wallet { get; set; } = nameof(Server.Wallet.Roubles);

    public int Amount { get; set; }

    public long TakenAtUtc { get; set; }
}

/// <summary>
/// Records money that has left the player but not yet been settled.
///
/// The table itself is in memory on purpose -- a half-played hand should not survive
/// a restart. The stake is a different matter: it is debited from the profile and
/// written to disk immediately, so without this a crash mid-hand takes the player's
/// money and leaves no hand to win it back with. Anything found outstanding is
/// refunded the next time that player is seen.
///
/// The same record covers items held in a bet container once valuables are staked
/// through EFT's grid, which has exactly this failure mode.
/// </summary>
[Injectable(InjectionType.Singleton)]
public class EscrowStore : IEscrowStore
{
    private const string FileName = "escrow.json";

    private readonly ISptLogger<EscrowStore> _logger;
    private readonly FileUtil _fileUtil;
    private readonly JsonUtil _jsonUtil;
    private readonly string _path;
    private readonly Lock _writeLock = new();
    private readonly ConcurrentDictionary<string, OutstandingStake> _held;

    public EscrowStore(
        ISptLogger<EscrowStore> logger,
        FileUtil fileUtil,
        JsonUtil jsonUtil,
        ModHelper modHelper)
    {
        _logger = logger;
        _fileUtil = fileUtil;
        _jsonUtil = jsonUtil;

        var folder = System.IO.Path.Combine(
            modHelper.GetAbsolutePathToModFolder(Assembly.GetExecutingAssembly()),
            "data");

        _fileUtil.CreateDirectory(folder);
        _path = System.IO.Path.Combine(folder, FileName);
        _held = Load();

        if (!_held.IsEmpty)
        {
            _logger.Info($"Blackjack: {_held.Count} unsettled stake(s) carried over -- each is refunded on next contact.");
        }
    }

    public int Outstanding => _held.Count;

    public OutstandingStake? Get(MongoId sessionId) =>
        _held.TryGetValue(sessionId.ToString(), out var stake) ? stake : null;

    /// <summary>
    /// Adds to whatever is already held. Doubling and splitting raise the stake after
    /// the fact, so this accumulates rather than replaces.
    /// </summary>
    public void Hold(MongoId sessionId, Wallet wallet, int amount)
    {
        if (amount <= 0)
        {
            return;
        }

        var key = sessionId.ToString();
        _held.AddOrUpdate(
            key,
            _ => new OutstandingStake
            {
                Wallet = wallet.ToString(),
                Amount = amount,
                TakenAtUtc = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            },
            (_, existing) =>
            {
                existing.Amount += amount;
                return existing;
            });

        Flush();
    }

    public void Release(MongoId sessionId)
    {
        if (_held.TryRemove(sessionId.ToString(), out _))
        {
            Flush();
        }
    }

    private void Flush()
    {
        lock (_writeLock)
        {
            try
            {
                _fileUtil.WriteFile(_path, _jsonUtil.Serialize(_held, true));
            }
            catch (Exception ex)
            {
                // Worth shouting about: a stake that cannot be recorded is a stake that
                // cannot be refunded if the server goes down before the hand ends.
                _logger.Error($"Blackjack: could not record the outstanding stake at {_path} -- {ex.Message}");
            }
        }
    }

    private ConcurrentDictionary<string, OutstandingStake> Load()
    {
        if (!_fileUtil.FileExists(_path))
        {
            return new ConcurrentDictionary<string, OutstandingStake>();
        }

        try
        {
            var loaded = _jsonUtil.Deserialize<Dictionary<string, OutstandingStake>>(_fileUtil.ReadFile(_path));
            return new ConcurrentDictionary<string, OutstandingStake>(loaded ?? []);
        }
        catch (Exception ex)
        {
            _logger.Error($"Blackjack: escrow file at {_path} is unreadable -- {ex.Message}");
            return new ConcurrentDictionary<string, OutstandingStake>();
        }
    }
}
