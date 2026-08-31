# Blackjack 1.0.3

The bet box has separators.

## What changed

Typing a bet gave you `100000`, which you had to count to read. It gives you
`100,000` now, formatted as you type rather than when you finish.

Everywhere else already read this way — the balance in the corner, the stake under
each hand, the stats — so the one number you actually enter was the one you had to
count.

## Typing in it

Nothing to learn. Type digits and the separators appear where they belong; the
caret stays where you put it, so you can still click into the middle of a number
and edit it. Pasting `1,000,000` works — it is stripped to digits and reformatted,
so it cannot end up disagreeing with the bet that gets sent.

The box takes digits only, as before. The separators are put in for you, never
typed.

## Updating

Client only — the server is unchanged apart from its version. Stop the server,
extract over the top, start it again. Statistics and BepInEx settings are
untouched.
