namespace Poker.Game;

/// <summary>
/// A bot. One per seat, with its own <see cref="PokerPersonality"/> and its own RNG.
///
/// The decision is the same one a person makes, in the same order: work out how often
/// the hand wins, work out what the call costs, and let temperament settle the rest.
/// Everything a real player weighs is in here somewhere --
///
/// - **how often the hand wins**, from <see cref="HandEquity"/>, which already
///   accounts for how many opponents are still in;
/// - **the price**, as pot odds -- what the call costs against what is out there to
///   win, which is the arithmetic anchor the whole thing hangs off;
/// - **position**, because acting last is worth real money and weak players ignore it;
/// - **how much is already in**, because chips in the pot change what folding costs;
/// - **stack depth**, because a short stack has one decision and a deep one has four;
/// - **the street**, because a bluff on the river is a different animal from a raise
///   before the flop.
///
/// Every one of those is weighted by the personality rather than replaced by it.
/// There is one procedure and eight sets of dials, which is what makes a seat's
/// behaviour explicable when it does something strange -- and the log says which
/// factor decided it.
/// </summary>
public sealed class BotAgent : IPokerAgent
{
    private readonly PokerPersonality _base;
    private readonly Random _rng;
    private readonly IGameLog _log;
    private readonly int _samples;

    /// <summary>
    /// Where the seat's head is, from -1 (rattled) through 0 (level) to +1 (running
    /// hot). Moves with results and decays back towards level on its own.
    /// </summary>
    private double _mood;

    private int _losingStreak;

    public BotAgent(
        PokerPersonality? personality = null,
        Random? rng = null,
        IGameLog? log = null,
        int samples = HandEquity.DefaultSamples)
    {
        _base = personality ?? PokerPersonality.Balanced;
        _rng = rng ?? new Random();
        _log = log ?? GameLog.Null;
        _samples = samples;
    }

    /// <summary>Who this seat is when it sits down, before anything has happened to it.</summary>
    public PokerPersonality Personality => _base;

    /// <summary>How rattled or how hot, -1 to +1.</summary>
    public double Mood => _mood;

    public int LosingStreak => _losingStreak;

    /// <summary>
    /// Who this seat is **right now**, which is the base character bent by how the
    /// night is going.
    ///
    /// Direction falls out of <see cref="PokerPersonality.Risk"/> rather than being a
    /// dial of its own, because the two real reactions to losing are the two ends of
    /// it. Gamblers steam: they loosen up, start swinging, and try to get it back in
    /// one hand. Careful players shut down and wait. Both are things you can watch
    /// happen at a table, and a bot that did neither would be the one thing no player
    /// ever is -- unchanged by the last hour.
    /// </summary>
    public PokerPersonality Current
    {
        get
        {
            // Steadiness is how little any of this reaches them. At 1 the seat plays
            // its thousandth hand exactly as it played its first.
            var swing = _mood * (1 - _base.Steadiness);

            if (Math.Abs(swing) < 0.001)
            {
                return _base;
            }

            // Losing and winning are not mirror images of each other, and modelling
            // them as one signed number got it backwards: it had a gambler running hot
            // becoming *careful*, which is the one thing a gambler never does.
            if (swing < 0)
            {
                var hurt = -swing;

                // A steamer loosens and swings harder to get it back. A careful player
                // does the opposite and shuts down. Which one a seat does falls out of
                // whether it is a gambler, and both are things you can watch happen.
                return _base.Risk >= 0.5
                    ? _base.Shift(
                        tightness: -hurt * 0.28,
                        aggression: hurt * 0.26,
                        bluff: hurt * 0.20,
                        risk: hurt * 0.15)
                    : _base.Shift(
                        tightness: hurt * 0.26,
                        aggression: -hurt * 0.18,
                        bluff: -hurt * 0.12,
                        risk: -hurt * 0.15);
            }

            // Winning makes everybody a little bolder, whoever they are. Confidence
            // is not a personality type.
            return _base.Shift(
                tightness: -swing * 0.12,
                aggression: swing * 0.14,
                bluff: swing * 0.10);
        }
    }

