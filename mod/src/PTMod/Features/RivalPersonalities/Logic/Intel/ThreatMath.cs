using System;

namespace PTMod.Features.RivalPersonalities
{
    /// <summary>
    /// How the panel tells the player when a threat is expected (FR-9 item 3, AC-9.2: "expected in 2-7 weeks"). The exact day is not shown: the intelligence
    /// mail names the range of the warning (<see cref="ReactionPlannerCore.WeekRange"/>, 14..45 days is 2-7 weeks) and the panel keeps that range, shrinking
    /// it as the days pass since the mail came. The true remaining time always lies inside the range shown. Pure.
    /// </summary>
    public static class ThreatMath
    {
        /// <summary>
        /// The range of weeks left until a planned reaction is carried out, from the day the mail was delivered. Right after the delivery it is the range of
        /// the mail (<c>2..7</c>); later it is the range minus the days that passed, never below 0 weeks and never so narrow that the true remaining time falls
        /// outside it. <paramref name="to"/> is at least 1 and at least <paramref name="from"/>. On the day itself, or after it, the range is <c>0..1</c>.
        /// </summary>
        /// <param name="today">The game date.</param>
        /// <param name="fireOn">The day the reaction will be carried out (known once the mail was delivered).</param>
        /// <param name="telegraphDays">The warning that was rolled for this reaction, counted from the delivery of the mail.</param>
        /// <param name="lowDays">The shortest warning the mail promises (<c>telegraphDays[0]</c> of the config, 14).</param>
        /// <param name="highDays">The longest warning the mail promises (<c>telegraphDays[1]</c>, 45).</param>
        public static void WeeksLeft(DateTime today, DateTime fireOn, int telegraphDays, int lowDays, int highDays, out int from, out int to)
        {
            int low = lowDays < 0 ? 0 : lowDays;
            int high = highDays < low ? low : highDays;
            int remaining = Math.Max(0, RivalDates.DaysBetween(today, fireOn));
            if (remaining == 0)
            {
                // the day has come (the reaction is carried out at the next tick of the day): "within days"
                from = 0;
                to = 1;
                return;
            }

            int warning = telegraphDays < 1 ? 1 : telegraphDays;
            int elapsed = Math.Max(0, warning - remaining);   // the days since the mail came, as far as the dates tell

            int nearest = Math.Min(Math.Max(0, low - elapsed), remaining);
            int furthest = Math.Max(Math.Max(0, high - elapsed), remaining);

            from = nearest / 7;
            to = Math.Max(1, (furthest + 6) / 7);
            if (to < from) to = from;
        }

        /// <summary>The range as the panel writes it: <c>2–7</c>, or a single number when the ends are the same.</summary>
        public static string RangeText(int from, int to)
        {
            string low = from.ToString(System.Globalization.CultureInfo.InvariantCulture);
            string high = to.ToString(System.Globalization.CultureInfo.InvariantCulture);
            return from == to ? low : low + "–" + high;
        }
    }
}
