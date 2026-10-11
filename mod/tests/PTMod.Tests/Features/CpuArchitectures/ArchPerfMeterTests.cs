using System;
using System.Collections.Generic;
using PTMod.Features.CpuArchitectures;

namespace PTMod.Tests.Features.CpuArchitectures
{
    /// <summary>
    /// The timer of the feature (TSK-030, NFR-1): the budgets of the spec (0.1 ms for a day of research and arbiter, 3 ms for the month, 0.2 ms a day for the rating, 5 µs a call of
    /// the cost of a project, 0.05 ms a frame for the designer), samples per call and per bucket (a day, a frame), one line a month with the verdict, the totals of the session, and no
    /// cost at all while the debug flag is off. The clock is made up: one tick is one microsecond.
    /// </summary>
    [Covers(typeof(ArchPerfMeter))]
    public class ArchPerfMeterTests
    {
        private sealed class Fixture
        {
            public long Now;
            public bool Enabled = true;
            public readonly List<string> Lines = new List<string>();
            public readonly List<bool> Over = new List<bool>();
            public ArchPerfMeter Meter;

            public Fixture()
            {
                Meter = new ArchPerfMeter(() => Enabled, () => Now, 1000000, (line, over) =>
                {
                    Lines.Add(line);
                    Over.Add(over);
                });
            }
        }

        private static readonly DateTime Jan = new DateTime(1990, 1, 1);
        private static readonly DateTime Feb = new DateTime(1990, 2, 1);
        private static readonly DateTime Mar = new DateTime(1990, 3, 1);

        [Test]
        public void TheBudgets_AreThoseOfNfr1()
        {
            Assert.Near(0.1, ArchPerfMeter.BudgetOf(ArchPerfMeter.Day));
            Assert.Near(3.0, ArchPerfMeter.BudgetOf(ArchPerfMeter.Month));
            Assert.Near(0.2, ArchPerfMeter.BudgetOf(ArchPerfMeter.Rating));
            Assert.Near(0.005, ArchPerfMeter.BudgetOf(ArchPerfMeter.Cost), 1e-12, "5 microseconds");
            Assert.Near(0.05, ArchPerfMeter.BudgetOf(ArchPerfMeter.Designer));
            foreach (string step in new[] { ArchPerfMeter.MonthEcosystem, ArchPerfMeter.MonthSupport, ArchPerfMeter.MonthRoyalty, ArchPerfMeter.MonthAi, "anything else" })
            {
                Assert.Near(0.0, ArchPerfMeter.BudgetOf(step), 1e-12, step + " has no budget of its own");
            }
        }

        [Test]
        public void WithTheFlagOff_NothingIsMeasured_NothingIsWritten()
        {
            var f = new Fixture { Enabled = false };
            Assert.Equal(-1L, f.Meter.Begin());
            f.Now = 500;
            f.Meter.End(ArchPerfMeter.Day, -1, Jan);
            f.Meter.Flush();
            f.Meter.FlushSession();
            Assert.Equal(0, f.Meter.TotalSamples);
            Assert.Equal(0, f.Lines.Count);
            Assert.False(new ArchPerfMeter(null, () => 0, 1000, null).Enabled, "no flag at all is off");
            Assert.False(new ArchPerfMeter(() => { throw new InvalidOperationException(); }, () => 0, 1000, null).Enabled, "a flag that throws is off");
        }

        [Test]
        public void ACallIsMeasuredFromBeginToEnd_AndEveryCallOfAProbeWithoutABucketIsASample()
        {
            var f = new Fixture();
            for (int i = 0; i < 3; i++)
            {
                f.Now = 1000 * i;
                long started = f.Meter.Begin();
                Assert.Equal(f.Now, started);
                f.Now += 3;   // 3 microseconds
                f.Meter.End(ArchPerfMeter.Cost, started, Jan);
            }
            Assert.Equal(3, f.Meter.TotalSamples);
            Assert.Equal(3, f.Meter.SessionCount(ArchPerfMeter.Cost));
            Assert.Near(0.003, f.Meter.SessionAverageMs(ArchPerfMeter.Cost), 1e-9);

            f.Meter.Flush();
            Assert.Equal(1, f.Lines.Count);
            Assert.Equal("perf 1990-01: Cost n=3 avg=0.0030 ms max=0.0030 ms (budget 0.005 ms: ok)", f.Lines[0]);
            Assert.False(f.Over[0]);
        }

