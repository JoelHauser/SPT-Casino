# Blackjack -- working notes for Claude

A card table for the SPT hideout. Server mod in C# (.NET 10) against SPT 4.1.3,
with a BepInEx client plugin still to be written. Players stake roubles, dollars,
euros, GP coins, bitcoin or Lega medals.

This file is loaded automatically at the start of every session. Keep it to things
a fresh session would otherwise rediscover the hard way -- not a chronological
diary, which would grow without bound. **Update "Current state" when you finish a
piece of work.**

---

## The single most important fact

**SPT 4.x server mods are C#, not TypeScript.** The `mod.ts` / `package.json` /
tsyringe world ended at 3.x, and most guides online still describe it. Server mods
are .NET 10 class libraries referencing `SPTarkov.Server.Core`, with an
`IModMetadata` record in place of `package.json`.

`SptVersion` in that record is a **hard load gate**. It is `~4.1.3` (>=4.1.3
<4.2.0). A mod outside the range loads nothing and logs nothing.

## Layout

| Project | Owns |
| --- | --- |
| `src/Blackjack.Game` | Rules engine. No SPT reference, no I/O, no clock. |
| `src/Blackjack.Server` | The mod: routes, DI, currency, stats, escrow, logging. |
| `src/Blackjack.Client` | The BepInEx plugin: the menu button, the task-bar tab, the table. net472, and the one project that needs an install to build. |
| `tests/Blackjack.Game.Tests` | 52 tests over the engine. |
| `tests/Blackjack.Server.Tests` | 51 tests over the money flow, on fakes. |
| `tools/Blackjack.Console` | Terminal table. Plays the engine with no SPT install. |
| `scripts/smoke.ps1` | Drives a real server over HTTP. Verified; `-Play` picks the action to exercise. |

The engine knows nothing about currency -- it takes an `int` and returns an `int`.
Everything that maps a `Wallet` to an item template lives in `Wallets.cs` and
`Bank.cs`. Keep it that way; it is what makes the rules testable.

## Whether there is an SPT install depends on the machine

This repo is worked on from more than one machine. Check before assuming:

| Machine | Installs |
| --- | --- |
| The one this file was written on | `C:\HUH` -- SPT 4.1.3, **EFT 0.16.9.5-40743** |
| Joel's Windows box | `H:\SPT4.1.X` (4.1.3) and `H:\SPT2026` (4.0.13) |

`Blackjack.Client.csproj` picks whichever of those exists; `-p:SPTPath=...` for
anything else. **The two installs are not the same game build**, and the client
plugin is compiled against the game, not just against SPT -- see the MenuScreen note
under "Things that will bite you".

**With an install present**, three things the rest of this file calls unverifiable
become checkable, and all three have now been done -- see "What the install
settled" below. Item templates live at
`SPT_Runtime/SPT_Data/database/templates/items.json`, the server assemblies at
`SPT_Runtime/SPTarkov.*.dll`, and `EscapeFromTarkov_Data/Managed/Assembly-CSharp.dll`
is what the client plugin needs.

**Reflecting over the installed assemblies beats reflecting over the NuGet
package**, because the package lags: NuGet tops out at 4.1.2 and the install is
4.1.3. Mono.Cecil ships with the game at `BepInEx/core/Mono.Cecil.dll` and reads
them without loading them.

**Without an install**, .NET 10 file-based apps make the NuGet package a
one-liner:

```csharp
// probe.cs, run with: dotnet run probe.cs
#:package SPTarkov.Server.Core@4.1.2
var asm = typeof(SPTarkov.Server.Core.Models.Eft.Profile.SptProfile).Assembly;
var t = asm.GetTypes().First(x => x.Name == "MailSendService");
foreach (var m in t.GetMethods()) Console.WriteLine(m);
```

Source lives at `github.com/sp-tarkov/server-csharp` under
`Libraries/SPTarkov.Server.Core/`.

## What the install settled

Done on Joel's box against 4.1.3, by reading the shipped assemblies and database.
None of it required running the server.

- **Building against NuGet 4.1.2 is safe on a 4.1.3 install.** Every SPT symbol the
  compiled mod names -- 36 types and 63 members -- resolves against the installed
  `SPTarkov.*` assemblies. Nothing the mod touches moved between the two.
