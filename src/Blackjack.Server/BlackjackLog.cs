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

        var path = Path.Combine(folder, "blackjack.config.json");
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
            _logger.Error($"{Prefix} blackjack.config.json is unreadable, using defaults -- {ex.Message}");
        }
    }

    public BlackjackConfig Config { get; }

    public string ModFolder { get; }

    public bool Verbose => Config.VerboseLogging;

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
