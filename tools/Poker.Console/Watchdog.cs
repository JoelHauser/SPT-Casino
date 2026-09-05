using Poker.Game;

namespace Poker.Console;

/// <summary>
/// The reason this tool exists.
///
/// It is the engine's log sink and its invariant checker at once. Every line the
/// engine writes is kept for the current hand, and the table is checked after every
/// single action -- so when something goes wrong the complete story of the hand it
/// went wrong in is already in hand, rather than having to be reproduced.
///
/// Checking after every action rather than at the end of a hand is deliberate. A
/// betting-round bug shows up as an impossible intermediate state -- an actor who has
/// folded, a minimum raise above the maximum, a stack gone negative -- and by the time
/// the hand settles the evidence has usually been tidied away by the next street.
/// </summary>
internal sealed class Watchdog(bool verbose) : IGameLog
{
    private readonly List<string> _transcript = [];

    /// <summary>
    /// Always on. Even in quiet mode every line is captured, because the only run
    /// worth having a transcript of is the one that just failed -- and by then it is
    /// too late to turn logging on.
    /// </summary>
    public bool Enabled => true;

    public int HandNumber { get; private set; }

    public List<string> Failures { get; } = [];

    /// <summary>What the table should hold. Rises only when somebody buys back in.</summary>
    public int ExpectedChips { get; private set; }

    /// <summary>
    /// The largest pot seen. Sampled here rather than after the hand, because
    /// settlement clears the commitments -- read it afterwards and every pot is zero.
    /// </summary>
    public int BiggestPot { get; private set; }

    public void Write(string message)
    {
        _transcript.Add(message);

        if (verbose)
        {
            System.Console.WriteLine($"    {message}");
        }
    }

    public void Seat(int chips) => ExpectedChips = chips;

    /// <summary>A busted seat buying back in creates chips, and the books must know.</summary>
    public void ToppedUp(int chips) => ExpectedChips += chips;

    public void BeginHand(int number)
    {
        HandNumber = number;
        _transcript.Clear();
    }

    /// <summary>
    /// Checks everything that must be true of a table at rest between actions.
    ///
    /// Each of these is cheap and each of them has a failure mode that is otherwise
    /// silent -- a chip quietly created, a seat asked to act after folding, a raise
    /// range that cannot be satisfied.
    /// </summary>
    public bool Check(HoldemTable table, string when)
    {
        BiggestPot = Math.Max(BiggestPot, table.Pot);

        var problems = new List<string>();

        if (table.ChipsInPlay != ExpectedChips)
        {
            problems.Add(
                $"chips in play are {table.ChipsInPlay} but should be {ExpectedChips} "
                + $"({table.ChipsInPlay - ExpectedChips:+#;-#;0})");
        }

        foreach (var seat in table.Seats)
        {
            if (seat.Stack < 0)
            {
                problems.Add($"{seat.Name} has a stack of {seat.Stack}");
            }

            if (seat.CommittedThisStreet > seat.CommittedThisHand)
            {
                problems.Add(
                    $"{seat.Name} has {seat.CommittedThisStreet} in this street "
                    + $"but only {seat.CommittedThisHand} in the hand");
            }

            if (seat.CommittedThisHand < 0 || seat.CommittedThisStreet < 0)
            {
                problems.Add($"{seat.Name} has committed a negative amount");
            }
        }

        if (table.Pot != table.Seats.Sum(seat => seat.CommittedThisHand))
        {
            problems.Add($"the pot says {table.Pot} but the seats add up to something else");
        }

        var expectedBoard = table.Street switch
        {
            HoldemStreet.PreFlop => 0,
            HoldemStreet.Flop => 3,
            HoldemStreet.Turn => 4,
            HoldemStreet.River => 5,
            _ => -1,
        };

        if (expectedBoard >= 0 && table.Community.Count != expectedBoard)
        {
            problems.Add($"the {table.Street} is showing {table.Community.Count} cards, not {expectedBoard}");
        }

        if (table.Actor is { } actor)
        {
            if (actor.Folded)
            {
                problems.Add($"{actor.Name} has folded and is being asked to act");
            }

            if (actor.Stack <= 0)
            {
                problems.Add($"{actor.Name} is all-in and is being asked to act");
            }

            var options = table.Options();

            if (options.Moves.Count == 0)
            {
                problems.Add($"{actor.Name} has no legal move");
            }

            if (options.ToCall < 0)
            {
                problems.Add($"{actor.Name} is asked for {options.ToCall}");
            }

            if (options.ToCall > actor.Stack)
            {
                problems.Add($"{actor.Name} is asked for {options.ToCall} with {actor.Stack} behind");
            }

            if (options.Moves.Contains(HoldemMove.Raise) && options.MinRaiseTo > options.MaxRaiseTo)
            {
                problems.Add(
                    $"{actor.Name} may raise to between {options.MinRaiseTo} and {options.MaxRaiseTo}, "
                    + "which is not a range");
            }

            if (options.Moves.Contains(HoldemMove.Check) && options.Moves.Contains(HoldemMove.Call))
            {
                problems.Add($"{actor.Name} is offered both check and call");
            }

            if (options.ToCall == 0 && options.Moves.Contains(HoldemMove.Call))
            {
                problems.Add($"{actor.Name} is offered a call with nothing to call");
            }
        }

        if (problems.Count == 0)
        {
            return true;
        }

        Report(when, problems);
        return false;
    }

    private void Report(string when, IReadOnlyList<string> problems)
    {
        Failures.AddRange(problems.Select(problem => $"hand {HandNumber}: {problem}"));

        System.Console.WriteLine();
        System.Console.WriteLine($"!! INVARIANT BROKEN {when} (hand {HandNumber})");

        foreach (var problem in problems)
        {
            System.Console.WriteLine($"!!   {problem}");
        }

        System.Console.WriteLine("!! the hand, from the top:");

        foreach (var line in _transcript)
        {
            System.Console.WriteLine($"!!   {line}");
        }

        System.Console.WriteLine();
    }

    /// <summary>Prints the current hand on demand, which is what the `l` key is for.</summary>
    public void Dump()
    {
        System.Console.WriteLine($"  -- hand {HandNumber}, {_transcript.Count} lines --");

        foreach (var line in _transcript)
        {
            System.Console.WriteLine($"  {line}");
        }
    }
}
