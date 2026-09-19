# Horse Racing -- working notes

Read the root `CLAUDE.md` first. This file holds only what is true of this table.

**Update "Current state" when you finish a piece of work.** Poker's notes went four
commits claiming its server did not exist, and a fresh session reads that section first
and believes it.

---

## Current state

**2026-09-19, fourth pass. The race itself, from a screenshot.**

Four things, all found by looking at it rather than by any test:

- **Horses slid backwards.** Progress was `Pace(u) + Jostle(u)` -- the wobble added
  straight to the *position*. Early in a race `Pace` is still shallow, so when the sine
  turned over it fell faster than `Pace` rose and the net position decreased. Measured
  at up to 9 pixels. The wobble now warps *time* instead: progress is one
  strictly-increasing integral divided by another, which cannot run backwards, and
  `Pace` is applied on top. Both guarantees survive -- verified that every runner still
  reaches exactly 1.0 at its own finish time.
- **The field stopped short of the line.** The finish stagger was 9 pixels per place, so
  seven of eight horses ended up to 63 pixels behind the post and read as never having
  finished. They now straddle it over 35 -- the winner a little past, the last a little
  short, the way real ones cross and pull up.
- **The finish line did not cross the whole track.** It was stretched to the holder and
  trimmed to clear the furlong strip, leaving gaps at both ends. It is sized from the
  lanes now, and its pivot is centred so it sits exactly where a runner's centre lands.
- **"3 FACTORY FLYE".** The exacta button was drawn over the last 74 pixels of the
  second stepper. The pairs row now has explicit widths that end at 880 of 890.

**2026-09-19, third pass. Betting fixed, and the oval dropped for a straight.**

The second pass shipped a table nobody could bet at. `RenderBoard` was still reading
`_card["Runners"]` after the courses moved that key inside `Courses[i]`, so it matched
nothing, returned early and drew an empty board. **Nothing threw and nothing logged** --
an empty board is a perfectly valid thing to draw -- and the only symptom was a player
saying they could not bet. `CourseTests.ThePingCarriesEveryKeyThePanelReads` now pins
every key the panel reads and asserts the two that moved are gone from the top level, so
a stale read is a missing property rather than a silent null.

The ovals looked wrong and are gone. See "One shape, and there used to be two".

**2026-09-19, second pass. Three courses, and the panel relaid out.** The first
version had one course and a layout that overlapped itself; both are fixed. The engine
and money path have been played through the live server but **not yet through a game
client** -- see "What has not been checked".

- **Three courses**, the same eight horses at each: THE DASH (5f, straight), THE MILE
  (8f, one lap of an oval), THE MARATHON (2m, two laps). A horse's chance comes from
  its speed and stamina ratings weighted by the course, so the form inverts as the
  races get longer.
- Engine: 64 tests. The 336 prefixes sum to 1.0 **at every course**, and a 200,000-race
  Monte Carlo agrees with the computed chance at all three.
- Server: 38 tests, 12/12 mutants caught on the money path. Routes `/races/ping`,
  `/races/place` and `/races/stats`, item event `RacesSync`.
- Client: fifth tile in the lobby. Course tabs, two track renderers, board, slip, stake
  stepper, currency switch, and the result drawn when the last runner is home.
- Verified live against a running server on 2026-09-19: all three boards served, each
  108 spots with win chances summing to 1.0000000000, per-course ceilings enforced, and
  an unknown course refused by name.
- Verified against the built assembly: every racing type is in `Casino.Client.dll`, and
  the private-use byte scan comes back zero.
- `scripts/casino/pack.ps1` stages 11 assemblies and `horseracing.config.json`.

## Randomness

`RandomSource.Create()` returns `Random.Shared` -- .NET's `ThreadSafeRandom`, xoshiro256\*\*
under the hood, seeded per thread from a strong entropy source. Races are not
reproducible across restarts and concurrent requests cannot corrupt its state.

