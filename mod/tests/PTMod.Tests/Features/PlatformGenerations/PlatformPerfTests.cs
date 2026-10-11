using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using PTMod.Features.PlatformGenerations;
using PTMod.Tests.Architecture;

namespace PTMod.Tests.Features.PlatformGenerations
{
    /// <summary>
    /// TSK-020, NFR-1, NFR-2: the timers and the counters of the feature. The figures and the budgets of <see cref="PlatformPerf"/> (the design's
    /// 5 ms for a month, 50 ms for the full rebuild, 100 ms for the reconstruction together with it, scaled by the number of CPUs), the one warning
    /// per operation, the calls of the providers per game day, the real operations measured against those budgets on 1000 CPUs, and the places of
    /// the game side where the timers are started and stopped (checked on the source, the code cannot be loaded here).
    /// </summary>
    [Covers(typeof(PlatformPerf), typeof(PerfOp), typeof(PerfStat))]
    public class PlatformPerfTests
    {
        private static string FeatureDirectory()
        {
            return Path.Combine(SourceScan.SrcDirOrFail(), "Features", "PlatformGenerations");
        }

        private static string Code(string file)
        {
            string path = Path.Combine(FeatureDirectory(), file);
            Assert.True(File.Exists(path), "missing: " + path);
            return SourceScan.Strip(SourceScan.ReadText(path), false);
        }

        private static string Squeeze(string text)
        {
            return Regex.Replace(text, @"\s+", " ");
        }

        // ---- the budgets ----

        [Test]
        public void TheBudgets_AreTheDesignsFigures_AtAThousandCpus()
        {
            Assert.Equal(1000, PlatformPerf.BudgetCpus);
            Assert.Near(3.0, PlatformPerf.BudgetMs(PerfOp.ApplyMonth, 1000));
            Assert.Near(2.0, PlatformPerf.BudgetMs(PerfOp.RebuildCache, 1000));
            Assert.Near(50.0, PlatformPerf.BudgetMs(PerfOp.RebuildFull, 1000));
            Assert.Near(50.0, PlatformPerf.BudgetMs(PerfOp.Reconstruction, 1000));

            // The two combined budgets of the design: a month (5 ms), the reconstruction with the full rebuild (100 ms).
            Assert.Near(5.0, PlatformPerf.BudgetMs(PerfOp.ApplyMonth, 1000) + PlatformPerf.BudgetMs(PerfOp.RebuildCache, 1000));
            Assert.Near(100.0, PlatformPerf.BudgetMs(PerfOp.Reconstruction, 1000) + PlatformPerf.BudgetMs(PerfOp.RebuildFull, 1000));
        }

        [Test]
        public void TheBudget_GrowsWithTheMarket_AndNeverShrinksBelowTheBase()
        {
            Assert.Near(6.0, PlatformPerf.BudgetMs(PerfOp.ApplyMonth, 2000));
            Assert.Near(7.5, PlatformPerf.BudgetMs(PerfOp.ApplyMonth, 2500));
            Assert.Near(3.0, PlatformPerf.BudgetMs(PerfOp.ApplyMonth, 100), 1e-12, "a small market gets the base budget");
            Assert.Near(3.0, PlatformPerf.BudgetMs(PerfOp.ApplyMonth, 0));
            Assert.Near(3.0, PlatformPerf.BudgetMs(PerfOp.ApplyMonth, -5));
        }

        // ---- recording ----

        [Test]
        public void ARun_IsBooked_WithItsCountTotalAverageLastAndSlowest()
        {
            var perf = new PlatformPerf();
            Assert.Equal(0, perf.Of(PerfOp.ApplyMonth).Count);
            Assert.Near(0, perf.Of(PerfOp.ApplyMonth).AverageMs);

            perf.Record(PerfOp.ApplyMonth, 1.0, 100);
            perf.Record(PerfOp.ApplyMonth, 2.5, 120);
            perf.Record(PerfOp.ApplyMonth, 0.5, 130);

            PerfStat stat = perf.Of(PerfOp.ApplyMonth);
            Assert.Equal(3, stat.Count);
            Assert.Near(4.0, stat.TotalMs);
            Assert.Near(4.0 / 3, stat.AverageMs, 1e-12);
            Assert.Near(0.5, stat.LastMs);
            Assert.Near(2.5, stat.MaxMs);
            Assert.Equal(130, stat.LastCpus);
            Assert.Equal(0, stat.OverBudget);

            Assert.Equal(0, perf.Of(PerfOp.RebuildFull).Count, "another operation is not touched");
        }

