# SPT Casino 1.1.0

The fourth table, AUTO and SPEED for it, and two smaller things that came out of
building it all.

## A fourth table: the slot machine

Five reels, three rows, and **243 ways to win** instead of paylines — any symbol
landing on three or more reels running left to right pays, and landing more than once
on a reel multiplies that win instead of just counting once. All nine symbols can pay
on the same pull.

The reels actually spin: they snap up to speed, hold flat out, and ease into their
stop with a little overshoot, the way a real reel settles rather than a wheel winding
down.

**92.510% back, 7.490% to the house** — computed from the strips and the paytable
rather than measured by spinning it a lot, and checked against two million pulls to be
sure the two agree.

| Symbol | 3 | 4 | 5 |
|---|---|---|---|
| Can of TarCola | 1x | 1x | 1x |
| Salewa first aid kit | 1x | 1x | 2x |
| Fierce Hatchling moonshine | 1x | 2x | 2x |
| Tetriz portable game console | 1x | 2x | 5x |
| Roler gold watch | 1x | 2x | 5x |
| Golden rooster figurine | 2x | 4x | 12x |
| Graphics card | 5x | 20x | 80x |
| Physical Bitcoin | 10x | 50x | 250x |
| TerraGroup Labs keycard (red) | 25x | 150x | 1000x |

Every icon on the reels is the game's own, rendered from your own installation the
same way the stash renders yours — not shipped art.

There is nothing to sit down at. No seat, no hand to hold, no chips to build up: walk
up, spin, walk away.

**Roubles, dollars or euros** — the first table here that isn't roubles-only.

| Wallet | Minimum | Maximum | Step |
|---|---|---|---|
| Roubles | 5,000 | 50,000 | 5,000 |
| Dollars | 50 | 500 | 50 |
| Euros | 50 | 500 | 50 |

The stake is typed, not just stepped — click the box and enter any amount between the
two ends. **F12 → Slots → No maximum stake** lifts the ceiling entirely, the same way
Blackjack's table maximum works: the machine pays up to a thousand times the stake, so
a capped win is already 50,000,000, and the cap is the house being careful on your
behalf. You can tell it not to be.

Win it back and the banner says how big by how it looks — WIN, BIG WIN, HUGE WIN,
JACKPOT, growing from 36pt to 64pt with the multiple. From twenty times the stake up,
the letters run their own band of colour.

**Slots gets a lifetime record, the same way Blackjack already has one.** A STATS
button next to CLOSE covers the reels with pulls, win/loss, hit rate, the best
multiple you've ever hit, how many pulls cleared JACKPOT, your current and best
streak, and staked/returned/net per currency.

## AUTO, and a SPEED switch that isn't only for it

**AUTO**, under SPIN — click it and the machine keeps pulling on its own, pausing a
beat after each spin so the result actually sits on screen, until you click it again.
Closing the machine stops it too, and so does a pull that comes back with an error: it
will not sit there quietly retrying against a wallet that cannot cover the stake.

**SPEED**, under AUTO — one button, cycling 1X, 2X, 4X, 6X. It is not a blur dialled
up: the belt still scrolls at the same rate either way, a faster setting just runs the
spin for less time, so it travels fewer cells before landing rather than the same
cells faster. And it is not only AUTO's — turn it up and a manual SPIN is quick too.
AUTO's own pause between pulls shortens by the same amount, so a 6X run actually moves.

Both buttons are green throughout. STOP is AUTO clicked again, not a second button,
the same way getting back to 1X is SPEED clicked around to it rather than a switch of
its own.

## One money box, shared with Blackjack

Blackjack's wager box and the slot machine's stake box are the same control now.
Blackjack gains the separators the stake box already had — 1,250,000 instead of
1250000 — and the stake box gains a caret you can actually see, which Blackjack's box
already had. Neither table had both before.

That also caught a smaller bug: the caret used to be invisible the *first* time you
opened either box in a session — closing the panel and reopening it was what actually
fixed it, by accident. It shows straight away now, in both places.

## The server console, quieter still

1.0.1 cut the startup block down to one line naming every table:

```
[Casino] v1.0.1 ready -- blackjack, hold'em and a single-zero wheel, playing for real roubles.
```

It says less now:

```
[Casino] v1.1.0
```

The version was always the part worth having at a glance — SPT logs the rest itself —
and it's still in colour.

---

Everything else is identical to 1.0.1. No change to Blackjack, Poker or Roulette's
money, odds or rules.