Audited over 2,000,000 races on 2026-09-19:

| | |
| --- | --- |
| Winner distribution | chi-square 2.63 on 7 df (5% critical value 14.07) |
| Every runner finishes exactly once | row/column sum error 2.2e-16 |
| Race N to race N+1 correlation | chi-square 49.16 on 49 df -- the expected value *is* 49 |

Worst single deviation was 0.24%. **There is no memory between races**: a losing run
does not make the next race kinder, and the edge is a flat 6% at every spot of every
course.

---

## The single most important fact about this table

**Every price on every board is exact, and both the board and the settlement compute it
the same way -- from the same course.**

`Odds.Chance` asks `Bet.Covers` which of the 336 ordered top-threes win, and so does the
settlement. Every method on `Odds` takes a `Track` and **there is no overload that does
not**: "the chance runner 7 wins" is not a question with one answer, it is 16.26% at the
dash and 2.59% at the marathon. A default course would let a caller price against one
card and settle against another. That is deliberate, and it is not a tidiness argument. The failure it
removes is the one where the board and the table quietly disagree about what a bet
means: a quinella priced as an ordered pair and paid as an unordered one is mispriced by
a factor of two, and **every individual payout would still be correct**, so no balance
check anywhere in this repo would catch it.

## The model, and why it was chosen

The finishing order is drawn by weighted sampling without replacement -- pick a winner
in proportion to weight, remove it, pick the next from what is left. That is
Plackett-Luce, and the reason it was chosen over anything more elaborate is that every
probability a punter can bet on falls out of it *exactly*, by enumeration, in
microseconds.

A simulated race -- speeds, stamina, a bit of noise per furlong -- would look better in
a devlog and would leave nobody able to state the house edge. This repo has said twice
already that a game whose return is only known approximately is a game whose edge
nobody actually knows.

On a field of eight there are `8 x 7 x 6 = 336` ordered top-threes, they are mutually
exclusive, their probabilities sum to one, and **every bet this table takes is decided
by the first three home**. So a bet's chance is the sum of the prefixes it covers. No
enumeration of all 40,320 full orders, and no simulation.

## The stable, and the three cards it makes

**A horse has no weight of its own.** It has a speed rating and a stamina rating, and
each course turns those into a weight with its own formula. That is the whole reason
there is more than one course: three cards of unrelated horses would be three separate
games sharing a panel, whereas this way the form is worth learning.

| # | Name | Speed | Stamina | DASH | MILE | MARATHON |
| --- | --- | --- | --- | --- | --- | --- |
| 1 | GRAY GHOST | 20 | 9 | **22.17%** | 19.40% | 12.95% |
| 2 | DOLLAR SIGN | 17 | 13 | 19.70% | **19.65%** | 17.62% |
| 3 | FACTORY FLYER | 12 | 16 | 13.79% | 16.42% | 19.69% |
| 4 | NIGHT RAIDER | 8 | 19 | 9.36% | 14.43% | **22.28%** |
| 5 | RESHALA'S PRIDE | 15 | 6 | 13.30% | 10.20% | 5.70% |
| 6 | SCAV LUCK | 5 | 15 | 2.96% | 6.72% | 14.51% |
| 7 | LABS LIGHTNING | 18 | 3 | 16.26% | 10.95% | 2.59% |
| 8 | LEFT BEHIND | 7 | 8 | 2.46% | 2.24% | 4.66% |

LABS LIGHTNING and SCAV LUCK are mirror images and are what the design is for: one wins
a race in six over five furlongs and one in forty over two miles; the other does the
reverse. DOLLAR SIGN is the class horse, never worse than third choice anywhere. LEFT
BEHIND is the rag, and a card needs one -- the longest price on every board involves it.

The courses:

