using System;
using System.Globalization;

namespace PTMod.Features.PressReviews
{
    /// <summary>
    /// Looks a string up by its key and fills in its arguments: <c>ModL10n.L</c> in the game, the string tables of the feature in the tests. The pure
    /// builders of the mail and of the windows take it, so what the player reads is checked without the game.
    /// </summary>
    public delegate string PressText(string key, params object[] args);

    /// <summary>
    /// The shared words of the windows and letters of the feature: the keys of the segments and of the axes, the formatting of a few numbers.
    /// Pure; the strings themselves are in <c>L10n/&lt;language&gt;/press-reviews.json</c>.
    /// </summary>
    public static class PressWords
    {
        private static readonly string[] SegmentKeys =
        {
            "press.segment.highend", "press.segment.midrange", "press.segment.lowend", "press.segment.industries",
        };

        private static readonly string[] AxisKeys =
        {
            "press.axis.performance", "press.axis.value", "press.axis.efficiency", "press.axis.thermal",
        };

        /// <summary>The key of the name of a segment (<c>(int)Segment</c>, 0…3); the key of Mid Range for a number outside the range.</summary>
        public static string SegmentKey(int segment)
        {
            if (segment < 0 || segment >= SegmentKeys.Length) return SegmentKeys[1];
            return SegmentKeys[segment];
        }

        /// <summary>The key of the name of an axis (<see cref="Axis"/>); null for a number outside the range.</summary>
        public static string AxisKey(int axis)
        {
            if (axis < 0 || axis >= AxisKeys.Length) return null;
            return AxisKeys[axis];
        }

        /// <summary>The effect of the reputation as the player reads it: <c>×1.12</c>, two decimals, a point in every language.</summary>
        public static string Multiplier(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value)) value = 1f;
            return "×" + value.ToString("0.00", CultureInfo.InvariantCulture);
        }

        /// <summary>A score or a reputation as a whole number, halves away from zero (<c>67.5</c> is 68).</summary>
        public static int Whole(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value)) return 0;
            return (int)Math.Round(value, MidpointRounding.AwayFromZero);
        }

        /// <summary>A name that may carry the characters of a rich-text tag, made safe for a text of the game (<c>&lt;</c> and <c>&gt;</c> are dropped).</summary>
        public static string Plain(string text)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;
            if (text.IndexOf('<') < 0 && text.IndexOf('>') < 0) return text;
            return text.Replace("<", string.Empty).Replace(">", string.Empty);
        }

        /// <summary>The text of a key that may be missing: the key's own text, or <paramref name="fallback"/> for a null or empty key.</summary>
        public static string TextOrEmpty(PressText text, string key, string fallback = "")
        {
            if (text == null || string.IsNullOrEmpty(key)) return fallback;
            return text(key) ?? fallback;
        }
    }
}
