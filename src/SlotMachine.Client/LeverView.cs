using System;
using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Casino.Shared;

namespace SlotMachine.Client
{
    /// <summary>
    /// The handle down the side of the machine.
    ///
    /// **A lever you can actually pull**, not a button wearing a lever's picture. Press
    /// it and drag down and the arm follows your hand; let go past the halfway mark and
    /// it fires and springs back. Let go short of it and it springs back without firing,
    /// which is the whole reason to build it this way -- a control you can begin and
    /// then not commit to is a different thing from a button, and this one spends money.
    ///
    /// A click without any drag still works. Somebody who does not realise it is
    /// draggable should not be locked out of the game, and there is a SPIN button
    /// beside it for the same reason.
    /// </summary>
    internal static class LeverView
    {
        /// <summary>How far the arm travels, in canvas units.</summary>
        private const float Throw = 132f;

        /// <summary>
        /// How far down counts as a pull, as a fraction of the throw.
        ///
        /// Just under half. Far enough that a twitch does not spend money, near enough
        /// that a real pull never feels like it failed.
        /// </summary>
        private const float Commit = 0.45f;

        private static readonly Color Rod = new Color(0.62f, 0.63f, 0.66f, 1f);
        private static readonly Color Mount = new Color(0.20f, 0.20f, 0.22f, 1f);
        private static readonly Color Knob = new Color(0.68f, 0.14f, 0.14f, 1f);
        private static readonly Color Edge = new Color(0.42f, 0.36f, 0.22f, 1f);

        /// <summary>
        /// Builds the lever.
        /// </summary>
        /// <param name="onPull">Fired once when a pull commits.</param>
        /// <param name="canPull">
        /// Asked before the arm will move at all, so the lever goes dead while the reels
        /// are still turning rather than queueing a second pull nobody asked for.
        /// </param>
        internal static GameObject Build(Transform parent, MonoBehaviour host, Action onPull, Func<bool> canPull)
        {
            var root = NewBox("Lever", parent, Color.clear);
            root.sizeDelta = new Vector2(88f, Throw + 190f);

            // The mount stays put; only the arm above it moves.
            var mount = NewBox("Mount", root, Color.white);
            mount.sizeDelta = new Vector2(46f, 74f);
            mount.anchoredPosition = new Vector2(0f, -(Throw * 0.5f) - 60f);
            var mountImage = mount.GetComponent<Image>();
            mountImage.sprite = Textures.RoundedBox(8, Mount, Edge, 3);
            mountImage.type = Image.Type.Sliced;

            var arm = NewBox("Arm", root, Color.clear);
            arm.sizeDelta = new Vector2(80f, Throw + 120f);
            arm.anchoredPosition = Vector2.zero;

            var rod = NewBox("Rod", arm, Color.white);
            rod.sizeDelta = new Vector2(16f, 150f);
            rod.anchoredPosition = new Vector2(0f, -34f);
            var rodImage = rod.GetComponent<Image>();
            rodImage.sprite = Textures.RoundedBox(6, Rod, Edge, 2);
            rodImage.type = Image.Type.Sliced;

            var knob = NewBox("Knob", arm, Color.white);
            knob.sizeDelta = new Vector2(62f, 62f);
            knob.anchoredPosition = new Vector2(0f, 56f);
            var knobImage = knob.GetComponent<Image>();
            knobImage.sprite = Textures.RoundedBox(31, Knob, Edge, 3);
            knobImage.type = Image.Type.Sliced;

            var handle = arm.gameObject.AddComponent<LeverHandle>();
            handle.Arm = arm;
            handle.Host = host;
            handle.OnPull = onPull;
            handle.CanPull = canPull;

            // The arm is transparent, so it needs something to be hit by a click. An
            // invisible graphic over the whole arm is what makes the rod draggable
            // rather than only the knob.
            var grab = arm.gameObject.AddComponent<Image>();
            grab.color = new Color(0f, 0f, 0f, 0.004f);
            grab.raycastTarget = true;

            return root.gameObject;
        }

        private static RectTransform NewBox(string name, Transform parent, Color colour)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.transform.SetParent(parent, false);

            var rect = (RectTransform)go.transform;
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);

            go.GetComponent<Image>().color = colour;

            return rect;
        }

        /// <summary>
        /// The arm's behaviour: follow the pointer down, fire if it went far enough,
        /// spring back either way.
        /// </summary>
        internal sealed class LeverHandle : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
        {
            internal RectTransform Arm;

            internal MonoBehaviour Host;

            internal Action OnPull;

            internal Func<bool> CanPull;

            private float _grabbedAt;
            private float _pulled;
            private bool _holding;
            private Coroutine _spring;

            public void OnPointerDown(PointerEventData eventData)
            {
                if (CanPull != null && !CanPull())
                {
                    return;
                }

                _holding = true;
                _grabbedAt = eventData.position.y;
                _pulled = 0f;

                if (_spring != null && Host != null)
                {
                    Host.StopCoroutine(_spring);
                    _spring = null;
                }
            }

            public void OnDrag(PointerEventData eventData)
            {
                if (!_holding)
                {
                    return;
                }

                // Screen pixels rather than canvas units, so the arm keeps up with the
                // pointer on any resolution. Only downward movement counts.
                var dragged = Mathf.Clamp(_grabbedAt - eventData.position.y, 0f, Throw);

                _pulled = dragged / Throw;
                Arm.anchoredPosition = new Vector2(0f, -dragged);
            }

            public void OnPointerUp(PointerEventData eventData)
            {
                if (!_holding)
                {
                    return;
                }

                _holding = false;

                // A click with no drag is still a pull. Anyone who does not realise the
                // lever moves should not be locked out of their own slot machine.
                var fired = _pulled >= Commit || _pulled <= 0.001f;

                if (Host != null)
                {
                    _spring = Host.StartCoroutine(SpringBack(fired));
                }
                else
                {
                    Arm.anchoredPosition = Vector2.zero;

                    if (fired)
                    {
                        OnPull?.Invoke();
                    }
                }
            }

            /// <summary>
            /// Lets the arm go, and fires at the moment it snaps rather than when the
            /// pointer was released. The reels starting as the handle flies back up is
            /// the whole feel of the thing.
            /// </summary>
            private IEnumerator SpringBack(bool fired)
            {
                var from = Arm.anchoredPosition.y;
                const float seconds = 0.16f;

                for (var t = 0f; t < seconds; t += Time.unscaledDeltaTime)
                {
                    Arm.anchoredPosition = new Vector2(
                        0f, Mathf.Lerp(from, 0f, Mathf.SmoothStep(0f, 1f, t / seconds)));

                    yield return null;
                }

                Arm.anchoredPosition = Vector2.zero;
                _pulled = 0f;
                _spring = null;

                if (fired)
                {
                    OnPull?.Invoke();
                }
            }
        }
    }
}
