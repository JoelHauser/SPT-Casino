using UnityEngine;
using UnityEngine.UI;

namespace Casino.Shared
{
    /// <summary>
    /// Lighting a button up under the cursor.
    ///
    /// Unity's default is <see cref="Selectable.Transition.ColorTint"/>, which
    /// multiplies the whole graphic by a colour. On a sliced sprite with a gold edge
    /// that dims the edge along with everything else, so the button gets *duller* on
    /// hover rather than brighter. Every table here swaps the sprite instead: a face
    /// drawn lighter, with the edge lit to the table's accent.
    ///
    /// ## Why this is shared and Blackjack's copy is not
    ///
    /// Blackjack worked this out first and has its own private version of it, which is
    /// where the numbers below come from -- lift the fill about a tenth, light the edge
    /// gold, and on press darken and flatten so the face reads as going *in* rather
    /// than merely changing colour. Slots never got any of it and Horse Racing was
    /// written without it.
    ///
    /// Rather than copy it a third time this is the one copy the new work uses.
    /// Blackjack's is deliberately left alone: it is a shipped table, its version is
    /// working, and folding it in is a change to a money table for no visible gain. If
    /// anybody does fold it in later, the thing to preserve is that its buttons take a
    /// two-stop face (<see cref="Textures.ButtonFace"/>) while these take a flat one.
    /// </summary>
    internal static class Hover
    {
        /// <summary>How much lighter the face gets under the cursor.</summary>
        private const float HoverLift = 0.10f;

        /// <summary>How much darker it goes while held down.</summary>
        private const float PressDrop = 0.06f;

        /// <summary>
        /// Lights a button whose face is a flat <see cref="Textures.RoundedBox"/>.
        ///
        /// Safe to call more than once on the same button, and safe to call before the
        /// <see cref="Button"/> exists -- it sets the resting sprite either way and
        /// only wires the swap when there is something to wire it to. Both matter for
        /// a face that changes with state: the slot machine's SPIN button is red when
        /// it is ready and grey when it is not, and has to be re-lit each time.
        /// </summary>
        internal static void Rounded(
            Image face, int radius, Color fill, Color edge, Color accent, int border = 2)
        {
            if (face == null)
            {
                return;
            }

            var normal = Textures.RoundedBox(radius, fill, edge, border);

            face.sprite = normal;
            face.type = Image.Type.Sliced;

            Wire(
                face,
                normal,
                Textures.RoundedBox(radius, Lift(fill, HoverLift), accent, border),
                Textures.RoundedBox(radius, Lift(fill, -PressDrop), accent, border));
        }

        /// <summary>
        /// Lights a button whose face is a two-stop <see cref="Textures.ButtonFace"/>.
        /// </summary>
        internal static void Faced(
            Image face, int radius, Color top, Color bottom, Color edge, Color accent, int border = 2)
        {
            if (face == null)
            {
                return;
            }

            var normal = Textures.ButtonFace(radius, top, bottom, edge, border);

            face.sprite = normal;
            face.type = Image.Type.Sliced;

            Wire(
                face,
                normal,
                Textures.ButtonFace(radius, Lift(top, HoverLift), Lift(bottom, HoverLift * 0.8f), accent, border),

                // Pressed flattens as well as darkens -- both stops come from the
                // bottom one, so the face loses its lit top and reads as pushed in.
                Textures.ButtonFace(radius, Lift(bottom, 0.02f), Lift(bottom, -0.02f), accent, border));
        }

        private static void Wire(Image face, Sprite normal, Sprite hover, Sprite pressed)
        {
            var button = face.GetComponent<Button>();

            if (button == null)
            {
                return;
            }

            // targetGraphic has to be this image or the swap lands on nothing. Set
            // here rather than trusted from the caller, because a button built without
            // one silently never highlights and looks exactly like a button that does.
            button.targetGraphic = face;

            button.transition = Selectable.Transition.SpriteSwap;
            button.spriteState = new SpriteState
            {
                highlightedSprite = hover,
                pressedSprite = pressed,
                selectedSprite = normal,
                disabledSprite = normal,
            };
        }

        /// <summary>Lightens or darkens a colour, keeping its alpha.</summary>
        internal static Color Lift(Color colour, float amount) => new Color(
            Mathf.Clamp01(colour.r + amount),
            Mathf.Clamp01(colour.g + amount),
            Mathf.Clamp01(colour.b + amount),
            colour.a);
    }
}
