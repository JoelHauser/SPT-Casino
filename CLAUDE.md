# SPT-Casino -- working notes for Claude

Three gambling mods for SPT 4.1.x, in one repository: **Blackjack**, **Poker** and
**Roulette**. Each is still its own installable mod with its own GUID, its own plugin
and its own server folder. They share a repository, not a build output.

**Read the per-mod notes as well as this file.** This one holds only what is true of
all three; everything specific lives next door and is much longer:

| Mod | Notes | State |
| --- | --- | --- |
| Blackjack | `docs/blackjack.md` | Shipped. 1.1.0 is what other people have; 1.1.4 is built |
| Poker | `docs/poker.md` | Shipped at 1.0.0 |
| Roulette | `docs/roulette.md` | 0.1.0, plays but moves no money |

`docs/blackjack-readme.md` is Blackjack's public README, kept because it was the
repo's front page before the merge.

---

## Why they are together

They were built one after another, each borrowing the last one's answers, and it
shows. Normalise the mod name away and much of the client half is the *same file*:

| File | Poker vs Roulette | Blackjack vs Poker |
| --- | --- | --- |
| `Textures.cs` (461 lines) | identical | identical |
| `MenuIcon.cs` (213) | identical | 50 lines differ |
| `ProfileSync.cs` (88) | identical | 49 differ |
| `EscapePatch.cs` (69) | identical | 8 differ |
| `CardView.cs` (207) | n/a | identical |
| `TabCrowding.cs` (263) | 2 lines differ | 2 differ |
| `TaskBarTab.cs` (1233) | 8 lines differ | 215 differ |

The server half repeats too: `Bank.cs`, `Escrow.cs`, `Abstractions.cs`,
`ProfileGateway.cs`, `TableStore.cs` and `Wallets.cs` exist three times.

That is roughly two thousand lines of copy that has to be fixed three times and, in
practice, gets fixed once. **Blackjack is the one that drifts** -- it is the oldest,
and improvements made while writing Poker and Roulette were never carried back. Its
`TaskBarTab` is 215 lines away from the other two.

**Pulling the shared halves into `Casino.Client` and `Casino.Server` is the point of
the merge and has not been done yet.** Until it is, a fix to a shared file still has
to be made three times, and this table is the list of where.

## The layout

```
src/<Mod>.Game/      the rules, no SPT types, unit tested
src/<Mod>.Server/    SPT server mod, .NET 10
src/<Mod>.Client/    BepInEx plugin, net472, built against a real install
tests/<Mod>.*.Tests/
tools/<Mod>.Console/ a harness that plays the game in a terminal
scripts/<mod>/       pack and smoke scripts, one folder per mod
releases/<mod>/      the zips that shipped, and their changelogs
docs/<mod>.md        that mod's working notes
```

Nothing under `src/`, `tests/` or `tools/` moved in the merge, because every project
is already name-prefixed. Blame reaches straight through the merge commits into each
mod's own history -- 163 commits, three roots.

`SPT-Casino.slnx` covers all three, grouped by mod rather than by layer, because a
change is nearly always to one game top to bottom.

## `dotnet` on this box is not the `dotnet` you want

The one first on PATH is `C:\Program Files\dotnet\dotnet.exe` and it carries **only
the 8.0.423 SDK**, so every .NET 10 project -- nine of them here -- dies on
NETSDK1045 before compiling a line. That reads exactly like the repo targeting
something impossible. The .NET 10 SDK is installed, just user-local:

```
C:\Users\Hoel\.dotnet\dotnet.exe --list-sdks   # 9.0.317, 10.0.400
```

Put that directory ahead of `C:\Program Files\dotnet` on PATH for any `dotnet
build`, `dotnet test` or `dotnet run`, and for the pack scripts, which shell out to
plain `dotnet`. The `.Client` projects are net472 and build under either.

Whole solution, from a shell with that PATH:

```
dotnet build SPT-Casino.slnx      # 16 projects, clean
dotnet test  SPT-Casino.slnx      # 404 tests
```

`tools/Blackjack.Installer` is deliberately outside the solution: it embeds a
`payload.zip` that `tools/build-installer.py` generates, so from a clean checkout it
fails with CS1566.

## The things that are true of all three

**SPT 4.x server mods are C#, not TypeScript.** The `mod.ts` / `package.json` /
tsyringe world ended at 3.x and most guides online still describe it. Server mods are
.NET 10 class libraries referencing `SPTarkov.Server.Core`, with an `IModMetadata`
record in place of `package.json`.

**`SptVersion` is a hard load gate.** All three say `~4.1.3` (>=4.1.3 <4.2.0). A mod
outside the range loads nothing and *logs nothing*. Silence at startup means the
gate, not a bug in the game code.

**The client plugin is compiled against the game, not just against SPT.** 4.1.3's
`PluginValidator` reads a plugin's references to `spt-*` and compares Major.Minor to
the running server, so a plugin built against a 4.0 install is rejected outright.
Each `.Client.csproj` picks up an install through `$(SPTPath)`; pass `-p:SPTPath=...`
through PowerShell, not Bash, because a backslash path gets mangled on the way and
every reference silently fails to resolve.

**Request bodies are matched case-sensitively, and PascalCase.** Lowercase keys bind
nothing and every field takes its default, which is how a 100,000 stake arrives as 0
while looking like it bound correctly.

**All three add a task-bar tab, and that is now a shared problem.** With Raid Review's
tab option and PIT Fireteam installed as well, the bar crowds and labels wrap. Each
mod carries its own `TabCrowding.cs` that measures whether a single-word label renders
on more than one line. Three tabs from one family is itself part of the crowding; a
single casino tab with a lobby behind it would fix it outright, and was considered and
not chosen at the merge.

## Where the money is, and is not

Blackjack and Poker both move real profile money, through escrow and the item-event
transport. **Roulette does not** -- its chips are numbers in memory and a winning spin
pays nothing. That is a deliberate stopping point, the same one Poker made: a mod that
cannot move money cannot lose any, so loading, routing and play get proven against a
real profile before settlement, which is the part that cost Blackjack the most.

`docs/roulette.md` under "The money" lists what to port and in what order. Write
`MoneyInvariantTests` **before** settlement, not after.

## Keeping these notes honest

Each `docs/<mod>.md` has a **Current state** section. Update it when a piece of work
finishes. Poker's notes went four commits claiming its server did not exist; a fresh
session reads that section first and believes it.
