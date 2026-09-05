# Poker -- working notes for Claude

A poker table for the SPT hideout. Server mod in C# (.NET 10) against SPT 4.1.3,
with a BepInEx client plugin. Players buy in with **roubles, dollars or euros** --
spendable currency only. See "Currency only, and why the valuables went".

Sibling project to **Blackjack** (`../Blackjack`), which is shipped and working at
1.0.2. **The core of this mod comes from there.** Most of what is written here was
learned there, the expensive way. When something below says "port", it means there
is working, shipped code in that repo to copy rather than rediscover -- see "What
Blackjack gives this mod" for the file-by-file list.

This file is loaded automatically at the start of every session. Keep it to things
a fresh session would otherwise rediscover the hard way -- not a chronological
diary. **Update "Current state" when you finish a piece of work.**

---

## The variant: no-limit Texas Hold'em against bots

**Decided, after two reversals.** Read the history before reopening it, because the
same argument has now been had three times.

The mod is a **Texas Hold'em cash game against AI opponents**. There is a pot, the
bots bet into it, and the player wins their chips or loses their own.

It was Ultimate Texas Hold'em for a while and a working UTH table was built and
tested before the decision changed -- see "Parked: the Ultimate Texas Hold'em
build". The thing that settled it: **UTH is house-banked and has no pot.** Every
seat there plays its own hand against the dealer, independently, and two players at
one table never take a chip from each other. That makes the seat-mates scenery, and
scenery was not what was wanted. Nobody bluffs anybody in UTH.

### The structure

- **No-limit**, with bots choosing from a **discrete menu of sizes** -- roughly a
  third of the pot, two thirds, pot, and all-in. This is how real poker AI is built
  and it is the difference between a bot that looks like a player and one that
  gives itself away instantly: naive no-limit sizing is the tell. The player is not
  restricted to the menu; it exists so the bots have a tractable decision.
- Small blind and big blind, a button that moves, and the four streets.
- Up to five seats including the player, as decided for UTH and unchanged.

### Where the money comes from, which is new and matters

**The bots' chips are notional -- a number on screen -- and the player's are real.**
So when the player drags a pot in, currency is created into their stash, and when
they lose one it is destroyed. The mod is a faucet and a sink.

This is a real departure and it has two consequences worth being deliberate about:

- **There is no fixed house edge any more.** UTH's economics were bounded by
  arithmetic at 2.185% of the ante. Here the rate at which currency appears is
  exactly the skill gap between the player and the bots. Beat weak bots and the mod
  prints roubles indefinitely. That is an accepted consequence of the decision, not
  an oversight -- but anything that makes the bots weaker also makes the mod a
  better faucet, which is a strange pressure to have on a difficulty dial.
- **Blackjack's "no chips, no buy-in" decision does not survive.** Hold'em cannot
  settle per hand: a stack is what a bet is sized against, what an all-in means, and
  what decides who is eligible for which side pot. The player **buys in** -- real
  currency debited, chips on the table -- and **cashes out** at the end.

  That makes escrow far more load-bearing than it was in Blackjack, and changes what
  it holds. `EscrowStore` recorded a *stake* until a hand settled. It must now record
  the player's **current stack**, updated as it changes, because a crash mid-session
  has to give back what they actually have rather than what they sat down with.

### What this buys, and what it costs

The bots become the product. They can take the player's money, which is the only way
a seat at a table ever feels like a person -- but it also means a flat bot ruins this
game far more thoroughly than it would have ruined UTH, where nobody was pretending
the other seats were players.

They do not have to be *good*, only **believable**, and that is a much lower bar than
it first appears. Rule-based play over a Monte Carlo equity estimate, with position
awareness and randomised aggression, reads as human. The expensive foundation for it
already exists: `HandEvaluator` is exhaustively verified and fast, and Monte Carlo
equity is built directly on it.

### What the decision changed in the code

- **`PotBuilder` is load-bearing again.** It was written first, then spent a day as
  dead code under UTH, and is now the settlement path. Already mutation-checked,
  side pots and uncalled-bet refunds included.
- **The payout-scale problem largely dissolves.** UTH's Blind paid 500:1 and forced
  ceilings down; a pot cannot pay more than the chips in it. See "The payout scale".
- The UTH table, its paytables and its strategy are parked, not deleted.

## The single most important fact

**SPT 4.x server mods are C#, not TypeScript.** The `mod.ts` / `package.json` /
tsyringe world ended at 3.x, and most guides online still describe it. Server mods
are .NET 10 class libraries referencing `SPTarkov.Server.Core`, with an
`IModMetadata` record in place of `package.json`.

`SptVersion` in that record is a **hard load gate**. It is `~4.1.3` (>=4.1.3
<4.2.0). A mod outside the range loads nothing and logs nothing. Silence at startup
means the gate, not a bug in the game code.

## Layout

| Project | Owns |
| --- | --- |
| `src/Poker.Game` | Rules engine. No SPT reference, no I/O, no clock. |
| `tests/Poker.Server.Tests` | 21 tests over the money and both transports, on fakes. No SPT server needed. |
| `tests/Poker.Game.Tests` | 189 tests. The evaluator, pot builder, log, hold'em table and bots are live; the paytables, UTH table and strategy are parked. |
| `src/Poker.Server` | The mod: routes, DI, logging, and the money. |
| `src/Poker.Client` | The BepInEx half: task-bar tab, table panel, card and chip art. `net472`, built against the install. |
| `tools/Poker.Console` | Terminal table and soak harness. No SPT needed. See "The harness". |
| `scripts/` | `pack-mod.ps1` builds the droppable zip, `pack-console.ps1` the harness, `smoke.ps1` drives a real server. |

The engine knows nothing about currency -- it takes an `int` and returns an `int`.
Everything that maps a `Wallet` to an item template lives in `Wallets.cs` and
`Bank.cs`, both of which are ported. Keep it that way; it is what makes the rules
testable.

## What Blackjack gives this mod

Roughly **1,400 lines of server plumbing that ports nearly unchanged** and **~800
lines of client card rendering**. It is shipped code that has moved real money on a
real profile. Read the original before rewriting anything here from scratch.

Line counts are from the working tree, as a sense of what each piece costs.

### Ports essentially as-is -- rename the namespace and go

| File | Lines | Notes |
| --- | --- | --- |
| `src/Blackjack.Server/Bank.cs` | 295 | The money. Stack walking, live `StackMaxSize`, balance checks either side of every move, shortfall-to-mail. Carries no blackjack rules at all. |
| `src/Blackjack.Server/ProfileGateway.cs` | 38 | `HasProfile` / `SaveAsync`. |
| `src/Blackjack.Server/Abstractions.cs` | 70 | `IBank`, `IProfileGateway`, `IStatsStore`, `IEscrowStore`. The seams that make the service testable with no server. |
| `src/Blackjack.Server/Wallets.cs` | 90 | Six wallets, templates, symbols, per-wallet limits. **Ported, then cut to three** -- currency only. See "Currency only, and why the valuables went". |
| `src/Blackjack.Server/Escrow.cs` | 146 | Records money taken but not settled, and refunds orphans on next contact. **Needs reworking, not just porting** -- it holds a stake, and hold'em needs it to hold the player's live stack. See "Open items". |
| `src/Blackjack.Server/BlackjackLog.cs` | 75 | Logger with a verbosity switch and the mod folder. See "Logging". |
| `src/Blackjack.Server/ModMetadata.cs` | 27 | New name and URL; the `~4.1.3` range is unchanged. The GUID is **`com.mybutthasarash.poker`** -- see "Releasing". |
| `src/Blackjack.Server/Startup.cs` | 50 | Boot banner. Retune the lines it prints. |
| `src/Blackjack.Game/EnumJson.cs` | 67 | `StringEnumListConverter`. Needed the moment a view carries a list of available actions. |
| `src/Blackjack.Client/Textures.cs` | 461 | Every sprite drawn in code -- rounded boxes, chips, felt. The mod ships no art. |
| `src/Blackjack.Client/CardView.cs` | 207 | Draws one card from its two-character code, with a drawn fallback when the art is absent. **`Card.Code` here is deliberately identical to Blackjack's, so this ports untouched.** |
| `src/Blackjack.Client/MenuButtonPatch.cs` | 379 | Menu entry, the end-of-frame clone trick that makes it survive menu mods, raid guard. |
| `src/Blackjack.Client/BlackjackClientPlugin.cs` | 86 | BepInEx entry point, config binding. |
| `src/Blackjack.Client/ProfileSync.cs` | 81 | Keeps the client's stash view in step after the table moves money. |
| `src/Blackjack.Client/BlackjackApi.cs` | 77 | The client's side of the transport. |
| `scripts/smoke.ps1` | 259 | Drives a real server with no game attached. The HTTPS, compression and cookie handling in it is the expensive part. |
| `tools/Blackjack.Console/Program.cs` | 100 | Terminal table. Worth more here than there: it can watch the bots play thousands of hands with no Unity. |

### Ports as a shape, with different contents

| File | Lines | What changes |
| --- | --- | --- |
| `src/Blackjack.Server/BlackjackService.cs` | 295 | The flow -- validate, let the engine decide, move money to match, save -- is the model to copy exactly. The requests differ: an Ante/Blind/Trips bet, then Play or Check, then Play or Fold. |
| `src/Blackjack.Server/Contracts.cs` | 146 | Same job, different verbs. Keep `PingResponse` almost verbatim: it answers "did the mod load, did the session resolve, can the money be read" and is the first thing worth having. |
| `src/Blackjack.Server/BlackjackCallbacks.cs` | 148 | Static routes, for curl. |
| `src/Blackjack.Server/BlackjackItemEventCallbacks.cs` | 87 | Item events, for the real client. |
| `src/Blackjack.Server/BlackjackRouter.cs` + `BlackjackItemEventRouter.cs` | 96 | Route registration for both transports. |
| `src/Blackjack.Server/TableStore.cs` | 70 | Live tables keyed by session, in memory on purpose. Now holds seat-mates too. |
| `src/Blackjack.Server/Stats.cs` + `StatsStore.cs` | 244 | The persistence ports; the recorded fields do not. Blackjack outcomes do not map onto UTH -- hands played, Play bets made at each size, Blind hits by category. |
| `tests/Blackjack.Server.Tests/Fakes.cs` | 135 | Fake bank, profile, stats and escrow. The reason the money tests need no server. |
| `tests/Blackjack.Server.Tests/MoneyInvariantTests.cs` | 78 | Plays 400 random rounds and checks the money moved equals the profit the engine reported. **Port this before writing settlement, not after.** |

### Does not port

- `src/Blackjack.Client/BlackjackPanel.cs` (1,696 lines) -- the layout is blackjack
  shaped: one hand, one dealer, a hit/stand strip. A UTH table is several seats,
  five community cards and three bet spots. The *techniques* carry (nine-sliced
  sprites, the felt, cursor handling, the settled-round strip); the layout does not.
- `src/Blackjack.Game/*` -- already replaced. `Card`, `Deck`, `HandRank` and
  `HandEvaluator` exist here and are better suited to poker than the blackjack
  originals.

### Fork, do not share

Two SPT mods each shipping their own build of a same-named DLL into one process is a
load conflict waiting to happen. The shared code is ~800 lines. Copy it.

## Logging

**Everything in this project logs, and the logging is part of how it is tested.**
Two mechanisms, because the engine and the mod have different constraints.

### In the engine: `IGameLog`

