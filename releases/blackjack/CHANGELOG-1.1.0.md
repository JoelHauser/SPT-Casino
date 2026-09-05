# Blackjack 1.1.0

**The table is now reachable from anywhere in the menu**, through a BLACKJACK tab on
the bar along the bottom — beside HIDEOUT, on every screen out of raid.

Nothing about the rules, the payouts or the money changed.

## A tab, not just a menu button

The BLACKJACK button on the main menu only exists on the main menu. Sitting in the
hideout, or halfway through a flea-market search, meant backing out to reach the
table.

That bar along the bottom is on every out-of-raid screen, so a tab there is a way in
from all of them. The main-menu button stays — both do the same thing, and either can
be turned off.

The tab is a copy of one of the game's own, so it looks like what it sits next to
whatever else is installed: a mod that restyles the bar restyles ours with it, because
ours is a copy of the result. It carries a diamond rather than a borrowed icon, dims
and stops answering exactly when the rest of the row does, and reflows if another mod
adds a tab of its own — the row lays itself out, and our tab is just one more thing in
it.

## The table no longer follows you into a raid

The table's canvas outlives a scene change, and nothing was taking it down. On your
own that is nearly unreachable — the table covers the screen, so PLAY is not clickable
while it is open. In co-op it is not: the raid is started by the host, and a player
can be pulled out of the lobby with the table still up.

Now a raid starting closes it. A stake in play at that moment is already covered — an
unsettled stake is refunded the next time you sit down.

## New settings (F12)

| Setting | Default | What it does |
| --- | --- | --- |
| Show the task-bar tab | on | The tab on the bottom bar. |
| Put the tab on the right | off | Moves it in with CHARACTER and the rest, instead of beside HIDEOUT. |

## Also

- The main-menu button now finds what it patches by name at load rather than naming it
  at compile time. It could not be compiled at all against some EFT builds, where the
  method it hooks is private; and if it now fails to apply, it says so in the log
  rather than taking the rest of the plugin down with it.

## Known

The tab had not been seen running when this was packed. Every part of the game it
touches was checked against a real install, and it builds against one, but that is not
the same as a screenshot. If the tab is missing, misplaced or dead,
`BepInEx/LogOutput.log` records which tabs it found, which one it copied, where it put
it and what it switched off — that is the useful thing to report.