    /// <summary>
    /// Takes the result of a hand and moves the seat's head accordingly.
    ///
    /// Scaled by what the hand cost as a share of a full stack, so losing a whole
    /// buy-in registers and losing a blind does not. Folding pre-flop for a small
    /// loss barely counts, which is right -- nobody tilts off a folded small blind.
    /// </summary>
    public void HandEnded(HandOutcome outcome)
    {
        var scale = Math.Clamp(outcome.Net / (double)Math.Max(1, outcome.BuyIn), -1, 1);

        if (outcome.Net < 0)
        {
            // A hand given up cheaply is not a beat. A stack lost at showdown is.
            _losingStreak += outcome.Folded && Math.Abs(scale) < 0.1 ? 0 : 1;
        }
        else if (outcome.Net > 0)
        {
            _losingStreak = 0;
        }

        // Decays towards level first, so a seat that has been left alone for a while
        // comes back to itself rather than carrying an hour-old bad beat forever.
        _mood *= 0.82;
        _mood += scale * 1.4;

        // A run of losses weighs more than any single one. Three in a row is what
        // makes a person start playing differently, not the arithmetic.
        _mood -= _losingStreak >= 3 ? 0.12 : 0;

        _mood = Math.Clamp(_mood, -1, 1);

        if (_log.Enabled && Math.Abs(_mood) > 0.25)
        {
            _log.Write(
                $"  {_base.Name} is {(_mood < 0 ? "rattled" : "running hot")} "
                + $"({_mood:F2}, {_losingStreak} lost in a row)");
        }
    }

