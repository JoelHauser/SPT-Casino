using SPTarkov.Server.Core.Models.Utils;
using SPTarkov.DI.Annotations;

namespace Casino.Server;

/// <summary>
/// The casino's own logging, as an interface so the gift can be tested without a
/// server around it -- the same arrangement, and the same two methods, as each table's
/// <c>ISlotLog</c> and its siblings.
///
/// No Detail here. The tables have one because a table logs every bet, every card and
/// every spin, and that needs a switch to turn off. The gift happens once per profile
/// and says so once.
/// </summary>
public interface ICasinoLog
{
    void Info(string message);

    void Error(string message);
}

/// <summary>
/// Prefixed so the whole of it can be picked out of a busy server console with one
/// filter on "[Casino]", exactly like the four tables.
/// </summary>
[Injectable(InjectionType.Singleton)]
public class CasinoLog(ISptLogger<CasinoLog> logger) : ICasinoLog
{
    private const string Prefix = "[Casino]";

    public void Info(string message) => logger.Info($"{Prefix} {message}");

    public void Error(string message) => logger.Error($"{Prefix} {message}");
}
