using System.Collections.Concurrent;
using System.Reflection;
using SPTarkov.Common.Models.Logging;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Helpers.Server;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Utils;

namespace Poker.Server;

/// <summary>
/// Records the chips the table owes a player, and hands them back after a crash.
///
/// The table itself lives in memory on purpose -- a half-played hand has no business
/// surviving a restart. The **stack** is a different matter entirely: it was bought
/// with real currency that has already left the stash, so without this a crash
/// mid-session takes the buy-in and leaves nothing behind.
///
/// This is where hold'em departs from Blackjack, whose escrow held a *stake* until a
/// hand settled and then dropped it. Here one buy-in is taken and the player then
/// holds a number that moves every hand, so what is recorded has to move with it. A
/// file that remembered only the buy-in would refund a player who had lost most of it
/// and rob one who had doubled up -- both silently, and both looking like a payout
/// bug rather than a bookkeeping one.
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
    private readonly ConcurrentDictionary<string, OutstandingStack> _held;

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
                $"[Poker] {_held.Count} unfinished session(s) carried over -- "
                + "each is paid back on next contact.");
        }
    }

    public int Outstanding => _held.Count;

    public OutstandingStack? Get(MongoId sessionId) =>
        _held.TryGetValue(sessionId.ToString(), out var owed) ? owed : null;

    /// <summary>
    /// Writes down the stack as it stands, replacing whatever was there.
    ///
    /// Replaces rather than accumulates, which is the opposite of Blackjack's escrow
    /// and is the whole point: this is a running total of what is owed, not a tally of
    /// what has been taken.
    /// </summary>
    public void Record(MongoId sessionId, Wallet wallet, int chips)
    {
        if (chips < 0)
        {
            chips = 0;
        }

        var key = sessionId.ToString();

        _held.AddOrUpdate(
            key,
            _ => new OutstandingStack
            {
                Wallet = wallet.ToString(),
                Chips = chips,
                SatDownAtUtc = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            },
            (_, existing) =>
            {
                existing.Wallet = wallet.ToString();
                existing.Chips = chips;
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
                var json = _jsonUtil.Serialize(_held, true);

                if (json is null)
                {
                    // Writing nothing would truncate the file and lose every stack it
                    // was holding, which is worse than failing to write at all.
                    _logger.Error($"[Poker] the outstanding stacks would not serialise -- {_path} left as it was.");
                    return;
                }

                _fileUtil.WriteFile(_path, json);
            }
            catch (Exception ex)
            {
                // Worth shouting about: a stack that cannot be recorded is a stack
                // that cannot be given back if the server goes down.
                _logger.Error($"[Poker] could not record the outstanding stack at {_path} -- {ex.Message}");
            }
        }
    }

    private ConcurrentDictionary<string, OutstandingStack> Load()
    {
        if (!_fileUtil.FileExists(_path))
        {
            return new ConcurrentDictionary<string, OutstandingStack>();
        }

        try
        {
            var loaded = _jsonUtil.Deserialize<Dictionary<string, OutstandingStack>>(_fileUtil.ReadFile(_path));
            return new ConcurrentDictionary<string, OutstandingStack>(loaded ?? []);
        }
        catch (Exception ex)
        {
            _logger.Error($"[Poker] escrow file at {_path} is unreadable -- {ex.Message}");
            return new ConcurrentDictionary<string, OutstandingStack>();
        }
    }
}
