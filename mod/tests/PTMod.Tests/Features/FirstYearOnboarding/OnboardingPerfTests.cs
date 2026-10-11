using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using PTMod.Features.FirstYearOnboarding.Logic;
using PTMod.Tests.Architecture;

namespace PTMod.Tests.Features.FirstYearOnboarding
{
    /// <summary>
    /// The timing of the poll and of the game-day handler (TSK-017, NFR-1): what the meter writes (the average and the maximum of each entry once per game
    /// month), that nothing is timed while the game loads, fast-forwards or runs a month in one frame (zero samples), that a game without the flag pays for
    /// nothing, and the offline measure over two game years on the track idm against the budgets of NFR-1 (0.2 ms a poll, 0.1 ms a day). The numbers of a run are
    /// in <c>PERF.md</c>; the measure of the real game is made with the debug flag (see there).
    /// </summary>
    [Covers(typeof(PerfMeter), typeof(OnboardingController), typeof(IOnboardingHost))]
    public class OnboardingPerfTests
    {
        private const long TicksPerSecond = 1000000;   // one tick is a microsecond: 1000 ticks make a millisecond

        private sealed class Lines
        {
            public readonly List<string> Written = new List<string>();
            public readonly List<bool> Over = new List<bool>();

            public void Write(string line, bool over)
            {
                Written.Add(line);
                Over.Add(over);
            }
        }

        private static PerfMeter Meter(Lines lines, bool enabled = true)
        {
            return new PerfMeter(() => enabled, () => 0, TicksPerSecond, lines.Write);
        }

        private static readonly DateTime Jan = new DateTime(1975, 1, 10);
        private static readonly DateTime Feb = new DateTime(1975, 2, 3);

        // ---- the meter ----

        [Test]
        public void AMonth_IsWritten_WhenTheNextMonthStarts_WithTheAverageAndTheMaximumOfEachEntry()
        {
            var lines = new Lines();
            PerfMeter meter = Meter(lines);

            meter.Add(PerfMeter.Poll, 100, Jan);
            meter.Add(PerfMeter.Poll, 300, Jan.AddDays(1));
            meter.Add(PerfMeter.Poll, 200, Jan.AddDays(2));
            meter.Add(PerfMeter.OnGameDay, 50, Jan.AddDays(3));
            Assert.Equal(0, lines.Written.Count, "the month is not over");

            meter.Add(PerfMeter.Poll, 100, Feb);

            Assert.Equal(1, lines.Written.Count);
            Assert.Equal("perf 1975-01: Poll n=3 avg=0.2000 ms max=0.3000 ms (budget 0.2 ms: ok); OnGameDay n=1 avg=0.0500 ms max=0.0500 ms (budget 0.1 ms: ok)",
                lines.Written[0], "the shape of the line in Player.log");
            Assert.False(lines.Over[0]);
            Assert.Equal(4, meter.TotalSamples - 1, "the sample of February belongs to the next line");
        }

        [Test]
        public void AnAverageOverTheBudget_IsFlagged()
        {
            var lines = new Lines();
            PerfMeter meter = Meter(lines);

            meter.Add(PerfMeter.Poll, 500, Jan);   // 0.5 ms
            meter.Add(PerfMeter.OnGameDay, 20, Jan);
            meter.Flush();

            Assert.Equal(1, lines.Written.Count);
            Assert.True(lines.Over[0]);
            Assert.True(lines.Written[0].Contains("Poll n=1 avg=0.5000 ms max=0.5000 ms (budget 0.2 ms: OVER)"), lines.Written[0]);
            Assert.True(lines.Written[0].Contains("OnGameDay n=1 avg=0.0200 ms max=0.0200 ms (budget 0.1 ms: ok)"), lines.Written[0]);
        }

        [Test]
        public void ABigMaximum_WithAGoodAverage_IsNotOverTheBudget()
        {
            var lines = new Lines();
            PerfMeter meter = Meter(lines);
            for (int i = 0; i < 99; i++) meter.Add(PerfMeter.Poll, 10, Jan);
            meter.Add(PerfMeter.Poll, 5000, Jan);   // one hiccup of 5 ms
            meter.Flush();

            Assert.False(lines.Over[0], "NFR-1 is about the average");
            Assert.True(lines.Written[0].Contains("max=5.0000 ms"), lines.Written[0]);
        }

