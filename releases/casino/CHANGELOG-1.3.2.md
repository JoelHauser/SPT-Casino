# SPT Casino 1.3.2

**2026-09-24.** A fix for the roulette table disappearing after a raid.

No change to odds, payouts, or anything about how the tables play.

## What happened

After a raid or two, opening roulette showed only the title, the chip tray and the
amount on the cloth. There was no wheel, no cloth and no buttons, so there was no way
to bet or spin. Relaunching the game brought it back until the next raid or two. The
BepInEx log showed:

    [Error  :SPT Casino] [Roulette] could not open the table: System.NullReferenceException
      at UnityEngine.Transform.get_childCount(UnityEngine.Transform)
      at Roulette.Client.ClothView.ShowBets (...)

## Why

Going into a raid loads a new scene, and Unity throws away anything that isn't marked
to survive it. The other tables' screens were marked, or rebuild everything when they
reopen. Roulette's was neither.

Its screen was thrown away, and the next open built a fresh one. But roulette only
draws the wheel and cloth when they change, and it still remembered drawing them. So
it skipped drawing them into the new screen and went to put your chips on a cloth that
no longer existed. That error stopped the rest of the table (the buttons and the
balance line) from being drawn too.

## What's fixed

- The roulette screen now survives a raid, as the other tables' screens do. It still
  closes when a raid starts.
- If it is ever rebuilt for any other reason, the wheel and cloth are drawn again
  rather than assumed to be there.

## Your chips were safe

Only the display broke. Bets placed before the table vanished are still on the server
and will be back on the cloth when you open the table in 1.3.2.

## Installing

Same as before: extract `SPT_CasinoV1.3.2.zip` into your SPT folder and overwrite. No
config changes.
