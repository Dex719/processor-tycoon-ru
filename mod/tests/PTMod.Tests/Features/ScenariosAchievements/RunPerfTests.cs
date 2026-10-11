using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using PTMod.Features.ScenariosAchievements.Logic;
using PTMod.Tests.Architecture;
using static PTMod.Tests.Features.ScenariosAchievements.RunTestData;

namespace PTMod.Tests.Features.ScenariosAchievements
{
    /// <summary>
    /// TSK-030 (NFR-1): the timing of the passes of the run. What the meter writes (the average and the maximum of each entry once per game month and once for the session), that a game
    /// without the debug flag pays for nothing, that the entries of a fast-forward are counted and must not work; the wiring of <c>RunDirector</c> (read as text); and the offline
    /// measure: three game years of the real engine - a scenario with the 37 achievements and challenges, and free play - over a market of 400 CPUs, against the budgets of NFR-1
    /// (0.5 ms a day, 2 ms a month, 50 ms the table). The numbers of a run are in <c>PERF.md</c>; the measure of the real game is made with the debug flag (see there).
    /// </summary>
    [Covers(typeof(RunPerfMeter))]
    public class RunPerfTests
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

        private static RunPerfMeter Meter(Lines lines, bool enabled = true)
        {
            return new RunPerfMeter(() => enabled, () => 0, TicksPerSecond, lines.Write);
        }

        private static readonly DateTime Jan = new DateTime(2010, 1, 10);
        private static readonly DateTime Feb = new DateTime(2010, 2, 3);

        // ---- the meter ----

        [Test]
        public void AMonth_IsWritten_WhenTheNextMonthStarts_WithTheAverageAndTheMaximumOfEachEntry()
        {
            var lines = new Lines();
            RunPerfMeter meter = Meter(lines);

            meter.Add(RunPerfMeter.Day, 100, Jan);
            meter.Add(RunPerfMeter.Day, 300, Jan.AddDays(1));
            meter.Add(RunPerfMeter.Day, 200, Jan.AddDays(2));
            meter.Add(RunPerfMeter.Month, 1500, Jan.AddDays(3));
            Assert.Equal(0, lines.Written.Count, "the month is not over");

            meter.Add(RunPerfMeter.Day, 100, Feb);

            Assert.Equal(1, lines.Written.Count);
            Assert.Equal("perf 2010-01: Day n=3 avg=0.2000 ms max=0.3000 ms (budget 0.5 ms: ok); Month n=1 avg=1.5000 ms max=1.5000 ms (budget 2.0 ms: ok)",
                lines.Written[0], "the shape of the line in Player.log");
            Assert.False(lines.Over[0]);
            Assert.Equal(5, meter.TotalSamples);
        }

        [Test]
        public void AnAverageOverItsBudget_IsFlagged_ForEveryEntry()
        {
            var lines = new Lines();
            RunPerfMeter meter = Meter(lines);

            meter.Add(RunPerfMeter.Day, 800, Jan);                    // 0.8 ms against 0.5
            meter.Add(RunPerfMeter.Month, 3000, Jan);                 // 3 ms against 2
            meter.Add(RunPerfMeter.Comparison, 60000, Jan);           // 60 ms against 50
            meter.Flush();

            Assert.Equal(1, lines.Written.Count);
            Assert.True(lines.Over[0]);
            Assert.True(lines.Written[0].Contains("Day n=1 avg=0.8000 ms max=0.8000 ms (budget 0.5 ms: OVER)"), lines.Written[0]);
            Assert.True(lines.Written[0].Contains("Month n=1 avg=3.0000 ms max=3.0000 ms (budget 2.0 ms: OVER)"), lines.Written[0]);
            Assert.True(lines.Written[0].Contains("Comparison n=1 avg=60.0000 ms max=60.0000 ms (budget 50.0 ms: OVER)"), lines.Written[0]);
        }

        [Test]
        public void OneSlowCall_ShowsInTheMaximum_AndDoesNotBreakTheBudgetOfTheAverage()
        {
            var lines = new Lines();
            RunPerfMeter meter = Meter(lines);

            for (int i = 0; i < 29; i++) meter.Add(RunPerfMeter.Day, 100, Jan.AddDays(i % 20));   // 0.1 ms
            meter.Add(RunPerfMeter.Day, 9000, Jan.AddDays(5));                                      // a pause of the garbage collector: 9 ms
            meter.Flush();

            Assert.False(lines.Over[0], lines.Written[0]);
            Assert.True(lines.Written[0].Contains("Day n=30 avg=0.3967 ms max=9.0000 ms (budget 0.5 ms: ok)"), lines.Written[0]);
        }

