namespace War.Game;

/// <summary>
/// The house edge, computed rather than measured -- the same discipline as the slot
/// machine's <c>Odds.ReturnToPlayer</c>. A simulation in the tests checks this; it is
/// not where the number comes from.
///
/// ## The whole calculation
///
/// A shoe of <c>n</c> cards holds <c>c</c> of each of the thirteen ranks.
///
/// - **A tie on the deal** is the dealer's card matching the player's from what is
///   left: <c>(c - 1) / (n - 1)</c>. Six decks: 23 / 311.
/// - **Everything that is not a tie is a coin toss.** The player's card and the
///   dealer's are drawn from the same shoe, so higher and lower are equally likely and
///   the main bet's expectation off a tie is exactly zero. All of the edge lives in
///   the ties.
/// - **The war cards** come from the shoe less the two tied cards. The three burned
///   cards are unseen and do not change the chances of what follows them, so the war
///   is a fresh deal from <c>n - 2</c> cards with the tied rank two short. It ties
///   again with probability
///   <c>((c - 2)(c - 3) + 12 c (c - 1)) / ((n - 2)(n - 3))</c>, and is otherwise a
///   coin toss -- and a tie at war goes to the player.
/// - Going to war pays +1 ante (raise wins, ante pushes) or costs 2. Surrendering
///   costs a half.
///
/// The tie bet is simpler still: ten to one on <c>(c - 1) / (n - 1)</c>.
///
/// Every figure is per unit of the bet it belongs to, and negative means the house.
/// </summary>
public static class Odds
{
    /// <summary>The chance the first two cards are the same rank.</summary>
    public static double TieChance(int decks = Rules.Decks)
    {
        var (n, c) = Shoe(decks);
        return (c - 1d) / (n - 1d);
    }

    /// <summary>The chance the war cards tie again, given the deal already tied.</summary>
    public static double WarTieChance(int decks = Rules.Decks)
    {
        var (n, c) = Shoe(decks);
        return (((c - 2d) * (c - 3d)) + (12d * c * (c - 1d))) / ((n - 2d) * (n - 3d));
    }

    /// <summary>What going to war is worth, per ante, once the deal has tied.</summary>
    public static double WarValue(int decks = Rules.Decks)
    {
        var again = WarTieChance(decks);
        var higher = (1d - again) / 2d;
        var winning = higher + again;

        return winning - (2d * higher);
    }

    /// <summary>The main bet's expectation per ante, going to war on every tie.</summary>
    public static double MainBetAlwaysWar(int decks = Rules.Decks) => TieChance(decks) * WarValue(decks);

    /// <summary>The main bet's expectation per ante, surrendering every tie.</summary>
    public static double MainBetAlwaysSurrender(int decks = Rules.Decks) => TieChance(decks) * -0.5d;

    /// <summary>The tie bet's expectation per unit staked on it.</summary>
    public static double TieBet(int decks = Rules.Decks)
    {
        var tie = TieChance(decks);
        return (tie * Rules.TiePays) - (1d - tie);
    }

    /// <summary>
    /// The house edge going to war, as a positive percentage. About 2.88 at six decks,
    /// which is the figure the rules are quoted with.
    /// </summary>
    public static double HouseEdgePercent(int decks = Rules.Decks) => -MainBetAlwaysWar(decks) * 100d;

    private static (int Cards, int PerRank) Shoe(int decks) => (decks * 52, decks * 4);
}
