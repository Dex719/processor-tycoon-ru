using System;
using System.Globalization;

namespace PTMod.Core.Logic.Ui
{
    /// <summary>
    /// The arithmetic of <c>ModUI.Slider</c> (FR-16 item 5), without Unity: where a stepped value lands and how it is shown. The slider of
    /// the game runs from min to max in raw mode; the steps are snapped here, so the range need not be a multiple of the step.
    /// </summary>
    public static class SliderMath
    {
        /// <summary>Puts the arguments right: min below max (swapped if reversed, a hair apart if equal), a step that is NaN, negative or infinite becomes 0 (continuous).</summary>
        public static void Normalize(ref float min, ref float max, ref float step)
        {
            if (float.IsNaN(min) || float.IsInfinity(min)) min = 0f;
            if (float.IsNaN(max) || float.IsInfinity(max)) max = min + 1f;
            if (max < min)
            {
                float swap = min;
                min = max;
                max = swap;
            }
            if (max - min < 1e-6f) max = min + 1f;
            if (float.IsNaN(step) || float.IsInfinity(step) || step < 0f) step = 0f;
        }

        /// <summary>
        /// The value clamped to [min, max] and, for a positive step, moved to the nearest <c>min + k * step</c> (halves round up); a point past <paramref name="max"/>
        /// gives the highest one below it. NaN gives <paramref name="min"/>. The result carries no float noise beyond six decimals.
        /// </summary>
        public static float Snap(float value, float min, float max, float step)
        {
            if (float.IsNaN(value)) value = min;
            if (value < min) value = min;
            if (value > max) value = max;
            if (!(step > 0f)) return value;

            // A float step such as 0.1f is a hair above 0.1, so 1 / 0.1f is a hair below 10: the grid count needs a relative tolerance or the top point is lost.
            double ratio = ((double)max - min) / step;
            double points = Math.Floor(ratio + 1e-6 * Math.Max(1.0, ratio));
            double k = Math.Round(((double)value - min) / step, MidpointRounding.AwayFromZero);
            if (k > points) k = points;
            if (k < 0) k = 0;

            double snapped = Math.Round(min + k * step, 6);
            if (snapped > max) snapped = max;
            if (snapped < min) snapped = min;
            return (float)snapped;
        }

        /// <summary>How many decimals show a step exactly, at most four: 1 -> 0, 0.5 -> 1, 0.05 -> 2; a step of zero or less -> 2.</summary>
        public static int Decimals(float step)
        {
            if (!(step > 0f)) return 2;

            double scale = 1.0;
            for (int digits = 0; digits <= 4; digits++)
            {
                double scaled = step * scale;
                if (Math.Abs(scaled - Math.Round(scaled)) <= 1e-4 * scaled) return digits;
                scale *= 10.0;
            }
            return 4;
        }

        /// <summary>The value as text with the decimals of the step, the invariant culture (a continuous slider shows up to two decimals).</summary>
        public static string Format(float value, float step)
        {
            if (!(step > 0f)) return value.ToString("0.##", CultureInfo.InvariantCulture);
            return value.ToString("F" + Decimals(step).ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
        }
    }
}
