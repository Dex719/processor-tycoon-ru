using System;
using PTMod.Core;

namespace PTMod.Features.DesignFlawRecalls
{
    /// <summary>
    /// The discovery of a hidden flaw (FR-4, design.md Component 5, formula 3): the monthly chance, the forced discovery, the expiry, the source and the
    /// leak rate of a cover-up. Pure; the random draws go through the <see cref="IModRng"/> of the caller.
    /// </summary>
    public static class DiscoveryModel
    {
        /// <summary>
        /// The chance that a hidden flaw is found in one month: <c>min(hMax, hBase[sev] * (1 + kUnits * log10(1 + U / unitsRef)))</c>, where U is the
        /// number of units of the affected CPUs sold so far. Minor at 100 000 units gives 0.026, Major at 1 000 000 gives 0.082, Critical at 10 000 000 gives 0.210.
        /// </summary>
        public static float MonthlyHazard(FlawSeverity sev, long unitsSold, FlawBalance b)
        {
            FlawBalance.DiscoveryBalance d = b.Discovery;
            double units = unitsSold < 0 ? 0.0 : unitsSold;
            double reference = d.UnitsRef > 0f ? d.UnitsRef : 1.0;

            double h = FlawMath.At(d.HBase, FlawMath.SeverityIndex(sev), 0f) * (1.0 + d.KUnits * Math.Log10(1.0 + units / reference));
            return (float)FlawMath.Clamp(h, 0.0, Math.Max(0.0, d.HMax));
        }

        /// <summary>
        /// The flaw cannot stay hidden any longer: at least <c>maxHiddenMonths[sev]</c> months have passed since the release and something was sold
        /// (FR-4 item 3). A CPU that sold nothing is never found by this rule (it expires instead).
        /// </summary>
        /// <param name="unitsSold">Units of the affected CPUs sold so far.</param>
        public static bool IsForced(FlawSeverity sev, int monthsSinceRelease, long unitsSold, FlawBalance b)
        {
            int limit = FlawMath.At(b.Discovery.MaxHiddenMonths, FlawMath.SeverityIndex(sev), int.MaxValue);
            return monthsSinceRelease >= limit && unitsSold > 0;
        }

        /// <summary>
        /// The hidden flaw is no longer worth checking (FR-4 item 4): the CPU was taken off the market more than <c>expireAfterRetiredMonths</c>
        /// months ago, or it sold nothing in the last <c>expireNoSalesMonths</c> months (and has been on the market for that long).
        /// </summary>
        /// <param name="retired">The CPU is off the market (<c>IsRetired</c>).</param>
        /// <param name="monthsSinceRetired">Months since it went off the market (only used when <paramref name="retired"/>).</param>
        /// <param name="monthsSinceRelease">Whole months since the release.</param>
        /// <param name="unitsInNoSalesWindow">Units sold in the last <c>expireNoSalesMonths</c> months (the window of the rule, not the total).</param>
        public static bool IsExpired(bool retired, int monthsSinceRetired, int monthsSinceRelease, long unitsInNoSalesWindow, FlawBalance b)
        {
            FlawBalance.DiscoveryBalance d = b.Discovery;

            if (retired && monthsSinceRetired > d.ExpireAfterRetiredMonths) return true;
            return monthsSinceRelease >= d.ExpireNoSalesMonths && unitsInNoSalesWindow <= 0;
        }

        /// <summary>
        /// Who finds the flaw (FR-4 item 2): <see cref="DiscoverySource.Internal"/> with the probability <c>qInternal[sev]</c> (the company's own
        /// engineers), otherwise <see cref="DiscoverySource.External"/> (the press and the buyers). Draws one number.
        /// </summary>
        public static DiscoverySource PickSource(FlawSeverity sev, IModRng rng, FlawBalance b)
        {
            float q = FlawMath.At(b.Discovery.QInternal, FlawMath.SeverityIndex(sev), 0f);
            return rng.Chance(q) ? DiscoverySource.Internal : DiscoverySource.External;
        }

        /// <summary>
        /// The monthly chance that a cover-up leaks (FR-4 item 6, FR-7 item 4, FR-9 item 4): an ignored flaw <c>leakRate.ignore</c>; a quiet revision
        /// <c>leakRate.quietStepping</c> while it is being made and <c>leakRate.tail</c> after it was finished; any other response 0.
        /// </summary>
        /// <param name="response">What the owner did about the flaw.</param>
        /// <param name="steppingDone">Only for <see cref="FlawResponse.Stepping"/>: the revision is finished (the tail).</param>
        public static float LeakRate(FlawResponse response, bool steppingDone, FlawBalance b)
        {
            FlawBalance.LeakRateBalance rate = b.Discovery.LeakRate;
            switch (response)
            {
                case FlawResponse.Ignore:
                    return rate.Ignore;
                case FlawResponse.Stepping:
                    return steppingDone ? rate.Tail : rate.QuietStepping;
                default:
                    return 0f;
            }
        }
    }
}
