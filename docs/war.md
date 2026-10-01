# Casino War -- working notes for Claude

The fifth table in **SPT Casino**. One card each, high card wins, aces high. A tie
stops the hand: the player goes to war (matches the bet, three cards are burned, one
more each, and equal-or-higher wins) or surrenders half. An optional tie bet pays 10
to 1. Plays for **roubles, dollars or euros**.

**Update "Current state" when you finish a piece of work.** A fresh session reads that
section first and believes it.

---

## The rules, and where they came from

Cache Creek's page, https://cachecreek.com/casino-war, given by the user as the
reference. Every rule in `Rules.cs` and every test in `SettlementTests.cs` quotes it:

- Aces high. Main bet pays 1:1. Tie bet pays 10:1, on the first two cards only.
- On a tie: **surrender** forfeits half the main bet, or **go to war** by matching it.
  Three burned, one more each. "If the player's second card is equal to or greater than
  the dealer's, they win and receive an even-money payout on their original bet, with the
  additional bet being returned as a push." Two antes down, three back.

**What the page does not say is how many decks.** Six, and it is not a guess: the page's
own figures only agree with six. The tie bet's "over 18 percent" is 18.65% at six decks,
35.3% at one and 17.8% at eight; the main bet's 2.88% is six decks too.
`OddsTests.OnlySixDecksMatchOverEighteenPercentOnTheTieBet` holds all of it -- written
because the first draft of the comment in `Rules.cs` said 15.3% for one deck, which is
the infinite shoe's number.

**The page muddles one figure.** It says 2.88% for "standard play" and that going to war
"drops it to 2.33%". Under the rules it states, 2.88% *is* always going to war;
always surrendering is 3.70%. 2.33% is what going to war costs at casinos that pay a
bonus when the war ties again -- a rule the page does not describe and this table does
not have. The table follows the rules as written; the README quotes 2.88 / 3.70 / 18.65.

The odd unit of an odd ante on surrender stays with the player (`ante - ante / 2`).

## The maths

`Odds.cs`, closed form, same discipline as the slot machine's: computed, not measured.

- Off a tie, the main bet is a coin toss: expectation exactly zero. All the edge is in
  the ties, which come up `(c-1)/(n-1)` = 23/311 at six decks.
- The burned cards are unseen, so the war is a fresh deal from `n - 2` cards with the
  tied rank two short. A war ties again 7086/95790 of the time, and that goes to the
  player.

Checked two ways in `OddsTests`: an enumeration over rank counts that shares no
arithmetic with the closed form (1, 2, 6 and 8 decks, agreeing to 12 places), and two
million rounds at the real table with real burns and reshuffles. The enumeration leaves
the burns out, and the simulation is what covers them.

## How it is built

```
src/War.Game/       Card, Shoe (6 decks, cut card at a quarter), WarTable, Odds, Rules
src/War.Server/     the slot machine's server, with a table store added
src/War.Client/     WarPanel + WarApi, compiled into Casino.Client -- no project of its own
tests/War.Game.Tests/     38: cards, shoe, every settlement, the odds three ways
tests/War.Server.Tests/   36: the money path
```

Routes `/war/ping`, `/war/state`, `/war/deal`, `/war/decide` (Choice `War` or
`Surrender`), `/war/stats`. Item event `WarSync`. Config `war.config.json`, escrow
`data/escrow-war.json`, stats `data/stats-war.json`.

**`War.Client` has no `.csproj`.** The other tables' client projects exist because they
were standalone plugins before the merge; War never was, so its two files are listed in
`Casino.Client.csproj` and nowhere else.

**`pack.ps1` lists War in `$tables` but not in `$retiring`.** The install step moves any
`plugins\<table>` or `user\mods\<table>` folder aside as a retired pre-casino mod. War
never had one, and a folder called "War" belonging to somebody else would have been
moved.

## The money

The slot machine's order -- check, record, debit, settle, credit, release, save -- with
one thing the slot machine never has: a round that stops half way. `TableStore` keeps
each player's `WarTable` (and its own shoe) in memory between the deal and the decision.

The escrow **replaces** rather than adds, and says what the table holds at each moment:

| When | Recorded |
| --- | --- |
| Before the deal's debit | ante + tie bet |
| A tie is waiting | the ante (the tie bet was paid on the deal) |
| Before the war's debit | the ante twice |
| Settled | released |

Every record is written before the debit it covers and released only after the credit,
so a crash anywhere refunds at least what is owed. A restart mid-tie refunds the whole
ante, which is more than surrendering would pay; that is deliberate, since the fault is
the server's.

A war the player cannot afford is refused **before** anything is recorded, and the tie
stays on the table with surrender still available.

**Every public entry point takes `Casino.Server.SessionGate`** -- Ping, State, Deal and
Decide -- the same per-player lock every table has taken since 1.3.1. `WarSync` is a
ping and a ping refunds from escrow, so without the gate a sync landing mid-deal would
read the live stake as stranded and pay it back. Not re-entrant: nothing gated calls
anything else gated.

