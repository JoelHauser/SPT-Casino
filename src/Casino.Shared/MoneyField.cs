using System;
using TMPro;
using UnityEngine;

namespace Casino.Shared
{
    /// <summary>
    /// A box you type an amount of money into.
    ///
    /// Two tables wanted this and each had grown half of it. Blackjack could write
    /// `1,250,000` and keep the caret in the right place while you typed it, but its
    /// caret was the default single pixel and effectively invisible. Slots had a caret
    /// you could see but no separators, so a stake read `1250000` and had to be counted
    /// by eye. Neither half is hard; both halves in two places is how they drift.
    ///
    /// ## The caret has to be built, not assumed
    ///
    /// TMP gives an input field a one-pixel caret in the text's own colour, which on a
    /// dark box at 1440p is not there as far as anybody is concerned. Making it visible
    /// takes three separate things, and the first two alone were not enough:
    ///
    /// * a caret several pixels wide, in an accent colour, blinking slowly enough to be
    ///   on most of the time;
    /// * **selecting the whole number on focus**, which paints a block exactly where the
    ///   click landed -- this is the part that actually answers "did my click work", and
    ///   it is also what a player wants, because clicking the stake means replacing it;
    /// * the field's own border lighting up, so the box looks focused even in the moment
    ///   between the caret's blinks.
    ///
    /// ## Separators move the caret, so the caret is counted in digits
    ///
    /// Inserting a comma to the left of the caret shifts every character after it. A
    /// caret kept by character index therefore walks backwards a place each time the
    /// number crosses a thousand -- which is exactly when somebody is still typing. So
    /// the position is converted to "how many digits are behind me", the text is
    /// reformatted, and the caret is put back after that many digits.
    /// </summary>
    internal static class MoneyField
    {
        /// <summary>
        /// Makes an input field's caret something a player can actually see.
        /// </summary>
        /// <param name="accent">The colour of the caret and of the selection block.</param>
        internal static void MakeCaretVisible(TMP_InputField input, Color accent)
        {
            if (input == null)
            {
                return;
            }

            input.customCaretColor = true;
            input.caretColor = accent;

            // Four, because one is invisible and two is a hairline once the canvas has
            // been scaled down on a tall screen.
            input.caretWidth = 4;

            // Blinks per second. Fast enough to read as a caret rather than a mark on
            // the screen, slow enough that it is not strobing. Never zero: TMP divides
            // by this to get the period.
            input.caretBlinkRate = 1.6f;

            input.selectionColor = new Color(accent.r, accent.g, accent.b, 0.45f);

            // Clicking the amount means replacing the amount. It also paints a block
            // where the click landed, which is the clearest "you are typing here" the
            // field has.
            input.onFocusSelectAll = true;
        }

        /// <summary>
        /// Digits only, in and out. Hand this to <c>onValidateInput</c> so the box holds
        /// separators this code wrote and nothing a keyboard produced.
        /// </summary>
        internal static char DigitsOnly(string _, int __, char character) =>
            char.IsDigit(character) ? character : '\0';

        /// <summary>
        /// Rewrites what was typed as a grouped number, keeping the caret where the
        /// typist thinks it is, and returns the value.
        ///
        /// Returns zero for an empty or unparseable box, and leaves the box empty rather
        /// than writing a `0` into it: a half-typed amount is not a bet yet, and shoving
        /// a zero under the caret mid-keystroke is how a field fights its user.
        /// </summary>
        internal static long Reformat(TMP_InputField input, string typed)
        {
            var digits = Digits(typed);
            var value = long.TryParse(digits, out var parsed) && parsed > 0 ? parsed : 0L;

            if (input == null)
            {
                return value;
            }

            var formatted = digits.Length == 0 ? string.Empty : value.ToString("N0");

            if (formatted == typed)
            {
                return value;
            }

            var behind = DigitsWithin(typed, input.stringPosition);

            input.SetTextWithoutNotify(formatted);
            input.stringPosition = AfterDigits(formatted, behind);

            return value;
        }

        /// <summary>What to put in the box for an amount.</summary>
        internal static string Format(long value) => value.ToString("N0");

        private static string Digits(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return string.Empty;
            }

            var kept = new char[text.Length];
            var length = 0;

            foreach (var character in text)
            {
                if (char.IsDigit(character))
                {
                    kept[length++] = character;
                }
            }

            return new string(kept, 0, length);
        }

        /// <summary>How many digits lie in the first <paramref name="count"/> characters.</summary>
        internal static int DigitsWithin(string text, int count)
        {
            if (string.IsNullOrEmpty(text))
            {
                return 0;
            }

            var seen = 0;
            var limit = Math.Min(count, text.Length);

            for (var i = 0; i < limit; i++)
            {
                if (char.IsDigit(text[i]))
                {
                    seen++;
                }
            }

            return seen;
        }

        /// <summary>
        /// Where the caret goes to sit after <paramref name="count"/> digits: just past
        /// the last of them, separators and all.
        /// </summary>
        internal static int AfterDigits(string text, int count)
        {
            if (string.IsNullOrEmpty(text))
            {
                return 0;
            }

            if (count <= 0)
            {
                return 0;
            }

            var seen = 0;

            for (var i = 0; i < text.Length; i++)
            {
                if (char.IsDigit(text[i]))
                {
                    seen++;

                    if (seen == count)
                    {
                        return i + 1;
                    }
                }
            }

            return text.Length;
        }
    }
}
