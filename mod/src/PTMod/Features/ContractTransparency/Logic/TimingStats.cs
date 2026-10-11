using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace PTMod.Features.ContractTransparency
{
    /// <summary>
    /// The places of the feature whose time is measured (design.md, "Performance Considerations" and "Logging Strategy"; NFR-1, NFR-2; TSK-020) and the budget of each, milliseconds. The timers write to
    /// <c>ModLog</c> only with <c>ModSettings.VerboseLog</c> on; with it off a timer is a flag test.
    /// </summary>
    public static class TimingSites
    {
        /// <summary>Prefix of <c>Contract.AddOffer</c>: ~2 microseconds for an AI offer (the early exit), 5 ms for the analysis of a manual one.</summary>
        public const string OfferGatePrefix = "OfferGate.prefix";

        /// <summary>Postfix of <c>Contract.AddOffer</c>: the record of an offer that was added.</summary>
        public const string OfferGatePostfix = "OfferGate.postfix";

        /// <summary>Prefix of <c>Contract.ChooseCpu</c>: the copy of the offers (about twenty draws a year).</summary>
        public const string TenderPrefix = "TenderRecorder.prefix";

        /// <summary>Postfix of <c>Contract.ChooseCpu</c>: the chances of the draw, the result for the log.</summary>
        public const string TenderPostfix = "TenderRecorder.postfix";

        /// <summary>Prefix of <c>ContractAutomation.SendOffers</c>: the plan of the run (once a year, 50 CPUs x 20 contracts).</summary>
        public const string AutomationPrefix = "AutomationLogger.prefix";

        /// <summary>Finalizer of <c>ContractAutomation.SendOffers</c>: the comparison with what the game did and the lines of the log.</summary>
        public const string AutomationFinalizer = "AutomationLogger.finalizer";

        /// <summary>The preview of the automation on demand.</summary>
        public const string AutomationPreview = "AutomationLogger.preview";

        /// <summary>The daily handler of the letter: the whole cost of a day without tenders is one counter (the metric is 0.05 ms a day).</summary>
        public const string MailerDay = "TenderMailer.OnDayPassed";

        /// <summary>The breakdown of the chance of one CPU in one contract (a click, 14 offers).</summary>
        public const string Odds = "ContractInspector.BuildOdds";

        /// <summary>The section of the log written into the side file at a save.</summary>
        public const string LogWrite = "ContractLog.Write";

        /// <summary>The section of the log read from the side file at a load.</summary>
        public const string LogRead = "ContractLog.Read";

        /// <summary>The check of the game's members at the start.</summary>
        public const string SelfCheck = "ContractSelfCheck.Run";

        private static readonly KeyValuePair<string, double>[] Budgets =
        {
            new KeyValuePair<string, double>(OfferGatePrefix, 5.0),
            new KeyValuePair<string, double>(OfferGatePostfix, 1.0),
            new KeyValuePair<string, double>(TenderPrefix, 1.0),
            new KeyValuePair<string, double>(TenderPostfix, 1.0),
            new KeyValuePair<string, double>(AutomationPrefix, 20.0),
            new KeyValuePair<string, double>(AutomationFinalizer, 20.0),
            new KeyValuePair<string, double>(AutomationPreview, 20.0),
            new KeyValuePair<string, double>(MailerDay, 0.05),
            new KeyValuePair<string, double>(Odds, 5.0),
            new KeyValuePair<string, double>(LogWrite, 10.0),
            new KeyValuePair<string, double>(LogRead, 10.0),
            new KeyValuePair<string, double>(SelfCheck, 50.0),
        };

        /// <summary>Every measured place.</summary>
        public static IReadOnlyList<string> All
        {
            get
            {
                var names = new List<string>(Budgets.Length);
                foreach (KeyValuePair<string, double> row in Budgets) names.Add(row.Key);
                return names;
            }
        }

        /// <summary>The budget of a place, milliseconds; -1 for a name that is not a measured place.</summary>
        public static double BudgetMs(string site)
        {
            foreach (KeyValuePair<string, double> row in Budgets)
            {
                if (string.Equals(row.Key, site, StringComparison.Ordinal)) return row.Value;
            }
            return -1.0;
        }
    }

    /// <summary>What has been measured at one place so far.</summary>
    public sealed class TimingSample
    {
        internal TimingSample(string site, double budgetMs)
        {
            Site = site;
            BudgetMs = budgetMs;
        }

        public string Site { get; private set; }

        /// <summary>How many times the place has run since the statistics were started.</summary>
        public int Count { get; internal set; }

        /// <summary>The last measurement, milliseconds.</summary>
        public double LastMs { get; internal set; }

        /// <summary>The sum of all measurements, milliseconds.</summary>
        public double TotalMs { get; internal set; }

        /// <summary>The longest measurement, milliseconds.</summary>
        public double MaxMs { get; internal set; }

        /// <summary>The budget of the place, milliseconds; -1 when none is set.</summary>
        public double BudgetMs { get; private set; }

        public double AverageMs
        {
            get { return Count == 0 ? 0.0 : TotalMs / Count; }
        }

        /// <summary>The last measurement is over the budget.</summary>
        public bool OverBudget
        {
            get { return BudgetMs >= 0.0 && LastMs > BudgetMs; }
        }
    }

    /// <summary>
    /// The statistics behind the timers of <c>PerfTimer</c>: the count, the average and the longest time of every measured place, and the rule which measurements are worth a line in the log. A place that
    /// runs thousands of times a year (the offers of the AI) must not fill the log, so the first measurements are written, then every hundredth, and any that goes over the budget at once.
    /// </summary>
    public sealed class TimingStats
    {
        /// <summary>The first measurements of every place that are all written.</summary>
        public const int AlwaysLogFirst = 20;

        /// <summary>After those, every this-many-th measurement is written.</summary>
        public const int LogEvery = 100;

        private readonly Dictionary<string, TimingSample> _samples = new Dictionary<string, TimingSample>(StringComparer.Ordinal);

        /// <summary>The number of places measured.</summary>
        public int Count
        {
            get { return _samples.Count; }
        }

        /// <summary>Takes in one measurement; a negative or not-a-number time counts as 0. Returns the statistics of the place after it.</summary>
        public TimingSample Add(string site, double ms)
        {
            if (string.IsNullOrEmpty(site)) throw new ArgumentException("the place of a measurement has a name", "site");
            if (double.IsNaN(ms) || ms < 0.0) ms = 0.0;

            TimingSample sample;
            if (!_samples.TryGetValue(site, out sample))
            {
                sample = new TimingSample(site, TimingSites.BudgetMs(site));
                _samples[site] = sample;
            }

            sample.Count++;
            sample.LastMs = ms;
            sample.TotalMs += ms;
            if (ms > sample.MaxMs) sample.MaxMs = ms;
            return sample;
        }

        /// <summary>The statistics of a place, or null when it has not been measured.</summary>
        public TimingSample Get(string site)
        {
            TimingSample sample;
            return site != null && _samples.TryGetValue(site, out sample) ? sample : null;
        }

        /// <summary>Forgets everything.</summary>
        public void Clear()
        {
            _samples.Clear();
        }

        /// <summary>This measurement is written to the log: one of the first, every hundredth, or one over the budget.</summary>
        public static bool ShouldLog(TimingSample sample)
        {
            if (sample == null) return false;
            return sample.Count <= AlwaysLogFirst || sample.Count % LogEvery == 0 || sample.OverBudget;
        }

        /// <summary>Milliseconds from stopwatch ticks.</summary>
        public static double ToMilliseconds(long ticks, long frequency)
        {
            if (frequency <= 0 || ticks <= 0) return 0.0;
            return ticks * 1000.0 / frequency;
        }

        /// <summary>The line for the log: <c>time: OfferGate.prefix 0.021 ms (n=20, avg 0.004 ms, max 0.310 ms, budget 5 ms)</c>, with <c>OVER BUDGET</c> at the end when it is.</summary>
        public static string Format(TimingSample sample)
        {
            if (sample == null) return string.Empty;

            var sb = new StringBuilder("time: ");
            sb.Append(sample.Site).Append(' ').Append(Ms(sample.LastMs)).Append(" (n=").Append(sample.Count.ToString(CultureInfo.InvariantCulture));
            sb.Append(", avg ").Append(Ms(sample.AverageMs)).Append(", max ").Append(Ms(sample.MaxMs));
            if (sample.BudgetMs >= 0.0) sb.Append(", budget ").Append(sample.BudgetMs.ToString("0.###", CultureInfo.InvariantCulture)).Append(" ms");
            sb.Append(')');
            if (sample.OverBudget) sb.Append(" OVER BUDGET");
            return sb.ToString();
        }

        private static string Ms(double value)
        {
            return value.ToString("0.000", CultureInfo.InvariantCulture) + " ms";
        }
    }
}
