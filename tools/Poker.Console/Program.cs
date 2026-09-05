using Poker.Console;
using Poker.Game;

// A terminal table for playing the engine with no SPT attached, and for pointing it
// at itself for a few thousand hands to see what falls out.
//
// Two modes and one purpose. Interactive is how the bots get judged -- no amount of
// test assertion tells you whether a maniac is fun to sit next to. Soak runs the same
// table with a bot in every seat and checks the invariants after every action, which
// is how the betting round gets found out.

var options = Args.Parse(args);

if (options.Help)
{
    Args.Usage();
    return 0;
}

var rules = new HoldemRules
{
    SmallBlind = options.SmallBlind,
    BigBlind = options.BigBlind,
    BuyIn = options.BuyIn,
};

var rng = new Random(options.Seed);
var watchdog = new Watchdog(options.Verbose);

// Every seat gets its own character, improvised rather than picked off the list, so
// no two tables are quite alike. The seed is printed so any hand can be got back.
var cast = Enumerable.Range(0, options.Seats - 1)
    .Select(_ => PokerPersonality.Improvise(rng))
    .ToList();

var agents = cast
    .Select((personality, index) =>
        (IPokerAgent)new BotAgent(personality, new Random(options.Seed + index + 1), watchdog, options.Samples))
    .ToList();

var table = new HoldemTable(rules, options.Seats, rng, watchdog, agents);
watchdog.Seat(table.ChipsInPlay);

Console.WriteLine($"Poker -- engine harness    seed {options.Seed}");
Console.WriteLine(
    $"{options.Seats} seats, blinds {rules.SmallBlind:N0}/{rules.BigBlind:N0}, "
    + $"buy-in {rules.BuyIn:N0} ({rules.BuyIn / rules.BigBlind} big blinds)");

for (var index = 0; index < cast.Count; index++)
{
    Console.WriteLine($"  seat {index + 1}: {cast[index]}");
}

Console.WriteLine();

return options.Soak > 0 ? Soak() : Play();

// ---------------------------------------------------------------- interactive

int Play()
{
    Console.WriteLine("[enter] deal   f fold   k check   c call   r <n> raise   a all-in");
    Console.WriteLine("l print this hand's log   q quit");

    var hand = 0;

    while (true)
    {
        if (Reseat() < 0)
        {
            Console.WriteLine("You are out of chips. That is the game.");
            return 0;
        }

        Console.Write($"\nhand {hand + 1}   your stack {table.Player.Stack:N0}  > ");
        var key = Console.ReadLine();

        if (key is null or "q")
        {
            break;
        }

        watchdog.BeginHand(++hand);
        table.StartHand();
        watchdog.Check(table, "after the deal");

        while (table.AwaitingPlayer)
        {
            Renderer.Table(table, options.Peek);
            Renderer.Options(table.Options());

            if (!TakeTurn())
            {
                return 0;
            }

            watchdog.Check(table, "after your move");
        }

        Renderer.Table(table, options.Peek);
        Renderer.Result(table);
        watchdog.Check(table, "at the end of the hand");
    }

    Report();
    return watchdog.Failures.Count == 0 ? 0 : 1;
}

/// Reads one decision. Returns false only if the player wants out.
bool TakeTurn()
{
    while (true)
    {
        Console.Write("  > ");
        var input = Console.ReadLine()?.Trim().ToLowerInvariant() ?? "q";

        if (input is "q")
        {
            return false;
        }

        if (input is "l")
        {
            watchdog.Dump();
            continue;
        }

        var current = table.Options();
        var word = input.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var verb = word.Length > 0 ? word[0] : string.Empty;

        HoldemDecision? decision = verb switch
        {
            "f" => HoldemDecision.Fold,
            "k" => HoldemDecision.Check,
            "c" => HoldemDecision.Call,
            "a" => HoldemDecision.RaiseTo(current.MaxRaiseTo),
            "r" => HoldemDecision.RaiseTo(
                word.Length > 1 && int.TryParse(word[1], out var to) ? to : current.MinRaiseTo),
            _ => null,
        };

        if (decision is null)
        {
            Console.WriteLine("  f fold, k check, c call, r <amount> raise, a all-in, l log, q quit");
            continue;
        }

        try
        {
            table.Act(decision.Value);
            return true;
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentOutOfRangeException)
        {
            // The engine is the authority on legality, so a refusal is information
            // rather than a crash. Printing it is how a rule gets learned.
            Console.WriteLine($"  no: {ex.Message}");
        }
    }
}

