using System;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace Blackjack.Client
{
    /// <summary>
    /// Puts a card suit on a button cloned from one of EFT's own.
    ///
    /// Shared by the main-menu button and the task-bar tab, because both are copies of
    /// a neighbour and both therefore arrive wearing somebody else's icon.
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

            foreach (var icon in icons)
            {
                var rect = icon.rectTransform;

                // The footprint the icon we are replacing occupies, read before the
                // swap. See Pin: the sprite going in is a different size and a layout
                // group will believe it.
                var was = rect.rect.size;

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
                icon.preserveAspect = true;

                Pin(icon, was);
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
        /// - The menu button's icon **blew up on hover**, when whatever the hover state
        ///   dirties let the Image have the width it had been asking for all along. A
        ///   diamond magnified sixfold and cropped to its middle is a band rather than a
        ///   rhombus, which is why the icon looked pulled apart.
        ///
        /// Pinned both ways because the two entrances are laid out differently: a
        /// LayoutElement for the parent that measures, an explicit size for the one that
        /// does not. A footprint that has not been laid out yet is left alone -- pinning
        /// zero would hide the icon rather than size it.
        /// </summary>
        private static void Pin(Image icon, Vector2 was)
        {
            if (was.x <= 1f || was.y <= 1f)
            {
                return;
            }

            var hold = icon.GetComponent<LayoutElement>();
            if (hold == null)
            {
                hold = icon.gameObject.AddComponent<LayoutElement>();
            }

            hold.preferredWidth = was.x;
            hold.preferredHeight = was.y;

            // SetSizeWithCurrentAnchors rather than sizeDelta, which does not mean a size
            // at all on a rect that stretches with its parent -- and an icon anchored
            // that way would be inflated by the padding rather than pinned.
            var rect = icon.rectTransform;
            rect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, was.x);
            rect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, was.y);
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
