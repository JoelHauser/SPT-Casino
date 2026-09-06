using System.Reflection;
using SPTarkov.Common.Models.Logging;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Helpers.Server;
using SPTarkov.Server.Core.Utils;

namespace Roulette.Server;

public record RouletteConfig
{
    /// <summary>
    /// Logs every request, every bet placed and every spin. Noisy in
    /// normal play; the point of it is the first run on a new SPT build, where the
    /// interesting failures are all in code that has never executed.
    /// </summary>
    public bool VerboseLogging { get; init; } = true;
}

/// <summary>
/// Every line this mod writes goes through here, prefixed so the whole of it can be
/// picked out of a busy server console with one filter on "[Roulette]".
/// </summary>
[Injectable(InjectionType.Singleton)]
public class RouletteLog : IRouletteLog
{
    private const string Prefix = "[Roulette]";

    private readonly ISptLogger<RouletteLog> _logger;

    public RouletteLog(ISptLogger<RouletteLog> logger, ModHelper modHelper, FileUtil fileUtil, JsonUtil jsonUtil)
    {
        _logger = logger;

        // Named ModFolder rather than Path: a property called Path shadows
        // System.IO.Path inside the class and breaks every Path.Combine in it.
        ModFolder = modHelper.GetAbsolutePathToModFolder(Assembly.GetExecutingAssembly());

        var configPath = System.IO.Path.Combine(ModFolder, "roulette.config.json");

        try
        {
            Config = fileUtil.FileExists(configPath)
                ? jsonUtil.Deserialize<RouletteConfig>(fileUtil.ReadFile(configPath)) ?? new RouletteConfig()
                : new RouletteConfig();
        }
        catch (Exception ex)
        {
            // A broken config must never stop the mod loading. That failure looks
            // identical to the mod being rejected by the version gate, which is the
            // one thing this logging exists to tell apart.
            Config = new RouletteConfig();
            _logger.Error($"{Prefix} roulette.config.json is unreadable, using defaults -- {ex.Message}");
        }
    }

    public string ModFolder { get; }

    public RouletteConfig Config { get; }

    public bool Verbose => Config.VerboseLogging;

    /// <summary>
    /// The headline line, in the casino's gold.
    ///
    /// `ISptLogger` has `LogWithColor(data, textColor, backgroundColor, ex)` taking a
    /// `Spectre.Console.Color`, which is what the mods with colour in their startup
    /// block are using. It comes through the SPT package already, so there is nothing
    /// to reference.
    ///
    /// All three tables use the same gold on purpose. They print three blocks in a row
    /// and they are one mod; three different colours would say otherwise.
    /// </summary>
    public void Success(string message) =>
        _logger.LogWithColor($"{Prefix} {message}", Spectre.Console.Color.Gold1);

    /// <summary>
    /// Something the reader has to see, in orange.
    ///
    /// Kept for the handful of lines that say real money moves. Those were the same
    /// grey as the route list, which is the wrong weight for the one thing in the block
    /// somebody could be surprised by later.
    /// </summary>
    public void Notice(string message) =>
        _logger.LogWithColor($"{Prefix} {message}", Spectre.Console.Color.Orange1);

    /// <summary>
    /// A startup line, in the next colour along.
    ///
    /// The cycle is shared across all three tables -- see
    /// <see cref="Casino.Server.Palette"/> -- so the block reads as one run of colour
    /// rather than three that each start over.
    /// </summary>
    public void Banner(string message) =>
        _logger.LogWithColor($"{Prefix} {message}", Casino.Server.Palette.Next());

    public void Info(string message) => _logger.Info($"{Prefix} {message}");

    void IRouletteLog.Error(string message) => Error(message);

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
    /// Bridges the engine's own log into this one, so a spin's reasoning lands in the
    /// server console alongside everything else. Off entirely unless verbose.
    /// </summary>
    public Roulette.Game.IGameLog ForEngine() =>
        Verbose ? new Roulette.Game.DelegateGameLog(line => Detail(line)) : Roulette.Game.GameLog.Null;
}
