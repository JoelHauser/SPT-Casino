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
    /// The track: eight lanes seen from the side, running left to right.
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
    /// That is deliberately a stronger guarantee than "the winner is nudged ahead at
    /// the end". Roulette's wheel had to learn the same lesson from the other side: the
    /// ball's landing frame is the result, not a frame near it.
    /// </summary>
    internal static class TrackView
    {
        /// <summary>How long the field takes to get home, in seconds.</summary>
        private const float Duration = 6.0f;

        /// <summary>
        /// How much later each place finishes, as a fraction of <see cref="Duration"/>.
        ///
        /// Small on purpose. Eight runners spread over 1.8% each puts the last horse
        /// about three quarters of a second behind the winner, which reads as a field
        /// crossing the line rather than a procession -- and still leaves every gap
        /// several frames wide at 60fps, so two horses never appear to dead-heat.
        /// </summary>
        private const float PlaceGap = 0.018f;

        private const float LaneHeight = 34f;

        private const float HorseSize = 26f;

        private static readonly Color Rail = new Color(0.16f, 0.17f, 0.15f, 1f);
        private static readonly Color Turf = new Color(0.10f, 0.14f, 0.10f, 1f);
        private static readonly Color Post = new Color(0.86f, 0.24f, 0.24f, 0.9f);
        private static readonly Color Ink = new Color(0.88f, 0.86f, 0.80f, 1f);

        /// <summary>
        /// The silks, one per saddlecloth number.
        ///
        /// Eight colours that stay apart from each other at 26 pixels and on a dark
        /// green background. Not generated from a hue wheel: an even spread puts two
        /// of them in the greens, which is precisely where the turf is.
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
        private static readonly List<RectTransform> _runners = new List<RectTransform>();
        private static readonly List<TextMeshProUGUI> _places = new List<TextMeshProUGUI>();
        private static Coroutine _running;

        /// <summary>
        /// Whether a race is on.
        ///
        /// Read by the panel to refuse a second slip mid-race and to hold the escape
        /// key -- a table that vanishes while the horses are running looks like a
        /// crash, which is exactly what roulette found with its wheel.
        /// </summary>
        internal static bool IsRunning => _running != null;

        /// <summary>
        /// Builds the lanes. Called once, from the panel's own Build.
        /// </summary>
        /// <param name="parent">The holder the track fills.</param>
        /// <param name="runners">Saddlecloth number and name, in card order.</param>
        /// <param name="font">The panel's font, so the track does not load its own.</param>
        internal static void Build(
            RectTransform parent, IReadOnlyList<KeyValuePair<int, string>> runners, TMP_FontAsset font)
        {
            _track = parent;
            _runners.Clear();
            _places.Clear();

            foreach (Transform child in parent)
            {
                UnityEngine.Object.Destroy(child.gameObject);
            }

            var turf = New("Turf", parent);
            Stretch(turf);
            turf.gameObject.AddComponent<Image>().sprite =
                Textures.RoundedBox(10, Turf, Rail, 2);
            turf.GetComponent<Image>().type = Image.Type.Sliced;

            for (var lane = 0; lane < runners.Count; lane++)
            {
                BuildLane(parent, lane, runners.Count, runners[lane].Key, runners[lane].Value, font);
            }

            BuildPost(parent);
        }

        private static void BuildLane(
            RectTransform parent, int lane, int lanes, int number, string name, TMP_FontAsset font)
        {
            var top = -(lane * LaneHeight) - 8f;

            // The rail under this lane, so eight horses on green do not read as one
            // crowd. Alternating rather than every lane: a line under every runner is
            // a grid, and a grid is what the betting board already looks like.
            if (lane % 2 == 1)
            {
                var stripe = New($"Stripe{number}", parent);
                stripe.anchorMin = new Vector2(0f, 1f);
                stripe.anchorMax = new Vector2(1f, 1f);
                stripe.pivot = new Vector2(0.5f, 1f);
                stripe.anchoredPosition = new Vector2(0f, top);
                stripe.sizeDelta = new Vector2(-16f, LaneHeight);

                var image = stripe.gameObject.AddComponent<Image>();
                image.color = new Color(1f, 1f, 1f, 0.03f);
            }

            var label = New($"Name{number}", parent);
            label.anchorMin = new Vector2(0f, 1f);
            label.anchorMax = new Vector2(0f, 1f);
            label.pivot = new Vector2(0f, 1f);
            label.anchoredPosition = new Vector2(14f, top - 6f);
            label.sizeDelta = new Vector2(230f, LaneHeight - 12f);

            var text = label.gameObject.AddComponent<TextMeshProUGUI>();
            text.font = font;
            text.fontSize = 13f;
            text.color = new Color(Ink.r, Ink.g, Ink.b, 0.55f);
            text.alignment = TextAlignmentOptions.Left;
            text.text = $"{number}  {name}";

            // Where the place is written as they cross the line. Built empty and
            // filled by Run, so it says nothing about a race that has not happened.
            var place = New($"Place{number}", parent);
            place.anchorMin = new Vector2(1f, 1f);
            place.anchorMax = new Vector2(1f, 1f);
            place.pivot = new Vector2(1f, 1f);
            place.anchoredPosition = new Vector2(-14f, top - 6f);
            place.sizeDelta = new Vector2(60f, LaneHeight - 12f);

            var placeText = place.gameObject.AddComponent<TextMeshProUGUI>();
            placeText.font = font;
            placeText.fontSize = 15f;
            placeText.fontStyle = FontStyles.Bold;
            placeText.color = Ink;
            placeText.alignment = TextAlignmentOptions.Right;
            placeText.text = string.Empty;
            _places.Add(placeText);

            var horse = New($"Horse{number}", parent);
            horse.anchorMin = new Vector2(0f, 1f);
            horse.anchorMax = new Vector2(0f, 1f);
            horse.pivot = new Vector2(0.5f, 1f);
            horse.anchoredPosition = new Vector2(Start, top - ((LaneHeight - HorseSize) / 2f));
            horse.sizeDelta = new Vector2(HorseSize, HorseSize);

            var silk = horse.gameObject.AddComponent<Image>();
            silk.sprite = Textures.RoundedBox(14, Silks[(number - 1) % Silks.Length], Rail, 2);
            silk.type = Image.Type.Sliced;

            var cloth = New("Number", horse);
            Stretch(cloth);

            var clothText = cloth.gameObject.AddComponent<TextMeshProUGUI>();
            clothText.font = font;
            clothText.fontSize = 15f;
            clothText.fontStyle = FontStyles.Bold;

            // Dark text on the pale silks, pale on the dark ones. Worked out from the
            // silk rather than picked per horse, so a recoloured runner stays legible.
            var silkColour = Silks[(number - 1) % Silks.Length];
            var luminance = (0.299f * silkColour.r) + (0.587f * silkColour.g) + (0.114f * silkColour.b);
            clothText.color = luminance > 0.55f ? new Color(0.1f, 0.1f, 0.1f, 1f) : Color.white;

            clothText.alignment = TextAlignmentOptions.Center;
            clothText.text = number.ToString();

            _runners.Add(horse);
        }

        private static void BuildPost(RectTransform parent)
        {
            var post = New("Post", parent);
            post.anchorMin = new Vector2(1f, 0f);
            post.anchorMax = new Vector2(1f, 1f);
            post.pivot = new Vector2(1f, 0.5f);
            post.anchoredPosition = new Vector2(-FinishInset, 0f);
            post.sizeDelta = new Vector2(3f, -10f);

            post.gameObject.AddComponent<Image>().color = Post;
        }

        /// <summary>Where a runner stands before the stalls open.</summary>
        private static float Start => 96f;

        /// <summary>How far in from the right edge the post stands.</summary>
        private static float FinishInset => 86f;

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
                finishAt[order[place]] = Duration * (1f + (place * PlaceGap));
            }

            var lastHome = Duration * (1f + ((order.Count - 1) * PlaceGap));
            var travel = Mathf.Max(80f, _track.rect.width - FinishInset - Start);

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

                    runner.anchoredPosition = new Vector2(
                        Start + (progress * travel), runner.anchoredPosition.y);
                }

                // The winner passing the post, which is the moment worth hearing --
                // not the end of the coroutine, three quarters of a second later.
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

            // Everybody home, whatever the frame timing did -- but strung out in
            // finishing order rather than stacked on the line.
            //
            // Snapping them all to the same x was the first version, and it drew eight
            // discs in a vertical column on the post: correct, and it threw away the
            // one thing the picture is for. The winner now sits on the line and each
            // place behind it is set back a little, so the frozen frame says who won
            // without the player reading the 1st/2nd/3rd column beside it.
            //
            // Presentation only. The order here is taken from the same array the
            // settlement used, so it cannot disagree with what was paid.
            for (var place = 0; place < order.Count; place++)
            {
                var index = order[place] - 1;

                if (index >= 0 && index < _runners.Count && _runners[index] != null)
                {
                    _runners[index].anchoredPosition = new Vector2(
                        Start + travel - (place * 9f), _runners[index].anchoredPosition.y);
                }
            }

            _running = null;
            onDone?.Invoke();
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
