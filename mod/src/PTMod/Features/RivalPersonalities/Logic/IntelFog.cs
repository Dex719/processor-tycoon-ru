using System;
using System.Globalization;

namespace PTMod.Features.RivalPersonalities
{
    /// <summary>
    /// The fog of the intelligence panel (FR-16, design.md formula 15; display only): with the parameter <c>intelFog</c> on, the public numbers of a
    /// rival - its share of the market and the <c>Ips</c> of its last CPU - are shown rounded and marked with "≈": the share to 5 percentage points
    /// (17.3 % is "≈ 15 %"), the <c>Ips</c> to two significant digits (123 is "≈ 120"). With the fog off the numbers are exact (17 %, 123). The fog
    /// touches nothing else: the attitude, the threats, the effects in force and the history are exact in both modes, and the game's values and the logic of
    /// the reactions never see the rounded numbers. Pure and total: a number that is not finite gives 0.
    /// </summary>
    public static class IntelFog
    {
        /// <summary>The step of the rounded share, in percentage points (<c>intelFog.shareStepPp</c> of <c>config.json</c>).</summary>
        public const float DefaultShareStepPp = 5f;

        /// <summary>The significant digits of the rounded <c>Ips</c> (<c>intelFog.ipsSignificantDigits</c> of <c>config.json</c>).</summary>
        public const int DefaultSignificantDigits = 2;

        /// <summary>The mark in front of a rounded number.</summary>
        public const string Approx = "≈ ";

        /// <summary>
        /// A share (0..1) rounded to the step in percentage points: <c>RoundShare(0.173, 5)</c> is 0.15. Halves go up. A step of 0 or less leaves the
        /// share as it is; the result stays in 0..1.
        /// </summary>
        public static float RoundShare(float share, float stepPp)
        {
            if (float.IsNaN(share) || float.IsInfinity(share)) return 0f;
            double points = share * 100.0;
            if (!(stepPp > 0f) || float.IsInfinity(stepPp))
            {
                return (float)Clamp01(points / 100.0);
            }

            double rounded = Math.Round(points / stepPp, MidpointRounding.AwayFromZero) * stepPp;
            return (float)Clamp01(rounded / 100.0);
        }

        /// <summary>
        /// A number rounded to <paramref name="digits"/> significant digits: <c>RoundSignificant(123, 2)</c> is 120, <c>RoundSignificant(1949, 2)</c> is
        /// 1900. Zero and a number that is not finite give 0; fewer than one digit counts as one.
        /// </summary>
        public static float RoundSignificant(float value, int digits)
        {
            if (float.IsNaN(value) || float.IsInfinity(value) || value == 0f) return 0f;

            int keep = digits < 1 ? 1 : (digits > 9 ? 9 : digits);
            double magnitude = Math.Abs((double)value);
            int exponent = (int)Math.Floor(Math.Log10(magnitude));
            double scale = Math.Pow(10.0, exponent - keep + 1);
            double rounded = Math.Round(magnitude / scale, MidpointRounding.AwayFromZero) * scale;
            return (float)(value < 0f ? -rounded : rounded);
        }

        /// <summary>The share as the panel writes it: <c>≈ 15 %</c> with the fog, <c>17 %</c> without (whole percents).</summary>
        public static string FormatShare(float share, bool fog)
        {
            return FormatShare(share, fog, DefaultShareStepPp, null);
        }

        /// <summary>
        /// The share as the panel writes it, with the step of the config and a way to mark the rounded number in the language of the player
        /// (<paramref name="approx"/> gets "15 %" and gives "≈ 15 %"; null marks it with <see cref="Approx"/>).
        /// </summary>
        public static string FormatShare(float share, bool fog, float stepPp, Func<string, string> approx)
        {
            if (float.IsNaN(share) || float.IsInfinity(share)) share = 0f;

            float shown = fog ? RoundShare(share, stepPp) : (float)Clamp01(share);
            string text = Percent(shown) + " %";
            return fog ? Mark(text, approx) : text;
        }

        /// <summary>The <c>Ips</c> as the panel writes it: <c>≈ 120</c> with the fog, <c>123</c> without. Digits are grouped by a space.</summary>
        public static string FormatIps(float ips, bool fog)
        {
            return FormatIps(ips, fog, DefaultSignificantDigits, null, null);
        }

        /// <summary>
        /// The <c>Ips</c> as the panel writes it, with the digits of the config, the formatter of the game for the number (null: whole number, groups of
        /// three digits) and a way to mark the rounded number (see <see cref="FormatShare(float, bool, float, Func{string, string})"/>).
        /// </summary>
        public static string FormatIps(float ips, bool fog, int significantDigits, Func<float, string> format, Func<string, string> approx)
        {
            if (float.IsNaN(ips) || float.IsInfinity(ips)) ips = 0f;

            float shown = fog ? RoundSignificant(ips, significantDigits) : ips;
            string text = format != null ? format(shown) : Whole(shown);
            return fog ? Mark(text, approx) : text;
        }

        // ---- internals ----

        private static string Mark(string text, Func<string, string> approx)
        {
            return approx != null ? approx(text) : Approx + text;
        }

        private static string Percent(float share)
        {
            double percent = Math.Round(share * 100.0, MidpointRounding.AwayFromZero);
            return percent.ToString("0", CultureInfo.InvariantCulture);
        }

        private static string Whole(float value)
        {
            var groups = new NumberFormatInfo { NumberGroupSeparator = " ", NumberGroupSizes = new[] { 3 }, NumberDecimalDigits = 0 };
            double rounded = Math.Round((double)value, MidpointRounding.AwayFromZero);
            return rounded.ToString("#,0", groups);
        }

        private static double Clamp01(double value)
        {
            if (value < 0.0) return 0.0;
            return value > 1.0 ? 1.0 : value;
        }
    }
}