        [Test]
        public void AnAverageOverTheBudget_IsMarkedOver_AndOneSlowCallIsNot()
        {
            var f = new Fixture();
            f.Meter.Add(ArchPerfMeter.Cost, 8, Jan);
            f.Meter.Add(ArchPerfMeter.Cost, 6, Jan);
            f.Meter.Flush();
            StringAssertContains(f.Lines[0], "avg=0.0070 ms");
            StringAssertContains(f.Lines[0], "(budget 0.005 ms: OVER)");
            Assert.True(f.Over[0], "the line is written as a warning");

            // One slow call among fast ones shows in max and does not break the budget: NFR-1 speaks of the average.
            var g = new Fixture();
            for (int i = 0; i < 99; i++) g.Meter.Add(ArchPerfMeter.Cost, 1, Jan);
            g.Meter.Add(ArchPerfMeter.Cost, 300, Jan);
            g.Meter.Flush();
            StringAssertContains(g.Lines[0], "max=0.3000 ms");
            StringAssertContains(g.Lines[0], "budget 0.005 ms: ok");
            Assert.False(g.Over[0]);
        }

        [Test]
        public void ADayOrAFrame_AddsItsCallsUp_AndIsOneSample()
        {
            var f = new Fixture();
            // Day 100: two calls of the research and the arbiter, 20 and 30 microseconds; day 101: 10.
            f.Meter.Add(ArchPerfMeter.Day, 20, Jan, 100);
            f.Meter.Add(ArchPerfMeter.Day, 30, Jan, 100);
            f.Meter.Add(ArchPerfMeter.Day, 10, Jan, 101);
            Assert.Equal(1, f.Meter.SessionCount(ArchPerfMeter.Day), "day 101 is not over yet");

            f.Meter.Flush();   // takes the sum of the day that was running
            Assert.Equal(2, f.Meter.SessionCount(ArchPerfMeter.Day));
            Assert.Near(0.030, f.Meter.SessionAverageMs(ArchPerfMeter.Day), 1e-9, "the samples are 50 and 10 microseconds");
            Assert.Near(0.050, f.Meter.SessionMaxMs(ArchPerfMeter.Day), 1e-9);
            StringAssertContains(f.Lines[0], "Day n=2 avg=0.0300 ms max=0.0500 ms (budget 0.1 ms: ok)");

            // A frame of the designer: the postfixes of one frame together are what the budget of 0.05 ms is about.
            var g = new Fixture();
            g.Meter.Add(ArchPerfMeter.Designer, 20, Jan, 7000);
            g.Meter.Add(ArchPerfMeter.Designer, 20, Jan, 7000);
            g.Meter.Add(ArchPerfMeter.Designer, 20, Jan, 7000);
            g.Meter.Flush();
            StringAssertContains(g.Lines[0], "Designer n=1 avg=0.0600 ms");
            StringAssertContains(g.Lines[0], "(budget 0.05 ms: OVER)");
            Assert.True(g.Over[0]);
        }

        [Test]
        public void TheFirstSampleOfANewMonth_WritesTheMonthThatEnded()
        {
            var f = new Fixture();
            f.Meter.Add(ArchPerfMeter.Month, 1500, Jan);
            Assert.Equal(0, f.Lines.Count);
            f.Meter.Add(ArchPerfMeter.Month, 2500, Feb);
            Assert.Equal(1, f.Lines.Count);
            Assert.Equal("perf 1990-01: Month n=1 avg=1.5000 ms max=1.5000 ms (budget 3 ms: ok)", f.Lines[0]);

            f.Meter.Add(ArchPerfMeter.Month, 3500, Mar);
            Assert.Equal(2, f.Lines.Count);
            StringAssertContains(f.Lines[1], "perf 1990-02: Month n=1 avg=2.5000 ms");
            Assert.False(f.Over[1]);

            f.Meter.Flush();
            Assert.Equal(3, f.Lines.Count);
            StringAssertContains(f.Lines[2], "perf 1990-03: Month n=1 avg=3.5000 ms");
            StringAssertContains(f.Lines[2], "OVER");
            Assert.True(f.Over[2]);
            Assert.Equal(3, f.Meter.LinesWritten);

            f.Meter.Flush();
            Assert.Equal(3, f.Lines.Count, "a month is written once");
        }

