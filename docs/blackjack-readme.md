# Blackjack

A blackjack table for SPT. Stake roubles, dollars, euros, GP coins, bitcoin or
Lega medals against a server-dealt shoe.

**Status: 1.0, working.** Both halves run against SPT 4.1.3 and have been played:
hands dealt, split and doubled, money moved in both directions, balances landing
on the arithmetic. 110 tests.

---

## Architecture

The server is authoritative for everything that matters. The client renders a
state it is handed and sends intents back -- it never sees the hole card, never
draws a card, and never decides an outcome.

```
client (BepInEx plugin)                 server mod (.NET 10)
  menu button           ──POST──►  /blackjack/deal    { wallet, wager }
                        ◄────────  RoundView + ItemEventRouterResponse
  hit / stand / double  ──POST──►  /blackjack/action  { action }
  / split               ◄────────  RoundView, settlement, money delta
```

This is not anti-cheat theatre. Money mutation has to happen server-side because
that is where the profile lives and saves, so the deck belongs next to it. The
consequence worth knowing: **the client cannot animate a deal it has not been
told about**, so the UI must be built around awaiting a response, not around
locally dealing and reconciling.

## Projects

| Project | Target | Purpose |
| --- | --- | --- |
| `src/Blackjack.Game` | net10.0 | Rules engine. No SPT reference, no I/O, no randomness it does not own. |
| `src/Blackjack.Server` | net10.0 | SPT server mod: routes, DI registration, currency. |
| `tests/Blackjack.Game.Tests` | net10.0 | 52 tests over the engine. |
| `tests/Blackjack.Server.Tests` | net10.0 | 58 tests over the money flow, using fakes. |
| `tools/Blackjack.Console` | net10.0 | Terminal table -- plays the engine with no SPT install. |
| `src/Blackjack.Client` | net472 | BepInEx plugin: the menu button, the table, input. |
| `tools/Blackjack.Installer` | net8.0 | Self-contained installer, with the mod embedded. |

`Blackjack.Client` is the one project here that is not .NET 10, because it runs
inside EFT's mono runtime rather than inside the server.

`Blackjack.Game` is currency-agnostic on purpose -- it deals in `int` wagers and
knows nothing about roubles. `Bank` in the server project is the only code that
maps a `Wallet` to an item template.

The server project is split so the interesting half is testable:

- `BlackjackService` holds the whole game flow and depends only on `IBank`,
  `IProfileGateway` and `TableStore`.
- `BlackjackCallbacks` is a thin HTTP adapter -- serialise, log, nothing else.
- `Bank` and `ProfileGateway` are the only classes that touch SPT services.

## How the player reaches the table

**A BLACKJACK entry on the main menu**, under EXIT. It works on a profile five
minutes old, which is the whole reason it is there.

The Rest Space was the original plan and it is worth writing down why it was
dropped, because EFT has more in it than expected. `RestSpaceBehaviour` exposes
`CanAcceptGameDisc`, `StartGame`, `ShowGameScreen` and `FocusGameZoneCamera`,
there is a `RestSpaceGamePanel` with a play button, and a `DialogItem` node holds
four game-disc items. It would have handed us camera framing and cursor handling
for nothing.

It is gated too hard to be the only way in. Rest Space level 1 is nearly free --
10,000 roubles, duct tape, matches, instant build -- but the disc player is level
2: 75,000 roubles, a DVD drive, a magnet, two lamps, Generator 1, an hour of
building, and the area needs the generator actually burning fuel before
`CanPlayGame` goes true. That locks a new profile out of the mod entirely. The
disc route remains a possible second entrance for players who have the area,
rather than the front door.

It is *not* a new hideout area either. `EFT.EAreaType` ends at
`CircleOfCultists = 27`, and every area has a baked prefab; a new value has no
model and the client does not know it exists.

There is no hotkey. A key would be reachable from anywhere, including a raid. The
menu button is the only way in, and it still checks rather than assuming.

### Fitting alongside other menu mods

