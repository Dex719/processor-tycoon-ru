using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using PTMod.Features.GpuProductLine;
using PTMod.Tests.Architecture;

namespace PTMod.Tests.Features.GpuProductLine
{
    /// <summary>
    /// The timer of the handlers (TSK-028, NFR-1) on a clock the test drives: a day is the sum of its handlers, a line goes out once per game month with the average and
    /// the maximum, the budgets of NFR-1 are 0.5 ms and 5 ms, a meter whose flag is off costs nothing and records nothing, a log that throws does not stop the game; and the
    /// text of the feature: every handler of a day or a month goes through <c>Timed</c> and the meter is created with the verbose flag.
    /// </summary>
    [Covers(typeof(GpuPerfMeter))]
    public class GpuPerfMeterTests
    {
        private const long TicksPerMs = 1000;   // a clock of a thousand ticks a millisecond

        private sealed class Rig
        {
            public long Now = 5000;
            public bool On = true;
            public readonly List<string> Lines = new List<string>();
            public readonly List<bool> Overs = new List<bool>();
            public GpuPerfMeter Meter;

            public Rig()
            {
                Meter = new GpuPerfMeter(() => On, () => Now, TicksPerMs * 1000, (line, over) =>
                {
                    Lines.Add(line);
                    Overs.Add(over);
                });
            }

            /// <summary>One handler that takes <paramref name="ms"/> milliseconds of the kind on the date.</summary>
            public void Run(string kind, double ms, DateTime date)
            {
                long started = Meter.Begin();
                Now += (long)(ms * TicksPerMs);
                Meter.End(kind, started, date);
            }
        }

        [Test]
        public void TheHandlersOfOneDay_AreOneSample_TheirSum()
        {
            var rig = new Rig();
            var day = new DateTime(1995, 3, 10);
            rig.Run(GpuPerfMeter.Day, 0.1, day);   // production
            rig.Run(GpuPerfMeter.Day, 0.2, day);   // market
            rig.Run(GpuPerfMeter.Day, 0.05, day);  // lineup
            Assert.Equal(1, rig.Meter.Samples(GpuPerfMeter.Day), "one day, three handlers");
            Assert.Near(0.35, rig.Meter.AverageMs(GpuPerfMeter.Day), 1e-9, "0.1 + 0.2 + 0.05");
            Assert.Near(0.35, rig.Meter.MaxMs(GpuPerfMeter.Day), 1e-9);

            rig.Run(GpuPerfMeter.Day, 0.45, day.AddDays(1));
            Assert.Equal(2, rig.Meter.Samples(GpuPerfMeter.Day), "the next day is the second sample");
            Assert.Near(0.4, rig.Meter.AverageMs(GpuPerfMeter.Day), 1e-9, "(0.35 + 0.45) / 2");
            Assert.Near(0.45, rig.Meter.MaxMs(GpuPerfMeter.Day), 1e-9, "the longer day");
        }

        [Test]
        public void TheMonthsHandlers_AreOneSampleOfTheMonth_AndDoNotMixWithTheDay()
        {
            var rig = new Rig();
            var first = new DateTime(1996, 4, 1);
            rig.Run(GpuPerfMeter.Day, 0.3, first);
            rig.Run(GpuPerfMeter.Month, 1.5, first);   // nodes
            rig.Run(GpuPerfMeter.Month, 2.0, first);   // market
            rig.Run(GpuPerfMeter.Month, 0.5, first);   // lineup
            Assert.Equal(1, rig.Meter.Samples(GpuPerfMeter.Month));
            Assert.Near(4.0, rig.Meter.AverageMs(GpuPerfMeter.Month), 1e-9, "1.5 + 2 + 0.5");
            Assert.Near(0.3, rig.Meter.AverageMs(GpuPerfMeter.Day), 1e-9, "the day of the 1st does not take the month");
        }