        [Test]
        public void TheEntries_AreWrittenInTheOrderOfTheBudgets_AndAnUnknownOneAfterThem()
        {
            var lines = new Lines();
            RunPerfMeter meter = Meter(lines);

            meter.Add("Zeta", 10, Jan);
            meter.Add(RunPerfMeter.Comparison, 10, Jan);
            meter.Add(RunPerfMeter.Day, 10, Jan);
            meter.Add(RunPerfMeter.Month, 10, Jan);
            meter.Flush();

            string line = lines.Written[0];
            Assert.True(line.IndexOf("Day ", StringComparison.Ordinal) < line.IndexOf("Month ", StringComparison.Ordinal));
            Assert.True(line.IndexOf("Month ", StringComparison.Ordinal) < line.IndexOf("Comparison ", StringComparison.Ordinal));
            Assert.True(line.IndexOf("Comparison ", StringComparison.Ordinal) < line.IndexOf("Zeta ", StringComparison.Ordinal));
            Assert.True(line.Contains("Zeta n=1 avg=0.0100 ms max=0.0100 ms") && !line.Contains("Zeta n=1 avg=0.0100 ms max=0.0100 ms (budget"), "no budget for an entry without one");
            Assert.Equal(0.0, RunPerfMeter.BudgetOf("Zeta"));
            Assert.Equal(0.5, RunPerfMeter.BudgetOf(RunPerfMeter.Day));
            Assert.Equal(2.0, RunPerfMeter.BudgetOf(RunPerfMeter.Month));
            Assert.Equal(50.0, RunPerfMeter.BudgetOf(RunPerfMeter.Comparison));
        }

        [Test]
        public void WithoutTheFlag_NothingIsTimed_AndNothingIsWritten()
        {
            var lines = new Lines();
            RunPerfMeter meter = Meter(lines, enabled: false);

            Assert.False(meter.Enabled);
            long started = meter.Begin();
            Assert.Equal(-1L, started, "a pass without the flag has no start");
            meter.End(RunPerfMeter.Day, started, Jan);
            meter.Flush();
            meter.FlushSession();
            meter.ReportFastForward();

            Assert.Equal(0, meter.TotalSamples);
            Assert.Equal(0, lines.Written.Count);
        }

        [Test]
        public void TheClock_IsReadOnlyWhileTheFlagIsOn()
        {
            long now = 5000;
            var lines = new Lines();
            var meter = new RunPerfMeter(() => true, () => now, TicksPerSecond, lines.Write);

            long started = meter.Begin();
            Assert.Equal(5000L, started);
            now = 5400;
            meter.End(RunPerfMeter.Day, started, Jan);
            now = 2000;
            meter.End(RunPerfMeter.Day, started, Jan);   // a clock that went back is a sample of zero, not a negative one
            meter.Flush();

            Assert.True(lines.Written[0].Contains("Day n=2 avg=0.2000 ms max=0.4000 ms"), lines.Written[0]);
        }

        [Test]
        public void AFlagThatThrows_IsOff()
        {
            var meter = new RunPerfMeter(() => { throw new InvalidOperationException("the settings are gone"); }, () => 0, TicksPerSecond, null);
            Assert.False(meter.Enabled);
            Assert.Equal(-1L, meter.Begin());
            Assert.False(new RunPerfMeter(null, () => 0, TicksPerSecond, null).Enabled, "no flag is never");
        }

        [Test]
        public void AWriterThatFails_DoesNotStopTheMeter()
        {
            int calls = 0;
            var meter = new RunPerfMeter(() => true, () => 0, TicksPerSecond, (line, over) =>
            {
                calls++;
                throw new InvalidOperationException("the log is gone");
            });

            meter.Add(RunPerfMeter.Day, 100, Jan);
            meter.Add(RunPerfMeter.Day, 100, Feb);   // writes January and throws inside the writer
            meter.Add(RunPerfMeter.Day, 100, Feb);
            meter.Flush();

            Assert.Equal(2, calls);
            Assert.Equal(3, meter.TotalSamples);
        }

