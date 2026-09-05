using Blackjack.Game;

// A terminal table for playing the engine without SPT attached. This is the only
// end-to-end check available on a machine with no game install: it exercises the
// exact code path the server mod drives, including the stake arithmetic the money
// handling depends on.

var rules = new Rules();
var table = new BlackjackTable(rules);
var balance = 1_000_000;
var wager = 10_000;

Console.WriteLine("Blackjack -- engine harness");
Console.WriteLine($"{rules.DeckCount} decks, dealer {(rules.DealerHitsSoft17 ? "hits" : "stands on")} soft 17, "
    + $"blackjack pays {rules.BlackjackPayout}:1");
Console.WriteLine("[enter] deal   +/- change wager   q quit");
Console.WriteLine();

while (true)
{
    Console.Write($"balance {balance:N0}   wager {wager:N0}  > ");
    var key = Console.ReadLine();

    if (key is null or "q")
    {
        break;
    }

    if (key is "+" or "-")
    {
        wager = Math.Clamp(key == "+" ? wager * 2 : wager / 2, rules.MinBet, rules.MaxBet);
        continue;
    }

    if (balance < wager)
    {
        Console.WriteLine("Not enough to cover that bet.\n");
        continue;
    }

    var view = table.Deal(wager);
    balance -= view.TotalWagered;

    while (view.Phase == RoundPhase.PlayerTurn)
    {
        Render(view);

        var actions = view.AvailableActions;
        Console.Write($"  {string.Join("  ", actions.Select(a => $"[{char.ToLower(a.ToString()[0])}]{a.ToString()[1..]}"))} > ");
        var choice = Console.ReadLine()?.Trim().ToLowerInvariant();

        var picked = actions.FirstOrDefault(
            a => char.ToLowerInvariant(a.ToString()[0]).ToString() == choice,
            PlayerAction.Stand);

        var staked = view.TotalWagered;
        view = picked switch
        {
            PlayerAction.Hit => table.Hit(),
            PlayerAction.Stand => table.Stand(),
            PlayerAction.Double => table.Double(),
            PlayerAction.Split => table.Split(),
            _ => table.Stand(),
        };

        // Mirrors what BlackjackCallbacks does: collect only the increase.
        balance -= view.TotalWagered - staked;
    }

    Render(view);
    balance += view.TotalReturned;

    var net = view.Net;
    Console.WriteLine(net switch
    {
        > 0 => $"  +{net:N0}",
        < 0 => $"  {net:N0}",
        _ => "  push",
    });
    Console.WriteLine();
}

static void Render(RoundView view)
{
    var dealerCards = string.Join(" ", view.Dealer.Cards);
    var hidden = view.Phase == RoundPhase.PlayerTurn ? " ??" : string.Empty;
    Console.WriteLine($"  dealer  {dealerCards}{hidden}  ({view.Dealer.Value}{(hidden.Length > 0 ? "+" : "")})");

    for (var i = 0; i < view.PlayerHands.Count; i++)
    {
        var hand = view.PlayerHands[i];
        var marker = view.Phase == RoundPhase.PlayerTurn && i == view.ActiveHandIndex ? ">" : " ";
        var label = view.PlayerHands.Count > 1 ? $"hand {i + 1}" : "player";
        var outcome = hand.Outcome == HandOutcome.Pending ? string.Empty : $"  {hand.Outcome}";

        Console.WriteLine(
            $" {marker}{label,-7} {string.Join(" ", hand.Cards)}  ({hand.Value}{(hand.IsSoft ? " soft" : "")})"
            + $"  {hand.Wager:N0}{outcome}");
    }
}