The button is a clone of one of the menu's own, installed at the end of the frame
rather than immediately. That is the whole integration story.

MoxoPixel's Menu Overhaul restyles the main menu from a hardcoded list of five
buttons, hiding each background, activating its icon and nudging it sideways by a
per-button offset from its own config. A sixth button cannot be in that list and
cannot ask to be added. Waiting a frame means the button being copied has already
been restyled, so the copy inherits all of it. Nothing here names that mod or
depends on it: anything that restyles the hideout button is inherited for free.

Two details that took a while to find:

- `DefaultUIButton` is **not** backed by a `UnityEngine.UI.Button`. It descends
  from `ButtonFeedback`, which implements `IPointerClickHandler` itself and
  exposes a plain `UnityEvent` field called `OnClick`. Looking for a Button
  component finds nothing, and the button appears, looks right and does nothing.
- Positions must be compared in **world space**. The exit entry is a group with
  the button nested inside it, so its `anchoredPosition` is measured against that
  group rather than the menu; comparing them as siblings puts the new button on
  top of EXIT.

### The table

Its own canvas over a dimmed backdrop, fading in and out. The cloth carries the
dealer and the player's hands; the betting bar and buttons sit beneath the table
rather than on it, because the table art is an oval and an oval has far less
usable room than the rectangle around it -- measured, 1230 by 654, narrowing to
58% of the width near the bottom edge.

Everything below the cloth is laid out by stacks rather than by arithmetic. Every
overlap during development came from positioning siblings by hand at heights that
were each individually plausible.

- The dealer shows one card and a drawn back, and reads `10 + ?` rather than a
  total. The hole card is not in the response until the hand ends, so the client
  could not reveal it if it wanted to.
- Buttons come from `availableActions`, rendered in a fixed order so Hit and
  Stand do not move when the legal set changes. The client holds no rules.
- LEAVE TABLE and STATS disappear while a hand is live. The stake is gone and the
  round is still owed.
- Escape closes, in the order things are stacked: the all-in question, then the
  stats sheet, then the table.

Card art is loaded from PNGs beside the plugin, one per card, named for the code
the server sends. Without them the client draws cards instead -- rounded face,
rank in opposite corners, suit through the middle -- and it still draws the back
either way, since the card set has none. Suits are shapes generated in code:
EFT's UI font has no `♠♥♦♣` in it, so spelling them put a giant C on every club.

## What can be staked

Two kinds of thing, and the table does not treat them alike.

**Currency** -- roubles, dollars, euros. Held in thousands, staked in thousands.
The player thinks in amounts, so bets move by a step.

| Wallet | Min | Max | Step |
| --- | --- | --- | --- |
| Roubles | 1,000 | 500,000 | 1,000 |
| Dollars | 10 | 5,000 | 10 |
| Euros | 10 | 5,000 | 10 |

**Valuables** -- GP coins, bitcoin, Lega medals. Held in single figures and staked
by the piece, so the player thinks in counts.

| Wallet | Min | Max |
| --- | --- | --- |
| GP coins | 1 | 50 |
| Bitcoin | 1 | 10 |
| Lega medals | 1 | 5 |

SPT classes GP coins as money; bitcoin and Lega medals are barter items. That
distinction does not matter to `Bank`, which walks item stacks either way, but it
is why they cannot share one set of limits: a minimum of 1,000 is beneath notice in
roubles and impossible in bitcoin.

**The natural rate follows what is being staked.**

| Stake | Win | Natural |
| --- | --- | --- |
| Currency | 2x | **2.5x** (3:2) |
| Valuables | 2x | **2x** (even money) |

Currency pays the usual 3:2 -- the rounding lands inside a rouble nobody counts.
Valuables cannot: one bitcoin at 3:2 settles on two and a half, and half a bitcoin
does not exist. Rounding it either way would be a rule the player could not see, so
they are dealt at even money instead, which divides for any whole stake.

