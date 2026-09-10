# SPT Casino 1.2.61

**2026-09-10.** Slots gets a balance readout, next to STATS and CLOSE -- what the
selected wallet currently holds, in whichever currency is staked. A player asked for
it: the panel covers the stash counter, and there was nowhere on it to check what you
had left to spend.

`PingResponse.Balances` has existed since the first version, read straight off
`IBank` by `SlotService.Ping` -- Blackjack and Roulette already showed their own, the
panel here simply never looked at it. Read off the ping already fetched on open, and a
fresh one after every settle rather than stake-minus-paid worked out on the client,
since a win big enough to overflow the stash posts the rest as mail and only the
server knows what actually landed. Switching currency redraws the same cached figures
rather than asking again.

No change to odds, payouts, or anything else about the tables.
