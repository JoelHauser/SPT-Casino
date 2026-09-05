namespace Poker.Game.Tests;

/// <summary>
/// The whole table, played correctly, measured.
///
/// This is the only test that exercises settlement the way a player would -- every
/// street, every outcome, in the proportions the deck actually produces. A pinned
/// hand proves one path; this proves the paths add up.
///
/// **What this cannot do is confirm the house edge**, and it is worth writing down
/// why, because the attempt looks obvious and wastes an afternoon.
///
/// Ultimate Texas Hold'em has a standard deviation near 4.9 antes a hand. Confirming
/// a 2.185% edge to within a tenth of a point needs a standard error near 0.03 antes,
/// which is nine million hands. A hundred thousand hands -- already three and a half
/// minutes -- gives a 95% interval six points wide, which cannot tell a correct
/// settlement from one losing three points somewhere.
///
/// So the assertions here are the low-variance ones. The **decision mix** is a
/// proportion, not a payoff: at three thousand hands its standard error is under a
/// point, so it pins the strategy tightly and cheaply. The **standard deviation**
/// pins the shape of the payouts -- get a paytable wrong and it moves. The edge
/// itself is checked only for gross inversion.
/// </summary>
public class HouseEdgeTests
{
    /// <summary>Plays one hand by the book, and says where it ended.</summary>
    private static string PlayCorrectly(UltimateHoldemTable table, int ante, out double net)
    {
        table.Deal(ante);
        var ended = "fold";

        while (table.CurrentStreet is { } street)
        {
            switch (street)
            {
                case Street.PreFlop:
                    if (UthStrategy.RaisesOnHoleCards(table.Player.Cards[0], table.Player.Cards[1]))
                    {
                        ended = "4x";
                        table.Play(4);
                    }
                    else
                    {
                        table.Check();
                    }

                    break;

                case Street.Flop:
                    if (UthStrategy.RaisesOnFlop(table.Player.Cards, table.Community))
                    {
                        ended = "2x";
                        table.Play(2);
                    }
                    else
                    {
                        table.Check();
                    }

                    break;

                default:
                    if (UthStrategy.BetsOnRiver(table.Player.Cards, table.Community))
                    {
                        ended = "1x";
                        table.Play(1);
                    }
                    else
                    {
                        table.Fold();
                    }

                    break;
            }
        }

        net = table.Player.Net / (double)ante;
        return ended;
    }

    [Fact]
    public void CorrectPlayBetsAtThePublishedFrequencies()
    {
        // These four numbers are the strategy's fingerprint, and they are the reason
        // the river rule was caught being wrong: it folded 26% where the real game
        // folds about 19%, and every one of those folds threw away two antes.
        const int hands = 3_000;
        const int ante = 100;

        var table = new UltimateHoldemTable(rng: new Random(20260901));
        var mix = new Dictionary<string, int>();

        double sum = 0;
        double sumOfSquares = 0;

        for (var hand = 0; hand < hands; hand++)
        {
            var ended = PlayCorrectly(table, ante, out var net);
            mix[ended] = mix.GetValueOrDefault(ended) + 1;
            sum += net;
            sumOfSquares += net * net;
        }

        double Share(string key) => mix.GetValueOrDefault(key) / (double)hands;

        // Published frequencies for correct play, with room for the sampling error
        // at this many hands and for the strategy being the simple one.
        Assert.InRange(Share("4x"), 0.34, 0.44);
        Assert.InRange(Share("2x"), 0.10, 0.18);
        Assert.InRange(Share("1x"), 0.24, 0.34);
        Assert.InRange(Share("fold"), 0.15, 0.24);

        // The average hand risks a little over four antes. Wrong Play sizes move
        // this immediately.
        var wagered = (Share("4x") * 6) + (Share("2x") * 4) + (Share("1x") * 3) + (Share("fold") * 2);
        Assert.InRange(wagered, 3.9, 4.4);

        var mean = sum / hands;
        var sd = Math.Sqrt((sumOfSquares / hands) - (mean * mean));

        // Near 4.9 over a long run, but the band has to be wide: the spread is
        // driven by payouts nobody sees often. A royal turns up about once in
        // thirty thousand hands, so three thousand of them usually contain none and
        // the figure comes in low. It is a smoke check on the paytable, not a
        // measurement.
        Assert.InRange(sd, 3.4, 6.0);

        // Gross-inversion guard only. The interval at this many hands is far too
        // wide to say anything finer, and pretending otherwise would be worse than
        // not testing it -- see the note on this class.
        Assert.InRange(-mean, -0.15, 0.25);
    }
}
