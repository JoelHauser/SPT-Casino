using System;
using System.Collections;
using UnityEngine;

namespace Casino.Shared
{
    /// <summary>
    /// Slides a just-built card in from a shared dealer point to the resting spot its
    /// own slot already gave it, fading and scaling up as it travels -- so a hand reads
    /// as a dealer working the table rather than cards fading in where they land.
    ///
    /// Moves the card's world position, not <c>anchoredPosition</c>. The card sits
    /// inside a slot a layout group owns (see <see cref="CardView.BuildSlotted"/>), and
    /// a layout rebuild mid-flight -- Blackjack's <c>FitHands</c> forces one on every
    /// redraw -- recomputes anchoredPosition from the slot's own layout and would undo
    /// an animation living there. World position is outside anything a layout group
    /// touches, so the slide survives redraws the same way the fade always did.
    /// </summary>
    internal static class DealAnimator
    {
        /// <summary>What a caller multiplies a card's place in the deal by.</summary>
        internal const float CardStagger = 0.3f;

        private const float Duration = 0.5f;
        private const float StartScale = 0.6f;

        /// <summary>
        /// Plays after <paramref name="delay"/> seconds, so a caller can stagger a
        /// whole hand or a whole table by handing each card a later delay than the
        /// last -- see <see cref="CardStagger"/>. <paramref name="origin"/> is where the
        /// card slides in from -- a fixed "the dealer is standing here" marker for
        /// Poker, the dealer's own card row for Blackjack -- so every card in one deal
        /// visibly comes from the same place. Falls back to leaving the card exactly
        /// where it already is if there is no plugin instance to run a coroutine on, or
        /// no origin to slide in from: a card that never animates in is still a dealt
        /// card.
        /// </summary>
        internal static void Deal(GameObject card, float delay, RectTransform origin)
        {
            var host = Host.Plugin;
            var rect = card == null ? null : card.transform as RectTransform;

            if (host == null || rect == null || origin == null)
            {
                return;
            }

            host.StartCoroutine(Animate(rect, delay, origin));
        }

        private static IEnumerator Animate(RectTransform rect, float delay, RectTransform origin)
        {
            var group = rect.GetComponent<CanvasGroup>();
            if (group == null)
            {
                group = rect.gameObject.AddComponent<CanvasGroup>();
            }

            // Hidden before anything reads a position: this card, its slot and the
            // origin marker were very possibly all built this same frame, and Unity
            // does not lay out a fresh hierarchy until its own end-of-frame pass, so a
            // world position read right now cannot be trusted yet.
            group.alpha = 0f;

            yield return null;

            // The row may have been torn down and rebuilt before that frame even
            // finished -- another server reply landing on top of this one -- in which
            // case the RectTransform now reads as destroyed. Unity's own == catches
            // that; a plain reference check would not.
            if (rect == null)
            {
                yield break;
            }

            var restWorld = rect.position;
            var restScale = rect.localScale;
            var startWorld = origin == null ? restWorld : origin.position;
            var startScale = restScale * StartScale;

            rect.position = startWorld;
            rect.localScale = startScale;

            var waited = 0f;
            while (waited < delay)
            {
                waited += Time.unscaledDeltaTime;
                yield return null;
            }

            if (rect == null)
            {
                yield break;
            }

            var elapsed = 0f;
            while (elapsed < Duration)
            {
                elapsed += Time.unscaledDeltaTime;
                var t = EaseOut(Mathf.Clamp01(elapsed / Duration));

                if (rect == null)
                {
                    yield break;
                }

                rect.position = Vector3.Lerp(startWorld, restWorld, t);
                rect.localScale = Vector3.Lerp(startScale, restScale, t);
                group.alpha = t;

                yield return null;
            }

            rect.position = restWorld;
            rect.localScale = restScale;
            group.alpha = 1f;
        }

