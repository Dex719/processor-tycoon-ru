using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using PTMod.Core;
using PTMod.Features.CpuArchitectures;
using PTMod.Tests.Architecture;

namespace PTMod.Tests.Features.CpuArchitectures
{
    /// <summary>
    /// The measurements of the feature (TSK-030, NFR-1) where they cannot be run without the game: every hook that has a budget is timed through <c>ArchPerf</c> with the probe the
    /// budget belongs to (the daily tick and the arbiter, the monthly handler and its steps, the provider of the rating, the provider of the cost, the postfixes of the designer), the
    /// debug flag is the only thing that turns the timing on and the release build never reads it, and nothing throws out of a measurement. Also a smoke test of the pure half of the
    /// hooks against the same budgets: the work that is not the game's own must leave the budget almost whole for the game's part. The wall clock is noisy, so the test takes the best
    /// of many runs and checks against the budget itself, which the pure half uses up a tenth of or less.
    /// </summary>
    public class PerfWiringTests
    {
        private static string FeatureDir()
        {
            return Path.Combine(SourceScan.SrcDirOrFail(), "Features", "CpuArchitectures");
        }

        private static string Code(string relative)
        {
            string path = Path.Combine(FeatureDir(), relative);
            Assert.True(File.Exists(path), "missing " + path);
            return SourceScan.Strip(SourceScan.ReadText(path), false);
        }

        private static string BodyOf(string code, string signature)
        {
            int at = code.IndexOf(signature, StringComparison.Ordinal);
            Assert.True(at >= 0, "no '" + signature + "' in the source");

            string blanked = SourceScan.Strip(code, true);
            int open = blanked.IndexOf('{', at);
            Assert.True(open > 0, "no body after '" + signature + "'");
            int depth = 0;
            for (int i = open; i < blanked.Length; i++)
            {
                if (blanked[i] == '{') depth++;
                else if (blanked[i] == '}' && --depth == 0) return code.Substring(open, i - open + 1);
            }
            Assert.Fail("the body of '" + signature + "' is not closed");
            return null;
        }

        private static void AssertTimed(string body, string probe, string where)
        {
            Assert.True(body.Contains("ArchPerf.Begin()"), where + " starts the clock");
            Assert.True(Regex.IsMatch(body, @"ArchPerf\.End\(ArchPerfMeter\." + probe + @",\s*\w+\)"), where + " ends the measurement of " + probe);
        }

        // ---- the hooks ----

        [Test]
        public void TheDailyTick_AndTheArbiter_AreTimedAsOneDay()
        {
            AssertTimed(BodyOf(Code("Services/ArchResearchService.cs"), "public void OnDailyTick()"), "Day", "the daily tick of the research");
            AssertTimed(BodyOf(Code("Services/LabArbiter.cs"), "public void OnEarlyTick()"), "Day", "the arbiter of the early tick");
            Assert.True(BodyOf(Code("Services/ArchResearchService.cs"), "public void OnDailyTick()").Contains("finally"), "the measurement ends even when the tick throws");
        }

        [Test]
        public void TheMonthlyHandler_IsTimedAsAWhole_AndStepByStep()
        {
            string feature = Code("ArchitecturesFeature.cs");
            string month = BodyOf(feature, "private void OnMonthPassed()");
            AssertTimed(month, "Month", "the monthly handler");
            foreach (string step in new[] { "MonthEcosystem", "MonthSupport", "MonthRoyalty", "MonthAi" })
            {
                Assert.True(Regex.IsMatch(month, @"ArchPerf\.End\(ArchPerfMeter\." + step + @",\s*step\)"), "the step " + step + " is timed");
            }

            // The whole month brackets the guarded body, and each step brackets its own call, in the order of the handler.
            int begin = month.IndexOf("ArchPerf.Begin()", StringComparison.Ordinal);
            int guard = month.IndexOf("ModHooks.Guard(FeatureId, MonthSite", StringComparison.Ordinal);
            int end = month.LastIndexOf("ArchPerf.End(ArchPerfMeter.Month,", StringComparison.Ordinal);
            Assert.True(begin >= 0 && begin < guard && guard < end, "the clock starts before the guarded body and stops after it");
        }

        [Test]
        public void TheProviders_AreTimed_TheRatingPerDay_TheCostPerCall()
        {
            AssertTimed(BodyOf(Code("Services/ArchRatingProvider.cs"), "private ProviderResult Provide(ModifierContext ctx)"), "Rating", "the provider of the rating");
            AssertTimed(BodyOf(Code("Designer/DesignCostProvider.cs"), "public ProviderResult Provide(ModifierContext ctx)"), "Cost", "the provider of the cost");
            Assert.True(BodyOf(Code("Services/ArchRatingProvider.cs"), "private ProviderResult Provide(ModifierContext ctx)").Contains("finally"), "the measurement ends on an exception too");
            Assert.True(BodyOf(Code("Designer/DesignCostProvider.cs"), "public ProviderResult Provide(ModifierContext ctx)").Contains("finally"));
        }

