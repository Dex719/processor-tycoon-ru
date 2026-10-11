using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using PTMod.Features.ContractTransparency;
using PTMod.Tests.Architecture;

namespace PTMod.Tests.Features.ContractTransparency
{
    /// <summary>
    /// TSK-020 (NFR-1, NFR-2): the timers. The statistics and the rule which measurements reach the log are pure and tested here; the timer itself (<c>PerfTimer</c>, one flag test with the verbose log
    /// off) and its places in <c>OfferGate</c>, <c>TenderRecorder</c>, <c>AutomationLogger</c> and <c>TenderMailer</c> are checked in the sources.
    /// </summary>
    [Covers(typeof(TimingSites), typeof(TimingSample), typeof(TimingStats))]
    public class TimingTests
    {
        private static string FeatureDir()
        {
            return Path.Combine(SourceScan.SrcDirOrFail(), "Features", "ContractTransparency");
        }

        private static string Code(string relative)
        {
            string path = Path.Combine(FeatureDir(), relative.Replace('/', Path.DirectorySeparatorChar));
            Assert.True(File.Exists(path), "missing " + path);
            return SourceScan.Strip(SourceScan.ReadText(path), false);
        }

        // ---- the budgets are the table of the design ----

        [Test]
        public void TheBudgets_AreThoseOfThePerformanceTableOfTheDesign()
        {
            Assert.Equal(0.05, TimingSites.BudgetMs(TimingSites.MailerDay));          // ежедневно, ≤ 0.05 мс
            Assert.Equal(5.0, TimingSites.BudgetMs(TimingSites.Odds));                 // разбор шанса ≤ 5 мс
            Assert.Equal(5.0, TimingSites.BudgetMs(TimingSites.OfferGatePrefix));      // prefix AddOffer (ручной) ≤ 5 мс
            Assert.Equal(1.0, TimingSites.BudgetMs(TimingSites.TenderPrefix));         // ChooseCpu ≤ 1 мс на контракт
            Assert.Equal(1.0, TimingSites.BudgetMs(TimingSites.TenderPostfix));
            Assert.Equal(20.0, TimingSites.BudgetMs(TimingSites.AutomationPrefix));    // прогон автоматики ≤ 20 мс
            Assert.Equal(20.0, TimingSites.BudgetMs(TimingSites.AutomationFinalizer));
            Assert.Equal(10.0, TimingSites.BudgetMs(TimingSites.LogWrite));            // сохранение журнала ≤ 10 мс
            Assert.Equal(-1.0, TimingSites.BudgetMs("no.such.place"));
            Assert.Equal(-1.0, TimingSites.BudgetMs(null));
        }

        [Test]
        public void TheSites_AreUnique_AndEveryOneHasABudget()
        {
            IReadOnlyList<string> all = TimingSites.All;
            Assert.Equal(all.Count, all.Distinct().Count());
            Assert.True(all.Count >= 12, "all the places are listed");
            foreach (string site in all) Assert.True(TimingSites.BudgetMs(site) > 0.0, site + " has no budget");

            // Every constant is in the list and the other way round.
            var constants = typeof(TimingSites).GetFields(BindingFlags.Public | BindingFlags.Static).Where(f => f.IsLiteral).Select(f => (string)f.GetRawConstantValue()).ToList();
            Assert.Equal(constants.OrderBy(s => s, StringComparer.Ordinal).ToArray(), all.OrderBy(s => s, StringComparer.Ordinal).ToArray());
        }

        // ---- the statistics ----

        [Test]
        public void TheStatistics_CountAverageAndKeepTheLongest()
        {
            var stats = new TimingStats();
            stats.Add(TimingSites.MailerDay, 0.002);
            stats.Add(TimingSites.MailerDay, 0.010);
            TimingSample sample = stats.Add(TimingSites.MailerDay, 0.006);

            Assert.Equal(3, sample.Count);
            Assert.Near(0.006, sample.LastMs);
            Assert.Near(0.018, sample.TotalMs);
            Assert.Near(0.006, sample.AverageMs);
            Assert.Near(0.010, sample.MaxMs);
            Assert.Near(0.05, sample.BudgetMs);
            Assert.False(sample.OverBudget);
            Assert.Equal(1, stats.Count);

            Assert.True(ReferenceEquals(sample, stats.Get(TimingSites.MailerDay)));
            Assert.Null(stats.Get(TimingSites.Odds));
            Assert.Null(stats.Get(null));

            stats.Clear();
            Assert.Equal(0, stats.Count);
            Assert.Null(stats.Get(TimingSites.MailerDay));
        }

