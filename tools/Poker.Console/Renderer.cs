using Poker.Game;

namespace Poker.Console;

/// <summary>Draws the table.</summary>
internal static class Renderer
{
    /// <summary>
    /// Shows the table as it stands.
    ///
    /// Hole cards belong to their seat until showdown. <paramref name="peek"/> lifts
    /// that, and is a debugging switch rather than a feature -- watching what a bot
    /// actually held while reading why it folded is the fastest way to tell a bad
    /// decision from a bad log line.
    /// </summary>
    public static void Table(HoldemTable table, bool peek)
    {
        var over = table.Street == HoldemStreet.Showdown;

        // A hand won because everybody folded is never shown, and the engine says so
        // by leaving `Hand` unset on seats that did not reach a showdown. Keying the
        // reveal off the *street* instead leaks the winner's cards on every hand that
        // ended early -- which this printed for two hands before anyone noticed.
        //
        // The server's view will have exactly the same choice to make.
        var showdown = over && table.Seats.Any(seat => seat.Hand is not null);

        // Settlement clears the commitments, so the pot reads zero once the hand is
        // over. What was actually played for is what went out to the winners.
        var pot = over ? table.Seats.Sum(seat => seat.Won) : table.Pot;

        System.Console.WriteLine();
        System.Console.WriteLine(
            $"  {(over ? showdown ? "Showdown" : "Won without a showdown" : table.Street.ToString())}"
            + $"   pot {pot:N0}   board {Board(table)}");

        foreach (var seat in table.Seats)
        {
            var marks = new List<string>();

            if (seat.Index == table.Button)
            {
                marks.Add("D");
            }

            if (seat.Folded)
            {
                marks.Add("folded");
            }
            else if (seat.IsAllIn)
            {
                marks.Add("all-in");
            }

            var turn = table.Actor?.Index == seat.Index ? ">" : " ";
            // Shown only to the seat itself, at a genuine showdown, or under --peek.
            var seen = seat.IsPlayer || peek || seat.Hand is not null;

            var cards = seen
                ? string.Join(" ", seat.Cards)
                : seat.Folded ? "--" : "?? ??";

            var hand = seat.Hand is { } rank ? $"  {rank.Describe()}" : string.Empty;
            var bet = seat.CommittedThisStreet > 0 ? $"  bet {seat.CommittedThisStreet:N0}" : string.Empty;
            var won = over && seat.Won > 0 ? $"  won {seat.Won:N0}" : string.Empty;

            System.Console.WriteLine(
                $" {turn}{seat.Name,-10} {seat.Stack,8:N0}  {cards,-7}"
                + $"{bet,-14}{string.Join(" ", marks),-8}{hand}{won}");
        }

        System.Console.WriteLine();
    }

    private static string Board(HoldemTable table) =>
        table.Community.Count == 0 ? "--" : string.Join(" ", table.Community);

    /// <summary>The prompt: what can be done, and for how much.</summary>
    public static void Options(BettingOptions options)
    {
        var parts = new List<string>();

        foreach (var move in options.Moves)
        {
            parts.Add(move switch
            {
                HoldemMove.Fold => "[f]old",
                HoldemMove.Check => "[k] check",
                HoldemMove.Call => $"[c]all {options.ToCall:N0}",
                _ => $"[r]aise {options.MinRaiseTo:N0}-{options.MaxRaiseTo:N0}   [a]ll-in {options.MaxRaiseTo:N0}",
            });
        }

        System.Console.WriteLine($"  {string.Join("   ", parts)}");
    }

    public static void Result(HoldemTable table)
    {
        var net = table.Player.Net;

        System.Console.WriteLine(net switch
        {
            > 0 => $"  you win {net:N0}",
            < 0 => $"  you lose {-net:N0}",
            _ => "  you break even",
        });
    }
}