        [Test]
        public void ARunOverTheBudget_IsCounted_AndTheBudgetFollowsTheNumberOfCpus()
        {
            var perf = new PlatformPerf();
            Assert.False(perf.Record(PerfOp.RebuildFull, 49.0, 1000), "under 50 ms at 1000 CPUs");
            Assert.True(perf.Record(PerfOp.RebuildFull, 51.0, 1000), "over");
            Assert.False(perf.Record(PerfOp.RebuildFull, 51.0, 2000), "the same time at 2000 CPUs is within 100 ms");
            Assert.True(perf.Record(PerfOp.RebuildFull, 101.0, 2000));
            Assert.Equal(2, perf.Of(PerfOp.RebuildFull).OverBudget);
        }

        [Test]
        public void ANegativeOrBrokenTime_CountsAsZero()
        {
            var perf = new PlatformPerf();
            perf.Record(PerfOp.RebuildCache, -3, 10);
            perf.Record(PerfOp.RebuildCache, double.NaN, -10);
            PerfStat stat = perf.Of(PerfOp.RebuildCache);
            Assert.Equal(2, stat.Count);
            Assert.Near(0, stat.TotalMs);
            Assert.Near(0, stat.MaxMs);
            Assert.Equal(0, stat.LastCpus);
            Assert.Equal(0, stat.OverBudget);
        }

        [Test]
        public void Stop_MeasuresTheClock_AndBooksTheRun()
        {
            var perf = new PlatformPerf();
            long start = PlatformPerf.Now();
            var watch = Stopwatch.StartNew();
            while (watch.ElapsedMilliseconds < 3) { }
            double ms = perf.Stop(PerfOp.Reconstruction, start, 40);

            Assert.True(ms >= 2.5, "at least the 3 ms that were waited: " + ms);
            Assert.True(ms < 1000, "and not wildly more: " + ms);
            Assert.Equal(1, perf.Of(PerfOp.Reconstruction).Count);
            Assert.Near(ms, perf.Of(PerfOp.Reconstruction).LastMs, 1e-9);
            Assert.Equal(40, perf.Of(PerfOp.Reconstruction).LastCpus);
        }

        [Test]
        public void MsBetween_NeverGoesNegative()
        {
            long now = PlatformPerf.Now();
            Assert.Near(0, PlatformPerf.MsBetween(now, now));
            Assert.Near(0, PlatformPerf.MsBetween(now + 1000, now), 0, "a clock that went back");
            Assert.True(PlatformPerf.MsBetween(now, now + Stopwatch.Frequency) > 999.0, "a second is a thousand milliseconds");
        }

        // ---- the warning ----

        [Test]
        public void TheWarning_ComesOnce_AndOnlyForAnOperationThatWentOver()
        {
            var perf = new PlatformPerf();
            Assert.False(perf.TakeWarning(PerfOp.ApplyMonth), "nothing ran");

            perf.Record(PerfOp.ApplyMonth, 1, 500);
            Assert.False(perf.TakeWarning(PerfOp.ApplyMonth), "within the budget");

            perf.Record(PerfOp.ApplyMonth, 9, 500);
            Assert.True(perf.TakeWarning(PerfOp.ApplyMonth), "the first time over");
            Assert.False(perf.TakeWarning(PerfOp.ApplyMonth), "one warning for the session");

            perf.Record(PerfOp.ApplyMonth, 20, 500);
            Assert.False(perf.TakeWarning(PerfOp.ApplyMonth), "still only the one");

            perf.Record(PerfOp.RebuildCache, 9, 500);
            Assert.True(perf.TakeWarning(PerfOp.RebuildCache), "each operation has its own");
        }

        // ---- the lines of the log ----

        [Test]
        public void TheLines_NameTheOperationTheTimeAndTheBudget()
        {
            var perf = new PlatformPerf();
            Assert.Equal("nothing timed yet", perf.Summary());

            perf.Record(PerfOp.ApplyMonth, 0.4, 180);
            Assert.Equal("ApplyMonth 0.4 ms of 3 (180 CPUs)", perf.DescribeLast(PerfOp.ApplyMonth));

            perf.Record(PerfOp.ApplyMonth, 4.0, 180);
            perf.Record(PerfOp.RebuildFull, 12.34, 900);
            string summary = perf.Summary();
            Assert.True(summary.Contains("ApplyMonth 2x, avg 2.2 ms, max 4 ms, over budget 1x"), summary);
            Assert.True(summary.Contains("RebuildFull 1x, avg 12.34 ms, max 12.34 ms"), summary);
            Assert.False(summary.Contains("Reconstruction"), "an operation that never ran is not listed: " + summary);
        }

