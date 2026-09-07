# Slots -- working notes for Claude

A five-reel slot machine for the SPT hideout, and the fourth table in **SPT Casino**.
Server mod in C# (.NET 10) against SPT 4.1.3; the panel is compiled into the one
casino plugin. It plays for **roubles, dollars or euros** -- the first table here that
takes anything but roubles.

**The reels actually spin.** That was a stated requirement rather than polish; see
"The reels". There was a draggable lever too, for one afternoon, until it was replaced
by a SPIN button at the player's request -- see "The lever, and what it cost".

Fourth in a family. Blackjack, Poker and Roulette between them already solved the
money, the two transports, the entrance and escape handling. Nearly all of this
table's server half is a port; the maths and the two views are the new work.

**Update "Current state" when you finish a piece of work.** Poker's notes went four
commits claiming its server did not exist, and a fresh session reads that section
first and believes it.

---

## The single most important fact about this table

**The house edge is computed, not measured.** `Odds.ReturnToPlayer()` returns
**92.510%** from a closed form over the strips and the paytable, and it is the number
the paytable was solved backwards from. A Monte Carlo run agrees with it, but the
Monte Carlo is a *check on the formula*, not the source of the number. If you change a
strip or a payout, the RTP moves, and the test that guards it will say so.

The first paytable I wrote returned **681%**. It was written with payline instincts on
a ways machine, where counts *multiply* -- three reels showing two of a symbol each is
eight wins, not one. That mistake does not look like a mistake until you compute it.

## What a "ways" machine is

Five reels, three rows, and **no paylines**. A win is any symbol appearing anywhere in
the visible rows on consecutive reels **starting from the leftmost**. The number of
ways is the product of the row counts:

```
3 x 3 x 3 x 3 x 3 = 243 ways
```

A win pays `multiplier x stake x ways`, where `ways` is the product of how many times
the symbol shows on each reel of the run. So a symbol appearing twice on reel 1, once
on reel 2 and three times on reel 3 is a 3-reel win worth `2 x 1 x 3 = 6` ways.

Every symbol is evaluated independently and all of them can pay on one pull. That is
normal for this format and it is where the 681% came from -- the multipliers have to
be small because there are so many chances at them.

### The exact return, without enumerating anything

For symbol *s*, a run of exactly *L* reels means it appears on reels 1..L and **not**
on reel L+1. Expected ways over a run is the product of expected counts, because the
reels are independent:

```
E[win_s] = SUM over L of  pay(s,L) x PRODUCT(i<L) E[count_i]  x  P(count_L = 0)
```

`E[count_i]` is `3 x (stops of s on reel i) / 30`, and `P(count_i = 0)` is the
hypergeometric chance that none of the three visible stops is *s*. Sum over the nine
symbols, divide by the stake, and that is the RTP. No simulation, no enumeration of
24 million grids. `Odds.cs` is that formula and nothing else.

## The strips

`Reels.cs`. Five reels of **30 stops**, nine symbols, three rows visible.

```
        Cola Salewa Moon Tetriz Watch Roost Gpu BTC Key
reel 1    5     5     4     4     4     3    2   2   1
reel 2    5     5     4     4     4     3    2   2   1
reel 3    5     5     5     4     4     3    2   1   1
reel 4    6     5     5     4     4     3    1   1   1
reel 5    6     6     5     4     4     2    1   1   1
```

**Every symbol appears at least once on every reel**, and there is a test that says
so. An earlier draft had the top symbol on zero stops of every reel, which does not
make the top prize rare -- it makes it *impossible*, and nothing about the machine
looks wrong when you play it.

`Strip()` throws if any count is below 1 or the total is not 30. Symbols are spread
around the strip with a stride of 7 using a `bool[] taken` array. It used to use
`default(Symbol)` as an "empty" marker, which is the first symbol of the enum -- so
that symbol's positions were silently overwritten by every later one.

## The paytable

`Paytable.cs`, multipliers on the stake, for 3 / 4 / 5 reels. `MinRun = 3`.

