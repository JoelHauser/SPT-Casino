using System;
using System.Collections;
using System.Collections.Generic;
using Casino.Shared;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace HorseRacing.Client
{
    /// <summary>
    /// The course: eight lanes seen from the side, run left to right.
    ///
    /// ## The one thing this must never get wrong
    ///
    /// **The horse the server said won has to cross the line first.** The finishing
    /// order arrives from the server already decided -- the money has already moved by
    /// the time a single frame is drawn -- so this is presentation, and presentation
    /// that disagrees with the result is worse than no animation at all. A player who
    /// watches number 3 win and is then paid for number 5 has been shown a lie, and
    /// there is no way for them to tell which half was the bug.
    ///
    /// So the ordering is not *arranged* here, it is *arithmetic*. Each runner is given
    /// a finishing time strictly ordered by its finishing position, and its progress is
    /// a monotonic function of its own elapsed fraction of that time. At the post every
    /// runner is at exactly 1.0, reached in ascending order of finish time, and nothing
    /// in the wobble can change that -- see <see cref="Warp"/>, which divides one
    /// strictly-increasing integral by another and so cannot run backwards or overshoot.
    ///
    /// ## Every course is a straight, and there used to be an oval
    ///
    /// The mile and the marathon were drawn as one and two laps of an oval. It looked
    /// wrong and was dropped: the track holder is about 1470 wide and 270 tall, so a
    /// circuit has to be flattened to roughly two and a half to one before it fits,
    /// which reads as a running stadium rather than a racecourse -- and the runners
    /// bunch together on the bends, which is exactly where eight coloured discs are
    /// hardest to tell apart.
    ///
    /// **A longer race is now a longer run rather than a different shape.** The
    /// distance is carried by the furlong markers along the top -- five ticks for the
    /// dash and sixteen for the marathon -- and by the clock, since the marathon takes
    /// nearly twice as long to run. Both come from the server with the rest of the
    /// course.
    /// </summary>
    internal static class TrackView
    {
        /// <summary>
        /// How much later each place finishes, as a fraction of the race's length.
        ///
        /// Small on purpose. Eight runners spread over 1.8% each puts the last horse
        /// about a tenth of the race behind the winner, which reads as a field crossing
        /// the line rather than a procession -- and still leaves every gap several
        /// frames wide at 60fps, so two horses never appear to dead-heat.
        /// </summary>
        private const float PlaceGap = 0.018f;

        private const float LaneHeight = 30f;
        private const float HorseSize = 22f;

        /// <summary>How many lanes there are. The card has eight runners and always has.</summary>
        private const int Lanes = 8;

        /// <summary>The band along the top that the furlong markers live in.</summary>
        private const float FurlongStrip = 22f;

        /// <summary>How wide the checkered finish line is. Two 6px squares across.</summary>
        private const int PostWidth = 12;

        /// <summary>Where a runner stands before the stalls open.</summary>
        private const float Start = 250f;

        /// <summary>
        /// How far in from the right edge the post stands.
        ///
        /// **120, and it is the placings column that sets it.** The field ends 20 past
        /// the line, so a runner's rightmost point lands at <c>FinishInset - 31</c> from
        /// the right edge -- and the placings occupy 14 to 70 there. At the old 76 the
        /// horses were drawn straight on top of the text.
        /// </summary>
        private const float FinishInset = 120f;

        private static readonly Color Rail = new Color(0.86f, 0.87f, 0.84f, 0.85f);
        private static readonly Color Turf = new Color(0.114f, 0.180f, 0.118f, 1f);
        private static readonly Color TurfMown = new Color(0.137f, 0.212f, 0.141f, 1f);
        private static readonly Color Post = new Color(0.86f, 0.24f, 0.24f, 0.95f);
        private static readonly Color Ink = new Color(0.88f, 0.86f, 0.80f, 1f);
        private static readonly Color Faint = new Color(0.88f, 0.86f, 0.80f, 0.35f);

        /// <summary>
        /// The silks, one per saddlecloth number.
        ///
        /// Eight colours that stay apart from each other at 22 pixels and on a dark
        /// green background. Not generated from a hue wheel: an even spread puts two of
        /// them in the greens, which is precisely where the turf is.
        /// </summary>
        private static readonly Color[] Silks =
        {
            new Color(0.93f, 0.93f, 0.90f, 1f), // 1  white
            new Color(0.85f, 0.20f, 0.20f, 1f), // 2  red
            new Color(0.25f, 0.50f, 0.90f, 1f), // 3  blue
            new Color(0.95f, 0.75f, 0.15f, 1f), // 4  yellow
            new Color(0.60f, 0.30f, 0.75f, 1f), // 5  purple
            new Color(0.95f, 0.50f, 0.15f, 1f), // 6  orange
            new Color(0.20f, 0.75f, 0.70f, 1f), // 7  teal
            new Color(0.55f, 0.35f, 0.20f, 1f), // 8  brown
        };

        private static RectTransform _track;
        private static float _duration = 6f;

        private static readonly List<RectTransform> _runners = new List<RectTransform>();
        private static readonly List<TextMeshProUGUI> _places = new List<TextMeshProUGUI>();
        private static Coroutine _running;

        /// <summary>
        /// Whether a race is on.
        ///
        /// Read by the panel to refuse a second slip mid-race and to hold the escape
        /// key -- a table that vanishes while the horses are running looks like a crash,
        /// which is exactly what roulette found with its wheel.
        /// </summary>
        internal static bool IsRunning => _running != null;

        /// <summary>
        /// Builds a course. Called whenever the player switches track, so it clears
        /// whatever was there first.
        /// </summary>
        /// <param name="parent">The holder the course fills.</param>
        /// <param name="furlongs">How long the race is. One marker each.</param>
        /// <param name="runSeconds">How long the field takes to cover it.</param>
        /// <param name="runners">Saddlecloth number and name, in card order.</param>
        /// <param name="font">The panel's font, so the course does not load its own.</param>
        internal static void Build(
            RectTransform parent,
            int furlongs,
            float runSeconds,
            IReadOnlyList<KeyValuePair<int, string>> runners,
            TMP_FontAsset font)
        {
            Reset();

            _track = parent;
            _duration = Mathf.Max(1f, runSeconds);

            _runners.Clear();
            _places.Clear();

            foreach (Transform child in parent)
            {
                UnityEngine.Object.Destroy(child.gameObject);
            }

            var turf = New("Turf", parent);
            Stretch(turf);
            var turfImage = turf.gameObject.AddComponent<Image>();
            turfImage.sprite = Textures.RoundedBox(10, Turf, new Color(0f, 0f, 0f, 0.5f), 2);
            turfImage.type = Image.Type.Sliced;
            turfImage.raycastTarget = false;

            // Mown stripes, running across the track the way a roller leaves them. This
            // is most of what makes it read as grass rather than as a dark rectangle,
            // and it costs five images.
            const int stripes = 9;

            for (var i = 0; i < stripes; i += 2)
            {
                var band = New($"Mown{i}", parent);
                band.anchorMin = new Vector2(i / (float)stripes, 0f);
                band.anchorMax = new Vector2((i + 1) / (float)stripes, 1f);
                band.offsetMin = new Vector2(0f, 6f);
                band.offsetMax = new Vector2(0f, -6f);
                var stripe = band.gameObject.AddComponent<Image>();
                stripe.color = TurfMown;
                stripe.raycastTarget = false;
            }

            // The running rails, top and bottom.
            foreach (var edge in new[] { 0f, 1f })
            {
                var rail = New(edge > 0.5f ? "RailTop" : "RailBottom", parent);
                rail.anchorMin = new Vector2(0f, edge);
                rail.anchorMax = new Vector2(1f, edge);
                rail.pivot = new Vector2(0.5f, edge);
                rail.anchoredPosition = new Vector2(0f, edge > 0.5f ? -5f : 5f);
                rail.sizeDelta = new Vector2(-12f, 2f);
                var image = rail.gameObject.AddComponent<Image>();
                image.color = Rail;
                image.raycastTarget = false;
            }

            BuildFurlongMarkers(parent, furlongs, font);

            for (var lane = 0; lane < runners.Count; lane++)
            {
                BuildLane(parent, lane, runners[lane].Key, runners[lane].Value, font);
            }

            BuildStalls(parent);
            BuildPost(parent);
        }

        /// <summary>
        /// A marker per furlong, counting down to the post the way a real course does.
        ///
        /// **This is what carries the distance.** Every course is the same number of
        /// pixels long, so without these a two-mile marathon and a five-furlong dash
        /// would be the same picture at a different speed. Five ticks against sixteen
        /// says which is which at a glance.
        ///
        /// Numbers are drawn on every marker when there is room and on every other one
        /// when there is not, since sixteen labels across the same span as five is the
        /// point at which they start touching.
        /// </summary>
        private static void BuildFurlongMarkers(RectTransform parent, int furlongs, TMP_FontAsset font)
        {
            if (furlongs <= 0)
            {
                return;
            }

            var travel = Travel(parent);
            var everyOther = furlongs > 10;

            // The post is the last marker and already has a line of its own, so the
            // ticks drawn here are the ones *before* it.
            for (var i = 1; i <= furlongs; i++)
            {
                var atFinish = i == furlongs;
                var x = Start + (travel * (i / (float)furlongs));

                var tick = New($"Furlong{i}", parent);
                tick.anchorMin = new Vector2(0f, 1f);
                tick.anchorMax = new Vector2(0f, 1f);
                tick.pivot = new Vector2(0.5f, 1f);
                tick.anchoredPosition = new Vector2(x, -3f);
                tick.sizeDelta = new Vector2(1f, atFinish ? 0f : 7f);

                var image = tick.gameObject.AddComponent<Image>();
                image.color = new Color(Rail.r, Rail.g, Rail.b, 0.35f);
                image.raycastTarget = false;

                var remaining = furlongs - i;

                if (atFinish || (everyOther && remaining % 2 != 0))
                {
                    continue;
                }

                var label = New($"FurlongLabel{i}", parent);
                label.anchorMin = new Vector2(0f, 1f);
                label.anchorMax = new Vector2(0f, 1f);
                label.pivot = new Vector2(0.5f, 1f);
                label.anchoredPosition = new Vector2(x, -10f);
                label.sizeDelta = new Vector2(34f, 14f);

                var text = label.gameObject.AddComponent<TextMeshProUGUI>();
                text.font = font;
                text.fontSize = 10f;
                text.color = new Color(Ink.r, Ink.g, Ink.b, 0.30f);
                text.alignment = TextAlignmentOptions.Center;
                text.text = remaining == 0 ? string.Empty : $"{remaining}f";
                text.raycastTarget = false;
            }
        }

        private static void BuildLane(
            RectTransform parent, int lane, int number, string name, TMP_FontAsset font)
        {
            // Below the furlong strip along the top.
            var top = -(lane * LaneHeight) - FurlongStrip;

            var label = New($"Name{number}", parent);
            label.anchorMin = new Vector2(0f, 1f);
            label.anchorMax = new Vector2(0f, 1f);
            label.pivot = new Vector2(0f, 1f);
            label.anchoredPosition = new Vector2(16f, top - 4f);
            label.sizeDelta = new Vector2(220f, LaneHeight - 10f);

            var text = label.gameObject.AddComponent<TextMeshProUGUI>();
            text.font = font;
            text.fontSize = 13f;
            text.color = Faint;
            text.alignment = TextAlignmentOptions.Left;
            text.text = $"{number}  {name}";
            text.raycastTarget = false;

            var place = New($"Place{number}", parent);
            place.anchorMin = new Vector2(1f, 1f);
            place.anchorMax = new Vector2(1f, 1f);
            place.pivot = new Vector2(1f, 1f);
            place.anchoredPosition = new Vector2(-14f, top - 4f);
            place.sizeDelta = new Vector2(56f, LaneHeight - 10f);

            var placeText = place.gameObject.AddComponent<TextMeshProUGUI>();
            placeText.font = font;
            placeText.fontSize = 15f;
            placeText.fontStyle = FontStyles.Bold;
            placeText.color = Ink;
            placeText.alignment = TextAlignmentOptions.Right;
            placeText.text = string.Empty;
            placeText.raycastTarget = false;
            _places.Add(placeText);

            _runners.Add(MakeHorse(parent, number, font, new Vector2(
                Start, top - ((LaneHeight - HorseSize) / 2f))));
        }

        /// <summary>The starting stalls, which is what the left edge of a race is.</summary>
        private static void BuildStalls(RectTransform parent)
        {
            var stalls = New("Stalls", parent);
            stalls.anchorMin = new Vector2(0f, 0f);
            stalls.anchorMax = new Vector2(0f, 1f);
            stalls.pivot = new Vector2(0f, 0.5f);
            stalls.anchoredPosition = new Vector2(Start - (HorseSize * 0.5f) - 8f, -8f);
            stalls.sizeDelta = new Vector2(5f, -32f);

            var image = stalls.gameObject.AddComponent<Image>();
            image.color = new Color(Rail.r, Rail.g, Rail.b, 0.5f);
            image.raycastTarget = false;
        }

        /// <summary>
        /// The finish line: a checkered strip from the top of the track to the bottom.
        ///
        /// **Full height, edge to edge.** It has been trimmed twice -- first to clear
        /// the furlong strip, then to fit the lanes -- and both times it ended up a
        /// line that visibly did not cross the whole track, which is the one thing a
        /// finish line has to do. A post is a physical thing standing across the
        /// course; it crosses the markers too.
        ///
        /// Checkered rather than a red stick, because a coloured vertical line in the
        /// middle of a racetrack reads as a barrier or a divider. Black and white
        /// squares read as one thing only.
        ///
        /// Its pivot is centred horizontally so the line sits exactly where a runner's
        /// centre lands at the end of its travel.
        /// </summary>
        private static void BuildPost(RectTransform parent)
        {
            var height = Mathf.Max(40, Mathf.RoundToInt(parent.rect.height) - 8);

            var post = New("Post", parent);
            post.anchorMin = new Vector2(1f, 1f);
            post.anchorMax = new Vector2(1f, 1f);
            post.pivot = new Vector2(0.5f, 1f);
            post.anchoredPosition = new Vector2(-FinishInset, -4f);
            post.sizeDelta = new Vector2(PostWidth, height);

            var image = post.gameObject.AddComponent<Image>();
            image.sprite = Textures.Checker(
                PostWidth,
                height,
                6,
                new Color(0.07f, 0.07f, 0.07f, 1f),
                new Color(0.95f, 0.95f, 0.93f, 1f));
            image.raycastTarget = false;
        }

        /// <summary>How far a runner actually travels, from the stalls to the post.</summary>
        private static float Travel(RectTransform holder) =>
            Mathf.Max(80f, holder.rect.width - FinishInset - Start);

        private static RectTransform MakeHorse(
            RectTransform parent, int number, TMP_FontAsset font, Vector2 at)
        {
            var horse = New($"Horse{number}", parent);
            horse.anchorMin = new Vector2(0f, 1f);
            horse.anchorMax = new Vector2(0f, 1f);
            horse.pivot = new Vector2(0.5f, 1f);
            horse.anchoredPosition = at;
            horse.sizeDelta = new Vector2(HorseSize, HorseSize);

            var silkColour = Silks[(number - 1) % Silks.Length];

            var silk = horse.gameObject.AddComponent<Image>();
            silk.sprite = Textures.RoundedBox(12, silkColour, new Color(0f, 0f, 0f, 0.6f), 2);
            silk.type = Image.Type.Sliced;
            silk.raycastTarget = false;

            var cloth = New("Number", horse);
            Stretch(cloth);

            var clothText = cloth.gameObject.AddComponent<TextMeshProUGUI>();
            clothText.font = font;
            clothText.fontSize = 14f;
            clothText.fontStyle = FontStyles.Bold;

            // Dark text on the pale silks, pale on the dark ones. Worked out from the
            // silk rather than picked per horse, so a recoloured runner stays legible.
            var luminance = (0.299f * silkColour.r) + (0.587f * silkColour.g) + (0.114f * silkColour.b);
            clothText.color = luminance > 0.55f ? new Color(0.1f, 0.1f, 0.1f, 1f) : Color.white;

            clothText.alignment = TextAlignmentOptions.Center;
            clothText.text = number.ToString();
            clothText.raycastTarget = false;

            return horse;
        }

        // ------------------------------------------------------------------- running

        /// <summary>
        /// Runs the race to the given finishing order.
        /// </summary>
        /// <param name="order">
        /// Saddlecloth numbers in finishing position, exactly as the server sent them.
        /// </param>
        /// <param name="onDone">
        /// Called once, after the last runner is home. The panel pays out here rather
        /// than when the reply arrived, so the money on screen and the horses agree.
        /// </param>
        internal static void Run(IReadOnlyList<int> order, Action onDone)
        {
            var host = RaceClientPlugin.Instance;

            if (_track == null || host == null || order == null || order.Count == 0)
            {
                onDone?.Invoke();
                return;
            }

            if (_running != null)
            {
                host.StopCoroutine(_running);
                _running = null;
            }

            _running = host.StartCoroutine(Gallop(order, onDone));
        }

        /// <summary>
        /// Abandons any running race and puts the field back behind the stalls.
        ///
        /// For the panel to call, never for <see cref="Gallop"/> -- it stops the
        /// coroutine, and a coroutine that stops itself here would take the rest of its
        /// own body with it.
        /// </summary>
        internal static void Reset()
        {
            var host = RaceClientPlugin.Instance;

            if (_running != null && host != null)
            {
                host.StopCoroutine(_running);
            }

            _running = null;
            Rewind();
        }

        /// <summary>
        /// Puts the field behind the stalls and clears the placings, and touches
        /// nothing else. Safe from inside the race itself.
        /// </summary>
        private static void Rewind()
        {
            foreach (var runner in _runners)
            {
                if (runner != null)
                {
                    runner.anchoredPosition = new Vector2(Start, runner.anchoredPosition.y);
                }
            }

            foreach (var place in _places)
            {
                if (place != null)
                {
                    place.text = string.Empty;
                }
            }
        }

        private static IEnumerator Gallop(IReadOnlyList<int> order, Action onDone)
        {
            Rewind();

            // Finishing time per saddlecloth number, strictly increasing down the
            // order. This is the whole correctness argument: a runner reaches the post
            // exactly when its own timer expires, and these are ordered.
            var finishAt = new Dictionary<int, float>();

            for (var place = 0; place < order.Count; place++)
            {
                finishAt[order[place]] = _duration * (1f + (place * PlaceGap));
            }

            var lastHome = _duration * (1f + ((order.Count - 1) * PlaceGap));
            var travel = Travel(_track);

            SoundBoard.Play(Cue.RaceOff);

            var elapsed = 0f;
            var called = false;

            while (elapsed < lastHome)
            {
                // Unscaled: the menu is not necessarily running at a normal timescale,
                // and a race that stalls with it would hang the panel open. The same
                // reason every fade in this mod uses it.
                elapsed += Time.unscaledDeltaTime;

                for (var lane = 0; lane < _runners.Count; lane++)
                {
                    var runner = _runners[lane];

                    if (runner == null)
                    {
                        continue;
                    }

                    var number = lane + 1;

                    if (!finishAt.TryGetValue(number, out var finish))
                    {
                        continue;
                    }

                    var progress = Pace(Warp(number, elapsed, finish));

                    runner.anchoredPosition = new Vector2(
                        Start + (progress * travel), runner.anchoredPosition.y);
                }

                // The winner passing the post, which is the moment worth hearing --
                // not the end of the coroutine, a fraction of the race later.
                if (!called && elapsed >= finishAt[order[0]])
                {
                    called = true;
                    SoundBoard.Play(Cue.RaceFinish);
                }

                // Placings appear as each runner gets home rather than all at once at
                // the end, because that is the order the eye already watched happen.
                for (var place = 0; place < order.Count; place++)
                {
                    var index = order[place] - 1;

                    if (index >= 0 && index < _places.Count
                        && _places[index] != null
                        && _places[index].text.Length == 0
                        && elapsed >= finishAt[order[place]])
                    {
                        _places[index].text = Ordinal(place + 1);
                    }
                }

                yield return null;
            }

            Finish(travel);

            _running = null;
            onDone?.Invoke();
        }

        /// <summary>
        /// Everybody home, whatever the frame timing did -- strung out in finishing
        /// order rather than stacked on the line.
        ///
        /// **Everybody past the line, all at the same point.**
        ///
        /// Staggering by finishing position was tried twice and reported wrong both
        /// times. Nine pixels a place spread the field over sixty-three and read as
        /// seven horses that never finished; five pixels straddling the post read as a
        /// diagonal scatter across it, because the lanes run in saddlecloth order and
        /// the finishing order does not, so the stagger draws a zigzag rather than a
        /// line.
        ///
        /// A photo finish is a real thing, but it needs the runners close enough
        /// together to be one photograph. Eight discs in fixed lanes, spread over a
        /// tenth of the visible track, is not that -- it is eight horses stopped in
        /// eight different places.
        ///
        /// So the field ends in a column just past the line, which is what a field that
        /// has crossed looks like, and **the order is read from the placings beside
        /// each lane** -- which is what that column is for and is unambiguous in a way
        /// that comparing eight x-positions by eye never was.
        /// </summary>
        private static void Finish(float travel)
        {
            foreach (var runner in _runners)
            {
                if (runner != null)
                {
                    runner.anchoredPosition = new Vector2(
                        Start + travel + 20f, runner.anchoredPosition.y);
                }
            }
        }

        /// <summary>
        /// How a horse covers the ground: away hard, settle, quicken off the turn.
        ///
        /// A straight line reads as a progress bar. This is a gentle S, weighted so the
        /// middle third is the slowest part -- which is where the jostle has room to
        /// change the running order before it decays away.
        /// </summary>
        private static float Pace(float u) => (u * u * (3f - (2f * u)) * 0.55f) + (u * 0.45f);

        // How much a runner's speed varies, and how fast. The two frequencies are
        // deliberately not multiples of each other, so the field does not breathe in
        // and out together like a concertina.
        //
        // **WobbleA + WobbleB must stay below 1.** Speed is 1 + A*sin + B*sin, so at
        // 0.53 the slowest a horse ever runs is 0.47 of the average -- and the moment
        // the sum reaches 1 that speed touches zero and the guarantee below is lost.
        private const float WobbleA = 0.35f;
        private const float WobbleB = 0.18f;
        private const float WobbleW1 = 1.9f;
        private const float WobbleW2 = 3.1f;

        /// <summary>
        /// The distance this runner has covered by <paramref name="elapsed"/>, in
        /// arbitrary units -- the integral of its own speed.
        ///
        /// Speed is <c>1 + A*sin(w1 t) + B*sin(w2 t)</c>, which is never less than
        /// 0.47, so this is **strictly increasing**. That is the whole point: see
        /// <see cref="Warp"/>.
        ///
        /// Integrated in closed form rather than accumulated frame by frame, because an
        /// accumulator depends on the frame rate and could not be asked where a runner
        /// will be at some future instant -- which <see cref="Warp"/> has to know in
        /// order to normalise.
        /// </summary>
        private static float Ridden(int number, float elapsed)
        {
            var p1 = number * 1.7f;
            var p2 = number * 1.02f;

            return elapsed
                + (WobbleA * (Mathf.Cos(p1) - Mathf.Cos((WobbleW1 * elapsed) + p1)) / WobbleW1)
                + (WobbleB * (Mathf.Cos(p2) - Mathf.Cos((WobbleW2 * elapsed) + p2)) / WobbleW2);
        }

        /// <summary>
        /// This runner's own sense of how far through its race it is: 0 at the stalls,
        /// exactly 1 at its finishing time, and **never going backwards in between**.
        ///
        /// ## Why the wobble warps time instead of moving the horse
        ///
        /// The first version added the wobble straight to the position --
        /// <c>Pace(u) + Jostle(u)</c> -- and that is wrong in a way that is easy to
        /// miss. Early in a race <c>Pace</c> is still shallow, so when the sine term
        /// turned over it fell faster than <c>Pace</c> was rising and the net position
        /// *decreased*: the horse visibly slid backwards. Measured at up to 9 pixels on
        /// the shipped numbers, which is exactly what it was reported as -- "some
        /// horses go forward then fall backwards in place".
        ///
        /// Dividing one strictly-increasing integral by another cannot do that. The
        /// wobble now changes how fast the clock runs for this horse, never which way,
        /// and <see cref="Pace"/> is applied on top -- a monotonic function of a
        /// monotonic function is monotonic.
        ///
        /// **Both guarantees survive**: <c>Ridden(0)</c> is 0 and the ratio is 1 at
        /// <paramref name="finish"/>, so every runner still reaches the post exactly
        /// when its own timer expires, and those timers are ordered by finishing
        /// position.
        /// </summary>
        private static float Warp(int number, float elapsed, float finish)
        {
            var total = Ridden(number, finish);

            // Unreachable while WobbleA + WobbleB < 1, since the integrand is positive
            // throughout. Guarded because dividing by it would put a NaN into every
            // runner's position at once.
            if (total <= 0f)
            {
                return Mathf.Clamp01(elapsed / finish);
            }

            return Mathf.Clamp01(Ridden(number, elapsed) / total);
        }

        private static string Ordinal(int place) => place switch
        {
            1 => "1st",
            2 => "2nd",
            3 => "3rd",
            _ => place + "th",
        };

        private static RectTransform New(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);

            return rect;
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }
    }
}