        [Test]
        public void Flush_WritesTheMonthInProgress_Once_AndNothingWhenThereIsNothing()
        {
            var lines = new Lines();
            PerfMeter meter = Meter(lines);

            meter.Flush();
            Assert.Equal(0, lines.Written.Count, "nothing to write");

            meter.Add(PerfMeter.OnGameDay, 40, Jan);
            meter.Flush();
            meter.Flush();

            Assert.Equal(1, lines.Written.Count);
            Assert.Equal(1, meter.LinesWritten);
            Assert.True(lines.Written[0].StartsWith("perf 1975-01: OnGameDay n=1", StringComparison.Ordinal), lines.Written[0]);
        }

        [Test]
        public void TwoMonths_AreTwoLines_AndAnEarlierDateStartsAnotherLine()
        {
            var lines = new Lines();
            PerfMeter meter = Meter(lines);

            meter.Add(PerfMeter.Poll, 10, Jan);
            meter.Add(PerfMeter.Poll, 10, Feb);
            meter.Add(PerfMeter.Poll, 10, Jan);   // a save of an earlier day was loaded
            meter.Flush();

            Assert.Equal(new[] { "1975-01", "1975-02", "1975-01" },
                lines.Written.Select(l => l.Substring("perf ".Length, 7)).ToArray());
        }

        [Test]
        public void Record_TakesTheTimeFromTheClock()
        {
            long now = 1000;
            var lines = new Lines();
            var meter = new PerfMeter(() => true, () => now, TicksPerSecond, lines.Write);

            long start = meter.Now();
            now += 250;
            meter.Record(PerfMeter.Poll, start, Jan);
            now -= 900;   // a clock that goes back must not make a negative sample
            meter.Record(PerfMeter.Poll, start + 1000, Jan);
            meter.Flush();

            Assert.True(lines.Written[0].Contains("Poll n=2 avg=0.1250 ms max=0.2500 ms"), lines.Written[0]);
        }

        [Test]
        public void TheFlag_IsAskedEveryTime_AndAFlagThatThrowsIsOff()
        {
            bool on = false;
            var meter = new PerfMeter(() => on, () => 0, TicksPerSecond, null);

            Assert.False(meter.Enabled);
            on = true;
            Assert.True(meter.Enabled, "the player may set the flag while the game runs");

            Assert.False(new PerfMeter(null, () => 0, TicksPerSecond, null).Enabled);
            Assert.False(new PerfMeter(() => { throw new InvalidOperationException("boom"); }, () => 0, TicksPerSecond, null).Enabled);
        }

        [Test]
        public void ALogThatThrows_DoesNotStopTheMeter()
        {
            var meter = new PerfMeter(() => true, () => 0, TicksPerSecond, (line, over) => { throw new InvalidOperationException("the log is full"); });
            meter.Add(PerfMeter.Poll, 10, Jan);

            meter.Flush();   // must not throw
            meter.Add(PerfMeter.Poll, 10, Jan);
            Assert.Equal(2, meter.TotalSamples);
        }

        [Test]
        public void TheBudgets_AreTheOnesOfNfr1()
        {
            Assert.Near(0.2, PerfMeter.BudgetOf(PerfMeter.Poll));
            Assert.Near(0.1, PerfMeter.BudgetOf(PerfMeter.OnGameDay));
            Assert.Near(0.0, PerfMeter.BudgetOf("Other"));
        }

        [Test]
        public void TheMeter_RefusesNoClockAndNoTicks()
        {
            Assert.Throws<ArgumentNullException>(() => new PerfMeter(() => true, null, TicksPerSecond, null));
            Assert.Throws<ArgumentOutOfRangeException>(() => new PerfMeter(() => true, () => 0, 0, null));
        }

        // ---- the controller: when it is timed ----

        private static CtlRig Rig(string track = "idm")
        {
            var rig = new CtlRig(ShippedContext.Load().BuildCatalog(), track);
            rig.Controller.NewGame(true, "menu");
            return rig;
        }

        [Test]
        public void WhileTheGameFastForwards_NoHandlerRuns_AndNothingIsTimed()
        {
            // AC of TSK-017: the fast-forward of a new game of 2000 gives zero records.
            CtlRig rig = Rig();
            var lines = new Lines();
            PerfMeter meter = Meter(lines);
            rig.Controller.Perf = meter;
            int calls = rig.Out.Calls;
            int evaluated = rig.World.EvaluateCalls;
            rig.World.Blocked = true;

            for (int i = 0; i < 10000; i++)
            {
                rig.Controller.Poll();
                rig.Controller.OnGameDay();
            }

            Assert.Equal(0, meter.TotalSamples, "0 records");
            Assert.Equal(0, lines.Written.Count);
            Assert.Equal(calls, rig.Out.Calls, "no mail, no hint");
            Assert.Equal(evaluated, rig.World.EvaluateCalls, "no condition was read");
        }

