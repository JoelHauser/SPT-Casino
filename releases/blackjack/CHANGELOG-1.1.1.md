# Blackjack 1.1.1

**A tidying-up release.** Nothing about the rules, the payouts or the money changed —
this is about the tab, the escape key, and getting BLACKJACK out of the main menu's own
list of buttons.

## The tab is the only way in now

1.1.0 added the tab and kept the main-menu button, on the grounds that both did the
same thing and either could be turned off. Seen side by side, that was the wrong call.

The button only ever existed on the main menu, where the tab is on every out-of-raid
screen. What it added was an entry to a list of five that reads ESCAPE FROM TARKOV,
CHARACTER, TRADING, HIDEOUT, EXIT — and with Poker installed alongside, that list grew
by 40% and the two card games were the loudest thing on it. The bar along the bottom is
where the game already keeps the places you can go.

So the button is gone, and with it the whole patch that grafted it onto the menu. That
is worth saying plainly: **the mod no longer modifies the main menu at all.** It was the
most fragile thing in the plugin — it broke on an EFT build change, and with two mods
adding buttons the same way the pair used to walk down the screen a row at a time.

## The tab is the size of the tabs beside it

It was about twice as wide, and the diamond distorted when the pointer went over it —
enough to look like the icon had split in two.

Both were the same cause. A Unity `Image` reports its sprite's own size as the size it
wants to be, and the layout believes it: the drawn diamond asked for 160 units where the
hideout's icon asked for 25. Once the icon was pinned to the space it replaced, the tab
came out narrower than HIDEOUT — as it should be — and the pip stopped stretching.

If you have Poker installed too, both tabs were wrong in exactly the same way and both
are fixed.

## Escape closes the table, and only the table

Pressing escape used to close the table **and** back you out of whatever was behind it.
From the stash or the flea market that dropped you on the main menu; from the hideout it
looked like the mod was throwing you out of the hideout.

The table is a window the game does not know about, so watching for the key was only
ever racing it — the screen underneath took the same keypress on the same frame. It is
taken out of the frame's input properly now, so nothing behind the table sees it.

## Smaller things

- The table closes the moment a raid begins loading, checked every frame rather than
  once a second. It could previously stay up for up to a second into a scene change,
  which matters most in co-op where the host decides when the raid starts.
- The tab freezes the animation it inherits from the tab it was cloned from, so it
  settles into the resting look of an unselected tab instead of whatever state it was
  copied mid-way through.

## Upgrading

Drop it in over 1.1.0 and restart the game. There are no config changes to make; the
main-menu button's setting is gone along with the button. Your stats and any unsettled
escrow are untouched — the server half is unchanged in this release.
