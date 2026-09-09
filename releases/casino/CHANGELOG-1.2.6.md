# SPT Casino 1.2.6

The slot machine works again. 1.2.0 and 1.2.1 shipped it badly broken, in four separate
ways that each hid the next one, and all four are fixed here. **One of them affected every
table, not just Slots.**

## The reels were blank

1.2.0 restored item-icon rendering after an EFT update renamed the game's item factory out
from under it, and did it by pinning the factory's **metadata token** -- a number that
addresses the method directly, so it does not care that the class it lives on no longer has
a name.

That number is only correct for the exact copy of `Assembly-CSharp.dll` it was read out of.
It verified perfectly on the machine it came from, twice, using two different tools -- and
on a player's install it came back as something that was not even a method, so the cast
threw before a single icon could be drawn. Every reel fell back to its plain dark tile and
stayed there.

The factory is now found by describing it rather than numbering it: the assembly is searched
for a method named `CreateItem` that takes an id, a template id and an optional diff and
returns an `Item`. The method's own name has survived every rename this mod has hit; it is
the class around it that keeps losing one. Checked against a real install before shipping --
exactly one method out of 15,136 types matches, and it is the right one.

**The lesson is in `CLAUDE.md` now:** a number read off one machine's copy of a game file was
never going to survive the fleet, and verifying it on that same machine cannot detect that.

## A stalled symbol blanked the whole machine

The renderer fetches all nine symbols in one pass, and a single symbol that could not be drawn
yet stopped the pass dead rather than letting the other eight try. The same symbol failed first
every time, so three panel opens was enough to abandon rendering for the rest of the session.
Each symbol gets its own attempts now.

## A spin could finish and never settle

The reels stop on the board the server has already settled. Everything that happens *because*
of that board -- paying the win into the panel, asking the game to collect the money, replacing
the "..." under the reels -- runs in one step afterwards, and that step was only reached when
the spin ended cleanly.

Two things could stop it. A reel animation that died reported nothing back, so the machine
waited for it for ever: the SPIN button stayed on "..." and every later press was dropped
before it reached the server, which reads exactly as "you can spin it but no money moves".
And when the spin did end, settling sat outside the block that guarantees cleanup, so anything
going wrong skipped it while still releasing the machine to take the next click -- leaving a
board that had plainly won, a SPIN button ready to go, and the mid-spin "..." still underneath.

Each reel now reports itself finished even when it fails, the wait is bounded (past the longest
a spin can physically take, the reels are landed on the server's result and play continues), and
settling happens whatever the spin did. Putting the button back no longer sits behind the code
that draws win lines, so a result that cannot be drawn can no longer leave the machine unusable.

## The machine greeted everyone with a win it could never pay

The board shown before the first spin was rolled from the reel number and nothing else, so it
was not merely random -- it was the *same* board for every player, on every launch, for ever.

It was a winning one: Gold Rooster across the first four reels and Moonshine across the first
four as well, which at 243 ways reads as six times the stake. Every player opened the machine,
saw a paying board, pressed SPIN, watched it become an ordinary loss, and reasonably concluded
something had taken a win off them. Nothing had -- that board was never a result and the server
had never seen it -- but there is no way to tell that from the outside.

The resting board is dealt rather than rolled now: three symbols per reel walking the paytable
in order, so the first two reels share no symbol and nothing can run far enough to read as a
win. It also puts every symbol on the glass at once, which is a better greeting than a random
handful.

## Money moved and the game was never told -- on every table

A bet leaves your stash and a win is paid back into it on the server, and that has always
worked. But currency moved that way lands in the profile without the running game noticing, so
each table sends a do-nothing item event afterwards purely to make the client collect the
changes. That is what puts the new rouble count on screen.

The event was only sent if `ItemUiContext` existed, and it silently did nothing if it did not.
`ItemUiContext` is built by the inventory screens, not by the menu -- so walking from the task
bar straight into the casino, the normal way in, meant nothing was ever asked to collect
anything. The stash never moved and the tables looked like they were playing for free. Nothing
said so, either: the check returned quietly on purpose, which is precisely what kept it hidden.

The session is now taken from the running application when the menu context has not been built,
and if there is genuinely none it says so once in the log instead of never. Blackjack, Poker,
Roulette and Slots share this code and all four get the fix.

**No money was ever lost.** It moved correctly the whole time; only the number on screen was
stale, and it came right as soon as anything reloaded the profile.

## Also

Saving a rendered icon borrows the game's global render target and only put it back on the
success path, so a failed read left the whole game drawing into a buffer that had already been
returned to the pool. It is restored in a `finally` now. Nothing had ever reached that code on a
player's machine before, because no icon had ever rendered to be saved.

---

No change to any table's odds, payouts, or buy-in. The machine still returns 92.51%, computed
rather than sampled.