| Course | Distance | Shape | Weight formula | Spread | Longest price | Slip max |
| --- | --- | --- | --- | --- | --- | --- |
| THE DASH | 5f | straight | 3*Speed + 1*Stamina - 24 | 45:5 | 1259.41 | 1,500,000 |
| THE MILE | 8f | oval, 1 lap | 5*Speed + 4*Stamina - 58 | 79:9 | 611.13 | 2,000,000 |
| THE MARATHON | 2m | oval, 2 laps | 1*Speed + 3*Stamina - 22 | 43:5 | 757.93 | 2,000,000 |

**The threshold is what makes a card interesting.** Without it the weights would be raw
scores like 69 and 29 -- barely two to one, every runner priced within a whisker of every
other. Subtracting a fixed amount stretches what is left to about nine to one, which is
roughly what a real card looks like. It is subtraction rather than a power curve because
it keeps the weights whole numbers, and whole numbers are what let a chance be an exact
ratio rather than a float already rounded twice.

`Track.MinWeight` floors a weight at 1 so a badly-treated horse cannot go negative -- a
negative weight makes the field's total *smaller* by entering it, which would corrupt
every other price rather than failing. `TrackTests` asserts no shipped course actually
reaches the floor, so it is a guard and not something in use.

The ratings were tuned against three constraints that `TrackTests` enforces: **no ties at
any course** (two identical prices is a choice nobody can make), **nothing at the floor**,
and **not all three courses may share a favourite**.

A price is the **total** returned per chip, stake included, not the profit.

**Eight runners, and that is the one number worth defending.** Exacta is an ordered pair,
so the field size squares: six runners give thirty exactas, twelve give a hundred and
thirty-two. Eight gives fifty-six -- enough that the bet is worth having, few enough that
the board stays readable.

## The takeout, and what it actually is

`Odds.Takeout` is 6%, one number applied identically to all 108 spots **at all three
courses**. That is the property worth protecting: a marathon quietly carrying twice the
edge of a dash would punish exactly the players who compare the boards most carefully,
and nothing on screen would say so. The courses differ in who wins, never in what the
house takes. **The realised
edge is 6.0002% to 6.4327%**, and the gap is the tick: prices are rounded *down* to 0.01,
which costs the player a little and costs them most on the short-priced favourite, where
the price is small enough for a hundredth to matter.

Rounding is applied to the price rather than to the payout, so the number on the board is
the number that settles the bet. A board showing 3.60 and a table paying 3.6127 is a lie
that happens to be in the player's favour, and it is still a lie. `Odds.Realised` reports
what the board actually carries rather than what `Takeout` intended, and `OddsTests`
asserts both ends of it on every spot rather than on a sample.

Six percent sits between roulette's 2.70% and the slot machine's, and is a long way
kinder than any real track, where a tote takeout of 15-20% is ordinary.

## The slip ceiling is per course, and it is arithmetic

**Each course carries its own `MaxSlip`, and the dash's is the lowest.** Not because the
house is more careful over five furlongs: the dash has the widest spread, so it has the
longest price (1259.41), and 2,000,000 at that price is 2.52 billion -- past the `int`
the engine counts chips in. An overflow does not pay a big win, it pays a negative one.

`TrackTests.TheSlipCeilingAtItsOwnLongestPriceStillFitsInAnInt` asserts this against each
real board rather than against the paragraph above, and also asserts that *doubling* the
ceiling breaks -- so each limit is demonstrably the binding one rather than a round number
somebody liked. The moment anybody edits a rating or a threshold, that test moves and this
prose does not.

The cap in force is `min(currency cap, course cap)`, applied by `WalletInfo.AllowsSlip`
and shown by the panel before the player finds out by being refused.

Raising any of them means moving the engine to `long` first.

## The ceiling is on the slip, not on the bet

**This is the one place horse racing differs from every other table in the casino, and
it is not cosmetic.** The other four take one stake per round, so a per-bet ceiling and a
per-round ceiling are the same number. A slip here carries up to 108 bets, and what has
to stay inside an `int` is the total that comes back. A two-million per-bet limit with no
limit on the count would let a full slip return something like a hundred and sixty
billion.