| Symbol | Enum | 3 | 4 | 5 |
| --- | --- | --- | --- | --- |
| Can of TarCola | `Cola` | 1 | 1 | 1 |
| Salewa first aid kit | `Salewa` | 1 | 1 | 2 |
| Fierce Hatchling moonshine | `Moonshine` | 1 | 2 | 2 |
| Tetriz portable game console | `Tetriz` | 1 | 2 | 5 |
| Roler Submariner gold watch | `Watch` | 1 | 2 | 5 |
| Golden rooster figurine | `Rooster` | 2 | 4 | 12 |
| Graphics card | `Gpu` | 5 | 20 | 80 |
| Physical Bitcoin | `Bitcoin` | 10 | 50 | 250 |
| TerraGroup Labs keycard (Red) | `Keycard` | 25 | 150 | 1000 |

**The symbol set has been renamed twice and the maths has never moved.** First from
placeholder loot names (bandage, crackers, screwdriver) to a set matching some drawn
art, then to this one -- chosen to be worth looking at, and to climb, after the second
set turned out to be medkits and ammo boxes, which is what a Tarkov player already
scrolls past. The strips and the multipliers were untouched both times, so the 92.510%
is untouched, and the test that guards the return is what proves it was only a rename.

Solved numerically against the strips to land on 92.5%. The low symbols pay about what
they cost because they hit constantly; the top of the table is where the machine is
worth pulling.

## The money

Ported whole from Roulette -- `Bank.cs`, `Escrow.cs`, `ProfileGateway.cs`,
`Abstractions.cs` -- and the order in `SlotService.PullAsync` is the one all four
tables arrived at:

1. **Check first.** Unknown currency, a stake the wallet does not take, a balance that
   will not cover it. Nothing is recorded on a refusal.
2. **Record the stake in escrow, before it is taken.** A crash here leaves a record of
   money owed. The other order leaves a window where the stake is gone and nothing
   says so.
3. **Debit.** A failure releases the escrow and refuses: nothing moved.
4. **Settle** -- the reels landing and the paytable being read.
5. **Credit, then release.** A crash between them refunds a stake that was also paid
   out, which is the safe way round. The other pays nothing and forgets it was owed.
6. **Save.** Money not flushed to disk did not move.

**There is no state between pulls.** No seat, no hand, nothing to abandon, which is
why this service is the shortest of the four and why it has no store.

### Three currencies

`Wallets.cs`. The wallet is refused **by name** rather than parsed with a default --
`Enum.TryParse` on an unknown string leaves the value at zero, which here is Roubles,
so a typo would spend a currency the player never chose.

| Wallet | Min | Max | Step |
| --- | --- | --- | --- |
| Roubles | 5,000 | 50,000 | 5,000 |
| Dollars | 50 | 500 | 50 |
| Euros | 50 | 500 | 50 |

**Step is what the +/- buttons move by, and nothing else.** `Allows` takes any whole
amount between the two ends. It used to insist on a multiple of the step as well, back
when the panel only offered a button that walked them -- but the stake can be typed
now, and a machine that refuses 7,500 roubles for no reason a player can see is a
machine that looks broken.

Both ends are still checked on the server rather than trusted from the panel. The
ceiling is what keeps a thousand-times payout to a sane number: at 50,000 a five-reel
keycard already returns 50,000,000.

## The client

`src/SlotMachine.Client/`, compiled into `Casino.Client` like every other table.
Three files: `SlotPanel`, `ReelView`, `SlotApi`.

**Everything lives inside one frame.** The first layout scattered pieces across a
full-screen canvas at hand-picked coordinates, and it hid a crash: see below.

### The reels

`ReelView.cs`, and the only genuinely hard part of this table.

**The cells never move. The symbols do.** Nine cells sit at fixed positions behind a
`Mask`ed three-cell window. What travels is a read head over a strip: cell *i* shows
`strip[i - floor(position)]`, and the column slides by the fractional part of the
position. Advance the position by one and every symbol has moved down exactly one
cell, seamlessly, for as long as you like.

