# Blackjack 1.1.2

**One fix.** The tab on the bottom bar now greys out while you are loading into a raid,
the way every other tab on that bar already did. Nothing about the rules, the payouts or
the money changed, and the server half is untouched.

## The tab stayed lit on the deployment screen

Queue for a raid and the bar dims: MAIN MENU, HIDEOUT, TRADERS, FLEA MARKET and the rest
all go grey, because none of them will take you anywhere from there. BLACKJACK stayed at
full brightness beside them, looking like the one thing still open for business.

It was not. The table has always closed at the first sign of a raid and a click on the
tab was already being refused — so this was never a way to lose money or to end up with
a card table on top of your raid. It was the tab telling you something that was not
true, at exactly the moment you are watching a progress bar and looking for something to
do.

Now it dims with its neighbours, to the same shade, and the hover highlight stops
following your cursor across it.

## What was actually wrong

Worth writing down, because the tab had looked correct on every other screen.

The tab is a copy of one of the game's own, and it takes its cue from a real tab so that
it locks and unlocks at the same moments as the rest of the row without having to know
why. It was watching the wrong half of that tab: the toggle's `interactable` flag, which
is a value set once in the prefab and never touched again.

The game dims a tab through the `CanvasGroup` on the tab's wrapper instead — that is
where the alpha and the interactable flag it actually changes both live. So the tab it
was copying went grey and the flag being watched never moved, and the copy stayed bright
through the whole loading screen.

It reads that `CanvasGroup` now, and it also dims on its own account the moment a raid
starts loading, without waiting to be told by the bar.

## Upgrading

Drop it in over 1.1.1 or 1.1.0 and restart the game. No config changes. Your stats and
any unsettled escrow are untouched — the server half is identical apart from its version
number.

If you are coming from 1.1.0, everything in
[the 1.1.1 notes](CHANGELOG-1.1.1.md) applies too: the main-menu button is gone, the tab
is the right width, and escape closes the table without backing you out of the screen
behind it.
