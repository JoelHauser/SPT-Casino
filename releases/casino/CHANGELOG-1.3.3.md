# SPT Casino 1.3.3 (pre-release)

A sixth table: **Casino War**. This is a pre-release because nobody has played it in game
yet -- see the bottom of this page.

## Casino War

One card for you, one for the dealer. High card wins, aces high, and a win pays even money.

**A tie stops the hand and asks you a question:**

- **Go to war.** Put down the same bet again. The dealer burns three cards and deals one
  more to each of you. If yours is equal or higher, you win: your first bet comes back and
  the second pays even money. If the dealer's is higher, both bets are lost.
- **Surrender.** Give up half your bet and take the other half back.

**The tie bet** is optional, sits beside your main bet, and pays **10 to 1** if the first two
cards tie. It is settled on those two cards, so it pays even if you go on to lose the war.

The rules are Cache Creek's (cachecreek.com/casino-war), played from a six-deck shoe.

**The house edge, computed rather than guessed at:**

| | House edge |
|---|---|
| Main bet, going to war on every tie | 2.88% |
| Main bet, surrendering every tie | 3.70% |
| Tie bet | 18.65% |

Going to war is always the better answer to a tie. The tie bet is the worst bet in the
casino.

**Limits:** 1,000 to 500,000 roubles, or 10 to 5,000 dollars or euros, on each bet -- the same
as Blackjack. **No maximum bet** under *Casino War* in F12 raises that to 100,000,000.

**A tie waits for you.** If you close the table mid-tie, the hand is still there when you come
back. If the server restarts while a tie is waiting, your whole bet is returned the next time
you open the table -- more than surrendering would have paid you.

The table keeps a record behind its own STATS button: hands, wins and losses, ties, wars won
and lost, surrenders, tie bets won, and staked and returned per currency.

## Also

The install step in the build script no longer moves aside a folder just because it shares a
table's name. Only Blackjack, Poker, Roulette and Slots ever shipped as mods of their own, so
only their old folders are retired. This only affects people building from source.

## Before you rely on it

**Casino War has not been played in game yet.** Its rules, odds and money handling are covered
by tests the same way every other table's are -- every way a hand can end, the house edge
checked three ways, and every way the money could go wrong tried on purpose and caught. What
has not been seen is the table itself: where the cards land, how the result reads, and whether
everything lines up on your screen. If anything looks out of place or a hand settles wrong,
please report it.

No change to any other table's odds, payouts, or limits. Install as usual: extract into your
SPT folder and overwrite.