`src/Poker.Game/GameLog.cs`. The engine has no SPT reference and no I/O, so it
cannot take a logger -- it takes a *sink*.

- **Off by default.** Every class defaults to `GameLog.Null`, so nothing allocates
  and nothing prints unless a caller asks for it.
- **Guard every call site with `_log.Enabled`.** This is not ceremony. The
  distribution test evaluates all 2,598,960 five-card hands; building a log string
  per hand that is then discarded turns a 1-second test into minutes.
- **`ListGameLog` is the test seam.** It captures lines in memory, so a test can
  assert on the engine's *reasoning* -- that a refund was decided, that a layer
  collapsed -- and not merely on its output. `GameLogTests` is the pattern.
- `DelegateGameLog` adapts it to anything else: `Console.WriteLine` in the console
  tool, `PokerLog` on the server.

Log the decision, not the arithmetic. A line saying which branch was taken and why
is worth ten lines reciting numbers the caller already has.

### On the server: `PokerLog`

Port `BlackjackLog` (75 lines). It wraps `ISptLogger<T>`, knows the mod folder, and
has a **verbose switch in `config.json`** so `log.Detail(...)` can be left in place
around every rouble and turned off once things work. Blackjack's `Bank` is the model
for how much to log on the money path: every debit and credit says what it intended,
what it did, and shouts when those disagree.

## What is done

- `Card` / `Rank` / `Suit` -- **Ace is high at 14**, the opposite of the Blackjack
  engine where Ace was 1 and the hand applied the 11. Poker never adds ranks, only
  orders them. The two-character wire form (`AS`, `TH`, `2C`) is deliberately
  identical to Blackjack's so the client card art and parsing port unchanged.
- `Deck` -- single deck, freshly shuffled each hand. Simpler than Blackjack's shoe
  on purpose: a shoe exists so several decks can be dealt to a cut card, which
  matters only because blackjack is beatable by tracking what has gone.
  `Deck.Stacked("AS KS ...")` pins a deal for tests, same idea as `Shoe.Stacked`.
- `HandRank` -- category plus kickers packed into one int, so comparison is a
  single integer compare rather than a walk down two kicker lists. `Describe()`
  gives the table-side reading ("Full house, fours over nines").
- `HandEvaluator` -- ranks 5 to 7 cards. Best-of-seven is a brute-force walk of all
  21 combinations, deliberately: this runs dozens of times a hand, not millions, so
  the only thing worth optimising for is being correct on inspection.
- `GameLog` -- the engine's logging seam. See "Logging".
- `PotBuilder` -- splits what every seat committed into a main pot and however many
  side pots the all-ins require, and returns an uncalled bet rather than potting it.
  **The settlement path for hold'em.** Written first, spent a day as dead code under
  UTH, now load-bearing again. Mutation-checked -- see below.
- `StringEnumListConverter` -- ported from Blackjack, for a list of enums on the
  wire.
- `HoldemTable` / `HoldemSeat` / `HoldemRules` -- the game. Button, blinds, four
  streets, a full no-limit betting round, side pots and showdown. See "The betting
  round" below.
- `IPokerAgent` -- where a bot's decision comes from, **one instance per seat**. Its
  `PokerContext` carries that seat's own cards, the board, the stacks, the pot and
  what is legal -- never another seat's cards and never the deck. `HandEnded` is how
  a seat learns what the hand cost it, and is the only route anything carries from
  one hand to the next.
- `HoldemView` -- the only thing a transport ever sends, and the single place the
  hidden-card rule is applied. `Of` reveals a seat's cards to that seat and
  otherwise only once `HoldemSeat.Hand` is filled in, which the engine does for
  seats that reached a showdown. See "It found something on the first hand it drew".
- `INameSource` -- the engine takes names rather than inventing them, and numbers
  any seat it is not given one for. See "Naming the bots".
- The whole Ultimate Texas Hold'em game -- **parked, not on the path**. See "Parked:
  the Ultimate Texas Hold'em build".

- `BotAgent` / `PokerPersonality` / `HandEquity` -- the opponents. Monte Carlo
  equity, pot odds, position, stack depth and seven characters over one decision
  procedure -- blendable, and drifting with how the night goes. See "How they
  actually decide".

## The betting round

The bug-dense part of hold'em, and where the tests are aimed. Settlement is
comparatively easy because `PotBuilder` already does it.

Rules that are each one line of code and each cost a real bug when missed:

- **Heads-up reverses the blinds.** The button posts the small blind and acts
  **first** before the flop, then last on every street after it. With three or more
  the button is last pre-flop and the seat after the big blind opens. This is the one
  everybody gets wrong.
- **After the flop the small blind opens**, which heads-up means the big blind. Two
  different orders for the two halves of a hand; using one for both is invisible for
  as long as every bot only checks.
- **Posting a blind is not acting.** That distinction, and nothing else, is what
  leaves the big blind its option to raise when the table has only called round to
  it.
- **A raise must be at least the size of the last one.** Otherwise a player can grind
  a round out in single chips and never let it close.
- **An all-in too small to be a full raise does not reopen the betting.** Seats that
  have already acted owe the difference and may call or fold, but may not raise
  again. Miss it and a short all-in becomes an unlimited raising war between two
  other players.
- **An uncalled bet comes back** rather than being counted as a pot that was won.
  `PotBuilder` already does this; the table only has to return it to the stack.
- **The odd chip on a split goes to the first winner left of the button.** Any rule
  will do; having none quietly destroys a chip a hand, and a table whose books drift
  is a bug nobody sees until the numbers are far apart.

### Chips are conserved, and that is the invariant to build against

Every hand starts with a known number of chips at the table and must end with the
same number. `ChipsAreNeitherCreatedNorDestroyed` fuzzes two to five seats through
three hundred hands of random aggression and checks it after every one. The ways to
break it all live in the betting round -- an uncalled bet kept, a side pot paid
twice, an odd chip dropped -- and none of them are settlement bugs.

Mutation-checked, nine faults, each caught: heads-up blinds reversed, no minimum
raise, a short all-in reopening the betting, posting a blind counting as acting, the
odd chip dropped, uncalled bets not returned, a round closing before everyone has
matched, the flop using the pre-flop order, and a seat betting more than it has.

**Three of those nine survived the first pass**, and all three were holes in the
tests rather than in the code:

- The big-blind-option test asserted on the *first* thing that seat was offered, and
  a table that closed the pre-flop round early simply offered it the same shape on
  the flop -- nothing to call, a raise available. **Assert the street.**
- The post-flop order tests only ever used bots that checked, so every order looked
  alike. Order is only visible when somebody bets: make the seat that should act
  first bet, then check that the next seat has something to call.
- The clamp stopping a seat betting more than it has is currently unreachable,
  because the options already cap every caller. Unreachable defensive code needs a
  direct test or it silently stops being true.

### The evaluator is trustworthy, and here is why

`HandDistributionTests` deals **all 2,598,960 distinct five-card hands** and checks
the category counts against the published figures (40 straight flushes, 624 quads,
3,744 full houses, 5,108 flushes, 10,200 straights, 54,912 trips, 123,552 two pair,
1,098,240 pairs, 1,302,540 high card, 4 royals). An evaluator that misreads one
hand in the deck lands off them.

Mutation-checked, per the rule below. Each of these was introduced and the suite
caught it: wheel reported ace-high (4 fail), straight flush not checked (8 fail),
two-pair kickers reversed (2 fail), full-house pair ignored (7 fail). Do this again
after touching the evaluator.

### The pot builder is trustworthy too, with one caveat worth keeping

Mutation-checked the same way. Each was introduced and the suite caught it:
uncalled bet never refunded (2 fail), folded seats ignored when finding the matched
level (5 fail), unwinnable layers not collapsed (4 fail), folded seats left eligible
(3 fail), layer ceiling not advanced (8 fail).

The two-fail case is the one to remember. `EveryChipCommittedIsEitherPottedOrRefunded`
does **not** catch a missing refund -- the chips simply stay in the pot, so the books
still balance. Conservation is necessary and nowhere near sufficient: money can be
conserved and still settle to the wrong seat. **The same trap is waiting in UTH
settlement**, where three bets resolve on different rules and a total can come out
right with the Blind and the Play swapped.

## Parked: the Ultimate Texas Hold'em build

A complete, tested UTH game is in the tree and is **not** on the path any more. It is
kept rather than deleted because it works and because the decision has moved twice
already. Do not build on it, and do not let it accrete: nothing new should call into
it.

`Paytable.cs`, `Rules.cs`, `UltimateHoldemTable.cs`, `Seat.cs`, `TableView.cs`,
`UthStrategy.cs`, `SeatMateAgent.cs`, plus their tests. Green, mutation-checked, and
worth reading before writing the hold'em equivalents -- several of its lessons are
about card games rather than about UTH.

The parts of it that are **not** UTH-specific and should carry across:

- **A fixed, documented deal order, pinned by a test.** Adding a seat changes which
  cards every later position receives, so a stacked-deck test is pinned to a seat
  count as well. If the order moves, every pinned deal breaks at once and the
  failures read as rules bugs. This is just as true in hold'em.
- **Hidden cards are absent from the view, not blanked.** Anything sent to the client
  is knowable by the client.
- **Conservation is necessary and nowhere near sufficient.** Money can be conserved
  and still settle to the wrong seat -- see the pot builder note above.
- **Mutation-check anything that ranks a hand or moves money.** Every settlement
  rule in the UTH table was introduced as a deliberate fault and caught: the Ante
  pushing only on a won hand (1 fail), the Blind paytable consulted on a tie (1),
  folding taking the Trips bet (1), a winning bet returning winnings without its
  stake (2), the dealer needing better than a pair (3), a third check at the river
  (1), the dealer dealt before the seats (5), hole cards visible from the deal (1).

The UTH-specific knowledge, compressed, in case it is ever picked up again:

- Settlement is three bets on three rules. The Ante pushes whenever the dealer fails
  to open, **including on a hand the seat lost** -- the natural misreading looks
  right in every winning hand anyone would test. The Blind pays its table on a win,
  pushes on a tie, and pushes rather than paying beneath a straight. Trips ignores
  the dealer and survives a fold.
- Paytables are data, not code, so the capped valuables table is a different table
  rather than a second path through settlement. A push and a loss are both `Payout`
  values for the same reason.
- **The river is computed, not looked up, and that was worth six points of house
  edge.** A rule of thumb -- bet a hidden pair or better, else fold -- folded 26% of
  hands where the real game folds about 19%, and each of those folds threw away two
  antes. Measured edge 8.4%. Walking all 990 possible dealer holdings instead gives
  the exact value of betting, at about four milliseconds a decision.
- **Folding is the expensive decision and every plausible heuristic forgets it.** A
  royal on the board cannot be beaten, so every dealer holding ties and betting is
  worth exactly zero -- and every rule of thumb folds it, which is the worst answer
  available.
- The edge still measured 5.4% against a published 2.185%, and the evidence pointed
  at which hands the pre-flop and flop lookups selected rather than at settlement.
  Unresolved, and only matters if UTH is revived.
- **Do not try to confirm a house edge by simulation.** At a standard deviation near
  4.9 antes a hand, a tenth of a point needs about nine million hands. Measure
  decision frequencies instead -- they are proportions, they converge in thousands
  rather than millions, and they are what caught the river bug.
## The bots

They are opponents now, not scenery, and they are the product. A flat bot ruins this
game in a way it never could have ruined UTH, where nobody was pretending the other
seats were players.

