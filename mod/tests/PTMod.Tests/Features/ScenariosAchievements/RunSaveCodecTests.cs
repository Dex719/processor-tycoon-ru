using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using PTMod.Core;
using PTMod.Core.Logic.Json;
using PTMod.Core.Logic.Save;
using PTMod.Features.ScenariosAchievements.Logic;
using PTMod.Tests.Architecture;
using static PTMod.Tests.Features.ScenariosAchievements.RunTestData;

namespace PTMod.Tests.Features.ScenariosAchievements
{
    /// <summary>
    /// TSK-014: the section <c>scenarios-achievements</c> of the side save file (FR-16, NFR-3). The run - the scenario with its snapshot, goals, races
    /// and fail rules, the share window, the streaks, the counters, the ids of the modifier records and of the letters, the CPUs received by a
    /// transfer - goes out and comes back without loss (AC-16.1); the written section has no field of version (the core adds <c>_schema</c>); no section,
    /// a section newer than the build, or a damaged one gives no run (the director makes a <c>legacy</c> one on <c>GameReady(Loaded)</c>), and
    /// a status the code does not know falls back to the default of its kind. Driven also through the real model of the side file of the core.
    /// </summary>
    [Covers(typeof(RunSaveCodec), typeof(RunSaveSection), typeof(RunState), typeof(RunOrigin))]
    public class RunSaveCodecTests
    {
        private const string RunId = "4f6c1a8e-2d1b-4c55-9c0e-3a7b9e2f1d10";

        private static RunState RichRun()
        {
            ScenarioDefinition scenario = ScenarioTestData.Scenario("fabless-1985");
            RunState run = RunState.StartNew(RunId, D(1985, 1, 1), 2, scenario, new[] { "expensive-research", "no-debt" },
                new[] { "first-year-onboarding", "event-system", "scenarios-achievements" });
            run.MarkAssisted("cheats.enabled");
            run.MarkAssisted("feature.disabled:event-system");
            run.ScenarioEntryIds.Add("m-000020");
            run.BriefingMailId = 100001;
            run.ResultMailId = 100002;
            run.AchievementsThisRun.AddRange(new[] { "first-cpu", "first-sale" });

            run.ChallengeStates.Add("expensive-research", new[] { "m-000021" });
            ChallengeState debt = run.ChallengeStates.Add("no-debt", new string[0]);
            debt.Fail.HoldDays["debt"] = 12;
            debt.Status = ChallengeStatus.Failed;
            debt.FailedOn = D(1986, 4, 2);

            ScenarioState state = run.ScenarioState;
            string[] goalIds = scenario.Goals.Select(g => g.Id).ToArray();
            state.Goals[goalIds[0]].Status = GoalStatus.Done;
            state.Goals[goalIds[0]].DoneOn = D(1985, 9, 1);
            state.Goals[goalIds[1]].HoldDays = 5;
            state.Goals[goalIds[2]].Status = GoalStatus.Failed;
            state.Goals[goalIds[2]].FailedOn = D(1986, 1, 1);
            state.Races.Entries[goalIds[3]] = new RaceEntry { GoalId = goalIds[3], Status = RaceStatus.Lost, WinnerName = "Zilog Acorn", WonOn = D(1985, 7, 4) };
            state.Fail.HoldDays["debt-limit"] = 3;

            MetricsState m = run.Metrics;
            for (int day = 0; day < 40; day++)
            {
                m.ShareWindow.Push(Sales(10 + day, 1, 2 + day % 3, 3, 4), Sales(100 + day, 10, 20, 30, 40));
            }
            m.ProfitStreakMonths = 2;
            m.SolventStreakDays = 241;
            m.NegativeStreakDays = 0;
            m.MissedZeroStreakDays = 17;
            m.IpsLeaderStreakDays = 5;
            m.MinMoney = -118500000.5;
            m.SalesTotal = 41200000000L;
            m.OemSignedTotal = 3;
            m.RivalsBankrupt = 2;
            m.DefaultRecovered = 1;
            m.WasInDefaultYesterday = true;
            m.RunDays = 321;
            m.LastMonthKey = 198511;
            m.EventCounts["cpu.released.player"] = 4;
            m.EventCounts["oem.signed"] = 3;
            m.TransferredCpuIds.Add(901);
            m.TransferredCpuIds.Add(17);
            return run;
        }

        private static RunState RoundTrip(RunState run, out string text, List<string> problems = null)
        {
            JsonObject written = RunSaveCodec.Write(run);
            text = ModJson.Write(written, true);
            JsonNode parsed;
            JsonError error;
            Assert.True(ModJson.TryParse(text, out parsed, out error), "the section is valid JSON: " + error);
            return RunSaveCodec.Read((JsonObject)parsed, problems ?? new List<string>());
        }

        private static JsonObject Parse(string text)
        {
            JsonNode node;
            JsonError error;
            Assert.True(ModJson.TryParse(text, out node, out error), "invalid JSON: " + error);
            return (JsonObject)node;
        }

        // ---- the round trip ----