        [Test]
        public void InAMonthBatchRun_ThePollIsNotCalled_AndNotTimed()
        {
            CtlRig rig = Rig();
            PerfMeter meter = Meter(new Lines());
            rig.Controller.Perf = meter;
            int evaluated = rig.World.EvaluateCalls;
            rig.World.Batch = true;

            for (int i = 0; i < 100; i++) rig.Controller.Poll();

            Assert.Equal(0, meter.TotalSamples);
            Assert.Equal(evaluated, rig.World.EvaluateCalls);
        }

        [Test]
        public void WithTheFlagOff_NothingIsTimed_AndTheTutorialRunsAsBefore()
        {
            CtlRig rig = Rig();
            var lines = new Lines();
            PerfMeter meter = Meter(lines, false);
            rig.Controller.Perf = meter;

            for (int i = 0; i < 40; i++)
            {
                rig.Day();
                rig.Controller.Poll();
            }

            Assert.Equal(0, meter.TotalSamples);
            Assert.True(rig.Out.Mails.Count >= 2, "the mails came all the same (welcome and research): " + rig.Out.Mails.Count);
        }

        [Test]
        public void WithTheFlagOn_ThePollAndTheDayAreTimed_AndTheMonthIsWrittenWhenItEnds()
        {
            CtlRig rig = Rig();
            var lines = new Lines();
            rig.Controller.Perf = Meter(lines);

            for (int i = 0; i < 70; i++)   // January, February and into March
            {
                rig.Day();
                rig.Controller.Poll();
            }

            Assert.True(lines.Written.Count >= 2, "January and February are written: " + string.Join(" | ", lines.Written));
            Assert.True(lines.Written[0].StartsWith("perf 1975-01: Poll n=", StringComparison.Ordinal), lines.Written[0]);
            Assert.True(lines.Written[0].Contains("OnGameDay n="), "both entries are in the line: " + lines.Written[0]);
        }

        [Test]
        public void WhenTheTutorialEnds_TheMonthInProgressIsWritten()
        {
            // The window of 30 days runs out: the last timed entry ends the run, nothing comes after it, so the line is written then.
            StepCatalog catalog = OnbSteps.Catalog(new OnboardingConfig { TutorialWindowDays = 30 },
                OnbSteps.Info("welcome", 10), OnbSteps.Seq("research", 20, "{'type':'researchActive'}"), OnbSteps.Info("graduation", 100));
            var rig = new CtlRig(catalog);
            rig.Controller.NewGame(true, "menu");
            var lines = new Lines();
            rig.Controller.Perf = Meter(lines);

            for (int i = 0; i < 40 && rig.Controller.IsRunning; i++)
            {
                rig.Day();
                rig.Controller.Poll();
            }

            Assert.False(rig.Controller.IsRunning, "the window ran out");
            Assert.True(lines.Written.Count >= 1, "the last month was written");
            Assert.True(lines.Written[lines.Written.Count - 1].StartsWith("perf 1975-02: ", StringComparison.Ordinal), "the month the run ended in: " + lines.Written[lines.Written.Count - 1]);
        }

        // ---- two game years on the track idm ----

        /// <summary>The overall averages of a run, summed up from the lines the meter wrote (each line carries the count and the average of its month).</summary>
        private sealed class Totals
        {
            private static readonly Regex Entry = new Regex(@"(Poll|OnGameDay) n=(\d+) avg=([0-9.]+) ms max=([0-9.]+) ms", RegexOptions.Compiled);

            public readonly Dictionary<string, int> Count = new Dictionary<string, int>();
            public readonly Dictionary<string, double> Sum = new Dictionary<string, double>();
            public readonly Dictionary<string, double> Max = new Dictionary<string, double>();
            public int Months;

            public void Add(string line)
            {
                Months++;
                foreach (Match match in Entry.Matches(line))
                {
                    string probe = match.Groups[1].Value;
                    int n = int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture);
                    double avg = double.Parse(match.Groups[3].Value, CultureInfo.InvariantCulture);
                    double max = double.Parse(match.Groups[4].Value, CultureInfo.InvariantCulture);

                    Count[probe] = (Count.ContainsKey(probe) ? Count[probe] : 0) + n;
                    Sum[probe] = (Sum.ContainsKey(probe) ? Sum[probe] : 0) + avg * n;
                    Max[probe] = Math.Max(Max.ContainsKey(probe) ? Max[probe] : 0, max);
                }
            }