One shoe serves every currency, so the rate cannot live on the table.
`BlackjackTable.Deal` takes a per-round override and `WalletInfo.BlackjackPayout`
supplies it. A natural is still adjudicated as a `Blackjack` outcome and counted
separately in the stats either way.

Bet limits live in `WalletInfo`, not `Rules`. The engine has no concept of a
currency -- it takes an int -- so `TableStore` builds tables with deliberately wide
engine limits and the per-wallet ones govern.

**The maximum is the house's real protection**, and not for the reason it looks
like. These rules -- six decks, dealer stands on soft 17, 3:2 naturals -- are
about a 0.45% edge, which is invisible across a session. What stops a player
compounding is being unable to cover a losing streak by doubling up, and a ceiling
of five hundred times the minimum caps that at nine doubles. Tightening the rules
instead barely helps: dealer hitting soft 17 is worth 0.22%, and even 6:5 naturals
only reach about 1.9%, which an unbounded bet walks straight through.

It can be turned off, in the BepInEx menu under **Table**. The client sends
`IgnoreMaximum` and the server takes it at its word: this is single player, the
person sending the request owns the server receiving it, and nothing is being
defended against. The setting lives in F12 rather than a JSON file because that is
where someone will look for it. The **minimum is not waivable** -- a bet of
nothing is not a bet.

## Winning a hand

The panel is the notification. The settled state shows the outcome and the payout
while the player is looking straight at it, so there is no system popup -- one would
only compete with the thing that already says it.

Winnings go straight to the stash. Posting them instead would mean a message per
hand, each needing manual collection, which would make the game unplayable.

**Except when the stash will not take them.** `AddItemToStash` can decline to place
an item without throwing, usually because the stash is full, and the winnings would
simply be gone. `Bank.Credit` compares the balance before and after against what it
intended, and posts whatever failed to land as a message with the items attached --
the same way insurance returns arrive. SPT's own new-message notification tells the
player it is waiting. Mail is held for 90 days, because a payout that expires is the
payout this exists to rescue.

A trader could front that message instead of the system. Nothing in the hideout is
plausibly running the table, so it is a system message for now.

## Unsettled stakes

The table lives in memory; the stake does not. A stake is debited from the profile
and written to disk the moment a hand is dealt, so a crash or restart mid-round
would take the player's money and leave no hand to win it back with.

`EscrowStore` records every stake from the deal until settlement, in the mod's own
folder. Anything found outstanding is refunded the next time that player is seen --
lazily, on their next request, rather than at boot, which avoids touching profiles
before the server has finished loading them. A round still in progress is left
alone; only an orphaned stake is refundable.

This also covers valuables staked through EFT's grid. Betting those means the items
genuinely move into a container, so there is a window where they are neither in the
stash nor won or lost, with exactly the same failure mode.

## Stats

Every settled round is folded into a per-profile record: rounds and hands played,
wins, losses, pushes, blackjacks, busts, streaks, and per-currency totals with best
and worst round. Served by `POST /blackjack/stats`.

Stored in the mod's own folder via `ModHelper.GetAbsolutePathToModFolder`, **not in
the SPT profile**. Adding fields to the profile changes its schema, which is what
makes some mods demand a wipe when they are removed; keeping the record separate
means uninstalling this mod costs the player nothing but the stats. A corrupt stats
file logs and starts fresh rather than blocking the mod from loading.

Two counting rules that are easy to get wrong, and are pinned by tests:

- A split is **one round but two hands**. Wins, losses and pushes sum to hands;
  streaks run on rounds, so a split that wins one and loses the other is a round
  the player broke even on, not a win and a loss at once.
- A 21 assembled after a split is a win, **not** a blackjack, and must not be
  tallied as one.

## Testing without an SPT install

SPT's `InventoryHelper`, `ProfileHelper` and `SaveServer` are concrete classes
with non-virtual methods, so anything depending on them directly cannot be tested
without a running server. `IBank` and `IProfileGateway` exist to break that: SPT's
DI registers a class against every interface it implements, so the real
implementations resolve with no extra wiring, and the tests substitute fakes.