    public HoldemDecision Decide(PokerContext context)
    {
        var personality = Current;
        var options = context.Options;
        var seat = context.Seat;
        var opponents = Math.Max(1, context.Opponents.Count(other => !other.Folded));

        // Every die is rolled here, in a fixed order, before any of them is consulted.
        // Short-circuiting would make the number of draws depend on the decision, and
        // a seeded table would then play differently the moment a dial was retuned --
        // which quietly turns every pinned multi-seat test into a liar.
        var willRaise = _rng.NextDouble();
        var willBluff = _rng.NextDouble();
        var sizing = _rng.NextDouble();
        var slowPlay = _rng.NextDouble();

        var equity = HandEquity.Estimate(seat.Cards, context.Community, opponents, _rng, _samples, _log);

        var toCall = options.ToCall;
        var pot = Math.Max(1, context.Pot);

        // The price of continuing, as the share of the eventual pot this call buys.
        // Equity above this number is a call that makes money; below it is one that
        // does not. Everything else here is an adjustment to one side or the other.
        var price = toCall == 0 ? 0.0 : toCall / (double)(pot + toCall);

        // Acting last is worth something real, and how much a seat believes that is
        // one of the clearest differences between a good player and a weak one.
        var position = personality.Positional * (context.SeatsToActAfter == 0 ? 0.05 : -0.04);

        // Chips already in are gone whatever happens next, but they do change what is
        // being played for -- which is why a committed stack calls hands it would
        // never have opened.
        var invested = seat.CommittedThisHand / (double)Math.Max(1, seat.CommittedThisHand + seat.Stack);
        var commitment = invested * 0.12 * (0.5 + personality.Risk);

        var strength = Math.Clamp(equity + position + commitment, 0, 1);

        // Tightness is the margin demanded over the price. A rock wants a wide one; a
        // calling station will take almost any number.
        //
        // The span here is what separates the characters, and it was originally too
        // narrow to see: a rock and a calling station folded at almost the same rate,
        // which is not two players, it is one player with two names.
        var callBar = price + 0.01 + (personality.Tightness * 0.18);

        // The bar for putting money in rather than merely matching it. Aggression
        // lowers it, which is exactly what aggression is.
        // The span matters as much as the midpoint. At a narrower one the bar was
        // the binding constraint for everybody in the middle, so a merely ordinary
        // player raised as rarely as a calling station and the two were the same
        // seat wearing different names.
        var raiseBar = 0.60 + (personality.Tightness * 0.10) - (personality.Aggression * 0.30);

        // How often it takes a raise it qualifies for. Nearly the whole range, so a
        // passive seat almost never puts money in of its own accord and a maniac
        // almost always does -- a floor of a quarter made even the calling station
        // raise once every three chances.
        var raises = willRaise < 0.05 + (0.9 * personality.Aggression);

        // Bluffing into a crowd does not work and real players know it, so the
        // frequency falls off sharply with every extra opponent left to get through.
        var bluffs = willBluff < personality.Bluff * Math.Pow(0.55, opponents - 1);

        var bigBlinds = seat.Stack / (double)Math.Max(1, context.Rules.BigBlind);

        var decision = Choose();

        if (_log.Enabled)
        {
            _log.Write(
                $"  {seat.Name} ({personality.Name}): equity {equity:P0}, "
                + $"price {price:P0}, {(context.SeatsToActAfter == 0 ? "last to act" : "out of position")}, "
                + $"{bigBlinds:F0}bb -> {decision}");
        }

        return decision;

        HoldemDecision Choose()
        {
            // A short stack has one decision left, not four, and playing it in
            // fractions only ever gets the chips in worse. Anyone who has watched a
            // tournament recognises this and the risk dial decides how early a seat
            // starts thinking that way.
            if (bigBlinds <= 6 + (6 * personality.Risk) && strength > 0.45 && CanRaise())
            {
                return HoldemDecision.RaiseTo(options.MaxRaiseTo);
            }

            if (toCall == 0)
            {
                // Nothing to beat: the choice is between building a pot and keeping
                // it small.
                if (strength >= raiseBar && raises && CanRaise())
                {
                    // Very strong and out of position occasionally checks instead, to
                    // let somebody else do the betting. Passive seats do it more.
                    return slowPlay < 0.12 * (1 - personality.Aggression) && strength > 0.75
                        ? HoldemDecision.Check
                        : HoldemDecision.RaiseTo(Size(strength));
                }

                // A hand with nothing that bets anyway. Semi-bluffs -- middling
                // equity with somewhere to go -- are the ones that actually work, so
                // they get a wider licence than a pure bluff.
                var semiBluff = strength is > 0.28 and < 0.50;
                if (CanRaise() && bluffs && (strength < 0.28 || semiBluff))
                {
                    return HoldemDecision.RaiseTo(Size(semiBluff ? 0.55 : 0.40));
                }

                return HoldemDecision.Check;
            }

            if (strength >= raiseBar && raises && CanRaise())
            {
                return slowPlay < 0.10 * (1 - personality.Aggression) && strength > 0.80
                    ? HoldemDecision.Call
                    : HoldemDecision.RaiseTo(Size(strength));
            }

            if (strength >= callBar)
            {
                return HoldemDecision.Call;
            }

            // Folding is the default once the price is wrong, but a seat that bluffs
            // will sometimes raise instead -- and raising as a bluff is far more use
            // than calling as one, since calling can only win by having the best hand.
            if (CanRaise() && bluffs && strength > 0.15)
            {
                return HoldemDecision.RaiseTo(Size(0.5));
            }

            return HoldemDecision.Fold;
        }

        bool CanRaise() => options.Moves.Contains(HoldemMove.Raise);

        // Bets are a fraction of the pot, which is how players think and talk about
        // them. Sizing off the stack or in round numbers is the tell that gives a bot
        // away faster than anything it actually does.
        int Size(double confidence)
        {
            var fractions = new[] { 0.35, 0.55, 0.75, 1.0 };

            var lean = (confidence * 0.5) + (personality.Aggression * 0.35) + (sizing * 0.3);
            var index = Math.Clamp((int)(lean * fractions.Length), 0, fractions.Length - 1);

            var raiseBy = (int)Math.Round(fractions[index] * (pot + toCall));
            var target = seat.CommittedThisStreet + toCall + raiseBy;

            var clamped = Math.Clamp(target, options.MinRaiseTo, options.MaxRaiseTo);

            // Leaving a fraction of a stack behind is the worst of both: it commits
            // the chips without the fold equity of committing them. Push the rest in.
            var behind = options.MaxRaiseTo - clamped;
            return behind > 0 && behind < 0.3 * options.MaxRaiseTo && strength > 0.5
                ? options.MaxRaiseTo
                : clamped;
        }
    }
}
