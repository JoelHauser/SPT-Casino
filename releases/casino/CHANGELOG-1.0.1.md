# SPT Casino 1.0.1

Two changes, both asked for on the mod page.

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

**F12 → Poker → Buy-in.** Anywhere from 200,000 to 5,000,000 roubles, default
1,000,000.

The blinds stay at 10,000 / 20,000 whatever you choose, so this changes how deep the
table plays rather than how much a night costs: 200,000 is ten big blinds and an
all-in-heavy game, 5,000,000 is two hundred and fifty and a slow one. The table says
which as you change it.

Five seats, as before.

---

Everything else is identical to 1.0. No change to the money, the odds, or any table's
rules.