        [Test]
        public void TheRun_GoesOutAndComesBack_WithoutLoss()
        {
            RunState run = RichRun();
            string text;
            var problems = new List<string>();
            RunState back = RoundTrip(run, out text, problems);

            Assert.Equal(0, problems.Count, string.Join("; ", problems));
            Assert.NotNull(back);
            Assert.Equal(RunId, back.RunId);
            Assert.Equal(RunOrigin.NewGame, back.Origin);
            Assert.Equal(D(1985, 1, 1), back.StartDate);
            Assert.Equal(2, back.DifficultyAtStart);
            Assert.True(back.Assisted);
            Assert.Equal(new[] { "cheats.enabled", "feature.disabled:event-system" }, back.AssistedReasons.ToArray());
            Assert.False(back.Custom);
            Assert.Equal(new[] { "first-year-onboarding", "event-system", "scenarios-achievements" }, back.FeaturesAtStart.ToArray());
            Assert.Equal(new[] { "expensive-research", "no-debt" }, back.Challenges.ToArray());
            Assert.Equal(new[] { "m-000020" }, back.ScenarioEntryIds.ToArray());
            Assert.Equal(100001, back.BriefingMailId);
            Assert.Equal(100002, back.ResultMailId);
            Assert.Equal(new[] { "first-cpu", "first-sale" }, back.AchievementsThisRun.ToArray());

            // the challenges
            Assert.Equal(D(1985, 1, 1), back.ChallengeStates.StartDate);
            ChallengeState research = back.ChallengeStates.Get("expensive-research");
            Assert.Equal(ChallengeStatus.Active, research.Status);
            Assert.Equal(new[] { "m-000021" }, research.EntryIds.ToArray());
            ChallengeState debt = back.ChallengeStates.Get("no-debt");
            Assert.Equal(ChallengeStatus.Failed, debt.Status);
            Assert.Equal(D(1986, 4, 2), debt.FailedOn.Value);
            Assert.Equal(12, debt.Fail.HoldOf("debt"));
        }

        [Test]
        public void TheScenario_ComesBackWithItsSnapshotGoalsRacesAndFailMemory()
        {
            RunState run = RichRun();
            string text;
            RunState back = RoundTrip(run, out text);

            Assert.True(back.HasScenario);
            Assert.Equal("fabless-1985", back.ScenarioId);
            Assert.Equal(ScenarioStatus.Active, back.ScenarioState.Status);
            Assert.Equal(run.Scenario.ContentHash, back.Scenario.ContentHash, "the snapshot is the same record");
            Assert.Equal(run.ScenarioState.ContentHash, back.ScenarioState.ContentHash);
            Assert.Equal(ModJson.Write(run.Scenario.Source, false), ModJson.Write(back.Scenario.Source, false), "the whole snapshot of the record");
            Assert.Equal(run.Scenario.Goals.Length, back.Scenario.Goals.Length);
            Assert.Equal(D(1985, 1, 1), back.ScenarioState.StartDate);
            Assert.Equal(D(1985, 1, 1), back.ScenarioState.Races.StartDate);

            string[] goalIds = run.Scenario.Goals.Select(g => g.Id).ToArray();
            GoalState g0 = back.ScenarioState.FindGoal(goalIds[0]);
            Assert.Equal(GoalStatus.Done, g0.Status);
            Assert.Equal(D(1985, 9, 1), g0.DoneOn.Value);
            Assert.Equal(5, back.ScenarioState.FindGoal(goalIds[1]).HoldDays);
            GoalState g2 = back.ScenarioState.FindGoal(goalIds[2]);
            Assert.Equal(GoalStatus.Failed, g2.Status);
            Assert.Equal(D(1986, 1, 1), g2.FailedOn.Value);
            Assert.Equal(GoalStatus.Open, back.ScenarioState.FindGoal(goalIds[3]).Status);

            RaceEntry race = back.ScenarioState.Races.Get(goalIds[3]);
            Assert.Equal(RaceStatus.Lost, race.Status);
            Assert.Equal("Zilog Acorn", race.WinnerName);
            Assert.Equal(D(1985, 7, 4), race.WonOn.Value);

            Assert.Equal(3, back.ScenarioState.Fail.HoldOf("debt-limit"));
            Assert.False(back.ScenarioState.Fail.IsTriggered);
            Assert.Null(back.ScenarioState.Result);
        }