- **The 4.1.3 namespaces**, which are not what the older docs say:
  `Helpers.Profile.InventoryHelper`, `Helpers.Profile.ProfileHelper`,
  `Helpers.Items.ItemHelper`, `Services.Commerce.MailSendService`,
  `Servers.SaveServer`, `Common.Models.Logging.ISptLogger<T>`.
- **Signatures on the money path are as assumed**:
  `AddItemToStash(MongoId, AddItemDirectRequest, PmcData, ItemEventRouterResponse)`
  returning void, and `GetPmcProfile(MongoId)` returning `PmcData`.
- **All six wallet templates exist**, with these real stack limits:

  | Wallet | Template | StackMaxSize |
  | --- | --- | --- |
  | Roubles | `5449016a4bdc2d6f028b456f` | 1,000,000 |
  | Dollars | `5696686a4bdc2da3298b456a` | 50,000 |
  | Euros | `569668774bdc2da2298b4568` | 50,000 |
  | GP coins | `5d235b4d86f7742e017bc88a` | 100 |
  | Bitcoin | `59faff1d86f7746c51718c9c` | **1** |
  | Lega medal | `6656560053eaaa7a23349c86` | **1** |

  **Bitcoin and Lega medals do not stack.** A maximum bitcoin win is 20 separate
  items needing 20 free grid cells, which is the likeliest way a payout runs out of
  room -- and the reason `Bank.Credit`'s shortfall-to-mail path matters more than it
  looked. `Bank` reads these from the database rather than assuming them, so an item
  mod that changes a limit is handled.

  A comment in `Bank.cs` claimed roubles cap at 500,000. They cap at 1,000,000.

## The task bar, as it actually is

Read out of `C:\HUH`'s `Assembly-CSharp.dll` with Mono.Cecil. This is the row along
the bottom of the menu -- MAIN MENU, HIDEOUT on the left, CHARACTER through WATCHLIST
on the right -- and every one of these was a guess before it was checked.

- **`EFT.UI.PreloaderUI.MenuTaskBar`** is the bar, a public field on a
  `MonoBehaviourSingleton`. So `PreloaderUI.Instantiated` then
  `PreloaderUI.Instance.MenuTaskBar`, and there is never any need to search the scene
  for it.
- **It belongs to PreloaderUI, which outlives a raid.** The bar is hidden in a raid,
  not destroyed, so "the bar is gone" is not a test for anything. Nor is
  `Singleton<GameWorld>.Instantiated`, which is true from the moment a raid starts
  *loading* -- see "Playing while a raid loads".
- **The tabs are `EFT.UI.AnimatedToggle`, which is a `UnityEngine.UI.Toggle`** -- not a
  button, and not a `DefaultUIButton` like the main menu's. A clone therefore handles
  its own click with no listener to clear, and joining the toggle group would deselect
  whatever screen the player is looking at. Disabling the component settles both:
  the event system skips a disabled Behaviour, and `Toggle.OnDisable` leaves the group.
- **They live in a private `_toggleButtons`**, a `Dictionary<EMenuType, AnimatedToggle>`,
  Odin-serialized from the prefab. `EMenuType` is the key worth having: MainMenu=100,
  Play=0, Player=1, Trade=3, Chat=4, Handbook=5, Settings=6, Exit=7, Logout=8,
  HideScreen=9, EditBuild=10, Hideout=11, Reconnect=12, RagFair=13, GoInRaid=14,
  ToggleGameMode=15, NewsHub=16. PRESETS is EditBuild; CHARACTER is Player.
- **The labels are `CustomTextMeshProUGUI`** -- BSG's own subclass of
  `TextMeshProUGUI`, in **no namespace at all**. Anything that decides what to switch
  off on a cloned tab by namespace switches the tab's own text off with it. Test the
  type, not the namespace.
- **The text is driven by an `EFT.UI.LocalizedText`** that re-applies its key on
  `OnEnable`, so a renamed clone reverts to HANDBOOK unless that component is disabled.
- **The unread badges are fields on the bar**, one per kind: `_newInformation`
  (an array), `_producedItemsObject`, `_failedItemsObject`, `_newMessagesObject`,
  `_newAttachmentsMessagesObject`, `_newFriendRequestsObject`, `_newNodesObject`,
  `_newNewsObject`. The bar drives them on the originals only, so a clone freezes
  whatever it copied -- a hideout tab cloned mid-craft keeps that badge until restart.
