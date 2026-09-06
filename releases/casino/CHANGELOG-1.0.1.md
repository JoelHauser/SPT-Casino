# SPT Casino 1.0.1

Three changes, all from things people said on the mod page.

## One folder instead of four

The casino installed as one plugin folder and **three** server folders — `Blackjack`,
`Poker` and `Roulette` sitting separately under `user/mods`, left over from when they
were three mods. It is one folder on each side now:

```
BepInEx/plugins/Casino
SPT_Runtime/user/mods/Casino
```

Nothing about how the tables work changed. SPT loads every assembly it finds in a mod
folder and registers all of them, so the seven files that used to be spread across
three folders simply share one.

**Upgrading from 1.0:** delete the old `SPT_Runtime/user/mods/Blackjack`, `Poker` and
`Roulette` folders. Left in place they are still whole mods, and the server will
register every route twice.

**If you were mid-hand when you last played**, move those three folders somewhere else
rather than deleting them, launch once, then delete them. Each keeps a small record of
anything the house still owes you and the casino imports it on first run — you will see
a line in the server console saying so.

## The poker buy-in is yours to set

**F12 → Poker → Buy-in**, from a list of round figures:

| Buy-in | Big blinds |
|---|---|
| 200,000 | 10 |
| 500,000 | 25 |
| **1,000,000** | **50** (default) |
| 1,500,000 | 75 |
| 2,000,000 | 100 |
| 3,000,000 | 150 |
| 4,000,000 | 200 |
| 5,000,000 | 250 |

The blinds stay at 10,000 / 20,000 whatever you choose, so this changes how deep the
table plays rather than how much a night costs. 200,000 is a short stack and an
all-in-heavy game; 5,000,000 is a slow one. The table tells you which as you change it.

Five seats, as before.

## The server console got quiet

The casino used to print about twenty-five lines when the server started — a routes
list, a mod folder, a wheel description and a money notice, three times over, before
you had opened anything. It prints one:

```
[Casino] v1.0.1 ready -- blackjack, hold'em and a single-zero wheel, playing for real roubles.
```

**Verbose logging is off by default now.** It was on, which is why the block was that
long, and it also meant every request and every bot decision was logged while you
played. Turn it back on per table in `blackjack.config.json`, `poker.config.json` or
`roulette.config.json` beside the mod, and that table's full block comes back along
with the detail.

If the house is holding money for you — a hand or a spin that was interrupted — you
will still get a line about it. That one is not silenced, and it goes away once you are
paid back.

---

Everything else is identical to 1.0. No change to the money, the odds, or any table's
rules.