        [Test]
        public void TheConstructor_RefusesAMissingClockAndAZeroRate()
        {
            Assert.Throws<ArgumentNullException>(() => new RunPerfMeter(() => true, null, TicksPerSecond, null));
            Assert.Throws<ArgumentOutOfRangeException>(() => new RunPerfMeter(() => true, () => 0, 0, null));
        }

        // ---- the session ----

        [Test]
        public void TheSession_IsWrittenOnce_WithTheMonthsItSpans_AndStartsAgain()
        {
            var lines = new Lines();
            RunPerfMeter meter = Meter(lines);

            meter.Add(RunPerfMeter.Day, 100, new DateTime(2010, 1, 5));
            meter.Add(RunPerfMeter.Day, 300, new DateTime(2010, 1, 6));
            meter.Add(RunPerfMeter.Month, 1000, new DateTime(2010, 2, 1));
            meter.Add(RunPerfMeter.Day, 200, new DateTime(2010, 3, 9));
            Assert.Equal(2, lines.Written.Count, "January and February were written when the next month began");

            Assert.Equal(3, meter.SessionCount(RunPerfMeter.Day));
            Assert.Near(0.2, meter.SessionAverageMs(RunPerfMeter.Day));
            Assert.Near(0.3, meter.SessionMaxMs(RunPerfMeter.Day));
            Assert.Near(1.0, meter.SessionAverageMs(RunPerfMeter.Month));

            meter.FlushSession();
            Assert.Equal(4, lines.Written.Count, "March, then the whole session");
            Assert.Equal("perf 2010-03: Day n=1 avg=0.2000 ms max=0.2000 ms (budget 0.5 ms: ok)", lines.Written[2]);
            Assert.Equal("perf total 2010-01..2010-03: Day n=3 avg=0.2000 ms max=0.3000 ms (budget 0.5 ms: ok); Month n=1 avg=1.0000 ms max=1.0000 ms (budget 2.0 ms: ok); fast-forward entries=0, passes=0",
                lines.Written[3]);
            Assert.False(lines.Over[3]);

            Assert.Equal(0, meter.SessionCount(RunPerfMeter.Day), "the session starts again");
            meter.FlushSession();
            Assert.Equal(4, lines.Written.Count, "an empty session writes nothing");
        }

        [Test]
        public void ASessionInOneMonth_NamesThatMonthOnly()
        {
            var lines = new Lines();
            RunPerfMeter meter = Meter(lines);
            meter.Add(RunPerfMeter.Comparison, 4000, Jan);
            meter.FlushSession();

            Assert.Equal(2, lines.Written.Count);
            Assert.True(lines.Written[1].StartsWith("perf total 2010-01: Comparison n=1 avg=4.0000 ms max=4.0000 ms (budget 50.0 ms: ok)", StringComparison.Ordinal), lines.Written[1]);
        }

        [Test]
        public void TheAccessors_AnswerZeroForAnEntryWithoutSamples()
        {
            RunPerfMeter meter = Meter(new Lines());
            Assert.Equal(0, meter.SessionCount(RunPerfMeter.Day));
            Assert.Equal(0.0, meter.SessionAverageMs(RunPerfMeter.Day));
            Assert.Equal(0.0, meter.SessionMaxMs(RunPerfMeter.Day));
            Assert.Equal(0, meter.SessionCount(null));
            meter.Add(null, 100, Jan);
            meter.Add(string.Empty, 100, Jan);
            Assert.Equal(0, meter.TotalSamples, "an entry without a name is not a sample");
        }

        // ---- the fast-forward of a new game ----

        [Test]
        public void TheFastForward_IsReportedWithItsEntries_AndOnlyAPassThatWorkedIsOver()
        {
            var lines = new Lines();
            RunPerfMeter meter = Meter(lines);

            meter.NoteFastForward(false);
            meter.NoteFastForward(false);
            Assert.Equal(2, meter.FastForwardEntries);
            Assert.Equal(0, meter.FastForwardWork);
            meter.ReportFastForward();

            Assert.Equal("perf fast-forward: entries=2, passes=0 (budget 0: ok)", lines.Written[0]);
            Assert.False(lines.Over[0]);
            Assert.Equal(0, meter.FastForwardEntries, "counted again for the next new game");

            meter.NoteFastForward(true);
            meter.ReportFastForward();
            Assert.Equal("perf fast-forward: entries=1, passes=1 (budget 0: OVER)", lines.Written[1]);
            Assert.True(lines.Over[1]);
        }

