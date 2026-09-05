Blackjack {VERSION} -- a card table for Escape from Tarkov
==========================================================

Built for SPT 4.1.3. The server half refuses to load outside 4.1.x.

INSTALL
  Extract into your SPT folder -- the one with SPT.Server.exe in it -- so the
  files land in:

      <SPT>\SPT_Runtime\user\mods\Blackjack\
      <SPT>\BepInEx\plugins\Blackjack\

  Both halves are needed. The server owns the game: it shuffles, deals, decides
  and moves the money. The plugin draws the table and sends what you asked for.

PLAYING
  BLACKJACK on the bar along the bottom of the menu, beside HIDEOUT. It is
  there on every screen out of raid, so the table opens from the hideout or
  the flea market without backing out first. It greys out with the rest of the
  bar once you are loading into a raid.

  Escape closes the table, and only the table -- the screen behind it stays put.

  Stake roubles, dollars, euros, GP coins, bitcoin or Lega medals. The table
  takes up to 500,000 roubles a hand, 5,000 dollars or euros, 50 GP, 10 bitcoin
  or 5 Lega. Naturals pay 3:2 in currency and even money in valuables, because
  half a bitcoin is not a thing.

  Winnings go straight to your stash, or to your mail if the stash is full.

SETTINGS (F12, in game)
  Enforce maximum bet     the table's ceiling. Off means bet what you carry.
  Show the task-bar tab   the bottom-bar tab, on by default.
  Put the tab on the right  moves it in with CHARACTER instead of HIDEOUT.

FIRST RUN
  Start the server and look for:

      [Blackjack] v{VERSION} loaded -- built for SPT ~4.1.3

  No banner means the mod was rejected before it ran, which is almost always the
  SPT version gate. Nothing else works until it appears.

  In game, BepInEx/LogOutput.log carries every client line, each prefixed
  [Blackjack].

WHAT IS NEW IN 1.1.2
  The tab greys out while a raid loads, in step with every other tab on the bar,
  instead of staying lit on the deployment screen as though the table were still
  open for business. It was never actually playable there -- the table closes at
  the first sign of a raid -- so this is about the tab telling the truth.

  If the tab is ever missing, misplaced or dead, BepInEx/LogOutput.log says which
  tabs it found and what it did with them -- that is the thing worth reporting.

LOGGING
  config.json turns the server's verbose logging off once things work. Leave it
  on while you are still watching money move -- it logs every request and every
  rouble.

  https://github.com/JoelHauser/Blackjack
