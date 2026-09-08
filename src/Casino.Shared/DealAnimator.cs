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
        internal const float CardStagger = 0.1f;

        private const float Duration = 0.3f;
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

        private static float EaseOut(float t) => 1f - ((1f - t) * (1f - t));
    }
}
