using System.Collections.Concurrent;
using System.Reflection;
using SPTarkov.Common.Models.Logging;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Helpers.Server;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Utils;

namespace Roulette.Server;

/// <summary>
/// Records the stake the table is holding, and hands it back after a crash.
///
/// The table itself lives in memory on purpose -- a cloth with chips on it has no
/// business surviving a restart, and keeping it out of the profile means this mod
/// never changes the profile schema. The **stake** is a different matter: once the
/// wheel turns it is real currency that has already left the stash, and without a
/// record on disk a server killed mid-spin has taken it and paid nothing, with no way
/// for the next session to know it ever happened.
///
/// ## Blackjack's escrow, not Poker's
///
/// Poker takes one buy-in and hands the player a live stack that moves every hand, so
/// what it records has to move with it -- recording the buy-in and stopping would
/// refund a player who had lost most of it and rob one who had doubled up. None of
/// that applies here. The money is out of the wallet only between the debit and the
/// credit of a single spin, and **nothing happens in that window**, so what is owed
/// cannot drift from what was taken.
///
/// That makes this the simplest escrow of the three, and the shortest-lived. It is
/// still not optional: short is not zero, and it is the only window in this mod where
/// the player's money exists nowhere at all.
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

        // Named folder rather than path: a local called `path` is fine, but a
        // *property* named Path shadows System.IO.Path inside the class and breaks
        // every Path.Combine in it.
        var folder = System.IO.Path.Combine(
            modHelper.GetAbsolutePathToModFolder(Assembly.GetExecutingAssembly()),
            "data");

        _fileUtil.CreateDirectory(folder);
        _path = System.IO.Path.Combine(folder, FileName);
        _held = Load();

        if (!_held.IsEmpty)
        {
            _logger.Info(
                $"[Roulette] {_held.Count} spin(s) were interrupted with a stake taken -- "
                + "each is paid back on next contact.");
        }
    }

    public int Outstanding => _held.Count;

    public OutstandingStake? Get(MongoId sessionId) =>
        _held.TryGetValue(sessionId.ToString(), out var owed) ? owed : null;

    /// <summary>
    /// Writes down what has been taken for the spin about to happen.
    ///
    /// Called once, immediately before the debit. There is deliberately no accumulate
    /// here and no update: a second spin cannot start while one is in flight, so a
    /// record already present when this is called would mean the last spin never
    /// finished -- and overwriting it would lose the stake it was holding. That is
    /// why <see cref="RouletteService"/> refunds before it records.
    /// </summary>
    public void Record(MongoId sessionId, Wallet wallet, int amount)
    {
        if (amount < 0)
        {
            amount = 0;
        }

        _held[sessionId.ToString()] = new OutstandingStake
        {
            Wallet = wallet.ToString(),
            Amount = amount,
            TakenAtUtc = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
        };

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
                var json = _jsonUtil.Serialize(_held, true);

                if (json is null)
                {
                    // Writing nothing would truncate the file and lose every stake it
                    // was holding, which is worse than failing to write at all.
                    _logger.Error($"[Roulette] the outstanding stakes would not serialise -- {_path} left as it was.");
                    return;
                }

                _fileUtil.WriteFile(_path, json);
            }
            catch (Exception ex)
            {
                // Worth shouting about: a stake that cannot be recorded is a stake
                // that cannot be given back if the server goes down.
                _logger.Error($"[Roulette] could not record the outstanding stake at {_path} -- {ex.Message}");
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
            _logger.Error($"[Roulette] escrow file at {_path} is unreadable -- {ex.Message}");
            return new ConcurrentDictionary<string, OutstandingStake>();
        }
    }
}

/// <summary>
/// The wheel's randomness, in the server.
///
/// `Random.Shared` rather than a field: it is thread-safe, which a shared `new
/// Random()` is not, and this is a singleton reached from request threads.
/// </summary>
[Injectable(InjectionType.Singleton)]
public class RandomSource : IRandomSource
{
    public Random Create() => Random.Shared;
}
