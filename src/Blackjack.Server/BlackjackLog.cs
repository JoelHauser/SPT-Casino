using System.Reflection;
using SPTarkov.Common.Models.Logging;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Helpers.Server;
using SPTarkov.Server.Core.Utils;

namespace Blackjack.Server;

public record BlackjackConfig
{
    /// <summary>
    /// Logs every request, every card dealt and every rouble moved. Expensive and
    /// noisy in normal play; the point of it is the first run on a new SPT build,
    /// where the interesting failures are all in code that has never executed.
    /// </summary>
    public bool VerboseLogging { get; init; } = true;
}

/// <summary>
/// Every line this mod writes goes through here, prefixed so the whole of it can be
/// picked out of a busy server console with a single filter on "[Blackjack]".
/// </summary>
[Injectable(InjectionType.Singleton)]
public class BlackjackLog
{
    private const string Prefix = "[Blackjack]";

    private readonly ISptLogger<BlackjackLog> _logger;

    public BlackjackLog(ISptLogger<BlackjackLog> logger, ModHelper modHelper, FileUtil fileUtil, JsonUtil jsonUtil)
    {
        _logger = logger;

        var folder = modHelper.GetAbsolutePathToModFolder(Assembly.GetExecutingAssembly());
        ModFolder = folder;

        var path = Path.Combine(folder, "config.json");
        try
        {
            Config = fileUtil.FileExists(path)
                ? jsonUtil.Deserialize<BlackjackConfig>(fileUtil.ReadFile(path)) ?? new BlackjackConfig()
                : new BlackjackConfig();
        }
        catch (Exception ex)
        {
            // A broken config must never stop the mod loading -- it would look
            // identical to the mod being rejected outright, which is the one failure
            // this logging exists to tell apart.
            Config = new BlackjackConfig();
            _logger.Error($"{Prefix} config.json is unreadable, using defaults -- {ex.Message}");
        }
    }

    public BlackjackConfig Config { get; }

    public string ModFolder { get; }

    public bool Verbose => Config.VerboseLogging;

    public void Info(string message) => _logger.Info($"{Prefix} {message}");

    public void Success(string message) => _logger.Success($"{Prefix} {message}");

    public void Error(string message, Exception? ex = null) =>
        _logger.Error($"{Prefix} {message}{(ex is null ? "" : $" -- {ex.GetType().Name}: {ex.Message}")}");

    /// <summary>Only written when verbose logging is on.</summary>
    public void Detail(string message)
    {
        if (Verbose)
        {
            _logger.Info($"{Prefix} {message}");
        }
    }
}
