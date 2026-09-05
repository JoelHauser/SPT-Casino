using System.Collections.Concurrent;
using System.Reflection;
using SPTarkov.Common.Models.Logging;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Helpers.Server;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Utils;

namespace Blackjack.Server;

/// <summary>
/// Lifetime stats, one record per profile, persisted to the mod's own folder.
///
/// Deliberately not stored in the SPT profile. Adding fields there would change the
/// profile schema, which is what makes some mods require a wipe when they are
/// removed -- keeping stats in a file of our own means uninstalling this mod costs
/// the player nothing but the stats themselves.
/// </summary>
[Injectable(InjectionType.Singleton)]
public class StatsStore : IStatsStore
{
    private const string FileName = "stats.json";

    private readonly ISptLogger<StatsStore> _logger;
    private readonly FileUtil _fileUtil;
    private readonly JsonUtil _jsonUtil;
    private readonly string _path;

    // Writes are serialised: a round settling on one thread while another reads
    // could otherwise persist a half-updated map.
    private readonly Lock _writeLock = new();
    private readonly ConcurrentDictionary<string, PlayerStats> _stats;

    public StatsStore(
        ISptLogger<StatsStore> logger,
        FileUtil fileUtil,
        JsonUtil jsonUtil,
        ModHelper modHelper)
    {
        _logger = logger;
        _fileUtil = fileUtil;
        _jsonUtil = jsonUtil;

        var folder = Path.Combine(modHelper.GetAbsolutePathToModFolder(Assembly.GetExecutingAssembly()), "data");
        _fileUtil.CreateDirectory(folder);
        _path = Path.Combine(folder, FileName);

        _stats = Load();

        try
        {
            _fileUtil.WriteFile(_path, _jsonUtil.Serialize(_stats, true));
            Writable = true;
        }
        catch (Exception ex)
        {
            Writable = false;
            _logger.Error($"Blackjack: stats folder is not writable at {_path} -- {ex.Message}");
        }
    }

    /// <summary>
    /// Where the record lives. Not named Path -- that would shadow System.IO.Path
    /// inside this class and break every Path.Combine above.
    /// </summary>
    public string FilePath => _path;

    /// <summary>
    /// Probed once at construction. A read-only mod folder loses the record silently
    /// otherwise -- the player would just never accumulate stats.
    /// </summary>
    public bool Writable { get; private set; }

    public PlayerStats Get(MongoId sessionId) =>
        _stats.GetOrAdd(sessionId.ToString(), _ => new PlayerStats());

    public void Save(MongoId sessionId, PlayerStats stats)
    {
        _stats[sessionId.ToString()] = stats;

        lock (_writeLock)
        {
            try
            {
                _fileUtil.WriteFile(_path, _jsonUtil.Serialize(_stats, true));
            }
            catch (Exception ex)
            {
                // Losing stats is annoying; losing the round because stats failed to
                // write would be worse. Log and carry on.
                _logger.Error($"Blackjack: could not write stats to {_path} -- {ex.Message}");
            }
        }
    }

    private ConcurrentDictionary<string, PlayerStats> Load()
    {
        if (!_fileUtil.FileExists(_path))
        {
            return new ConcurrentDictionary<string, PlayerStats>();
        }

        try
        {
            var loaded = _jsonUtil.Deserialize<Dictionary<string, PlayerStats>>(_fileUtil.ReadFile(_path));
            return new ConcurrentDictionary<string, PlayerStats>(loaded ?? []);
        }
        catch (Exception ex)
        {
            // A corrupt stats file must not stop the mod loading. Start fresh; the
            // old file is overwritten on the next settled round.
            _logger.Error($"Blackjack: stats file at {_path} is unreadable, starting fresh -- {ex.Message}");
            return new ConcurrentDictionary<string, PlayerStats>();
        }
    }
}