The version before this recycled cells -- moved the lowest to the top and gave it a
new face. It looked right and it was wrong: **once a cell has been moved, its array
index no longer says where it is**, and the landing symbols were being written to
indices 3, 4 and 5 on the assumption that those were the window. They were, until the
first recycle. With fixed cells, 3, 4 and 5 are the window always and that class of
bug cannot happen. It was never seen on screen; it was found rewriting the motion.

**The motion is a real reel's, not a wheel winding down.** A physical reel snaps up to
speed, holds flat out, decelerates into its stop, and thumps against the detent. The
first version eased off from the first frame, which is a completely different thing to
watch. So:

| | |
| --- | --- |
| `SpinUp = 0.10` | smoothstep from a standstill to full speed |
| `HoldUntil = 0.62` | flat out, which is most of the spin |
| then | a squared ease-out, long, where all the tension is |
| `Overshoot = 0.16` | of a cell past the stop, sprung back over `SettleSeconds` |

That last bounce is the difference between stopping and *landing*.

`Travelled(u)` is the **exact integral** of that profile rather than a per-frame
accumulation, so the reel arrives on its stop to the pixel on a machine dropping
frames as well as on one that is not.

Two other numbers matter:

- **`PeakCellsPerSecond = 22`**, chosen under `MaxCellsPerFrame = 0.85`. Past about a
  cell a frame the belt stops being a blur and becomes a row of separate pictures --
  the same strobing that took three rounds to find on the roulette ball. 22 is 0.73 of
  a cell at 30fps and 0.37 at 60, so it holds up on a bad frame rate too.
- **`Stagger = 0.34`.** Each reel runs longer than the one before it, so they come to
  rest left to right. Five reels stopping together reads as a picture appearing rather
  than as anything spinning.

The total travel is **rounded to whole cells** and the landing symbols are written
into the strip at the place the reel will rest on, so the reel is spinning towards its
answer from the first frame. Nothing is swapped in at the last moment.

The belt scrolling past is random rather than the true 30-stop strip -- nobody can
read it at speed -- but it is **weighted low** (two draws, keep the cheaper), because
a belt with as many keycards on it as medkits reads as a machine about to pay out.

**The server settled the pull before the first frame drew.** The spin is theatre over
a fact, which is the only honest arrangement: reels that chose where to stop would be
reels the client could be made to lie with. Same as the roulette wheel.

### The lever, and what it cost

There was a draggable lever: press, drag down, and past 45% of its throw it fired and
sprang back. It is gone -- the player asked for a SPIN button -- and it is worth a
section anyway, because of how it failed.

`LeverView.Build` did this:

```csharp
var arm = NewBox("Arm", root, Color.clear);   // NewBox already adds an Image
var grab = arm.gameObject.AddComponent<Image>();
grab.color = new Color(0f, 0f, 0f, 0.004f);   // NullReferenceException
```

**`Graphic` is `[DisallowMultipleComponent]`, so `AddComponent<Image>` returns null**
on an object that already has one. Not an exception, not a compile error: a null, and
the NRE lands a line later on something that looks unrelated.

What made it expensive was the layout. `Build` threw halfway down, `Open` caught and
logged it, and the half that had been built -- title, cabinet, reels -- **looked like a
finished panel with a few things missing**, so the report that came back was "the
paytable isn't showing" rather than "it crashed". The paytable, the stake line, the
status and the buttons had simply never been created.

Two lessons, both now in the code:

* **Build the whole panel inside one frame**, positioned from that frame's edges. A
  partial build then leaves an obvious hole rather than a plausible panel.
* **Check the log first.** It said `NullReferenceException at LeverView.Build` on the
  first line anybody looked at.

### The spin button

Where the lever was, on the right of the reels: a red disc that says SPIN, and greys
to `...` while the reels are turning. `Pull` refuses a second spin anyway; the greying
is so the machine looks like it is refusing rather than like it missed the click.

