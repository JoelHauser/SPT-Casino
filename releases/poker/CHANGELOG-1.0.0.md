# Poker 1.0.0

**First release.** No-limit Texas Hold'em against bots, at a table in your menu, played
with your own roubles.

This is not a slot machine with cards on it. There is a pot, the bots bet into it, and
you win their chips or lose yours.

## The game

Six-max minus one: **up to five seats including you**, small blind and big blind, a
button that moves, and all four streets. Blinds are **10,000 / 20,000** and the buy-in
is **1,000,000** — fifty big blinds, which is a real short-stacked cash game rather
than a novelty.

The engine is the part that had the most work put into it. The hand evaluator is
checked against **all 2,598,960 distinct five-card hands** and their published category
counts, so it is not wrong about a hand anywhere in the deck. Side pots, uncalled bets
and the odd chip on a split are all handled, and the table is fuzzed through hundreds
of hands of random aggression checking after **every single action** that chips are
neither created nor destroyed.

## The opponents

The bots are the point, and they are not a lookup table with names on.

Each seat estimates how often its hand wins by **Monte Carlo rollout** over the unseen
cards — which means aces against one opponent and aces against four are different hands
to them, the thing no starting chart can tell a bot. Against that they weigh the price
in pot odds, their position, what is already in the pot, how deep they are, and how many
players are still live.

They bet from a **discrete menu of sizes** — about a third of the pot, two thirds, pot,
all-in — because naive no-limit sizing is what gives a bot away instantly. You are not
restricted to the menu; it exists so their decisions are tractable.

Seven characters sit behind one decision procedure, separated by five dials rather than
by five different sets of code. Facing a bet, a Rock folds 78% of the time and a Gambler
27%. They are also **blended and improvised**, so a table is filled with people who are
mostly a grinder with a streak of gambler in them rather than with the seven originals,
and **they tilt**: a seat that has been losing chases, a careful one shuts down, and how
much any of it reaches them is itself one of the dials.

They are named from the game's own PMC nickname list, so you are playing against people
called things you would meet in a raid. A bot that busts leaves and somebody new sits
down.

**A bot can only see what a player in that seat could see** — its own two cards, the
board, the betting, the stacks. Never another seat's cards, never the deck. That is
structural rather than a promise: they are not handed the information, so they cannot
use it.

## The money

**One chip is one rouble.** You buy in for 1,000,000 out of your stash, and whatever is
in front of you when you stand up goes back in. The difference is what you won or lost.

- Roubles only for now. One chip to the unit means a million-chip table needs a million
  of something, and nothing else is held in those numbers.
- **Your gear is never at stake.** Weapons, armour and rigs cannot be bet.
- The server deals every card, scores every hand and moves every rouble. Your game
  never receives a hidden card until the hand is over.
- If you close the game at the table, the chips in front of you are recorded. Open the
  table again and they are handed straight back.

There is **no house edge**, and that is worth being plain about: this is not a casino
game with a built-in rake. What the mod pays out is exactly how much better you are
than the bots. Beat them consistently and it is a source of roubles.

## Where it lives

A **POKER** tab on the bar along the bottom of the menu, beside HIDEOUT. That bar is on
every screen outside a raid, so the table opens from the hideout, the flea market or a
trader screen without backing out of them first. The mod does not touch the main menu.

Escape closes the table and leaves you on the screen you opened it from. The table
closes itself the moment a raid starts loading, and the tab greys out with the rest of
the row.

If you also run my **Blackjack** mod, the two tabs sit side by side — one spade, one
diamond — and neither has to know about the other.

## Installing

Stop the server, then extract the archive into your SPT folder — the one containing
`SPT_Runtime`. Both halves are needed: the server deals and holds the money, the client
draws the table.

```
SPT_Runtime\user\mods\Poker\
BepInEx\plugins\Poker\
```

Start the server and look for a **[Poker]** block in the console. Silence means the
version gate — this needs **SPT 4.1.3** or later 4.1.x.

If the tab is missing, check the folder went to `SPT_Runtime\user\mods\` and not to a
`user\mods\` beside it. That is the one mistake that leaves no trace in the log.

## Known limits

- **Roubles only.** Dollars and euros are refused at these stakes.
- **Side pots are settled correctly but drawn as one pot.** The money is right; the
  picture is simpler than the truth when somebody is all-in.
- **No statistics screen yet.**
- Seats, buy-in and blinds are fixed rather than chosen at the table.

## Notes

The card faces are Chris Aguilar's *Vectorized Playing Cards*. They sit as ordinary
PNGs beside the plugin, one per card, alongside the table photograph and the chips —
swap any of them for your own, or delete them and the mod draws its own instead.

Verbose logging is on by default, so the server console narrates every hand and every
bot decision. Turn it off in `config.json` once you are happy it works.
