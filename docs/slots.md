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

### The ceiling comes off, the floor does not

`Allows(wallet, stake, ignoreMaximum)`. **"No maximum stake" in the F12 menu** lifts
the ceiling and nothing else, exactly the way Blackjack's table maximum does -- the
client sends `IgnoreMaximum` on every pull, true or false, so the request says plainly
what was asked for, and the server is still the one that decides.

The maximum exists to keep a thousand-times payout to a sane number: at 50,000 a
five-reel keycard already returns 50,000,000. That is the house being careful on the
player's behalf, so a player is allowed to say no.

**The minimum is not negotiable and there are tests that say so.** A stake of zero is
a free spin at a machine that pays multiples of the stake; a negative one is a machine
that pays you to play. The mutation check carries two mutants for this now -- lifting
the ceiling for everybody, and ignoring the switch entirely -- and both are caught.

## The client

`src/SlotMachine.Client/`, compiled into `Casino.Client` like every other table.
Four files: `SlotPanel`, `ReelView`, `SlotApi`, `ItemArt`.

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

### The win banner

Above the reels, and **it says how big the win was by how big it is.** The first
version printed every win at the same 30pt gold, so ten times the stake and a thousand
times it looked identical and the machine had no top end.

| Multiple of the stake | | Size |
| --- | --- | --- |
| under 1x | just the number | 30 |
| 1x | `WIN` | 36 |
| 5x | `BIG WIN` | 44 |
| 20x | `HUGE WIN` | 54 |
| 100x | `JACKPOT` | 64 |

Multiples rather than amounts, because the stake is three currencies and, with the cap
off, can be anything at all: 100,000 is a rounding error on an uncapped spin and a
fortune on a minimum one. What the player feels is the multiple.

Anything that earns a word also gets a pop -- scale up past full size and settle back,
the same shape as the reels' own overshoot. A number that simply appears is a number
the eye has already finished reading.

**From `HUGE WIN` up, the banner runs a band of colour along itself.** TMP colours a
label as a whole, so `Rainbow()` reaches past that and writes the four vertex colours
of each glyph, giving every letter a hue a little further round the wheel than the last
and advancing all of them each frame. `ForceMeshUpdate` is called once and not per
frame: per frame it re-runs auto-sizing as well, and a banner that resizes itself sixty
times a second hunts visibly for a font size.

Twenty times the stake and up, deliberately. Every tier doing it would make it mean
nothing.

The banner is **600 units wide with auto-sizing**, which is not decoration: it is
centred on the reels, the reels are not centred in the frame, and a jackpot on an
uncapped spin can read `JACKPOT   +50,000,000,000`. Left to grow it would run over the
paytable. The layout was checked arithmetically -- banner rect 189..267 vertically,
reels top at 184, the ways line at 296, paytable right edge at -213 and the banner
stopping at -191.

### The stake box

Typed, with a minus and a plus either side and the currency beside it.

It is built by `Casino.Shared.MoneyField`, which both this and Blackjack's wager box
now use. **Each of them had grown half of it**: Blackjack could write `1,250,000` and
keep the caret in the right place while you typed, but its caret was TMP's default
single pixel and effectively invisible; slots had a caret you could see and no
separators, so a stake read `1250000` and had to be counted by eye.

**Three things make the focus visible** and none was enough alone: a four-pixel gold
caret blinking at 1.6/sec; `onFocusSelectAll`, which paints the whole number in a gold
block the instant the box is clicked and is the part that actually answers "where did
my click go"; and the border lighting gold through a `SpriteState`, which says the box
has the keyboard even between the caret's blinks.

**Separators move the caret, so the caret is counted in digits.** Inserting a comma to
its left shifts every character after it, so a caret kept by character index walks
backwards a place each time the number crosses a thousand -- which is exactly when
somebody is still typing. `Reformat` converts the position to "how many digits are
behind me", rewrites the text, and puts the caret back after that many digits.

The content type is `Standard`, not `IntegerNumber`: integer validation refuses to
display separators and would strip them straight back out. Digits are enforced by
`onValidateInput` instead, which does the same job and leaves this code's own
formatting alone.

**Nothing is clamped while you type.** Clamping per keystroke means somebody reaching
for 50,000 has it snapped to the minimum the instant they have typed a 5, so the ends
are applied when the box is left. A box left empty falls back to the wallet's minimum
rather than to the previous value -- restoring the old number would mean the box
disagreeing with what was just typed into it.
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
to mind for "BEAR dogtag" is the USEC one, and the Labs keycard has several plausible
ids of which exactly one is the red.

| Symbol | Template | Item |
| --- | --- | --- |
| `Cola` | `57514643245977207f2c2d09` | Can of TarCola soda |
| `Salewa` | `544fb45d4bdc2dee738b4568` | Salewa first aid kit |
| `Moonshine` | `5d1b376e86f774252519444e` | Bottle of Fierce Hatchling moonshine |
| `Tetriz` | `5c12620d86f7743f8b198b72` | Tetriz portable game console |
| `Watch` | `59faf7ca86f7740dbe19f6c2` | Roler Submariner gold wrist watch |
| `Rooster` | `5bc9bc53d4351e00367fbcee` | Golden rooster figurine |
| `Gpu` | `57347ca924597744596b4e71` | Graphics card |
| `Bitcoin` | `59faff1d86f7746c51718c9c` | Physical Bitcoin |
| `Keycard` | `5c1d0efb86f7744baf2e7b7b` | TerraGroup Labs keycard (Red) |