So `WalletInfo.MaxStake` is the slip total, `WalletInfo.Allows` checks one bet against
the minimum only, and `WalletInfo.AllowsSlip` checks the total. Splitting the two checks
is what stops a player being told a perfectly ordinary 50,000 bet is too large when what
is actually too large is the twenty other bets beside it.

**`IgnoreMaximum` does not lift it past the arithmetic.** Elsewhere the maximum is the
house being careful on the player's behalf and a player may say they would rather it did
not -- Blackjack's table maximum and Slots' both work that way. Here it is the width of
an int, and a player who waives it does not get a bigger win, they get a negative one.

| Currency | Min per bet | Max per slip | Step |
| --- | --- | --- | --- |
| Roubles | 10,000 | 2,000,000 | 5,000 |
| Dollars | 100 | 20,000 | 100 |
| Euros | 100 | 20,000 | 100 |

## A slip is many bets and one transaction

The whole slip is validated before any of it is taken, then taken with **one** debit and
paid with **one** credit.

Taking each bet as it is read has a failure mode nothing here could recover from: a slip
refused on its tenth bet with nine already paid for. A service that did it would still
balance at the end of a run, so only counting the movements catches it --
`ASlipOfManyBetsIsStillOneDebitAndAtMostOneCredit` and
`ASlipRefusedOnItsLastBetTakesNoMoneyForTheOthers` are the two tests that hold that line,
and the second one is the reason the arrangement exists.

Bets are settled independently and none of them can see each other. That forbids the
obvious and wrong optimisation -- stop at the first winner, since surely only one bet can
win -- which is false here and expensively so: backing runner 3 to win, to place and to
show collects all three when it wins.

## No legacy escrow import, deliberately

The other four tables each shipped as their own mod once, so each looks for an old
`escrow.json` under its former folder and imports it once. **This table was born inside
the casino.** No such file has ever existed for it, and a lookup for one could only ever
find somebody else's money. `Casino.Server.LegacyData` is not called from here, and that
is not an oversight.

## One shape, and there used to be two

**Every course is a straight, run left to right.** The mile and the marathon were first
drawn as one and two laps of an oval, and it was dropped after being looked at.

The reason is the panel's proportions. The track holder is about 1470 wide and 268 tall,
so a circuit has to be flattened to roughly two and a half to one before it fits -- which
reads as a running stadium rather than a racecourse -- and the runners bunch together on
the bends, which is exactly where eight coloured discs are hardest to tell apart. It also
needed a results board off to one side, because an oval has no lanes to write each
runner's placing beside.

**A longer race is a longer run, not a different shape.** The distance is carried by
furlong markers along the top, counting down to the post the way a real course does:
five ticks for the dash and sixteen for the marathon. Without them every course would be
the same picture at a different speed, since the straight is the same number of pixels
whatever the race. The clock agrees with them -- the marathon takes nearly twice as long
as the dash -- and `TrackTests` asserts both orderings, so a course cannot be listed as
longer while running shorter.

**The ordering is arithmetic, not arrangement.** Each runner is given a finishing time
strictly ordered by its finishing position, and its progress is its own elapsed fraction
of that time -- so at the post every runner is at exactly 1.0, reached in ascending order
of finish time. The jostle that makes it a race rather than eight progress bars is
multiplied by `(1 - u)^2`, which is exactly zero at the line: it can be as ugly as it
likes in the back straight without ever touching the finishing order. **Nothing in
`Jostle` knows who won**, and it does not need to.

That is deliberately a stronger guarantee than "the winner is nudged ahead at the end".
A player who watches number 3 win and is then paid for number 5 has been shown a lie, and
there is no way for them to tell which half was the bug.

The field takes 6 seconds, with each place 1.8% of that behind the one in front -- about
three quarters of a second between first and last, which reads as a field crossing the
line rather than a procession, and still leaves every gap several frames wide at 60fps so
two horses never appear to dead-heat.