// ----------------------------------------------------------------------- soak

int Soak()
{
    // A bot in the player's seat too, so the table plays itself. The point is volume:
    // the betting round has states that only turn up when somebody shoves into a
    // short stack on the turn, and no amount of hand-written cases finds those.
    var me = new BotAgent(PokerPersonality.Improvise(rng), new Random(options.Seed), watchdog, options.Samples);

    var wins = new int[options.Seats];
    var moves = new Dictionary<HoldemMove, int>();
    var showdowns = 0;
    var reseats = 0;

    for (var hand = 1; hand <= options.Soak; hand++)
    {
        reseats += Math.Max(0, Reseat());

        watchdog.BeginHand(hand);
        table.StartHand();

        if (!watchdog.Check(table, "after the deal"))
        {
            return 1;
        }

        while (table.AwaitingPlayer)
        {
            var decision = me.Decide(table.ContextForActor());
            moves[decision.Move] = moves.GetValueOrDefault(decision.Move) + 1;

            table.Act(decision);

            if (!watchdog.Check(table, "after a move"))
            {
                return 1;
            }
        }

        me.HandEnded(new HandOutcome(table.Player.Net, table.Player.Stack, rules.BuyIn, table.Player.Folded));

        if (!watchdog.Check(table, "at the end of the hand"))
        {
            return 1;
        }

        if (table.Seats.Count(seat => !seat.Folded) > 1)
        {
            showdowns++;
        }

        foreach (var seat in table.Seats.Where(seat => seat.Won > 0))
        {
            wins[seat.Index]++;
        }

        if (hand % Math.Max(1, options.Soak / 10) == 0)
        {
            Console.WriteLine($"  {hand:N0} hands...");
        }
    }

    Console.WriteLine();
    Console.WriteLine(
        $"  {options.Soak:N0} hands, {showdowns:N0} reached a showdown, "
        + $"biggest pot {watchdog.BiggestPot:N0}");
    Console.WriteLine($"  {reseats} re-seat(s) after somebody went broke");
    Console.WriteLine(
        "  your moves: "
        + string.Join(", ", moves.OrderBy(pair => pair.Key).Select(pair => $"{pair.Key} {pair.Value:N0}")));
    Console.WriteLine();

    foreach (var seat in table.Seats)
    {
        var who = seat.IsPlayer ? "you" : cast[seat.Index - 1].Name;
        Console.WriteLine($"  {seat.Name,-10} {seat.Stack,10:N0}   won {wins[seat.Index],5:N0} pots   {who}");
    }

    Report();
    return watchdog.Failures.Count == 0 ? 0 : 1;
}

// ---------------------------------------------------------------------- both

/// <summary>
/// Buys a busted seat back in, and tells the watchdog it did.
///
/// The engine refuses to deal to a seat with no chips, deliberately -- who leaves and
/// who sits down is table management. Here that policy is "everybody rebuys", which
/// keeps a soak run going, and the created chips are declared so the conservation
/// check stays honest rather than being switched off.
/// </summary>
int Reseat()
{
    var bought = 0;

    foreach (var seat in table.Seats.Where(seat => seat.Stack <= 0).ToList())
    {
        if (seat.IsPlayer && options.Soak == 0)
        {
            return -1;
        }

        // A new face for a bot seat, rather than the same character reappearing with
        // a fresh stack. That is the difference between a table and a treadmill.
        var newcomer = seat.IsPlayer
            ? null
            : new BotAgent(
                PokerPersonality.Improvise(rng),
                new Random(rng.Next()),
                watchdog,
                options.Samples);

        table.Reseat(seat.Index, rules.BuyIn, newcomer);
        watchdog.ToppedUp(rules.BuyIn);
        Console.WriteLine($"  {seat.Name} went broke; somebody sits down with {rules.BuyIn:N0}");
        bought++;
    }

    return bought;
}

void Report()
{
    Console.WriteLine();

    if (watchdog.Failures.Count == 0)
    {
        Console.WriteLine("  no invariant was broken.");
        return;
    }

    Console.WriteLine($"  {watchdog.Failures.Count} INVARIANT FAILURE(S):");

    foreach (var failure in watchdog.Failures.Distinct().Take(20))
    {
        Console.WriteLine($"    {failure}");
    }
}
