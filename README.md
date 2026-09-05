# SPT-Casino

Three gambling mods for [SPT](https://sp-tarkov.com) 4.1.x, developed together.

| | | |
| --- | --- | --- |
| **Blackjack** | A blackjack table in the hideout menu. Stake roubles, dollars, euros, GP coins, bitcoin or Lega medals. | Shipped |
| **Poker** | No-limit Texas Hold'em against bots, seated under names drawn from the game's own PMC nickname list. | Shipped |
| **Roulette** | A European wheel that actually spins, and a betting cloth to play it from. | In progress |

Each is a separate mod with its own GUID, plugin and server folder, and each installs
on its own. They live in one repository because they are the same mod three times
underneath: the menu tab, the escape handling, the profile sync, the chip and card art
and the money path were written once and copied twice.

## Building

Requires the .NET 10 SDK, and an SPT 4.1.x install for the client plugins.

```
dotnet build SPT-Casino.slnx
dotnet test  SPT-Casino.slnx
```

The `.Client` projects are BepInEx plugins targeting net472 and are compiled against
the assemblies of a real install, found through `$(SPTPath)` or passed with
`-p:SPTPath=<install root>`. The rest is .NET 10 and builds anywhere.

## Packing a mod

```
scripts/blackjack/pack.ps1
scripts/poker/pack-mod.ps1
scripts/roulette/pack-mod.ps1
```

Each produces a zip under `releases/<mod>/`, laid out relative to the SPT folder so it
extracts straight over an install. `-InstallPath` also deploys it. **`SPT_Runtime/` is
part of the path inside the zip**, not the folder you extract into.

## Layout

```
src/<Mod>.Game       the rules, with no SPT types in them, unit tested
src/<Mod>.Server     the SPT server mod
src/<Mod>.Client     the BepInEx plugin that draws the table
tools/<Mod>.Console  a harness that plays the game in a terminal
docs/<mod>.md        that mod's working notes
```

## Releases

Under `releases/<mod>/`, with a changelog beside each zip.

## Licence

Not yet chosen.