        [Test]
        public void ThePostfixesOfTheDesigner_AreTimedAsTheFrameTheyBelongTo()
        {
            string designer = Code("Patches/DesignerPatches.cs");
            foreach (string handler in new[] { "private static void PostfixInitialize(", "private static void PostfixUpdateUI(", "private static void PostfixConsumptionAndTemperature(", "private static void PostfixPerformance(" })
            {
                AssertTimed(BodyOf(designer, handler), "Designer", handler.Replace("private static void ", string.Empty).TrimEnd('('));
            }
            AssertTimed(BodyOf(Code("Patches/ExtensionPatches.cs"), "private static void PostfixUpdateNumbers("), "Designer", "PostfixUpdateNumbers");

            // The clock is started after the check that the feature is neutral: a feature that is off pays nothing for the timing.
            foreach (string handler in new[] { "private static void PostfixConsumptionAndTemperature(", "private static void PostfixPerformance(" })
            {
                string body = BodyOf(designer, handler);
                Assert.True(body.IndexOf("ArchitecturesFeature.NeutralNow", StringComparison.Ordinal) < body.IndexOf("ArchPerf.Begin()", StringComparison.Ordinal), handler);
            }
        }

        [Test]
        public void TheTotalsOfTheSession_AreWrittenWhenTheSceneEndsAndWhenTheFeatureIsSwitchedOff()
        {
            string feature = Code("ArchitecturesFeature.cs");
            Assert.True(BodyOf(feature, "private void OnSceneLeaving()").Contains("ArchPerf.FlushSession()"));
            Assert.True(BodyOf(feature, "public void OnDisabled()").Contains("ArchPerf.FlushSession()"));
        }

        // ---- the switch ----

        [Test]
        public void TheTiming_IsOnlyOnForTheDebugFlag_AndTheReleaseBuildNeverReadsIt()
        {
            string perf = Code("Services/ArchPerf.cs");
            Assert.True(perf.Contains("\"cpu-architectures.perf\""), "the flag is named after the feature");
            Assert.True(Regex.IsMatch(perf, @"ModSettings\.Debug\(Flag\)"), "the flag is the one of the debug section");
            Assert.True(Regex.IsMatch(perf, @"return BuildInfo\.IsDebug \? Meter\.Begin\(\) : -1;"), "a release build starts no clock");
            Assert.True(perf.Contains("Stopwatch.GetTimestamp") && perf.Contains("Stopwatch.Frequency"), "the clock is the monotonic one");
            Assert.True(BodyOf(perf, "public static void End(").Contains("catch (Exception)"), "a measurement never throws");
            Assert.True(BodyOf(perf, "public static void FlushSession()").Contains("catch (Exception)"));
            Assert.True(perf.Contains("ModLog.Warn(ArchitecturesFeature.FeatureId, line)") && perf.Contains("ModLog.Info(ArchitecturesFeature.FeatureId, line)"), "over the budget is a warning, within it an info line");

            // Days and frames are the buckets; the rest is per call.
            string bucket = BodyOf(perf, "private static long BucketOf(");
            Assert.True(bucket.Contains("ArchPerfMeter.Day") && bucket.Contains("ArchPerfMeter.Rating") && bucket.Contains("TicksPerDay"));
            Assert.True(bucket.Contains("ArchPerfMeter.Designer") && bucket.Contains("Time.frameCount"));
            Assert.True(bucket.Contains("ArchPerfMeter.NoBucket"));
        }

        // ---- the pure half against the budgets ----

        /// <summary>
        /// The tests are compiled without optimization (<c>-optimize-</c>, so that a failure can be debugged): the JIT then neither inlines nor optimizes, and the same math runs about five
        /// times slower than in the shipped build (measured: 1 200 contributions take 0.27 ms here and 0.054 ms in a build with <c>-optimize+</c>). The pure half may therefore use up to
        /// this many times the budget here, which leaves it at 60 % of the budget or less in the real build - and the other half for what the game's part of the hook costs.
        /// </summary>
        private const double UnoptimizedBuildAllowance = 3.0;

