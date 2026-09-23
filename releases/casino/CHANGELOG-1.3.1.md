# SPT Casino 1.3.1

**2026-09-22.** A fix for a bug that could corrupt a profile badly enough to lock
**every** player on a server out, not just the one playing. Update if you run the
casino on a server other people use, and especially if anybody plays slots on AUTO.

No change to odds, payouts, or anything about how the tables play.

## What happened

A player left the slot machine on AUTO. A few minutes later their profile would no
longer load, and the SPT launcher could not list profiles for anybody on that server:

    Error handling request: /launcher/v2/profiles Object reference not set to an instance of an object.
       at SPTarkov.Server.Core.Extensions.ItemExtensions.GetItemStackSize(Item item)
       at SPTarkov.Server.Core.Helpers.Profile.ProfileHelper.GetAccountCurrency(SptProfile profile)

It had happened once before on the same server, to a different profile.

## Why

**Two requests from one player were changing their stash at the same time.**

After every spin the slot machine quietly tells the server to bring the game's stash
display up to date. That message is sent in the background, and on AUTO at high speed
the next spin can already be under way by the time it arrives.

The message does one other thing: it gives back any stake left behind by a spin that
never finished, which is what protects a player's money if the server crashes mid-spin.
But a spin that is *still running* looks exactly like one that crashed. So the message
refunded a live spin's stake -- a double payment -- and it did so while that spin was
paying into the same stash from another thread.

SPT's list of a player's items is not built to be written from two places at once.
Two writes landing together can leave an empty slot in it, and an empty slot is what
every one of the errors above is tripping over. It is saved to disk with the rest of
the profile, so it survives a restart, and SPT's own launcher then fails on it while
listing everybody's profiles.

Roulette, Horse Racing, Blackjack and Poker could all do the same in principle. Slots
on AUTO was just by far the likeliest way to hit it.

## The fix

- **One casino request at a time, per player.** Anything that touches a player's money
  now waits for the previous request from that same player to finish. Different
  players never wait on each other. The wait is a few milliseconds: the reels, the
  wheel and the race all animate on your own machine, not the server.
- **A damaged stash no longer breaks every balance.** The casino used to fail outright
  on a profile that already had an empty slot in its item list. It now skips over it.
- **A payout that fails partway through is posted, not lost.** If placing winnings in
  the stash threw an error, the rest of that payout used to be dropped. It now goes to
  your messages, the same way winnings too big for a full stash already did.

## Checked against

A stress run firing a pull and several balance checks at the same instant, 300 rounds
at a time, against a real SPT server:

| | 1.3.0 | 1.3.1 |
|---|---|---|
| Spins that finished | 281 of 300 | 300 of 300 |
| Requests that failed | 154 | 0 |
| Stakes wrongly refunded | 341 | 0 |
| Roubles gained that should not have been | 1,570,000 | 0 |

611 automated tests pass, including a new one that holds a spin mid-payment and checks
that a balance check arriving then waits for it rather than refunding it.

## If a profile on your server is already damaged

1.3.1 stops this from happening; it does not repair a profile it has already happened
to. Stop the server, back up `SPT_Runtime/user/profiles`, open the affected profile's
`.json`, and remove the `null` entry from `characters.pmc.Inventory.items`. The
launcher lists profiles again on the next start.
