# Poker 1.0.2

**One fix.** The tab now gets out of the way when the bar along the bottom of the menu
runs out of room. Nothing about the game, the bots, the payouts or the money changed,
and the server half is untouched.

## The bar was breaking every label in half

With enough mods installed, every tab on the bottom bar starts splitting its own name
across two lines — `HIDEOU/T`, `CHARACTE/R`, `POKE/R`. The bar sizes each tab from its
label and then squeezes them all when there is no room left, and past a certain number
of tabs the whole row becomes hard to read.

It is not any one mod's doing. It took a handful of them together to get there: this
one, Blackjack, Roulette, Raid Review's optional menu item and PIT Fireteam's slots. But
POKER is one of the tabs on that row, so it is one of the reasons the row is full.

**Now it gives way.** When anything on the bar is breaking a word across lines, the tab
drops its label and keeps its spade — about 112 units down to about 40 — and hands the
difference back to the tabs around it. When there is room again it takes its name back.
Nothing to configure; it checks once a second and follows the bar as other mods add and
remove tabs, and as you change resolution.

**This helps, and it is not a whole answer.** One tab going compact frees perhaps 70
units of a 1920-wide row. If your bar is still crowded, the largest single thing you can
do is turn off Raid Review's menu item — it is **off by default** and adds a tab when
switched on, in `BepInEx\config\ekky.raidreview.cfg`:

    Insert Menu Item = false

If you have Blackjack or Roulette installed, they do the same thing now and for the same
reason.

## About the version number

There is no 1.0.1. It was built and it did not work: the check it used asked whether any
label was narrower than it wanted to be, which sounds right and can never be true —
TextMeshPro does not overflow, it wraps, so a label is at its most broken at exactly the
moment it reports being the width it asked for. 1.0.2 asks the question you would ask
looking at the bar instead: is a single word split across two lines?

## Upgrading

Drop it in over 1.0.0 and restart the game. No config changes. Your chips, your stats
and any unsettled escrow are untouched — the server half is unchanged in this release.
