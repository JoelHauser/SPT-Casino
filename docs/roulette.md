# Roulette -- working notes for Claude

A roulette table for the SPT hideout. Server mod in C# (.NET 10) against SPT 4.1.3,
with a BepInEx client plugin. **The wheel actually spins**, and that is a stated
requirement rather than polish -- see "The wheel has to spin".

Third in a family. **Blackjack** (`src/Blackjack.*`, shipped at 1.0.2) and **Poker**
(`src/Poker.*`, shipped at 1.0.0) between them already solved the money, the two
transports, the menu entrance, escape handling and the card and chip art. When
something below says "port", there is working, shipped code in one of those repos to
copy rather than rediscover.

This file is loaded automatically at the start of every session. Keep it to things a
fresh session would otherwise rediscover the hard way -- not a chronological diary.
**Update "Current state" when you finish a piece of work.** Poker's notes went four
commits claiming its server did not exist; a fresh session reads that section first
and believes it.

---

## The single most important fact

**SPT 4.x server mods are C#, not TypeScript.** The `mod.ts` / `package.json` /
tsyringe world ended at 3.x, and most guides online still describe it. Server mods
are .NET 10 class libraries referencing `SPTarkov.Server.Core`, with an
`IModMetadata` record in place of `package.json`.

`SptVersion` in that record is a **hard load gate**. It is `~4.1.3` (>=4.1.3
<4.2.0). A mod outside the range loads nothing and logs nothing. Silence at startup
means the gate, not a bug in the game code.

## What roulette is, and why it is the easy one of the three

**House-banked, single player, no opponents.** There is no pot, no second seat and no
bot to write. Poker's bots were the product and its hardest work; here there is
nothing to model but a wheel.

**The edge is exact arithmetic and always the same.** One pocket in thirty-seven pays
nobody, so every bet on a European wheel gives the house 2.70% -- computed, not
measured. That is worth stating plainly because Poker could not have it: there the
rate money appeared was the skill gap between the player and the bots, which made the
mod a faucet whose flow rate nobody could state. Here it is 1/37 and that is the end
of it.

**So do not measure the edge by simulation.** Poker's notes work out that a tenth of a
point needs about nine million hands. `PayoutTests` computes the exact edge over all
37 pockets in microseconds. Anything that wants proving about payouts is arithmetic.

## Layout

| Project | Owns |
| --- | --- |
| `src/Roulette.Game` | Wheel, cloth, bets, payouts. No SPT reference, no I/O, no clock. |
| `tests/Roulette.Game.Tests` | 79 tests. |
| `src/Roulette.Server` | The mod: routes, DI, logging, the money. |
| `src/Roulette.Client` | The BepInEx half: entrance, table panel, the spinning wheel. `net472`. |
| `tools/Roulette.Console` | Terminal table. No SPT needed. |
| `scripts/roulette/` | `pack-mod.ps1`, `smoke.ps1`, mirroring Poker's. |

The engine knows nothing about currency -- it takes an `int` and returns an `int`.
Everything that maps a wallet to an item template belongs in `Wallets.cs` and
`Bank.cs` when they are ported. Keep it that way; it is what makes the rules testable.

## The wheel has to spin

The headline requirement, and the one thing neither sibling has done.

**The server decides, the client acts it out.** `RouletteTable.Spin` picks the pocket
and settles every bet before the client has drawn a frame. The animation is theatre
over a decided result, which is the only honest way round: a client that decided
where the ball stopped would be a client that decides how much money it wins.

**That is why `SpinResult` carries `Position` and not only the number.** The wheel is
not in numerical order -- 26 sits at position 36 on a European wheel -- so the client
cannot work out where to stop from the result alone. Handing it over is one field and
saves the client re-implementing the wheel order, which is exactly the kind of
duplicated table that drifts.

**Land it by construction, not by deceleration.** Compute the final angle from
`Position` first, add whole turns, and ease to it. Spinning at a decaying rate and
hoping it stops in the right place gives a wheel that lands a pocket out every so
often -- which reads as a payout bug, because the number under the marker will not be
the number that paid.

The trick that makes it exact is describing the ball **relative to the wheel**:

    ballAngle = wheelAngle + pocketAngle(Position) + drift

where `drift` starts at several turns' worth and eases to exactly zero. Early on the
drift dominates and the ball races the rim against the wheel's direction; at the end
it is zero, so the ball sits in its pocket and rides round with the wheel -- with no
special case to put it there and nothing to round off.