- **They have to be believable, not good.** Strong poker AI is a research problem;
  a bot that reads as a person is not. Rule-based play over a Monte Carlo equity
  estimate, with position awareness and randomised aggression, gets there -- and the
  expensive part already exists, because `HandEvaluator` is what equity is estimated
  with.
- **Bet sizing is the tell.** Naive no-limit bots give themselves away instantly by
  betting odd amounts. Bots choose from a discrete menu -- about a third of the pot,
  two thirds, pot, all-in -- which is both how real poker AI works and what makes
  their bets look considered.
- **A bot sees only what a player at that seat could see**: its own cards, the board,
  the betting so far, the stacks. Never another seat's cards and never the deck.
  Structural, not a promise -- a bot cannot cheat with what it was never handed.
- **A bot must never call `IBank`.** Their chips are notional. Worth an explicit
  test.
- **Their RNG must be injectable**, exactly as `Deck`'s is, or their behaviour cannot
  be pinned in a test.
- **Every decision is logged** with its reason, through `IGameLog`. A table of seats
  that silently do things is untestable and unwatchable; the console tool has to be
  able to print why seat 3 shoved.
- **Hole cards are absent from the view until showdown**, and mucked hands stay
  absent. Anything sent to the client is knowable by the client.

### How they actually decide

`BotAgent` runs one procedure for every character, weighted by five dials in
`PokerPersonality`. One procedure and eight sets of dials, never eight procedures:
a seat that decides by its own logic cannot be debugged, and when two of them
disagree about a hand there is no way to say which is wrong.

What goes into a decision, which is what a person weighs too:

- **How often the hand wins**, from `HandEquity` -- a Monte Carlo rollout over the
  unseen cards. It handles any street and any number of opponents in the same code,
  and it already accounts for the crowd: aces against one player and aces against
  four are different hands, which is the thing no chart can tell a bot.
- **The price**, as pot odds. Equity above the price is a call that makes money and
  below it is one that does not; everything else is an adjustment to one side.
- **Position**, weighted by the `Positional` dial. Weak players ignore it, which is
  the most reliable way to spot one.
- **What is already in**, because chips in the pot change what folding costs.
- **Stack depth** -- under about ten big blinds a seat starts shoving, and the
  `Risk` dial decides how early.
- **How many opponents are still live**, which throttles bluffing hard.

The dials are `Tightness`, `Aggression`, `Bluff`, `Risk`, `Positional` and
`Steadiness`, each 0 to 1. Measured over sixty hands apiece, facing a bet:

| | folds | calls | raises |
| --- | --- | --- | --- |
| Rock | 78% | 18% | 4% |
| Grinder | 70% | 17% | 13% |
| Shark | 61% | 18% | 20% |
| Tourist | 50% | 39% | 11% |
| Station | 44% | 55% | 2% |
| Maniac | 32% | 24% | 44% |
| Gambler | 27% | 35% | 38% |

**The spans on the dials matter more than their midpoints**, and every one has had to
be widened after measuring. At the first attempt a rock folded 15% and a calling
station 11%, and a merely ordinary player raised as rarely as a station -- that is
not a cast, it is one character with several names. **If a dial is retuned, measure
this table again rather than trusting that it still separates.**

There were eight characters. An "Owl" -- tight, patient, enormous when it finally
played -- came out folding 74% and raising 2%, which is the Rock with a different
name on it, and its real trait was about bet *sizing* rather than about how often it
acted. Seven that genuinely differ beat eight where two are the same person, and only
four are ever at a table at once.

### They are not fixed, and they are not only these seven

Two things make the seats a population rather than a list.

**They blend.** `Blend` interpolates every dial between two characters and
`Improvise` crosses two at random and jitters the result, so a table can be filled
with people who are mostly a grinder with a streak of gambler in them. The named cast
are landmarks, not the population.

This works *only* because every character runs the same procedure and differs only in
its numbers. Eight separate decision procedures could not be blended at all -- there
would be nothing to interpolate. It is the whole return on having built it as dials.

**They drift.** `IPokerAgent.HandEnded` tells every seat how each hand went, and
`BotAgent` carries a mood from -1 to +1 that moves with results and decays back
towards level. `Current` is the base character bent by that mood; `Personality` is
who they were when they sat down.

- **Which way a seat tilts falls out of `Risk`, not a dial of its own.** Gamblers
  steam -- looser, swinging harder, trying to get it back in one hand. Careful
  players shut down. Both are things you can watch happen at a table.
- **Losing and winning are not mirror images.** Modelling them as one signed number
  had a gambler running *hot* becoming careful, which is the one thing a gambler
  never does. Winning makes everybody a little bolder; confidence is not a
  personality type.
- **`Steadiness` is how little any of it reaches them.** At 1 a seat plays its
  thousandth hand exactly as it played its first.
- **Net has to come off the stack.** Winnings minus what was committed misses an
  uncalled bet coming back, and reports a raise everybody folded to as a large loss
  -- which would have seats tilting off hands they had just won. `HoldemSeat.Net` is
  `Stack - StackAtHandStart` for this reason.
- **Folding a blind is not a bad beat.** A streak counter that thinks otherwise has
  every seat steaming within an orbit.

### Three things about testing bots that cost time here

- **Measure decisions where money was actually asked for.** Most of what a seat does
  is check into pots nobody bet, and averaging over all of it drowns every
  difference. "How often it folds when asked" is both the number that separates the
  characters and the one a person at the table would notice.
- **Top the stacks up between hands when measuring style.** The maniac busted a
  third of the way through its sample and took the sample with it. True to life,
  useless as a measurement.
- **A five-seat table is not a five-handed pot.** Bluffing frequency is conditioned
  on *live* opponents, and by the time anyone checks after the flop most of the table
  has folded -- so a test that varies the seat count measures almost nothing (48%
  against 46%). Condition on what the rule actually reads.
- **A test wrapper around an agent must forward `HandEnded`.** One here did not, so
  every personality measurement was quietly taken with the bot's memory disconnected,
  and the tests disagreed with the real table without failing. If `IPokerAgent` grows
  another method, every wrapper in the tests needs it too.

### They have to feel like real people. This is a stated requirement, not polish.

The table is meant to feel alive and the seats are meant to read as players rather
than as a lookup table with names on it.

**This section was written for UTH and its central constraint no longer holds.**
There it argued the seat-mates could never feel *dangerous*, because a house-banked
game has no pot and no seat can take the player's money. Hold'em has both. Being
dangerous is now the strongest thing they have, and everything below is what is
still needed on top of it: a life of their own that the player watches happen --
their own money, their own runs of luck, their own mistakes, and their own
reactions to all three.

What that needs, roughly in order of how much it buys:

1. **Persistence between hands.** The same named characters, still there next hand,
   with a history the player can notice. A cast that is re-rolled every deal is
   scenery.
2. **A bankroll with consequences.** Each seat has notional chips that rise and
   fall, and a seat that busts **leaves and is replaced**. A player who can go broke
   is the single strongest signal that a seat is a person, and it costs almost
   nothing -- `HoldemSeat.Net` already produces the number.
3. **Mood that moves.** Dials that drift during a session rather than sitting where
   they were set: a seat that has lost four in a row chases, a seat that just won
   big gets careless. A fixed personality is still a lookup table, just a biased
   one. This is the thing that most makes a bot stop feeling mechanical.
4. **Reactions tied to real events**, emitted by the engine rather than invented by
   the client -- hitting the Blind paytable, a bad beat, a third fold running, a
   royal. The client must never make up a fact about a hand; it renders what the
   engine says happened.
5. **Timing.** Real players do not act instantly or uniformly. The engine should hand
   the client a thinking time per decision, and it already knows the right one: the
   river calculation produces the exact value of betting against folding, so **a seat
   can take longer precisely when the decision is genuinely close.** That single
   detail does more than any amount of random delay.
6. **Visible mistakes.** The dials already produce them and the log already records
   them; surfacing one as a tell is nearly free.

**Done.** `HoldemTable` takes one `IPokerAgent` per seat and keeps them in a
dictionary keyed on the seat index, so every seat-mate is its own character.
`ISeatAgent` survives only in the parked UTH files. The seats are also named from
the game's own PMC list now -- see "Naming the bots".

None of this belongs in the client. The engine owns behaviour and emits events; the
client owns rendering and animation. A bot whose personality lives in the UI cannot
be tested and will not survive the first refactor.

## The payout scale, which hold'em mostly solves

This was the mod's hardest open problem under UTH, where the Blind paid **500:1** on
a royal and the worst case reached 511 antes -- a payout that has to arrive as items,
in a stash, at the wallet's `StackMaxSize`.

**A pot cannot pay more than the chips in it.** The most the player can win in a hand
is the sum of what everyone else put in, which is bounded by the stacks at the table
and therefore by the buy-in. No paytable, no multiplier, no tail. That removes the
entire class of problem, and with it the capped valuables paytable that existed to
work around it.

What is left is smaller and still real:

- **The buy-in is now the number that sets the ceiling**, not a bet limit. A player
  who buys in for X can at most cash out X times the number of seats, so the maximum
  a session can hand back is roughly `buy-in x seats`. That is the figure to size
  wallet limits against.
- ~~**Bitcoin and Lega medals have a `StackMaxSize` of 1.**~~ **Gone with the
  valuables.** Every remaining wallet stacks in the tens of thousands or better, so a
  payout is a handful of stacks rather than a pile of individually-gridded items. See
  "Currency only, and why the valuables went".
- **Chips need a denomination.** Blackjack never had one because it never had chips.
  A stack of a million roubles cannot be one chip per rouble, so the table needs a
  chip size -- a big blind, effectively -- and the buy-in has to be a whole number of
  them. Rounding here is where money goes missing.
- `Bank.Credit`'s shortfall-to-mail path is still the backstop, not the plan. Mail
  has attachment limits too, and "you won, here are 40 letters" is not an outcome.

## The harness

`tools/Poker.Console` plays the engine in a terminal with no SPT install, and points
it at itself when nobody is playing.

```
dotnet run --project tools/Poker.Console                        # sit down and play
dotnet run --project tools/Poker.Console -- --soak 2000 --samples 12
dotnet run --project tools/Poker.Console -- --seed 1234 -v --peek
```

`--soak N` puts a bot in every seat, plays N hands and **checks the table's
invariants after every single action**. `--seed` fixes the shuffle and the characters
so any hand can be got back; `-v` prints every engine line as it happens; `--peek`
shows everybody's cards.

The invariant checking is the point, not a decoration:

- **Every line the engine writes is captured whether or not it is printed.** The only
  run worth having a transcript of is the one that just failed, and by then it is too
  late to turn logging on. A failure dumps the whole hand.
- **Checks run after every action, not at the end of the hand.** A betting-round bug
  shows up as an impossible intermediate state -- an actor who has folded, a minimum
  raise above the maximum, a stack gone negative -- and by settlement the evidence has
  been tidied away by the next street.
- What it checks: chips conserved against a declared expectation, no negative stack,
  the pot matching the seats, the board matching the street, and the offered options
  being coherent (nothing to call *and* a call offered, check and call together, a
  raise range that is not a range, a folded or all-in seat being asked to act).
- **Re-buying is declared rather than switched off.** `HoldemTable.Reseat` is the one
  place in the engine that makes chips out of nothing, so the harness tells the
  watchdog and the conservation check stays live.

4,500 soaked hands across two, three and five seats have not broken one.

### Taking it to another machine