- **`EFT.UI.HoverTooltipArea.SetMessageText(string, bool rawText)` is public**, and the
  clone keeps a working reference to the shared `SimpleTooltip`, so a cloned tab gets a
  real tooltip for the price of one call.
- **The row lays itself out, and the gap in the middle is an object.** From the prefab
  in `EscapeFromTarkov_Data/sharedassets49.assets`:

  ```
  TaskBar                 MenuTaskBar, Animator, VerticalLayoutGroup
    Tabs                  HorizontalLayoutGroup, ToggleGroup
      MainMenu            wrapper: HorizontalLayoutGroup, ToggleGroup, CanvasGroup, HoverTooltipArea
        MainMenuButton    Image, HorizontalLayoutGroup, Animator, AnimatedToggle, LayoutElement
          Icon            Image
          Text            LocalizedText, CustomTextMeshProUGUI
        NewInformation    the unread badges
      Hideout             ... same shape
      GroupPanel
      Spacer              the empty middle, a layout element rather than a coincidence
      Character, Merchants, FleaMarket, EditBuild, Handbook, Chat, Watchlist, News, Settings
  ```

  Every tab is sized (0,0) in the prefab, so nothing is positioned by hand and adding
  one is a sibling index: before Spacer for the left group, after it for the right.
- **The toggle is on a child of a tab, not on the tab.** `_toggleButtons` hands back the
  AnimatedToggle, which lives on the *button* inside the wrapper. Cloning that object
  and parenting it where it sits puts BLACKJACK **inside** the hideout tab, sharing its
  slot. The wrapper is what gets cloned.
- **Another mod's tab costs this one nothing.** The layout group measures the row every
  time it is dirtied, so a second added tab shifts everything along and removing it
  reflows -- neither mod has to know about the other. Two things follow for anyone
  writing the other mod: take the template from `_toggleButtons`, which holds only the
  game's own tabs, or a mod that picks one geometrically eventually clones *our* tab and
  inherits a diamond and a pile of disabled components; and split the row on the
  spacer's `flexibleWidth` rather than on the widest gap, because added tabs eat that
  gap until measuring says the row is one group and puts the new tab beside SETTINGS.
- **A tab's CanvasGroup ships at alpha 0.3 with `interactable` false** -- the
  locked-feature look, which MenuTaskBar turns on per tab as a profile unlocks things.
  It does not know about a grafted-on tab, so a clone stays greyed out *and swallows its
  own clicks* until both are set by hand.
- **The wrapper is a HorizontalLayoutGroup too**, so a highlight added as a child gets
  laid out as one more item in the row and shoves the button sideways. `LayoutElement.ignoreLayout`
  is what keeps an overlay an overlay.
- **The bar greys itself out through its own dictionary** (`SetTaskBarInteractable`,
  `SetButtonsInteractable`), which a grafted-on tab is not in. Mirroring a neighbour's
  `interactable` is how ours dims and stops answering at the same moments as the rest.

### The pip is 160 units wide, and that one number broke both entrances

Seen at last on a real screen, with Poker installed alongside: **both mods' tabs were
about twice the width of the game's own**, and the menu button's icon pulled apart into
two shapes on hover. One cause, wearing two disguises.

**An `Image` reports its sprite's native size as its layout-preferred size, and a
layout group believes it.** `Textures.Suit` draws 160 pixels square, and the canvas is
at 100 reference pixels per unit, so the pip asks for **160 units where the hideout's
own icon asked for 25**. The tab measured 230 wide against the game's 112; on the menu
button the icon was normal until the hover state dirtied the layout and let the Image
have the width it had been asking for all along, and a diamond magnified sixfold and
cropped to its middle is a band rather than a rhombus.

`MenuIcon.Pin` holds the icon to the footprint of the one it replaced -- read off the
rect *before* the swap -- with a `LayoutElement` for the parent that measures and
`SetSizeWithCurrentAnchors` for the one that does not. Not `sizeDelta`: on a rect that
stretches with its parent that is not a size at all.