        [Test]
        public void TheSessionLine_CarriesTheFastForward_AndCountsAPassThatWorkedAsOver()
        {
            var lines = new Lines();
            RunPerfMeter meter = Meter(lines);
            meter.NoteFastForward(true);
            meter.FlushSession();

            Assert.Equal(1, lines.Written.Count, "a session with no sample but with an entry during the fast-forward still reports it");
            Assert.Equal("perf total -: fast-forward entries=1, passes=1", lines.Written[0]);
            Assert.True(lines.Over[0]);
        }

        [Test]
        public void TheFastForwardReport_NeedsTheFlag_AndStillStartsTheCountersAgain()
        {
            var lines = new Lines();
            RunPerfMeter meter = Meter(lines, enabled: false);
            meter.NoteFastForward(false);
            meter.ReportFastForward();

            Assert.Equal(0, lines.Written.Count);
            Assert.Equal(0, meter.FastForwardEntries);
        }

        // ---- the wiring of the director, read as text ----

        private static string Source(params string[] path)
        {
            string file = Path.Combine(new[] { SourceScan.SrcDirOrFail(), "Features", "ScenariosAchievements" }.Concat(path).ToArray());
            Assert.True(File.Exists(file), "no source file: " + file);
            return SourceScan.Strip(SourceScan.ReadText(file), false);
        }

        private static string Between(string text, string from, string to)
        {
            int start = text.IndexOf(from, StringComparison.Ordinal);
            Assert.True(start >= 0, "no '" + from + "' in the source");
            int end = text.IndexOf(to, start + from.Length, StringComparison.Ordinal);
            return end < 0 ? text.Substring(start) : text.Substring(start, end - start);
        }

        [Test]
        public void TheDirector_TimesWithTheRealClock_BehindTheDebugFlag()
        {
            string code = Source("Integration", "RunDirector.cs");
            Assert.True(code.Contains("internal const string PerfFlag = \"scenarios-achievements.perf\";"), "the flag is the id of the feature and '.perf'");
            Assert.True(Regex.IsMatch(code, @"new RunPerfMeter\(\(\) => ModSettings\.Debug\(PerfFlag\), Stopwatch\.GetTimestamp, Stopwatch\.Frequency, WritePerf\)"), "the flag, the real clock, the log");
            string write = Between(code, "private static void WritePerf(", "private static LiveRun Live()");
            Assert.True(write.Contains("ModLog.Warn(") && write.Contains("ModLog.Info("), "a line over the budget is a warning, the others are information");
        }

        [Test]
        public void TheDayThePassAndTheTable_AreEachTimed_AndThePublishingIsNotPartOfIt()
        {
            string code = Source("Integration", "RunDirector.cs");

            string day = Between(code, "private void OnDayPassed()", "private void OnLateMonthPassed()");
            AssertTimedBeforePublish(day, "engine.OnDay(_sampleDay, Live())", "RunPerfMeter.Day");

            string month = Between(code, "private void OnLateMonthPassed()", "private void OnYearPassed()");
            AssertTimedBeforePublish(month, "engine.OnMonth(_sampleMonth, Live())", "RunPerfMeter.Month");

            string table = Between(code, "internal ComparisonRow[] BuildComparison(", "internal double RivalBestFrequency()");
            int begin = table.IndexOf("_perf.Begin()", StringComparison.Ordinal);
            int build = table.IndexOf("_source.BuildComparison(maxAi)", StringComparison.Ordinal);
            int end = table.IndexOf("_perf.End(RunPerfMeter.Comparison,", StringComparison.Ordinal);
            Assert.True(begin >= 0 && build > begin && end > build, "the table is built between the start and the end of the timing");
        }

        private static void AssertTimedBeforePublish(string entry, string pass, string probe)
        {
            int begin = entry.IndexOf("_perf.Begin()", StringComparison.Ordinal);
            int run = entry.IndexOf(pass, StringComparison.Ordinal);
            int end = entry.IndexOf("_perf.End(" + probe + ",", StringComparison.Ordinal);
            int publish = entry.IndexOf("Publish(result)", StringComparison.Ordinal);
            Assert.True(begin >= 0 && run > begin && end > run && publish > end, probe + ": the clock starts, the pass runs, the clock stops, and only then the windows are told");
        }