```
./scripts/pack-console.ps1          # dist/console-win-x64/Poker.Console.exe
./scripts/pack-console.ps1 -Zip     # and a zip beside it in releases/
```

**Self-contained by default**, which is 70MB of exe and worth it: the box with the
game on it is not the box this gets built on, and it needs no .NET installed.
`-FrameworkDependent` gives a small build for a machine that has the .NET 10 runtime.

The zip is gitignored -- thirty megabytes that rebuild in seconds. The mod zip, when
there is one, is small and gets tracked the way Blackjack's is.

**This is not the SPT mod.** The console is the engine in a terminal. The mod
itself is `src/Poker.Server` plus `src/Poker.Client`, and both are built and
working -- see "Shipping it to an install".

### It found something on the first hand it drew

Not in the engine -- in the renderer, which showed a bot's hole cards on a hand that
ended with everybody folding. There had been no showdown and the cards should never
have been seen.

The fix is worth carrying into the server's view: **reveal keys off `Hand is not
null`, never off the street.** The engine fills in `HoldemSeat.Hand` only for seats
that actually reached a showdown, so it is already saying which hands may be shown.
Reading `Street == Showdown` instead leaks the winner's cards on every hand that ends
early -- which is most of them.

## Shipping it to an install

```
./scripts/pack-mod.ps1                          # server only, anywhere
./scripts/pack-mod.ps1 -InstallPath 'H:\SPT4.1.X'   # both halves, on the game box
./scripts/smoke.ps1 -SessionId <id> -PingOnly
```

**`-InstallPath` is what makes it a whole mod.** With it the script also builds the
client plugin against that install, stages it under `BepInEx/plugins/Poker` with the
card and chip art, and copies both halves in. Without it there is nothing to compile
the client against, so the zip carries the server alone and is named
`-server-only` -- a half mod that looks like a whole one is worse than one that is
obviously partial.

The zip lays out as `SPT_Runtime/user/mods/Poker/` and extracts at the **root** of
the install, which is how Blackjack ships. It carries only this mod's two
assemblies and `config.json` -- SPT provides its own, and a second copy of those in
one process is a load conflict waiting to happen.

**What it does.** Loads, announces itself, serves six routes on two transports, deals
real no-limit hold'em against the bots from a table inside the game, and **moves
money**: one chip to one rouble, the buy-in debited on sitting down, the stack
credited on standing up, an unfinished session paid back on the next buy-in or
cash-out. The startup banner says so out loud, because a mod that takes currency out of
a stash should announce itself before it does -- though it currently says "next
contact", which is wider than the truth. See "Open items".

**What has actually been run.** All of it, on a real 4.1.3 install and a real profile:
loading, routing, playing, and **the money in both directions with the expected balance
matching the observed one**. See "Current state" for the figures. What is still untried
is any wallet but roubles -- which needs the chips-per-unit rate before it is even
reachable -- and the shortfall-to-mail path, which needs a stash too full to take a
payout.

### The client plugin

`src/Poker.Client`, and it can only be built on a machine with the game on it: it
compiles against `Assembly-CSharp.dll` and the `spt-*` DLLs of the install it will
run on, because 4.1.3's `PluginValidator` reads a plugin's `spt-*` references and
requires a major.minor match. It targets `net472`, not .NET 10, because it runs in
the game's mono runtime.

    dotnet build src/Poker.Client/Poker.Client.csproj -c Release \
      -p:SPTPath="H:\SPT4.1.X" -p:DeployToSPT=true

Deploys to `BepInEx\plugins\Poker\` with the art beside it. Verify the gate by
reading the built DLL's `spt-*` references with Mono.Cecil and checking major.minor
against `BepInEx\plugins\spt\`; that is the whole of what the validator does.

**What ported from Blackjack unchanged** -- `Textures`, `CardView`,
`MenuButtonPatch`, and all 52 card faces plus the table photograph. `Card.Code` is
deliberately identical between the two mods, which is what makes the art carry over
untouched.

**What is new here** -- `PokerClientPlugin`, `PokerApi` (all six routes, request
shapes matched against `Contracts.cs`), `ChipView`, and `PokerPanel`.

**`ProfileSync` is deliberately not ported.** It sends an item event the server has
no handler for yet, so it could only fail. It arrives with the money path.

**The panel decides nothing.** The action strip is built from the server's own
`Options.Moves` rather than from the client's idea of the rules, and is rebuilt on
every view -- a stale button that is still clickable is a move the player did not
mean to make. A refused move comes back with the real view attached, so the client
redraws rather than arguing.

**The reveal rule is the server's.** Draw `Cards` exactly as sent; an empty list
means backs. `HoldemView.Of` keys it off the seat having reached a showdown.

### Two things the menu did to you, while there was a menu button

**History.** `MenuButtonPatch` has been deleted and the tab is the only entrance;
none of this is live code any more. It is kept because both lessons are about
EFT and about sharing a menu with another mod, not about the button, and the next
thing that clones a menu row will meet them again.

- **The button walks down the screen.** `MenuButtonPatch` installs on both `Awake`
  and `Show`, and placement measured against the lowest button while excluding only
  *our own*. With a second mod doing the same thing the two leapfrog -- we drop
  below them, they drop below us -- a row per cycle, until both are far off the
  bottom. With one such mod installed it never shows, because a mod's own button is
  excluded from its own measurement and the lowest row stays the exit group forever.
  Poker now measures once per menu and remembers. **Blackjack still has this bug**
  and will still creep, though it settles now that we hold still.
- **Holding still was not enough: it left a hole.** Remembering our first answer
  stopped *us* walking down the screen, but that first answer had already been
  measured against Blackjack's button -- so POKER landed a row under BLACKJACK, and
  when Blackjack then leapfrogged below us the row it vacated stayed empty. On screen
  that is a double gap between EXIT and POKER, and it looks like our spacing
  arithmetic rather than like another mod moving. **Measure only the buttons the menu
  declares as its own** -- `MenuScreen` has `_playButton`, `_playerButton`,
  `_tradeButton`, `_hideoutButton`, `_exitButton` and two contextual ones, exactly
  the way `MenuTaskBar` has `_toggleButtons`. We then take the row directly under
  EXIT and hold it, and anything else that measures the lowest button settles under
  us. No negotiation, no hole, and it works whatever else is installed.
- **Read `BepInEx\LogOutput.log` before guessing.** Two "menu button added" lines
  is how the leapfrog was found. `[Poker] client loaded` confirms the plugin
  started; its absence points at the validator.

### The client had never been compiled, and that hid a real bug

`Poker.Client` can only be built on a box with the game on it, so for five commits it
was written and never once put through a compiler. The first build found two errors,
both in `MenuButtonPatch` -- the file the notes list as porting from Blackjack
*unchanged*:

    'MenuScreen' does not contain a definition for 'Awake'
    the type name 'MainMenuBaseScreenController' does not exist in the type 'MenuScreen'

It was ported from an older copy of Blackjack that still named its patch targets in
attributes. **On EFT `0.16.9.5` `MenuScreen.Awake` is private and `Show`'s controller
argument is an obfuscated nested type with no name to write down**, so neither can be
named at compile time. Blackjack hit this and moved to a `TargetMethods()` that looks
both up through `AccessTools` at load; that is now here too. Harmony never minded --
it takes a `MethodBase`. It was only ever the compiler.

Two things worth keeping from it:

- **"Ports unchanged" is a claim with a shelf life.** The original moved on and this
  copy did not. When this file says a file ports as-is, diff it against the original
  rather than trusting the sentence.
- **A client commit that has not been compiled has not been written.** The notes said
  "the mod has run in the game" and separately "four commits have never been
  compiled", and both were true -- the button worked on an older EFT build and the
  code that drew it had since stopped compiling. Build the client at the first
  opportunity on any box with an install, even if the game is never started.

### The task-bar tab

`TaskBarTab.cs`, ported from Blackjack, and the reason POKER is reachable from the
hideout, the flea market or a trader screen rather than only from the main menu. It
puts a tab on `EFT.UI.PreloaderUI.MenuTaskBar` beside MAIN MENU and HIDEOUT.

It is **not a patch**. `Heartbeat()` polls once a second, because the bar has to be
found again after every raid and after any mod that rebuilds the row, and a poll
notices both without naming a method a future build could rename.

**Blackjack's notes are addressed directly to whoever writes the second mod, and both
of its rules are obeyed here.** They are not style preferences -- each is a bug that
mod would otherwise ship:

- **Take the template from `_toggleButtons`**, the private dictionary keyed on
  `EMenuType` that holds only the game's *own* tabs. A mod that instead picks a
  template geometrically eventually clones **Blackjack's** tab and inherits a diamond
  and a pile of disabled components. `Keyed()` reads it via `AccessTools.Field`.
- **Split the row on the spacer's `flexibleWidth`, not on the widest gap.** Added tabs
  eat that gap, until measuring decides the row is one group and puts the new tab
  beside SETTINGS. `Divider()` finds the spacer by its `flexibleWidth`.

Two things added here on top of the port:

- **A spade, not Blackjack's diamond.** The two tabs sit side by side with the same
  label size in the same colour, so the pip is the only thing telling them apart at a
  glance. `MenuIcon.Draw` normalises rotation and scale on the borrowed icon, because
  a spade that inherits a mirrored transform comes out looking like a trophy -- which
  is the reason Blackjack picked the one suit with no up or down.
- **`IsAnotherModsTab()`** guards the degraded fallback path, so we cannot clone the
  other mod's tab even if the keyed lookup fails.

**The tab dims with the row, and the toggle is the wrong thing to read.** MenuTaskBar
locks the bar for a raid through `SetButtonsInteractable(false, NOT_AVAILABLE_IN_RAID)`,
which reaches `HoverTooltipArea.SetUnlockStatus` and ends in
`MyExtensions.SetUnlockStatus(CanvasGroup, bool, bool)` -- and that sets the **wrapper's
CanvasGroup** to alpha 0.3 and `interactable` false. It never touches
`Toggle.interactable`, which is the serialized field `PokerTabClick.Mirror` used to
read, so the mirror reported "live" and our tab stayed lit beside a row of grey ones
while a raid loaded. `MirrorGroup` is that CanvasGroup and is the signal that actually
moves; `Toggle.IsInteractable()` -- the method, not the field -- is the fallback.

`LockedAlpha` is 0.3 because that is the literal in `SetUnlockStatus`, so ours greys to
exactly the row's shade rather than nearly it. `TaskBarTab.InRaid` is checked first and
on its own: the table is closed at the first hint of a raid and a tab still lit at that
moment invites a click going nowhere, so that must not depend on the bar having dimmed
itself. The hover highlight is switched off with it, because the pointer can already be
resting on the tab when it locks and the exit handler will not fire.

**Blackjack found this one first**, and it is in both mods now.

**The tab is the only way in, and `MenuButtonPatch` is gone.** There was a main-menu
button as well, cloned onto `EFT.UI.MenuScreen`. It was the weaker entrance twice over:
it existed only on the main menu, where the tab is on every out-of-raid screen, and it
added a card game to a list of five reading ESCAPE FROM TARKOV, CHARACTER, TRADING,
HIDEOUT, EXIT -- with Blackjack installed as well that list grew by 40% and the two mods
were the loudest thing on it.

It was switched off behind an F12 setting first, and that was the wrong shape of answer:
a setting is a promise that turning it on works, and this one had never been exercised
since it was disabled. **A dead option is worse than no option**, so the setting, the
patch and the file went together. The history is still worth reading in git -- the
leapfrog, the hole it left behind, and `MenuScreen`'s own `_playButton` / `_hideoutButton`
fields -- if a second entrance is ever wanted again.

**The suit is chosen in `MenuIcon` and nowhere else, and that used to be two places.**
`MenuButtonPatch` carried its own pasted copy of the icon routine, drawing a *diamond*
and missing the `icon.color = Color.white` the shared one sets -- so the menu entry was
both indistinguishable from Blackjack's at a glance and visibly weaker than it, the
borrowed icon's tint bleeding through. It is `MenuIcon.Draw` from both entry points
now. This is the same failure as the `MenuScreen.Awake` one: a file copied from
Blackjack, kept in one place and not the other.

### The pip is 160 units wide, and that one number broke both entrances

**An `Image` reports its sprite's native size as its layout-preferred size, and a
layout group believes it.** `Textures.Suit` draws 160 pixels square, and the canvas is
at 100 reference pixels per unit, so the pip asks for **160 units where the hideout's
own icon asked for 25**. Everything below is that, wearing two disguises:

- **The task-bar tab came out 230 wide against the game's 112.** It reads as a font or
  a padding fault, and a whole round of fixes was aimed at both before anything was
  measured.
- **The menu button's icon blew up on hover**, when whatever the hover state dirties
  finally let the Image have the width it had been asking for all along. A spade
  magnified sixfold and cropped to its middle shows its two lobes and nothing else,
  which is why it looked like the icon had been pulled apart into two of something.

`MenuIcon.Pin` holds the icon to the footprint of the one it replaced -- read off the
rect *before* the swap -- with a `LayoutElement` for the parent that measures and
`SetSizeWithCurrentAnchors` for the one that does not. Not `sizeDelta`: on a rect that
stretches with its parent that is not a size at all, and an icon anchored that way
comes out inflated by the padding rather than pinned.

**The label was innocent the whole time, and the tab is the reason to distrust a
plausible story.** POKER at 230 against HIDEOUT at 112 pointed straight at text
fitting, and three real faults were duly found in `Relabel` -- auto-sizing, growth in
one direction only, chrome counted twice. All three are worth fixing and **none of them
was happening**: the template's label measured 16pt at 64.6 wide and ours 16pt at 48.3,
so our label was the *narrower* of the two. They are kept as defence and the comment on
`Relabel` now says so out loud.

**`Measured()` is what ended it**, and it is worth keeping for the next one. It logs
the template's geometry and the clone's side by side, once, a frame after the tab is
built -- every child's width, its `LayoutElement`, and each label's size and
auto-sizing. A layout fault is the one class of bug a compiler, a test and a screenshot
are all bad at: the screenshot says it is wrong and nothing says by how much or which
box is carrying the extra. One line of log said `Icon w=25` against `Icon w=160` and
there was nothing left to argue about.

Separately, and not about size: **an `Animator` is a `Behaviour`, not a
`MonoBehaviour`.** `Neuter` sweeps `GetComponentsInChildren<MonoBehaviour>` and so
never saw the one the tab clones, which then went on animating a tab whose toggle no
longer drove it. Frozen instead -- `Instantiate` copied the template's current values
and the template is picked unselected, so freezing keeps exactly the resting look.

### The table was laid out against the picture, not the table in it

Three faults, all visible in one screenshot: the community cards off the middle of the
cloth, the seats either side sitting on the table, and the player's hand tangled up
with the status line and the action buttons.

- **The cloth is not centred in `table.png` and does not fill it.** It is 0.42 x 0.34
  of the image and sits 2.1% above its middle. Everything placed at the centre of the
  felt *rect* is therefore placed against the photograph rather than against the table
  in it. `ClothHalfWidth` / `ClothHalfHeight` / `ClothRise` carry the measurement, and
  the board, the pot and the seat ring are all positioned from it.

  **Measure the cloth by hue, not by brightness.** The green is in shadow down its left
  side, so a brightness test drops that edge and reports the cloth 3% right of where it
  is -- a plausible-looking answer, in the wrong direction, that would have moved the
  board further off centre. Testing for "greener than it is red or blue" finds the
  shadowed edge and puts the cloth centre within a pixel of the image's.

- **No single ellipse can seat this table.** The old ring was 0.52 x 0.74 of the felt
  rect, which put the side seats 534 out with a 240-wide plaque on them -- an inner
  edge at 414 against a cloth reaching 454, so they sat on the playing surface. Widen
  the ring to clear it and the seats *above* the table leave the screen, because they
  sit at 0.81 of the ring's height while the player sits at all of it. Both ends cannot
  be satisfied by one pair of radii.

  `SeatPosition` pushes each seat out along its own direction instead, until its box is
  clear of the cloth in one axis or the other -- `min(clearX/|cos|, clearY/|sin|)`. A
  seat to the side goes far enough sideways, one above goes far enough up, and neither
  pays for the other. Checked at two, three, four and five seats: every seat clears the
  cloth and stays inside the play area.

- **A layout group measures a child's rect and ignores its `localScale`.** The same
  fault as the menu pip, from the other direction. Cards are scaled rather than resized
  -- `CardView` sizes its pips and corner blocks in absolute units, so a smaller rect
  would not make a smaller card -- so every row was laid out at a full 96x138 per card
  for cards drawn at 44% and 78%. A seat's two cards reserved 198 of width to draw 90;
  the five on the board reserved 520 to draw 414. That is most of where the crowding
  came from, and why the gaps between cards looked nothing like the spacing asked for:
  the spacing was right and the slots either side of it were twice the size of their
  contents. `CardSlot` wraps each card in a slot the size it is actually drawn at.

**`StageRise` is a solved constraint, not a preference.** The seats above the table
have to clear the cloth and stay under the title, and the player's seat below it has to
clear the cloth and stay above the status line; together those leave it between about
22 and 41, and it is 32. Move the title, the status line or the action strip and it has
to be worked out again.

### Escape, and why watching the key was never enough

The table is our window floating over one of the game's screens, and the game has no
idea it exists. Watching for the key in `Update` closed the table but did not *stop*
it: the stash or the flea market underneath took the same escape on the same frame and
backed out too, so closing the table also left the screen it was opened from. From the
hideout it read as the mod throwing you out of the hideout.

**Take the command out of the frame's list; do not answer it.** EFT's input system is a
tree of `InputNode`s under an `InputTree`, and
`InputNodeAbstract.TranslateInput(commands, ref axes, ref cursor)` is what walks it:
every node is handed the same `List<ECommand>` and recurses into its children. Removing
`ECommand.Escape` from that list before the root recurses means nothing below is ever
offered it. `InputTree` is the root and does **not** override `TranslateInput`, so
patching the abstract base's implementation *is* patching the root -- one patch for the
stash, the flea market, the hideout, a trader screen and whatever a future build adds.

**The obvious hook is a stub, and it cost a round trip.** `UIInputRoot.TranslateCommand`
is the root of the UI input tree and its name says it translates commands. Its entire
body is `return ETranslateResult.Ignore`. Patching it applied cleanly, logged no error,
changed nothing -- and disabled the key-watching fallback that had at least been closing
the table, so escape went from half working to not working at all. **Read a method's IL
before hanging behaviour off its name**; `Body()` in a Cecil probe is four lines.

The `Update` poll survives as a fallback for a build where the patch will not apply -- a
table that cannot be closed is worse than one that closes the screen behind it -- and
`EscapePatch.Applied` is what keeps the two from both firing.

Blackjack patches the same method. Two prefixes on one method is ordinary Harmony, and
only the mod whose table is open answers.

### Playing while a raid loads, and why it is not allowed

**Tried, shipped, and taken straight back out.** It reads like a free win: the task bar
stays up through matchmaking and the loading screen, the player can already open their
character there, and a few hands beats watching a progress bar.

What made it look easy is that `Singleton<GameWorld>.Instantiated` -- the raid test both
mods have always used -- is true from the moment a raid starts *loading*, not when it
starts. So the table was being shut the instant the player queued, and the obvious
refinement was to wait for a signal meaning the raid had actually begun:
`GameWorld.MainPlayer` being filled in, or `AbstractGame.Status` reaching `Started`.
Both were read out of the installed assembly rather than guessed. **Neither fired.** The
table stayed up into the raid, and the panel's backdrop is nearly opaque and swallows
every click, so it did not merely look wrong -- it locked the player out of their own
game.

The rule is therefore not "close when the raid starts" but **close at the first hint of
one**. Being early costs a few hands of cards. Being late costs the raid. `GameWorld`
existing is the earliest signal there is, and that is exactly why it is the one used.

It is checked every frame in the plugin's `Update` rather than in the tab's
once-a-second heartbeat, because a poll can be a second late and a second is enough. In
co-op the moment is not even the player's to choose: the host starts the raid and pulls
them out of the lobby with the table open.

**If this is ever reopened**, the thing to establish first is a signal that can be shown
to fire -- log it through a real raid before anything depends on it. Two plausible ones
already failed silently.

### Naming the bots

`BotTable` is injectable and `Types["usec"].FirstNames` is the game's own PMC
nickname list -- 619 of them, the names a player meets in raids. `BotNames` reads
**both `usec` and `bear`**, which carry the same list, so a change to either on some
future build cannot leave the table nameless. It reads once, filters to ASCII names
of 16 characters or fewer, and hands distinct ones to each table.

The filter is not fussiness: the panel borrows whatever font the menu happens to
have loaded, and a name that renders as boxes is worse than a numbered seat. The
scav lists are Cyrillic throughout and a few PMC entries are too.

It goes through `INameSource` for the same reason the money goes through `IBank`.
The engine *takes* names rather than inventing them -- it has no business knowing
where a good name comes from -- and numbers any seat it is not given one for.

## The chips, and the stakes that follow from them

Six denominations, drawn from an image cut into one file per chip: **10k, 25k,
50k, 100k, 500k, 1M**. `ChipView` holds the value beside the file name, so the
artwork and the arithmetic cannot drift apart.

**The stakes are set by the chips, not the other way round.** The smallest chip is
10,000, so that is the small blind: blinds are **10k / 20k** and the buy-in
**1,000,000**, fifty big blinds. Every stake is
a whole number of the smallest chip, because a blind that cannot be built out of
chips is one the table can never show honestly. The first stakes were 25 / 50 with
a 5,000 buy-in, at which no chip could ever have appeared on the felt.

**Greedy breakdown is wrong for this set, and looks right until it is not.**
10,000 does not divide 25,000. The pre-flop pot at these blinds is exactly 30,000,
which is three 10k chips -- and a greedy pass renders it as one 25k chip with 5,000
stranded, so the very first thing anyone sees is wrong. `ChipView.Breakdown`
searches for the fewest chips that make the amount exactly, in units of the 5,000
the denominations share, and reports anything genuinely unrepresentable as a
remainder rather than rounding it away.

### Chips in front of the seats was tried and abandoned

Three goes, and none of them looked like a card table. Worth writing down so it is not
started a fourth time on the same reasoning.

The idea was a pile of chips on the cloth in front of each seat, growing and shrinking
with that seat's stack -- the one thing on the table that would show at a glance who was
winning. It kept failing on the same rock: **the table is photographed from directly
above.**

- **Tall columns overlapped by three quarters** drew a ladder of green crescents. The
  chips are drawn face on, so a heavy overlap leaves nothing of each one but a sliver of
  rim -- and from this camera a stack of chips is one disc anyway. Height is the thing
  the view cannot show, so it cannot be what carries the amount.
- **Centred rows of four** drew a green pyramid, because each row is narrower than the
  one below and every one is centred.
- **Separate stacks in a row along the rail**, which is what a player's chips genuinely
  look like from overhead, still read as a cluster of flat green circles rather than as
  money.

The underlying problem is the artwork, not the arrangement. These chip faces are big
flat top-down discs with a value printed across them, drawn to be read at pot size --
somewhere around 44 units. At the 20 a seat's spot on the felt allows, the print is
mush and all that is left is a coloured circle. **Anything built out of them at that
size will look like coloured circles**, however they are arranged, so a fourth
arrangement is not the answer. Chips drawn small, or drawn at an angle with a visible
edge, would be -- that is new artwork, not new layout code.

What survives the attempt: `ChipView.Breakdown` and `ChipView.Build` are untouched and
still draw the pot, which is the one place these chips are shown at a size they were
made for. And the reason a breakdown is wrong for a *stack* is worth keeping if this is
ever revisited -- 765,000 is a 500k, two 100ks, a 50k and a 10k, and so is 1,200,000,
and so is 250,000, so a pile built from one would barely move between a short stack and
a big one.

**Settled with the money path: a chip is a rouble, and the ceiling rose.** The rouble
buy-in was capped at 500,000, well under the chip buy-in of the day, so the table asked
for money the wallet refused. The cap is 5,000,000 now. It cost nothing while the
chips were notional and became blocking the moment they were not, which is the usual
shape of a deferred contradiction.

The same rate is why no other wallet can sit down yet: one chip to the unit means a
1,000,000 chip table needs 1,000,000 of something, and nothing but roubles is held in
those numbers. A chips-per-unit rate per wallet is what opens the rest up.

## The money

**One chip is one rouble.** The buy-in is debited on sitting down, the stack is
credited on standing up, and the difference is what the player won or lost. That
rate is also why roubles are the only wallet that works at these stakes: a
1,000,000 chip table cannot be bought into with two bitcoin, and nothing else is
held in numbers like these. Giving each wallet a chips-per-unit rate is what would
open the rest up.

### Currency only, and why the valuables went

**Roubles, dollars and euros. Nothing else.** GP coins, physical bitcoin and Lega
medals were stakeable and were removed deliberately. Do not add them back without
reading this.

- **Bitcoin and Lega medals have a `StackMaxSize` of 1** -- one item per unit, one
  grid cell each. A five-seat table paying back a doubled-up buy-in hands over a pile
  of coins measured in free grid cells rather than in money. That one fact drove the
  buy-in ceilings, forced a separate capped paytable back under UTH, and was the
  reason the payout scale was this mod's hardest open problem for weeks.
- **They could never have been tested anyway.** All three read zero on both profiles,
  so the riskiest payout path in the mod had nothing to exercise it with.
- **`WalletKind` went with them.** It existed to mark a wallet as something the table
  should not treat like money, and **nothing ever read it** -- the distinction was
  carried entirely in the buy-in ceilings. A record field that is never branched on is
  a comment with a type, so it is now a comment.

What this does **not** fix: dollars and euros still cannot sit down. One chip to the
unit means a 1,000,000 chip table needs 1,000,000 of something, and their ceilings are
5,000. **Roubles remain the only wallet a player can actually use**, and the
chips-per-unit rate is still what opens the other two.
`AWalletThatCannotCoverTheseStakesIsRefusedByName` pins the refusal, and now does it
with dollars -- a case a player can really hit -- rather than with bitcoin.

### Escrow holds the live stack, and that is the whole difference from Blackjack

Blackjack's escrow recorded a *stake* until a hand settled and then dropped it.
That is not enough here. One buy-in is taken and the player then holds a number
that moves every hand, so what is owed back moves with it -- and a crash has to
return **what they actually have**. Recording the buy-in and stopping would refund
a player who had lost most of it and rob one who had doubled up, both silently,
and both looking like a payout bug rather than a bookkeeping one.

So `EscrowStore.Record` **replaces** rather than accumulates, and is called after
every hand. `EscrowFollowsTheStackRatherThanTheBuyIn` pins it, and asserts the
recorded value actually changes -- without that, recording the buy-in every time
would pass.

### The order of the last two lines matters

The cash-out credits **before** it releases escrow. A crash between them leaves the
stack recorded and refundable, which is the safe way round; the other order pays
nothing and forgets it was owed. Deleting the release entirely fails five tests,
which is the shape of that mistake.

### A busted player is not topped up

The console tops everybody up and is right to -- it is a harness. Here the chips
cost currency, so a fresh stack is a fresh buy-in and has to be asked for.
`PokerService.Deal` refuses rather than creating chips out of nothing. Busted
*bots* are still replaced by new characters, which costs nobody anything.

### Mutation-checked, seven faults, each caught

Escrow recording the buy-in rather than the stack (2 fail), the cash-out paying the
buy-in rather than the stack (6), escrow never released (5), an abandoned stack
never given back (2), the buy-in taken without checking it could be afforded (1),
a busted player topped up for free (1), and money never flushed to disk (1).

**`MoneyInvariantTests` was written before the settlement it checks**, which is the
instruction this file has carried since before there was a server. The invariant is
that the change in the wallet equals the change in the stack across a whole
session; an end-of-run balance check would miss errors that cancel.

`TheBotsNeverTouchTheBank` is the other one worth keeping: twenty hands of four
seats betting, raising and busting must move the wallet by exactly nothing.

### Two transports, one service

`PokerRouter` serves plain static paths and `PokerItemEventRouter` serves the same
five actions on the endpoint EFT already uses for moving items. They share a
service on purpose -- a second copy of the flow would be a second set of money
bugs -- and `BothTransportsMoveTheSameMoney` pins that.

The difference is only what comes back. A static route returns JSON and the
client's stash goes stale; an item event returns the `ItemEventRouterResponse` SPT
itself filled in, so the inventory updates without a reload. The table rides along
in `ExtensionData` under `poker`, because **an item-event reply carries
`ProfileChanges` and nothing else** -- and a second request for the view is a
second chance for the two to disagree.

### `ProfileSync`, and why a stale stash is not only cosmetic

Money moved through a static route lands in the profile and leaves the running game
none the wiser. That looks like a display fault and is worse than one: the client
goes on believing in stacks the server has deleted, so the next stack the player
drags produces

    Unable to merge stacks as destination item: ... cannot be found

SPT holds a session's profile changes until the client's next item event and hands
them back on that reply. So the fix is not to re-send the money -- it has already
moved -- but to give the client a reason to ask. `ProfileSync.Request()` sends an
event that does nothing at all, and is called after the buy-in and after the
cash-out. `PokerActions.Sync` is the server half and the two names must stay in
step.

### The pot reads as a column, and the table fades

Two client details worth not undoing.

**The pot's total sits under its chips, not beside them.** Side by side, a stack of
overlapping discs and a five-figure number compete for the same horizontal space and
the eye has to work out which belongs to which. `ChipView.Build` is a vertical group
with the chips in a row above the number, and the sizes are **computed rather than
left to a `ContentSizeFitter`** -- the pot holder is itself a layout group with
`childControl` off, so a fitter resolving a frame later leaves the pot jumping on its
first draw.

**Closing fades, and the numbers are Blackjack's.** The backdrop is 0.93 opaque, so
toggling the canvas takes the whole screen from table to menu in one frame, which
reads as a hard cut. A `CanvasGroup` on the canvas root fades it: **0.16 seconds,
linear, both directions**, on unscaled time, with raycasts blocked the moment a close
starts so a stray click cannot land on a table that is leaving.

Those numbers are not a first guess -- they are what Blackjack arrived at by trying
it, and Blackjack's transition is the one that reads correctly. **Do not tune this
without watching it.** An earlier pass here reasoned that a linear fade on an opaque
backdrop is a muddy cross-dissolve and "improved" it to an eased, asymmetric one,
which was substituting an argument for the thing that had already been tested.

### The buy-in asks first

`SIT DOWN` was written when sitting down was free. It now spends a million
roubles, so the price is on the button and the button asks twice. The seats, chips
and blinds are constants in one place so the label cannot drift from the request --
which is the same failure the five stake defaults have, one screen further out.

## Things that will bite you

Carried over from Blackjack. Each cost real time there. None are hypothetical, and
all of them still apply to this mod.

- **`new ItemEventRouterResponse()` is not a usable response.** Its constructor
  initialises nothing, and `RemoveItemByCount` reaches into
  `output.ProfileChanges[sessionId]`, so a hand-built one throws
  NullReferenceException -- *after* the items are already gone. That failure reported
  itself as "not enough roubles" while the stake had left the stash. Get one from
  `EventOutputHolder.GetOutput(sessionId)`.
- **A mod can change any item's stack limit.** Roubles cap at 1,000,000 in the base
  database and at 20,000,000 on a server running BarterItemsStacks. Read
  `StackMaxSize` live. Clamp it to at least 1: a limit of zero, which a careless
  item mod can produce, makes the splitting loops take zero each pass and hang a
  server thread rather than fail.
- **Stack limits cannot be reported at startup.** `PostLoad + 1` is not last --
  BarterItemsStacks rewrites them about half a second later. Report them on first
  contact instead, which is the earliest the answer is trustworthy.
- **`PaymentService` cannot settle a bet.** Both entry points derive currency from a
  trader. Walk item stacks directly, as `Bank` does.
- **`AddItemToStash` can decline an item without throwing.** A full stash silently
  swallows a payout. Compare the balance either side of every move against what was
  intended and post the shortfall as mail rather than losing it.
- **An item-event reply carries `ProfileChanges` and nothing else.** The round rides
  in the response's `ExtensionData`, or the client needs a second request for it.
- **A custom static route does not update the client's inventory.** Money lands in
  the profile but the stash view stays stale until reload, which reads to a player
  as the mod eating their winnings. Use item-event actions for the real client.
- **`[JsonConverter]` on the enum type is not enough.** System.Text.Json resolves
  converters property attribute first, then `options.Converters`, then the type
  attribute -- and SPT registers `EftEnumConverterFactory` into `options.Converters`,
  which outranks anything declared on the enum. Blackjack's enums kept serialising
  as integers until the attributes moved onto the **properties** of the view record.
  Do it that way from the start.
- **The table is in memory and the stake is not.** Record every stake in escrow
  until settlement and refund orphans, or a crash mid-round takes the money and
  leaves no hand. In UTH the Play bet is collected later than the Ante, which is
  exactly why `EscrowStore.Hold` accumulates rather than replaces.
- **State routes are called before any hand exists.** An empty table must describe
  itself rather than indexing into cards that are not there. Blackjack's
  `DealerView` threw on a fresh table and would have failed every visit to the panel.
- **Naming a property `Path` shadows `System.IO.Path`** inside the same class and
  breaks every `Path.Combine`.
- **`OnLoadOrder` has no `PostDBModLoader`.** Values are `Watermark`, `Preload`,
  `GameCallbacks`, `TraderRegistration`, `Routers`, `HandbookCallbacks`,
  `SaveCallbacks`, `TraderCallbacks`, `PresetCallbacks`, `RagfairCallbacks`,
  `PostLoad`.
- **SPT's DI registers a class against every non-System interface it implements**
  (`DependencyInjectionHandler.InjectAll`), so `Bank : IBank` resolves for free.
- **The client plugin must be built against the install it runs on.** 4.1.3's
  `PluginValidator` reads a plugin's references to `spt-*` and requires a
  major.minor match. It targets `net472`, not .NET 10, because it runs inside the
  game's mono runtime.
- **Bash heredocs mangle backslashes**, and a long one containing quotes will fail
  to parse outright. Use the Write tool for C# and for any large file. This is not
  theoretical: rewriting the panel by heredoc failed to parse on the quotes, having
  already been warned about here.
- **The .NET 10 SDK on Joel's box is a user-local install** at
  `%USERPROFILE%\.dotnet` (10.0.400) and is **not on PATH**. A bare `dotnet` finds
  `C:\Program Files\dotnet`, which has only SDK 8, and fails with `NETSDK1045: The
  current .NET SDK does not support targeting .NET 10.0`. Build with
  `& "$env:USERPROFILE\.dotnet\dotnet.exe"`. The .NET 10 *runtime* is in Program
  Files, so the server runs fine -- only builds break.
- **The server lives in `SPT_Runtime\`, not at the install root.** Joining
  `user/mods` onto the install path creates a folder nothing ever reads, and a mod
  that never loads looks exactly like a mod that loaded and did nothing.
  `pack-mod.ps1 -InstallPath` did this and now finds the runtime or refuses. An
  unrelated mod, IncreaseClimbHeight, had been sitting unloaded in that dead folder
  since August for the same reason.
- **Stake defaults live in five places and must agree**: `HoldemRules`,
  `SitRequest`, the console's `Args`, `PokerPanel.Sit`, and `scripts/smoke.ps1`.
  The harness carrying its own copy made a retune look as though it had not taken
  effect -- the change was fine, the harness was overriding it.
- **Zip entries must be written by hand with forward slashes.** `Compress-Archive`
  writes backslash entry names, which extract on Linux as one file literally called
  `SPT_Runtime\user\mods\Poker\config.json`. An earlier version of this note said to
  use `System.IO.Compression` instead -- **that is not a fix.**
  `ZipFile::CreateFromDirectory` does exactly the same on Windows, which was found by
  opening the first zip this repo produced and reading the entry names. Open the
  archive and add each entry with `CreateEntryFromFile`, replacing the separators
  yourself; `scripts/pack-mod.ps1` does. Check the entry names of anything you ship.

## Talking to the server without a game client

All read out of 4.1.3 and confirmed against a running server, over in Blackjack.
`scripts/smoke.ps1` there is a working reference to port.

- **It serves HTTPS, not HTTP**, on the same port, with a self-signed certificate it
  generates into `user\certs\`. .NET rejects that by default and reports "the
  underlying connection was closed", which reads as the server being down.
- **Every request body is zlib-inflated and every response deflated.** Two headers
  opt out: `requestcompressed: 0` and `responsecompressed: 0`. Without them a plain
  JSON body dies inside `Inflater` complaining about an unsupported compression
  method.
- **Request bodies are matched case-sensitively.** Send PascalCase, or every
  property silently takes its default -- which made a 10,000 bet arrive as 0 while a
  field with a sensible default looked like it had bound correctly.
- **Enums go over the wire as integers, not names** unless made strings
  deliberately. Blackjack shipped integers and regretted it. **Make the wire enums
  strings here from the start**, with the property-level attributes noted above.
- **The session id is a `PHPSESSID` cookie.** In PowerShell it cannot be passed via
  `-Headers` -- `Cookie` is restricted and dropped **silently**, and the server then
  says "session id provided was empty". Use a `WebRequestSession`.

## Whether there is an SPT install depends on the machine

| Machine | Installs |
| --- | --- |
| Joel's home box | `H:\SPT4.1.X` (4.1.3) and `H:\SPT2026` (4.0.13) |
| Joel's work box | `C:\HUH` -- SPT `4.1.3-RELEASE+ddce41c`, **EFT client `0.16.9.5.40743`** |

**The work box is where the client first compiled, and the EFT build number is why it
had not.** See "The client had never been compiled, and that hid a real bug". Do not
launch the game there -- it is a work machine.

With an install present, item templates live at
`SPT_Runtime/SPT_Data/database/templates/items.json`, the server assemblies at
`SPT_Runtime/SPTarkov.*.dll`, and `EscapeFromTarkov_Data/Managed/Assembly-CSharp.dll`
is what the client plugin needs. **Reflecting over the installed assemblies beats
reflecting over the NuGet package**, which tops out at 4.1.2. Mono.Cecil ships with
the game at `BepInEx/core/Mono.Cecil.dll` and reads them without loading them.

Building against NuGet 4.1.2 is safe on a 4.1.3 install -- verified for Blackjack
across 36 types and 63 members.

Without an install, .NET 10 file-based apps make the package a one-liner:

```csharp
// probe.cs, run with: dotnet run probe.cs
#:package SPTarkov.Server.Core@4.1.2
var asm = typeof(SPTarkov.Server.Core.Models.Eft.Profile.SptProfile).Assembly;
var t = asm.GetTypes().First(x => x.Name == "MailSendService");
foreach (var m in t.GetMethods()) Console.WriteLine(m);
```

Source lives at `github.com/sp-tarkov/server-csharp` under
`Libraries/SPTarkov.Server.Core/`.

### The test profiles on Joel's box

Profile `6a8cd3a7e0b8272790f41285` ("test", level 69) is the sandbox. Read off it
on 1 Sep 2026: 24.9M roubles, 110M dollars, 1.02B euros. The other profile,
`6a7501c247d2e12a3892aaee` ("SCOOP", level 16), is the real one; leave it alone.

**GP coins, bitcoin and Lega medals were all three at zero there**, which is part of
why they were dropped: the riskiest payout path in the mod had nothing to exercise it
with. Moot now -- see "Currency only, and why the valuables went".

## Wallets, as verified on a real 4.1.3 install

Only the first three are stakeable. The rest are kept here as verified reference
data, and as the evidence for why they are not -- see "Currency only, and why the
valuables went".

| Wallet | Template | StackMaxSize | Stakeable |
| --- | --- | --- | --- |
| Roubles | `5449016a4bdc2d6f028b456f` | 1,000,000 | yes, and the only one that works |
| Dollars | `5696686a4bdc2da3298b456a` | 50,000 | yes, but refused at these stakes |
| Euros | `569668774bdc2da2298b4568` | 50,000 | yes, but refused at these stakes |
| GP coins | `5d235b4d86f7742e017bc88a` | 100 | **removed** |
| Bitcoin | `59faff1d86f7746c51718c9c` | **1** | **removed** |
| Lega medal | `6656560053eaaa7a23349c86` | **1** | **removed** |

The 4.1.3 namespaces, which are not what older docs say:
`Helpers.Profile.InventoryHelper`, `Helpers.Profile.ProfileHelper`,
`Helpers.Items.ItemHelper`, `Services.Commerce.MailSendService`,
`Servers.SaveServer`, `Common.Models.Logging.ISptLogger<T>`.

## Architecture

Server-authoritative. The client renders what it is handed and sends intents; it
never sees a hidden card, never draws, never decides an outcome. Mirror Blackjack:

```
PokerService              the whole game flow, on IBank / IProfileGateway /
                          IStatsStore / IEscrowStore. No SPT types but MongoId.