What that buys: every path that moves currency is covered without SPT present --
stake collection, double and split top-ups, settlement, refusals, and per-currency
isolation. `MoneyInvariantTests` plays 400 random rounds and asserts, after each
one, that the money the service moved equals the profit the engine reported.

The suite was mutation-checked: collecting the full stake instead of the increase,
and paying out on losing hands, each fail 7 tests.

Both of the things this could not reach have since been checked against a real
4.1.3 server: `Bank`'s own `InventoryHelper` calls move money correctly, and
`scripts/smoke.ps1` resolves the session and plays a hand over HTTP.

Neither was clean first time, and the bugs are worth knowing because none were
reachable from the tests:

- `new ItemEventRouterResponse()` initialises nothing, and `RemoveItemByCount`
  reaches into `output.ProfileChanges[sessionId]`. It threw **after** taking the
  items, so the mod reported "not enough roubles" while the stake had left the
  stash. Responses must come from `EventOutputHolder.GetOutput`.
- SPT matches request properties **case-sensitively**. Lowercase JSON binds
  nothing and every field takes its default.
- Enums cross the wire as integers unless a `[JsonConverter]` sits on the
  *property*. `options.Converters` outranks a type-level attribute, and SPT
  registers `EftEnumConverterFactory` into it.

## Engine

`BlackjackTable` is the whole game. Construct it, call `Deal`, then `Hit` /
`Stand` / `Double` / `Split`; every one returns the `RoundView` the client
renders. Illegal actions throw rather than being silently ignored.

Rules are configurable via `Rules`: deck count, dealer hits soft 17, blackjack
payout, double-after-split, split limit, one-card-after-ace-split, shoe
penetration, table limits.

### Rules the tests pin down

These are the ones implementations usually get wrong:

- A 21 assembled after a split is **not** a natural. It pays the same here either
  way, but the distinction still governs whether it counts as a blackjack.
- The dealer peeks, so a player never doubles or splits into a hand already lost.
- A player who busts loses immediately -- the dealer does not draw, even if it
  would also have busted. This is where the house edge actually comes from.
- Only one ace can count as 11, so scoring is a single conditional promotion.
- Split aces take exactly one card each and are then forced to stand.

Run them with `dotnet test`.

## Routes

| Route | Body | Purpose |
| --- | --- | --- |
| `POST /blackjack/deal` | `{ wallet, wager }` | Takes the stake, deals a round. |
| `POST /blackjack/action` | `{ action }` | Hit, Stand, Double or Split. |
| `POST /blackjack/state` | -- | Current round, for reconnecting a UI. |

All three return `{ ok, error, round, balance, wallet }`.

### Two transports, one game

The game client uses **item-event actions**, not the static routes above:

| Action | Body |
| --- | --- |
| `BlackjackDeal` | `{ Wallet, Wager }` |
| `BlackjackPlay` | `{ Move }` |

These arrive on the endpoint EFT already uses for moving items, so the reply carries
the `ProfileChanges` the client applies to its own inventory copy. Without that, money
lands in the profile but the stash view stays stale until the game reloads -- which
reads to a player exactly like the mod ate their winnings.

An item-event reply carries `ProfileChanges` and nothing else, so the round rides
along in the response's extension data under `blackjack` rather than costing a second
request.

The static routes stay because they are how the mod is tested with curl and no game
attached. They pass a throwaway change record, since nothing is listening for it --
so a curl session shows correct balances in the response and a stale stash in game.
Both transports call the same `BlackjackService`.

## Build and verify

```
dotnet test                                    # 110 tests, no SPT needed
dotnet run --project tools/Blackjack.Console   # play a hand in the terminal

dotnet build src/Blackjack.Server/Blackjack.Server.csproj -c Release
dotnet build src/Blackjack.Client/Blackjack.Client.csproj -c Release -p:SPTPath="H:\SPT4.1.X"
```