        [Test]
        public void AnotherMonth_WritesTheLineOfTheMonthThatEnded_Once()
        {
            var rig = new Rig();
            for (int day = 1; day <= 30; day++) rig.Run(GpuPerfMeter.Day, 0.1, new DateTime(1995, 6, day));
            rig.Run(GpuPerfMeter.Month, 1.0, new DateTime(1995, 6, 1));
            Assert.Equal(0, rig.Lines.Count, "a month is in progress");

            rig.Run(GpuPerfMeter.Day, 0.2, new DateTime(1995, 7, 1));
            Assert.Equal(1, rig.Lines.Count, "the first sample of July closes June");
            string line = rig.Lines[0];
            Assert.True(line.StartsWith("perf 1995-06: "), line);
            Assert.True(line.Contains("day n=30 avg=0.1000 ms max=0.1000 ms (budget 0.5 ms: ok)"), line);
            Assert.True(line.Contains("month n=1 avg=1.0000 ms max=1.0000 ms (budget 5.0 ms: ok)"), line);
            Assert.True(line.IndexOf("day n=", StringComparison.Ordinal) < line.IndexOf("month n=", StringComparison.Ordinal), "the day first");
            Assert.False(rig.Overs[0]);
            Assert.Equal(1, rig.Meter.LinesWritten);

            // July is the new period: the day of the 1st is the first sample of it
            rig.Run(GpuPerfMeter.Day, 0.2, new DateTime(1995, 7, 2));
            rig.Meter.Flush();
            Assert.Equal(2, rig.Lines.Count);
            Assert.True(rig.Lines[1].StartsWith("perf 1995-07: ") && rig.Lines[1].Contains("day n=2 "), rig.Lines[1]);
            rig.Meter.Flush();
            Assert.Equal(2, rig.Lines.Count, "nothing to write the second time");
        }

        [Test]
        public void AnAverageOverTheBudget_IsMarkedOver_ForTheDayAndForTheMonth()
        {
            var rig = new Rig();
            rig.Run(GpuPerfMeter.Day, 0.7, new DateTime(2000, 1, 5));
            rig.Run(GpuPerfMeter.Month, 6.0, new DateTime(2000, 1, 1));
            Assert.Equal(new[] { GpuPerfMeter.Day, GpuPerfMeter.Month }, rig.Meter.OverBudget().ToArray(), "0.7 > 0.5 and 6 > 5");
            rig.Meter.Flush();
            Assert.True(rig.Overs[0], "the line is flagged");
            Assert.True(rig.Lines[0].Contains("OVER"), rig.Lines[0]);

            var fine = new Rig();
            fine.Run(GpuPerfMeter.Day, 0.5, new DateTime(2000, 1, 5));
            fine.Run(GpuPerfMeter.Month, 5.0, new DateTime(2000, 1, 1));
            Assert.Equal(0, fine.Meter.OverBudget().Count, "the budget itself holds (at most)");
            fine.Run(GpuPerfMeter.UiDay, 2.5, new DateTime(2000, 1, 5));
            Assert.Equal(new[] { GpuPerfMeter.UiDay }, fine.Meter.OverBudget().ToArray(), "the refresh of the window has 2 ms");
        }

        [Test]
        public void TheBudgets_AreTheOnesOfNfr1()
        {
            Assert.Equal(0.5, GpuPerfMeter.BudgetOf(GpuPerfMeter.Day));
            Assert.Equal(5.0, GpuPerfMeter.BudgetOf(GpuPerfMeter.Month));
            Assert.Equal(2.0, GpuPerfMeter.BudgetOf(GpuPerfMeter.UiDay));
            Assert.Equal(0.0, GpuPerfMeter.BudgetOf("anything else"));
            Assert.Equal(0.0, GpuPerfMeter.BudgetOf(null));
        }

        [Test]
        public void AMeterWithTheFlagOff_StartsNothing_AndRecordsNothing()
        {
            var rig = new Rig { On = false };
            long start = rig.Meter.Begin();
            Assert.Equal(0L, start, "not measured");
            rig.Now += 10 * TicksPerMs;
            rig.Meter.End(GpuPerfMeter.Day, start, new DateTime(1995, 1, 1));
            Assert.Equal(0, rig.Meter.Samples(GpuPerfMeter.Day));
            rig.Meter.Flush();
            Assert.Equal(0, rig.Lines.Count);

            // a flag that throws is off; a missing flag is off
            var throwing = new GpuPerfMeter(() => { throw new InvalidOperationException("flag"); }, () => 1, 1000, null);
            Assert.False(throwing.Enabled);
            Assert.Equal(0L, throwing.Begin());
            Assert.False(new GpuPerfMeter(null, () => 1, 1000, null).Enabled);
        }

