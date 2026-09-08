# SPT Casino 1.2.0

One real money bug, cards that move like cards, and the first sound cues -- silent for
now, but every table is already calling them.

## A bug: bets could reach into your gear, not just your stash

**Placing a bet could pull roubles, dollars or euros from your pockets, secure
container, backpack or rig** -- anywhere in your inventory holding that currency, not
just the stash. Which stack it drew from depended on inventory order, which the game
does not guarantee, so a bet could quietly empty gear you were about to carry into a
raid.

Every table's Bank now checks that a stack actually resolves to your stash before
counting it. Winnings already paid back into the stash correctly; only what a bet could
spend was affected. Blackjack, Poker, Roulette and Slots all get the fix.

## Cards deal in and flip, instead of popping into existence

Blackjack and Poker's cards now slide in from a shared point above the table -- the
dealer's own row for Blackjack, a marker above the felt for Poker -- landing one after
another the way a dealer actually works a table, not all at once. Poker's hole cards go
out left to right by seat position, a card to every seat and then a second pass for the
next one.

**A card resolving from face-down to face-up flips instead of dealing back in.**
Blackjack's dealer turning over their hole card, and Poker's showdown, used to look like
a brand new card sliding onto a spot that was already occupied. It shrinks edge-on and
grows back out face up instead -- a turn, not an arrival.

## The result waits for the cards to finish moving

Blackjack's WIN/LOSE and Poker's showdown headline ("You win 4,820 with a flush") used
to appear the instant the server replied, which could be before the card that decided
the hand had actually finished turning over. Both now hold back until every card still
in motion has landed, so the table never tells you the result before you can see it for
yourself.

## Sound is wired in, but silent for now

Dealing, chips landing, the wheel and the reels spinning, the win banner -- every one of
those already fires its own cue, on every table. No audio files ship with this release,
so nothing plays yet. Once they do, dropping a file into the plugin's `sounds` folder,
named for the cue, is the entire remaining step -- no rebuild required.

## Slots' item icons, after a game update briefly broke them

An EFT update changed how the item system's internals are named under the hood, which
stopped the reels from rendering real item art. Icon rendering is restored -- resolved a
different way that does not depend on the name at all -- but it has not yet been
confirmed against a live session. If the reels come up blank on this build, that is the
one thing to check first.

---

Everything else is identical to 1.1.0. No change to any table's odds, payouts, or
buy-in.
