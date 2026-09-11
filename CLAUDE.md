# SPT-Casino -- working notes for Claude

**SPT Casino** is one mod: a single task-bar tab that opens a lobby, and four tables
behind it -- **Blackjack**, **Poker**, **Roulette** and **Slots**. It was three
separate mods until 2026-09-05, and the seams are still visible on purpose.

**One folder each side.** `BepInEx/plugins/Casino` and `SPT_Runtime/user/mods/Casino`.
The server folder holds nine assemblies -- a metadata one plus a `.Server` and a
`.Game` per table -- and SPT is perfectly happy with that. See "One folder, seven
assemblies", which was written when there were seven and is true of any number.

**Read the per-mod notes as well as this file.** This one holds only what is true of
all three; everything specific lives next door and is much longer:

| Table | Notes | State |
| --- | --- | --- |
| Blackjack | `docs/blackjack.md` | Plays for roubles. 1.1.0 is what other people have |
| Poker | `docs/poker.md` | Plays for roubles. Shipped at 1.0.0 |
| Roulette | `docs/roulette.md` | Plays for roubles as of 2026-09-05. Never released |
| Slots | `docs/slots.md` | Roubles, dollars or euros. Played, never released |

`docs/blackjack-readme.md` is Blackjack's public README, kept because it was the
repo's front page before the merge.

---

## How the casino is put together