**The label was innocent, and this is the reason to distrust a plausible story.** A tab
twice the width of its neighbours points straight at text fitting, and three real
faults were duly found in `Relabel` -- auto-sizing rescaling the letters rather than
the box, growth allowed in one direction only, chrome counted twice. All three are
worth fixing and **none of them was happening**: the template's label measured 16pt at
64.6 wide and the clone's 16pt at 48.3, so the clone's label was the *narrower*. They
are kept as defence and the comment on `Relabel` says so. What settled it was Poker
logging the two tabs' geometry side by side; one line reading `Icon w=25` against
`Icon w=160` ended the argument.

Separately, and not about size: **an `Animator` is a `Behaviour`, not a
`MonoBehaviour`.** `Neuter` sweeps `GetComponentsInChildren<MonoBehaviour>` and so
never saw the one the tab clones, which then went on animating a tab whose toggle no
longer drove it. Frozen instead -- `Instantiate` copied the template's current values
and the template is picked unselected, so freezing keeps exactly the resting look.

**The menu button is a separate story and it is left alone deliberately.** It still
creeps a row per menu when another mod places itself the same way, which is this
file's long-standing open item -- but Poker now measures only the buttons `MenuScreen`
declares as its own and holds the row directly under EXIT, so ours settles one row
under it and stays put. If both mods claimed the row under EXIT they would land on top
of each other, so the fix is not symmetrical and should not be ported.

## Escape, and why watching the key was never enough

The table is our window floating over one of the game's screens, and the game has no
idea it exists. Watching for the key in `Update` closed the table but did not *stop*
it: the stash or the flea market underneath took the same escape on the same frame and
backed out too, so closing the table also left the screen it was opened from. From the
hideout it read as the mod throwing you out of the hideout.

**Take the command out of the frame's list; do not answer it.** EFT's input system is a
tree of `InputNode`s under an `InputTree`, and
`InputNodeAbstract.TranslateInput(commands, ref axes, ref cursor)` is what walks it:
every node is handed the same `List<ECommand>` and recurses into its children. Removing
`ECommand.Escape` from that list before the root recurses means nothing below is ever
offered it. `InputTree` is the root and does **not** override `TranslateInput`, so
patching the abstract base's implementation *is* patching the root -- one patch for the
stash, the flea market, the hideout, a trader screen and whatever a future build adds.

**The obvious hook is a stub, and it cost a round trip.** `UIInputRoot.TranslateCommand`
is the root of the UI input tree and its name says it translates commands. Its entire
body is `return ETranslateResult.Ignore`. Patching it applied cleanly, logged no error,
changed nothing -- and disabled the key-watching fallback that had at least been closing
the table, so escape went from half working to not working at all. **Read a method's IL
before hanging behaviour off its name.**

The `Update` poll survives as a fallback for a build where the patch will not apply -- a
table that cannot be closed is worse than one that closes the screen behind it -- and
`EscapePatch.Applied` is what keeps the two from both firing.

Poker patches the same method. Two prefixes on one method is ordinary Harmony, and only
the mod whose table is open answers.

## Playing while a raid loads

**Deliberate.** Matchmaking and the loading screen leave the task bar up -- the player
can open their character there -- so the tab is reachable and a few hands is a better use
of that wait than a progress bar.

**`Singleton<GameWorld>.Instantiated` is not that line, though it reads like it.**
`GameWorld` is created when the raid *starts loading*, not when it starts, so testing it
shut the table the moment the player queued -- which is exactly the wait the table is
most wanted for.

Two signals now, either of which means the raid is genuinely under way, because being
late here is worse than being early: a table on a canvas at sorting order 30000 over a
live raid. `GameWorld.MainPlayer` is filled in when the player is actually spawned, and
`AbstractGame.Status` reaches `Started` when the raid is running -- during loading it is
`Starting`. Both are `Comfort.Common.Singleton` types the game itself registers.

It is checked every frame in the plugin's `Update` rather than in the tab's
once-a-second heartbeat, because a poll can be a second late. In co-op the moment is not
even the player's to choose: the host starts the raid and pulls them out of the lobby
with the table open.

## Things that will bite you

Each of these cost real time. None are hypothetical.

- **`new ItemEventRouterResponse()` is not a usable response.** Its constructor
  initialises nothing, and `RemoveItemByCount` reaches into
  `output.ProfileChanges[sessionId]`, so a hand-built one throws
  NullReferenceException -- *after* the items are already gone. That failure reported
  itself as "not enough roubles" while the stake had left the stash. Get one from
  `EventOutputHolder.GetOutput(sessionId)`. The static routes cannot return it to the
  client, so the stash view stays stale; being unread is fine, being uninitialised is
  not.
