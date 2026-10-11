using System;

namespace PTMod.Features.BankruptcyRecovery.Logic
{
    /// <summary>
    /// The arithmetic of the amount sliders of the rescue menu. A slider of the core shows its plain number, so an amount is chosen as a share of a limit
    /// (the whole limit is 100) and the menu writes the sum itself beside it: the bridge loan (25 to 100 percent of <c>A_max</c>, FR-6), what is drawn from or paid back to the
    /// credit line (FR-7), the part of a loan paid back early (FR-10 item 3). The shares move in steps of <see cref="PercentStep"/>; the top of the slider is always the whole limit, to the cent.
    /// </summary>
    internal static class AmountSteps
    {
        /// <summary>The step of a share slider, in percent.</summary>
        public const int PercentStep = 5;

        /// <summary>The lowest share of the bridge loan the player may pick (design.md: 25 to 100 percent).</summary>
        public const int BridgeMinPercent = 25;

        /// <summary>The lowest share a line slider offers.</summary>
        public const int LineMinPercent = 5;

        /// <summary>The lowest share of a loan the player may pay back early.</summary>
        public const int PrepayMinPercent = 5;

        /// <summary>The whole limit.</summary>
        public const int MaxPercent = 100;

        /// <summary>
        /// The sum for a share of a limit: the whole limit at 100, otherwise the share in whole dollars, rounded down (never more than the limit). 0 for a limit that is
        /// not a positive number and for a share of 0 or less.
        /// </summary>
        public static double OfMax(double max, int percent)
        {
            if (double.IsNaN(max) || double.IsInfinity(max) || !(max > 0.0)) return 0.0;
            if (percent <= 0) return 0.0;
            if (percent >= MaxPercent) return max;

            double sum = Math.Floor(max * percent / 100.0);
            if (sum < 0.0) return 0.0;
            return sum > max ? max : sum;
        }

        /// <summary>
        /// The sum of an early repayment (FR-10 item 3) for a share of what a loan still owes: all of the debt at 100 (to the cent, so the loan can be closed), otherwise the share in
        /// whole dollars, rounded down. 0 for a debt that is not a positive number and for a share of 0 or less; a very small debt can give 0 at a low share.
        /// </summary>
        public static double PrepayAmount(double outstanding, int percent)
        {
            return OfMax(outstanding, percent);
        }

        /// <summary>A share held between <paramref name="min"/> and 100.</summary>
        public static int ClampPercent(int percent, int min)
        {
            int low = min < 0 ? 0 : (min > MaxPercent ? MaxPercent : min);
            if (percent < low) return low;
            return percent > MaxPercent ? MaxPercent : percent;
        }

        /// <summary>The most that can be paid back to the credit line now: what is drawn, but not more than the balance holds (a balance of 0 or less holds nothing).</summary>
        public static double RepayLimit(double drawn, double balance)
        {
            if (double.IsNaN(drawn) || double.IsInfinity(drawn) || !(drawn > 0.0)) return 0.0;
            if (double.IsNaN(balance) || double.IsInfinity(balance) || !(balance > 0.0)) return 0.0;
            return drawn < balance ? drawn : balance;
        }
    }
}