`Bank.cs` is the slot machine's as of 1.3.1: it skips null items in the stash, and a
throw from `AddItemToStash` falls through to the shortfall check and mails the rest
instead of returning with it unpaid. War was first written against the older copy and
picked both up when it was moved onto 1.3.2.

**`WalletInfo.AbsoluteMax` = 100,000,000** is a ceiling the F12 switch does not lift.
The bank counts in `int`, a tie bet returns eleven times itself, and a war takes the
ante twice; a hundred million keeps all of that inside `int.MaxValue`. The slot
machine casts any `long` to `int` once its cap is lifted -- a bug this table does not
copy, and one still sitting in `SlotService.PullAsync`.

Mutation-checked, 2026-10-01: ten deliberate breakages of `WarService` -- escrow recorded
after the debit, the tie bet never paid, the tie bet paid twice, the escrow left at
ante + tie during a tie, a waiting tie refunded on contact, the escrow never released,
no save after a decision, the raise never taken, the tie bet's limits unchecked, an
unknown currency played as roubles -- **10 of 10 caught**. The last one survived the
first run: `AnUnknownCurrencyIsRefused` staked 1, which is refused as too small in any
currency. It stakes a legal rouble amount now. The script is
`mutate_war.py` in that session's scratchpad: a dict of anchor -> replacement over
`WarService.cs`, run `dotnet test`, restore.

## The panel

Blackjack's furniture: the same table photograph (`table.png`, already staged beside the
plugin), the same chips, two `MoneyField` boxes (BET and TIE BET), a STATS sheet laid on
the felt.

- **Cards are placed, not laid out.** The dealer's row at the top of the cloth, the
  player's at the bottom, the first card at x -70 and the war card at +70. A layout group
  would slide the first card sideways when the war card arrived.
- **The burned cards stay on the felt**, face down at x -330, so three can be counted.
- **The result sits right of the cards** (x 180..460 on a ~956 px cloth), never on one.
  The rules are printed on the empty felt from the ping, so the cloth cannot promise odds
  the table does not pay.
- **Everything waits for the cards.** The headline, the balance, the buttons and the
  `WarSync` item event all fire from `DealAnimator.After(finish)`. Buttons do nothing
  while cards are moving (`_busy`), so a double click cannot deal over a landing hand.
- **`Dealt` is cleared on every new deal**, so a card that repeats in the same place
  still flies in. Opening the table draws a waiting tie where it lies, without dealing it
  again.
- **A tie hides the betting bar, LEAVE and STATS** and shows GO TO WAR (+ante) and
  SURRENDER (half back). Escape still closes the table; the tie waits.
- Sounds reuse `CardDeal` (from `DealAnimator`) and `ChipBet` (deal, and going to war).
  No new cues, so the sound manifest is unchanged.

The lobby tile is `tile-war.png` in `Casino.Client/assets`. If it is ever missing the
lobby draws a spade, which is its standing fallback.

## Current state

**2026-10-01.** Shipped as the **1.3.3 pre-release**, the sixth table. **Never run in
game.**

- Written on the development box against a stale pre-rewrite `main`, then moved onto
  1.3.2 in a worktree: the War files copied over, the casino wiring redone against the
  Horse Racing tree, and the session gate and Bank fixes added.
- `dotnet test SPT-Casino.slnx`: 685 pass, 74 of them War's (38 engine, 36 money).
  Mutation check re-run after the gate went in: still 10 of 10.
- **The development box has no launched SPT install.** `C:\HUH` holds the unpatched
  `Assembly-CSharp.dll`, and since c792f96 `ProfileSync` names `IClientSession`, which
  only exists after the launcher's patch -- so the plugin does not build against it at
  all. 1.3.3 was built against a stand-in root in the session scratchpad: `C:\HUH`'s
  `Managed` folder with `Assembly-CSharp.dll` replaced by the output of
  `hpatchz` (HDiffPatch 5.1.3, from its GitHub release) applied to the install's own
  `.delta` -- 16,233,472 bytes, the size a correct patch produces -- and `BepInEx`
  linked back to `C:\HUH`. Do that again for any build here.
- **Lobby art** is the user's "Distressed Ace Card Collision Emblem" -- two aces
  clashing -- scaled from 1254 to 320 square (Lanczos) to match the other tiles. It
  already had a transparent background.

What to watch the first time it runs:

1. **The cloth layout.** Every position was worked out from the photograph's measured
   cloth, not seen. A tie prompt that wraps past three lines, a headline that runs into
   the cloth edge, or the burn pile on the rail would all show here.
2. **The deal origin.** Cards fly from a marker at the cloth's top right. Whether that
   reads as a shoe is a matter of looking at it.
3. **Six lobby tiles.** The lobby wraps at four a row since 1.3.0, so War sits on the
   second row beside Horse Racing. Not seen.
4. **Reopening on a waiting tie**, and **a restart mid-tie** -- the server tests cover
   both, the panel has not been watched doing either.
