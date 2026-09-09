# SPT Casino

A casino for [SPT](https://sp-tarkov.com) 4.1.x. One tab on the menu bar opens a
lobby; the lobby has four tables.

| | |
| --- | --- |
| **Blackjack** | Twenty-one against the dealer. |
| **Poker** | No-limit hold'em against bots, seated under names drawn from the game's own PMC nickname list. |
| **Roulette** | A single-zero European wheel that actually spins, and a full betting cloth to play it from. |
| **Slots** | Five reels and 243 ways, spinning the item icons out of your own install. AUTO and SPEED keep it moving without a click every time. Takes roubles, dollars or euros. |

**It plays for real money out of your stash -- never your gear.** A stake leaves the
moment you commit it and winnings are paid straight back in, and it only ever comes
from what is actually put away: pockets, the secure container, backpack and rig are
untouched, so a bet can never eat into what you're about to carry into a raid. There
is no chip balance and nothing to cash out. If your stash is too full to take a
payout, it arrives in the post instead.

The house edge is real too, and it is computed rather than guessed at. Roulette keeps
2.70% of everything staked on it; the slot machine returns 92.51% and keeps the rest.
The other two are not charity either.

## Installing

Extract over your SPT folder -- the one holding `SPT_Runtime` -- and start the server.

**If you have Blackjack, Poker or Roulette installed separately, remove them first.**
They are all part of this now. Leaving them gives you extra tabs on the bar and several
copies of the same key handler fighting over the escape key.

You should see a `[Casino] client loaded` line in `BepInEx/LogOutput.log` and one
`[Casino]` line in the server console. Silence there means the version gate rather than
a bug: the server declares `~4.1.3` and loads nothing outside it.

Each table can be made talkative on its own, with `VerboseLogging` in its config file
beside the mod -- `blackjack.config.json`, `poker.config.json`, `roulette.config.json`,
`slots.config.json`.

## Playing

**CASINO** on the bar along the bottom of the menu. It is on every out-of-raid screen,
so the tables open from the hideout or the flea market without backing out first.

Escape leaves a table and brings you back to the lobby. Escape again closes the casino,
and only the casino -- the screen behind it stays where it was.

The first time an account walks in, a card explains what the money does. Read it once.

Cards deal the way a dealer would throw them -- sliding in from one spot on the table
rather than popping into place -- and a hole card turns over in place instead of being
dealt again when it's revealed. The result doesn't post until every card involved has
actually finished turning over.

Blackjack and Slots each keep a running record behind their own STATS button: rounds
or pulls, wins and losses, best streak, and staked and returned per currency. Poker
and Roulette don't have one yet.

### Settings

F12 opens BepInEx's configuration manager. The casino keeps a handful of switches
there:

| Section | Setting | |
| --- | --- | --- |
| Menu | **Show the task-bar tab** | Whether CASINO appears on the bar at all. |
| Menu | **Put the tab on the right** | Sits it with CHARACTER instead of beside HIDEOUT. |
| Poker | **Buy-in** | What sitting down costs. Five seats, always. |
| Blackjack | **Enforce the table maximum** | Refuse a wager over the limit instead of trimming it. |
| Slots | **No maximum stake** | Bet as much as you like. The minimum still applies. |

## Known issues

**Sound is wired in but silent for now.** Every action -- dealing, chips landing, the
wheel and reels spinning, the win banner -- already fires its own cue; no audio files
ship with the mod yet, so nothing plays. Drop one into the plugin's `sounds` folder,
named for the cue, and it plays immediately -- no rebuild required.

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

`pack.ps1` will not write over a running server's assemblies. It says so and installs
the plugin anyway, so a client-only change does not need the server stopped.

## How it is laid out

```
src/Casino.Client     the plugin: tab, lobby, welcome and gift cards, escape key
src/Casino.Shared     one copy of what every table draws with
src/Casino.Server     the mod metadata, the startup line, and the 1.2.6 gift
src/<Table>.Client    each table's panel and views, compiled into the plugin
src/<Table>.Server    each table's server code, on its own routes
src/<Table>.Game      the rules, with no SPT types in them, unit tested
tools/<Table>.Console a harness that plays the game in a terminal (Blackjack, Poker)
docs/<table>.md       that table's working notes
```

**One plugin and one server mod**, however many tables there are. The tables were three
separate mods until September 2026; they now install into a single folder each side,
and the server folder holds one metadata class beside a `.Server` and a `.Game`
assembly per table. SPT is perfectly happy loading them all as one mod, and that is
what stops the menu bar growing a tab every time a game is added.

Each table still keeps its own money in its own place, including the record of what the
house owes a player whose hand was interrupted.

## Licence

Not yet chosen.