        [Test]
        public void AClockAtZero_StillGivesAStartThatCounts()
        {
            long now = 0;
            var meter = new GpuPerfMeter(() => true, () => now, 1000, null);
            long start = meter.Begin();
            Assert.NotEqual(0L, start, "0 means \"not measured\"");
            now = 2;
            meter.End(GpuPerfMeter.Day, start, new DateTime(1995, 1, 1));
            Assert.Equal(1, meter.Samples(GpuPerfMeter.Day));
        }

        [Test]
        public void AClockThatGoesBack_AddsNothing_AndALogThatThrows_StopsNothing()
        {
            var rig = new Rig();
            long start = rig.Meter.Begin();
            rig.Now -= 100;
            rig.Meter.End(GpuPerfMeter.Day, start, new DateTime(1995, 1, 1));
            Assert.Equal(0.0, rig.Meter.AverageMs(GpuPerfMeter.Day), "a negative time is 0");

            var meter = new GpuPerfMeter(() => true, () => 100, 1000, (line, over) => { throw new InvalidOperationException("log"); });
            meter.Add(GpuPerfMeter.Day, 10, new DateTime(1995, 1, 1));
            meter.Flush();   // the exception of the log stays in the meter
            Assert.Equal(1, meter.LinesWritten);
        }

        [Test]
        public void TheArguments_AreChecked()
        {
            Assert.Throws<ArgumentNullException>(() => new GpuPerfMeter(() => true, null, 1000, null));
            Assert.Throws<ArgumentOutOfRangeException>(() => new GpuPerfMeter(() => true, () => 1, 0, null));
            var meter = new GpuPerfMeter(() => true, () => 1, 1000, null);
            meter.Add(null, 5, new DateTime(1995, 1, 1));
            meter.Add(string.Empty, 5, new DateTime(1995, 1, 1));
            Assert.Equal(0, meter.Samples(null));
            Assert.Equal(0, meter.Samples(string.Empty));
        }

        [Test]
        public void TheFeature_TimesEveryHandlerOfADayAndOfAMonth_WhenTheVerboseLogIsOn()
        {
            string path = Path.Combine(GpuFiles.FeatureSourceDir, "GpuFeature.cs");
            string code = SourceScan.Strip(SourceScan.ReadText(path), false);

            Assert.True(code.Contains("new GpuPerfMeter(() => ModSettings.VerboseLog, Stopwatch.GetTimestamp, Stopwatch.Frequency, LogPerf)"), "the flag, the real clock, the log");
            Assert.True(Regex.IsMatch(code, @"internal void Timed\(string kind, string siteId, Action body\)\s*\{[^}]*long started = perf != null \? perf\.Begin\(\) : 0L;\s*Guard\(siteId, body\);\s*if \(started != 0L\) perf\.End\(kind, started, ModTicks\.Today\);"),
                "Timed: begin, the handler under Guard, end");

            string[] days = { "production", "research", "projects", "market", "market/day", "lineup/day", "rivals/stock", "rivals/day", "synergy/day" };
            foreach (string site in days)
                Assert.True(code.Contains("Timed(GpuPerfMeter.Day, \"" + site + "\", "), "the day handler " + site);
            string[] months = { "month/nodes", "market/month", "lineup/month", "eras/month", "rivals/month", "synergy/month" };
            foreach (string site in months)
                Assert.True(code.Contains("Timed(GpuPerfMeter.Month, \"" + site + "\", "), "the month handler " + site);
            Assert.True(code.Contains("Timed(GpuPerfMeter.UiDay, \"ui/day\", "), "the refresh of the window");

            // the line of the month is a debug line, and a warning when the budget is broken
            Assert.True(Regex.IsMatch(code, @"private static void LogPerf\(string line, bool overBudget\)\s*\{\s*if \(overBudget\) ModLog\.Warn\(FeatureId, line\);\s*else ModLog\.Debug\(FeatureId, line\);"));

            // the meter itself is pure
            string meter = SourceScan.Strip(SourceScan.ReadText(Path.Combine(GpuFiles.FeatureSourceDir, "Logic", "Services", "GpuPerfMeter.cs")), true);
            Assert.False(Regex.IsMatch(meter, @"\bStopwatch\b|\bModLog\b|\bModSettings\b|\bUnityEngine\b"), "the clock, the flag and the log are handed in");
        }
    }
}