### The win lines

**A 243-ways machine has no paylines.** That is the whole difference between it and
the twenty-line machines the lines are borrowed from: a win is any position on each
reel, so there is no fixed set of paths to print down the side of the cabinet, and
none of them exist until the reels have stopped.

So they are drawn afterwards. **The frames do most of the work and the lines do the
rest** -- every winning symbol gets a rounded outline with a wash of the win's colour
inside it, and each way gets a polyline through the middle of the symbols it claims,
with a numbered badge on the left. Capped at `MaxLines = 12`, because a big win runs
to dozens; the rest are counted in words -- "Showing 12 of 27 ways".

**Every line of a win runs through the same cells**, so drawn where they fall they sit
on top of each other and a win on eight ways looks like a win on one. They are spread
evenly across a 64-unit band inside the symbol instead, the way a payline machine
spaces its lines: parallel where they share a row, separating where they do not. One
line runs dead centre; more fan out either side. `MaxLineGap` caps it, or two lines
would take the whole band and run along the top and bottom edges of the symbols rather
than through them.

That needs the count *before* anything is drawn, so `DrawWinLines` plans every way
first and draws second. Each way gets its own colour, and the numbered badges are
dropped past `MaxBadges = 8`, where they stack into a pile -- a way has no name the way
a payline does, so the numbering is a convenience rather than a fact about the game.

The first version was lines alone, 4px and hard-edged, and it read as a scratch on the
screen. Three things fixed it:

* **Frames.** A line tells you the shape of a way; a frame tells you which symbols are
  in it, and the second is what a player actually looks for. Drawn once per win rather
  than once per way -- nine identical outlines stacked on one symbol turn the edge into
  a smear.
* **A dark halo under the line**, 3.5 units wider. The line crosses a bright rouble
  stack and a dark grenade in the same run, and a single colour cannot sit on both.
* **A dot at every corner.** Two rotated rectangles meeting at an angle leave a notch
  on the outside of the turn. A notch on every corner was most of what looked broken.
  It is what a line renderer would call a joint.

The ways are **worked out on the client**, from the grid and the winning symbol: which
rows hold it on each reel it ran through, then every combination of those. That count
is exactly what the server calls `Ways`, arrived at independently -- so a line through
anything but matching symbols means the two disagree and one of them is wrong. It is a
free cross-check on the settlement, drawn on screen.

uGUI has no line renderer. A segment is a thin `Image` with its pivot on the left,
sized to the gap and rotated to face along it, which is the whole of what a line
renderer would be.

### The stake box

Typed, with a minus and a plus either side and the currency beside it.

**Three things make the focus visible**, and none of them was enough alone. The caret
at its default single pixel is invisible on a 1440p screen, so it is three wide, gold,
and blinking. `onFocusSelectAll` paints the whole number in a gold block the moment the
box is clicked, which is the part that actually answers "where did my click go".
And the border lights gold on focus through a `SpriteState`, which says the box has the
keyboard before anything has been typed.
 A stepper alone
cannot express "I want to spin for 12,345", which is what prompted the server to stop
requiring multiples of the step.

Built by hand, because there is no prefab to instantiate: a background image, a
viewport to clip against, a `TextMeshProUGUI` inside it, and a `TMP_InputField`
pointed at both. Miss `textViewport` and the caret is placed relative to nothing; miss
`targetGraphic` and clicking the box does not focus it.

What is typed is **clamped, not refused**. Somebody who types 90,000 into a machine
whose ceiling is 50,000 meant "as much as it takes", and putting 50,000 in the box
tells them what that is. The box is always rewritten from the accepted value, through
`SetTextWithoutNotify` -- assigning `.text` raises `onEndEdit` on some paths, and a
setter that calls the handler that calls the setter is a loop waiting for an excuse.

### The paytable down the side, and measuring instead of nudging