        [Test]
        public void ALostScenario_KeepsItsResultAndTheReasonOfTheLoss()
        {
            RunState run = RichRun();
            ScenarioState state = run.ScenarioState;
            state.Status = ScenarioStatus.Lost;
            state.Fail.TriggeredId = "raceLost";
            state.Fail.TextKey = "scn.fail.raceLost";
            state.Fail.TextArgs = new[] { "Zilog Acorn", "1985-07-04" };
            state.Fail.TriggeredOn = D(1985, 7, 4);
            state.Result = new ScenarioResult
            {
                Outcome = ScenarioStatus.Lost,
                FinishedOn = D(1985, 7, 4),
                Score = 0,
                Medal = ScoreCalculator.MedalNone,
                FailId = "raceLost",
                FailTextKey = "scn.fail.raceLost",
                FailArgs = new[] { "Zilog Acorn", "1985-07-04" },
                Breakdown = new ScoreBreakdown { GoalPoints = 100, TimeBonus = 0, DaysLeft = 0, TotalDays = 2191, ChallengeBonusPercent = 25, Score = 0 },
            };

            string text;
            RunState back = RoundTrip(run, out text);
            ScenarioState got = back.ScenarioState;
            Assert.Equal(ScenarioStatus.Lost, got.Status);
            Assert.True(got.Fail.IsTriggered);
            Assert.Equal("raceLost", got.Fail.TriggeredId);
            Assert.Equal("scn.fail.raceLost", got.Fail.TextKey);
            Assert.Equal(new[] { "Zilog Acorn", "1985-07-04" }, got.Fail.TextArgs);
            Assert.Equal(D(1985, 7, 4), got.Fail.TriggeredOn.Value);

            ScenarioResult result = got.Result;
            Assert.NotNull(result);
            Assert.Equal(ScenarioStatus.Lost, result.Outcome);
            Assert.False(result.IsWin);
            Assert.Equal(D(1985, 7, 4), result.FinishedOn);
            Assert.Equal(0, result.Score);
            Assert.Equal("none", result.Medal);
            Assert.Equal("raceLost", result.FailId);
            Assert.Equal(new[] { "Zilog Acorn", "1985-07-04" }, result.FailArgs);
            Assert.Equal(2191, result.Breakdown.TotalDays);
            Assert.Equal(25, result.Breakdown.ChallengeBonusPercent);
            Assert.Equal(new[] { "scenario.lost" }, result.EventIds());
        }

        [Test]
        public void AWonScenario_KeepsItsMedalAndScore()
        {
            RunState run = RichRun();
            run.ScenarioState.Status = ScenarioStatus.Won;
            run.ScenarioState.Result = new ScenarioResult
            {
                Outcome = ScenarioStatus.Won,
                FinishedOn = D(1988, 3, 1),
                Score = 612,
                Medal = "silver",
                Breakdown = new ScoreBreakdown { GoalPoints = 400, TimeBonus = 90, DaysLeft = 200, TotalDays = 1000, ChallengeBonusPercent = 25, Score = 612 },
            };

            string text;
            ScenarioResult back = RoundTrip(run, out text).ScenarioState.Result;
            Assert.True(back.IsWin);
            Assert.Equal(612, back.Score);
            Assert.Equal("silver", back.Medal);
            Assert.Null(back.FailId);
            Assert.Equal(0, back.FailArgs.Length);
            Assert.Equal(new[] { "scenario.won" }, back.EventIds());
        }

        [Test]
        public void TheMetrics_ComeBack_TheShareWindowToo_AC161()
        {
            RunState run = RichRun();
            string text;
            RunState back = RoundTrip(run, out text);
            MetricsState a = run.Metrics;
            MetricsState b = back.Metrics;

            Assert.Equal(a.ProfitStreakMonths, b.ProfitStreakMonths);
            Assert.Equal(a.SolventStreakDays, b.SolventStreakDays);
            Assert.Equal(a.NegativeStreakDays, b.NegativeStreakDays);
            Assert.Equal(a.MissedZeroStreakDays, b.MissedZeroStreakDays);
            Assert.Equal(a.IpsLeaderStreakDays, b.IpsLeaderStreakDays);
            Assert.Equal(a.MinMoney, b.MinMoney);
            Assert.Equal(a.SalesTotal, b.SalesTotal);
            Assert.Equal(a.OemSignedTotal, b.OemSignedTotal);
            Assert.Equal(a.RivalsBankrupt, b.RivalsBankrupt);
            Assert.Equal(a.DefaultRecovered, b.DefaultRecovered);
            Assert.True(b.WasInDefaultYesterday);
            Assert.Equal(321, b.RunDays);
            Assert.Equal(198511, b.LastMonthKey);
            Assert.Equal(4, b.EventCounts["cpu.released.player"]);
            Assert.Equal(3, b.EventCounts["oem.signed"]);
            Assert.Equal(2, b.EventCounts.Count);
            Assert.True(b.TransferredCpuIds.SetEquals(new[] { 17, 901 }));

            // the window: the same ring, the same shares (the 30-day share is the same to the last digit)
            Assert.Equal(a.ShareWindow.Days, b.ShareWindow.Days);
            Assert.Equal(a.ShareWindow.Head, b.ShareWindow.Head);
            for (int slot = 0; slot < a.ShareWindow.Days; slot++)
            {
                for (int column = 0; column < RollingWindow.Columns; column++)
                {
                    Assert.Equal(a.ShareWindow.PlayerAt(slot, column), b.ShareWindow.PlayerAt(slot, column));
                    Assert.Equal(a.ShareWindow.TotalAt(slot, column), b.ShareWindow.TotalAt(slot, column));
                }
            }
            Assert.Equal(a.ShareWindow.Share(RollingWindow.ColumnOf(Segment.MidRange)), b.ShareWindow.Share(RollingWindow.ColumnOf(Segment.MidRange)));
            Assert.True(a.ShareWindow.Share(0) > 0, "the test window is not empty");
        }