- **A mod can change any item's stack limit.** Roubles cap at 1,000,000 in the base
  database and at 20,000,000 on a server running BarterItemsStacks. `Bank` reads
  `StackMaxSize` live for this reason; assuming the database value would be wrong on
  a real install.
- **`PaymentService` cannot settle a bet.** Both entry points derive currency from
  a trader -- `GiveProfileMoney` reads `trader.Currency`, and `PayMoney`'s
  no-trader path is hardcoded to roubles. `Bank` walks item stacks directly.
- **`AddItemToStash` can decline an item without throwing.** A full stash silently
  swallows a payout. `Bank.Credit` compares the balance either side of every move
  against what it intended and posts the shortfall as mail rather than losing it.
- **An item-event reply carries `ProfileChanges` and nothing else.** The round
  rides in the response's `ExtensionData` under `blackjack`, or the client would
  need a second request for it.
- **A custom static route does not update the client's inventory.** Money lands in
  the profile but the stash view stays stale until reload, which reads to a player
  as the mod eating their winnings. That is why the client uses item-event actions.
- **The table is in memory and the stake is not.** A stake is debited and saved the
  moment a hand is dealt, so a crash mid-round used to take the money and leave no
  hand. `EscrowStore` records every stake until settlement and refunds orphans.
- **`/blackjack/state` is called before any hand exists.** `DealerView` used to
  read `_dealer.Cards[0]` unconditionally and threw on a fresh table -- every visit
  to the panel would have failed. An empty dealer hand must describe itself.
- **The client plugin is compiled against the game, not just against SPT.** Two
  installs on the same SPT 4.1.3 can be different EFT builds, and `EFT.UI` moves. On
  0.16.9.5 `MenuScreen.Awake` is private and `Show`'s controller argument is an
  obfuscated nested type, so `[HarmonyPatch(typeof(MenuScreen), nameof(MenuScreen.Awake))]`
  -- which compiled on the other box -- does not compile at all here. Harmony only ever
  wanted a `MethodBase`: `TargetMethods()` with `AccessTools.Method(..., "Awake")` binds
  by name at runtime and is indifferent to both. `PatchAll` is wrapped in a try/catch
  for the same reason -- a patch that will not apply must not take the rest down.
- **Naming a property `Path` shadows `System.IO.Path`** inside the same class and
  breaks every `Path.Combine`. `StatsStore.FilePath` is named that for this reason.
- **`OnLoadOrder` has no `PostDBModLoader`.** The values are `Watermark`, `Preload`,
  `GameCallbacks`, `TraderRegistration`, `Routers`, `HandbookCallbacks`,
  `SaveCallbacks`, `TraderCallbacks`, `PresetCallbacks`, `RagfairCallbacks`,
  `PostLoad`.
- **SPT's DI registers a class against every non-System interface it implements**
  (`DependencyInjectionHandler.InjectAll`), so `Bank : IBank` resolves for free.
  That is what makes the interface seams cost nothing.
- **Bash heredocs mangle backslashes.** Writing C# with `'\\'` through
  `cat <<'EOF'` produces broken escapes. Use the Write tool for those files.
- **`Compress-Archive` writes backslash zip entries**, which extract as one literal
  filename on Linux. Pack releases with `System.IO.Compression` instead.

## Reading the game's UI without launching it

The assemblies say what the code does; the prefabs say what the screen looks like, and
that is where the layout questions get answered. `UnityPy` reads them straight out of
the install -- no AssetStudio, no game running:

```python
# pip install UnityPy
import UnityPy
env = UnityPy.load(r"C:\HUH\EscapeFromTarkov_Data\sharedassets49.assets",
                   r"C:\HUH\EscapeFromTarkov_Data\globalgamemanagers.assets")
# GameObject / RectTransform / CanvasGroup read in full;
# a MonoBehaviour gives up its m_Script, and m_Script.deref().read().m_ClassName is
# the component's real name -- HorizontalLayoutGroup, AnimatedToggle, LocalizedText.
```

- **The menu UI is `sharedassets49.assets`** (~700 GameObjects). `globalgamemanagers.assets`
  is 359MB of MonoScript and nothing else -- it is the script registry, which is why a
  raw search for a class name lands there and finds no objects.