        [Test]
        public void TheNumbersOfTheLines_AreCultureFree()
        {
            System.Globalization.CultureInfo before = System.Threading.Thread.CurrentThread.CurrentCulture;
            try
            {
                System.Threading.Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo("ru-RU");
                var perf = new PlatformPerf();
                perf.Record(PerfOp.ApplyMonth, 1.5, 100);
                Assert.True(perf.DescribeLast(PerfOp.ApplyMonth).Contains("1.5 ms"), perf.DescribeLast(PerfOp.ApplyMonth));
            }
            finally
            {
                System.Threading.Thread.CurrentThread.CurrentCulture = before;
            }
        }

        // ---- the calls of the providers ----

        [Test]
        public void TheCallsOfTheProviders_AreCounted_AndSummedUpPerGameDay()
        {
            var perf = new PlatformPerf();
            Assert.Equal("provider calls: no game day yet", perf.TakeProviderLine());

            for (int i = 0; i < 10; i++) perf.RatingCalls++;
            for (int i = 0; i < 4; i++) perf.ChanceCalls++;
            Assert.Equal(10, perf.RatingCalls);
            perf.EndDay();
            Assert.Equal(0, perf.RatingCalls, "a new day starts from 0");
            Assert.Equal(0, perf.ChanceCalls);

            for (int i = 0; i < 20; i++) perf.RatingCalls++;
            perf.EndDay();
            Assert.Equal(2, perf.PeriodDays);

            string line = perf.TakeProviderLine();
            Assert.Equal("provider calls per day over 2 day(s): rating avg 15 peak 20, chance avg 2 peak 4", line);
            Assert.Equal(0, perf.PeriodDays, "the period begins again");
            Assert.Equal("provider calls: no game day yet", perf.TakeProviderLine());
        }

        [Test]
        public void Reset_ForgetsEverything()
        {
            var perf = new PlatformPerf();
            perf.Record(PerfOp.RebuildFull, 99, 10);
            perf.RatingCalls = 5;
            perf.EndDay();
            perf.Reset();

            Assert.Equal(0, perf.Of(PerfOp.RebuildFull).Count);
            Assert.Equal("nothing timed yet", perf.Summary());
            Assert.Equal(0, perf.PeriodDays);
            Assert.True(perf.Record(PerfOp.RebuildFull, 99, 10));
            Assert.True(perf.TakeWarning(PerfOp.RebuildFull), "the warning is available again");
        }

        // ---- the real operations against the budgets (NFR-1) ----

        private static double Best(int runs, Action action)
        {
            action();   // warm-up: the first call pays for the JIT
            double best = double.MaxValue;
            for (int i = 0; i < runs; i++)
            {
                long start = PlatformPerf.Now();
                action();
                best = Math.Min(best, PlatformPerf.MsBetween(start, PlatformPerf.Now()));
            }
            return best;
        }

        [Test]
        public void TheFullRebuild_OfAThousandCpusOver720Months_IsWithinItsBudget()
        {
            List<CpuLedgerView> cpus = LedgerTestData.Market(1000, 720, 11u);
            var ledger = new InstalledBaseLedger();
            PlatformBalance balance = PlatformBalance.Defaults();

            var perf = new PlatformPerf();
            double best = Best(5, () => ledger.RebuildFull(cpus, 719, balance));
            perf.Record(PerfOp.RebuildFull, best, ledger.CpuCount);

            Assert.True(ledger.CpuCount > 900, "the market was built: " + ledger.CpuCount);
            Assert.Equal(0, perf.Of(PerfOp.RebuildFull).OverBudget, "RebuildFull of " + ledger.CpuCount + " CPUs took " + best.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " ms (best of 5), the budget is 50");
        }

        [Test]
        public void TheMonthlyStep_OfAThousandCpus_IsWithinItsBudget()
        {
            List<CpuLedgerView> cpus = LedgerTestData.Market(1000, 720, 12u);
            var ledger = new InstalledBaseLedger();
            PlatformBalance balance = PlatformBalance.Defaults();
            ledger.RebuildFull(cpus, 600, balance);

            int month = 600;
            var perf = new PlatformPerf();
            double best = Best(5, () => ledger.ApplyMonth(cpus, ++month, balance));
            perf.Record(PerfOp.ApplyMonth, best, ledger.CpuCount);

            Assert.True(ledger.IsConsistent());
            Assert.Equal(0, perf.Of(PerfOp.ApplyMonth).OverBudget, "ApplyMonth of " + ledger.CpuCount + " CPUs took " + best.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) + " ms (best of 5), the budget is 3");
        }