        [Test]
        public void TheWrittenText_IsStable_ReadingAndWritingAgainChangesNothing()
        {
            RunState run = RichRun();
            string first;
            RunState back = RoundTrip(run, out first);
            string second = ModJson.Write(RunSaveCodec.Write(back), true);
            Assert.Equal(first, second);
        }

        [Test]
        public void ALegacyRun_AndAFreePlayRun_RoundTrip()
        {
            RunState legacy = RunState.CreateLegacy(RunId, D(1993, 5, 5), 3, true, new[] { "event-system" });
            string text;
            RunState back = RoundTrip(legacy, out text);
            Assert.Equal(RunOrigin.Legacy, back.Origin);
            Assert.True(back.IsLegacy);
            Assert.True(back.Assisted);
            Assert.False(back.HasScenario);
            Assert.Null(back.Scenario);
            Assert.Equal(D(1993, 5, 5), back.StartDate);
            Assert.True(text.Contains("\"scenario\": null"), "free play writes 'scenario: null'");
            Assert.Equal(MetricsState.NoMinMoney, back.Metrics.MinMoney, "no day has been seen yet");
            Assert.True(text.Contains("\"minMoney\": null"));
            Assert.Equal(0, back.Challenges.Count);
        }

        // ---- the shape ----

        [Test]
        public void TheWrittenSection_HasNoVersionField_TheCoreAddsIt()
        {
            JsonObject section = RunSaveCodec.Write(RichRun());
            Assert.False(section.ContainsKey("_schema"));
            Assert.False(section.ContainsKey("schema"));
            Assert.False(ModJson.Write(section, false).Contains("_schema"));
            Assert.Equal(new[] { "run", "scenario", "metrics", "achievementsThisRun" }, section.Keys.ToArray());
            Assert.Equal("scenarios-achievements", RunSaveCodec.SectionId);
            Assert.Equal(1, RunSaveCodec.SchemaVersion);
        }

        [Test]
        public void ARunIsWrittenInTheShapeOfTheDesign()
        {
            JsonObject section = RunSaveCodec.Write(RichRun());
            JsonObject run = section.GetObject("run");
            foreach (string key in new[] { "runId", "origin", "startDate", "difficultyAtStart", "assisted", "assistedReasons", "custom", "featuresAtStart",
                                           "challenges", "challengeStates", "scenarioEntryIds", "transferredCpuIds", "briefingMailId", "resultMailId" })
            {
                Assert.True(run.ContainsKey(key), "run." + key);
            }
            Assert.Equal("newGame", run.GetString("origin"));
            Assert.Equal("1985-01-01", run.GetString("startDate"));
            Assert.Equal(new[] { 17, 901 }, run.GetArray("transferredCpuIds").Cast<JsonValue>().Select(v => (int)v.NumberValue).ToArray(), "sorted, so the text is stable");

            JsonObject scenario = section.GetObject("scenario");
            foreach (string key in new[] { "id", "status", "definition", "contentHash", "goals", "races", "fail", "result" })
            {
                Assert.True(scenario.ContainsKey(key), "scenario." + key);
            }
            Assert.Equal("Active", scenario.GetString("status"));
            Assert.Equal("fabless-1985", scenario.GetObject("definition").GetString("id"));

            JsonObject metrics = section.GetObject("metrics");
            foreach (string key in new[] { "shareWindow", "profitStreakMonths", "solventStreakDays", "negativeStreakDays", "missedZeroStreakDays", "ipsLeaderStreakDays",
                                           "minMoney", "salesTotal", "oemSignedTotal", "rivalsBankrupt", "defaultRecovered", "wasInDefaultYesterday", "eventCounts" })
            {
                Assert.True(metrics.ContainsKey(key), "metrics." + key);
            }
            JsonObject window = metrics.GetObject("shareWindow");
            Assert.Equal(30, window.GetInt32("days"));
            Assert.Equal(10, window.GetInt32("head"));
            Assert.Equal(30, window.GetArray("player").Count);
            Assert.Equal(5, ((JsonArray)window.GetArray("player")[0]).Count);
            Assert.Equal(1, metrics.GetArray("eventCounts").Cast<JsonArray>().Count(p => ((JsonValue)p[0]).StringValue == "oem.signed" && ((JsonValue)p[1]).NumberValue == 3));
        }

