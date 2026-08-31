# Blackjack 1.0.2

One bug reported from play -- **ALL IN did not work** -- and the bet box now
reads in thousands.

## What was wrong

ALL IN staked your whole balance. The table does not take your whole balance — it
takes up to 500,000 roubles a hand, 5,000 dollars or euros, 50 GP coins, 10
bitcoin, or 5 Lega medals.

So a player carrying 200 GP coins pressed ALL IN, confirmed it, got a wager of
200, pressed DEAL, and was refused. The button did exactly what it said and the
table was always going to say no. Nothing was lost and no money moved, but the
button was useless for anyone holding more than the table takes — which, at 50 GP
coins, is most people.

## What it does now

**ALL IN means the most this table will take**, which is your balance or the
ceiling, whichever is lower. The confirmation says which one you are getting:

- under the ceiling — *Bet everything?*
- over it — *Bet the table maximum?*, with what you are carrying underneath, so
  it is clear you are not staking the lot

The confirm button reads BET MAXIMUM instead of BET IT ALL when it is capping you.

If you have switched the table maximum off in the BepInEx menu, ALL IN means all
in again, exactly as before.

## Two smaller things that came out of the same bug

**The line beside the wager box names the right problem.** Betting more than the
table takes and betting more than you own are different mistakes — one is fixed by
betting less, the other by holding more — and both used to read *not enough*. Over
the ceiling now says `the table takes up to 50`.

**DEAL greys over the ceiling too.** It greyed when you could not afford the bet
but stayed lit when the table was about to refuse it, which is the state that made
ALL IN look broken. It is still clickable, so the refusal can explain itself.

**Also:** ALL IN now says so when your balance is under the table *minimum*, rather
than filling in an amount that cannot be bet.

## The bet box has separators

Typing a bet gave you `100000`, which you had to count to read. It gives you
`100,000` now, formatted as you type rather than when you finish.

Everywhere else already read this way -- the balance in the corner, the stake
under each hand, the stats -- so the one number you actually enter was the one
you had to count.

Nothing to learn: type digits and the separators appear where they belong, and
the caret stays where you put it, so you can still click into the middle of a
number and edit it. Pasting `1,000,000` works -- it is stripped to digits and
reformatted, so it cannot end up disagreeing with the bet that gets sent. The
box takes digits only, as before; the separators are put in for you.

## Updating

Both halves change. The client has to be told what the limits are, so the server
now sends them — an old server with a new client leaves ALL IN behaving as it did
in 1.0.1 rather than breaking, but you want the pair.

Stop the server, extract over the top, start it again. Your statistics and BepInEx
settings are untouched; neither the folder layout nor the GUID has changed.
