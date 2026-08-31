# Blackjack 1.0.2

Two things about placing a bet: **ALL IN did not work**, and the bet box made you
count digits.

Nothing about the rules, the payouts or the money changed.

## ALL IN now means the most the table will take

It used to stake your whole balance, and the table does not take your whole
balance. It takes up to 500,000 roubles a hand, 5,000 dollars or euros, 50 GP
coins, 10 bitcoin, or 5 Lega medals.

So if you were carrying 200 GP coins, ALL IN gave you a wager of 200, and DEAL
was refused — the button did exactly what it said, and the table was always going
to say no. Nothing was lost and no money moved, but ALL IN was useless to anyone
holding more than the ceiling, which at 50 GP coins is most people.

Now it offers your balance or the ceiling, whichever is lower, and the
confirmation says which one you are getting:

- under the ceiling — *Bet everything?*
- over it — *Bet the table maximum?*, with what you are carrying shown
  underneath, so it is clear you are not staking the lot

The confirm button reads **BET MAXIMUM** rather than BET IT ALL when it is capping
you. If you have switched the table maximum off in the BepInEx menu, ALL IN means
all in again, exactly as it did before.

Two smaller things came out of the same bug:

- **The line beside the box names the right problem.** Betting more than the table
  takes and betting more than you own are different mistakes — one is fixed by
  betting less, the other by holding more — and both used to read *not enough*.
  Over the ceiling now says `the table takes up to 50`.
- **DEAL greys for the ceiling too.** It greyed when you could not afford a bet but
  stayed lit when the table was about to refuse one, which is the state that made
  ALL IN look broken. It is still clickable, so the refusal can explain itself.

ALL IN also tells you when your balance is under the table *minimum*, instead of
filling in an amount that cannot be bet.

## The bet box reads in thousands

Typing a bet gave you `100000`, which you had to count. It gives you `100,000`
now, formatted as you type rather than when you finish.

Every other figure on the table already read this way — the balance in the corner,
the stake under each hand, the stats — so the one number you actually enter was
the only one you had to count.

There is nothing to learn. Type digits and the separators appear where they
belong; the caret stays where you put it, so you can still click into the middle
of a number and edit it. Pasting `1,000,000` works — it is stripped to digits and
reformatted, so what you see cannot disagree with the bet that gets sent. The box
takes digits only, as it did; the separators are put in for you.

## Updating

Both halves change. The client cannot know the table's limits, so the server sends
them now — a 1.0.1 server with a 1.0.2 client leaves ALL IN behaving as it did
rather than breaking, but you want the pair.

Stop the server, extract over the top, start it again. Your statistics and your
BepInEx settings are untouched: neither the folder layout nor the GUID has moved.