        [Test]
        public void TheExampleSectionOfTheDesign_IsRead()
        {
            string definition = ModJson.Write(ScenarioTestData.RecordsById("scenarios.json")["fabless-1985"], false);
            string text = @"{
  ""run"": {
    ""runId"": ""4f6c1a8e-2d1b-4c55-9c0e-3a7b9e2f1d10"", ""origin"": ""newGame"", ""startDate"": ""1985-01-01"", ""difficultyAtStart"": 2,
    ""assisted"": false, ""assistedReasons"": [], ""custom"": false,
    ""featuresAtStart"": [""first-year-onboarding"", ""event-system"", ""scenarios-achievements""],
    ""challenges"": [""expensive-research""],
    ""challengeStates"": { ""expensive-research"": { ""status"": ""active"", ""failedOn"": null, ""entryIds"": [""m-000021""] } },
    ""scenarioEntryIds"": [], ""transferredCpuIds"": [], ""briefingMailId"": 100001, ""resultMailId"": 0
  },
  ""scenario"": {
    ""id"": ""fabless-1985"", ""status"": ""Active"", ""definition"": __DEFINITION__, ""contentHash"": ""9b1e0f3c"",
    ""goals"": {
      ""g1"": { ""status"": ""Done"", ""doneOn"": ""1985-09-01"", ""holdDays"": 0 },
      ""g2"": { ""status"": ""Open"", ""doneOn"": null, ""holdDays"": 0 },
      ""g3"": { ""status"": ""Open"", ""doneOn"": null, ""holdDays"": 0 },
      ""g4"": { ""status"": ""Open"", ""doneOn"": null, ""holdDays"": 0 }
    },
    ""races"": {}, ""fail"": null, ""result"": null
  },
  ""metrics"": {
    ""shareWindow"": { ""head"": 12, ""days"": 30, ""player"": [[0,0,0,0,0]], ""total"": [[0,0,0,0,0]] },
    ""profitStreakMonths"": 2, ""solventStreakDays"": 241, ""negativeStreakDays"": 0, ""missedZeroStreakDays"": 0, ""ipsLeaderStreakDays"": 0,
    ""minMoney"": 118500000.0, ""salesTotal"": 412000, ""oemSignedTotal"": 0, ""rivalsBankrupt"": 0, ""defaultRecovered"": 0,
    ""wasInDefaultYesterday"": false, ""eventCounts"": [[""cpu.released.player"", 1]]
  },
  ""achievementsThisRun"": [""first-cpu"", ""first-sale""]
}".Replace("__DEFINITION__", definition);

            var problems = new List<string>();
            RunState run = RunSaveCodec.Read(Parse(text), problems);
            Assert.Equal(0, problems.Count, string.Join("; ", problems));
            Assert.NotNull(run);
            Assert.Equal(RunId, run.RunId);
            Assert.False(run.Assisted);
            Assert.Equal(new[] { "expensive-research" }, run.Challenges.ToArray());
            Assert.Equal(ChallengeStatus.Active, run.ChallengeStates.Get("expensive-research").Status, "'active' in any case");
            Assert.Equal(100001, run.BriefingMailId);
            Assert.Equal("9b1e0f3c", run.ScenarioState.ContentHash);
            Assert.Equal(GoalStatus.Done, run.ScenarioState.FindGoal("g1").Status);
            Assert.Equal(D(1985, 9, 1), run.ScenarioState.FindGoal("g1").DoneOn.Value);
            Assert.Equal(GoalStatus.Open, run.ScenarioState.FindGoal("g2").Status);
            Assert.Equal(12, run.Metrics.ShareWindow.Head);
            Assert.Equal(118500000.0, run.Metrics.MinMoney);
            Assert.Equal(241, run.Metrics.SolventStreakDays);
            Assert.Equal(1, run.Metrics.EventCounts["cpu.released.player"]);
            Assert.Equal(412000L, run.Metrics.SalesTotal);
            Assert.Equal(new[] { "first-cpu", "first-sale" }, run.AchievementsThisRun.ToArray());
        }

        // ---- no section, a newer one, a damaged one ----

        [Test]
        public void NoSection_OrNoRun_GivesNoRun_TheDirectorMakesALegacyOne()
        {
            var problems = new List<string>();
            Assert.Null(RunSaveCodec.Read(null, problems));
            Assert.Null(RunSaveCodec.Read(new JsonObject(), problems));
            Assert.Null(RunSaveCodec.Read(Parse("{ \"run\": { \"runId\": \"x\" } }"), problems), "a run without a start date is no run");
            Assert.Null(RunSaveCodec.Read(Parse("{ \"run\": { \"startDate\": \"yesterday\" } }"), problems));
            Assert.Null(RunSaveCodec.Read(Parse("{ \"run\": 5 }"), problems));
            Assert.True(problems.Count >= 4);
            Assert.Null(RunSaveCodec.Write(null), "no run, nothing to store");
        }

        [Test]
        public void UnknownStatuses_BecomeTheDefaultOfTheirKind()
        {
            JsonObject section = RunSaveCodec.Write(RichRun());
            JsonObject scenario = section.GetObject("scenario");
            scenario.Set("status", "Exploded");
            JsonObject goals = scenario.GetObject("goals");
            foreach (KeyValuePair<string, JsonNode> pair in goals) ((JsonObject)pair.Value).Set("status", "Frozen");
            JsonObject challenge = section.GetObject("run").GetObject("challengeStates").GetObject("no-debt");
            challenge.Set("status", "Teleported");
            section.GetObject("run").Set("origin", "from-mars");

            var problems = new List<string>();
            RunState run = RunSaveCodec.Read(section, problems);
            Assert.NotNull(run);
            Assert.Equal(ScenarioStatus.None, run.ScenarioState.Status, "an unknown scenario status is 'none': nothing is judged");
            foreach (GoalState goal in run.ScenarioState.Goals.Values) Assert.Equal(GoalStatus.Open, goal.Status);
            Assert.Equal(ChallengeStatus.Active, run.ChallengeStates.Get("no-debt").Status);
            Assert.Equal(RunOrigin.NewGame, run.Origin);
        }