        [Test]
        public void TheSession_IsWrittenWhenTheSceneIsLeft_AndWhenTheFeatureIsSwitchedOff()
        {
            string code = Source("Integration", "RunDirector.cs");
            string leaving = Between(code, "private void OnSceneLeaving()", "private void OnFeatureDisabled()");
            Assert.True(leaving.Contains("_perf.FlushSession()"));
            Assert.True(code.Contains("ScenariosFeature.Disabled += director.OnFeatureDisabled;"));
            string disabled = Between(code, "private void OnFeatureDisabled()", "private void OnRunReplaced()");
            Assert.True(disabled.Contains("_perf.FlushSession()"));
        }

        [Test]
        public void TheNewGamesFastForward_IsReportedOnGameReady_BeforeTheRunStarts()
        {
            string code = Source("Integration", "RunDirector.cs");
            string ready = Between(code, "private void OnGameReady(", "internal void OnFeatureSwitchedOn()");
            int report = ready.IndexOf("_perf.ReportFastForward()", StringComparison.Ordinal);
            int start = ready.IndexOf("StartNewRun();", StringComparison.Ordinal);
            Assert.True(report >= 0 && start > report, "the count of the fast-forward is written before the first pass of the run");
            Assert.True(Regex.IsMatch(ready, @"if \(reason == GameReadyReason\.NewGame\) _perf\.ReportFastForward\(\);"), "for a new game only");
        }

        [Test]
        public void TheEntries_AreCountedWhileTheGameFastForwards()
        {
            string code = Source("Integration", "RunDirector.cs");
            string ready = Between(code, "private bool Ready(out RunEngine engine)", "private static void WritePerf(");
            Assert.True(ready.Contains("ModTicks.IsFastForwarding"));
            Assert.Equal(2, Regex.Matches(ready, @"_perf\.NoteFastForward\(").Count, "an entry that left at once and one that went on to work");
            Assert.True(ready.Contains("NoteFastForward(false)") && ready.Contains("NoteFastForward(engine != null)"));
        }

        [Test]
        public void TheNewGamesFastForward_ReachesNoPassOfTheRun_OnAnyPathOfTheGame()
        {
            // 1. the ticks: the core does not call a handler of a feature during the fast-forward unless it asks (TickOptions.RunDuringFastForward); nothing in the feature asks
            string root = Path.Combine(SourceScan.SrcDirOrFail(), "Features", "ScenariosAchievements");
            foreach (string path in SourceScan.Files(root, ".cs"))
            {
                string code = SourceScan.Strip(SourceScan.ReadText(path), false);
                Assert.False(Regex.IsMatch(code, @"\bRunDuringFastForward\b|\bRunDuringLoading\b|\bTickOptions\b"), SourceScan.Relative(root, path) + " asks the core to run a handler during the fast-forward");
            }

            // 2. the events of the core and of the game: the first line of each handler leaves
            string hooks = Source("Integration", "GameEventHooks.cs");
            Assert.True(Regex.IsMatch(hooks, @"private static void OnCpuReleased\([^)]*\)\s*\{\s*if \(duringFastForward\) return;"), "the release of a CPU");
            Assert.True(Regex.IsMatch(hooks, @"private static void OnCpuTransferred\([^)]*\)\s*\{\s*if \(ModTicks\.IsFastForwarding\) return;"), "the transfer of a CPU");
            Assert.True(Regex.IsMatch(hooks, @"private static void OnCompanyBankrupt\([^)]*\)\s*\{\s*if \(ModTicks\.IsLoading \|\| ModTicks\.IsFastForwarding\) return;"), "a bankruptcy");
            Assert.True(Regex.IsMatch(hooks, @"private static void OnBusinessSigned\(\)\s*\{\s*if \(ModTicks\.IsLoading \|\| ModTicks\.IsFastForwarding\) return;"), "a foundry contract");
            Assert.True(Regex.IsMatch(Source("Integration", "ScenarioPatches.cs"), @"Postfix\([^)]*\)\s*\{\s*if \(ModTicks\.IsLoading \|\| ModTicks\.IsFastForwarding\) return;"), "an OEM contract (the postfix)");

            // 3. the rest is the director's, and every entry of it but the lifecycle goes through Ready, which refuses while the game is not ready
            string director = Source("Integration", "RunDirector.cs");
            string ready = Between(director, "private bool Ready(out RunEngine engine)", "private static void WritePerf(");
            Assert.True(ready.Contains("!ModTicks.IsGameReady"), "not ready (loading, fast-forwarding) is no run");
        }