        [Test]
        public void ADayBucketThatStraddlesTheMonth_BelongsToTheMonthItBegan()
        {
            var f = new Fixture();
            f.Meter.Add(ArchPerfMeter.Day, 10, Jan, 31);
            f.Meter.Add(ArchPerfMeter.Day, 10, Feb, 32);   // the first sample of February writes January, with the day 31 in it
            Assert.Equal(1, f.Lines.Count);
            StringAssertContains(f.Lines[0], "perf 1990-01: Day n=1 avg=0.0100 ms");
        }

        [Test]
        public void TheStepsOfTheMonth_AreWrittenAfterTheMonth_WithoutABudget()
        {
            var f = new Fixture();
            f.Meter.Add(ArchPerfMeter.MonthAi, 400, Jan);
            f.Meter.Add(ArchPerfMeter.Month, 900, Jan);
            f.Meter.Add(ArchPerfMeter.MonthEcosystem, 300, Jan);
            f.Meter.Add(ArchPerfMeter.Rating, 100, Jan, 1);
            f.Meter.Add("Other", 10, Jan);
            f.Meter.Flush();

            string line = f.Lines[0];
            int month = line.IndexOf("Month n=", StringComparison.Ordinal);
            int ecosystem = line.IndexOf("Month.ecosystem n=", StringComparison.Ordinal);
            int ai = line.IndexOf("Month.ai n=", StringComparison.Ordinal);
            int rating = line.IndexOf("Rating n=", StringComparison.Ordinal);
            int other = line.IndexOf("Other n=", StringComparison.Ordinal);
            Assert.True(month >= 0 && ecosystem > month && ai > ecosystem && rating > ai && other > rating, "the order of the probes: " + line);
            Assert.False(line.Contains("Month.ai n=1 avg=0.4000 ms max=0.4000 ms (budget"), "a step has no budget");
            Assert.False(f.Over[0]);
        }

        [Test]
        public void TheSession_IsWrittenOnce_WithItsSpan_AndStartsAgain()
        {
            var f = new Fixture();
            f.Meter.Add(ArchPerfMeter.Cost, 2, Jan);
            f.Meter.Add(ArchPerfMeter.Cost, 4, Mar);
            f.Meter.FlushSession();

            Assert.Equal(3, f.Lines.Count, "January, March and the totals");
            StringAssertContains(f.Lines[2], "perf total 1990-01..1990-03: Cost n=2 avg=0.0030 ms max=0.0040 ms");
            Assert.Equal(0, f.Meter.SessionCount(ArchPerfMeter.Cost), "the session starts again");

            f.Meter.FlushSession();
            Assert.Equal(3, f.Lines.Count, "an empty session writes nothing");
        }

        [Test]
        public void ALogThatFails_DoesNotStopTheGame()
        {
            var meter = new ArchPerfMeter(() => true, () => 0, 1000000, (line, over) => { throw new InvalidOperationException("the log is full"); });
            meter.Add(ArchPerfMeter.Day, 5, Jan, 1);
            meter.Flush();
            Assert.Equal(1, meter.LinesWritten);
            Assert.Equal(1, meter.SessionCount(ArchPerfMeter.Day));
        }

        [Test]
        public void ANegativeElapsedTime_AndAnEmptyProbe_AreIgnoredSafely()
        {
            var f = new Fixture();
            f.Now = 100;
            f.Meter.End(ArchPerfMeter.Cost, 500, Jan);   // the clock went back
            f.Meter.Add("", 5, Jan);
            f.Meter.Add(null, 5, Jan);
            Assert.Equal(1, f.Meter.TotalSamples);
            Assert.Near(0.0, f.Meter.SessionAverageMs(ArchPerfMeter.Cost), 1e-12);
            Assert.Throws<ArgumentNullException>(() => new ArchPerfMeter(() => true, null, 1000, null));
            Assert.Throws<ArgumentOutOfRangeException>(() => new ArchPerfMeter(() => true, () => 0, 0, null));
        }

        private static void StringAssertContains(string text, string fragment)
        {
            Assert.True(text.IndexOf(fragment, StringComparison.Ordinal) >= 0, "'" + fragment + "' is not in: " + text);
        }
    }
}