**The stash is not updated until the last runner is home.** The server settled the race
before it answered, but a rouble counter that jumps while the field is in the back
straight tells the player the result several seconds before the race does. Roulette found
this with its wheel and Slots with its reels.

`Settled()` draws the result inside a `try` and resyncs the stash *outside and below* it.
That ordering is the 1.2.6 lesson from `docs/slots.md`: a line that could throw was put
ahead of the try guarding the drawing, so every table paid out correctly and then drew
nothing.

## The panel is laid out from a band table

Every element takes its vertical position from a named constant measured down from the
top of the frame, and **never from the element before it**. The first version positioned
the pair-bet row relative to where the runner loop happened to finish and the status and
result relative to the bottom of the frame: two coordinate systems growing towards each
other, which at eight runners overlapped by 18 pixels and drew the word NOTHING through
the EXACTA/QUINELLA selector.

The bands are checked for overlaps arithmetically rather than by looking at the panel,
because looking at it is the thing this environment cannot do.

The canvas uses `ScaleWithScreenSize` against a 1920x1080 reference matched on height,
the same as Roulette's and Slots'. It was `ConstantPixelSize` at first, which would have
left it a fixed 1520x900 actual pixels -- shrinking into the middle of a 1440p or 4K
screen while every other table scaled up around it.

## The pip is a horseshoe

`Textures.Inside` gained a `'U'` case for it. The four card suits were already spoken for
-- diamond for Blackjack, spade for Poker, heart for Roulette, club for Slots -- and the
default branch draws **nothing at all** for an unknown char, which is precisely what
`ICasinoGame.Pip` exists to avoid. A second heart would have read as a duplicate of
roulette.

There is no `tile-horseracing.png` in the repo, so the horseshoe is what shows today.
Dropping a file of that name in beside the plugin replaces it with no rebuild, the same
arrangement every other tile uses.

## There is no `HorseRacing.Client.csproj`

The other four tables have one because they *were* standalone plugins and the project is
the leftover editing surface -- three of them do not even build (see the root
`CLAUDE.md`). This table was never a plugin, so there is nothing to leave behind.
`Casino.Client.csproj` compiles the four client files directly, which is the whole of the
`ICasinoGame` seam, and nothing else is needed.

## What has not been checked

**No game client has been run against this.** Everything above about the money and the
arithmetic is covered by tests that pass; everything about how it *looks* is a claim
about code that reads correctly, not an observation.

Specifically unverified:

- The panel at any real resolution. The band table is verified to have no overlaps and
  60 units of bottom margin, but that arithmetic has still never met a screen.
- Whether the mown stripes, rails, stalls and furlong markers help or just add noise.
- Whether the wobble is now too tame. It was cut from a position offset to a speed
  variation of plus or minus 53%, which cannot reverse a horse but may also no longer
  read as jostling.
- Whether sixteen furlong markers across the same span as five reads as "longer" or
  just as "busier".
- Whether three course tabs at 260 units each are the right size.
- Whether the item-event sync actually round-trips in a running game. The two strings
  themselves **do** agree -- `RacePanel.SyncAction` and `RaceActions.Sync` were both
  read out of the source on 2026-09-19 and are both `"RacesSync"` -- but **nothing
  enforces that**, and a future edit to one is a sync that is silently never answered, so
  the stash would go stale with no error anywhere.

Per `docs/slots.md` and the client-debugging notes: if something looks wrong in-game,
rule out a stale build from the BepInEx logs *first*, and ask what the player actually
sees before changing code.

## Exercising it without the game

The routes answer over https with zlib-compressed bodies -- see the root `CLAUDE.md`,
which has the `curl` recipe and the traps. `/races/ping` needs a real profile id, since
it reads balances off the bank.

Never call it against a profile you care about while poking: `/races/place` moves real
money.