Chosen to be **worth looking at**, and to climb. The set before this was medkits, ammo
boxes and dog tags, which is what a Tarkov player already scrolls past.

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

The money path is **mutation-checked**, and was re-run after each change to the stake
rules. Eleven deliberate breakages -- escrow never
released, the stake paid back instead of the win, a failed debit ignored, an unknown
currency quietly becoming roubles, a reply reporting a payout the wallet never got --
the ceiling lifted for everybody, the F12 switch ignored -- and **11 of 11 were
caught, 0 survived**. The script is in the scratchpad pattern used for Roulette; rerun
it after changing `SlotService`.

One of those mutants silently stopped applying when `Allows` gained its third argument
and its anchor no longer matched. The script prints `SKIP (anchor not found)` for that
case, which is the only reason it was noticed -- **a mutation script that cannot find
its anchor is a test that is not running.**

## Current state

**2026-09-10. Shipped in 1.2.61, played, and confirmed working.** A balance readout
next to STATS and CLOSE, showing what the selected wallet currently holds. A player
asked for it -- the panel covers the stash counter, and there was previously no way to
see what you had left to spend while it was open.

The server was already sending this: `PingResponse.Balances` has existed since the
first version, read by `SlotService.Ping` straight off `IBank`. The panel simply never
looked at it. `RefreshBalances` reads it off the ping already fetched on open, and off
a fresh one after every settle -- **not** stake-minus-paid worked out on the client,
because a win big enough to overflow the stash posts the rest as mail (see the root
`CLAUDE.md`, "Where the money is") and only the server knows what actually landed.
Switching currency redraws the same cached figure rather than asking again, the same
as the wallet label beside it.

`SPT_CasinoV1.2.61.zip` is only the plugin version bumped for this -- Slots' own
`TableInfo.Version` ("0.1.0") was left alone, since that number describes the table
rather than the download and nothing about the table's own maturity changed.

**2026-09-09, evening. Rebuilt, republished, and played -- it works.** Win lines draw, the
stash moves, and a table survives more than one round.

1.2.6 had shipped broken. The plugin in the first archive named an obfuscated game class at
compile time, so `SlotPanel.Resync` threw a `TypeLoadException` on any install whose
`Assembly-CSharp.dll` was not the one it was built against. `Resync` runs in `Settled` **before** the try that guards presentation, so a
spin took the stake, paid the win, and drew no win lines, no banner and no result line --
and since all four tables share `ProfileSync`, one round left every one of them dead. See
"Writing the name down is the same trap as pinning the number" in the root `CLAUDE.md`.

Worth keeping: **the reported symptom was "the win lines don't show up"**, and the cause
was a line three statements earlier that had nothing to do with drawing. The log said so
on the first try -- `[Slots] could not settle the spin:` with the whole stack under it.

**2026-09-07.** Complete, installed, and played over several sittings. Every screen in
this file has been looked at on a real machine.

- Engine: 17 tests. RTP 92.510%, computed and simulated.
- Server: 27 money tests, mutation-checked 11/11. Routes `/slots/ping` and
  `/slots/pull`, item event `SlotsSync`.
- Client: panel, reels, SPIN button, stake stepper, currency switch, a paytable read
  from the ping response, and win lines drawn over the reels. Fourth tile in the lobby.
