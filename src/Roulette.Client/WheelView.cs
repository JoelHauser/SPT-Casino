using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Roulette.Client
{
    /// <summary>
    /// The wheel, the ball, and the spin.
    ///
    /// ## Drawn, not photographed
    ///
    /// This began as a photograph with a pocket ring painted over it and was wrong
    /// twice: the photograph had 33 or 34 pockets against the 37 the server settles on,
    /// and the overlay's radii were measured against the image's diameter where they
    /// meant its radius, so the ring came out at double size and burst out of the bowl.
    ///
    /// Everything is drawn now, from the pocket list the server sends. There are 37
    /// pockets because there are 37 pockets, and they are where they are asked to be.
    ///
    /// ## The bowl is still, the head turns
    ///
    /// A real wheel is two pieces. The **bowl** -- the outer rim, the wooden apron and
    /// the track the ball runs on -- never moves. The **head** -- the pockets, the
    /// numbers, the green ring and the cone -- spins inside it. Drawing them as one
    /// picture rotated the track under the ball, so no amount of counter-rotation could
    /// ever have read.
    ///
    /// ## Why the ball looked like it was hitching
    ///
    /// Not the maths. The ball travelled **41 pixels between frames while being 31
    /// pixels wide**, so two consecutive frames never overlapped and the eye saw a row
    /// of separate balls rather than one moving one. Anything that moves more than its
    /// own width per frame strobes, however smooth the function driving it.
    ///
    /// Two rounds of chasing that as a discontinuity found nothing, because there was
    /// nothing to find: a simulation of the exact formulas showed zero direction
    /// reversals. What it did show was the step size. The ball is slower and larger
    /// now, and every constant below is chosen against that ratio -- see
    /// <see cref="StepPerFrame"/>.
    ///
    /// ## Landing is computed, never hoped for
    ///
    /// The ball's position is described **relative to the head**:
    ///
    ///     ballAngle = headAngle + pocketAngle(position) + relative
    ///
    /// where `relative` decays to exactly zero. Early on its decay outruns the head, so
    /// the ball visibly runs the other way; at the end it is zero, so the ball sits in
    /// its pocket and rides round, with no special case to put it there. The decay is
    /// exponential because angular velocity under friction is.
    /// </summary>
    internal static class WheelView
    {
        // ---- geometry, as fractions of the wheel's RADIUS, read from outside in -----
        //
        // Laid out to match a real wheel: gold rim, wooden apron carrying the
        // deflectors, the pocket ring with the numbers inside the pockets, a gold
        // separator, the green inner ring, and the cone with its spider.

        private const float RimInner = 0.955f;
        private const float ApronInner = 0.865f;
        private const float OuterGoldInner = 0.845f;
        private const float PocketInner = 0.640f;
        private const float MidGoldInner = 0.615f;
        private const float GreenInner = 0.470f;
        private const float ConeOuter = 0.445f;

        /// <summary>Where the numbers sit: the middle of a pocket block.</summary>
        private const float NumberRadius = 0.738f;

        /// <summary>Where the ball runs before it drops: the wooden track.</summary>
        private const float TrackRadius = 0.905f;

        /// <summary>
        /// Where it comes to rest: the middle of the green ring.
        ///
        /// **The green ring is the pocket floor, not decoration.** The coloured band
        /// carrying the numbers is the label above it; the slot a ball actually sits in
        /// is the green segment beneath. Resting the ball in the number ring put it a
        /// whole band too far out, sitting on the label rather than in the pocket.
        /// </summary>
        private const float RestRadius = 0.545f;

        /// <summary>
        /// Ball diameter over wheel diameter. A green slot is about 0.046 of the
        /// diameter across, so this very nearly fills one -- which is what a ball in a
        /// pocket looks like -- while still being wider than the distance it covers
        /// between frames.
        /// </summary>
        private const float BallSize = 0.044f;

        // ---- motion -----------------------------------------------------------------
        //
        // Every one of these is rolled fresh for each spin rather than fixed, because a
        // wheel that takes exactly eight seconds and exactly five turns every single
        // time stops being a wheel and becomes a loading bar. Ranges, not constants.
        //
        // **None of it can change where the ball lands.** The pocket is decided by the
        // server before any of this is rolled; these only decide how long it takes to
        // get there and how it looks on the way.

        private const float MinDuration = 6.5f;
        private const float MaxDuration = 9.5f;

        /// <summary>Where the ball leaves the track and starts falling in.</summary>
        private const float MinDrop = 0.55f;
        private const float MaxDrop = 0.68f;

        /// <summary>Friction. Angular velocity decays as e^-kt, so the journey left does too.</summary>
        private const float MinDecay = 2.8f;
        private const float MaxDecay = 3.7f;

        /// <summary>At least this many turns of travel relative to the head.</summary>
        private const float MinTurns = 4.0f;
        private const float MaxTurns = 7.0f;

        private const float MinHeadTurns = 1.5f;
        private const float MaxHeadTurns = 2.8f;

        /// <summary>
        /// The most the ball may move between frames, as a multiple of its own width.
        ///
        /// **This is the ceiling the variance is not allowed to breach.** A ball that
        /// covers more than its own width in a frame never overlaps itself between
        /// frames and reads as a row of separate balls -- which is what "the ball
        /// hitches" turned out to mean, after two rounds of hunting it as a
        /// discontinuity that was never there. Rolling a short duration with a lot of
        /// turns would walk straight back into it, so a rolled profile that would
        /// exceed this has its turns cut until it does not.
        /// </summary>
        private const float MaxStepPerFrame = 0.88f;

        private const int Texture = 1024;

        // ---- palette ----------------------------------------------------------------

        private static readonly Color32 GoldBright = new Color32(226, 194, 118, 255);
        private static readonly Color32 Gold = new Color32(190, 154, 74, 255);
        private static readonly Color32 GoldDeep = new Color32(140, 111, 48, 255);
        private static readonly Color32 Wood = new Color32(101, 68, 42, 255);
        private static readonly Color32 WoodDark = new Color32(66, 43, 26, 255);
        private static readonly Color32 Red = new Color32(178, 30, 38, 255);
        private static readonly Color32 Black = new Color32(24, 23, 25, 255);
        private static readonly Color32 Green = new Color32(30, 150, 68, 255);
        private static readonly Color32 GreenRing = new Color32(38, 150, 78, 255);
        private static readonly Color32 Clear = new Color32(0, 0, 0, 0);

        private static readonly Color Numerals = new Color(0.95f, 0.87f, 0.66f, 1f);

        private static RectTransform _head;
        private static RectTransform _ballPivot;
        private static RectTransform _ball;
        private static TMP_FontAsset _font;

        private static float _diameter;
        private static int _pocketCount = 37;
        private static float _headAngle;

        /// <summary>Where the ball is now, so a new spin launches from it rather than jumping.</summary>
        private static float _ballAngle;

        /// <summary>Which way the last spin went, so the next one goes the other way.</summary>
        private static bool _clockwise;

        private static Coroutine _spinning;

        internal static bool IsSpinning => _spinning != null;

        /// <summary>How one spin differs from the last. Presentation only.</summary>
        private readonly struct Profile
        {
            internal Profile(float duration, float drop, float decay, float turns, float headTurns, float direction)
            {
                Duration = duration;
                Drop = drop;
                Decay = decay;
                Turns = turns;
                HeadTurns = headTurns;
                Direction = direction;
            }

            internal float Duration { get; }

            internal float Drop { get; }

            internal float Decay { get; }

            internal float Turns { get; }

            internal float HeadTurns { get; }

            /// <summary>+1 for a head turning clockwise, -1 for one going the other way.</summary>
            internal float Direction { get; }
        }

        /// <summary>
        /// Rolls a spin.
        ///
        /// The direction alternates rather than being random, because that is what a
        /// croupier does -- the wheel goes the other way each time, and the ball always
        /// opposes it. Random would give the same way twice often enough to look like a
        /// mistake.
        ///
        /// The turns are then cut, if need be, so the ball cannot outrun its own width
        /// between frames. That check is what stops a short duration and a lot of turns
        /// walking back into the strobing this spent three rounds fixing.
        /// </summary>
        private static Profile Roll(float diameter)
        {
            _clockwise = !_clockwise;

            var duration = UnityEngine.Random.Range(MinDuration, MaxDuration);
            var decay = UnityEngine.Random.Range(MinDecay, MaxDecay);
            var headTurns = UnityEngine.Random.Range(MinHeadTurns, MaxHeadTurns);
            var turns = UnityEngine.Random.Range(MinTurns, MaxTurns);

            // No ceiling check here. It used to trim the turns to fit, and it did not
            // work: Run rounds the travel up to a whole number of laps afterwards, which
            // put back most of a lap of whatever had just been trimmed. Sweeping four
            // hundred rolled spins found the ball back over its own width per frame at
            // launch. The check belongs after that rounding, where the real figure is
            // known -- see Run.
            return new Profile(
                duration,
                UnityEngine.Random.Range(MinDrop, MaxDrop),
                decay,
                turns,
                headTurns,
                _clockwise ? 1f : -1f);
        }

        /// <summary>
        /// How far the ball moves between frames at its quickest, as a multiple of its
        /// own width. Over 1 and it strobes.
        /// </summary>
        private static float StepPerFrame(
            float diameter, float duration, float decay, float turns, float headTurns)
        {
            var launch = 360f * turns * decay / (1f - Mathf.Exp(-decay)) / duration;
            var head = 360f * headTurns * 2.4f / duration;

            // What matters is how far the ball crosses the *screen*, not how fast it
            // moves relative to the head. Its screen speed is the relative speed less
            // the head's, because the head is carrying it the other way -- the ball
            // rides the head a little even while running against it.
            var degreesPerFrame = Mathf.Abs(launch - head) / 120f;
            var arc = degreesPerFrame * Mathf.Deg2Rad * TrackRadius * diameter * 0.5f;

            return arc / (BallSize * diameter);
        }

        internal static GameObject Build(
            Transform parent, IReadOnlyList<PocketInfo> pockets, float diameter, TMP_FontAsset font)
        {
            _font = font;
            _diameter = diameter;
            _pocketCount = Mathf.Max(1, pockets.Count);
            _headAngle = 0f;
            _ballAngle = 0f;

            var root = NewImage("Wheel", parent, Clear);
            root.sizeDelta = new Vector2(diameter, diameter);

            // The head goes down first so the bowl's inner edge draws over it, which is
            // what makes the pockets look sunk into the bowl rather than pasted on.
            _head = NewImage("Head", root, Color.white);
            _head.anchorMin = _head.anchorMax = new Vector2(0.5f, 0.5f);
            _head.pivot = new Vector2(0.5f, 0.5f);
            _head.sizeDelta = new Vector2(diameter, diameter);
            _head.GetComponent<Image>().sprite = HeadSprite(pockets);

            BuildNumbers(pockets, diameter);

            var bowl = NewImage("Bowl", root, Color.white);
            bowl.anchorMin = bowl.anchorMax = new Vector2(0.5f, 0.5f);
            bowl.pivot = new Vector2(0.5f, 0.5f);
            bowl.sizeDelta = new Vector2(diameter, diameter);
            bowl.GetComponent<Image>().sprite = BowlSprite();

            BuildBall(root, diameter);
            BuildMarker(root, diameter);

            Apply(0f, 0f, TrackRadius);

            RouletteClientPlugin.Log.LogInfo(
                $"[Roulette] wheel built with {pockets.Count} pockets. Every spin rolls its own "
                + "duration, turns, friction and drop, and alternates direction.");

            return root.gameObject;
        }

        /// <summary>
        /// Spins to a pocket and lands on it. <paramref name="position"/> is the winning
        /// pocket's place on the wheel, clockwise from the single zero.
        /// </summary>
        internal static void Spin(int position, Action onFinished)
        {
            var host = RouletteClientPlugin.Instance;

            if (host == null || _head == null)
            {
                onFinished?.Invoke();
                return;
            }

            if (_spinning != null)
            {
                host.StopCoroutine(_spinning);
            }

            _spinning = host.StartCoroutine(Run(position, onFinished));
        }

        private static IEnumerator Run(int position, Action onFinished)
        {
            var spin = Roll(_diameter);

            var headFrom = _headAngle;
            var headTo = headFrom + (360f * spin.HeadTurns * spin.Direction);
            var landAt = PocketAngle(position);

            // Launch from wherever the ball is sitting rather than from wherever the
            // arithmetic happens to start, so the first frame of a spin is a ball
            // setting off rather than a ball teleporting. Whole turns are added, in the
            // head's own direction, until it is far enough to be a spin.
            //
            // The relative angle carries the head's sign, and that is what makes the
            // ball oppose it: the ball's own velocity is the negative of this term, so
            // a head going one way always has a ball going the other.
            var need = _ballAngle - headFrom - landAt;
            var turns = Mathf.Ceil(((360f * spin.Turns) - (spin.Direction * need)) / 360f);
            var relative = need + (360f * turns * spin.Direction);

            // Now that the real travel is known -- rounding up to a whole lap can add
            // most of another one -- check what it does to the ball's speed on screen
            // and bring it back under its own width per frame.
            //
            // Lengthening the spin is the first lever, because the ball still goes
            // round as many times and merely takes longer about it. But only so far:
            // solving it by time alone produced spins of fourteen seconds, which is no
            // longer a spin, it is a wait. Past the ceiling the laps come off instead,
            // one at a time. Neither can move where the ball lands -- any relative
            // travel works, because it is the decay to zero that does the landing.
            var duration = spin.Duration;
            var ratio = StepPerFrame(
                _diameter, duration, spin.Decay, Mathf.Abs(relative) / 360f, spin.HeadTurns);

            if (ratio > MaxStepPerFrame)
            {
                duration = Mathf.Min(duration * ratio / MaxStepPerFrame, MaxDuration);

                while (Mathf.Abs(relative) > 360f * MinTurns
                       && StepPerFrame(_diameter, duration, spin.Decay,
                              Mathf.Abs(relative) / 360f, spin.HeadTurns) > MaxStepPerFrame)
                {
                    relative -= 360f * spin.Direction;
                }
            }

            var elapsed = 0f;

            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                var t = Mathf.Clamp01(elapsed / duration);

                var head = Mathf.Lerp(headFrom, headTo, EaseOut(t, 2.4f));
                var ball = head + landAt + (relative * Friction(t, spin.Decay))
                           + (Rattle(t, spin.Drop) * spin.Direction);

                Apply(head, ball, Radius(t, spin.Drop));

                yield return null;
            }

            _headAngle = headTo;
            Apply(headTo, headTo + landAt, RestRadius);

            _spinning = null;
            onFinished?.Invoke();
        }

        /// <summary>
        /// What is left of the ball's journey, from 1 down to exactly 0.
        ///
        /// A ball on a track loses speed to friction, so its angular velocity decays
        /// exponentially and the distance still to travel decays with it. Shifted and
        /// scaled so it is precisely 0 at the end -- an exponential never actually
        /// arrives, and "nearly there" is a ball resting a fraction out of its pocket.
        /// </summary>
        private static float Friction(float t, float decay)
        {
            var e = Mathf.Exp(-decay * t);
            var end = Mathf.Exp(-decay);
            var remaining = (e - end) / (1f - end);

            // **The taper is why the ball no longer stops dead.**
            //
            // An exponential reaches zero distance-to-go at the end but not zero
            // *speed*: this one was still creeping at thirty degrees a second on the
            // last frame, and then the loop finished and it froze. Nothing was wrong
            // with where it stopped -- it was that it stopped rather than came to rest.
            //
            // Smoothstep is flat at both ends, so multiplying by its reverse over the
            // last stretch brings the speed to nothing at the same moment the distance
            // reaches nothing. The head's own ease is already flat at the end, so once
            // this is too, everything settles instead of halting.
            return remaining * (1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.80f, 1f, t)));
        }

        /// <summary>
        /// Fades a bounce in with no step in value **or in speed** at either end.
        /// Smoothstep is flat at both ends, so the bouncing arrives rather than
        /// switching on mid-flight.
        /// </summary>
        private static float RampIn(float u) => Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(u / 0.18f));

        /// <summary>
        /// The ball nudging across the frets as it settles. Deliberately small: at nine
        /// degrees it was cutting the ball's speed to a third at every beat, which reads
        /// as surging rather than as rattling. Three degrees is a third of a pocket.
        ///
        /// Zero at the end, like everything else here, so however lively it looks it
        /// cannot move where the ball finishes.
        /// </summary>
        private static float Rattle(float t, float drop)
        {
            if (t < drop)
            {
                return 0f;
            }

            var u = (t - drop) / (1f - drop);
            var envelope = (1f - u) * (1f - u) * RampIn(u) * 3f;
            var phase = Mathf.Pow(u, 1.25f);

            return envelope * ((Mathf.Sin(phase * 7f) * 0.7f) + (Mathf.Sin(phase * 15f) * 0.3f));
        }

        /// <summary>
        /// How far out the ball is: the track while it is fast, then falling inward and
        /// bouncing back up the slope a couple of times. A ball that slides straight
        /// down reads as a bead on a wire.
        /// </summary>
        private static float Radius(float t, float drop)
        {
            if (t < drop)
            {
                return TrackRadius;
            }

            var u = (t - drop) / (1f - drop);
            var fall = Mathf.SmoothStep(TrackRadius, RestRadius, u);
            var phase = Mathf.Pow(u, 1.25f);
            var bounce = Mathf.Abs(Mathf.Sin(phase * 8f)) * (1f - u) * (1f - u) * RampIn(u) * 0.045f;

            return fall + bounce;
        }

        private static void Apply(float headAngle, float ballAngle, float ballRadius)
        {
            if (_head == null)
            {
                return;
            }

            // Negated because Unity turns counter-clockwise and a wheel's numbers run
            // clockwise from the zero.
            _head.localRotation = Quaternion.Euler(0f, 0f, -headAngle);

            if (_ballPivot == null || _ball == null)
            {
                return;
            }

            _ballAngle = ballAngle;
            _ballPivot.localRotation = Quaternion.Euler(0f, 0f, -ballAngle);
            _ball.anchoredPosition = new Vector2(0f, ballRadius * _diameter * 0.5f);
        }

        private static float PocketAngle(int position) => position * (360f / _pocketCount);

        private static float EaseOut(float t, float power) => 1f - Mathf.Pow(1f - t, power);

        // ---------------------------------------------------------------- the drawing

        /// <summary>
        /// The bowl: the gold rim, the wooden apron with its deflectors, and the track
        /// the ball runs on. Transparent inside, where the head shows through.
        /// </summary>
        private static Sprite BowlSprite()
        {
            return Paint((f, angle, light) =>
            {
                if (f > 1f)
                {
                    return Clear;
                }

                if (f > RimInner)
                {
                    // A bright gold band with a bevel: lighter towards its middle so it
                    // reads as a rounded edge rather than a flat hoop.
                    var g = Mathf.InverseLerp(1f, RimInner, f);
                    return Shade(Lerp(GoldDeep, GoldBright, Mathf.Sin(g * Mathf.PI) * 0.9f), light);
                }

                if (f > ApronInner)
                {
                    // The wooden apron the ball runs on, darker at its outside.
                    var g = Mathf.InverseLerp(RimInner, ApronInner, f);
                    var wood = Lerp(WoodDark, Wood, Mathf.SmoothStep(0f, 1f, g));

                    // Eight gold deflectors, as on a real bowl.
                    var mid = (RimInner + ApronInner) * 0.5f;
                    var diamond = Mathf.Abs(Mathf.DeltaAngle(angle, Mathf.Round(angle / 45f) * 45f));
                    var near = Mathf.Abs(f - mid);

                    if ((diamond / 3.2f) + (near / 0.020f) < 1f)
                    {
                        wood = Lerp(GoldBright, Gold, (diamond / 3.2f));
                    }

                    return Shade(wood, light);
                }

                if (f > OuterGoldInner)
                {
                    return Shade(Gold, light);
                }

                return Clear;
            });
        }

        /// <summary>
        /// The head: the pockets with their numbers, the green inner ring, and the cone.
        /// Drawn from the pocket list the server sent, so the colours and their order
        /// cannot disagree with the wheel it is settling against.
        /// </summary>
        private static Sprite HeadSprite(IReadOnlyList<PocketInfo> pockets)
        {
            var step = 360f / pockets.Count;
            var half = Texture / 2f;

            return Paint((f, angle, light) =>
            {
                if (f > OuterGoldInner)
                {
                    return Clear;
                }

                if (f > PocketInner)
                {
                    // **Pocket i is centred on i * step, not started there.** The numbers
                    // are placed at i * step and the ball lands there, so a wedge that
                    // merely begins at that angle puts every number half a pocket
                    // clockwise of the colour it belongs to.
                    var index = (int)((angle + (step * 0.5f)) / step) % pockets.Count;

                    var pocket = pockets[index].Colour switch
                    {
                        "Red" => Red,
                        "Green" => Green,
                        _ => Black,
                    };

                    // Frets are a constant thickness in pixels, so their angular width
                    // grows as the radius shrinks or they taper away at the inside.
                    var pixels = Mathf.Max(f * half, 1f);
                    var fret = 2.4f / pixels * Mathf.Rad2Deg;

                    var offset = Mathf.DeltaAngle(index * step, angle);
                    var edge = (step * 0.5f) - Mathf.Abs(offset);

                    var onFret = Smooth(edge, fret, fret + (1.1f / pixels * Mathf.Rad2Deg));

                    return Shade(Lerp(Gold, pocket, onFret), light);
                }

                if (f > MidGoldInner)
                {
                    return Shade(Gold, light);
                }

                if (f > GreenInner)
                {
                    // The green ring: the slots the ball actually falls into, one under
                    // each number.
                    //
                    // **The dividers go on the frets, not on the pockets.** Rounding to
                    // the nearest multiple of step finds a pocket's centre, because that
                    // is where pockets are centred -- so drawing the line there put a
                    // gold bar down the middle of every slot and a join where the ball
                    // was meant to sit. Half a step across from a centre is a boundary.
                    var pixels = Mathf.Max(f * half, 1f);
                    var line = 1.6f / pixels * Mathf.Rad2Deg;

                    var boundary = (Mathf.Round((angle / step) - 0.5f) + 0.5f) * step;
                    var offset = Mathf.DeltaAngle(boundary, angle);
                    var onLine = Smooth(Mathf.Abs(offset), line, line + (1.2f / pixels * Mathf.Rad2Deg));

                    // Darker towards the hub, so each slot reads as a recess rather than
                    // a flat wedge.
                    var band = Mathf.InverseLerp(GreenInner, MidGoldInner, f);
                    var green = Lerp(Scale(GreenRing, 0.72f), GreenRing, band);

                    return Shade(Lerp(Gold, green, onLine), light);
                }

                if (f > ConeOuter)
                {
                    return Shade(Gold, light);
                }

                // The cone, and the spider standing on it.
                var cone = Lerp(GoldBright, GoldDeep, Mathf.InverseLerp(0f, ConeOuter, f) * 0.85f);

                // Four arms and a boss, which is what stops the middle of the wheel
                // looking like a plain disc while it turns.
                var arm = Mathf.Abs(Mathf.DeltaAngle(angle, Mathf.Round(angle / 90f) * 90f));
                var armWidth = Mathf.Lerp(7f, 2.2f, Mathf.InverseLerp(0.06f, ConeOuter, f));

                if (f > 0.06f && f < ConeOuter - 0.02f && arm < armWidth)
                {
                    cone = Lerp(GoldBright, cone, Smooth(arm, armWidth - 1.4f, armWidth));
                }

                if (f < 0.075f)
                {
                    cone = Lerp(GoldBright, Gold, f / 0.075f);
                }

                return Shade(cone, light);
            });
        }

        /// <summary>
        /// Runs a function over every pixel of a square texture, handing it the radius as
        /// a fraction, the angle clockwise from the top, and a lighting factor.
        ///
        /// The fraction is of the **radius**, not the diameter. Getting that wrong is
        /// what drew a pocket ring at twice its size and burst it out of the bowl.
        /// </summary>
        private static Sprite Paint(Func<float, float, float, Color32> shade)
        {
            var texture = new Texture2D(Texture, Texture, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.HideAndDontSave,
            };

            var pixels = new Color32[Texture * Texture];
            var half = Texture / 2f;

            // **Every ring boundary was a hard cut, and that is the jaggedness.** The
            // frets were antialiased by hand and the outer edge was feathered, but the
            // dozen concentric boundaries between rim, apron, pockets, green and cone
            // each stepped straight from one colour to the next at whole pixels -- so
            // every circle in the wheel had a staircase on it.
            //
            // Rather than hand-smooth each one, every pixel is sampled four times on a
            // half-pixel grid and averaged. That antialiases everything at once,
            // including the spider's arms and the deflectors, and it cannot be
            // forgotten when a new ring is added. It costs one pass over four million
            // samples, once, when the table is first opened.
            var offsets = new[] { -0.25f, 0.25f };

            for (var y = 0; y < Texture; y++)
            {
                for (var x = 0; x < Texture; x++)
                {
                    float r = 0f, g = 0f, b = 0f, a = 0f;

                    foreach (var oy in offsets)
                    {
                        var dy = y + oy - half;

                        foreach (var ox in offsets)
                        {
                            var dx = x + ox - half;
                            var f = Mathf.Sqrt((dx * dx) + (dy * dy)) / half;

                            if (f > 1f)
                            {
                                continue;
                            }

                            var angle = Mathf.Atan2(dx, dy) * Mathf.Rad2Deg;
                            if (angle < 0f)
                            {
                                angle += 360f;
                            }

                            // A soft key light from the upper left, and a gentle
                            // darkening towards the edge, so the rings do not read flat.
                            var nx = dx / half;
                            var ny = dy / half;
                            var light = 1f + (0.18f * ((-nx * 0.6f) + (ny * 0.8f))) - (0.12f * f * f);

                            var c = shade(f, angle, light);

                            // Premultiplied while averaging, or a transparent sample
                            // drags its colour into the ones beside it and leaves a
                            // dark fringe around the rim.
                            var w = c.a / 255f;
                            r += c.r * w;
                            g += c.g * w;
                            b += c.b * w;
                            a += w;
                        }
                    }

                    if (a <= 0.0001f)
                    {
                        pixels[(y * Texture) + x] = Clear;
                        continue;
                    }

                    pixels[(y * Texture) + x] = new Color32(
                        (byte)Mathf.Clamp(r / a, 0f, 255f),
                        (byte)Mathf.Clamp(g / a, 0f, 255f),
                        (byte)Mathf.Clamp(b / a, 0f, 255f),
                        (byte)Mathf.Clamp(a / offsets.Length / offsets.Length * 255f, 0f, 255f));
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply();

            return Sprite.Create(
                texture, new Rect(0f, 0f, Texture, Texture), new Vector2(0.5f, 0.5f), 100f);
        }

        private static Color32 Lerp(Color32 a, Color32 b, float t)
        {
            t = Mathf.Clamp01(t);

            return new Color32(
                (byte)(a.r + ((b.r - a.r) * t)),
                (byte)(a.g + ((b.g - a.g) * t)),
                (byte)(a.b + ((b.b - a.b) * t)),
                (byte)(a.a + ((b.a - a.a) * t)));
        }

        private static Color32 Scale(Color32 c, float by) =>
            new Color32((byte)(c.r * by), (byte)(c.g * by), (byte)(c.b * by), c.a);

        private static Color32 Shade(Color32 c, float light)
        {
            light = Mathf.Clamp(light, 0.6f, 1.4f);

            return new Color32(
                (byte)Mathf.Clamp(c.r * light, 0f, 255f),
                (byte)Mathf.Clamp(c.g * light, 0f, 255f),
                (byte)Mathf.Clamp(c.b * light, 0f, 255f),
                c.a);
        }

        private static float Smooth(float v, float a, float b) =>
            Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(a, b, v));

        // ---------------------------------------------------------------- the pieces

        /// <summary>
        /// The numbers, as labels parented to the head so they turn with it, sitting
        /// inside the pocket blocks the way a real wheel prints them.
        /// </summary>
        private static void BuildNumbers(IReadOnlyList<PocketInfo> pockets, float diameter)
        {
            var step = 360f / pockets.Count;

            for (var i = 0; i < pockets.Count; i++)
            {
                var go = new GameObject("N" + pockets[i].Label, typeof(RectTransform));
                go.transform.SetParent(_head, false);

                var text = go.AddComponent<TextMeshProUGUI>();
                text.text = pockets[i].Label;
                text.fontSize = diameter * 0.034f;
                text.alignment = TextAlignmentOptions.Center;
                text.color = Numerals;
                text.raycastTarget = false;
                text.enableWordWrapping = false;

                if (_font != null)
                {
                    text.font = _font;
                }

                var rect = (RectTransform)go.transform;
                rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
                rect.pivot = new Vector2(0.5f, 0.5f);
                rect.sizeDelta = new Vector2(diameter * 0.09f, diameter * 0.055f);

                var angle = i * step;
                var radians = (90f - angle) * Mathf.Deg2Rad;
                var radius = NumberRadius * diameter * 0.5f;

                rect.anchoredPosition = new Vector2(
                    Mathf.Cos(radians) * radius, Mathf.Sin(radians) * radius);

                // Feet towards the hub, so every number reads from the outside in.
                rect.localRotation = Quaternion.Euler(0f, 0f, -angle);
            }
        }

        /// <summary>
        /// The marker the winning pocket comes to rest under. Outside the head so it
        /// does not turn: it is the fixed point the animation is aimed at, and a
        /// spinning marker would mean nothing.
        /// </summary>
        private static void BuildMarker(RectTransform root, float diameter)
        {
            var marker = NewImage("Marker", root, Color.white);
            marker.anchorMin = marker.anchorMax = new Vector2(0.5f, 0.5f);
            marker.pivot = new Vector2(0.5f, 1f);
            marker.sizeDelta = new Vector2(diameter * 0.022f, diameter * 0.055f);
            marker.anchoredPosition = new Vector2(0f, diameter * 0.5f * 1.02f);

            var image = marker.GetComponent<Image>();
            image.sprite = Textures.RoundedBox(
                5, new Color(0.89f, 0.76f, 0.46f, 1f), new Color(0.18f, 0.14f, 0.08f, 1f), 2);
            image.type = Image.Type.Sliced;
        }

        private static void BuildBall(RectTransform root, float diameter)
        {
            // A pivot at the centre carrying the ball out at a radius: rotating the pivot
            // walks the ball round the track, which is one transform instead of
            // trigonometry every frame.
            _ballPivot = NewImage("BallPivot", root, Clear);
            _ballPivot.anchorMin = _ballPivot.anchorMax = new Vector2(0.5f, 0.5f);
            _ballPivot.pivot = new Vector2(0.5f, 0.5f);
            _ballPivot.sizeDelta = new Vector2(diameter, diameter);

            _ball = NewImage("Ball", _ballPivot, Color.white);
            _ball.anchorMin = _ball.anchorMax = new Vector2(0.5f, 0.5f);
            _ball.pivot = new Vector2(0.5f, 0.5f);
            _ball.sizeDelta = new Vector2(diameter * BallSize, diameter * BallSize);

            var image = _ball.GetComponent<Image>();
            var art = Textures.FromFile(System.IO.Path.Combine(
                System.IO.Path.GetDirectoryName(RouletteClientPlugin.Instance?.Info?.Location ?? ".") ?? ".",
                "ball.png"));

            if (art != null)
            {
                image.sprite = art;
                image.preserveAspect = true;
            }
            else
            {
                image.sprite = Textures.RoundedBox(
                    64, new Color(0.96f, 0.94f, 0.86f, 1f), new Color(0.55f, 0.52f, 0.44f, 1f), 2);
                image.type = Image.Type.Sliced;
            }

            var shadow = _ball.gameObject.AddComponent<Shadow>();
            shadow.effectColor = new Color(0f, 0f, 0f, 0.6f);
            shadow.effectDistance = new Vector2(2f, -3f);

            _ballPivot.SetAsLastSibling();
        }

        private static RectTransform NewImage(string name, Transform parent, Color colour)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.transform.SetParent(parent, false);

            var image = go.GetComponent<Image>();
            image.color = colour;
            image.raycastTarget = false;

            return (RectTransform)go.transform;
        }
    }

    /// <summary>One pocket as the server described it. See TableView.PocketView.</summary>
    internal sealed class PocketInfo
    {
        internal PocketInfo(int number, string label, string colour)
        {
            Number = number;
            Label = label;
            Colour = colour;
        }

        internal int Number { get; }

        internal string Label { get; }

        internal string Colour { get; }
    }
}