- **Type trees are stripped for MonoBehaviours**, so a component's *fields* are not
  readable -- no spacing values, no LayoutElement widths. Its identity is. Unity's own
  types (RectTransform, CanvasGroup, GameObject) read completely.
- A raw byte search for a class name across `*.assets` is the fast way to find which
  file to open.

## Talking to the server without a game client

Three things about SPT's HTTP layer, each of which cost a round trip to discover
because the error named none of them. All read out of 4.1.3 and then confirmed
against a running server.

- **It serves HTTPS, not HTTP**, on the same port, with a self-signed certificate
  it generates into `user\certs\`. .NET rejects that by default and reports "the
  underlying connection was closed", which reads as the server being down.
- **Every request body is zlib-inflated and every response deflated**, because that
  is what the EFT client speaks. Two headers opt out, and
  `SptHttpListener.HandleAsync` / `IsDebugRequest` are where they are read:

  | Header | Effect |
  | --- | --- |
  | `requestcompressed: 0` | read my body as plain UTF-8 |
  | `responsecompressed: 0` | reply in plain JSON |

  Without them a plain-JSON body dies inside `Inflater` with "the archive entry was
  compressed using an unsupported compression method".
- **Request bodies are matched case-sensitively.** `{"wager": 10000}` binds nothing
  against `public int Wager`; every property silently takes its default. That made a
  10,000 bet arrive as 0 and come back "bets run from 1,000 to 500,000", while
  `Wallet` -- which has a default of Roubles -- looked like it had bound correctly.
  Send PascalCase.
- **Enums go over the wire as integers, not names.** `phase` is `1`, not
  `"PlayerTurn"`. Comparing against the name never matches and never errors, which
  left a dealt hand sitting in PlayerTurn with the stake in escrow while the caller
  reported success. `RoundPhase` is AwaitingBet/PlayerTurn/DealerTurn/Settled,
  `HandOutcome` is Pending/Win/Lose/Push/Blackjack/Bust, `PlayerAction` is
  Hit/Stand/Double/Split, all zero-based. Worth making these strings before the
  client plugin is written, so it does not hardcode magic numbers.
- **The session id is a `PHPSESSID` cookie**, read with
  `Request.Cookies.TryGetValue` in `HttpServer.HandleRequestAsync`. In PowerShell it
  cannot be passed through `-Headers`: `Cookie` is a restricted header and it is
  dropped **silently**, so the request arrives with no session and the server says
  "session id provided was empty, did you restart the server while the game was
  running?". Use a `WebRequestSession`. `scripts\smoke.ps1` does.

## Architecture

Server-authoritative. The client renders what it is handed and sends intents; it
never sees the hole card, never draws, never decides an outcome.

```
BlackjackService          the whole game flow, on IBank / IProfileGateway /
                          IStatsStore / IEscrowStore. No SPT types but MongoId,
                          so it is testable without a server.