        private static double BestOfMs(int runs, Action body)
        {
            for (int i = 0; i < 30; i++) body();   // the JIT
            double best = double.MaxValue;
            for (int run = 0; run < runs; run++)
            {
                long start = Stopwatch.GetTimestamp();
                body();
                double ms = (Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency;
                if (ms < best) best = ms;
            }
            return best;
        }

        [Test]
        public void TheRatingMath_OfAFullDaysCalls_IsInsideTheBudgetOfTheRating()
        {
            ArchCatalog catalog = ArchTestKit.BuiltInCatalog();
            BalanceConfig balance = ArchTestKit.BuiltInBalance();
            balance.stage = BalanceConfig.Stage2;
            var inputs = new RatingInputs();
            var families = new List<ArchFamily>(catalog.Families);
            float sink = 0f;

            // 1 200 questions a day at most (NFR-1): 300 CPUs in four segments.
            double ms = BestOfMs(40, () =>
            {
                for (int i = 0; i < 1200; i++)
                {
                    sink += RatingForecast.One(inputs, families[i % families.Count], (Segment)(i % 4), 1990 + i % 30, balance, 20f + i % 70, (i % 10) / 10f, 1 + i % 4, i % 2 == 0, 1f);
                }
            });
            Assert.True(sink > 0f);
            Assert.True(ms <= ArchPerfMeter.RatingBudgetMs * UnoptimizedBuildAllowance, "1 200 contributions take " + ms + " ms; the budget of the whole provider is " + ArchPerfMeter.RatingBudgetMs + " ms a day");
        }

        [Test]
        public void TheCostMath_OfOneCall_IsInsideTheBudgetOfTheCost()
        {
            DesignDef design = ArchTestKit.BuiltInCatalog().Get("x86").Design;
            float sink = 0f;
            const int calls = 1000;
            double ms = BestOfMs(40, () =>
            {
                for (int i = 0; i < calls; i++) sink += DesignMath.Cost(50000000f + i, design, 0.1f);
            });
            Assert.True(sink > 0f);
            Assert.True(ms / calls <= ArchPerfMeter.CostBudgetMs * UnoptimizedBuildAllowance, "one call takes " + ms / calls * 1000 + " microseconds; the budget is 5");
        }

        [Test]
        public void TheMonthlyMath_For300Cpus_And14Companies_IsInsideTheBudgetOfTheMonth()
        {
            ArchCatalog catalog = ArchTestKit.BuiltInCatalog();
            BalanceConfig balance = ArchTestKit.BuiltInBalance();
            balance.stage = BalanceConfig.Stage2;
            AiPreferenceTable preferences = ArchTestKit.BuiltInPreferences(catalog);

            // 300 CPUs with two years of monthly series (units sold and sold by contracts).
            var sold = new List<int[]>();
            var contracts = new List<int[]>();
            for (int cpu = 0; cpu < 300; cpu++)
            {
                var a = new int[25];
                var b = new int[25];
                for (int m = 0; m < a.Length; m++)
                {
                    a[m] = 1000 + cpu * 7 + m * 13;
                    b[m] = cpu % 5 == 0 ? 200 : 0;
                }
                sold.Add(a);
                contracts.Add(b);
            }
            var familyOf = new string[300];
            for (int cpu = 0; cpu < 300; cpu++) familyOf[cpu] = catalog.Families[cpu % catalog.Count].Id;

            // 14 companies, each with all the families researched and a tier set.
            var states = new List<CompanyArchState>();
            for (int c = 0; c < 14; c++)
            {
                var state = new CompanyArchState(100 + c, false) { Active = "proprietary" };
                foreach (ArchFamily family in catalog.Families)
                {
                    state.Families[family.Id] = new FamilyState(family.Id) { Researched = true, Progress = 1f, Support = 40f + c, Tier = (SupportTier)(c % 4) };
                }
                states.Add(state);
            }

            float sink = 0f;
            double ms = BestOfMs(30, () =>
            {
                // The ecosystem: the sum of the window and of the window a year ago for every CPU, and the shares.
                var sales = new Dictionary<string, long>(StringComparer.Ordinal);
                var before = new Dictionary<string, long>(StringComparer.Ordinal);
                for (int cpu = 0; cpu < 300; cpu++)
                {
                    long now = EcosystemMath.WindowSum(sold[cpu], contracts[cpu], 12);
                    long ago = EcosystemMath.WindowSum(sold[cpu], contracts[cpu], 12, 12);
                    long have;
                    sales.TryGetValue(familyOf[cpu], out have);
                    sales[familyOf[cpu]] = have + now;
                    before.TryGetValue(familyOf[cpu], out have);
                    before[familyOf[cpu]] = have + ago;
                }
                Dictionary<string, float> shares = EcosystemMath.Shares(sales, "proprietary");
                EcosystemMath.Shares(before, "proprietary");

                // The support of every company, then its decision.
                for (int c = 0; c < 14; c++)
                {
                    SupportMonthResult result = SupportCycle.RunMonth(states[c], catalog, balance.support, 1995, 1.2f, 1e12f);
                    sink += result.Upkeep;
                    var view = new AiView { CompanyId = states[c].CompanyId, UniqueId = c, Money = 1e11f, Year = 1995, Inflation = 1.2f, State = states[c], SegmentShare = new[] { 0.3f, 0.3f, 0.2f, 0.2f } };
                    AiDecision decision = ArchAiPolicy.DecideFull(view, preferences.Match(c), catalog, balance.ai, shares, balance.rating, balance.support);
                    if (decision.Tiers != null) sink += decision.Tiers.Count;
                }
            });
            Assert.True(sink > 0f);
            Assert.True(ms <= ArchPerfMeter.MonthBudgetMs * UnoptimizedBuildAllowance, "the pure half of a month (300 CPUs, 14 companies) takes " + ms + " ms; the budget of the whole month is " + ArchPerfMeter.MonthBudgetMs + " ms");
        }
    }
}