Nine rows, richest first: the artwork, the name, and what 3, 4 and 5 of them pay --
written as `25x 150x 1000x` rather than as bare numbers in unlabelled columns. A
paytable nobody can read is a machine that looks like it pays at random.

**Every number comes from the ping response.** Nothing about the payouts is written
into the client, so the panel cannot advertise something the machine does not give.
`NameOf` is the one exception and it is presentation only -- it maps `Keycard` to
"LABS KEYCARD" and falls through to the server's own name for anything it does not
recognise, so a symbol added on the server shows up on an old client looking plain
rather than looking broken.

The columns are **derived from the panel's own width**, not typed in. The version that
was typed in had the names starting five units to the *left* of the icons they were
labelling, which is exactly the kind of thing a hand-picked offset does and a
subtraction does not. The layout is now: inset, icon, a stated gap, then the name
filling whatever is left before the first figure column. Same for the three columns of
multipliers -- they are three right-aligned labels at computed positions, because the
padded-string version (`$"{pays[0],4}x"`) only lines up in a monospaced font and the
game's font is not one.

The names are also capped with `TextOverflowModes.Ellipsis`. Font metrics are not
something to take on trust, and a name that outgrows its column should lose its tail
rather than run into the numbers.

### The stash is told late

`SlotPanel.Resync` holds the `SlotsSync` item event until the reels stop. The money
already moved -- the server took the stake and paid the win before the panel drew a
frame -- so telling the game straight away would show the result in the rouble counter
behind the machine while the reels were still turning. **Roulette learned this with its
wheel, twice.** Closing mid-spin settles the debt on the way out.

## The art

**The reels show the game's own item icons.** `ItemArt.cs`, and it is worth reading
before touching anything near it.

Tarkov does not ship item icons as pictures. It *renders* them: the item's 3D model,
posed by a camera, into a texture. `ItemIconCreator` is that, and
`ItemViewFactory.GetItemSpriteAsync` is the front door -- the same call the stash and
the flea market make for every icon anybody has ever seen in the menu. So:

```csharp
Singleton<ItemFactory>.Instance.CreateItem(MongoID.Generate(true), template, null)
ItemViewFactory.GetItemSpriteAsync(item, ScaleFactor)   // -> Task<Sprite>
```

All three types are public and unobfuscated. **None of it was remembered** -- the call
shape was read out of `Assembly-CSharp.dll` with Mono.Cecil (`EFT.StashSizeBonus` is
the clearest example of the `Singleton<ItemFactory>` pattern), and the template ids
came out of `SPT_Data/database/templates/items.json`. That mattered: the id that comes
to mind for "BEAR dogtag" is the USEC one, and the Labs keycard has two plausible ids
of which only one is violet.

| Symbol | Template | Item |
| --- | --- | --- |
| `Medkit` | `5755356824597772cb798962` | AI-2 medkit |
| `AmmoBox` | `6570254fcfc010a0f5006a22` | 7.62x51mm M61 ammo pack (20) |
| `Grenade` | `5710c24ad2720bc3458b45a3` | F-1 hand grenade |
| `Helmet` | `5ac8d6885acfc400180ae7b0` | Ops-Core FAST MT (Urban Tan) |
| `DogTag` | `59f32bb586f774757e1e8442` | Dogtag BEAR |
| `Roubles` | `5449016a4bdc2d6f028b456f` | Roubles |
| `GpCoin` | `5d235b4d86f7742e017bc88a` | GP coin |
| `Bitcoin` | `59faff1d86f7746c51718c9c` | Physical Bitcoin |
| `Keycard` | `5c1e495a86f7743109743dfb` | TerraGroup Labs keycard (Violet) |

**Nothing here ships BSG's art.** The icons are made on the player's own machine out of
their own installation, which is the honest arrangement and the reason the mod does not
carry a folder of somebody else's pictures.

A rendered icon is cached as a PNG in `symbols/ingame/` beside the plugin, so the
second launch reads a file instead of posing a camera at a rooster. `pack.ps1` never
touches that folder: it removes only files from its own manifest, and these are written
at runtime.

