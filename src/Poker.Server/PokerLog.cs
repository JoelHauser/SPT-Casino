using System.Reflection;
using SPTarkov.Common.Models.Logging;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Helpers.Server;
using SPTarkov.Server.Core.Utils;

namespace Poker.Server;

public record PokerConfig
{
    /// <summary>
    /// Logs every request, every card dealt and every decision a bot makes. Noisy in
    /// normal play; the point of it is the first run on a new SPT build, where the
    /// interesting failures are all in code that has never executed.
    /// </summary>
    public bool VerboseLogging { get; init; } = true;
}

/// <summary>
/// Every line this mod writes goes through here, prefixed so the whole of it can be
/// picked out of a busy server console with one filter on "[Poker]".
/// </summary>
[Injectable(InjectionType.Singleton)]
public class PokerLog : IPokerLog
{
    private const string Prefix = "[Poker]";

    private readonly ISptLogger<PokerLog> _logger;

    public PokerLog(ISptLogger<PokerLog> logger, ModHelper modHelper, FileUtil fileUtil, JsonUtil jsonUtil)
    {
        _logger = logger;

        // Named ModFolder rather than Path: a property called Path shadows
        // System.IO.Path inside the class and breaks every Path.Combine in it.
        ModFolder = modHelper.GetAbsolutePathToModFolder(Assembly.GetExecutingAssembly());

        var configPath = System.IO.Path.Combine(ModFolder, "config.json");

        try
        {
            Config = fileUtil.FileExists(configPath)
                ? jsonUtil.Deserialize<PokerConfig>(fileUtil.ReadFile(configPath)) ?? new PokerConfig()
                : new PokerConfig();
        }
        catch (Exception ex)
        {
            // A broken config must never stop the mod loading. That failure looks
            // identical to the mod being rejected by the version gate, which is the
            // one thing this logging exists to tell apart.
            Config = new PokerConfig();
            _logger.Error($"{Prefix} config.json is unreadable, using defaults -- {ex.Message}");
        }
    }

    public string ModFolder { get; }

    public PokerConfig Config { get; }

    public bool Verbose => Config.VerboseLogging;

    public void Success(string message) => _logger.Success($"{Prefix} {message}");

    public void Info(string message) => _logger.Info($"{Prefix} {message}");

    void IPokerLog.Error(string message) => Error(message);

    public void Error(string message, Exception? ex = null) =>
        _logger.Error($"{Prefix} {message}{(ex is null ? string.Empty : $" -- {ex}")}");

    /// <summary>Only when verbose logging is on.</summary>
    public void Detail(string message)
    {
        if (Verbose)
        {
            _logger.Info($"{Prefix} {message}");
        }
    }

    /// <summary>
    /// Bridges the engine's own log into this one, so a hand's reasoning lands in the
    /// server console alongside everything else. Off entirely unless verbose, because
    /// the engine writes a line per bot decision and several per hand.
    /// </summary>
    public Poker.Game.IGameLog ForEngine() =>
        Verbose ? new Poker.Game.DelegateGameLog(line => Detail(line)) : Poker.Game.GameLog.Null;
}