        // ---- PERF.md ----

        [Test]
        public void PerfMd_IsNextToTheTests_AndTellsHowToMeasureInTheGame()
        {
            string path = Path.GetFullPath(Path.Combine(SourceScan.SrcDirOrFail(), "..", "..", "tests", "PTMod.Tests", "Features", "ScenariosAchievements", "PERF.md"));
            string text = SourceScan.ReadText(path);
            Assert.True(text.Length > 0, "PERF.md is next to the tests of the feature: " + path);

            foreach (string part in new[]
                     {
                         "scenarios-achievements.perf", "ptmod_settings.json", "\"debug\"", "0,5 мс", "2 мс", "50 мс", "perf total", "perf fast-forward", "Замер в игре", "Офлайн-замер",
                         "RunPerfMeter", "400 CPU", "2010",
                     })
            {
                Assert.True(text.Contains(part), "PERF.md does not mention '" + part + "'");
            }
            Assert.True(text.Contains(RunDirectorFlag()), "the flag of the document is the flag of the director");
        }

        private static string RunDirectorFlag()
        {
            Match flag = Regex.Match(Source("Integration", "RunDirector.cs"), "PerfFlag = \"([^\"]+)\"");
            Assert.True(flag.Success, "PerfFlag");
            return flag.Groups[1].Value;
        }

        // ---- the offline measure ----

        private const int Cpus = 400;
        private const int PlayerCpus = 100;

        /// <summary>A market of 400 CPUs (100 of the player, 300 of the AI companies, a third of the player's retired) and the engine over it, the way the director drives it.</summary>
        private sealed class Rig
        {
            private readonly DaySampler _sampler = new DaySampler();
            private readonly DaySample _day = new DaySample();
            private readonly CpuRow[] _rows = new CpuRow[Cpus];
            private readonly RunEngine _engine;
            private readonly Func<DaySample> _sample;
            private DateTime _today;
            private int _dayIndex;

            public readonly RunState Run;
            public readonly RunPerfMeter Meter = new RunPerfMeter(() => true, Stopwatch.GetTimestamp, Stopwatch.Frequency, null);

            public Rig(string scenarioId, params string[] challengeIds)
            {
                ScenarioDefinition scenario = scenarioId == null ? null : ScenarioTestData.Scenario(scenarioId);
                DateTime start = scenario == null ? new DateTime(2010, 1, 1) : new DateTime(scenario.Start.Year, 1, 1);
                Run = RunState.StartNew("perf", start, 3, scenario, challengeIds, new[] { "event-system", "scenarios-achievements" });
                foreach (string id in challengeIds) Run.ChallengeStates.Add(id, new string[0]);

                _engine = new RunEngine(Run, challengeIds.Select(ScenarioTestData.Challenge).ToList(), new AchievementEvaluator(ScenarioTestData.StandardAchievements(), ScenarioTestData.ScenarioIds),
                    null, new Ports(), null, () => new DateTime(2026, 10, 11, 12, 0, 0, DateTimeKind.Utc), 10);
                _today = start;

                for (int i = 0; i < Cpus; i++)
                {
                    bool player = i < PlayerCpus;
                    _rows[i] = new CpuRow
                    {
                        SaveId = i + 1,
                        IsPlayerCpu = player,
                        IsReleased = true,
                        IsRetired = player ? i % 3 == 0 : i % 5 == 0,
                        Sold = player ? 10 + i % 7 : 40 + i % 11,
                        SoldHighEnd = player ? 2 : 8,
                        SoldMidRange = player ? 5 : 20,
                        SoldLowEnd = player ? 3 : 10,
                        SoldIndustries = player ? 0 : 2 + i % 11,
                        MissedSales = player ? i % 3 : 0,
                        Ips = 1000 + i * 3.5,
                        Popularity = 0.2 + (i % 10) / 20.0,
                        Name = (player ? "player-" : "rival-") + i,
                        Frequency = 3000 + i * 10,
                        Cores = 1 + i % 4,
                        Smt = i % 2 == 0,
                        Power = 10 + i % 50,
                    };
                }

                _sample = SampleDay;
            }

