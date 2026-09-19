# Horse Racing -- working notes

Read the root `CLAUDE.md` first. This file holds only what is true of this table.

**Update "Current state" when you finish a piece of work.** Poker's notes went four
commits claiming its server did not exist, and a fresh session reads that section first
and believes it.

---

## Current state

**2026-09-19. Engine, server and client written; not yet played.** Everything below is
true of the code, and the parts that have been *verified* are marked as such. Nothing
in this file claims in-game behaviour, because no game client has been run against it
-- see "What has not been checked".

- Engine: 34 tests, 13/13 mutants caught. The 336 prefixes sum to 1.0, and a 200,000
  race Monte Carlo agrees with the computed chance for all five bet kinds.
- Server: 24 money tests, 12/12 mutants caught. Routes `/races/ping`, `/races/place`
  and `/races/stats`, item event `RacesSync`.
- Client: the fifth tile in the lobby. Track, board, slip, stake stepper, currency
  switch, and the result drawn when the last runner is home.
- Verified against the built assembly: every racing type is in `Casino.Client.dll`, and
  the private-use byte scan comes back zero.
- `scripts/casino/pack.ps1` stages 11 assemblies and `horseracing.config.json`.

---

## The single most important fact about this table

**Every price on the board is exact, and both the board and the settlement compute it
the same way.**

`Odds.Chance` asks `Bet.Covers` which of the 336 ordered top-threes win, and so does the
settlement. That is deliberate, and it is not a tidiness argument. The failure it
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

## The card

Weights, not probabilities. A field of probabilities has to sum to one, so every edit to
it is an edit to every other runner -- and worse, the sum is a thing that can be wrong. A
card adding to 0.99 is a silently rigged race and nothing about reading the numbers would
show it. Weights cannot be inconsistent with each other.

| # | Name | Weight | Win | Price | Place | Show |
| --- | --- | --- | --- | --- | --- | --- |
| 1 | GRAY GHOST | 30 | 27.03% | 3.47 | 1.87 | 1.36 |
| 2 | DOLLAR SIGN | 24 | 21.62% | 4.34 | 2.23 | 1.54 |
| 3 | FACTORY FLYER | 18 | 16.22% | 5.79 | 2.85 | 1.88 |
| 4 | NIGHT RAIDER | 14 | 12.61% | 7.45 | 3.58 | 2.29 |
| 5 | RESHALA'S PRIDE | 10 | 9.01% | 10.43 | 4.90 | 3.05 |
| 6 | SCAV LUCK | 7 | 6.31% | 14.90 | 6.90 | 4.21 |
| 7 | LABS LIGHTNING | 5 | 4.50% | 20.86 | 9.58 | 5.78 |
| 8 | LEFT BEHIND | 3 | 2.70% | 34.77 | 15.82 | 9.44 |

A price is the **total** returned per chip, stake included, not the profit.

The weights are the only numbers in this game chosen by taste. Everything else is
computed from them. The spread is the whole character of the table: the favourite wins a
little over a quarter of the time and the rag wins about one race in thirty-seven. A flat
field would make every bet the same bet wearing a different number.

**Eight runners, and that is the one number worth defending.** Exacta is an ordered pair,
so the field size squares: six runners give thirty exactas, twelve give a hundred and
thirty-two. Eight gives fifty-six -- enough that the bet is worth having, few enough that
the board stays readable.

## The takeout, and what it actually is

`Odds.Takeout` is 6%, one number applied identically to all 108 spots. **The realised
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

## MaxBet is arithmetic, not a house limit

The longest price on this card is **751.24** -- the two slowest runners home in order --
and the engine counts chips in an `int`. Two million at that price is about 1.5 billion;
three million does not fit. An overflow here does not pay a big win, it pays a negative
one.

`OddsTests.TheMaximumStakeAtTheLongestPriceStillFitsInAnInt` asserts this against the
real card rather than against the paragraph above, and also asserts that *doubling*
MaxBet breaks -- so the limit is demonstrably the binding one rather than a round number
somebody liked. The moment anybody edits a weight in `Field`, that test moves and this
prose does not.

Raising it means moving the engine to `long` first.

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

## The track

`TrackView` draws eight lanes side-on, running left to right.

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

- The panel's layout at any real resolution. The frame is 1520x900 with a 384px track,
  which fits 8 lanes at 46px -- but that arithmetic has never met a screen.
- Whether the board and the slip actually fit side by side without overlapping at the
  hardcoded offsets in `RacePanelChrome`.
- Whether the race reads as a race.
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