PokerCallbacks            static routes  -- curl testing
PokerItemEventCallbacks   item events -- the game client
Bank / ProfileGateway     the only classes that touch SPT services
PokerLog                  the one place that knows how to write a line
```

Two transports, one service. Do not put game logic in either adapter. The interface
seams exist because `InventoryHelper`, `ProfileHelper` and `SaveServer` are concrete
classes with non-virtual methods.

The bots live **inside the engine**, not in the service. They are part of what the
table does when a street advances, and they must be exercisable from
`tools/Poker.Console` with no server present.

## Decisions inherited from Blackjack

These were settled there against the real client and apply unchanged.

- **Not a new hideout area.** `EFT.EAreaType` ends at `CircleOfCultists = 27` and
  each area has a baked prefab. A new value has no model.
- **Not the Rest Space either.** It has a whole game-disc system in it that would
  have solved the camera and cursor problems for free, but the disc player needs
  Rest Space 2, a generator and burning fuel, which locks a new profile out of the
  mod entirely. It stays available as an optional second entrance later.
- ~~**The entry point is a button on `EFT.UI.MenuScreen`**~~ -- **the entry point is
  the task-bar tab, and the button has been removed entirely.** See "The task-bar tab".
- **Guarding against play-in-raid is the mod's job.** Nothing enforces it.
- **The panel floats over a dimmed hideout**, so freeing the cursor and swallowing
  player input is a hard requirement.
- **No hotkey.** A key would be reachable from anywhere, including a raid.
- ~~**Valuables are staked through EFT's own grid component**, dragged into a
  container.~~ **Does not survive here** -- there are no valuables to stake. Currency
  is a number, not a pile of items, so the buy-in is a figure on a button. See
  "Currency only, and why the valuables went".
- ~~**Per-hand settlement, straight to the stash.** No session, no chips, no
  buy-in.~~ **This one did not survive the variant change.** Hold'em cannot settle
  per hand -- a stack is what a bet is sized against and what decides side-pot
  eligibility -- so the player buys in and cashes out. See "Where the money comes
  from". Mail when the stash cannot take the winnings still stands.
- **Settings a player might want to change live in the F12 BepInEx menu**, not in a
  server config file that needs a restart. It is single player; the person sending
  the request owns the server it is sent to.

## Conventions

- **Comments explain why, not what** -- ideally naming the failure the code
  prevents. The codebase is deliberately heavy on rationale.
- Prose in comments uses `--`, not em dashes.
- Tests are named as the rule they pin, not the method they call.
- Every tunable a player might argue about lives in `Rules` or `WalletInfo`.
- **Everything logs.** See "Logging" -- through `IGameLog` in the engine, through
  `PokerLog` on the server, off by default, and never by building a string that is
  then thrown away.

## Verifying

```
dotnet test    # 214 tests, no SPT needed. About 11s.
```

189 over the engine and 25 over the money and both transports. Neither needs a server:
the money tests run on fakes, which is the whole reason they could be written before
the settlement they check.

**Distrust a suite that passes first time.** Every one of the six mutation runs in
this file caught faults the green suite had not, and three of them turned out to be
holes in the tests rather than in the code. Mutation-check anything that ranks a hand
or moves money.

**Chips are conserved. That is the invariant hold'em has and UTH did not.** Every
hand starts with a known number of chips at the table and must end with the same
number: what leaves the stacks equals what the pots pay out plus what is refunded.
`PotBuilder` already carries the pot half of that -- and its own caveat applies here
too, that conservation is necessary and nowhere near sufficient, because chips can
balance and still reach the wrong seat.

Fuzz the betting round rather than the payouts. The bug-dense part of hold'em is not
settlement, it is **who acts next and when a round closes**: min-raises, an all-in
that is too small to reopen the action, a blind that is already all-in, everyone
folding to the big blind. Those are cheap to generate randomly and expensive to
enumerate by hand.

**Do not try to confirm a house edge by simulation**, if a session ever reaches for
it -- see the note under "Parked" for why. Measure decision frequencies instead;
they are proportions and converge in thousands rather than millions.

On a machine with SPT, `scripts\smoke.ps1 -SessionId <id> -PingOnly` first. It
touches no money and proves the mod loaded, the route is reachable, the session
resolved and the profile can be read.

## Releasing

Mirror Blackjack: `releases/Poker-<ver>-SPT4.1.zip`, laid out as
**`SPT_Runtime/user/mods/Poker/`** plus `BepInEx/plugins/Poker/`, and extracted at the
root of the install. Not `user/mods/Poker/` -- that is the same mistake the note about
the runtime folder warns against, and it produces a mod nothing ever loads.

A zip built without `-InstallPath` is named **`-server-only`** and contains no client
plugin, because there was nothing to compile one against. Do not ship that one.

The version lives in **two** places and they must agree: the server csproj
`<Version>` and `ModMetadata.Version`. `pack-mod.ps1` reads the version back out of
`ModMetadata.cs` so the zip name cannot drift from it. SPT's own assemblies are not
bundled -- the server provides them.

**The mod GUID is `com.mybutthasarash.poker`**, and **both halves declare it
unchanged** -- `ModMetadata.ModGuid` on the server and `[BepInPlugin]` on the client
plugin, with no `.client` suffix on either. The Forge checks that the two halves
agree with the GUID the mod is registered under and rejects an upload where they
differ. There is nothing to collide with: BepInEx keeps its own plugin registry and
SPT's mod GUID lives in the server metadata, so the two identifiers never meet.
Blackjack ships as `com.mybutthasarash.blackjack` on the same rule.

---

## Current state

**Update this section as work completes.** The last session found this section
claiming the server did not exist, four commits after it shipped -- a fresh session
reads this first and would have started building one.

- Working branch **`uth`** (named before the variant changed), off `main`, and pushed.
  `main` has not been moved onto it.
- **1.0.0 is the current build**: `releases/Poker-1.0.0-SPT4.1.zip`, both halves in one
  zip, with `releases/CHANGELOG-1.0.0.md` beside it. The 0.1.0 zips were never public
  and are removed rather than left beside it -- a stale wrong-version zip in the folder
  is a wrong one to pick. The version lives in **five** places and `pack-mod.ps1` reads
  it back out of `ModMetadata.cs`, so the zip name cannot drift: both csprojs, the
  console's csproj, `ModMetadata.Version` and `PokerClientPlugin.PluginVersion`.
- Green at **214 tests** -- 189 over the engine, 25 over the money -- mutation-checked
  throughout. Every engine test builds its own `HoldemRules`, so the stakes can be
  retuned without touching the suite.
- **The variant is no-limit Texas Hold'em against bots**, decided after two
  reversals. See the top of this file, and read it before reopening the question.
- **THE MONEY HAS RUN, ON A REAL PROFILE, AND IT WAS RIGHT.** 3 Sep 2026 on the home
  box, profile `6a8cd3a7e0b8272790f41285`, with the server console open. This was the
  largest open item in the mod and it is closed:

  - Buy-in: `debit 2,000,000 Roubles across 3 stack(s)`, 16,208,844 -> 14,208,844,
    **and the expected figure matched the observed one** -- which is the check
    `Bank` does either side of every move and the one Blackjack's money path went
    wrong without.
  - Cash-out: `credit 1,660,000`, 14,208,844 -> 15,868,844 in one stack, reported as
    `1,660,000 against a 2,000,000 buy-in (-340000)`.
  - **`StackMaxSize` read live as 20,000,000**, not the database's 1,000,000, because
    that install runs BarterItemsStacks. The note under "Things that will bite you"
    is confirmed rather than theoretical.
  - Multiple stacks were walked on the way in (three, then four) and coalesced into
    one on the way out. Not one error or exception in the whole run.

  What has **not** been exercised: any wallet but roubles, and the shortfall-to-mail
  path.
- **The one fault the live run turned up is fixed**: an abandoned stack is now given
  back when the panel is opened, not only on a buy-in or a cash-out. See "Open items".
- **The mod has run in the game** and hands deal, play and settle from inside Tarkov:
  the version gate passes, the six routes register, the session resolves, the wallets
  read, and hands run through all four streets with re-raises, folds, side stacks and
  all-ins. Folded seats stay face down at showdown. The uncalled-bet refund fired in a
  real hand -- `refunding 1915000 to seat 0 -- bet 2000000 but only 85000 was ever
  matched` -- which is `PotBuilder`'s two-fail mutation case working on live money.
- **The table has been seen and looks right**, layout fixes included. See "The table
  was laid out against the picture".
- **The tab is the only way in**, at the right size and with the right pip, sitting
  beside Blackjack's. `MenuButtonPatch` has been deleted along with the F12 setting
  that briefly hid it, so the mod no longer patches `MenuScreen` at all.
- **Escape closes the table and nothing else.** See "Escape, and why watching the key
  was never enough".
- **The bots have names** from the game's own PMC list -- JoshuaGraham, BSG_FIX_UR_GAME,
  imhoom__ttv -- and one agent per seat, blended from the named cast (Shark/Maniac,
  Rock/Gambler) so no two tables are the same.
- **The chips are real art with real denominations**, and the stakes were retuned
  to fit them: blinds 10k / 20k, buy-in 1,000,000.
- **Currency only, as of the wallet cut.** Roubles, dollars and euros; GP coins,
  bitcoin and Lega medals are gone, and `WalletKind` with them. Roubles are still the
  only wallet that can actually cover these stakes. See "Currency only, and why the
  valuables went".
- A complete UTH game is in the tree and **parked**. It is green and does no harm;
  nothing new should call into it.

### What to check first at the home box

The entrances, the table and the money have all been seen now. What is left is narrow:

1. **The escrow fault below**, which is money the player is currently owed.
2. **A wallet other than roubles**, which cannot be bought in with at these stakes --
   so this waits on the chips-per-unit rate.
3. **The shortfall-to-mail path**, which needs a stash too full to take a payout.

`[Poker] tab, as laid out --` is still in the log if the tab ever looks wrong again; it
prints the template's geometry and ours side by side.

### Open items

**The money -- run for real, and one fault found by running it**

- ~~**Smoke it against a profile.**~~ **Done, and it was right.** See "Current state"
  for the figures.
- ~~**An abandoned stack is only refunded on `sit` or `leave`.**~~ **Fixed: reading the
  table refunds too.** Worth keeping the reasoning, because "sit and leave both refund"
  sounds sufficient and is not. A player who is owed a stack has no reason to press
  either -- SIT DOWN asks for another buy-in and LEAVE says they are not at a table --
  so the money sat in escrow with nothing telling them it was there. **Opening the panel
  is the one request a player makes without meaning to spend anything**, which is exactly
  why it is the one that has to hand money back.

  What made it look impossible was `State` being a pure read with no
  `ItemEventRouterResponse` to hang item changes off. It never needed one of its own:
  the static callback asks `EventOutputHolder` for an output the same way `sit` and
  `leave` already do, and the item-event `sync` has had one all along.

  Mutation-checked, three faults, each caught: state never refunding (3 fail), the
  live-table guard dropped so a seated player is refunded mid-session (1), and escrow
  never released so the refund pays on every redraw (4).

  **The client has to sync afterwards.** The refund goes through a static route, so the
  money lands in the profile and the running game does not know -- the documented stale
  stash hazard, and here it would look exactly like the mod eating the refund.
  `PokerPanel.Open` calls `ProfileSync.Request()` and shows the note when one comes back.
- ~~**Build the client changes.**~~ **Done**, and they have now been seen as well: the
  tab, the pot column, the buy-in confirmation, the close fade and the whole table
  layout.
- ~~**No profile exists on the work box.**~~ One was registered, and **`-PingOnly`
  passes there**: route reachable, session resolved, profile found, all six wallets
  read, stack limits reported on first contact, no errors either side. The HTTPS,
  compression and cookie handling in `smoke.ps1` all work as ported.
- **A registered profile is not a character, and the money path needs a character.**
  The launcher writes a **384-byte stub** -- `characters.pmc` is `savage`,
  `Encyclopedia`, `Hideout`, `WishList` and nothing else, because the PMC is built
  when the game is first launched and a side is chosen. So every wallet reads 0,
  there is no stash container to credit into, and **the buy-in cannot be exercised
  without launching the game once.** That is the actual blocker on smoking the money.

  Worth noting what it *did* prove: `Bank`'s stack walk ran against a profile with
  essentially no inventory and returned zero rather than throwing, which is the
  "state routes are called before anything exists" hazard passing on the money path.

- **Creating the character over the wire was tried and abandoned. Do not retry it
  blind.** `/client/game/profile/create` takes
  `ProfileCreateRequestData { Side, Nickname, HeadId, VoiceId }`, and calling it
  directly threw `NullReferenceException` inside
  `CreateProfileService.CreateProfile` twice, leaving the in-memory profile
  unreadable until a restart. What was learned, so an hour is not spent again:

  - **The route answers `200` with an empty body when it has thrown.** Nothing in the
    reply says it failed -- the exception is only in the server console. Any script
    driving it must verify by reading the profile back, never by checking the
    response.
  - **The on-disk profile is not touched by the failure.** SPT holds profiles in
    memory and flushes on save, so a restart restores the stub. The damage is
    recoverable, which is the one good thing about it.
  - The likely cause is **request binding**, not the profile: the properties bind
    case-sensitively and PascalCase was sent without ever confirming the
    `JsonPropertyName` the model actually declares. `Nickname.ToLowerInvariant()`
    early in the service is an unguarded dereference, so a nickname that failed to
    bind throws exactly this. **Read the attributes off
    `ProfileCreateRequestData` before sending anything.**
  - Seeding `characters.pmc` with `Info` / `Achievements` / `Prestige` did **not**
    fix it, so the "existing PMC is carried forward" theory is wrong or incomplete.

  Launching the game once is cheaper than finishing this, unless a machine turns up
  where that is impossible.
- **Give each wallet a chips-per-unit rate.** One chip to one unit means only roubles
  can buy into a 1,000,000 chip table, so **dollars and euros are stakeable in the
  enum and refused in practice** -- their ceilings are 5,000 against a 1,000,000 chip
  buy-in. A rate per wallet is what opens them up, and it is now the *only* thing
  standing between the mod and its full wallet list, because the valuables that used
  to complicate this are gone.
- **`StatsStore` is still to port**, and the recorded fields are poker's rather than
  blackjack's -- hands played, biggest pot, showdowns won, best hand.

**The client**

- ~~**The table layout is derived, not eyeballed.**~~ It was eyeballed, and seen on a
  screen it was wrong in three ways at once. Now derived -- see "The table was laid
  out against the picture, not the table in it".
- **The buy-in is hardcoded** in `PokerPanel.Sit` at five seats, 1,000,000, 20k
  blinds. `/poker/sit` takes all three, so a setup row is easy.
- Side pots are settled correctly but are not drawn as separate pots.

**The game**

- **Give the bots a life the player can see.** Mood and memory are in, so are names
  and one agent per seat, and a busted bot is already replaced by a new character --
  `PokerService.Reseat` improvises somebody and swaps the agent, so the table turns
  over on its own. Still missing: **engine-emitted reactions**, and **thinking time
  taken from how close the decision was**. See "They have to feel like real people".
- **Re-seating is settled for the mod and deliberately different in the console.**
  `PokerService` replaces a busted bot with a new improvised character and **refuses
  to deal to a broke player** -- their chips cost currency, so a fresh stack is a
  fresh buy-in. The console tops everybody up, which is right for a harness and would
  be minting roubles anywhere else. `HoldemTable.Reseat` supplies the mechanism and
  takes no view.
- Decide UTH's fate -- delete, ship as a second table, or leave parked. Undecided
  on purpose; it costs nothing where it is.

**Outside this repo**

- ~~**Blackjack's menu button still leapfrogs.**~~ Moot: both mods' main-menu buttons
  have been removed, and the tab is the only entrance in either.