            public RunEngine Engine { get { return _engine; } }

            private DaySample SampleDay()
            {
                _sampler.Begin(_day, Run.Metrics.TransferredCpuIds);
                for (int i = 0; i < _rows.Length; i++) _sampler.Add(ref _rows[i]);
                _sampler.End();

                _day.Date = _today;
                _day.Money = 5000000 + _dayIndex * 1000.0;
                _day.ActiveOemContracts = 0;
                _day.FoundryClientsActive = 1;
                _day.FactoryLines = 50;
                _day.RunDays = _dayIndex;
                _day.Difficulty = 3;
                return _day;
            }

            /// <summary>Plays the days one after the other: a day pass each, a month pass on the first of the month, as the core's ticks call them.</summary>
            public void Play(int days)
            {
                DateTime first = _today;
                for (int d = 0; d < days; d++)
                {
                    DateTime date = first.AddDays(d);
                    _today = date;
                    _dayIndex = d;
                    var live = new LiveRun { Today = date, CompanyType = "idm", CompanyName = "Perf CPU", Difficulty = 3, StrictAssisted = false };

                    if (date.Day == 1 && d > 0)
                    {
                        DateTime last = date.AddMonths(-1);
                        long month = Meter.Begin();
                        _engine.OnMonth(() => Month(last.Year, last.Month, 2000000 + d, 1500000, 24000000), live);
                        Meter.End(RunPerfMeter.Month, month, date);
                    }

                    long day = Meter.Begin();
                    _engine.OnDay(_sample, live);
                    Meter.End(RunPerfMeter.Day, day, date);
                }
            }
        }

        private sealed class Ports : IRunPorts
        {
            private readonly ProfileModel _profile = new ProfileModel(new PTMod.Core.Logic.Json.JsonObject());

            public ProfileView Profile { get { return _profile.View; } }
            public bool UnlockAchievement(string achievementId, AchievementEntry entry) { return _profile.Unlock(achievementId, entry); }
            public void RecordScenarioResult(ScenarioResult result, RunInfo run) { _profile.RecordResult(result, run); }
            public void RecordChallengePassed(string challengeId, int years) { _profile.RecordChallenge(challengeId, years); }
            public void RemoveModifierEntries(IList<string> entryIds) { }
        }

        private sealed class Measure
        {
            public double DayAverage = double.MaxValue, DayMax, MonthAverage = double.MaxValue, MonthMax;
            public int Days, Months;
        }

        private const int GameDays = 1096;   // three game years, one of them a leap year
        private const int Runs = 3;

