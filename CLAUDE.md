# SPT-Casino -- working notes for Claude

**SPT Casino** is one mod: a single task-bar tab that opens a lobby, and three tables
behind it -- **Blackjack**, **Poker** and **Roulette**. It was three separate mods
until 2026-09-05, and the seams are still visible on purpose.

**One folder each side.** `BepInEx/plugins/Casino` and `SPT_Runtime/user/mods/Casino`.
The server folder holds seven assemblies -- a metadata one plus a `.Server` and a
`.Game` per table -- and SPT is perfectly happy with that. See "One folder, seven
assemblies".

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
src/Casino.Shared/        one copy of what every table draws with. No project of its own
src/<Table>.Client/       each table's panel and views. NOT shipped as plugins
src/Casino.Server/        the one IModMetadata, and the legacy-data finder
src/<Table>.Server/       each table's server code. No metadata of its own any more
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

## What the tables share, and where it lives

`src/Casino.Shared` holds one copy of everything every table draws with:

| File | Was |
| --- | --- |
| `Textures.cs` | identical in all three |
| `CardView.cs` | identical in Blackjack and Poker |
| `ChipView.cs` | Roulette's was a strict superset of Poker's, zero lines lost |
| `ProfileSync.cs` | identical but for the sync action, which is now a parameter |
| `Host.cs` | new: the two things the shared code needs from its host |

**`Host` is the whole seam.** These files used to reach for their own table's plugin
by name, which is most of why they could not simply be shared, and there were only
ever two such reaches: where the art is, and where to log. Both are set once at
startup, and both tolerate never being set -- a shared file that throws because a host
forgot to introduce itself would be worse than the duplication it replaced.

The three `.Client` projects compile the shared files too, so each still builds on its
own. `Casino.Client` compiles them once alongside the three panels.

Verified against the built assembly rather than assumed: `Casino.Client.dll` now
carries exactly one `Textures`, one `ProfileSync`, one `CardView` and one `ChipView`.
Before the extraction it shipped three, three, two and two.

**Still duplicated, on the server side**: `Bank.cs`, `Escrow.cs`, `Abstractions.cs`,
`ProfileGateway.cs`, `TableStore.cs` and `Wallets.cs` exist three times. Those are
three separate mods loaded into one server process, so the duplication is real but
harmless in a way the client's was not. See "Merging the servers".

**Blackjack is the one that drifts**: it is the oldest and improvements made while
writing the other two were never carried back. On 2026-09-05 that cost a real bug --
`InRaid` was wrong in all three copies at once, and every casino tab greyed out for
the rest of the session after a visit to the hideout. That class of fault is what the
extraction was for.

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

## Publishing

**SPT Casino registers as `com.mybutthasarash.sptcasino`, and only the main file has
to declare it.** That is `Casino.Client.dll`, and it does. The three server mods
bundled alongside keep their own GUIDs and that is a valid upload.

Written down because the opposite was believed here for months and is still the
reason `docs/blackjack.md` and `docs/poker.md` needed correcting: the stricter reading
was used, on 2026-09-05, to argue that the release could not go out without merging
the three server mods first. It could. Do not block a release on this again.

```
scripts/casino/pack.ps1 -Zip     # releases/casino/SPT_CasinoV1.0.zip
```

## One folder, seven assemblies

Read out of `SPT.Server.dll` rather than guessed, because the shape of the install
depends on it:

- `ModLoader.LoadMods` walks `Directory.GetDirectories("./user/mods/")` and calls
  `LoadMod` once per **folder**.
- `LoadMod` does `new DirectoryInfo(path).GetFiles()`, loads **every** `.dll` it finds,
  and hangs them all off one `SptMod.Assemblies`.
- `RegisterSptServicesAsync` walks that whole list, so every `[Injectable]` in every
  assembly is registered.
- `LoadModMetadata` runs `SingleOrDefault` over the types implementing `IModMetadata`
  and throws **"Duplicate mod metadata found for mod at path"** on the second.

So the rule is: **one folder, one metadata, as many assemblies as you like.** That is
why `Casino.Server` exists and is almost empty, and why the three tables carry a
`TableInfo` with their version on it instead of an `IModMetadata`. Their versions are
still their own -- Blackjack is on 1.1.4 inside a casino on 1.0.0 -- because they
describe the table rather than the download.

**Do not put the parked folder inside `user/mods`.** SPT tries to load every directory
under there, and one holding no assemblies throws `No Assemblies found in path` at
Critical on every boot. That was traded for three folders once already; the install
script parks old mods in `user/_replaced-by-SPT-Casino`, beside `mods` rather than in
it.

### The two collisions one folder creates

**`config.json`** was the same name in all three, so one file would have been read
three times. They are `blackjack.config.json`, `poker.config.json` and
`roulette.config.json` now.

**`escrow.json` was the dangerous one**, and it is the record of money the house owes
a player whose hand or spin was interrupted. Three writers on one path would have been
three tables overwriting each other's. They are `escrow-<table>.json`, and because the
old file is now somewhere the new code would never look, each store imports it once on
first run -- see `Casino.Server.LegacyData`, which checks both where the folder was and
where the install script parks it.

Proven rather than assumed: a record for 4,250,000 was planted in a retired Roulette
folder, the server was restarted, and it arrived in `escrow-roulette.json` under the
new folder. Then it was deleted, because it named a real session and would have paid
out money nobody staked.

## Keeping these notes honest

Each `docs/<table>.md` has a **Current state** section. Update it when a piece of work
finishes. Poker's notes went four commits claiming its server did not exist, and this
file spent a day saying Roulette moved no money after it did; a fresh session reads
those sections first and believes them.