        [Test]
        public void ABadTime_CountsAsZero_AndAnEmptyNameIsRefused()
        {
            var stats = new TimingStats();
            Assert.Near(0.0, stats.Add(TimingSites.Odds, double.NaN).LastMs);
            Assert.Near(0.0, stats.Add(TimingSites.Odds, -3.0).LastMs);
            Assert.Equal(2, stats.Get(TimingSites.Odds).Count);

            Assert.Throws<ArgumentException>(() => stats.Add(null, 1.0));
            Assert.Throws<ArgumentException>(() => stats.Add(string.Empty, 1.0));
        }

        [Test]
        public void APlaceWithoutABudget_IsMeasuredAllTheSame_AndNeverOver()
        {
            var stats = new TimingStats();
            TimingSample sample = stats.Add("elsewhere", 900.0);

            Assert.Near(-1.0, sample.BudgetMs);
            Assert.False(sample.OverBudget);
            Assert.False(TimingStats.Format(sample).Contains("budget"));
            Assert.False(TimingStats.Format(sample).Contains("OVER"));
        }

        [Test]
        public void TheTime_IsOverTheBudgetOnlyAboveIt()
        {
            var stats = new TimingStats();
            Assert.False(stats.Add(TimingSites.Odds, 5.0).OverBudget, "the budget itself is allowed");
            Assert.True(stats.Add(TimingSites.Odds, 5.001).OverBudget);
            Assert.False(stats.Add(TimingSites.Odds, 1.0).OverBudget, "and the verdict is about the last measurement");
        }

        [Test]
        public void TicksOfTheStopwatch_BecomeMilliseconds()
        {
            Assert.Near(1000.0, TimingStats.ToMilliseconds(10000000, 10000000));   // one second of a 10 MHz stopwatch
            Assert.Near(0.5, TimingStats.ToMilliseconds(5000, 10000000));
            Assert.Near(0.0, TimingStats.ToMilliseconds(-5, 10000000), 0.0, "a clock that went back is zero");
            Assert.Near(0.0, TimingStats.ToMilliseconds(5, 0), 0.0, "and so is a clock without a frequency");
        }

        // ---- which measurements reach the log ----

        [Test]
        public void TheLog_GetsTheFirstTwenty_ThenEveryHundredth_AndAnythingOverTheBudget()
        {
            var stats = new TimingStats();
            var logged = new List<int>();
            for (int n = 1; n <= 450; n++)
            {
                TimingSample sample = stats.Add(TimingSites.OfferGatePrefix, 0.001);
                if (TimingStats.ShouldLog(sample)) logged.Add(n);
            }

            var expected = Enumerable.Range(1, TimingStats.AlwaysLogFirst).ToList();
            expected.AddRange(new[] { 100, 200, 300, 400 });
            Assert.Equal(expected, logged);
        }

        [Test]
        public void ASlowMeasurement_IsLoggedAtOnce_AtAnyCount()
        {
            var stats = new TimingStats();
            for (int n = 0; n < 57; n++) stats.Add(TimingSites.Odds, 0.1);

            TimingSample slow = stats.Add(TimingSites.Odds, 6.0);
            Assert.Equal(58, slow.Count);
            Assert.True(TimingStats.ShouldLog(slow));
            Assert.False(TimingStats.ShouldLog(stats.Add(TimingSites.Odds, 0.1)));
            Assert.False(TimingStats.ShouldLog(null));
        }

        [Test]
        public void TheLine_NamesThePlaceTheTimeTheCountTheAverageTheMaximumAndTheBudget()
        {
            var stats = new TimingStats();
            stats.Add(TimingSites.OfferGatePrefix, 0.30);
            TimingSample sample = stats.Add(TimingSites.OfferGatePrefix, 0.02);

            Assert.Equal("time: OfferGate.prefix 0.020 ms (n=2, avg 0.160 ms, max 0.300 ms, budget 5 ms)", TimingStats.Format(sample));
            Assert.Equal(string.Empty, TimingStats.Format(null));

            TimingSample slow = stats.Add(TimingSites.MailerDay, 0.4);
            Assert.Equal("time: TenderMailer.OnDayPassed 0.400 ms (n=1, avg 0.400 ms, max 0.400 ms, budget 0.05 ms) OVER BUDGET", TimingStats.Format(slow));
        }

        [Test]
        public void TheLine_IsTheSameWhateverTheCulture()
        {
            var stats = new TimingStats();
            TimingSample sample = stats.Add(TimingSites.MailerDay, 0.0125);

            System.Globalization.CultureInfo before = System.Threading.Thread.CurrentThread.CurrentCulture;
            try
            {
                System.Threading.Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo("ru-RU");
                Assert.True(TimingStats.Format(sample).Contains("0.013 ms") || TimingStats.Format(sample).Contains("0.012 ms"), TimingStats.Format(sample));
                Assert.False(Regex.IsMatch(TimingStats.Format(sample), @"\d,\d"), "no decimal comma: " + TimingStats.Format(sample));
            }
            finally
            {
                System.Threading.Thread.CurrentThread.CurrentCulture = before;
            }
        }

