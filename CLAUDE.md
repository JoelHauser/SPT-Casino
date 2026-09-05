# SPT-Casino -- working notes for Claude

**SPT Casino** is one mod: a single task-bar tab that opens a lobby, and three tables
behind it -- **Blackjack**, **Poker** and **Roulette**. It was three separate mods
until 2026-09-05, and the seams are still visible on purpose.

**One plugin, three server mods.** `src/Casino.Client` is the only thing installed
into `BepInEx/plugins`; the three server halves stay separate under
`SPT_Runtime/user/mods`, each on its own routes with its own GUID. That split is
deliberate and there is a hazard in closing it -- see "Merging the servers".

**Read the per-mod notes as well as this file.** This one holds only what is true of
all three; everything specific lives next door and is much longer:

| Table | Notes | State |
| --- | --- | --- |
| Blackjack | `docs/blackjack.md` | Plays for roubles. 1.1.0 is what other people have |
| Poker | `docs/poker.md` | Plays for roubles. Shipped at 1.0.0 |
| Roulette | `docs/roulette.md` | Plays for roubles as of 2026-09-05. Never released |

`docs/blackjack-readme.md` is Blackjack's public README, kept because it was the
repo's front page before the merge.

---

## How the casino is put together

```
src/Casino.Client/        the only plugin. Tab, lobby, welcome card, escape key
src/<Table>.Client/       each table's panel and views. NOT shipped as plugins
src/<Table>.Server/       each table's server mod, still separate, still its own GUID
src/<Table>.Game/         the rules, no SPT types, unit tested
tests/<Table>.*.Tests/
tools/<Table>.Console/    a harness that plays the game in a terminal
scripts/casino/pack.ps1   builds and installs the whole thing
scripts/<table>/          the per-table server pack and smoke scripts
docs/<table>.md           that table's working notes
```

**`Casino.Client` compiles the three tables in rather than owning them.** The panels
are listed as `<Compile Include="..\Roulette.Client\...">` in its project file and are
edited where they live. Not a line of them changed at the merge, which was possible
only because no panel ever referenced the task bar, the menu icon or the escape key.
The one thing they did reach for -- a log and a MonoBehaviour to start coroutines on --
is `src/Casino.Client/Shims.cs`, which stands in under the three old plugin names.

The three `.Client` projects still build on their own and still produce plugins. **Do
not ship those.** They are the editing surface, and `scripts/casino/pack.ps1` retires
their installed folders when it installs the casino, because four tabs and four Harmony
patches on one method is the likeliest way an upgrade goes wrong.

### Adding a table

Write the panel, implement `ICasinoGame` in `Games.cs` (three properties, three
methods: Name, Pip, Blurb, IsOpen, Open, Close), and add a line to `Games.All`. No
second tab, no second GUID, no second plugin. The lobby and the escape key pick it up
without being told.

### The layers, which matter more than they look

| Canvas | Sorting order |
| --- | --- |
| Lobby | 2900 |
| Welcome card | 2950 |
| The tables | 30000 |

Everything covers the lobby. That is what makes the transitions work: bring the lobby
up **solid underneath** whatever is on screen, then fade that away. Fading the lobby
*in* after removing the thing above it leaves frames where only the game's menu is
drawn, which is exactly the flash that had to be fixed once already. `CasinoLobby.Show`
takes an `instant` flag for this.

## Why the tables are still three copies of the same code

They were built one after another, each borrowing the last one's answers. Normalise
the mod name away and much of the client half is the *same file*:

| File | Poker vs Roulette | Blackjack vs Poker |
| --- | --- | --- |
| `Textures.cs` (461 lines) | identical | identical |
| `ProfileSync.cs` (88) | identical | 49 differ |
| `CardView.cs` (207) | n/a | identical |
| `ChipView.cs` | near-identical | n/a |

The server half repeats too: `Bank.cs`, `Escrow.cs`, `Abstractions.cs`,
`ProfileGateway.cs`, `TableStore.cs` and `Wallets.cs` exist three times.

**This is now worse than it was, not better.** Before the merge those were three
copies in three assemblies. They are three copies compiled into *one* assembly, so
`Casino.Client.dll` ships `Textures` three times over. `MenuIcon`, `TabCrowding`,
`TaskBarTab` and `EscapePatch` are no longer triplicated -- the casino owns one of
each -- but the rest is still waiting.

**Blackjack is the one that drifts**: it is the oldest and improvements made while
writing the other two were never carried back. On 2026-09-05 that cost a real bug --
`InRaid` was wrong in all three copies at once, and every casino tab greyed out for
the rest of the session after a visit to the hideout.