        [Test]
        public void ARaceWithoutAKnownOutcome_IsNotKept()
        {
            JsonObject section = RunSaveCodec.Write(RichRun());
            JsonObject races = section.GetObject("scenario").GetObject("races");
            Assert.Equal(1, races.Count);
            foreach (KeyValuePair<string, JsonNode> pair in races) ((JsonObject)pair.Value).Set("status", "Maybe");

            var problems = new List<string>();
            RunState run = RunSaveCodec.Read(section, problems);
            Assert.Equal(0, run.ScenarioState.Races.Entries.Count, "an undecided race is simply open");
            Assert.True(problems.Count > 0);
        }

        [Test]
        public void ABrokenSnapshot_LeavesTheRunWithoutAScenario_ButKeepsTheRest()
        {
            JsonObject section = RunSaveCodec.Write(RichRun());
            section.GetObject("scenario").Set("definition", new JsonObject().Set("id", "x"));
            var problems = new List<string>();
            RunState run = RunSaveCodec.Read(section, problems);

            Assert.NotNull(run);
            Assert.False(run.HasScenario);
            Assert.Equal(241, run.Metrics.SolventStreakDays, "the metrics are still there");
            Assert.True(problems.Any(p => p.Contains("scenario.definition")), string.Join("; ", problems));

            JsonObject noSnapshot = RunSaveCodec.Write(RichRun());
            noSnapshot.GetObject("scenario").Remove("definition");
            Assert.False(RunSaveCodec.Read(noSnapshot, null).HasScenario);
        }

        [Test]
        public void AScenarioBuiltByHand_HasNoRecordToKeep_AndReadsBackAsFreePlay()
        {
            ScenarioDefinition byHand = Scenario(new[] { Goal("g1", Metric("money", ">=", 1)) });
            Assert.Null(byHand.Source, "a definition that was not mapped from a record has no snapshot");
            RunState run = RunState.StartNew("r", D(1980, 1, 1), 2, byHand, null, null);

            JsonObject section = RunSaveCodec.Write(run);
            Assert.True(section.GetObject("scenario").GetObject("definition") == null, "nothing to write as the snapshot");

            var problems = new List<string>();
            RunState back = RunSaveCodec.Read(section, problems);
            Assert.NotNull(back);
            Assert.False(back.HasScenario);
            Assert.True(problems.Any(p => p.Contains("scenario.definition")));
        }

        [Test]
        public void TheFlagOfAssisted_CannotBeLoweredByTheFile()
        {
            JsonObject section = RunSaveCodec.Write(RichRun());
            section.GetObject("run").Set("assisted", false);
            RunState run = RunSaveCodec.Read(section, null);
            Assert.True(run.Assisted, "reasons are left: the run is still assisted");

            section.GetObject("run").Set("assistedReasons", new JsonArray());
            Assert.False(RunSaveCodec.Read(section, null).Assisted, "a file with neither a flag nor a reason is a run without help");

            section.GetObject("run").Set("assisted", true);
            RunState flagOnly = RunSaveCodec.Read(section, null);
            Assert.True(flagOnly.Assisted, "the flag alone is enough, whatever the reasons");
            Assert.Equal(0, flagOnly.AssistedReasons.Count);
        }

        [Test]
        public void DamagedValues_AreRepaired_AndEachRepairIsALine()
        {
            JsonObject section = RunSaveCodec.Write(RichRun());
            JsonObject runBlock = section.GetObject("run");
            runBlock.Set("difficultyAtStart", 99);
            runBlock.Set("briefingMailId", -5);
            runBlock.Set("featuresAtStart", new JsonArray().Add("ok").Add(7).Add(new string('x', 500)));
            runBlock.Set("transferredCpuIds", new JsonArray().Add(5).Add("text").Add(1.5));
            JsonObject metrics = section.GetObject("metrics");
            metrics.Set("solventStreakDays", "many");
            metrics.Set("profitStreakMonths", -3);
            metrics.Set("eventCounts", new JsonArray().Add(new JsonArray().Add("a").Add(2)).Add("junk").Add(new JsonArray().Add("b")).Add(new JsonArray().Add("c").Add(-1)));
            metrics.GetObject("shareWindow").Set("days", 0);
            section.Set("achievementsThisRun", "not a list");

            var problems = new List<string>();
            RunState run = RunSaveCodec.Read(section, problems);

            Assert.NotNull(run);
            Assert.Equal(5, run.DifficultyAtStart, "brought into range");
            Assert.Equal(0, run.BriefingMailId);
            Assert.Equal(new[] { "ok" }, run.FeaturesAtStart.ToArray(), "a number and an endless text are dropped");
            Assert.True(run.Metrics.TransferredCpuIds.SetEquals(new[] { 5 }));
            Assert.Equal(0, run.Metrics.SolventStreakDays);
            Assert.Equal(0, run.Metrics.ProfitStreakMonths);
            Assert.Equal(1, run.Metrics.EventCounts.Count);
            Assert.Equal(2, run.Metrics.EventCounts["a"]);
            Assert.Equal(RollingWindow.DefaultDays, run.Metrics.ShareWindow.Days, "an impossible ring length is the usual 30");
            Assert.Equal(0, run.AchievementsThisRun.Count);
            Assert.True(problems.Count >= 8, problems.Count + " lines: " + string.Join("; ", problems));
            Assert.True(problems.Any(p => p.Contains("shareWindow.days")), "the impossible ring length is reported: " + string.Join("; ", problems));
        }

