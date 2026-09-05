namespace Poker.Console;

/// <summary>What the harness was asked to do.</summary>
internal sealed record Options
{
    public int Seats { get; init; } = 4;

    public int SmallBlind { get; init; } = 10_000;

    public int BigBlind { get; init; } = 20_000;

    public int BuyIn { get; init; } = 1_000_000;

    /// <summary>Printed at startup, so any hand can be got back exactly.</summary>
    public int Seed { get; init; } = Environment.TickCount;

    /// <summary>Hands to play with nobody at the keyboard. Zero means play by hand.</summary>
    public int Soak { get; init; }

    /// <summary>Print every engine line as it happens, rather than only on a failure.</summary>
    public bool Verbose { get; init; }

    /// <summary>Show everybody's hole cards. A debugging switch, not a feature.</summary>
    public bool Peek { get; init; }

    /// <summary>
    /// Rollouts per equity estimate. Lower is faster and shakier, which is fine for a
    /// long soak where the point is the betting round rather than the play.
    /// </summary>
    public int Samples { get; init; } = Game.HandEquity.DefaultSamples;

    public bool Help { get; init; }
}

internal static class Args
{
    public static Options Parse(string[] args)
    {
        var options = new Options();

        for (var i = 0; i < args.Length; i++)
        {
            var next = i + 1 < args.Length ? args[i + 1] : null;

            int Number(int fallback) => int.TryParse(next, out var value) ? value : fallback;

            switch (args[i])
            {
                case "--seats": options = options with { Seats = Number(options.Seats) }; i++; break;
                case "--small": options = options with { SmallBlind = Number(options.SmallBlind) }; i++; break;
                case "--big": options = options with { BigBlind = Number(options.BigBlind) }; i++; break;
                case "--buyin": options = options with { BuyIn = Number(options.BuyIn) }; i++; break;
                case "--seed": options = options with { Seed = Number(options.Seed) }; i++; break;
                case "--soak": options = options with { Soak = Number(1_000) }; i++; break;
                case "--samples": options = options with { Samples = Number(options.Samples) }; i++; break;
                case "--verbose" or "-v": options = options with { Verbose = true }; break;
                case "--peek": options = options with { Peek = true }; break;
                case "--help" or "-h": options = options with { Help = true }; break;
            }
        }

        return options;
    }

    public static void Usage()
    {
        System.Console.WriteLine("Poker -- engine harness");
        System.Console.WriteLine();
        System.Console.WriteLine("  --seats N      seats at the table, the player included (default 4)");
        System.Console.WriteLine("  --small N      small blind (25)");
        System.Console.WriteLine("  --big N        big blind (50)");
        System.Console.WriteLine("  --buyin N      starting stack (5,000)");
        System.Console.WriteLine("  --seed N       fix the shuffle and the characters, so a hand can be got back");
        System.Console.WriteLine("  --soak N       play N hands with a bot in every seat and check every invariant");
        System.Console.WriteLine("  --samples N    equity rollouts per decision (240; drop it for a fast soak)");
        System.Console.WriteLine("  -v, --verbose  print every engine line as it happens");
        System.Console.WriteLine("  --peek         show everybody's hole cards");
        System.Console.WriteLine();
        System.Console.WriteLine("  dotnet run --project tools/Poker.Console");
        System.Console.WriteLine("  dotnet run --project tools/Poker.Console -- --soak 5000 --samples 30");
    }
}