BlackjackCallbacks        static routes  -- curl testing, throwaway change record
BlackjackItemEventCallbacks  item events -- the game client, real change record
Bank / ProfileGateway     the only classes that touch SPT services
```

The interface seams exist because `InventoryHelper`, `ProfileHelper` and
`SaveServer` are concrete classes with non-virtual methods. Depending on them
directly makes the calling code untestable, server or not.

Two transports, one service. Do not put game logic in either adapter.

## Decisions already made

- **Not a new hideout area.** Confirmed against the client: `EFT.EAreaType` ends at
  `CircleOfCultists = 27`, and each area has a baked prefab. A new value has no model
  and the client does not know it exists.
- **Not the Rest Space either -- reversed on 2026-08-27.** The table is reached from
  a button this mod adds to the main menu, and works on a fresh profile.

  The Rest Space was the original plan, and EFT turns out to have a whole game-disc
  system sitting in it -- `RestSpaceBehaviour` with `CanAcceptGameDisc`, `StartGame`,
  `ShowGameScreen` and `FocusGameZoneCamera`, plus a `RestSpaceGamePanel` with a play
  button, and a `DialogItem` node (`684070bd2f743ae53b0b80ec`) holding four disc
  items. It would have solved the camera and cursor problems for free.

  It is gated too hard to be the only way in. Rest Space level 1 is nearly free
  (10,000 roubles, duct tape, matches, Vents 1, instant), but the disc player is
  level 2: 75,000 roubles, a DVD drive, a magnet, two lamps, Generator 1, an hour to
  build -- and the area is `needsFuel`, with `CanPlayGame` gated on the generator
  actually running. That locks a new profile out of the mod entirely.

  The disc route stays on the table as an optional second entrance later, for players
  who have the area built. It is flavour, not the front door.
- ~~**The entry point is a button on `EFT.UI.MenuScreen`**~~ -- **the entry point is the
  task-bar tab**, and the button is off by default behind `ShowMenuButton`. Seen beside
  Poker's, the button was the weaker entrance twice over: it exists only on the main
  menu, where the tab is on every out-of-raid screen, and it adds a card game to a list
  of five that reads ESCAPE FROM TARKOV, CHARACTER, TRADING, HIDEOUT, EXIT -- with both
  mods installed that list grew by 40% and the card games were the loudest thing on it.
  The code is kept, not deleted: still cloned from one of the `DefaultUIButton` fields
  already there (`_hideoutButton`, `_playButton`, `_tradeButton`, ...), still patched on
  `Awake` and `Show`. It is a patch applied once at load, so unlike the tab's setting
  this one takes a restart.
- **Guarding against play-in-raid is now this mod's job.** The old design got that for
  free, since the Rest Space does not exist on a raid map. A main-menu button is not
  reachable mid-raid either, but nothing enforces that any more -- so whatever opens
  the panel must check, rather than relying on where it was placed.
- **The panel floats over a dimmed hideout**, not a fullscreen takeover. This makes
  freeing the cursor and swallowing player input a hard requirement. Fallback if
  that proves impractical: takeover, which is how EFT presents its own area screens.
- **No hotkey.** A key would be reachable from anywhere, including a raid. The menu
  button is the only way in, and it must still check rather than assume.
- **Valuables are staked through EFT's own grid component**, dragged into a
  container. One item type per bet: a mixed stake has no coherent payout.
- **Per-hand settlement, straight to the stash.** No session, no chips, no buy-in.
  Mail only when the stash cannot take the winnings.
- **Naturals pay 3:2 in currency, even money in valuables.** One bitcoin at 3:2
  settles on half a coin, which cannot exist. The rate is a per-round argument to
  `Deal`, because one shoe serves every currency.
- Design mockups: [panel states](https://claude.ai/code/artifact/99573205-77e3-4c7e-860d-d4a10e713fb3),
  [opening the table](https://claude.ai/code/artifact/f5f210b0-1748-4b56-a766-da4f4fcf0ad6).

## Conventions

- **Comments explain why, not what** -- ideally naming the failure the code
  prevents. The codebase is deliberately heavy on rationale.
- Prose in comments uses `--`, not em dashes.
- Tests are named as the rule they pin, not the method they call.
- Every tunable that a player might argue about lives in `Rules` or `WalletInfo`.

## Verifying

There is no game here, so:

```
dotnet test                                    # 103 tests, no SPT needed
dotnet run --project tools/Blackjack.Console   # play a hand in the terminal
```

**Distrust a suite that passes first time.** The money tests were mutation-checked:
collecting the full stake instead of the increase, and paying out on losing hands,
each fail 7 tests. Do that again after changing anything that moves currency.

`MoneyInvariantTests` plays 400 random rounds and checks, after each, that the money
moved equals the profit the engine reported. An end-of-session balance check would
miss errors that cancel.

On a machine with SPT, `scripts\smoke.ps1 -SessionId <id> -PingOnly` first. It
touches no money and proves the mod loaded, the route is reachable, the session
resolved and the profile can be read.

## Releasing

`releases/Blackjack_V<ver>.zip`, holding both halves and laid out to extract straight
into an SPT folder:

```
SPT_Runtime/user/mods/Blackjack/    Blackjack.Server.dll + .pdb, Blackjack.Game.dll + .pdb, config.json
BepInEx/plugins/Blackjack/          Blackjack.Client.dll, table.png, cards/*.png
README.txt
```

**`SPT_Runtime/` is part of the path**, not the folder you extract into -- the server
mod lives under it in a 4.x install. Dropping that prefix produces a zip that looks
right and installs nothing.

The version lives in **four** places and they must agree: `Blackjack.Server.csproj`
`<Version>`, `ModMetadata.Version`, `Blackjack.Client.csproj` `<Version>` and
`BlackjackClientPlugin.PluginVersion`. The Forge rejects an upload whose two halves
disagree about the GUID; a version they disagree about is the same kind of trap.

Pack with `System.IO.Compression` -- `Compress-Archive` writes backslash entries,
which extract on Linux as one file with slashes in its name. `scripts/pack.ps1` does
it correctly and rebuilds Release first.

SPT's own assemblies are not bundled -- the server provides them. Symbols ship for
the server half, deliberately, because so much of it has still only run once.

---

## Current state

**Update this section as work completes.**

- Working branch **`test`**, level with `main`.
- **1.1.1 is the current build**: `releases/Blackjack_V1.1.1.zip`, both halves in one
  zip. It is 1.1.0 plus the tab and icon sizing below, and it is **a test build --
  installed and not yet looked at.** 1.1.0 is the last one anybody else has.
- **The task-bar tab has now been seen**, on the home box with Poker installed
  alongside, and it came out about twice the width of the game's own tabs -- the pip
  sizing itself from its own sprite. The same number made the menu button's icon pull
  apart on hover. Fixed, built and deployed to `H:\SPT4.1.X`; **the fix itself has not
  been seen.** See "The pip is 160 units wide".
- Server mod is feature-complete: rules, six wallets, money, stats, escrow, logging,
  both transports. **111 tests green** (52 engine, 59 money).
- Client plugin exists and works: the panel, the table art, the card faces, and the tab
  on the bottom bar, which is now the way in. The main-menu button is still there and
  off by default -- see the entry-point note above.
- **Money moves correctly, for real.** Hands dealt, played and settled against a real
  profile in both directions, including doubles and splits, each landing on the exact
  expected balance with escrow empty afterwards.
- **Untested still:** valuables (bitcoin and Lega are at zero in the test profile),
  the full-stash shortfall-to-mail path, a restart mid-round, and the tab-sizing fix.

### Testing on Joel's box

Profile `6a8cd3a7e0b8272790f41285` ("test", level 69) is the sandbox -- roughly
499M roubles, 500M dollars, 500M euros, 5,000 GP coins. The other profile,
`6a7501c247d2e12a3892aaee` ("SCOOP", level 16), is the real one; leave it alone.

**Bitcoin and Lega medals are both at zero there**, so the two wallets with a
`StackMaxSize` of 1 -- the riskiest payout path, one item per coin -- cannot be
exercised by betting until some are added.

### Open items

- ~~**The task-bar tab compiles but has never run.**~~ It runs, and BLACKJACK is on
  the bar beside MAIN MENU and HIDEOUT with Poker's tab next to it. What that first
  sighting found was the icon sizing itself from its own sprite -- see "The pip is 160
  units wide". **Fixed, packed as 1.1.1 and not yet looked at.** 1.1.0 is what anybody
  else has, and it still has the oversized tab; whether 1.1.1 goes out is a separate
  decision from having built it.
- **The tab closes the table when a raid starts.** The panel's canvas is
  `DontDestroyOnLoad`, so nothing else would. It matters most in co-op, where the raid
  is started by the host and a player can be pulled out of the lobby with the table
  open.

- **Which install the client is built against matters.** 4.1.3's `PluginValidator`
  reads a plugin's references to `spt-*` and requires a major.minor match, and the
  game half has to match too: `C:\HUH` is EFT 0.16.9.5 and is not the same build as
  `H:\SPT4.1.X`. If a build made on one machine misbehaves on the other, rebuild it
  there -- `dotnet build src/Blackjack.Client -p:SPTPath=<install>`.
- **`smoke.ps1` works** against a real server, as of the first run on Joel's box.
  The PHPSESSID assumption was right; three things around it were wrong. See
  "Talking to the server without a game client".
- **Make the wire enums strings.** The client hardcodes the integers today. See the
  note above.
- **Mail attachments are unverified** -- SPT may expect `ParentId`/`SlotId` set on
  them in ways not checked here.
- **`ExtensionData` serialisation is unverified**, as is whether the client accepts
  an unfamiliar action name on the item-event endpoint.
- **The panel mockup is stale**: it still shows an odd-stake warning from a payout
  rule that was abandoned.
- Undecided: whether a settled round reads in the strip above the buttons or as an
  overlay across the felt.
