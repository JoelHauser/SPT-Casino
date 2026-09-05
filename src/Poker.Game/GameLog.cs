namespace Poker.Game;

/// <summary>
/// Where the engine writes down what it decided.
///
/// The engine deliberately has no SPT reference and no I/O, so it cannot hold a
/// logger -- it holds a sink, and the caller decides what that sink does. The
/// server passes one that writes through its own log, the console tool one that
/// writes to the terminal, and a test passes <see cref="ListGameLog"/> and then
/// asserts on what the engine said.
///
/// That last use is the point. A test over a return value pins what the engine
/// concluded; a test over the log pins *why*, which is what catches a right answer
/// reached by the wrong route -- a pot that balances with two layers swapped, or a
/// settlement that totals correctly with the Blind and the Play the wrong way round.
/// </summary>
public interface IGameLog
{
    /// <summary>
    /// Whether anything is listening.
    ///
    /// Every call site guards on this before building its message, and that is not
    /// premature. HandDistributionTests ranks all 2,598,960 five-card hands;
    /// formatting one line per hand and discarding it turns a one-second test into
    /// minutes. Interpolated strings are built before the call, so only an explicit
    /// check avoids the cost.
    /// </summary>
    bool Enabled { get; }

    void Write(string message);
}

/// <summary>Sinks that do not need a class of their own.</summary>
public static class GameLog
{
    /// <summary>
    /// The default everywhere. Reports itself disabled, so a guarded call site does
    /// no work at all rather than formatting a message and dropping it.
    /// </summary>
    public static IGameLog Null { get; } = new NullLog();

    /// <summary>Wraps anything that takes a string -- Console.WriteLine, a server logger.</summary>
    public static IGameLog To(Action<string> sink) => new DelegateGameLog(sink);

    private sealed class NullLog : IGameLog
    {
        public bool Enabled => false;

        public void Write(string message)
        {
        }
    }
}

/// <summary>
/// Collects lines in memory. The test seam, and what the console tool uses when it
/// wants to print a hand's reasoning after the hand rather than during it.
/// </summary>
public sealed class ListGameLog : IGameLog
{
    private readonly List<string> _lines = [];

    public bool Enabled => true;

    public IReadOnlyList<string> Lines => _lines;

    public void Write(string message) => _lines.Add(message);

    /// <summary>
    /// True when some line contains this text. Case-insensitive and substring-based
    /// on purpose: a test should pin the decision that was recorded, not the exact
    /// wording, or every reworded message becomes a failing test.
    /// </summary>
    public bool Mentions(string fragment) =>
        _lines.Any(line => line.Contains(fragment, StringComparison.OrdinalIgnoreCase));

    /// <summary>Every line, one per row -- an xunit failure message worth reading.</summary>
    public override string ToString() => string.Join(Environment.NewLine, _lines);
}

/// <summary>Adapter for a sink that is already a method somewhere else.</summary>
public sealed class DelegateGameLog(Action<string> sink, bool enabled = true) : IGameLog
{
    public bool Enabled { get; } = enabled;

    public void Write(string message) => sink(message);
}
