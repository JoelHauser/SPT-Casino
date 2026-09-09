# 1.2.6 — sorry, and a million roubles

**First: sorry for pulling the mod off the hub.**

SPT Casino was unlisted for a day with no warning and not much of an explanation. The slot machine shipped in a bad state, the problems turned out to run deeper than the first fix, and taking it down seemed better than leaving people to install something that did not work. If you grabbed it before that, you got a casino whose newest table was broken — and no way to know that was not your fault.

Sorry for the disruption, short as it was.

## A million roubles, on the house

**The first time you open the casino on 1.2.6, it hands you 1,000,000 roubles.** No condition, no quest, nothing to click through — it is in your stash before you have finished reading the note that comes with it.

Once per profile. Reinstalling the mod does not earn a second one; a second profile on the same install gets its own. If your stash is too full to take it, it arrives in the post like any other payout.

No table's odds changed to pay for it. The house is covering it.

## A massive shoutout to ABlindGuy

**This release exists because of ABlindGuy.**

Versions 1.2.2, 1.2.3, 1.2.4 and 1.2.5 were never published to anybody — they went straight to ABlindGuy, one after another, as debugging builds. They installed every one, played a slot machine that was still broken, and came back with what it actually did each time. When a fix worked, they said so. When it uncovered the next problem underneath, they said that too, and then took the next build anyway.

Most of the list below was found that way. Chasing a bug you cannot reproduce yourself is close to impossible, and ABlindGuy is the reason it stopped being guesswork. Thank you — genuinely.

## What was actually wrong

It was not one bug. It was five, and each one hid the next — which is why this took a few passes to get to the bottom of.

- **The reels were blank.** The slot machine draws its symbols from your own game's item icons, and the code that asks for them broke after an EFT update. Every reel fell back to a plain dark tile and stayed there.
- **One stubborn symbol blanked the whole machine.** A single icon that could not be drawn stopped the other eight from even being tried.
- **A spin could finish and never settle.** The SPIN button stuck on `...` and every press after that went nowhere. It read exactly like "you can spin it, but nothing happens."
- **Your stash never moved.** Bets left and winnings arrived on the server correctly, but the game was never told to go and look, so the rouble counter on screen sat there unchanged. Every table did this, not just slots — which is what made it look like the whole casino was playing for pretend money.
- **The machine greeted you with a win it could never pay.** The board sitting there before your first spin was the same winning board for every player, every launch. You would open it, see a paying line, press SPIN, watch it turn into a loss, and quite reasonably conclude something had just been taken off you.

And one more, found after all of the above were fixed: **the tables paid out and then drew nothing.** The reels stopped, the money moved, and no win lines, no banner and no result text ever appeared — and it left every table dead until you reopened it. That one turned out to be a single line of code looking for a part of the game by a name that changes when the game updates.

## Nothing was ever lost

Worth saying plainly, because "the stash never moved" reads like money going missing: **it never did.** Every bet and every payout was settled correctly on the server and written to disk the whole time. The only thing that was wrong was the number on your screen, and it corrected itself the moment anything reloaded your profile.

## Updating

Extract over your SPT folder — the one that holds `SPT_Runtime` — and let it overwrite. It is one folder on each side, `BepInEx/plugins/Casino` and `SPT_Runtime/user/mods/Casino`.

**Coming from any 1.x casino release, that is the whole job.** 1.2.6 replaces every file the older versions installed, so there is nothing left over to clean up.

**Coming from the old separate Blackjack, Poker or Roulette mods?** Delete those first. They were folded into this one mod back in 1.0, and extracting over them does not remove them — leave them in place and you get a duplicate tab per old mod and two copies of the same server routes. Remove these if you have them, from both sides:

```
BepInEx/plugins/Blackjack        SPT_Runtime/user/mods/Blackjack
BepInEx/plugins/Poker            SPT_Runtime/user/mods/Poker
BepInEx/plugins/Roulette         SPT_Runtime/user/mods/Roulette
```

Nothing is lost by deleting them. If the house owed you money on an interrupted hand, 1.2.6 finds that record in the old folder and pays it either way.

**If you downloaded 1.2.6 before this post went up, replace it.** The first archive under that version number had the last bug on the list above. To check which one you have, right-click `BepInEx/plugins/Casino/Casino.Client.dll` → Properties → Details: if the product version ends in `dca357a`, that is the old one.

Nothing else changed. Same odds, same payouts, same buy-ins — the slot machine still returns 92.51%.