The client must be built against the install it will run on: 4.1.3's
`PluginValidator` reads a plugin's references to `spt-*` and requires the
major.minor to match.

Releases:

```
python tools/build-installer.py       # builds both halves, stages payload.zip
python tools/build-zip.py             # the same payload as a plain archive
dotnet publish tools/Blackjack.Installer/Blackjack.Installer.csproj -c Release -o dist-installer
```

The server mod goes to `SPT_Runtime/user/mods/Blackjack/` and the plugin to
`BepInEx/plugins/Blackjack/`. Note `SPT_Runtime`, not `SPT` -- 4.0 used the
latter, and an archive laid out for it extracts one level too high and is never
scanned, which looks exactly like the mod failing to load.

Then, with the server running:

```
scripts\smoke.ps1 -SessionId <your-profile-id>
```

That plays a hand over HTTP with no game client attached.

## Diagnosing a bad run

Every line the mod writes is prefixed `[Blackjack]`, so it can be filtered out of a
busy server console. `config.json` beside the DLL turns verbose logging off once
things work; leave it on for a first run.

**Start with the ping.**

```
scripts\smoke.ps1 -SessionId <your-profile-id> -PingOnly
```

It touches no money and starts no round, and answers the four things that must be
true before a bet is worth attempting: the mod loaded, the route is reachable, the
session resolved to a real profile, and that profile's money can be read.

What the failures mean:

| Symptom | Cause |
| --- | --- |
| No `[Blackjack]` banner at server startup | The mod never loaded. Almost always the `SptVersion` gate. |
| Ping returns 404 | Routes not registered, though the mod loaded. |
| Ping returns a blank `sessionId` | The PHPSESSID cookie assumption is wrong. |
| `sessionId` set but no profile | Wrong id -- check the filename in `user\profiles\`. |
| `debit mismatch` / `credit mismatch` in the log | `InventoryHelper` did something other than what was asked. Every balance shown to the client is then suspect. |
| `AddItemToStash threw ... unpaid` | A payout was lost. The line says exactly how much. |

The mismatch lines are the ones worth watching for. `Bank` compares the balance
before and after every move against what it intended, so a silent failure inside
`InventoryHelper` -- a full stash, for instance, which can decline an item without
throwing -- gets caught rather than quietly shorting the player.

## Art and credits

### Playing cards

The card faces are the **Vectorized Playing Cards 1.3** set by *Chris Aguilar*,
from [opengameart.org](https://opengameart.org/content/playing-cards-vector-png).

They ship as PNGs in `BepInEx/plugins/Blackjack/cards/`, one per card, named for
the two-character code the server sends -- `AS.png` is the ace of spades, `TD.png`
the ten of diamonds. Renaming them from `ace_of_spades.png` is the only change
made to the set; the images themselves are untouched.

Check the licence in that set before redistributing this mod with the cards
included. Anyone can delete the folder and the mod draws its own cards instead,
so the art is not required for it to work.

### Everything else

The table is a photograph, shipped as `table.png` beside the plugin. Delete it
and a drawn table takes its place.

The suits, card backs, rounded panels and the menu button's diamond are all
generated in code at load, from the shapes in `Textures.cs`. Nothing else is
shipped and nothing is loaded from an asset bundle.

## SPT version sensitivity

Targets **SPT 4.1.3**. The `SPTarkov.*` NuGet packages lag the game: 4.1.2 is the
newest published, which is what this references. If a 4.1.3-specific API is
missing, reference the DLLs from the server install directly instead.

Note that SPT 4.0 moved server mods from TypeScript to C# -- 3.x guides do not
apply.

Things to re-check when the SPT version moves:

- `IModMetadata.SptVersion` is a hard gate; the mod will not load outside its
  range. It is `~4.1.3`, meaning `>=4.1.3 <4.2.0`.
- `InventoryHelper.RemoveItemByCount` / `AddItemToStash` signatures.
- `Money` template ids (stable so far, but they live in the server enum).