        private static Measure Best(Func<Rig> make, out Rig last)
        {
            // warm-up: the first run compiles the code and fills the caches
            make().Play(366);

            var best = new Measure();
            last = null;
            for (int run = 0; run < Runs; run++)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                Rig rig = make();
                rig.Play(GameDays);
                last = rig;

                best.Days = rig.Meter.SessionCount(RunPerfMeter.Day);
                best.Months = rig.Meter.SessionCount(RunPerfMeter.Month);
                best.DayAverage = Math.Min(best.DayAverage, rig.Meter.SessionAverageMs(RunPerfMeter.Day));
                best.DayMax = rig.Meter.SessionMaxMs(RunPerfMeter.Day);
                best.MonthAverage = Math.Min(best.MonthAverage, rig.Meter.SessionAverageMs(RunPerfMeter.Month));
                best.MonthMax = rig.Meter.SessionMaxMs(RunPerfMeter.Month);
            }
            return best;
        }

        private static string Describe(Measure m)
        {
            return "Day n=" + m.Days + " avg=" + m.DayAverage.ToString("0.0000", CultureInfo.InvariantCulture) + " ms max=" + m.DayMax.ToString("0.0000", CultureInfo.InvariantCulture)
                   + " ms; Month n=" + m.Months + " avg=" + m.MonthAverage.ToString("0.0000", CultureInfo.InvariantCulture) + " ms max=" + m.MonthMax.ToString("0.0000", CultureInfo.InvariantCulture) + " ms";
        }

        [Test]
        public void ThreeYearsOfAScenarioWithChallenges_OverAMarketOf400Cpus_StayInsideTheBudgets()
        {
            Rig last;
            Measure measure = Best(() => new Rig("power-wall-2000", "no-oem", "no-debt", "tight-credit"), out last);
            Console.WriteLine("  perf  scenario power-wall-2000 + 3 challenges, 37 achievements, " + Cpus + " CPUs, " + GameDays + " days, best of " + Runs + ": " + Describe(measure));

            Assert.Equal(GameDays, measure.Days);
            Assert.Equal(35, measure.Months, "a month pass on the first of every month but the first");
            Assert.Equal(ScenarioStatus.Active, last.Engine.Scenario.Status, "the scenario was still going: the measure is of a run that works, not of one that ended");
            Assert.Equal(ChallengeStatus.Active, last.Run.ChallengeStates.Get("no-debt").Status);
            Assert.Equal(ChallengeStatus.Active, last.Run.ChallengeStates.Get("no-oem").Status);
            Assert.True(measure.DayAverage <= RunPerfMeter.DayBudgetMs, "the average day " + measure.DayAverage + " ms is over " + RunPerfMeter.DayBudgetMs + " ms");
            Assert.True(measure.MonthAverage <= RunPerfMeter.MonthBudgetMs, "the average month " + measure.MonthAverage + " ms is over " + RunPerfMeter.MonthBudgetMs + " ms");
        }

        [Test]
        public void ThreeYearsOfFreePlay_From2010_OverAMarketOf400Cpus_StayInsideTheBudgets()
        {
            Rig last;
            Measure measure = Best(() => new Rig(null, "no-oem", "no-debt"), out last);
            Console.WriteLine("  perf  free play from 2010 + 2 challenges, 37 achievements, " + Cpus + " CPUs, " + GameDays + " days, best of " + Runs + ": " + Describe(measure));

            Assert.Equal(GameDays, measure.Days);
            Assert.False(last.Run.HasScenario, "free play");
            Assert.Equal(ChallengeStatus.Active, last.Run.ChallengeStates.Get("no-debt").Status);
            Assert.True(measure.DayAverage <= RunPerfMeter.DayBudgetMs, "the average day " + measure.DayAverage + " ms is over " + RunPerfMeter.DayBudgetMs + " ms");
            Assert.True(measure.MonthAverage <= RunPerfMeter.MonthBudgetMs, "the average month " + measure.MonthAverage + " ms is over " + RunPerfMeter.MonthBudgetMs + " ms");
        }

        [Test]
        public void TheTableOfTheCompanies_TwelveRivalsAndTheText_IsBuiltFarInsideTheBudget()
        {
            var texts = new TestTexts("ru");
            var player = new ComparisonRow { CompanyKey = "player", Name = "Perf CPU", IsPlayer = true, Money = 5e6, Revenue12m = 24e6, MarketShare = 0.09, CpuCount = 100, MaxFrequency = 3000000 };
            var rivals = new List<ComparisonRow>();
            for (int i = 0; i < 12; i++)
            {
                rivals.Add(new ComparisonRow { CompanyKey = "ai:" + i, Name = "Rival " + i, Money = 1e6 * i, Revenue12m = 1e7 * i, MarketShare = 0.03 + i / 100.0, CpuCount = 20 + i, MaxFrequency = 2000000 + i * 1000, IsBankrupt = i == 11 });
            }

            var meter = new RunPerfMeter(() => true, Stopwatch.GetTimestamp, Stopwatch.Frequency, null);
            for (int round = 0; round < 60; round++)
            {
                long started = meter.Begin();
                ComparisonRow[] rows = ComparisonBuilder.Build(player, rivals, ComparisonBuilder.DefaultMaxAi);
                List<ComparisonCells> cells = ComparisonTableBuilder.Rows(rows, texts);
                meter.End(RunPerfMeter.Comparison, started, new DateTime(2010, 1, 1));
                if (round == 0) Assert.Equal(7, cells.Count, "the player and six AI companies");
            }

            double average = meter.SessionAverageMs(RunPerfMeter.Comparison);
            Console.WriteLine("  perf  table of the companies (12 rivals in, 6 out, with the text), 60 builds: avg=" + average.ToString("0.0000", CultureInfo.InvariantCulture)
                              + " ms max=" + meter.SessionMaxMs(RunPerfMeter.Comparison).ToString("0.0000", CultureInfo.InvariantCulture) + " ms");
            Assert.True(average <= RunPerfMeter.ComparisonBudgetMs, "the table takes " + average + " ms against " + RunPerfMeter.ComparisonBudgetMs + " ms");
        }
    }
}
