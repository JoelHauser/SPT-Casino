namespace Roulette.Game;

/// <summary>
/// The kinds of bet the layout offers.
///
/// Named for what they are called at a table rather than for how many numbers they
/// cover, because that is what the client has to label them and what a player asks
/// for.
/// </summary>
public enum BetKind
{
    /// <summary>One number. 35 to 1.</summary>
    Straight,

    /// <summary>Two numbers sharing an edge. 17 to 1.</summary>
    Split,

    /// <summary>A row of three. 11 to 1.</summary>
    Street,

    /// <summary>A block of four. 8 to 1.</summary>
    Corner,

    /// <summary>Two rows, six numbers. 5 to 1.</summary>
    SixLine,

    /// <summary>One of the three columns of twelve. 2 to 1.</summary>
    Column,

    /// <summary>1-12, 13-24 or 25-36. 2 to 1.</summary>
    Dozen,

    /// <summary>Eighteen numbers. Even money.</summary>
    Red,

    /// <summary>Eighteen numbers. Even money.</summary>
    Black,

    /// <summary>Odd numbers. Even money, and zero is not odd.</summary>
    Odd,

    /// <summary>Even numbers. Even money, and zero is not even.</summary>
    Even,

    /// <summary>1-18. Even money.</summary>
    Low,

    /// <summary>19-36. Even money.</summary>
    High,

    /// <summary>
    /// 0, 00, 1, 2 and 3 on an American wheel. 6 to 1, which is the one bet on
    /// either wheel that pays worse than the rest -- 7.89% against the house's
    /// usual 5.26%.
    /// </summary>
    TopLine,
}

/// <summary>
/// One bet: what kind, what it covers, and how much is on it.
///
/// <see cref="Selection"/> means different things per kind and that is deliberate --
/// a straight-up bet needs a number, a dozen needs which dozen, and red needs
/// nothing. Carrying one int rather than a shape per kind keeps the wire simple, and
/// <see cref="RouletteRules.Validate"/> is the single place that says what a
/// selection may be.
/// </summary>
/// <param name="Kind">Which bet.</param>
/// <param name="Selection">
/// For <see cref="BetKind.Straight"/>, the number. For <see cref="BetKind.Column"/>
/// and <see cref="BetKind.Dozen"/>, which one, 1 to 3. For
/// <see cref="BetKind.Street"/>, <see cref="BetKind.Corner"/> and
/// <see cref="BetKind.SixLine"/>, the lowest number the bet covers, which on that
/// grid fixes the shape. For <see cref="BetKind.Split"/> it is an **index into
/// <see cref="Layout.Splits"/>**, because a lowest number does not fix a split.
/// Ignored by the even-money bets and the top line.
/// </param>
/// <param name="Amount">Chips staked. Always positive; a zero bet is not a bet.</param>
public sealed record Bet(BetKind Kind, int Selection, int Amount)
{
    /// <summary>
    /// Every number this bet covers, on the wheel it was placed on.
    ///
    /// Computed rather than stored, so the layout cannot drift out of step with the
    /// payouts. The betting cloth is a 3-by-12 grid running 1..36 in columns of
    /// three, and every positional bet below is that grid read a different way.
    /// </summary>
    public IEnumerable<int> Covers(Wheel wheel)
    {
        switch (Kind)
        {
            case BetKind.Straight:
                yield return Selection;
                break;

            case BetKind.Split:
                // An index into Layout.Splits, not a number: "the split on 1" is
                // ambiguous between 1-2 and 1-4. See Layout.
                var split = Layout.Splits[Selection];
                yield return split.Low;
                yield return split.High;
                break;

            case BetKind.Street:
                for (var i = 0; i < 3; i++)
                {
                    yield return Selection + i;
                }

                break;

            case BetKind.Corner:
                yield return Selection;
                yield return Selection + 1;
                yield return Selection + 3;
                yield return Selection + 4;
                break;

            case BetKind.SixLine:
                for (var i = 0; i < 6; i++)
                {
                    yield return Selection + i;
                }

                break;

            case BetKind.Column:
                // Columns run across the cloth: 1, 4, 7 ... then 2, 5, 8 ...
                for (var n = Selection; n <= 36; n += 3)
                {
                    yield return n;
                }

                break;

            case BetKind.Dozen:
                for (var i = 0; i < 12; i++)
                {
                    yield return ((Selection - 1) * 12) + 1 + i;
                }

                break;

            case BetKind.Red:
            case BetKind.Black:
                var wanted = Kind == BetKind.Red ? PocketColour.Red : PocketColour.Black;
                for (var n = 1; n <= 36; n++)
                {
                    if (Wheel.ColourOf(n) == wanted)
                    {
                        yield return n;
                    }
                }

                break;

            case BetKind.Odd:
            case BetKind.Even:
                var odd = Kind == BetKind.Odd;
                for (var n = 1; n <= 36; n++)
                {
                    if (n % 2 == 1 == odd)
                    {
                        yield return n;
                    }
                }

                break;

            case BetKind.Low:
                for (var n = 1; n <= 18; n++)
                {
                    yield return n;
                }

                break;

            case BetKind.High:
                for (var n = 19; n <= 36; n++)
                {
                    yield return n;
                }

                break;

            case BetKind.TopLine:
                yield return 0;
                if (wheel.Kind == WheelKind.American)
                {
                    yield return Pocket.DoubleZero;
                }

                yield return 1;
                yield return 2;
                yield return 3;
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(Kind), Kind, "Unknown bet.");
        }
    }

    public bool Wins(Wheel wheel, Pocket result) => Covers(wheel).Contains(result.Number);
}

/// <summary>
/// What each bet pays, and the arithmetic that follows from it.
///
/// **These are "to one", the way a table states them.** A winning straight-up bet of
/// 10,000 gets 350,000 in winnings *and its 10,000 back*. Paying 35 times the stake
/// and keeping the stake is the classic off-by-one here, and it is worth six percent
/// of every payout -- large enough to matter and small enough to look like rounding.
/// </summary>
public static class Payouts
{
    public static int ToOne(BetKind kind) => kind switch
    {
        BetKind.Straight => 35,
        BetKind.Split => 17,
        BetKind.Street => 11,
        BetKind.Corner => 8,
        BetKind.SixLine => 5,
        BetKind.TopLine => 6,
        BetKind.Column or BetKind.Dozen => 2,
        BetKind.Red or BetKind.Black or BetKind.Odd or BetKind.Even
            or BetKind.Low or BetKind.High => 1,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown bet."),
    };

    /// <summary>
    /// What comes back to the player: the winnings plus the stake they already put
    /// up. A loser returns nothing at all, stake included.
    /// </summary>
    public static int Returned(Bet bet, Wheel wheel, Pocket result) =>
        bet.Wins(wheel, result) ? bet.Amount * (ToOne(bet.Kind) + 1) : 0;

    /// <summary>
    /// What the player is up or down on this bet, which is what the stash actually
    /// moves by once the stake has already been taken.
    /// </summary>
    public static int Profit(Bet bet, Wheel wheel, Pocket result) =>
        Returned(bet, wheel, result) - bet.Amount;
}
