# Slots -- working notes for Claude

A five-reel slot machine for the SPT hideout, and the fourth table in **SPT Casino**.
Server mod in C# (.NET 10) against SPT 4.1.3; the panel is compiled into the one
casino plugin. It plays for **roubles, dollars or euros** -- the first table here that
takes anything but roubles.

**The reels actually spin and the lever actually pulls.** Both were stated
requirements rather than polish. See "The reels" and "The lever".

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
        Med Ammo Gren Helm Tag  RUB  GP  BTC Key
reel 1   5    5    4    4   4   3    2    2   1
reel 2   5    5    4    4   4   3    2    2   1
reel 3   5    5    5    4   4   3    2    1   1
reel 4   6    5    5    4   4   3    1    1   1
reel 5   6    6    5    4   4   2    1    1   1
```

**Every symbol appears at least once on every reel**, and there is a test that says
so. An earlier draft had LEDX on zero stops of every reel, which does not make the top
prize rare -- it makes it *impossible*, and nothing about the machine looks wrong when
you play it.

`Strip()` throws if any count is below 1 or the total is not 30. Symbols are spread
around the strip with a stride of 7 using a `bool[] taken` array. It used to use
`default(Symbol)` as an "empty" marker, which is `Bandage` -- so the first symbol's
positions were silently overwritten by every later one.

## The paytable

`Paytable.cs`, multipliers on the stake, for 3 / 4 / 5 reels. `MinRun = 3`.

| Symbol | Enum | 3 | 4 | 5 |
| --- | --- | --- | --- | --- |
| AI-2 medkit | `Medkit` | 1 | 1 | 1 |
| 7.62x51 ammo box | `AmmoBox` | 1 | 1 | 2 |
| Grenade | `Grenade` | 1 | 2 | 2 |
| Helmet | `Helmet` | 1 | 2 | 5 |
| BEAR dog tag | `DogTag` | 1 | 2 | 5 |
| Rouble stack | `Roubles` | 2 | 4 | 12 |
| GP coin | `GpCoin` | 5 | 20 | 80 |
| Bitcoin | `Bitcoin` | 10 | 50 | 250 |
| Violet Labs keycard | `Keycard` | 25 | 150 | 1000 |

The set was placeholder loot names until 2026-09-06 (bandage, crackers, screwdriver
and so on) and was renamed to match the artwork that arrived. **The strips and the
multipliers did not move**, so neither did the 92.510%: it was a rename, and the test
that guards the return proves it was only a rename.

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

## The client

`src/SlotMachine.Client/`, compiled into `Casino.Client` like every other table. Four
files: `SlotPanel`, `ReelView`, `LeverView`, `SlotApi`.

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

### The lever

`LeverView.cs`. A real one. Press and drag and the arm follows your hand; let go past
`Commit = 0.45f` of its 132-unit throw and it fires and springs back, let go short of
it and it springs back without firing. **A control you can begin and then not commit
to is a different thing from a button**, and this one spends money.

It fires at the moment the arm *snaps back*, not at pointer-up, because the reels
starting as the handle flies up is the whole feel of the thing.

A click with no drag still counts as a pull, and there is a SPIN button beside it, for
anyone who does not realise the handle moves.

### The paytable down the side

Nine rows, richest first: the artwork, the name, and what 3, 4 and 5 of them pay --
written as `25x 150x 1000x` rather than as bare numbers in unlabelled columns. A
paytable nobody can read is a machine that looks like it pays at random.

**Every number comes from the ping response.** Nothing about the payouts is written
into the client, so the panel cannot advertise something the machine does not give.
`NameOf` is the one exception and it is presentation only -- it maps `Keycard` to
"VIOLET KEYCARD" and falls through to the server's own name for anything it does not
recognise, so a symbol added on the server shows up on an old client looking plain
rather than looking broken.

### The stash is told late

`SlotPanel.Resync` holds the `SlotsSync` item event until the reels stop. The money
already moved -- the server took the stake and paid the win before the panel drew a
frame -- so telling the game straight away would show the result in the rouble counter
behind the machine while the reels were still turning. **Roulette learned this with its
wheel, twice.** Closing mid-spin settles the debt on the way out.

## The art

Nine rendered items in `src/SlotMachine.Client/assets/symbols/`, **named for the
lowercase symbol name** -- that filename is the entire binding between the enum and
the picture, so renaming a symbol means renaming a file.

They arrived as 1254px transparent PNGs and are processed down to 168px squares:
cropped to the alpha bounding box first, so a grenade and a rouble stack end up the
same visual weight instead of one floating in its own padding, then centred on
transparency rather than stretched, because a squashed helmet reads as a bad
screenshot.

`FaceFor` falls back to a drawn box on a missing file -- a reel with holes in it looks
broken, where a plain tile looks like a symbol nobody has drawn yet.

`assets/tile-slotmachine.png` in `Casino.Client` is the lobby tile.

## Conventions worth not rediscovering

- **The config is `slots.config.json`, not `slotmachine.config.json`.** It is named for
  the routes (`/slots/ping`, `/slots/pull`), not the folder. `pack.ps1` carries an
  explicit table-to-config map because of it.
- `TableInfo` is **not** an `IModMetadata`. One folder, one metadata -- see the root
  `CLAUDE.md`.
- Request bodies are PascalCase. SPT binds case-sensitively, so lowercase keys bind
  nothing and every field silently takes its default.

## Verifying

```
dotnet test tests/SlotMachine.Game.Tests     # 17: strips, ways, paytable, RTP
dotnet test tests/SlotMachine.Server.Tests   # 15: the money path
```

The engine tests include a **two-million-pull Monte Carlo** cross-checking the
computed 92.510%. It is slow by the standards of the rest of the suite and it is worth
it: it is the only thing that would catch the closed form and the settlement drifting
apart.

The money path is **mutation-checked**. Nine deliberate breakages -- escrow never
released, the stake paid back instead of the win, a failed debit ignored, an unknown
currency quietly becoming roubles, a reply reporting a payout the wallet never got --
and **9 of 9 were caught, 0 survived**. The script is in the scratchpad pattern used
for Roulette; rerun it after changing `SlotService`.

## Current state

**2026-09-06.** Server and client both complete and installed. Not yet played in game.

- Engine: 17 tests. RTP 92.510%, computed and simulated.
- Server: 15 money tests, mutation-checked 9/9. Routes `/slots/ping` and `/slots/pull`,
  item event `SlotsSync`.
- Client: panel, reels, lever, stake stepper, currency switch, and a paytable read
  from the ping response rather than written into the panel. Fourth tile in the lobby.
- Art: nine rendered items, in. No placeholders left.
- `pack.ps1` builds and installs it with the rest of the casino.

### Not yet seen on screen

Nothing here has been played. The specific things to watch on the first run:

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
- A LEDX five-of-a-kind has never been seen and will not be for a long time. The
  payout-splitting path in `Bank.Credit` for very large wins is still unexercised
  here, as it is in Roulette.

### Open items

- No autoplay, and no plans for one.
- The reels are silent. A ratchet on the spin and a thump on each stop would do more
  for the feel than anything left on this list.
