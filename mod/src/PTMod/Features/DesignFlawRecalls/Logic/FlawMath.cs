using System;

namespace PTMod.Features.DesignFlawRecalls
{
    /// <summary>
    /// The small numeric helpers of the pure logic of the feature. <c>Math.Clamp</c>, <c>MathF</c> and <c>float.IsFinite</c> are not in the
    /// common subset of .NET Framework 4.8 and the Unity runtime (decision 0003), so the formulas use these.
    /// </summary>
    internal static class FlawMath
    {
        /// <summary>A number that is neither NaN nor an infinity.</summary>
        public static bool IsFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }

        /// <summary>The value, or 0 for NaN and the infinities (a broken input counts as "nothing").</summary>
        public static double Finite(double value)
        {
            return IsFinite(value) ? value : 0.0;
        }

        /// <summary>
        /// A coefficient of the balance as the decimal number it was written as: <c>0.15f</c> is 0.15, not 0.1500000059604645. The money formulas multiply
        /// these by millions, and the amount under the button must come out as a whole number the design example promises (66 000 000, not 66 000 001).
        /// </summary>
        public static double Exact(float value)
        {
            if (!IsFinite(value) || value > 1e20f || value < -1e20f) return value;
            return (double)(decimal)value;
        }

        /// <summary><paramref name="value"/> limited to [lo, hi]; NaN gives <paramref name="lo"/>.</summary>
        public static double Clamp(double value, double lo, double hi)
        {
            if (double.IsNaN(value)) return lo;
            if (value < lo) return lo;
            if (value > hi) return hi;
            return value;
        }

        /// <summary>The element of an array "by severity" (or by any index), or <paramref name="fallback"/> when the array is missing or too short.</summary>
        public static float At(float[] values, int index, float fallback)
        {
            if (values == null || index < 0 || index >= values.Length) return fallback;
            return values[index];
        }

        /// <summary>The element of an array of ints, or <paramref name="fallback"/> when the array is missing or too short.</summary>
        public static int At(int[] values, int index, int fallback)
        {
            if (values == null || index < 0 || index >= values.Length) return fallback;
            return values[index];
        }

        /// <summary>The index of a severity in the arrays "by severity": 0, 1 or 2 (a value outside the enum counts as the nearest end).</summary>
        public static int SeverityIndex(FlawSeverity severity)
        {
            int index = (int)severity;
            return index < 0 ? 0 : (index > FlawBalance.SeverityCount - 1 ? FlawBalance.SeverityCount - 1 : index);
        }

        /// <summary>Rounds money to whole units of currency, half away from zero (the amount the player sees is the amount that is taken).</summary>
        public static double RoundMoney(double value)
        {
            return Math.Round(value, MidpointRounding.AwayFromZero);
        }
    }
}