**The file is named for the template id, not the symbol name.** The name is what this
build calls the symbol; the id is what the picture is of. Keying on the name breaks the
moment a symbol keeps its name and changes its item -- which is exactly what `Keycard`
did when it moved from the violet Labs card to the red one, and the cache would have
gone on serving a violet card under a symbol that had become red.

### There is no second set of pictures, deliberately

The mod used to ship nine drawn stand-ins as a fallback, and they worked -- which was
the problem. They were good enough to look like the machine's symbols, so opening the
panel showed nine items and then, a moment later, nine **different** items as the real
icons arrived. A machine that changes its mind about what is on its reels is worse
than one that takes a second to fill in.

So they are gone, and the fallback is deliberately not an item: a plain dark tile that
reads as "nothing here yet". The panel **will not spin** until every symbol is in hand,
says so, and greys the button.

**And the blanks are not drawn either.** A row of grey boxes reads as unfinished, so
`ReelView.ShowSymbols(false)` hides the symbols while leaving the reel frame and the
windows in place -- a machine with dark windows reads as one that has not been switched
on, which is what it is. The paytable's icons are hidden the same way. Hiding the whole
reel block instead would leave a hole in the cabinet.

Two things keep that from being a trap:

* **`PrimeFromDisk` runs before `Build`, not after.** The fetch is a coroutine, so it
  cannot run until the frame after the panel exists -- even a cache hit meant one frame
  of blanks and then a swap. Reading the files synchronously first means that on every
  launch but the very first, the first frame the reels draw is already the real icons.
* **`MaxAttempts`.** A symbol the game refuses is recorded as given up on rather than
  left pending, and after three fruitless passes the whole set is. The machine is then
  playable with blank tiles and a warning in the log -- poor, but a great deal better
  than a panel that can never be used.

### Caching them, and a guard that never passed

The first version refused to cache any sprite whose `textureRect` was not its whole
texture, on the reasoning that cropping was risky. **All nine failed that test** -- the
icons are regions of an atlas -- so the cache never held a file and every launch
re-rendered all nine, silently. A guard that never passes is not a safe guard, it is a
disabled feature, and the log line saying "9 drawn by the game, 0 from the cache" was
the only sign.

It crops now, two ways round: `GetPixels` over the sprite's rect where the texture
allows it, and otherwise a `Blit` that applies the crop as a UV scale and offset into a
render texture the size of the sprite, followed by a **full-surface** `ReadPixels`.
Full-surface is the point -- reading a sub-rectangle is exactly where the two
coordinate conventions disagree about which way is up, and reading all of it cannot.

`assets/tile-slotmachine.png` in `Casino.Client` is the lobby tile.

## Conventions worth not rediscovering

- **The config is `slots.config.json`, not `slotmachine.config.json`.** It is named for
  the routes (`/slots/ping`, `/slots/pull`), not the folder. `pack.ps1` carries an
  explicit table-to-config map because of it.
- `TableInfo` is **not** an `IModMetadata`. One folder, one metadata -- see the root
  `CLAUDE.md`.
- Request bodies are PascalCase. SPT binds case-sensitively, so lowercase keys bind
  nothing and every field silently takes its default.

## Installing while the server is up

`pack.ps1` **skips the whole server half if any of its assemblies is locked**, warns,
and installs the plugin anyway. The server holds its DLLs open, most edits here are to
the client, and demanding a shutdown for a panel tweak is how a build script teaches
somebody to stop running it.

It also removes files it no longer produces. `Copy-Item` merges rather than replaces,
so eight renamed symbol files sat in the plugin folder after the art landed until this
was dealt with.

**How it decides what is stale is the part worth keeping.** The packer writes
`.casino-installed.txt` -- a manifest of what it put there -- and on the next run
removes only files that are in the last manifest and not in this build. Nothing else
is ever touched.