            public double Average(string probe)
            {
                return Count.ContainsKey(probe) && Count[probe] > 0 ? Sum[probe] / Count[probe] : 0;
            }

            public override string ToString()
            {
                return string.Join("; ", new[] { PerfMeter.Poll, PerfMeter.OnGameDay }.Select(p =>
                    p + " n=" + (Count.ContainsKey(p) ? Count[p] : 0) + " avg=" + Average(p).ToString("0.0000", CultureInfo.InvariantCulture) + " ms max="
                    + (Max.ContainsKey(p) ? Max[p] : 0).ToString("0.0000", CultureInfo.InvariantCulture) + " ms"));
            }
        }

        /// <summary>
        /// Two game years of the tutorial on the shipped steps with the real clock: every game day one <c>OnGameDay</c> and <paramref name="pollsPerDay"/> polls. The player
        /// <paramref name="plays"/> (a script that moves the fake game on); the line of every game month goes to <paramref name="totals"/>.
        /// </summary>
        private static CtlRig TwoYears(string track, int pollsPerDay, Action<CtlRig, int> plays, Totals totals)
        {
            CtlRig rig = Rig(track);
            rig.Controller.Perf = new PerfMeter(() => true, Stopwatch.GetTimestamp, Stopwatch.Frequency, (line, over) => totals.Add(line));

            for (int day = 1; day <= 735; day++)   // past the window of 730 days: the last month is written by the end of the run
            {
                rig.Day();
                if (plays != null) plays(rig, day);
                for (int poll = 0; poll < pollsPerDay; poll++) rig.Controller.Poll();
            }
            rig.Controller.Perf.Flush();
            return rig;
        }

        private const int Runs = 3;

        /// <summary>
        /// The run is repeated and the best average of each entry counts: the budget is about what the code costs, and one run on a machine that is busy with
        /// something else (a collection of garbage, another build) can be slower by an order of magnitude. A warm-up run comes first: the first calls compile the
        /// code and fill the caches, which is not what the budget is about.
        /// </summary>
        private static List<Totals> Measure(string track, Action<CtlRig, int> plays, out CtlRig last)
        {
            TwoYears(track, 4, plays, new Totals());

            var runs = new List<Totals>();
            last = null;
            for (int i = 0; i < Runs; i++)
            {
                var totals = new Totals();
                last = TwoYears(track, 4, plays, totals);
                runs.Add(totals);
            }
            return runs;
        }

        private static string Describe(List<Totals> runs)
        {
            return string.Join(" || ", runs.Select(t => t.ToString()));
        }

        [Test]
        public void TwoGameYearsOnTheIdmTrack_AnIdlePlayer_StaysWithinTheBudgets()
        {
            // The worst case of NFR-1: the tutorial is running the whole window, so every poll reads the current step and the tips for 730 days.
            CtlRig rig;
            List<Totals> runs = Measure("idm", null, out rig);

            Console.WriteLine("  perf  idle player, idm, 735 days x 4 polls, " + Runs + " runs: " + Describe(runs));
            Assert.True(runs.All(t => t.Count[PerfMeter.Poll] > 2500), "the polls were timed: " + Describe(runs));
            Assert.True(runs.All(t => t.Count[PerfMeter.OnGameDay] > 700), "the days were timed: " + Describe(runs));
            Assert.True(runs.Min(t => t.Average(PerfMeter.Poll)) <= PerfMeter.PollBudgetMs, "NFR-1: a poll takes at most 0.2 ms on average: " + Describe(runs));
            Assert.True(runs.Min(t => t.Average(PerfMeter.OnGameDay)) <= PerfMeter.DayBudgetMs, "NFR-1: a game day takes at most 0.1 ms on average: " + Describe(runs));
            Assert.True(runs.All(t => t.Months >= 23), "a line for every month of the window: " + Describe(runs));
            Assert.Equal(OnboardingMode.Completed, rig.State.Mode, "the window of 730 days ended the tutorial with the summary");
        }

