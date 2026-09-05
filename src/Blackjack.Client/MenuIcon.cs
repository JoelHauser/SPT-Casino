using System;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace Blackjack.Client
{
    /// <summary>
    /// Puts a card suit on the task-bar tab, which is a clone of one of the game's own
    /// and therefore arrives wearing somebody else's icon.
    ///
    /// It used to serve a main-menu button as well; that entrance has been removed, and
    /// the defensive shape and size handling below is written as if there were still two
    /// callers because it was two callers that found every bug in it.
    /// </summary>
    internal static class MenuIcon
    {
        /// <summary>
        /// Swaps the borrowed icon for a diamond.
        ///
        /// A clone wears whatever icon it copied, so without this the BLACKJACK entry
        /// carries the hideout's or the handbook's. Blanking it is not the answer
        /// either: with a menu mod installed the icon is the button's main visual and
        /// the others would all have one, leaving ours conspicuously bare. A suit is
        /// drawn by the same code that draws the cards, so it needs no art shipped and
        /// looks deliberate either way.
        ///
        /// The diamond specifically, because it is the only suit with no up or down. A
        /// spade inheriting a mirrored or rotated transform from the icon it replaced
        /// comes out looking like a trophy; a rhombus cannot.
        ///
        /// The container is left alone whatever happens, because its size is part of
        /// the row's spacing.
        /// </summary>
        internal static void Diamond(Component owner)
        {
            if (owner == null)
            {
                return;
            }

            var images = owner.GetComponentsInChildren<Image>(true)
                .Where(i => i != null)
                .ToList();

            var icons = images
                .Where(i => i.name.IndexOf("icon", StringComparison.OrdinalIgnoreCase) >= 0)
                .ToList();

            // Nothing called an icon does not mean there is no icon. The task-bar tabs
            // name theirs after the screen they open, so fall back to shape: the small
            // square graphic that is not the button's own background.
            if (icons.Count == 0)
            {
                icons = images.Where(i => LooksLikeAPip(i, owner)).ToList();
            }

            if (icons.Count == 0)
            {
                return;
            }

            var pip = Textures.Suit('D', Color.white);

            // **A DefaultUIButton carries two icons, not one** -- `_iconImage` and
            // `_iconIdleImage`, swapped by its own PointerEnter and PointerExit handlers.
            // Both are replaced, which is why the idle pip looked right; the hover one is
            // hidden when this runs, has never been through a layout pass, and reports a
            // rect it will never be drawn at. Sized from that, the pip stretched across it
            // -- which is what "the icon splits and becomes two" was.
            //
            // A square is what both get, and squareness rather than a size is the point:
            // a square sprite in a square rect cannot be stretched by anything, whatever
            // an Image or its parents do about aspect. The side is the smaller dimension
            // of whichever icon the layout has actually measured, so the pip fits the slot
            // the borrowed icon had rather than growing into it.
            var side = 0f;
            foreach (var icon in icons)
            {
                var size = icon.rectTransform.rect.size;
                if (size.x > 1f && size.y > 1f)
                {
                    side = Mathf.Max(side, Mathf.Min(size.x, size.y));
                }
            }

            foreach (var icon in icons)
            {
                var rect = icon.rectTransform;

                // Whatever the borrowed icon was, it may have been rotated or mirrored
                // to suit its own artwork, and a spade inherits that and comes out
                // upside down. Reported as well as reset, because a rotation here is
                // worth knowing about rather than silently undoing.
                if (rect.localRotation != Quaternion.identity ||
                    rect.localScale.x < 0f || rect.localScale.y < 0f)
                {
                    BlackjackClientPlugin.Log.LogInfo(
                        $"[Blackjack] icon '{icon.name}' had rotation {rect.localEulerAngles} " +
                        $"scale {rect.localScale}; normalising.");
                }

                rect.localRotation = Quaternion.identity;
                rect.localScale = new Vector3(
                    Mathf.Abs(rect.localScale.x),
                    Mathf.Abs(rect.localScale.y),
                    Mathf.Abs(rect.localScale.z));

                icon.color = Color.white;
                icon.sprite = pip;

                // Simple before preserveAspect, because preserveAspect is ignored outright
                // on a Sliced or Tiled Image -- which is the only way a square sprite in a
                // square rect could still come out the wrong shape. A pip has no
                // nine-slice border to lose by saying so.
                icon.type = Image.Type.Simple;
                icon.preserveAspect = true;

                Pin(icon, side);
            }
        }

        /// <summary>
        /// Holds the icon to the footprint of the one it replaced.
        ///
        /// **An Image reports its sprite's native size as its layout-preferred size**,
        /// and a layout group believes it. The pip is drawn 160 pixels square against a
        /// canvas at 100 reference pixels per unit, so it asks for 160 units where the
        /// hideout's own icon asked for 25 -- and both of this mod's entrances were
        /// misshapen by that one number, in ways that looked unrelated:
        ///
        /// - The task-bar tab came out **230 wide against the game's 112**, which read as
        ///   a font or padding fault and cost a round of fixes aimed at both. The label
        ///   was innocent throughout: 16pt on the template and 16pt on ours, and ours the
        ///   narrower of the two. It took logging the widths to say so.
        /// - The menu button's icon **blew up on hover**, when the hover state swapped in
        ///   the second Image, which had never been measured and so had never been held
        ///   to anything.
        ///
        /// Pinned both ways because the two entrances are laid out differently: a
        /// LayoutElement for the parent that measures, an explicit size for the one that
        /// does not. Square, so that nothing downstream can stretch the pip -- see
        /// <see cref="Diamond"/>. A button whose icons have none of them been laid out yet
        /// is left alone: pinning zero would hide the pip rather than size it.
        /// </summary>
        private static void Pin(Image icon, float side)
        {
            if (side <= 1f)
            {
                return;
            }

            var hold = icon.GetComponent<LayoutElement>();
            if (hold == null)
            {
                hold = icon.gameObject.AddComponent<LayoutElement>();
            }

            hold.preferredWidth = side;
            hold.preferredHeight = side;

            // SetSizeWithCurrentAnchors rather than sizeDelta, which does not mean a size
            // at all on a rect that stretches with its parent -- and an icon anchored
            // that way would be inflated by the padding rather than pinned.
            var rect = icon.rectTransform;
            rect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, side);
            rect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, side);
        }

        /// <summary>
        /// A graphic small enough and square enough to be an icon rather than the
        /// button's background or its label's backing plate.
        ///
        /// Both tests matter. Area alone catches a thin divider; aspect alone catches a
        /// square button. Requiring both leaves the pip.
        /// </summary>
        private static bool LooksLikeAPip(Image image, Component owner)
        {
            var rect = image.rectTransform;
            var root = owner is RectTransform asRect ? asRect : owner.GetComponent<RectTransform>();
            if (root == null || rect == root)
            {
                return false;
            }

            var size = rect.rect.size;
            var whole = root.rect.size;
            if (size.x <= 1f || size.y <= 1f || whole.x <= 1f || whole.y <= 1f)
            {
                return false;
            }

            var aspect = size.x / size.y;
            var share = (size.x * size.y) / (whole.x * whole.y);

            return aspect > 0.6f && aspect < 1.7f && share < 0.45f;
        }
    }
}
