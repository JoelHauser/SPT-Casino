using System.Collections.Concurrent;
using System.Reflection;
using SPTarkov.Common.Models.Logging;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Helpers.Server;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Utils;

namespace War.Server;

/// <summary>
/// Records what the table is holding of the player's money, and hands it back after a
/// crash.
///
/// The table itself lives in memory on purpose -- a hand of cards has no business
/// surviving a restart, and keeping it out of the profile means this mod never changes
/// the profile schema. The **stake** is a different matter: once it is taken it is real
/// currency that has left the stash, and without a record on disk a server killed with
/// a tie on the table has taken it and paid nothing.
///
/// ## Slots' escrow, with one difference
///
/// A pull holds its stake for the length of one request. A war round can hold it for
/// as long as the player takes to decide what to do about a tie, across two requests,
/// and what is held changes on the way:
///
/// | When | Recorded |
/// | --- | --- |
/// | Before the deal's debit | ante + tie bet |
/// | A tie is waiting | the ante alone (the tie bet is already settled) |
/// | Before the war's debit | the ante twice |
/// | Settled | nothing |
///
/// So <see cref="Record"/> **replaces** rather than adds: each call says what the
/// table holds now, in full. Every change is written before the money it describes
/// moves out, and released only after the money owed has moved back in, so a crash at
/// any point refunds at least what is owed -- sometimes a little more, never less.
/// </summary>
[Injectable(InjectionType.Singleton)]
public class EscrowStore : IEscrowStore
{
    private const string FileName = "escrow-war.json";

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

        // Named per table, because every table shares the one mod folder. No legacy
        // import: this table never shipped as a mod of its own, so there is no older
        // file anywhere to carry over.
        _path = System.IO.Path.Combine(folder, FileName);
        _held = Load();

        if (!_held.IsEmpty)
        {
            _logger.Info(
                $"[War] {_held.Count} round(s) were interrupted with a stake taken -- "
                + "each is paid back on next contact.");
        }
    }

    public int Outstanding => _held.Count;

    public OutstandingStake? Get(MongoId sessionId) =>
        _held.TryGetValue(sessionId.ToString(), out var owed) ? owed : null;

    /// <summary>
    /// Writes down everything the table now holds for this player, replacing whatever
    /// was recorded before. See the table in the class summary for when.
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
                    _logger.Error($"[War] the outstanding stakes would not serialise -- {_path} left as it was.");
                    return;
                }

                _fileUtil.WriteFile(_path, json);
            }
            catch (Exception ex)
            {
                // Worth shouting about: a stake that cannot be recorded is a stake
                // that cannot be given back if the server goes down.
                _logger.Error($"[War] could not record the outstanding stake at {_path} -- {ex.Message}");
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
            _logger.Error($"[War] escrow file at {_path} is unreadable -- {ex.Message}");
            return new ConcurrentDictionary<string, OutstandingStake>();
        }
    }
}

/// <summary>
/// The shoe's randomness, in the server.
///
/// `Random.Shared` rather than a field: it is thread-safe, which a shared `new
/// Random()` is not, and this is a singleton reached from request threads.
/// </summary>
[Injectable(InjectionType.Singleton)]
public class RandomSource : IRandomSource
{
    public Random Create() => Random.Shared;
}
