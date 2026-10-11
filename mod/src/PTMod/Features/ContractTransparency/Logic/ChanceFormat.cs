using System;
using System.Globalization;

namespace PTMod.Features.ContractTransparency
{
    /// <summary>
    /// How the feature prints a chance: the number only, without the percent sign (the string tables put the sign and the space: <c>"{0} %"</c>), with the
    /// decimals of <c>display.percentDecimals</c> / <c>display.actualPercentDecimals</c> and the decimal separator of the language. The game's own formatters
    /// are for money and IPS; a share of a draw needs nothing more than this.
    /// </summary>
    public static class ChanceFormat
    {
        /// <summary>
        /// <paramref name="fraction"/> (0..1) as a percentage: <c>0.211</c> with 0 decimals is <c>"21"</c>, with 1 decimal <c>"21.1"</c> (<c>"21,1"</c> when
        /// <paramref name="decimalComma"/>). The midpoint rounds away from zero. A value that is not a number, or below 0, is <c>"0"</c>; above 1 it is cut to 100.
        /// Decimals are held between 0 and 3.
        /// </summary>
        public static string Percent(double fraction, int decimals, bool decimalComma)
        {
            if (decimals < 0) decimals = 0;
            if (decimals > 3) decimals = 3;
            if (double.IsNaN(fraction) || fraction < 0) fraction = 0;
            if (fraction > 1) fraction = 1;

            double value = Math.Round(fraction * 100.0, decimals, MidpointRounding.AwayFromZero);
            string text = value.ToString("F" + decimals.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
            return decimalComma ? text.Replace('.', ',') : text;
        }

        /// <summary>The chance the game gives (a whole number of percent, 1..100) as text; a number outside 0..100 is cut to it.</summary>
        public static string Whole(int percent)
        {
            if (percent < 0) percent = 0;
            if (percent > 100) percent = 100;
            return percent.ToString(CultureInfo.InvariantCulture);
        }
    }
}