**The skipping is decoration with one hard rule: it is exactly zero at the end.** A
damped oscillation driven to nothing by the same `t` that ends the spin, so however
lively the bounce looks it cannot move where the ball finishes. Anything that added a
random nudge near the end would be a ball that lands a pocket out at random, which is
the whole failure this arithmetic exists to avoid.

**Draw the pockets from `Wheel.Pockets`, not from the image. This is not
hypothetical -- the supplied wheel failed the check.**

`assets/wheel.png` is AI-generated art. It is handsome, its green zero is at the top,
and clockwise from there it genuinely reads 32, 15, 19, 4, 21, 2, 25, 17, 34, 6, 27 --
correct European order. **But it does not have 37 pockets.** Measured off the image
three ways:

- the colour sequence agrees with the engine for seven pockets and then slips by one,
  and keeps slipping -- the signature of a wrong pitch rather than a wrong order;
- counting the gold frets directly gives 33 or 34;
- the median fret spacing is **10.65 degrees**, and 360 / 10.65 = 33.8. A real wheel
  is 9.73.

Landing the ball at the mathematically correct angle on that ring would stop it on a
number that did not win. **That reads as a payout bug, not an art bug**, which is why
it is worth this much prose.

So the client uses the image for the bowl, the brass studs, the worn metal and the
centre turret, and paints the coloured band over with a ring generated from the
server's pocket list, numbers on top. `WheelView` carries the measured geometry:

| Constant | Value | What it is |
| --- | --- | --- |
| `ClothInner` | 0.395 | where the coloured band starts, as a fraction of the image radius |
| `ClothOuter` | 0.715 | where it ends; outside is the wooden bowl |
| `PocketRadius` | 0.545 | the numbers, and where the ball rests |
| `TrackRadius` | 0.685 | the rim the ball runs on before it drops |
| `BallSize` | 0.030 | ball diameter over wheel diameter -- a real ball is ~20mm on ~800mm |

All of those were sampled from the picture rather than guessed, the same discipline
Poker needed for its table photograph. **Re-measure them if the art changes**, and
re-run the pocket count first: the script that found this is in the session history
and is a dozen lines of PIL.

**Settle the money when the server says, not when the animation ends.** A player who
closes the panel mid-spin, or alt-tabs, or takes a raid invite, has already had the
result. Tying the credit to an animation completing is how money goes missing.

## The money, inherited whole from Poker

Read `docs/poker.md` under "The money" before writing any of it. The short
version of what applies here:

- **One chip is one rouble.** Roubles are the only wallet that works at these stakes.
  Dollars and euros are capped far too low, and GP coins, bitcoin and Lega medals were
  removed from Poker deliberately -- bitcoin and Lega have a `StackMaxSize` of 1, so a
  payout arrives as a pile of grid cells rather than as money.
- **`Bank`, `ProfileGateway`, `Escrow`, `Wallets` and `Abstractions` port nearly
  as-is.** They are currency plumbing and carry no game rules.
- **Two transports, one service.** A static route for curl, an item event for the real
  client, sharing a service -- a second copy of the flow is a second set of money bugs.
- **`ProfileSync` after every move through a static route.** A stale stash is not
  cosmetic: the client goes on believing in stacks the server has deleted, and the next
  one the player drags fails with "cannot be found".
- **Credit before releasing escrow.** A crash between them leaves the money recorded
  and refundable; the other order pays nothing and forgets it was owed.
- **Write `MoneyInvariantTests` before the settlement it checks.** That instruction has
  been carried by both siblings since before either had a server.

### Where roulette differs from Poker, and it is simpler

Poker had to hold a **live stack** in escrow, updated every hand, because the player
bought in once and then held a number that moved. Roulette settles per spin, which is
Blackjack's model and much easier: the total on the cloth is debited when the wheel
turns, the returns are credited when it stops, and escrow holds the staked total for
exactly that long.

**Take the money when the wheel turns, not when the chip is placed.** Bets sit on the
cloth and can be cleared, and a debit per chip means a refund per chip and a
reconciliation nobody asked for. One debit, one credit, one spin.

## What is done

- `Wheel` -- both wheels as data, in published physical order, with colours derived
  from one list of reds. `PositionOf` is what the client spins to.
- `Pocket` -- the double zero is pocket 37, not 0. Folding them together makes a
  straight-up on zero come in twice as often as it should.
