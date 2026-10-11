using System;
using System.Collections.Generic;

namespace PTMod.Features.GpuProductLine
{
    /// <summary>
    /// The numbers of a console order (design.md, F35; FR-19, Could): which card an offer can be answered with, what the order pays and asks for, how much of it was
    /// not delivered and when a missed order is cancelled. Pure: the best performance of the segment, the buyers, the budget and the unit cost arrive as numbers, so the
    /// rules are tested without the market and the game.
    /// </summary>
    public static class GpuConsoleMath
    {
        /// <summary>The slack under the floor of a daily volume: <c>0.15f</c> times a round number of buyers must not fall a card short of its value because of the float noise.</summary>
        private const double FloorSlack = 1e-6;

        /// <summary>The same for the bar: a card at exactly 60 % of the best one is at the bar, though <c>0.6f</c> is a hair more than 0.6.</summary>
        private const double BarSlack = 1e-6;

        /// <summary>
        /// F35: a card can be offered to the console maker when its performance is at least <paramref name="minPerfShare"/> of the best card on sale in the target segment,
        /// <c>P_product &gt;= minPerfShare x max P</c>. A card without a performance (0, not a number) never qualifies; with no best card to compare with (nothing else sells there)
        /// any card with a performance does.
        /// </summary>
        public static bool Qualifies(double productPerf, double bestPerfInSegment, float minPerfShare)
        {
            if (double.IsNaN(productPerf) || double.IsInfinity(productPerf) || productPerf <= 0d) return false;
            if (double.IsNaN(bestPerfInSegment) || bestPerfInSegment <= 0d) return true;
            double share = float.IsNaN(minPerfShare) ? 0d : Math.Max(0d, minPerfShare);
            return productPerf >= share * bestPerfInSegment * (1d - BarSlack);
        }

        /// <summary>
        /// F35: the price of a card in the order, fixed when the offer is accepted, <c>max(priceFloorMarkup x U, priceFactor x Budget_s)</c>: never below the cost with the
        /// markup (<paramref name="priceFloorMarkup"/>, 1.15), usually a share of what a buyer of the segment pays (<paramref name="priceFactor"/>, 0.55 of the budget).
        /// </summary>
        public static float OrderPrice(float unitCost, float segmentBudget, float priceFactor, float priceFloorMarkup)
        {
            float cost = Usable(unitCost) * Usable(priceFloorMarkup);
            float share = Usable(segmentBudget) * Usable(priceFactor);
            float price = Math.Max(cost, share);
            return float.IsInfinity(price) ? 0f : price;
        }

        /// <summary>F35: the cards a day the maker asks for, <c>floor(unitsShare x N_s)</c> with <c>N_s</c> the buyers of the segment in a day when the offer is accepted.</summary>
        public static int DailyUnits(float segmentDailyBuyers, float unitsShare)
        {
            double units = Math.Floor((double)Usable(segmentDailyBuyers) * Usable(unitsShare) + FloorSlack);
            return units >= int.MaxValue ? int.MaxValue : (int)units;
        }

        /// <summary>F35: what was delivered of a day's order (<c>min(stock, units)</c>).</summary>
        public static int Delivered(int stock, int units)
        {
            return Math.Max(0, Math.Min(stock, units));
        }

        /// <summary>
        /// F35: the share of the order that was not delivered in the window, <c>Sum(missed) / (Sum(delivered) + Sum(missed))</c>; 0 for an empty window and for a window of days
        /// the order asked nothing in.
        /// </summary>
        public static float MissedShare(IReadOnlyList<int> delivered, IReadOnlyList<int> missed)
        {
            long gone = Sum(missed);
            long total = Sum(delivered) + gone;
            return total <= 0L ? 0f : (float)((double)gone / total);
        }

        /// <summary>
        /// F35: the order is cancelled when the missed share is over <paramref name="failCancelShare"/> (25 %, strictly) and at least <paramref name="graceDays"/> days have
        /// passed since the start - a window that is not full yet decides nothing. <paramref name="daysActive"/> counts the day of the start as the first.
        /// </summary>
        public static bool ShouldCancel(float missedShare, float failCancelShare, int daysActive, int graceDays)
        {
            if (daysActive < Math.Max(1, graceDays)) return false;
            return !float.IsNaN(missedShare) && missedShare > failCancelShare;
        }

        /// <summary>Puts a day at the end of the window of the last <paramref name="graceDays"/> days (the ring that is kept in the save).</summary>
        public static void Push(List<int> window, int value, int graceDays)
        {
            if (window == null) return;
            window.Add(Math.Max(0, value));
            int keep = Math.Max(1, graceDays);
            if (window.Count > keep) window.RemoveRange(0, window.Count - keep);
        }

        /// <summary>The days since the start of an order, the day of the start being the first.</summary>
        public static int DaysActive(DateTime started, DateTime today)
        {
            double days = (today.Date - started.Date).TotalDays;
            return days < 0d ? 0 : (int)days + 1;
        }

        private static long Sum(IReadOnlyList<int> values)
        {
            long sum = 0L;
            if (values == null) return sum;
            for (int i = 0; i < values.Count; i++) sum += Math.Max(0, values[i]);
            return sum;
        }

        // A usable factor: 0 for a number that is not one or is negative.
        private static float Usable(float value)
        {
            return float.IsNaN(value) || float.IsInfinity(value) || value < 0f ? 0f : value;
        }
    }
}