```
src/Casino.Client/        the only plugin. Tab, lobby, welcome + gift card, escape key
src/Casino.Shared/        one copy of what every table draws with. No project of its own
src/<Table>.Client/       each table's panel and views. NOT shipped as plugins
src/Casino.Server/        the one IModMetadata, the legacy-data finder, the gift
src/<Table>.Server/       each table's server code. No metadata of its own any more
src/<Table>.Game/         the rules, no SPT types, unit tested
tests/<Table>.*.Tests/
tests/Casino.Server.Tests/  the gift's money invariants
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
| Gift card | 2960 |
| The tables | 30000 |

Everything covers the lobby. That is what makes the transitions work: bring the lobby
up **solid underneath** whatever is on screen, then fade that away. Fading the lobby
*in* after removing the thing above it leaves frames where only the game's menu is
drawn, which is exactly the flash that had to be fixed once already. `CasinoLobby.Show`
takes an `instant` flag for this.

The gift card sits above the welcome card rather than beside it because a brand new
profile on 1.2.6 gets both, and the gift is built on top of the lobby while the welcome
is still fading off it. See `CasinoLobby.AfterIntro`.

## What the tables share, and where it lives

`src/Casino.Shared` holds one copy of everything every table draws with:

| File | Was |
| --- | --- |
| `Textures.cs` | identical in all three |
| `CardView.cs` | identical in Blackjack and Poker |
| `ChipView.cs` | Roulette's was a strict superset of Poker's, zero lines lost |
| `ProfileSync.cs` | identical but for the sync action, which is now a parameter |
| `Host.cs` | new: the two things the shared code needs from its host |
| `DealAnimator.cs` | new, 8 Sep 2026: slides a dealt card in and flips a revealed one over. `Deal`/`Flip`/`FinishTime` take an optional `duration`; Blackjack passes its own (faster) one, Poker takes the default -- see `docs/blackjack.md` |
| `SoundBoard.cs` | new, 8 Sep 2026: plays a named `Cue` by loading a file for it out of a shared `sounds/` folder -- see "The sound boilerplate" |

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

## The sound boilerplate

There are no sound files in the repo yet. `Casino.Shared.SoundBoard` exists so that
adding them later needs no code changes: every table already calls
`SoundBoard.Play(Cue.Something)` at the exact moment each cue belongs, and dropping a
`.wav` or `.ogg` named for that cue into `sounds/` beside the installed DLL is the
entire remaining task -- no rebuild, because
`SoundBoard` reads from disk at request time rather than baking anything into the
assembly, the same arrangement `CardView` and `ChipView` already use for art. A cue
with no file is silent, not broken; every table plays correctly today with nothing to
hear.

`src/Casino.Client/assets/sounds/README.txt` is the manifest -- every cue, the exact
file name it looks for, and one line on what it is. That folder ships inside the zip,
so the manifest is sitting right next to where a file actually needs to go. **Read it
before adding a cue**, and add the new file name to it -- the enum and the manifest
are two places that have to agree and nothing enforces that automatically.

**Timing is owned by whoever already knows it, never re-derived.** `SoundBoard.Play`
does exactly one thing -- find a file, play it -- and is called from inside
`DealAnimator`'s own coroutines (a deal's cue fires when the stagger delay ends and the
card actually starts moving, not when `Deal()` was called; a flip's cue fires at the
edge-on swap instant, not when the shrink starts), from `WheelView.Run` (spin start,
and the ball's landing frame, not the callback that follows it), from `ReelView`'s
`Spin`/`SpinOne` (once when all five reels start together, once per reel as each one
settles), and from `SlotPanel.SetPaid` (alongside the win banner's own pop, using the
identical multiple boundaries so the two tiers cannot drift apart). Every one of those
call sites already had the timing this needed; the alternative was a second, separate
system re-guessing it from outside and eventually disagreeing.

**What plays on a button press waits for the server to agree.** Poker's `Act`,
Blackjack's `Deal`, and Roulette's `Place` all check the reply for success before
playing their chip cue -- a refused bet moved no chips, so a sound there would be
lying about what just happened on screen.

**Known gaps, not oversights**: a bot betting in Poker plays no sound (only the human
player's own actions are hooked, since a bot's bet would need diffing server state the
chips do not currently track, the way dealt cards already are); Blackjack's Double and
Split take more money mid-hand and are unhooked; Roulette's `Lift` (taking a chip back)
has no cue. All are listed in the manifest too.

## `dotnet` on this box

**Corrected 2026-09-09.** This section used to say that the `dotnet` first on PATH
carried only the 8.0.423 SDK, so every .NET 10 project here died on NETSDK1045, and that
the real SDK was user-local under a `C:\Users\Hoel\.dotnet`. Neither is true on this
machine now: `dotnet --list-sdks` from the one on PATH reports **10.0.202**, there is no
`C:\Users\Hoel` on the box at all, and the whole solution builds and tests off it with
nothing prepended.

```
dotnet build SPT-Casino.slnx -p:SPTPath=C:\HUH
dotnet test  SPT-Casino.slnx -p:SPTPath=C:\HUH   # 504 tests
scripts/casino/pack.ps1 -SPTPath C:\HUH
```

If NETSDK1045 ever does appear, the old note is still the shape of the answer -- find a
newer SDK and put its directory first on PATH -- but run `--list-sdks` before believing
any of it.

**Pass `-p:SPTPath` through PowerShell, not Bash.** Still true, and it still fails
confusingly: a backslash path mangled on the way through arrives as `C:HUH` and the build
stops with "is not an SPT install root", which reads like a missing install rather than a
quoting problem.

`tools/Blackjack.Installer` is deliberately outside the solution: it embeds a
`payload.zip` that `tools/build-installer.py` generates, so from a clean checkout it
fails with CS1566.

**`.slnx` files are XML, so a `--` inside a comment is a parse error.** This has broken
the build twice; both times the comment was written in this repo's own house style.

**Three projects in the solution do not build, and did not before the gift either.**
`Blackjack.Client`, `Poker.Client` and `Roulette.Client` -- the retired standalone
plugins kept as an editing surface and never shipped -- fail with nine CS0122 errors
about `HideoutGameWorld`, `NarrateGameWorld` and `InputNodeAbstract.TranslateInput` being
inaccessible, out of `TaskBarTab.cs` and `EscapePatch.cs`. `Casino.Client` compiles the
panels it needs from those projects directly and is unaffected, so the plugin, the server
and all 504 tests still build. Check a pristine checkout before blaming a change for
those nine.

## This branch is the 4.0.x backport

`4.0.x-backport` targets **SPT 4.0.13**, installed at `H:\SPT2026`. `main` targets
4.1.x at `H:\SPT4.1.X`. Do not merge this branch into `main`: nearly every change
on it is a 4.1 API rewritten for 4.0, and merging would break the mainline.

```
$env:PATH = "C:\Users\Hoel\.dotnet;$env:PATH"   # 10.0.400; the one on PATH is 8.0 only
& 'H:\SPTMods\SPT-Casino\scripts\casino\pack.ps1' -SPTPath 'H:\SPT2026' -InstallPath 'H:\SPT2026'
```

**The two that actually stopped the server, and neither said so.**

- **4.0.13 runs on .NET 9, 4.1 on .NET 10.** Read it out of
  `SPT.Server.runtimeconfig.json` (`"tfm": "net9.0"`), and the NuGet package says the
  same thing by shipping `lib/net9.0`. Every assembly that lands in
  `user/mods/Casino` -- the five `.Server` and four `.Game` projects -- must target
  `net9.0`. A net10 assembly cannot be loaded by a net9 host, and what that looks
  like is **a server that exits during startup having written no log at all**, since
  it dies before the logger's first flush. `ModDllLoader.LoadAllMods` catches the
  per-mod exception and `Console.WriteLine`s it, so the only copy of the message is
  on a console that closes with the window.
- **An unregistered item-event action throws.** See `ItemEventActions`.

**Getting the error out of a server that dies on boot.** `SPT.Server.exe` calls
`SetConsoleOutputMode` before anything else and throws `Unable to get console mode`
the moment stdout is redirected, so it cannot be piped, teed or captured -- the
"crash" a redirect produces is the redirect. Run it under a console that outlives
it and read the scrollback:

```
Start-Process cmd.exe -ArgumentList '/k','"H:\SPT2026\SPT\SPT.Server.exe"' -WorkingDirectory 'H:\SPT2026\SPT'
```

Quote the full path -- this box will not resolve a bare `SPT.Server.exe` out of the
working directory. Once it boots, `user/logs/spt/spt<DATE>.log` has everything.

**Exercising it without the game.** The server listens on **https** (a self-signed
`https://127.0.0.1:6969`, so `curl -k`) and **request bodies are zlib-compressed**.
A plain-JSON body fails inside SPT's own inflater with "The archive entry was
compressed using an unsupported compression method" before any mod code runs, which
reads like a mod bug and is not one. A route that answers 200 with an empty body is
usually this. Compare against a nonsense URL: an unregistered route 404s.