- `Layout` -- every betting spot on the cloth enumerated. **Splits are indices into
  this list**, because "the split on 1" is ambiguous between 1-2 and 1-4 and paying
  the wrong one is a silent money bug. Streets, corners and six lines are named by
  their lowest number, which on that grid does fix the shape.
- `Bet` / `Payouts` -- what each bet covers, computed from the cloth rather than
  stored, and what it pays.
- `RouletteRules` -- the caps, and `MaxFor` scaling them by what a bet pays.
- `RouletteTable` -- placing, clearing, spinning, settling. `SettleOn` is the test
  seam, the same idea as a stacked deck.
- `GameLog` / `EnumJson` -- ported from Poker.

### The two payout facts worth keeping

**Odds times coverage is 36 for every bet on the cloth.** 35 to 1 on one number, 17
on two, 11 on three, 8 on four, 5 on six, 2 on twelve, 1 on eighteen -- every one
multiplies out to 36. That single identity catches both halves of the classic mistake,
wrong odds or a bet covering the wrong count of numbers, and it is why the edge comes
from the zero rather than from the paytable.

**The American top line is the exception and is meant to be.** Five numbers at 6 to 1
returns 35, not 36, which is why it is the worst bet on either wheel at 7.89%. Do not
"fix" it to 7 to 1.

**A winner gets its stake back on top of the winnings.** `Returned` is
`stake * (odds + 1)`. Paying the winnings alone quietly keeps every stake the house
ever took, and is worth about six percent of a straight-up payout -- large enough to
matter and small enough to look like rounding.

## Things that will bite you

Carried from Poker and Blackjack. Each cost real time there and all of them still
apply.

- **The .NET 10 SDK on Joel's box is a user-local install** at `%USERPROFILE%\.dotnet`
  and is **not on PATH**. A bare `dotnet` finds SDK 8 and fails with `NETSDK1045`.
  Build with `& "$env:USERPROFILE\.dotnet\dotnet.exe"`.