        [Test]
        public void AShortOrDamagedWindow_ReadsAsZeros_NotAnException()
        {
            JsonObject section = RunSaveCodec.Write(RichRun());
            JsonObject window = section.GetObject("metrics").GetObject("shareWindow");
            window.Set("player", new JsonArray().Add(new JsonArray().Add(5).Add("x")).Add(7));
            window.Set("total", JsonValue.Null);
            window.Set("head", 100);

            RunState run = RunSaveCodec.Read(section, new List<string>());
            RollingWindow w = run.Metrics.ShareWindow;
            Assert.Equal(5L, w.PlayerAt(0, 0));
            Assert.Equal(0L, w.PlayerAt(0, 1));
            Assert.Equal(0L, w.PlayerAt(1, 0));
            Assert.Equal(0L, w.TotalSum(0));
            Assert.Equal(100 % 30, w.Head, "a head outside the ring wraps around");
            Assert.Equal(0.0, w.Share(0));
        }

        [Test]
        public void ManyEntries_AreCutAtTheirLimits()
        {
            JsonObject section = RunSaveCodec.Write(RichRun());
            var many = new JsonArray();
            for (int i = 0; i < RunSaveCodec.MaxListItems + 50; i++) many.Add("f" + i);
            section.GetObject("run").Set("featuresAtStart", many);

            var kinds = new JsonArray();
            for (int i = 0; i < RunSaveCodec.MaxEventKinds + 20; i++) kinds.Add(new JsonArray().Add("e" + i).Add(1));
            section.GetObject("metrics").Set("eventCounts", kinds);

            var problems = new List<string>();
            RunState run = RunSaveCodec.Read(section, problems);
            Assert.Equal(RunSaveCodec.MaxListItems, run.FeaturesAtStart.Count);
            Assert.Equal(RunSaveCodec.MaxEventKinds, run.Metrics.EventCounts.Count);
            Assert.True(problems.Count >= 2);
        }

        [Test]
        public void Reading_DoesNotChangeTheObjectItIsGiven()
        {
            JsonObject section = RunSaveCodec.Write(RichRun());
            string before = ModJson.Write(section, false);
            RunSaveCodec.Read(section, new List<string>());
            Assert.Equal(before, ModJson.Write(section, false));
        }

        [Test]
        public void TheRunReadBack_IsNotTiedToTheObjectItCameFrom()
        {
            JsonObject section = RunSaveCodec.Write(RichRun());
            RunState run = RunSaveCodec.Read(section, null);
            section.GetObject("scenario").GetObject("definition").Set("id", "changed-later");
            Assert.Equal("fabless-1985", run.Scenario.Id);
            Assert.Equal("fabless-1985", run.Scenario.Source.GetString("id"), "the snapshot is a copy");
        }

        // ---- through the real model of the side file ----

        private static SideFileModel NewModel(RunSaveSection section)
        {
            var model = new SideFileModel();
            string problem;
            Assert.True(model.TryRegister(section, out problem), problem);
            return model;
        }

        [Test]
        public void TheSideFileModel_WritesTheSectionWithItsVersion_AndReadsItBack()
        {
            var writer = new RunSaveSection();
            SideFileModel model = NewModel(writer);
            model.StartNewGame();
            Assert.Null(writer.Run);
            writer.Run = RichRun();

            SideFileBuildReport build = model.Build(new SideFileHeader { modVersion = "0.1.0", gameDate = "1985-03-01" });
            JsonObject features = build.Document.GetObject("features");
            JsonObject stored = features.GetObject("scenarios-achievements");
            Assert.NotNull(stored);
            Assert.Equal(1, stored.GetInt32("_schema"), "the core writes the version of the section");
            Assert.True(stored.ContainsKey("run") && stored.ContainsKey("scenario") && stored.ContainsKey("metrics"));

            string fileText = ModJson.Write(build.Document, true);
            var reader = new RunSaveSection();
            SideFileModel loading = NewModel(reader);
            SideFileReadReport report = loading.Apply(fileText);
            Assert.Equal(SideFileStatus.Loaded, report.Status);
            Assert.NotNull(reader.Run);
            Assert.Equal(RunId, reader.Run.RunId);
            Assert.Equal(241, reader.Run.Metrics.SolventStreakDays);
            Assert.Equal("fabless-1985", reader.Run.ScenarioId);
        }

        [Test]
        public void ASaveWithoutTheSection_GivesNoRun_AndANewGameStartsClean()
        {
            var section = new RunSaveSection();
            SideFileModel model = NewModel(section);
            section.Run = RichRun();

            model.Apply("{ \"schema\": 1, \"features\": { } }");
            Assert.Null(section.Run, "the section is not in the file: no run, the director makes a legacy one");

            section.Run = RichRun();
            model.ApplyMissing();
            Assert.Null(section.Run, "no side file at all");

            section.Run = RichRun();
            model.StartNewGame();
            Assert.Null(section.Run);

            SideFileBuildReport build = model.Build(new SideFileHeader { modVersion = "0.1.0" });
            Assert.False(build.Document.GetObject("features").ContainsKey("scenarios-achievements"), "no run, no section");
        }

