using System;

namespace PTMod.Features.RivalPersonalities
{
    /// <summary>
    /// The price of keeping the engineers when a rival poaches them (design.md, formula 9; FR-7 item 6): the counter-offer of the player costs
    /// <c>C = max(minCost × Inflation, researchMonths × ExpenseByResearch of the last full month)</c>, with 250 000 and 2 by default. When the
    /// player's company has fewer than two monthly reports (a new game) there is no last full month and the cost is the minimum. Pure: the game
    /// reads the inflation and the reports.
    /// </summary>
    public static class PoachMath
    {
        /// <summary>The report the cost looks at is the last full month: <c>Reports[Count − 2]</c> (the last one is the month in progress). Fewer reports than this: the minimum.</summary>
        public const int MinReports = 2;

        /// <summary>The cost of the counter-offer. A damaged number (NaN, a negative) counts as 0 for the second term and 1 for the inflation.</summary>
        /// <param name="inflation"><c>InflationHandler.Instance.Inflation</c>.</param>
        /// <param name="reportCount"><c>Reports.Count</c> of the player's finance.</param>
        /// <param name="expenseByResearchLastMonth"><c>Reports[Count − 2].ExpenseByResearch</c>; unused when <paramref name="reportCount"/> is below <see cref="MinReports"/>.</param>
        /// <param name="minCost"><c>reactions.Poach.minCost</c>, 250 000.</param>
        /// <param name="researchMonths"><c>reactions.Poach.researchMonths</c>, 2.</param>
        public static float CounterOfferCost(float inflation, int reportCount, float expenseByResearchLastMonth, int minCost, int researchMonths)
        {
            float rate = float.IsNaN(inflation) || float.IsInfinity(inflation) || inflation <= 0f ? 1f : inflation;
            double minimum = (double)Math.Max(0, minCost) * rate;
            if (reportCount < MinReports) return Clamp(minimum);

            double spent = float.IsNaN(expenseByResearchLastMonth) || float.IsInfinity(expenseByResearchLastMonth) || expenseByResearchLastMonth < 0f
                ? 0.0 : expenseByResearchLastMonth;
            double scaled = (double)Math.Max(0, researchMonths) * spent;
            return Clamp(Math.Max(minimum, scaled));
        }

        /// <summary>The cost with the numbers of the config.</summary>
        public static float CounterOfferCost(float inflation, int reportCount, float expenseByResearchLastMonth, RivalConfig config)
        {
            RivalConfig.PoachReaction poach = (config ?? RivalConfig.Defaults()).reactions.Poach;
            return CounterOfferCost(inflation, reportCount, expenseByResearchLastMonth, poach.minCost, poach.researchMonths);
        }

        /// <summary>The index of the report of the last full month in a list of <paramref name="reportCount"/> reports, or -1 when there is none yet.</summary>
        public static int LastFullMonthIndex(int reportCount)
        {
            return reportCount >= MinReports ? reportCount - 2 : -1;
        }

        private static float Clamp(double value)
        {
            if (double.IsNaN(value) || value < 0.0) return 0f;
            return value > float.MaxValue ? float.MaxValue : (float)value;
        }
    }
}