- **The server lives in `SPT_Runtime\`, not at the install root.** Joining `user/mods`
  onto the install path creates a folder nothing reads, and a mod that never loads
  looks exactly like a mod that loaded and did nothing.
- **`new ItemEventRouterResponse()` is not a usable response.** Get one from
  `EventOutputHolder.GetOutput(sessionId)` or it throws after the items are gone.
- **A mod can change any item's stack limit.** Read `StackMaxSize` live and clamp it
  to at least 1; a limit of zero hangs a server thread.
- **`AddItemToStash` can decline an item without throwing.** Compare the balance
  either side of every move and post the shortfall as mail.
- **`[JsonConverter]` on the enum type is not enough.** SPT registers
  `EftEnumConverterFactory` into `options.Converters`, which outranks a type
  attribute. Put the attributes on the **properties** of the view record from the
  start.
- **Request bodies are matched case-sensitively.** Send PascalCase or every field
  silently takes its default.
- **It serves HTTPS with a self-signed certificate, and zlib-frames every body.**
  `requestcompressed: 0` and `responsecompressed: 0` opt out. The session id is a
  `PHPSESSID` cookie and PowerShell drops it silently from `-Headers`; use a
  `WebRequestSession`.
- **Bash heredocs mangle backslashes** and a long one containing quotes fails to parse
  outright. Use the Write tool for C# and for any large file.
- **Zip entries must be written with forward slashes.** Both `Compress-Archive` and
  `ZipFile::CreateFromDirectory` write backslashes on Windows, which extract on Linux
  as one file with a very long name. Open the archive and add each entry yourself.

## Conventions

- **Comments explain why, not what** -- ideally naming the failure the code prevents.
  The codebase is deliberately heavy on rationale.
- Prose in comments uses `--`, not em dashes.
- Tests are named as the rule they pin, not the method they call.
- Every tunable a player might argue about lives in `RouletteRules`.
- **Everything logs**, through `IGameLog` in the engine, off by default, and never by
  building a string that is then thrown away.

## Verifying

    & "$env:USERPROFILE\.dotnet\dotnet.exe" test

**Distrust a suite that passes first time.** Mutation-check anything that pays money:
introduce the fault deliberately and check the suite catches it. Both siblings record
which faults they injected and how many tests caught each, and that record is what
makes their evaluators and pot builders trustworthy. Roulette has not had this done
yet -- see "Open items".

The one that already earned its keep: `ColoursAlternateAroundTheWheel` was written to
assert plain alternation and **failed against a correct American wheel**. Each zero
there is deliberately flanked by a matching pair -- 0 between two blacks, 00 between
two reds. The test was wrong, not the data, and it now pins that property instead.

---

## Current state

**Update this section as work completes.**

- **This mod lives in `SPT-Casino` now**, merged with Blackjack and Poker on
  2026-09-04 and pushed to `github.com/JoelHauser/SPT-Casino`. The old
  `github.com/JoelHauser/Roulette` repo and the `H:\SPTMods\Roulette` folder are the
  pre-merge record and are not where work happens. See the root `CLAUDE.md`.
- **`Roulette.Game` green at 79 tests** in about 50ms. Both wheels, the full cloth,
  every bet, exact payouts, settlement, and lifting chips back off.
- **The server is built, installed and verified on the real 4.1.3 install.** The gate
  passes, the banner prints, seven static routes register, the session resolves, the
  wallets read, and spins settle correctly against the published wheel positions.
- **The client is built and deployed**, with the task-bar tab as its entrance. It
  references `spt-common 4.1.3.0`, so `PluginValidator` accepts it.
- **The wheel spins, the ball lands, and it has been watched.** Several rounds of
  fixes came out of watching it -- see "The wheel has to spin" for the strobe, the
  freeze at rest and the variance, each of which looked like something else first.
- **The cloth is drawn and playable.** All 36 numbers and the zero, every split,
  street, corner and six line as targets on the joins, the columns, the dozens and the
  outside bets -- built from the server's own enumeration so it cannot offer a bet the
  server would refuse. A chip tray picks the denomination; left click puts a chip on, a
  right click takes one back off.
- **The table plays for real roubles.** The stake leaves the stash when the wheel
  turns and the return is paid back when it stops; chips on the cloth cost nothing
  until you spin. Escrow records what was taken before it is taken, so a server killed
  mid-spin gives it back on next contact. 13 money tests, written before the
  settlement, and mutation-checked against eight deliberate faults -- all eight
  caught. **Not yet played against a real profile.**

### Opening the table costs 197ms, and used to cost 1489

Measured, not estimated -- `RoulettePanel.Open` times itself on the first open and
logs the breakdown, which is worth keeping because every candidate cause here looks
the same from the outside.

The first measurement was `1489ms -- panel 156ms, layout 2ms, wheel 1274ms, cloth
26ms, chips 62ms`. The wheel was 86% of it: two passes over a 1024-square texture at
four samples a pixel, about 640ms each.

**None of that is Unity work.** `WheelView.Paint` is split into `Compute`, which is
pure arithmetic filling a `Color32[]` and runs on a background thread, and `Upload`,
which makes the `Texture2D` and is the cheap end. `WheelView.Warm` is started from
`TaskBarTab` the moment the tab appears, which is many seconds before the earliest
possible click. It is now `197ms -- panel 124ms, wheel 41ms`, and the 41 is the two
uploads.

Three things here that were each wrong once:

- **`Warmed` is destructive.** It hands the arrays over and forgets them -- two
  megabytes that are dead once they are on the GPU. `Build` called it twice, once for
  the head and once for the bowl, so the first call took the warm pair and the second
  repainted both from scratch. The cost was paid in full *and* the background work was
  thrown away. **Call it once, into a local.**
- Nothing inside `Compute` may touch a Unity object. `Mathf` is fine, since every
  function used from it is a static wrapper over `System.Math`. `Texture2D`, `Sprite`
  and anything with a `GameObject` behind it are not.
- A warm-up in flight is joined, never raced. Repainting on the main thread while a
  thread is already doing it is the slow path twice over.

**The quality is untouched.** The wheel is not cheaper to draw; it is drawn somewhere
the player is not waiting. Do not trade samples for speed here -- there is nothing to
gain and the jaggedness took three rounds to remove.

What is left is `panel 124ms`, of which the six chip PNGs are 61ms. Warmable the same
way if it ever matters; at 197ms it does not.

### Not yet seen on screen

The most recent change -- right-click to lift a chip, the number hidden under the chip
covering it, and the balance line and chip tray moved clear of the wooden rail -- is
built and deployed to `H:\SPT4.1.X` but has **not been looked at**. Deployed
2026-09-04. What wants checking:

- A right click on a stacked spot takes back one chip's worth rather than the pile.
- A right click does not also place a chip, and does not open a game context menu.
- The number under a chip is gone, and comes back when the chip does.
- Nothing overlaps the wooden frame at the top or the bottom of the cloth.

### Open items

**Next**

- **Play it against a real profile.** The money path is written and tested but has
  never moved a rouble on a running server. Watch: the stake leaves on spin, the
  return arrives, the stash on screen updates without a reload, and a straight-up win
  splits across stacks correctly -- 1,000,000 on a number pays 36,000,000, which is
  thirty-six maximum rouble stacks.
- **Mutation-check the engine's payouts.** The settlement is checked; `Payouts` and
  `Bet.Covers` are not, and they are all money.
- **Play a straight-up win big enough to split.** A 1,000,000 chip on a number pays
  36,000,000, and on a stock server that is thirty-six maximum rouble stacks. This
  server's limit is raised to 20,000,000 by an item mod, so the biggest win seen so
  far arrived in one stack and the splitting loop has still never run for real.
- **The console harness**, so a spin can be run thousands of times without Unity.
  `tools/Roulette.Console` does not exist; Poker's is the model.

### The payouts are mutation-checked, and counting is not checking

Twenty-one deliberate faults in `Payouts` and `Bet.Covers`: every price in the table
moved one step, a winner paid without its stake back, a loser paid with it, and each
branch of `Covers` given a plausible off-by-one. Nineteen were caught at once.

**The two that survived were the same mistake, and it is worth naming.** A corner
covering 1, 2, 3, 5 instead of 1, 2, 4, 5 -- a column of three and a stray -- passed
everything, and so did swapping odd with even so the table paid odd bets on even
results. Both survived because the tests counted the covered numbers instead of
checking them: four distinct numbers in range is still four, eighteen is still
eighteen, and `odds x coverage = 36` holds either way. The identity is a good test and
it cannot see this class of fault at all.

They are pinned now by properties derived from somewhere other than the code under
test. A corner has to be two adjacent printed columns by two adjacent printed rows
with all four cells filled, which comes from the grid. Odd numbers are `n % 2 == 1`.
Red is whatever `Wheel.ColourOf` says. **When adding a bet, assert which numbers it
covers, not how many.**

### The play stays on static routes, and that is a decision

Poker and Blackjack put their whole game on EFT's item-event endpoint
(`/client/game/profile/items/moving`), because the reply carries `ProfileChanges` the
client applies to its own inventory -- one round trip moves the money and updates the
stash. This mod's notes carried "add the item-event transport" as a to-do for months,
inherited from Poker, where it is right.

**It is wrong here, and the reason is the animation.** The wheel runs for six to nine
seconds over a result the server settled before it replied. Send the spin as an item
event and the game applies the profile change the instant the reply lands, so the
rouble counter on the screen behind the table moves while the ball is still rolling
and the player knows the answer nine seconds early. That is not hypothetical -- it
happened, through `ProfileSync` firing too early, and `RoulettePanel.ResyncStash`
exists to fix it.

Static routes are what let the client decide *when* to ask, and it asks when the ball
lands. The one item event this mod registers is `RouletteSync`, which does nothing to
the game and exists only to be that ask. See `RouletteItemEventRouter`.

Do not "finish the port" by moving `place` and `spin` onto it.

**Held, deliberately**

- **One casino tab instead of three.** Joel's plan is a single tab opening a picker for
  all three games, **after this mod is finished** -- stated 2026-09-05. Not to be
  started early. It also moots the fact that `MenuIcon.Pip` is `'S'` here and `'S'` in
  Poker, which makes the two tabs identical once `TabCrowding` drops the labels.
- **Shared client code.** `Textures.cs`, `EscapePatch.cs`, `ProfileSync.cs` and
  `MenuIcon.cs` here are byte-identical to Poker's once the mod name is normalised, and
  `TaskBarTab.cs` differs by a comment. Pulling them into one shared source project is
  the precursor to the master tab. See the root `CLAUDE.md`.

**Decisions left open**

- **Which wheel ships by default.** European, on the grounds that there is no reason
  to hand a single player the worse of the two. Nothing branches on `RouletteRules.Wheel`
  except the top line, so it is a rules change and not a rewrite. Note the art would
  need redrawing for an American wheel -- the ring is generated from the pocket list,
  so it follows automatically, but the bowl photograph has one zero.