Pulling `Textures`, `ProfileSync`, `CardView` and `ChipView` into one shared source
file is the next obvious job. Until then, **a fix to any of them has to be made three
times**, and this table is the list of where.

## `dotnet` on this box is not the `dotnet` you want

The one first on PATH is `C:\Program Files\dotnet\dotnet.exe` and it carries **only
the 8.0.423 SDK**, so every .NET 10 project here dies on NETSDK1045 before compiling a
line. That reads exactly like the repo targeting something impossible. The .NET 10 SDK
is installed, just user-local:

```
C:\Users\Hoel\.dotnet\dotnet.exe --list-sdks   # 9.0.317, 10.0.400
```

Put that directory ahead of `C:\Program Files\dotnet` on PATH for any `dotnet build`,
`dotnet test` or `dotnet run`, and for the pack scripts, which shell out to plain
`dotnet`. The `.Client` projects are net472 and build under either.

```
dotnet build SPT-Casino.slnx      # 18 projects, clean
dotnet test  SPT-Casino.slnx      # 428 tests
scripts/casino/pack.ps1 -InstallPath 'H:\SPT4.1.X'
```

`tools/Blackjack.Installer` is deliberately outside the solution: it embeds a
`payload.zip` that `tools/build-installer.py` generates, so from a clean checkout it
fails with CS1566.

**`.slnx` files are XML, so a `--` inside a comment is a parse error.** This has broken
the build twice; both times the comment was written in this repo's own house style.

## The things that are true of every table

**SPT 4.x server mods are C#, not TypeScript.** The `mod.ts` / `package.json` /
tsyringe world ended at 3.x and most guides online still describe it. Server mods are
.NET 10 class libraries referencing `SPTarkov.Server.Core`, with an `IModMetadata`
record in place of `package.json`.

**`SptVersion` is a hard load gate.** All three say `~4.1.3` (>=4.1.3 <4.2.0). A mod
outside the range loads nothing and *logs nothing*. Silence at startup means the gate,
not a bug in the game code.

**The plugin is compiled against the game, not just against SPT.** 4.1.3's
`PluginValidator` reads a plugin's references to `spt-*` and compares Major.Minor to
the running server, so a plugin built against a 4.0 install is rejected outright. Pass
`-p:SPTPath=...` through PowerShell, not Bash: a backslash path gets mangled on the way
and every reference silently fails to resolve.

**Request bodies are matched case-sensitively, and PascalCase.** Lowercase keys bind
nothing and every field takes its default, which is how a 100,000 stake arrives as 0
while looking like it bound correctly.

**A destroyed Unity object is not null to a plain reference check.** Comfort's
`Singleton<T>.Instantiated` is `ldsfld; box; ldnull; cgt.un` -- a raw comparison, so it
reports a torn-down world as present. Use Unity's `==` on the instance. And note the
hideout is a `GameWorld` too: `HideoutGameWorld : ClientLocalGameWorld :
ClientGameWorld : GameWorld`.

## Where the money is

**All three tables move real roubles**, through escrow and the profile. There is no
chip balance and nothing to cash out: a stake leaves the stash when it is committed and
the return is paid straight back in, with anything the stash will not take posted as
mail.

Roulette's is the newest and the most carefully checked: 13 money tests written
**before** the settlement they check, then mutation-tested against eight deliberate
faults, all eight caught. `Payouts` and `Bet.Covers` were mutation-tested separately --
21 faults, and the two that survived the first pass were both tests **counting** the
numbers a bet covers instead of reading them. When adding a bet, assert *which* numbers
it covers, not how many.

**Write `MoneyInvariantTests` before the settlement, not after.** An end-of-run balance
check misses errors that cancel, and a settlement written first gets tests shaped around
what it already does rather than around what it owes.

## Merging the servers

Not done, and there is a landmine in it: **all three `EscrowStore`s write
`escrow.json`.** One mod folder would be one file with three writers silently
overwriting each other, and that file is the record of money the house owes a player
whose spin was interrupted. If it is ever done, namespace the files per table *and*
read the old paths once on upgrade, or somebody with an in-flight stake loses it.

There is no user-facing reason to do it. The three server mods install and load
perfectly well side by side.

## Keeping these notes honest

Each `docs/<table>.md` has a **Current state** section. Update it when a piece of work
finishes. Poker's notes went four commits claiming its server did not exist, and this
file spent a day saying Roulette moved no money after it did; a fresh session reads
those sections first and believes them.
