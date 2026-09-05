# SPT Casino

A casino for [SPT](https://sp-tarkov.com) 4.1.x. One tab on the menu bar opens a
lobby; the lobby has three tables.

| | |
| --- | --- |
| **Blackjack** | Twenty-one against the dealer. |
| **Poker** | No-limit hold'em against bots, seated under names drawn from the game's own PMC nickname list. |
| **Roulette** | A single-zero European wheel that actually spins, and a full betting cloth to play it from. |

**It plays for real roubles.** A stake leaves your stash the moment you commit it and
winnings are paid straight back into it. There is no chip balance and nothing to cash
out. If your stash is too full to take a payout, it arrives in the post instead.

The house edge is real too. Roulette keeps 2.70% of everything staked on it, and the
other two are not charity either.

## Installing

Extract over your SPT folder -- the one holding `SPT_Runtime` -- and start the server.

**If you have Blackjack, Poker or Roulette installed separately, remove them first.**
They are all part of this now. Leaving them gives you four tabs on the bar and four
copies of the same key handler fighting over the escape key.

You should see a `[Casino] client loaded` line in `BepInEx/LogOutput.log`, and a block
for each table in the server console. Silence from a table means the version gate: the
server mods declare `~4.1.3` and load nothing outside it.

## Playing

**CASINO** on the bar along the bottom of the menu. It is on every out-of-raid screen,
so the tables open from the hideout or the flea market without backing out first.

Escape leaves a table and brings you back to the lobby. Escape again closes the casino,
and only the casino -- the screen behind it stays where it was.

The first time an account walks in, a card explains what the money does. Read it once.

## Building

Requires the .NET 10 SDK, and an SPT 4.1.x install for the plugin.

```
dotnet build SPT-Casino.slnx
dotnet test  SPT-Casino.slnx
scripts/casino/pack.ps1 -InstallPath 'C:\path\to\SPT'
```

`Casino.Client` is net472 and is compiled against the assemblies of a real install,
found through `$(SPTPath)` or passed with `-p:SPTPath=<install root>`. Everything else
is .NET 10 and builds anywhere.

## How it is laid out

```
src/Casino.Client     the plugin: tab, lobby, welcome card, escape key
src/<Table>.Client    each table's panel and views, compiled into the plugin
src/<Table>.Server    each table's server mod, separate, on its own routes
src/<Table>.Game      the rules, with no SPT types in them, unit tested
tools/<Table>.Console a harness that plays the game in a terminal
docs/<table>.md       that table's working notes
```

One plugin, three server mods. The tables were three separate mods until September
2026 and their server halves still are, each keeping its own money in its own place.

## Licence

Not yet chosen.