Three earlier attempts, all wrong, in the order they were wrong:

1. **Empty the folders and copy.** Would have deleted `data\`, where the house records
   what it owes an interrupted player.
2. **Delete anything the stage does not contain.** Deleted `seen.txt`, the list of
   profiles that have read the welcome card -- and would delete `symbols/ingame`, the
   rendered item icons. Both are written at runtime by the mod and have never been in
   a stage.
3. **Compare hashes and refuse on a mismatch.** Fired every single time, because **two
   builds of unchanged sources do not come out byte-identical here** even with
   deterministic builds on.

A manifest cannot make mistake 1 or 2, because it only knows about files the packer
itself put there.

## Verifying

```
dotnet test tests/SlotMachine.Game.Tests     # 17: strips, ways, paytable, RTP
dotnet test tests/SlotMachine.Server.Tests   # 15: the money path
```

The engine tests include a **two-million-pull Monte Carlo** cross-checking the
computed 92.510%. It is slow by the standards of the rest of the suite and it is worth
it: it is the only thing that would catch the closed form and the settlement drifting
apart.

The money path is **mutation-checked**, and was re-run after the stake rule changed.
Nine deliberate breakages -- escrow never
released, the stake paid back instead of the win, a failed debit ignored, an unknown
currency quietly becoming roubles, a reply reporting a payout the wallet never got --
and **9 of 9 were caught, 0 survived**. The script is in the scratchpad pattern used
for Roulette; rerun it after changing `SlotService`.

## Current state

**2026-09-06.** Server and client both complete and installed. Not yet played in game.

- Engine: 17 tests. RTP 92.510%, computed and simulated.
- Server: 15 money tests, mutation-checked 9/9. Routes `/slots/ping` and `/slots/pull`,
  item event `SlotsSync`.
- Client: panel, reels, SPIN button, stake stepper, currency switch, a paytable read
  from the ping response, and win lines drawn over the reels. Fourth tile in the lobby.
- Art: the game's own item icons, rendered on the player's machine and cached beside
  the plugin under their template ids. No stand-ins at all, and nothing drawn on the
  reels until every icon has landed.
- The stake is typed, and the server takes any whole amount between the two ends.
- `pack.ps1` builds and installs it with the rest of the casino.

### Seen on screen once

2026-09-06, and it found the lever crash above. What is still unwatched:

- Whether the reels read as spinning at the game's framerate, or strobe. If they
  strobe, `PeakCellsPerSecond` is the dial and `MaxCellsPerFrame` is the reason.
- Whether the overshoot-and-settle reads as a thump or as a wobble. `Overshoot` and
  `SettleSeconds` are one dial between them.
- Whether the paytable is legible at 1080p. It is 372 units wide beside a 680-unit
  cabinet, which fits, but the type is small.
- Whether the lever's throw is reachable at 1080p and at ultrawide -- it is positioned
  relative to the reel block, not the screen.
- Whether the rouble counter behind the panel gives the result away. It should not:
  `Resync` is deferred. Roulette needed two goes at this.
- Whether the win lines read at a glance or as a tangle. `MaxLines` is the dial, and
  `LineWidth` the other one.
- Whether the frame is a sensible size on an ultrawide. It is 1240x700 against a
  1920x1080 reference matched on height, so it scales with the height and leaves more
  margin the wider the screen gets.
- A LEDX five-of-a-kind has never been seen and will not be for a long time. The
  payout-splitting path in `Bank.Credit` for very large wins is still unexercised
  here, as it is in Roulette.

### Open items

- No autoplay, and no plans for one.
- The icons render at `ScaleFactor = 3`, roughly 190px for a one-cell item. If they
  look soft on a 4K screen that is the number to raise.
- Whether the disk cache round-trips right way up. The `GetPixels` path cannot be
  wrong; the `Blit` fallback is the one to look at if a second launch shows an icon
  upside down.
- The reels are silent. A ratchet on the spin and a thump on each stop would do more
  for the feel than anything left on this list.