```
python -c "import zlib; open('body.z','wb').write(zlib.compress(b'{}'))"
curl -sk -X POST https://127.0.0.1:6969/blackjack/ping --data-binary @body.z \
     -H "Cookie: PHPSESSID=<a real profile id from SPT/user/profiles>"
```

Only Slots' ping needs a **real** profile -- it reaches `EventOutputHolder.GetOutput`,
and a made-up session id fails with "no profile found". The other three answer
without one. Never call `/casino/gift/claim` while poking at a real profile: it pays
a million roubles and records that it did.

**Verified on 4.0.13 as of 2026-09-11**: the mod loads (`ModValidator` logs it),
all four tables answer their ping with correct limits and balances off a real
profile, and `/casino/gift/status` reports the gift pending. Not yet verified
in-game -- no client session has been run.

## The SPT install on this box is not `H:\SPT4.1.X`

Every `.csproj`'s default `SPTPath` is `H:\SPT4.1.X`, and Blackjack's alone falls back
to `C:\HUH` if that path doesn't exist. On this machine that fallback is not a
coincidence to skip past: there is no `H:` drive at all, only `C:` and a `K:` that is a
disconnected work share (`\\bls-adfs\Common`, unrelated to any of this), and the real
install lives at `C:\HUH`. Pass it explicitly for anything that touches a `.Client`
project or `pack.ps1`:

```
dotnet build src/Casino.Client/Casino.Client.csproj -c Release -p:SPTPath=C:\HUH
scripts/casino/pack.ps1 -SPTPath C:\HUH
```

`Casino.Client.csproj` itself doesn't carry the `C:\HUH` fallback the way Blackjack's
does, so the plain `dotnet build SPT-Casino.slnx` this file and the README both show
elsewhere will fail to find the install on this box specifically unless `-p:SPTPath`
is added.

## When the obfuscator empties a name instead of just moving it

`MenuScreen.Awake` was the first time this repo hit BSG's obfuscator reshuffling a
class between game builds. `ItemFactory`, on EFT 0.16.9.5 build 40743, was the second
and the worse one: its `Name` in the assembly's own metadata is not a garbled Unicode
glyph the way `ItemIconCreator`'s is, it is the empty string. There is no identifier
C# will let anyone write for that -- `nameof` and Harmony's `AccessTools.TypeByName`
both still need a real string to search for, and an empty one is not one.

**Search for the member by its shape. Do not pin a metadata token.** That is the one
thing to take from this, and it was learned the expensive way:

```csharp
foreach (var type in module.GetTypes())          // wrap: ReflectionTypeLoadException
    foreach (var m in type.GetMethods(Public | Instance | DeclaredOnly))
        if (m.Name == "CreateItem" && m.ReturnType == typeof(Item) && /* params match */)
            return m;                            // DeclaringType is usable, named or not
```

The declaring type is what loses its name; the *method's* own name has survived every
rename this repo has hit, so there is still something stable to search on. Once found,
`methodInfo.DeclaringType` is a real `Type` that `MakeGenericType` accepts exactly like
a named one, which is how `Singleton<>.Instance` gets reached for a class with nothing
to write inside the angle brackets. See `ItemArt.FindCreateItem`.

A pinned token was tried first and **shipped broken in 1.2.0 and 1.2.1**.
`Module.ResolveMethod(0x06009726)` addresses the member directly, so it ignores names
entirely -- and it is correct only for the exact copy of `Assembly-CSharp.dll` it was
read out of. It verified against `C:\HUH` twice, by two people using two different
tools. On a player's clean install it came back as something that was not a
`MethodInfo`, and the cast threw `Specified cast is not valid` before an icon could be
drawn.

