using System.Collections;
using UnityEngine;

namespace Casino.Shared
{
    /// <summary>
    /// Slides and fades a just-built card into the resting spot its own slot already
    /// gave it, so a hand reads as being dealt out rather than appearing whole.
    ///
    /// Only the card's own local position, scale and alpha move. The slot around it
    /// (see <see cref="CardView.BuildSlotted"/>) keeps whatever anchored position the
    /// row or column laid it out at, so nothing here can throw a card row's spacing
    /// off -- the row was already measured and placed before this ever runs.
    /// </summary>
    internal static class DealAnimator
    {
        /// <summary>What a caller multiplies a card's place in the deal by.</summary>
        internal const float CardStagger = 0.08f;

        private const float Duration = 0.22f;
        private const float DropDistance = 60f;
        private const float StartScale = 0.55f;

        /// <summary>
        /// Plays after <paramref name="delay"/> seconds, so a caller can stagger a
        /// whole hand or a whole table by handing each card a later delay than the
        /// last -- see <see cref="CardStagger"/>. Falls back to leaving the card
        /// exactly where it already is if there is no plugin instance to run a
        /// coroutine on: a card that never animates in is still a dealt card.
        /// </summary>
        internal static void Deal(GameObject card, float delay)
        {
            var host = Host.Plugin;
            var rect = card == null ? null : card.transform as RectTransform;

            if (host == null || rect == null)
            {
                return;
            }

            host.StartCoroutine(Animate(rect, delay));
        }

        private static IEnumerator Animate(RectTransform rect, float delay)
        {
            var restPosition = rect.anchoredPosition;
            var restScale = rect.localScale;
            var startPosition = restPosition + new Vector2(0f, DropDistance);
            var startScale = restScale * StartScale;

            var group = rect.GetComponent<CanvasGroup>();
            if (group == null)
            {
                group = rect.gameObject.AddComponent<CanvasGroup>();
            }

            rect.anchoredPosition = startPosition;
            rect.localScale = startScale;
            group.alpha = 0f;

            var waited = 0f;
            while (waited < delay)
            {
                waited += Time.unscaledDeltaTime;
                yield return null;
            }

            // The row may have been torn down and rebuilt -- another server reply
            // landed while this card was waiting its turn -- in which case the
            // RectTransform now reads as destroyed and there is nothing left to
            // animate. Unity's own == catches that; a plain reference check would not.
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

                rect.anchoredPosition = Vector2.Lerp(startPosition, restPosition, t);
                rect.localScale = Vector3.Lerp(startScale, restScale, t);
                group.alpha = t;

                yield return null;
            }

            rect.anchoredPosition = restPosition;
            rect.localScale = restScale;
            group.alpha = 1f;
        }

        private static float EaseOut(float t) => 1f - ((1f - t) * (1f - t));
    }
}