        // ---- the timers in the code ----

        [Test]
        public void TheTimer_IsAFlagTestWithTheVerboseLogOff_AndWritesOnlyDebugLines()
        {
            string code = Code("PerfTimer.cs");

            Assert.True(Regex.IsMatch(code, @"ModSettings\.VerboseLog\s*\?\s*new\s+PerfTimer\(\s*site\s*,\s*Stopwatch\.GetTimestamp\(\)\s*\)\s*:\s*default\(PerfTimer\)"), "off: the default struct, no stopwatch");
            Assert.True(Regex.IsMatch(code, @"if\s*\(\s*_site\s*==\s*null\s*\)\s*return\s*;"), "a timer that did not start does nothing on Dispose");
            Assert.True(code.Contains("ModLog.Debug("), "the measurements go to the debug level of the log");
            Assert.False(Regex.IsMatch(code, @"ModLog\.(Info|Warn|Error)\("), "and never to a higher one");
            Assert.True(Regex.IsMatch(code, @"catch\s*\(\s*Exception\s*\)"), "a failure of the measuring never reaches the code it measures");
            Assert.True(code.Contains("TimingStats.ShouldLog("), "and the rule of the statistics decides what is written");
            Assert.True(Regex.IsMatch(code, @"struct\s+PerfTimer\s*:\s*IDisposable"), "a struct: no allocation per call");
        }

        [Test]
        public void TheFourClasses_TimeTheirHooks_AtTheirPlaces()
        {
            AssertTimed("Patches/OfferGate.cs", "OfferGatePrefix", "OfferGatePostfix");
            AssertTimed("Patches/TenderRecorder.cs", "TenderPrefix", "TenderPostfix");
            AssertTimed("Patches/AutomationLogger.cs", "AutomationPrefix", "AutomationFinalizer", "AutomationPreview");
            AssertTimed("TenderMailer.cs", "MailerDay");
        }

        private static void AssertTimed(string file, params string[] constants)
        {
            string code = Code(file);
            foreach (string name in constants)
            {
                Assert.True(Regex.IsMatch(code, @"using\s*\(\s*PerfTimer\.Start\(\s*TimingSites\." + name + @"\s*\)\s*\)"), file + " has no timer at TimingSites." + name);
            }
        }

        [Test]
        public void EveryPlaceOfTheList_HasATimerInTheSources_AndEveryTimerNamesAPlaceOfTheList()
        {
            var constants = typeof(TimingSites).GetFields(BindingFlags.Public | BindingFlags.Static).Where(f => f.IsLiteral).Select(f => f.Name).ToList();
            var started = new HashSet<string>(StringComparer.Ordinal);

            foreach (string path in SourceScan.Files(FeatureDir(), ".cs"))
            {
                string code = SourceScan.Strip(SourceScan.ReadText(path), false);
                foreach (Match m in Regex.Matches(code, @"PerfTimer\.Start\(\s*(?<arg>[^)]*)\)"))
                {
                    string arg = m.Groups["arg"].Value.Trim();
                    if (arg == "site") continue;   // the declaration in PerfTimer itself
                    Match site = Regex.Match(arg, @"^TimingSites\.(\w+)$");
                    Assert.True(site.Success, SourceScan.Relative(FeatureDir(), path) + ": a timer must name its place with a TimingSites constant, not \"" + arg + "\"");
                    Assert.True(constants.Contains(site.Groups[1].Value), "no such place: " + site.Groups[1].Value);
                    started.Add(site.Groups[1].Value);
                }
            }

            foreach (string name in constants) Assert.True(started.Contains(name), "TimingSites." + name + " is never timed");
        }

        [Test]
        public void TheTimersStayOutsideTheGuards_ButInsideTheBodiesOfThePatches_SoTheCostOfTheGuardCounts()
        {
            // The finalizer of the automation lowers its flag before anything else (PatchSourceTests); the timer comes after it.
            string code = Code("Patches/AutomationLogger.cs");
            int flag = code.IndexOf("OfferGate.AutomationScope = false;", StringComparison.Ordinal);
            int timer = code.IndexOf("TimingSites.AutomationFinalizer", StringComparison.Ordinal);
            Assert.True(flag > 0 && timer > flag, "the flag is lowered before the timer starts");

            // The daily handler is timed with the empty day included: that is the metric of NFR-1.
            string mailer = Code("TenderMailer.cs");
            int start = mailer.IndexOf("TimingSites.MailerDay", StringComparison.Ordinal);
            int check = mailer.IndexOf("PendingTenderCount == 0", StringComparison.Ordinal);
            Assert.True(start > 0 && check > start, "the timer starts before the test of the counter, so a day without tenders is measured");
        }
    }
}
