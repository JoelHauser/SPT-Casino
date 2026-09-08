Drop a .wav or .ogg file in this folder, named exactly as below, and it plays --
no rebuild, no code change. SoundBoard reads from disk at request time, the same
way CardView reads table.png and the card faces. A name with no file yet is
silent, not broken; the table plays correctly either way.

  card-deal.wav             a card sliding in -- Poker's hole cards and the
                             board, Blackjack's dealer and hand, one card at a
                             time, already staggered by the deal itself
  card-flip.wav             a card already on the table turning face up --
                             Poker's showdown, Blackjack's hole card

  chip-bet.wav               a wager committed -- Blackjack's bet confirmed,
                              Poker's Bet / Call / Raise / All-in
  chip-place.wav             a chip landing on Roulette's felt

  roulette-spin-start.wav    the wheel and ball setting off
  roulette-ball-land.wav     the ball settling into its pocket

  slot-reel-spin.wav         all five reels starting together
  slot-reel-stop.wav         one reel settling -- fires once per reel,
                              staggered the way the reels themselves are

  slot-win.wav                WIN         (multiple >= 1)
  slot-win-big.wav             BIG WIN     (multiple >= 5)
  slot-win-huge.wav            HUGE WIN    (multiple >= 20)
  slot-win-jackpot.wav         JACKPOT     (multiple >= 100)

Both extensions are checked for every name -- a .wav is tried first, then .ogg.
Only one is needed.

This list is Casino.Shared.Cue, in Casino.Shared/SoundBoard.cs. If a new cue is
added there, add its file name to this list too -- the two are meant to agree,
and nothing enforces that automatically.

What is NOT wired yet, as of this being written: bot-driven bets in Poker (a
sound plays when the human player bets, not when a seat-mate does -- that needs
diffing server state the way dealt cards are, which chips do not do yet), and
Blackjack's Double/Split (both take more money mid-hand; only the initial bet
plays a sound today). Roulette's Lift (taking a chip back off the felt) has no
cue either. All are reasonable next additions, not oversights being hidden.
