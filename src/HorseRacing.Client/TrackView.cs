using System;
using System.Collections;
using System.Collections.Generic;
using Casino.Shared;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace HorseRacing.Client
{
    /// <summary>How a course is drawn. Mirrors <c>HorseRacing.Game.TrackShape</c>.</summary>
    internal enum Shape
    {
        Straight,
        Oval,
    }

    /// <summary>
    /// The course: eight runners, drawn either along a straight or around an oval.
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
    /// its own elapsed fraction of that time. At the post every runner is at exactly
    /// 1.0, reached in ascending order of finish time, and nothing in the jostle can
    /// change that -- see <see cref="Jostle"/>, which is multiplied by a term that is
    /// zero at the line.
    ///
    /// ## Two shapes, one set of rules
    ///
    /// **The shape changes only where a runner is drawn, never how fast it gets
    /// there.** <see cref="Gallop"/> computes one number per runner per frame -- how
    /// far round it is, from 0 to 1 -- and hands it to whichever placement the course
    /// uses. The straight maps it along a line; the oval maps it to an angle, times the
    /// number of laps.
    ///
    /// That split is deliberate, and it is what keeps the guarantee above true at both
    /// shapes: there is exactly one piece of code that decides who is in front, and it
    /// does not know what the course looks like.
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

        private const float LaneHeight = 32f;
        private const float HorseSize = 24f;

        private static readonly Color Rail = new Color(0.86f, 0.87f, 0.84f, 0.85f);
        private static readonly Color Turf = new Color(0.114f, 0.180f, 0.118f, 1f);
        private static readonly Color TurfMown = new Color(0.137f, 0.212f, 0.141f, 1f);
        private static readonly Color Infield = new Color(0.086f, 0.141f, 0.094f, 1f);
        private static readonly Color Post = new Color(0.86f, 0.24f, 0.24f, 0.95f);
        private static readonly Color Ink = new Color(0.88f, 0.86f, 0.80f, 1f);
        private static readonly Color Faint = new Color(0.88f, 0.86f, 0.80f, 0.35f);

        /// <summary>
        /// The silks, one per saddlecloth number.
        ///
        /// Eight colours that stay apart from each other at 24 pixels and on a dark
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
        private static Shape _shape = Shape.Straight;
        private static int _laps = 1;
        private static float _duration = 6f;

        private static readonly List<RectTransform> _runners = new List<RectTransform>();
        private static readonly List<TextMeshProUGUI> _places = new List<TextMeshProUGUI>();
        private static TextMeshProUGUI _lapLabel;
        private static Coroutine _running;

        /// <summary>Lane geometry for the oval, worked out once at build time.</summary>
        private static float _ovalRadiusX;
        private static float _ovalRadiusY;
        private static float _laneStep;

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
        internal static void Build(
            RectTransform parent,
            Shape shape,
            int laps,
            float runSeconds,
            IReadOnlyList<KeyValuePair<int, string>> runners,
            TMP_FontAsset font)
        {
            Reset();

            _track = parent;
            _shape = shape;
            _laps = Mathf.Max(1, laps);
            _duration = Mathf.Max(1f, runSeconds);

            _runners.Clear();
            _places.Clear();
            _names.Clear();
            _lapLabel = null;

            foreach (var runner in runners)
            {
                _names[runner.Key] = runner.Value;
            }

            foreach (Transform child in parent)
            {
                UnityEngine.Object.Destroy(child.gameObject);
            }

            if (shape == Shape.Oval)
            {
                BuildOval(parent, runners, font);
            }
            else
            {
                BuildStraight(parent, runners, font);
            }
        }

        // ------------------------------------------------------------------ straight

        private static void BuildStraight(
            RectTransform parent, IReadOnlyList<KeyValuePair<int, string>> runners, TMP_FontAsset font)
        {
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

            for (var lane = 0; lane < runners.Count; lane++)
            {
                BuildLane(parent, lane, runners[lane].Key, runners[lane].Value, font);
            }

            BuildStalls(parent);
            BuildPost(parent);
        }

        private static void BuildLane(
            RectTransform parent, int lane, int number, string name, TMP_FontAsset font)
        {
            var top = -(lane * LaneHeight) - 10f;

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
                StraightStart, top - ((LaneHeight - HorseSize) / 2f))));
        }

        /// <summary>The starting stalls, which is what the left edge of a sprint is.</summary>
        private static void BuildStalls(RectTransform parent)
        {
            var stalls = New("Stalls", parent);
            stalls.anchorMin = new Vector2(0f, 0f);
            stalls.anchorMax = new Vector2(0f, 1f);
            stalls.pivot = new Vector2(0f, 0.5f);
            stalls.anchoredPosition = new Vector2(StraightStart - (HorseSize * 0.5f) - 8f, 0f);
            stalls.sizeDelta = new Vector2(5f, -16f);

            var image = stalls.gameObject.AddComponent<Image>();
            image.color = new Color(Rail.r, Rail.g, Rail.b, 0.5f);
            image.raycastTarget = false;
        }

        private static void BuildPost(RectTransform parent)
        {
            var post = New("Post", parent);
            post.anchorMin = new Vector2(1f, 0f);
            post.anchorMax = new Vector2(1f, 1f);
            post.pivot = new Vector2(1f, 0.5f);
            post.anchoredPosition = new Vector2(-FinishInset, 0f);
            post.sizeDelta = new Vector2(3f, -12f);

            var image = post.gameObject.AddComponent<Image>();
            image.color = Post;
            image.raycastTarget = false;
        }

        /// <summary>Where a runner stands before the stalls open.</summary>
        private static float StraightStart => 250f;

        /// <summary>How far in from the right edge the post stands.</summary>
        private static float FinishInset => 76f;

        // ---------------------------------------------------------------------- oval

        private static void BuildOval(
            RectTransform parent, IReadOnlyList<KeyValuePair<int, string>> runners, TMP_FontAsset font)
        {
            // Laid out from the holder's own rect, which the panel forces a layout pass
            // on before calling this -- a freshly-created RectTransform reports zero.
            var width = parent.rect.width;
            var height = parent.rect.height;

            _ovalRadiusY = (height * 0.5f) - 14f;

            // **Capped against the height, not stretched to the width.** The holder is
            // most of a 1520-wide panel and only about 270 tall, so filling it would
            // give a six-to-one sliver that reads as a stadium rather than a racecourse
            // -- and would squash the runners flat on the bends, where they are most
            // bunched. Real courses are nearer two to one; 2.6 is as flat as this still
            // looks right.
            _ovalRadiusX = Mathf.Min((width * 0.5f) - 30f, _ovalRadiusY * 2.6f);

            _laneStep = (_ovalRadiusY * 0.26f) / Mathf.Max(1, runners.Count);

            // The running surface: a wide annulus, which is exactly what Ring draws.
            var surface = New("Surface", parent);
            Centre(surface, _ovalRadiusX * 2f, _ovalRadiusY * 2f);
            var surfaceImage = surface.gameObject.AddComponent<Image>();
            surfaceImage.sprite = Textures.Ring(Turf, 0.22f);
            surfaceImage.raycastTarget = false;

            var infield = New("Infield", parent);
            Centre(infield, _ovalRadiusX * 2f * 0.56f, _ovalRadiusY * 2f * 0.56f);
            var infieldImage = infield.gameObject.AddComponent<Image>();
            infieldImage.sprite = Textures.Ring(Infield, 0.5f);
            infieldImage.raycastTarget = false;

            // Outer and inner rails.
            var outerRail = New("OuterRail", parent);
            Centre(outerRail, _ovalRadiusX * 2f, _ovalRadiusY * 2f);
            var outerImage = outerRail.gameObject.AddComponent<Image>();
            outerImage.sprite = Textures.Ring(Rail, 0.007f);
            outerImage.raycastTarget = false;

            var innerRail = New("InnerRail", parent);
            Centre(innerRail, _ovalRadiusX * 2f * 0.56f, _ovalRadiusY * 2f * 0.56f);
            var innerImage = innerRail.gameObject.AddComponent<Image>();
            innerImage.sprite = Textures.Ring(Rail, 0.012f);
            innerImage.raycastTarget = false;

            // The post, on the right-hand straight where the runners start and finish.
            var post = New("Post", parent);
            Centre(post, 3f, _ovalRadiusY * 0.44f);
            post.anchoredPosition = new Vector2(_ovalRadiusX * 0.78f, 0f);
            var postImage = post.gameObject.AddComponent<Image>();
            postImage.color = Post;
            postImage.raycastTarget = false;

            // The placings, in a column to the left of the course.
            //
            // Not in the infield, which is where they went first: eight rows do not fit
            // inside an infield only 130 units tall, and capping the oval's width above
            // leaves a wide empty margin beside it that wants using. A straight has
            // lanes to write each runner's place beside; an oval has none, so it gets a
            // results board instead.
            var boardX = -(_ovalRadiusX + 150f);
            var boardTop = (runners.Count - 1) * 9f;

            var heading = New("PlacingsHeading", parent);
            Centre(heading, 220f, 18f);
            heading.anchoredPosition = new Vector2(boardX, boardTop + 26f);

            var headingText = heading.gameObject.AddComponent<TextMeshProUGUI>();
            headingText.font = font;
            headingText.fontSize = 12f;
            headingText.color = Faint;
            headingText.alignment = TextAlignmentOptions.Left;
            headingText.text = "FINISH";
            headingText.raycastTarget = false;

            for (var i = 0; i < runners.Count; i++)
            {
                var row = New($"Place{runners[i].Key}", parent);
                Centre(row, 220f, 18f);
                row.anchoredPosition = new Vector2(boardX, boardTop - (i * 18f));

                var text = row.gameObject.AddComponent<TextMeshProUGUI>();
                text.font = font;
                text.fontSize = 13f;
                text.color = Ink;
                text.alignment = TextAlignmentOptions.Left;
                text.text = string.Empty;
                text.raycastTarget = false;
                _places.Add(text);

                _runners.Add(MakeHorse(parent, runners[i].Key, font, Vector2.zero));
            }

            if (_laps > 1)
            {
                var lap = New("Lap", parent);
                Centre(lap, 240f, 22f);
                lap.anchoredPosition = Vector2.zero;

                _lapLabel = lap.gameObject.AddComponent<TextMeshProUGUI>();
                _lapLabel.font = font;
                _lapLabel.fontSize = 15f;
                _lapLabel.fontStyle = FontStyles.Bold;
                _lapLabel.color = Faint;
                _lapLabel.alignment = TextAlignmentOptions.Center;
                _lapLabel.text = $"LAP 1 OF {_laps}";
                _lapLabel.raycastTarget = false;
            }

            // Everyone to the start before the first frame, so a freshly-built oval
            // does not show eight horses piled at the centre.
            for (var lane = 0; lane < _runners.Count; lane++)
            {
                _runners[lane].anchoredPosition = OvalPoint(lane, 0f);
            }
        }

        /// <summary>
        /// Where a runner sits when it is <paramref name="progress"/> of the way round.
        ///
        /// Angle zero is the post, and the field runs anticlockwise from it. Each lane
        /// sits a little further in than the one outside it, which is what stops eight
        /// discs overlapping into one on the bends.
        /// </summary>
        private static Vector2 OvalPoint(int lane, float progress)
        {
            // The middle of the running surface, then stepped inwards per lane.
            var inset = lane * _laneStep;
            var rx = (_ovalRadiusX * 0.78f) - inset;
            var ry = (_ovalRadiusY * 0.78f) - inset;

            var angle = progress * _laps * 2f * Mathf.PI;

            return new Vector2(Mathf.Cos(angle) * rx, Mathf.Sin(angle) * ry);
        }

        private static void Centre(RectTransform rect, float width, float height)
        {
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = new Vector2(width, height);
        }

        // ------------------------------------------------------------------ the horse

        private static RectTransform MakeHorse(
            RectTransform parent, int number, TMP_FontAsset font, Vector2 at)
        {
            var horse = New($"Horse{number}", parent);

            if (_shape == Shape.Oval)
            {
                horse.anchorMin = new Vector2(0.5f, 0.5f);
                horse.anchorMax = new Vector2(0.5f, 0.5f);
                horse.pivot = new Vector2(0.5f, 0.5f);
            }
            else
            {
                horse.anchorMin = new Vector2(0f, 1f);
                horse.anchorMax = new Vector2(0f, 1f);
                horse.pivot = new Vector2(0.5f, 1f);
            }

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
        /// Abandons any running race and puts the field back to the start.
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
        /// Puts the field at the start and clears the placings, and touches nothing
        /// else. Safe from inside the race itself.
        /// </summary>
        private static void Rewind()
        {
            for (var lane = 0; lane < _runners.Count; lane++)
            {
                if (_runners[lane] == null)
                {
                    continue;
                }

                _runners[lane].anchoredPosition = _shape == Shape.Oval
                    ? OvalPoint(lane, 0f)
                    : new Vector2(StraightStart, _runners[lane].anchoredPosition.y);
            }

            foreach (var place in _places)
            {
                if (place != null)
                {
                    place.text = string.Empty;
                }
            }

            if (_lapLabel != null)
            {
                _lapLabel.text = $"LAP 1 OF {_laps}";
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
            var travel = Mathf.Max(80f, _track.rect.width - FinishInset - StraightStart);

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

                    var u = Mathf.Clamp01(elapsed / finish);
                    var progress = Mathf.Clamp01(Pace(u) + Jostle(number, elapsed, u));

                    runner.anchoredPosition = _shape == Shape.Oval
                        ? OvalPoint(lane, progress)
                        : new Vector2(StraightStart + (progress * travel), runner.anchoredPosition.y);
                }

                if (_lapLabel != null)
                {
                    // The leader's lap, which is the one a commentator would call.
                    var leader = Mathf.Clamp01(Pace(Mathf.Clamp01(elapsed / finishAt[order[0]])));
                    var lap = Mathf.Clamp(Mathf.FloorToInt(leader * _laps) + 1, 1, _laps);
                    _lapLabel.text = $"LAP {lap} OF {_laps}";
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
                    // A straight writes into the finisher's own lane; the oval's board
                    // is ordered by finishing position, so it writes into the row for
                    // that position instead.
                    var index = _shape == Shape.Oval ? place : order[place] - 1;

                    if (index >= 0 && index < _places.Count
                        && _places[index] != null
                        && _places[index].text.Length == 0
                        && elapsed >= finishAt[order[place]])
                    {
                        // On the oval the board is a results list, so each line has
                        // to name its runner; on the straight the line already sits in
                        // that runner's own lane.
                        _places[index].text = _shape == Shape.Oval
                            ? $"{Ordinal(place + 1),-5} {order[place]}  {NameOf(order[place])}"
                            : Ordinal(place + 1);
                    }
                }

                yield return null;
            }

            Finish(order, travel);

            _running = null;
            onDone?.Invoke();
        }

        /// <summary>
        /// Everybody home, whatever the frame timing did -- strung out in finishing
        /// order rather than stacked on the line.
        ///
        /// Snapping them all to the same point was the first version, and it drew eight
        /// discs in a column on the post: correct, and it threw away the one thing the
        /// picture is for. The winner now sits on the line and each place behind it is
        /// set back a little, so the frozen frame says who won without the player
        /// reading the placings beside it.
        ///
        /// Presentation only. The order here is taken from the same array the
        /// settlement used, so it cannot disagree with what was paid.
        /// </summary>
        private static void Finish(IReadOnlyList<int> order, float travel)
        {
            for (var place = 0; place < order.Count; place++)
            {
                var index = order[place] - 1;

                if (index < 0 || index >= _runners.Count || _runners[index] == null)
                {
                    continue;
                }

                if (_shape == Shape.Oval)
                {
                    // A whole number of laps is back at the post, so the trailing
                    // places are backed off by a fraction of a lap rather than by
                    // pixels -- which on a bend would otherwise push them off the turf.
                    _runners[index].anchoredPosition =
                        OvalPoint(index, 1f - (place * 0.012f / _laps));
                }
                else
                {
                    _runners[index].anchoredPosition = new Vector2(
                        StraightStart + travel - (place * 9f),
                        _runners[index].anchoredPosition.y);
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

        /// <summary>
        /// The jostle: what makes it a race rather than eight progress bars.
        ///
        /// **Multiplied by <c>(1 - u)^2</c>, which is exactly zero at the post.** That
        /// is what lets this be as ugly as it likes in the back straight without ever
        /// touching the finishing order. Nothing here needs to know who won.
        ///
        /// The two frequencies are deliberately not multiples of each other, so the
        /// field does not breathe in and out together like a concertina.
        /// </summary>
        private static float Jostle(int number, float elapsed, float u)
        {
            var fade = (1f - u) * (1f - u);
            var phase = number * 1.7f;

            var swing = (Mathf.Sin((elapsed * 1.9f) + phase) * 0.045f)
                + (Mathf.Sin((elapsed * 3.1f) + (phase * 0.6f)) * 0.022f);

            return swing * fade;
        }

        /// <summary>Runner names, kept so the oval's results board can print them.</summary>
        private static readonly Dictionary<int, string> _names = new Dictionary<int, string>();

        private static string NameOf(int number) =>
            _names.TryGetValue(number, out var name) ? name : string.Empty;

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
