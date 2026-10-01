using System.Reflection;
using SPTarkov.Common.Models.Logging;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Helpers.Server;
using SPTarkov.Server.Core.Utils;

namespace War.Server;

public record WarConfig
{
    /// <summary>
    /// Logs every request and every round. Noisy in normal play; the point of it is
    /// the first run on a new SPT build, where the interesting failures are all in code
    /// that has never executed.
    /// </summary>
    public bool VerboseLogging { get; init; } = false;
}

/// <summary>
/// Every line this table writes goes through here, prefixed so the whole of it can be
/// picked out of a busy server console with one filter on "[War]".
/// </summary>
[Injectable(InjectionType.Singleton)]
public class WarLog : IWarLog
{
    private const string Prefix = "[War]";

    private readonly ISptLogger<WarLog> _logger;

    public WarLog(ISptLogger<WarLog> logger, ModHelper modHelper, FileUtil fileUtil, JsonUtil jsonUtil)
    {
        _logger = logger;

        // Named ModFolder rather than Path: a property called Path shadows
        // System.IO.Path inside the class and breaks every Path.Combine in it.
        ModFolder = modHelper.GetAbsolutePathToModFolder(Assembly.GetExecutingAssembly());

        var configPath = System.IO.Path.Combine(ModFolder, "war.config.json");

        try
        {
            Config = fileUtil.FileExists(configPath)
                ? jsonUtil.Deserialize<WarConfig>(fileUtil.ReadFile(configPath)) ?? new WarConfig()
                : new WarConfig();
        }
        catch (Exception ex)
        {
            // A broken config must never stop the mod loading. That failure looks
            // identical to the mod being rejected by the version gate.
            Config = new WarConfig();
            _logger.Error($"{Prefix} war.config.json is unreadable, using defaults -- {ex.Message}");
        }
    }

    public string ModFolder { get; }

    public WarConfig Config { get; }

    public bool Verbose => Config.VerboseLogging;

    /// <summary>Something the reader has to see, in orange. Kept for the lines that say real money moves.</summary>
    public void Notice(string message) =>
        _logger.LogWithColor($"{Prefix} {message}", Spectre.Console.Color.Orange1);

    /// <summary>A startup line, in the next colour of the casino's shared cycle.</summary>
    public void Banner(string message) =>
        _logger.LogWithColor($"{Prefix} {message}", Casino.Server.Palette.Next());

    public void Info(string message) => _logger.Info($"{Prefix} {message}");

    void IWarLog.Error(string message) => Error(message);

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
}
