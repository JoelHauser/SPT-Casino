namespace Roulette.Game;

/// <summary>
/// One pocket, as the client draws it.
///
/// **The client builds its wheel from this list rather than from a table of its
/// own.** A second copy of the pocket order is a second thing to keep in step, and
/// when it drifts the ball lands on a number that did not win -- which reads as a
/// payout bug rather than a drawing one. Sending 37 short records costs nothing and
/// makes disagreement impossible.
/// </summary>
public sealed record PocketView(int Number, string Label, string Colour);

/// <summary>One bet on the cloth.</summary>
public sealed record BetView(string Kind, int Selection, int Amount, string Description);

/// <summary>One split, and the two numbers it covers.</summary>
public sealed record SplitView(int Low, int High);

/// <summary>
/// Every spot a chip may go on, so the client can draw a cloth that offers exactly the
/// bets the server accepts.
///
/// **Sent rather than duplicated**, for the same reason the pockets are. A second copy
/// of this list in the client is a second thing to keep in step, and when it drifts the
/// cloth offers a bet the server refuses -- or worse, one it accepts as a different bet.
/// Splits especially: a split is placed by its **index in this list**, so a client
/// enumerating its own would be sending indices into a list nobody else has.
/// </summary>
public sealed record LayoutView(
    IReadOnlyList<SplitView> Splits,
    IReadOnlyList<int> Streets,
    IReadOnlyList<int> Corners,
    IReadOnlyList<int> SixLines)
{
    public static LayoutView Of() => new(
        [.. Layout.Splits.Select(s => new SplitView(s.Low, s.High))],
        Layout.Streets,
        Layout.Corners,
        Layout.SixLines);
}

/// <summary>What one bet did on the spin that just settled.</summary>
public sealed record OutcomeView(string Description, int Amount, bool Won, int Returned);

/// <summary>
/// The spin that just happened.
/// </summary>
/// <param name="Position">
/// Where the winning pocket sits on the wheel, clockwise from the single zero. **This
/// is what the client spins to.** The wheel is not in numerical order, so the number
/// alone does not say where to stop.
/// </param>
public sealed record SpinView(
    int Number,
    string Label,
    string Colour,
    int Position,
    int Staked,
    int Returned,
    int Profit,
    IReadOnlyList<OutcomeView> Outcomes);

/// <summary>
/// The whole table, as the client is allowed to see it.
///
/// Everything on the wire is a string or an int on purpose. SPT registers
/// `EftEnumConverterFactory` into `options.Converters`, which outranks a
/// `[JsonConverter]` declared on an enum type, so enums serialise as integers unless
/// every property carrying one is attributed. Both sibling mods were caught by that.
/// Not putting an enum on the wire at all sidesteps it entirely.
/// </summary>
public sealed record TableView(
    string Phase,
    string Wheel,
    IReadOnlyList<PocketView> Pockets,
    LayoutView Layout,
    IReadOnlyList<BetView> Bets,
    int Staked,
    int MinBet,
    int MaxTotalStake,
    SpinView? Last)
{
    public static TableView Of(RouletteTable table) => new(
        table.Phase.ToString(),
        table.Wheel.Kind.ToString(),
        [.. table.Wheel.Pockets.Select(p => new PocketView(p.Number, p.Label, p.Colour.ToString()))],
        LayoutView.Of(),
        [.. table.Bets.Select(Describe)],
        table.Staked,
        table.Rules.MinBet,
        table.Rules.MaxTotalStake,
        table.Last is null ? null : Describe(table.Last));

    private static BetView Describe(Bet bet) =>
        new(bet.Kind.ToString(), bet.Selection, bet.Amount, RouletteTable.Describe(bet));

    private static SpinView Describe(SpinResult spin) => new(
        spin.Result.Number,
        spin.Result.Label,
        spin.Result.Colour.ToString(),
        spin.Position,
        spin.Staked,
        spin.Returned,
        spin.Profit,
        [
            .. spin.Outcomes.Select(o =>
                new OutcomeView(RouletteTable.Describe(o.Bet), o.Bet.Amount, o.Won, o.Returned)),
        ]);
}