        [Test]
        public void TheMultipliers_OfAThousandCpus_AreWithinTheBudgetOfTheCache()
        {
            // The cache rebuild asks the engine for one answer per CPU; the game side adds only the reading of the CPUs.
            List<CpuLedgerView> cpus = LedgerTestData.Market(1000, 720, 13u);
            var ledger = new InstalledBaseLedger();
            PlatformBalance balance = PlatformBalance.Defaults();
            ledger.RebuildFull(cpus, 719, balance);
            var engine = new PlatformEffectEngine();
            var timeline = new PlatformTimeline();
            GameDate today = new GameDate(2030, 12, 1);

            SocketId[] sockets = new SocketId[cpus.Count];
            for (int i = 0; i < cpus.Count; i++) sockets[i] = cpus[i].SocketKey.StartsWith("custom:", StringComparison.Ordinal)
                ? SocketIdentity.Custom(int.Parse(cpus[i].SocketKey.Substring(7)), cpus[i].BasePackageName)
                : SocketIdentity.Standard(cpus[i].BasePackageName);

            double sum = 0;
            var perf = new PlatformPerf();
            double best = Best(5, () =>
            {
                sum = 0;
                for (int i = 0; i < cpus.Count; i++)
                {
                    RatingBreakdown rating = engine.RatingFor(cpus[i].CompanyKey, sockets[i], cpus[i].GenId, ledger, timeline, today, balance);
                    sum += rating.ConsumerMult + rating.IndustriesMult;
                }
            });
            perf.Record(PerfOp.RebuildCache, best, cpus.Count);

            Assert.True(sum > 1000, "the multipliers were computed");
            Assert.Equal(0, perf.Of(PerfOp.RebuildCache).OverBudget, "1000 multipliers took " + best.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) + " ms (best of 5), the budget is 2");
        }

        // ---- where the game side starts and stops the timers (checked on the source) ----

        [Test]
        public void TheMonthlyPass_TimesApplyMonth_AndReportsIt()
        {
            string code = Squeeze(Code("PlatformSubscriptions.cs"));
            Assert.True(code.Contains("long started = PlatformPerf.Now();"), "the clock starts before the ledger step");
            Assert.True(code.Contains("feature.Perf.Stop(PerfOp.ApplyMonth, started, feature.InstalledBase.CpuCount);"), "ApplyMonth is booked");
            Assert.True(code.Contains("feature.ReportPerf(PerfOp.ApplyMonth);"), "and reported");
            Assert.True(code.Contains("if (ModSettings.VerboseLog)"), "the monthly line is made only when the verbose log is on");
            Assert.True(code.Contains("feature.Perf.TakeProviderLine()"), "with the calls of the providers per day");
        }

        [Test]
        public void TheResynchronization_TimesTheReconstruction_AndTheFullRebuild()
        {
            string code = Squeeze(Code("PlatformSubscriptions.cs"));
            Assert.True(code.Contains("feature.Perf.Stop(PerfOp.Reconstruction, reconStarted, _recon.Count);"));
            Assert.True(code.Contains("feature.ReportPerf(PerfOp.Reconstruction);"));
            Assert.True(code.Contains("feature.Perf.Stop(PerfOp.RebuildFull, baseStarted, feature.InstalledBase.CpuCount);"));
            Assert.True(code.Contains("feature.ReportPerf(PerfOp.RebuildFull);"));
        }

        [Test]
        public void TheCacheRebuild_IsTimedInTheProvider_WhateverCallsIt()
        {
            string code = Squeeze(Code("PlatformModifierProvider.cs"));
            Assert.True(code.Contains("long started = PlatformPerf.Now(); RebuildCacheCore(); _feature.Perf.Stop(PerfOp.RebuildCache, started, _rebuiltCount); _feature.ReportPerf(PerfOp.RebuildCache);"),
                "the one public RebuildCache is the timer around the work");
        }

        [Test]
        public void TheProviders_CountTheirCalls_WithPlainIncrements()
        {
            string code = Squeeze(Code("PlatformModifierProvider.cs"));
            Assert.True(code.Contains("_feature.Perf.RatingCalls++;"));
            Assert.True(code.Contains("_feature.Perf.ChanceCalls++;"));
        }

        [Test]
        public void TheDay_IsClosedByATick_ThatDoesNothingElse()
        {
            string register = Squeeze(Code("PlatformSubscriptions.cs"));
            Assert.True(register.Contains("ctx.OnTick(TickPhase.DayPassed, OnDayPassed);"));
            Assert.True(register.Contains("private void OnDayPassed() { _feature.Perf.EndDay(); }"), "two additions per game day");
        }

        [Test]
        public void TheWarningAboutASlowOperation_IsOneLineInTheLog_AndTheTimeGoesToTheVerboseLog()
        {
            string code = Squeeze(Code("PlatformGenerationsFeature.cs"));
            Assert.True(code.Contains("if (perf.TakeWarning(op)) ModLog.Warn(FeatureId,"), "once per session");
            Assert.True(code.Contains("else if (ModSettings.VerboseLog) ModLog.Debug(FeatureId, \"perf: \" + perf.DescribeLast(op));"), "otherwise only when verbose");
        }
    }
}
