using System;
using System.Diagnostics;
using PTMod.Core;

namespace PTMod.Features.ContractTransparency
{
    /// <summary>
    /// The timer of the feature (design.md, "Logging Strategy": "Замеры ... ModLog.Debug, только при ModSettings.VerboseLog"; NFR-1, NFR-2; TSK-020): <c>using (PerfTimer.Start(TimingSites.MailerDay)) { ... }</c>.
    /// With the verbose log off, <see cref="Start"/> costs one flag test and the struct does nothing on <see cref="Dispose"/>. With it on, the time of the block goes to <see cref="TimingStats"/> and, by its
    /// rule (the first twenty, every hundredth, any over the budget), to <c>ModLog.Debug</c> as <c>time: OfferGate.prefix 0.021 ms (n=20, avg 0.004 ms, max 0.310 ms, budget 5 ms)</c>.
    /// A timer never throws: a failure of the measuring is swallowed, the block it wraps is not affected. Main thread only.
    /// </summary>
    internal struct PerfTimer : IDisposable
    {
        private static readonly TimingStats Stats = new TimingStats();

        private readonly string _site;
        private readonly long _start;

        private PerfTimer(string site, long start)
        {
            _site = site;
            _start = start;
        }

        /// <summary>Starts the timer of a place (<see cref="TimingSites"/>); a timer that is not running when the verbose log is off.</summary>
        internal static PerfTimer Start(string site)
        {
            return ModSettings.VerboseLog ? new PerfTimer(site, Stopwatch.GetTimestamp()) : default(PerfTimer);
        }

        public void Dispose()
        {
            if (_site == null) return;

            try
            {
                double ms = TimingStats.ToMilliseconds(Stopwatch.GetTimestamp() - _start, Stopwatch.Frequency);
                TimingSample sample = Stats.Add(_site, ms);
                if (TimingStats.ShouldLog(sample)) ModLog.Debug(ContractParamSpecs.FeatureId, TimingStats.Format(sample));
            }
            catch (Exception)
            {
                // The measuring must never disturb what it measures.
            }
        }
    }
}