**Verifying a token on the machine you read it from cannot detect what is wrong with
it.** That is the trap, and it is not obvious: the check passes, confidently, and says
nothing about any other install. Hardcoding an offset, index or ordinal read out of a
game file is the same mistake wearing different clothes.

### Writing the name down is the same trap as pinning the number

**A compile-time call into an obfuscated class puts its garbled name in our assembly**, and
that name is no more portable than a token. `ProfileSync` called
`ClientAppUtils.GetMainApp()?.GetClientBackEndSession()` -- perfectly ordinary C# -- and the
compiler wrote a typeref to `U+EA28` because that method's signature names a renamed class.
Mono resolves a typeref the first time the instruction using it runs, so it failed on a game
build that was not the one it compiled against, and **shipped that way in 1.2.6**:

```
TypeLoadException: Could not resolve type with token 01000068 from typeref
```

It threw in `SlotPanel.Settled` ahead of everything presentational, so every table paid out
correctly and then drew nothing -- and it was invisible until 1.2.6, because the old code
gave up before reaching the line and left the typeref unresolved.

Reach such a member by reflection off a type that *does* have a name (`TarkovApplication`),
and hold the result in an interface that has one (`IClientSession`). `GetMethod` walks base
types, so an inherited method needs nothing extra.

**Check the built plugin for obfuscated names before shipping it.** They are private-use
characters, so a byte scan of the DLL for UTF-8 `U+E000`-`U+F8FF` finds every one:
`Casino.Client.dll` must come out at zero. The first 1.2.6 build carried two, and nothing
else would have caught them.

`ilspycmd` is still the right tool for *finding out what to search for* --
`dotnet tool install -g ilspycmd`, then `--dump-table MethodDef <dll>` to list members
with their tokens, or `-m 0x0600XXXX <dll>` to decompile one by token and see its real
body even when its declaring type has no name to show it under. Use it to learn the
member's shape, then write the shape into the code, not the number.

And before shipping such a search, load the real assembly in a throwaway
`MetadataLoadContext` harness and **count the matches**. `FindCreateItem` was checked
that way and matches exactly one method out of 15,136 types, which is the difference
between a search and a guess.

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

## The one-off gift, and why the server owns it

1.2.6 pays every profile 1,000,000 roubles the first time it opens the casino, as an
apology for the pull. It is the only money in this mod that comes from nowhere rather
than out of somebody's stake, so it is also the only money with a way to be paid twice.

```
src/Casino.Server/GiftLedger.cs    who has been paid. No file, no clock, no SPT types
src/Casino.Server/GiftStore.cs     that ledger, plus data/gifts.json and the lock
src/Casino.Server/GiftBank.cs      credit only. Splits stacks, posts the shortfall
src/Casino.Server/GiftService.cs   claim first, pay second
src/Casino.Server/CasinoRouter.cs  /casino/gift/status, /casino/gift/claim, CasinoSync
src/Casino.Client/CasinoGift.cs    the card, drawn like the welcome card above it
```

**The flag lives on the server, not beside the plugin.** `CasinoIntro` keeps its own in
`seen.txt`, which is fine for a card that costs a second reading if it is lost. The same
arrangement here would be a file players could delete for another million. The client is
told what is *pending* and is never believed about what was *paid*.

**Claim first, pay second.** The two failures are not equal. Paying twice invents roubles
and there is no taking them back; a payment that fails after the claim is recorded is
recoverable, logged, and visible in `gifts.json`. So the claim is written before the
money moves, and put back only on the one path that knows nothing moved -- a bank that
threw, or a payment that landed nowhere at all. **Money that reached the message tab is
already the player's and must never be released**, or the mail and a second payment
together are worth two million. There is a test for exactly that.

**A record that cannot be trusted pays nobody.** An unwritable mod folder, or a
`gifts.json` that will not parse, both mean a payment cannot be remembered -- which would
pay the gift again on every open, for ever. Both switch the gift off instead, and the
corrupt file is deliberately *not* overwritten with an empty one, because it is the only
evidence of what was already paid.

**The key is not the mod version.** `GiftOffer.Key` is `1.2.6-apology`, frozen. Bumping
to 1.2.7 must not hand everyone another million, and a future gift is a new key rather
than a migration.

Written the way `docs/roulette.md` asks for: 24 tests, then mutation-tested against nine
deliberate faults -- pay twice, pay on a refused claim, pay with no record, release money
that was posted, forget to release money that was not -- and all nine caught.

The card claims as it *opens*, not when CONTINUE is pressed, so escape or alt-F4 over the
top of it cannot cost a player the gift. That is also why a double click on the tab is a
real race rather than a hypothetical one, and why the store's lock is around the whole
read-decide-write rather than around the write.

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
