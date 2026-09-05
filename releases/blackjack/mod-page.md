A blackjack table in your menu. Six decks, a server-dealt shoe, and your own money on the felt.

There is no unlock, no hideout requirement and no quest. Install it, and a **BLACKJACK** tab appears on the bar along the bottom of the menu, beside HIDEOUT — on a profile five minutes old or one with a thousand raids behind it.

The bar is on every screen outside a raid, so the table opens from the hideout, the flea market or a trader screen without backing out of them first.

You can stake roubles, dollars, euros, GP coins, physical bitcoin or Lega medals. They come out of your inventory and a hand you lose is gone. **Your gear is never at stake** — weapons, armour and rigs cannot be bet.

![Blackjack](https://i.imgur.com/v05YUz3.png)

## The table {.tabset}

### Playing

Click the **BLACKJACK** tab at the bottom of the screen. Pick a currency, type an amount, press **DEAL**.

From there it is blackjack: **HIT**, **STAND**, **DOUBLE** and **SPLIT** appear when they are legal. The dealer's second card stays face down until the hand is over.

- Six decks, reshuffled at three quarters
- Dealer stands on soft 17
- Blackjack pays **3:2** in currency, even money in valuables
- Double after split, up to four hands

Escape leaves the table and puts you back on the screen you opened it from. You cannot walk away mid-hand — the stake is already down.

Naturals pay even money on bitcoin, GP and Lega for a simple reason: one bitcoin at 3:2 settles on half a coin, and half a bitcoin does not exist.

### Bet limits

Each currency has its own minimum and maximum.

| Currency | Minimum | Maximum |
|---|---|---|
| Roubles | 1,000 | 500,000 |
| Dollars | 10 | 5,000 |
| Euros | 10 | 5,000 |
| GP coins | 1 | 50 |
| Bitcoin | 1 | 10 |
| Lega medals | 1 | 5 |

> **The maximum is the point, not an annoyance.** The house edge on these rules is about half a percent, which is nothing across an evening. What stops you getting rich is being unable to cover a losing streak by doubling up — and a ceiling five hundred times the minimum caps that at nine doubles.

**You can turn it off.** Press F12 in game, find **Blackjack → Table**, and untick *Enforce maximum bet*. Then the only limit is what you are carrying. The minimum always applies.

### Your record

**STATS** clears the table and lays out your lifetime figures: rounds and hands played, wins, losses and pushes, blackjacks, busts, current and best streak, and how much you have staked and won back in every currency you have played.

It is only there between hands.

The record is kept in the mod's own folder, **not in your profile**. Uninstalling costs you nothing but the statistics, and your profile is never modified.

### Installing

Stop the server, then extract the archive into your SPT folder — the one containing `SPT_Runtime`. Two files go in, and both are needed: the server deals and holds the money, the client draws the table.

```
SPT_Runtime\user\mods\Blackjack\
BepInEx\plugins\Blackjack\
```

Start the server. **Blackjack** should appear in the mod list, and a **BLACKJACK** tab on the bar along the bottom of the game's menu.

If the tab is missing, check the folder went to `SPT_Runtime\user\mods\` and not to a `user\mods\` beside it — that is the one mistake that leaves no trace in the log.

### Compatibility

**The mod does not modify the main menu at all.** Earlier versions added a BLACKJACK button to the menu's own list of entries; that is gone, and the tab replaced it.

The tab is not drawn from scratch — it is a copy of one of the game's own tabs, taken from the bar it is joining. So it looks like whatever it sits next to: a mod that restyles the bar restyles ours with it, because ours is a copy of the result. It carries a diamond rather than a borrowed icon, dims and stops answering exactly when the rest of the row does, and the row reflows if another mod adds a tab of its own.

That includes my own [Poker](https://github.com/JoelHauser/Poker-) mod, which adds a tab the same way. The two sit side by side, one diamond and one spade, and neither has to know about the other.

{.endtabset}

## Notes

The server deals every card, scores every hand and moves every rouble. The game running on your PC never receives the dealer's hidden card until the hand ends, so it could not show you the outcome early even if it wanted to.

The card faces are Chris Aguilar's *Vectorized Playing Cards*. They sit as ordinary PNGs beside the plugin, one per card, alongside the table photograph — swap either for your own, or delete them and the mod draws its own instead.

Requires **SPT 4.1.3** or later 4.1.x.