        /// <summary>
        /// Turns a card over in place -- shrinks <paramref name="back"/> to edge-on,
        /// swaps it for <paramref name="face"/>, then grows that back out to full width.
        /// <see cref="Deal"/> is the wrong shape for this: a reveal is not a card
        /// arriving from anywhere, it is one already on the table turning over, so
        /// nothing here moves position and only the width -- not the height -- scales,
        /// which is what reads as a card rotating on its long axis rather than one
        /// simply shrinking.
        ///
        /// <paramref name="back"/> is spent by this call: it exists only to be the
        /// thing shrinking away, and this destroys it partway through. Build it with
        /// <see cref="CardView.AddBackTo"/> immediately before calling, never reused.
        /// </summary>
        internal static void Flip(GameObject face, GameObject back, float delay)
        {
            var host = Host.Plugin;
            var faceRect = face == null ? null : face.transform as RectTransform;
            var backRect = back == null ? null : back.transform as RectTransform;

            if (host == null || faceRect == null || backRect == null)
            {
                return;
            }

            host.StartCoroutine(AnimateFlip(faceRect, backRect, delay));
        }

        private static IEnumerator AnimateFlip(RectTransform face, RectTransform back, float delay)
        {
            // The rest scale is whatever the slot already gave the face card -- read
            // once, before anything is hidden, the same number CardSlot/BuildSlotted
            // set synchronously at creation. Unlike Deal's world position, localScale
            // is not something a layout pass computes later, so there is nothing to
            // wait a frame for here.
            var restScale = face.localScale;
            back.localScale = restScale;
            face.gameObject.SetActive(false);

            var waited = 0f;
            while (waited < delay)
            {
                waited += Time.unscaledDeltaTime;
                yield return null;
            }

            if (face == null || back == null)
            {
                yield break;
            }

            var half = Duration * 0.5f;
            var elapsed = 0f;

            // The back shrinks to edge-on. Only x moves -- the height of a card does
            // not change as it turns face down to face up, only how wide it reads.
            while (elapsed < half)
            {
                elapsed += Time.unscaledDeltaTime;
                var t = EaseIn(Mathf.Clamp01(elapsed / half));

                if (back == null)
                {
                    yield break;
                }

                back.localScale = new Vector3(restScale.x * (1f - t), restScale.y, 1f);
                yield return null;
            }

            if (back != null)
            {
                UnityEngine.Object.Destroy(back.gameObject);
            }

            if (face == null)
            {
                yield break;
            }

            // The swap happens at the edge-on instant, where the card reads as a
            // sliver too thin to show a face either way -- the one moment a hard cut
            // between two different GameObjects cannot be seen happening.
            face.gameObject.SetActive(true);
            face.localScale = new Vector3(0f, restScale.y, 1f);

            elapsed = 0f;
            while (elapsed < half)
            {
                elapsed += Time.unscaledDeltaTime;
                var t = EaseOut(Mathf.Clamp01(elapsed / half));

                if (face == null)
                {
                    yield break;
                }

                face.localScale = new Vector3(restScale.x * t, restScale.y, 1f);
                yield return null;
            }

            face.localScale = restScale;
        }

        /// <summary>
        /// When a card started with <paramref name="delay"/> will actually have
        /// finished moving -- <see cref="Deal"/> and <see cref="Flip"/> take the same
        /// total time, so one formula covers both. For a caller that needs to hold
        /// something back until every card in a redraw is done: an outcome the player
        /// should not read before they can see the card that decided it.
        /// </summary>
        internal static float FinishTime(float delay) => delay + Duration;

        /// <summary>
        /// Runs <paramref name="action"/> once, <paramref name="delay"/> seconds from
        /// now -- see <see cref="FinishTime"/>. Runs it immediately if there is no
        /// plugin instance to run a coroutine on, the same fallback every other delay
        /// in this class uses: something that cannot be timed is still shown, just not
        /// late.
        /// </summary>
        internal static void After(float delay, Action action)
        {
            var host = Host.Plugin;

            if (host == null)
            {
                action?.Invoke();
                return;
            }

            host.StartCoroutine(Wait(delay, action));
        }

        private static IEnumerator Wait(float delay, Action action)
        {
            var waited = 0f;
            while (waited < delay)
            {
                waited += Time.unscaledDeltaTime;
                yield return null;
            }

            action?.Invoke();
        }

        private static float EaseOut(float t) => 1f - ((1f - t) * (1f - t));

        private static float EaseIn(float t) => t * t;
    }
}