        [Test]
        public void ASectionNewerThanTheBuild_GivesNoRun_AndIsKeptAsItWas()
        {
            JsonObject newer = RunSaveCodec.Write(RichRun());
            newer.Set("_schema", 2);
            newer.Set("futureField", "keep me");
            var file = new JsonObject();
            file.Set("schema", 1);
            file.Set("features", new JsonObject().Set("scenarios-achievements", newer));

            var section = new RunSaveSection();
            SideFileModel model = NewModel(section);
            SideFileReadReport report = model.Apply(ModJson.Write(file, false));
            Assert.Null(section.Run, "NFR-3: a newer section means defaults, no scenario is judged");
            Assert.Equal(SideFileStatus.Newer, report.Status);
            Assert.True(report.NewerSections.Contains("scenarios-achievements"));

            section.Run = RunState.CreateLegacy("legacy-1", D(1990, 1, 1), 2, false, null);   // the director's legacy run, in memory
            SideFileBuildReport build = model.Build(new SideFileHeader { modVersion = "0.1.0" });
            JsonObject kept = build.Document.GetObject("features").GetObject("scenarios-achievements");
            Assert.Equal(2, kept.GetInt32("_schema"));
            Assert.Equal("keep me", kept.GetString("futureField"));
            Assert.Equal(RunId, kept.GetObject("run").GetString("runId"), "the legacy run is not written over a newer section");
        }

        [Test]
        public void ASubscriberOfRunReplaced_IsToldOnALoadAndAReset_AndItsFaultIsLogged()
        {
            var warnings = new List<string>();
            var section = new RunSaveSection(warnings.Add);
            int calls = 0;
            section.RunReplaced += () => calls++;
            section.RunReplaced += () => { throw new InvalidOperationException("boom"); };
            section.RunReplaced += () => calls++;

            section.ResetToDefaults();
            Assert.Equal(2, calls, "the second subscriber failed and the third still ran");
            Assert.Equal(1, warnings.Count);

            section.Read(RunSaveCodec.Write(RichRun()), 1);
            Assert.Equal(4, calls);
            Assert.NotNull(section.Run);

            section.Read(Parse("{ \"run\": { \"startDate\": \"nope\" } }"), 1);
            Assert.Null(section.Run);
            Assert.True(warnings.Any(w => w.Contains("startDate")), string.Join("; ", warnings));
        }

        [Test]
        public void TheParticipant_ReportsTheIdAndTheVersion_OfTheDesign()
        {
            var section = new RunSaveSection();
            Assert.Equal("scenarios-achievements", section.SectionId);
            Assert.Equal(1, section.SchemaVersion);
            Assert.Null(section.Write(), "no run, no data");
            section.Run = RichRun();
            Assert.NotNull(section.Write());
        }

        // ---- the game-side participant, as text ----

        private static string Source(params string[] path)
        {
            string file = Path.Combine(new[] { SourceScan.SrcDirOrFail(), "Features", "ScenariosAchievements" }.Concat(path).ToArray());
            Assert.True(File.Exists(file), "no source file: " + file);
            return SourceScan.Strip(SourceScan.ReadText(file), false);
        }

        [Test]
        public void TheGameSideParticipant_IsTheCoresInterface_OverTheTestedSection_AndIsRegistered()
        {
            string code = Source("Integration", "RunSaveParticipant.cs");
            Assert.True(Regex.IsMatch(code, @"class\s+RunSaveParticipant\s*:\s*IModSaveParticipant"), "implements IModSaveParticipant");
            Assert.True(code.Contains("new RunSaveSection("), "the work is the section's");
            Assert.True(Regex.IsMatch(code, @"string\s+SectionId\s*\{\s*get\s*\{\s*return\s+_section\.SectionId\s*;"), "SectionId comes from the tested section");
            Assert.True(Regex.IsMatch(code, @"int\s+SchemaVersion\s*\{\s*get\s*\{\s*return\s+_section\.SchemaVersion\s*;"), "SchemaVersion comes from the tested section");
            Assert.True(code.Contains("_section.ResetToDefaults()") && code.Contains("_section.Read(section, schema)") && code.Contains("return _section.Write()"));
            Assert.True(code.Contains("ModLog.Warn("), "the problems of a damaged section go to the log");

            string feature = Source("ScenariosFeature.cs");
            Assert.True(Regex.IsMatch(feature, @"ctx\.SaveParticipant\(\s*RunSaveParticipant\.Instance\s*\)"), "Register hands the participant to the core");
        }

        [Test]
        public void TheSectionIdIsTheIdOfTheFeature()
        {
            Assert.Equal(ScenarioTestData.FeatureId, RunSaveCodec.SectionId);
            Assert.Equal(ContentVocabulary.FeatureId, RunSaveCodec.SectionId);
        }
    }
}