        [Test]
        public void TwoGameYearsOnTheIdmTrack_APlayerWhoPlays_StaysWithinTheBudgets()
        {
            Action<CtlRig, int> plays = (rig, day) =>
            {
                if (day == 3) rig.Player.ResearchActive = true;
                if (day == 5) rig.World.Facts.CpuProject = true;
                if (day == 20) { rig.World.Facts.CpuProject = false; rig.World.State.Player.CpuCount = 1; }
                if (day == 25) rig.World.Facts.Produced = true;
                if (day == 30) rig.World.Facts.Sold = true;
                if (day == 60) rig.World.Facts.Oem = true;
                if (day == 70) rig.World.State.Player.OemOfferSent = true;
                if (day == 200) rig.World.State.Player.Money = -50000;   // a debt tip
            };
            CtlRig rig;
            List<Totals> runs = Measure("idm", plays, out rig);

            Console.WriteLine("  perf  player who plays, idm, 735 days x 4 polls, " + Runs + " runs: " + Describe(runs));
            Assert.True(runs.Min(t => t.Average(PerfMeter.Poll)) <= PerfMeter.PollBudgetMs, "NFR-1: " + Describe(runs));
            Assert.True(runs.Min(t => t.Average(PerfMeter.OnGameDay)) <= PerfMeter.DayBudgetMs, "NFR-1: " + Describe(runs));
            Assert.True(rig.Out.Mails.Count >= 10, "the tutorial went through the track: " + rig.Out.Mails.Count + " mails");
        }

        [Test]
        public void TheFastForwardOfANewGameOf2000_IsSilentOnEveryTrack()
        {
            foreach (string track in new[] { "idm", "fabless", "foundry" })
            {
                CtlRig rig = Rig(track);
                var totals = new Totals();
                rig.Controller.Perf = new PerfMeter(() => true, Stopwatch.GetTimestamp, Stopwatch.Frequency, (line, over) => totals.Add(line));
                rig.World.Blocked = true;
                int calls = rig.Out.Calls;

                for (int day = 0; day < 10000; day++)
                {
                    rig.Controller.Poll();
                    rig.Controller.OnGameDay();
                }
                rig.Controller.Perf.Flush();

                Assert.Equal(0, rig.Controller.Perf.TotalSamples, track + ": 0 records");
                Assert.Equal(0, totals.Months, track);
                Assert.Equal(calls, rig.Out.Calls, track + ": nothing was sent");
            }
        }

        // ---- the director ----

        [Test]
        public void TheDirector_TimesThroughTheController_BehindTheDebugFlagOfTheFeature()
        {
            string folder = Path.Combine(SourceScan.SrcDirOrFail(), "Features", "FirstYearOnboarding");
            string code = SourceScan.Strip(SourceScan.ReadText(Path.Combine(folder, "OnboardingDirector.cs")), false);

            Assert.True(Regex.IsMatch(code, @"PerfFlag\s*=\s*""first-year-onboarding\.perf"""), "the debug flag of the feature");
            Assert.True(Regex.IsMatch(code, @"new\s+PerfMeter\s*\(\s*\(\)\s*=>\s*ModSettings\s*\.\s*Debug\s*\(\s*PerfFlag\s*\)"), "the flag is ModSettings.Debug");
            Assert.True(Regex.IsMatch(code, @"Stopwatch\s*\.\s*GetTimestamp") && Regex.IsMatch(code, @"Stopwatch\s*\.\s*Frequency"), "the real clock");
            Assert.True(Regex.IsMatch(code, @"Perf\s*=\s*_perf"), "the controller gets the meter");
            Assert.True(Regex.IsMatch(code, @"ModLog\s*\.\s*Warn\s*\(\s*OnboardingFeature\s*\.\s*FeatureId\s*,\s*line\s*\)") && Regex.IsMatch(code, @"ModLog\s*\.\s*Info\s*\(\s*OnboardingFeature\s*\.\s*FeatureId\s*,\s*line\s*\)"),
                "a month goes to Player.log: INFO, WARN when over the budget");
            Assert.True(Regex.IsMatch(code, @"_perf\s*\.\s*Flush\s*\(\s*\)"), "the month in progress is written when the feature stops");
        }

        [Test]
        public void ThePerfDocument_ExistsAndSaysHowToSwitchTheFlagOn()
        {
            string path = Path.Combine(SourceScan.SrcDirOrFail(), "..", "..", "tests", "PTMod.Tests", "Features", "FirstYearOnboarding", "PERF.md");
            string text = SourceScan.ReadText(path);

            Assert.True(text.Length > 0, "PERF.md is next to the tests of the feature: " + path);
            Assert.True(text.Contains("first-year-onboarding.perf"), "the flag");
            Assert.True(text.Contains("ptmod_settings.json"), "where to set it");
            Assert.True(text.Contains("0,2 мс") && text.Contains("0,1 мс"), "the budgets of NFR-1");
        }
    }
}