- Art: the game's own item icons, rendered on the player's machine and cached beside
  the plugin under their template ids. No stand-ins at all, and nothing drawn on the
  reels until every icon has landed.
  **Rendering was switched off between 8 Sep 2026 10:42 and 8 Sep 2026 (later the same
  day), then restored by token instead of by name.** `ItemArt.TryRender` looked up
  `Singleton<ItemFactory>` and `Singleton<ItemIconCreator>` by name; on EFT 0.16.9.5
  build 40743 `ItemIconCreator` resolves under an unreadable Unicode name and
  `ItemFactory` has no name at all -- its `Name` in the assembly's own metadata is the
  empty string, which blocked `Casino.Client` from compiling (Slots shares a plugin with
  the other three tables) and briefly took `TryRender` down to its documented
  "cannot draw anything" fallback for everyone. It turned out `ItemIconCreator` never
  needed naming at all: `ItemViewFactory.GetItemSpriteAsync`, the actual call site this
  file uses, is still a normal public method and resolves it internally. Only
  `ItemFactory.CreateItem` had to be reached another way. `ItemViewFactory
  .GetItemSpriteAsync`, the actual call site this file uses, is still a normal public
  method and resolves `ItemIconCreator` internally, so only the factory needed solving.

  **That was first tried as a pinned metadata token (`0x06009726`), and 1.2.0 and 1.2.1
  shipped broken because of it.** A token addresses metadata directly, so it does not
  care what anything is named -- but it is only correct for the exact copy of
  `Assembly-CSharp.dll` it was read out of. Against `C:\HUH` it verified perfectly, twice,
  by two different people using two different tools. On a player's clean install it
  resolved to something that was not a `MethodInfo` at all, and the `(MethodInfo)` cast
  threw before anything could be drawn:

  ```
  [Slots] item icon rendering is off: the factory token no longer resolves
          (Specified cast is not valid.). The reels will show blanks until it is re-pinned.
  ```

  **`ItemArt.FindCreateItem` describes the method instead of numbering it**, which is what
  it should always have done: a walk over every type in the module for a public instance
  method named `CreateItem`, taking `(string, string, <reference type>)` and returning
  `Item`. The *method's* name has survived every rename this repo has hit -- it is the
  declaring type that keeps losing its own -- so there is a stable thing to search on. The
  search was checked against the real assembly with a `MetadataLoadContext` harness before
  shipping: **exactly one method out of 15,136 types matches**, its declaring type's `Name`
  is the empty string, and it sits at `0x06009726` -- the very token that was pinned, which
  is why the pin verified locally and still failed in the field. `Module.GetTypes()` is
  wrapped for `ReflectionTypeLoadException`, since a heavily modded install can have types
  that will not load and none of that is this mod's business.

  The lesson is worth keeping: **a number read off one machine's copy of the file was
  never going to survive the fleet, and verifying it on that same machine could not
  detect that.** Describe the member; do not number it.

  Also fixed in the same release: `FetchAll`'s pass over the nine symbols used to `break`
  out entirely on the first symbol `TryRender` could not draw, rather than trying the
  rest. The same symbol failed first every pass, so three panel opens reached
  `MaxAttempts` and abandoned every symbol still unrendered -- including eight that had
  never been attempted -- for the rest of the session. It `continue`s now, so each symbol
  gets its own attempts.
- The stake is typed, and the server takes any whole amount between the two ends --
  or above the top one, with "No maximum stake" ticked in F12.
- The win banner scales with the multiple: WIN, BIG WIN, HUGE WIN, JACKPOT.
  **Each tier now has a matching sound cue** -- `SlotWin`/`SlotBigWin`/`SlotHugeWin`/
  `SlotJackpot` -- played from `SlotPanel.SetPaid` alongside the banner's own pop, off
  the identical multiple boundaries so the two can never disagree about which tier a
  spin landed in. `ReelView.Spin` plays `SlotReelSpin` once when all five reels start
  together and `SlotReelStop` once per reel as it individually settles. No audio files
  exist yet -- see "The sound boilerplate" in the root `CLAUDE.md` and
  `src/Casino.Client/assets/sounds/README.txt` for the exact file names this is
  waiting on.
- **AUTO**, under SPIN. Arms a run that keeps pulling on its own -- a beat after each
  settle, so a result is on screen for a moment -- until STOP is clicked, the panel is
  closed, or a pull comes back with an error. Green both ways; the label and the shade
  are what say which.
- `pack.ps1` builds and installs it with the rest of the casino.

### What playing it found

Every one of these was invisible until somebody looked at the screen, and most of
them are written up in full above:

- **The panel was crashing** and looked finished. `AddComponent<Image>` returns null on
  an object that already has one; `Build` stopped at the lever and everything after it
  was simply never created. The log said so on the first line.
- **The reels ate their own landing symbols** -- `Recycle` moved cells, so an array
  index stopped saying where a cell was. Found rewriting the motion, not on screen.
- **The icon cache had never written a file**, and the only sign was a log line reading
  "9 drawn by the game, 0 from the cache".
- **The paytable's names overlapped its icons** by five units, because both positions
  were hand-picked and disagreed.
- **The win lines stacked on top of each other**, so eight ways looked like one.
- **The stand-in art was good enough to be a problem**: the reels visibly changed their
  minds a second after opening.

What is still unwatched:

- Whether the reels strobe at a low framerate. `PeakCellsPerSecond` is the dial and
  `MaxCellsPerFrame` is the reason. They read as spinning at 120fps on a 3440x1440
  screen.
- **A five-reel keycard has never been seen and will not be for a long time.** The
  payout-splitting path in `Bank.Credit` for very large wins is still unexercised here,
  as it is in Roulette -- and the F12 switch that lifts the stake ceiling makes a win
  big enough to need it much more reachable.
- Whether `JACKPOT` at 64pt looks right, for the same reason: nobody has hit 100x.
- **AUTO has not been played in game.** It compiles and the button's position was
  measured against the same layout the paytable's was, underneath SPIN with room to
  the stake row below it, but nobody has clicked it on a real machine yet.

### Open items

- The icons render at `ScaleFactor = 3`, roughly 190px for a one-cell item. If they
  look soft on a 4K screen that is the number to raise.
- Whether the disk cache round-trips right way up. The `GetPixels` path cannot be
  wrong; the `Blit` fallback is the one to look at if a second launch shows an icon
  upside down.
- The reels are silent. A ratchet on the spin and a thump on each stop would do more
  for the feel than anything left on this list.
