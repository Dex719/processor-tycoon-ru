using System;
using System.Collections.Generic;
using System.Linq;
using PTMod.Core.Logic.Json;
using PTMod.Features.ScenariosAchievements.Logic;
using static PTMod.Tests.Features.ScenariosAchievements.RunTestData;

namespace PTMod.Tests.Features.ScenariosAchievements
{
    /// <summary>
    /// TSK-019: the brain of a run - a whole scenario played through the engine on the real content (AC-8.1), the order in which the end of a
    /// scenario reaches the profile and the achievements, the isolation of the blocks, challenges that fail or pass (in free play and inside a
    /// scenario), a lost race (AC-6.1), the bankruptcy of the player and of a rival (AC-12.3), a CPU that came by a transfer, the assisted flag
    /// in the profile, and a save and a load in the middle of a scenario (AC-16.1).
    /// </summary>
    [Covers(typeof(RunEngine), typeof(RunPassResult), typeof(LiveRun), typeof(IRunPorts))]
    public class RunEngineTests
    {
        private static readonly List<AchievementDefinition> Standard = ScenarioTestData.StandardAchievements();

        /// <summary>The ports over a real profile model, with a log of what was asked of them.</summary>
        private sealed class Ports : IRunPorts
        {
            public readonly ProfileModel Model = new ProfileModel(new JsonObject());
            public readonly List<string> Log = new List<string>();
            public readonly List<string> Removed = new List<string>();
            public readonly List<ScenarioResult> Results = new List<ScenarioResult>();
            public readonly List<string> Passed = new List<string>();
            public bool ProfileUnreadable;

            public ProfileView Profile
            {
                get { return ProfileUnreadable ? null : Model.View; }
            }

            public bool UnlockAchievement(string achievementId, AchievementEntry entry)
            {
                Log.Add("unlock:" + achievementId);
                return Model.Unlock(achievementId, entry);
            }

            public void RecordScenarioResult(ScenarioResult result, RunInfo run)
            {
                Log.Add("record");
                Results.Add(result);
                Model.RecordResult(result, run);
            }

            public void RecordChallengePassed(string challengeId, int years)
            {
                Passed.Add(challengeId + ":" + years);
                Model.RecordChallenge(challengeId, years);
            }

            public void RemoveModifierEntries(IList<string> entryIds)
            {
                Removed.AddRange(entryIds);
            }
        }

        private static readonly string[] Features = { "event-system", "scenarios-achievements" };

        private static LiveRun Live(DateTime today, bool strict = false, string companyType = "idm")
        {
            return new LiveRun { Today = today, CompanyType = companyType, CompanyName = "Garage CPU", Difficulty = 2, StrictAssisted = strict };
        }

        private static RunState Start(string scenarioId, params string[] challenges)
        {
            ScenarioDefinition scenario = scenarioId == null ? null : ScenarioTestData.Scenario(scenarioId);
            DateTime start = scenario == null ? D(1980, 1, 1) : D(scenario.Start.Year, 1, 1);
            return RunState.StartNew("run-1", start, 2, scenario, challenges, Features);
        }

        private static RunEngine Engine(RunState run, Ports ports, IEnumerable<ChallengeDefinition> challenges = null,
                                        Func<string, Action, bool> guard = null, int challengeYears = 10, IEnumerable<AchievementDefinition> catalog = null,
                                        IEnumerable<string> standardScenarios = null)
        {
            return new RunEngine(run, challenges, new AchievementEvaluator(catalog ?? Standard, standardScenarios ?? ScenarioTestData.ScenarioIds), null,
                ports, guard, () => new DateTime(2026, 10, 10, 12, 0, 0, DateTimeKind.Utc), challengeYears);
        }

        private static Func<DaySample> Today(DateTime date, double money = 1000)
        {
            return () => Day(date, money);
        }

        private static Func<MonthSample> LastMonth(int year, int month, double revenue, double expenses)
        {
            return () => Month(year, month, revenue, expenses, revenue);
        }

        private static ScenarioOutcomeEvent Find(RunPassResult result, ScenarioOutcomeKind kind, string goalId = null)
        {
            foreach (ScenarioOutcomeEvent e in result.ScenarioEvents)
            {
                if (e.Kind == kind && (goalId == null || e.GoalId == goalId)) return e;
            }
            Assert.Fail("no " + kind + " " + goalId + " among " + string.Join(",", result.ScenarioEvents.Select(x => x.Kind + ":" + x.GoalId)));
            return null;
        }

        private static CpuInfo ReleasedCpu(int id, DateTime on, float frequency = 3000, string companyType = "idm")
        {
            return Cpu(id, frequency, companyType: companyType, releasedOn: on);
        }

        // ---- a whole scenario ----

        [Test]
        public void TheGarageScenario_IsWonThroughTheEngine_WithTheScoreOfAC81_AndTheProfileKnowsIt()
        {
            RunState run = Start("garage-1975");
            var ports = new Ports();
            RunEngine engine = Engine(run, ports);

            // g1: the first CPU, long before 1976-07-01; the achievement "First silicon" (AC-12.1) opens with it
            RunPassResult release = engine.OnCpuReleased(ReleasedCpu(1, D(1975, 9, 1)), true, "Garage CPU", D(1975, 9, 1), Live(D(1975, 9, 1)));
            Assert.True(HasEvent(release.ScenarioEvents, ScenarioOutcomeKind.GoalDone, "g1"));
            Assert.Equal(new[] { "first-cpu" }, release.Achievements.ToArray());
            Assert.True(ports.Model.View.IsUnlocked("first-cpu"));
            Assert.Equal(new[] { "first-cpu" }, run.AchievementsThisRun.ToArray());

            // g2: a profitable month (monthly check); "In the black" opens
            RunPassResult month = engine.OnMonth(LastMonth(1975, 10, 20000, 8000), Live(D(1975, 11, 1)));
            Assert.True(HasEvent(month.ScenarioEvents, ScenarioOutcomeKind.GoalDone, "g2"));
            Assert.True(month.Achievements.Contains("first-profit"));

            // g4 (optional): an OEM contract
            RunPassResult oem = engine.OnOemSigned(Live(D(1976, 3, 1)));
            Assert.True(oem.IsEmpty, "'First deal' is a metric of the day: it comes with the next day's pass");
            Assert.Equal(1.0, engine.Snapshot.Get("oem.signed.total"), "but the counter is there at once");
            Assert.Equal(ScenarioStatus.Active, engine.Scenario.Status);

            // g3 on 1977-01-01: the last required goal wins
            RunPassResult last = engine.OnDay(Today(D(1977, 1, 1), 6000000), Live(D(1977, 1, 1)));
            Assert.Equal(ScenarioOutcomeKind.GoalDone, last.ScenarioEvents[0].Kind);
            Assert.Equal(ScenarioStatus.Won, engine.Scenario.Status);
            Assert.NotNull(last.Finished);
            Assert.Equal(600, last.Finished.Score);
            Assert.Equal("silver", last.Finished.Medal);
            Assert.Equal(D(1977, 1, 1), last.Finished.FinishedOn);
            Assert.True(ReferenceEquals(run.ScenarioState.Result, last.Finished), "the result lives in the run's state, which is what is saved");

            // the profile: the best result, the history, the achievement of the end
            ProfileScenarioRecord record = ports.Model.View.ScenarioRecordOf("garage-1975");
            Assert.NotNull(record);
            Assert.Equal(600, record.Best.Score);
            Assert.Equal("silver", record.Best.Medal);
            Assert.Equal(1, ports.Model.View.History.Count);
            Assert.True(last.Achievements.Contains("first-oem"), "the contract of the metric 'oem.signed.total' opens its achievement on the day pass");
            Assert.True(last.Achievements.Contains("scenario-first"), "'Scenario complete' comes with the win");
            Assert.False(last.Achievements.Contains("scenario-gold"));
            Assert.Equal(1, engine.Events.Count("scenario.won"));
            Assert.Equal(0, engine.Events.Count("scenario.gold"));
            Assert.True(run.AchievementsThisRun.Contains("scenario-first"));
        }

        [Test]
        public void AfterTheResult_NothingChanges_E8()
        {
            RunState run = Start("garage-1975");
            var ports = new Ports();
            RunEngine engine = Engine(run, ports);
            RunPassResult lost = engine.OnPlayerBankrupt(Live(D(1976, 1, 1)));
            Assert.NotNull(lost.Finished);
            Assert.Equal(ScenarioStatus.Lost, engine.Scenario.Status);
            Assert.Equal(1, ports.Results.Count);

            RunPassResult again = engine.OnPlayerBankrupt(Live(D(1976, 2, 1)));
            Assert.Null(again.Finished);
            Assert.Equal(1, ports.Results.Count, "the result is recorded once");
            Assert.Equal(ScenarioStatus.Lost, engine.Scenario.Status);

            RunPassResult day = engine.OnDay(Today(D(1977, 1, 1), 9000000), Live(D(1977, 1, 1)));
            Assert.Equal(0, day.ScenarioEvents.Count);
            Assert.Equal(ScenarioStatus.Lost, engine.Scenario.Status);
            Assert.Equal(0, run.ScenarioState.Result.Score);
        }

        // ---- the order of the end ----

        [Test]
        public void TheResult_ReachesTheProfileFirst_ThenTheEventsOfTheEnd_ThenTheAchievementsTheyOpen()
        {
            // an achievement that reads the profile ("gold in every standard scenario") can only be earned if the result is recorded first
            var rule = new AchievementDefinition
            {
                Id = "all-gold-test",
                Category = "meta",
                Points = 1,
                TitleKey = "ach.all-gold-test.title",
                DescKey = "ach.all-gold-test.desc",
                Check = new CheckSpec
                {
                    Kind = CheckSpec.KindProfile,
                    Profile = new ProfileSpec { Rule = ProfileSpec.RuleAllScenarioMedals, Medal = "gold" },
                    Cadence = CheckSpec.CadenceEvent,
                },
            };
            var won = new AchievementDefinition
            {
                Id = "won-test",
                Category = "meta",
                Points = 1,
                TitleKey = "ach.won-test.title",
                DescKey = "ach.won-test.desc",
                Check = Event("scenario.won"),
            };

            ScenarioDefinition scenario = Scenario(new[] { Goal("g1", Metric("money", ">=", 1)) });
            RunState run = RunState.StartNew("run-1", D(1980, 1, 1), 2, scenario, null, Features);
            var ports = new Ports();
            RunEngine engine = Engine(run, ports, null, null, 10, new[] { rule, won }, new[] { "test-scenario" });

            RunPassResult result = engine.OnDay(Today(D(1980, 1, 2), 5), Live(D(1980, 1, 2)));
            Assert.NotNull(result.Finished);
            Assert.Equal("gold", result.Finished.Medal, "a day after the start with the only goal done: 100 + 100 = 200");

            Assert.True(result.Achievements.Contains("all-gold-test"), "the profile already held the gold when the event was judged");
            Assert.True(result.Achievements.Contains("won-test"));
            Assert.True(ports.Log.IndexOf("record") >= 0 && ports.Log.IndexOf("record") < ports.Log.IndexOf("unlock:all-gold-test"), string.Join(",", ports.Log));
            Assert.True(ports.Log.IndexOf("record") < ports.Log.IndexOf("unlock:won-test"));
        }

        // ---- isolation ----

        [Test]
        public void TheBlocksRunInOrder_AndAFailedBlockDoesNotStopTheOthers()
        {
            RunState run = Start("garage-1975");
            var ports = new Ports();
            var log = new List<string>();
            string failing = "scenario";
            RunEngine engine = Engine(run, ports, null, (name, body) =>
            {
                log.Add(name);
                if (name == failing) return false;
                body();
                return true;
            });

            RunPassResult day = engine.OnDay(Today(D(1975, 1, 2), 150000000), Live(D(1975, 1, 2)));
            Assert.Equal(new[] { "metrics", "scenario", "challenges", "achievements" }, log.ToArray());
            Assert.True(day.Achievements.Contains("cash-100m"), "the achievements were judged although the scenario block failed");
            Assert.Equal(0, day.ScenarioEvents.Count);
        }

        [Test]
        public void WhenTheMetricsFail_NothingIsJudged_TheSnapshotWouldBeYesterdays()
        {
            RunState run = Start("garage-1975");
            var ports = new Ports();
            var log = new List<string>();
            RunEngine engine = Engine(run, ports, null, (name, body) =>
            {
                log.Add(name);
                if (name == "metrics") return false;
                body();
                return true;
            });

            RunPassResult day = engine.OnDay(Today(D(1975, 1, 2), 150000000), Live(D(1975, 1, 2)));
            Assert.Equal(new[] { "metrics" }, log.ToArray());
            Assert.True(day.IsEmpty);
            Assert.Equal(0, ports.Log.Count);
        }

        [Test]
        public void ASamplerThatThrows_IsCaughtByTheGuard_AndTheDayJudgesNothing()
        {
            RunState run = Start("garage-1975");
            RunEngine engine = Engine(run, new Ports());
            RunPassResult day = engine.OnDay(() => { throw new InvalidOperationException("the game is not ready"); }, Live(D(1975, 1, 2)));
            Assert.True(day.IsEmpty);
            Assert.Null(engine.LastDay);
        }

        [Test]
        public void AFailedProfileWrite_DoesNotLoseTheResultOfTheRun_NorTheOtherAchievements()
        {
            RunState run = Start("garage-1975");
            var ports = new FailingResultPorts();
            RunEngine engine = new RunEngine(run, null, new AchievementEvaluator(Standard, ScenarioTestData.ScenarioIds), null, ports,
                (name, body) => { try { body(); return true; } catch (InvalidOperationException) { return false; } });

            RunPassResult lost = engine.OnPlayerBankrupt(Live(D(1976, 1, 1)));
            Assert.NotNull(lost.Finished, "the scenario is over in the run whatever the profile did");
            Assert.Equal(ScenarioStatus.Lost, run.ScenarioState.Status);
            Assert.True(ports.Model.View.IsUnlocked("scenario-first") == false);
        }

        private sealed class FailingResultPorts : IRunPorts
        {
            public readonly ProfileModel Model = new ProfileModel(new JsonObject());
            public ProfileView Profile { get { return Model.View; } }
            public bool UnlockAchievement(string achievementId, AchievementEntry entry) { return Model.Unlock(achievementId, entry); }
            public void RecordScenarioResult(ScenarioResult result, RunInfo run) { throw new InvalidOperationException("the disk is full"); }
            public void RecordChallengePassed(string challengeId, int years) { }
            public void RemoveModifierEntries(IList<string> entryIds) { }
        }

        // ---- challenges ----

        [Test]
        public void AChallengeThatFailsInFreePlay_TakesItsRecordsAway_AndTheGameGoesOn()
        {
            ChallengeDefinition noDebt = Challenge("no-debt", 0.25f, Fail("debt", Metric("money.negativeStreakDays", ">=", 3)));
            RunState run = Start(null, "no-debt");
            run.ChallengeStates.Add("no-debt", new[] { "m-000021", "m-000022" });
            var ports = new Ports();
            RunEngine engine = Engine(run, ports, new[] { noDebt });

            Assert.Equal(0, engine.OnDay(Today(D(1980, 1, 2), -5), Live(D(1980, 1, 2))).ChallengeEvents.Count);
            Assert.Equal(0, engine.OnDay(Today(D(1980, 1, 3), -5), Live(D(1980, 1, 3))).ChallengeEvents.Count);
            RunPassResult failed = engine.OnDay(Today(D(1980, 1, 4), -5), Live(D(1980, 1, 4)));

            Assert.Equal(1, failed.ChallengeEvents.Count);
            Assert.Equal(ChallengeEventKind.Failed, failed.ChallengeEvents[0].Kind);
            Assert.Equal(new[] { "m-000021", "m-000022" }, ports.Removed.ToArray());
            Assert.Equal(ChallengeStatus.Failed, run.ChallengeStates.Get("no-debt").Status);
            Assert.Equal(1, engine.Events.Count("challenge.failed"));
            Assert.Null(failed.Finished, "free play: nothing is lost");
        }

        [Test]
        public void AChallengeThatFailsInsideAScenario_LosesTheScenario_AndKeepsItsRecords()
        {
            ChallengeDefinition noDebt = Challenge("no-debt", 0.25f, Fail("debt", Metric("money.negativeStreakDays", ">=", 2)));
            ScenarioDefinition scenario = Scenario(new[] { Goal("g1", Metric("money", ">=", 1000000000)) });
            RunState run = RunState.StartNew("run-1", D(1980, 1, 1), 2, scenario, new[] { "no-debt" }, Features);
            run.ChallengeStates.Add("no-debt", new[] { "m-000021" });
            var ports = new Ports();
            RunEngine engine = Engine(run, ports, new[] { noDebt });

            engine.OnDay(Today(D(1980, 1, 2), -5), Live(D(1980, 1, 2)));
            RunPassResult failed = engine.OnDay(Today(D(1980, 1, 3), -5), Live(D(1980, 1, 3)));

            Assert.Equal(1, failed.ChallengeEvents.Count);
            Assert.NotNull(failed.Finished);
            Assert.Equal(ScenarioStatus.Lost, run.ScenarioState.Status);
            Assert.Equal("challenge:no-debt", failed.Finished.FailId);
            Assert.Equal("test.fail.debt", failed.Finished.FailTextKey);
            Assert.Equal(0, ports.Removed.Count, "the scenario is over; the modifiers stay as they were");
            Assert.Equal(0, failed.Finished.Score);
        }

        [Test]
        public void AfterTheScenarioIsOver_TheChallengesGoOnAsFreePlay()
        {
            ChallengeDefinition noDebt = Challenge("no-debt", 0.25f, Fail("debt", Metric("money.negativeStreakDays", ">=", 2)));
            ScenarioDefinition scenario = Scenario(new[] { Goal("g1", Metric("money", ">=", 1)) });
            RunState run = RunState.StartNew("run-1", D(1980, 1, 1), 2, scenario, new[] { "no-debt" }, Features);
            run.ChallengeStates.Add("no-debt", new[] { "m-000021" });
            var ports = new Ports();
            RunEngine engine = Engine(run, ports, new[] { noDebt });

            Assert.NotNull(engine.OnDay(Today(D(1980, 1, 2), 5), Live(D(1980, 1, 2))).Finished, "won");
            engine.OnDay(Today(D(1980, 1, 3), -5), Live(D(1980, 1, 3)));
            RunPassResult failed = engine.OnDay(Today(D(1980, 1, 4), -5), Live(D(1980, 1, 4)));

            Assert.Equal(1, failed.ChallengeEvents.Count);
            Assert.Equal(new[] { "m-000021" }, ports.Removed.ToArray(), "D13: the game goes on as free play, the challenge ends like one");
            Assert.Equal(ScenarioStatus.Won, run.ScenarioState.Status, "E8: the win stays");
        }

        [Test]
        public void AChallengePassed_AfterItsYears_IsRecordedInTheProfile_AndOpensTheAchievement()
        {
            ChallengeDefinition expensive = Challenge("expensive-research", 0.15f);
            RunState run = Start(null, "expensive-research");
            run.ChallengeStates.Add("expensive-research", new[] { "m-1" });
            var ports = new Ports();
            RunEngine engine = Engine(run, ports, new[] { expensive }, null, 2);

            Assert.Equal(0, engine.OnDay(Today(D(1981, 12, 31)), Live(D(1981, 12, 31))).ChallengeEvents.Count);
            RunPassResult passed = engine.OnDay(Today(D(1982, 1, 1)), Live(D(1982, 1, 1)));

            Assert.Equal(1, passed.ChallengeEvents.Count);
            Assert.Equal(ChallengeEventKind.Passed, passed.ChallengeEvents[0].Kind);
            Assert.Equal(new[] { "expensive-research:2" }, ports.Passed.ToArray());
            Assert.True(ports.Model.View.IsChallengePassed("expensive-research"));
            Assert.Equal(1, engine.Events.Count("challenge.passed"));
            Assert.True(passed.Achievements.Contains("challenger"));
            Assert.Equal(0, ports.Removed.Count, "a passed challenge keeps its modifiers: they are the game now");
        }

        // ---- releases and races ----

        [Test]
        public void ARaceLostToARival_LosesTheScenarioWithTheNameAndTheDate_AC61()
        {
            RunState run = Start("mhz-race-1990");
            var ports = new Ports();
            RunEngine engine = Engine(run, ports);

            RunPassResult early = engine.OnCpuReleased(ReleasedCpu(70, D(1993, 5, 1), 40000), false, "Intel", D(1993, 5, 1), Live(D(1993, 5, 1)));
            Assert.Equal(0, early.ScenarioEvents.Count, "40 MHz is not the 100 MHz of the race");
            Assert.Equal(ScenarioStatus.Active, run.ScenarioState.Status);

            RunPassResult rival = engine.OnCpuReleased(ReleasedCpu(71, D(1994, 3, 14), 100000), false, "Intel", D(1994, 3, 14), Live(D(1994, 3, 14)));
            Assert.NotNull(rival.Finished);
            Assert.Equal(ScenarioStatus.Lost, run.ScenarioState.Status);
            Assert.Equal("scn.fail.raceLost", rival.Finished.FailTextKey);
            Assert.Equal(new[] { "Intel", "1994-03-14" }, rival.Finished.FailArgs);
            Assert.Equal(2, engine.Events.Count("cpu.released.ai"), "both releases of the rival were counted");
            Assert.Equal(0, engine.Events.Count("cpu.released.player"));
            Assert.Equal(1, ports.Results.Count);
            Assert.Equal("lost", ports.Model.View.History[0].Outcome.ToLowerInvariant());
        }

        [Test]
        public void TheRace_IsWonByThePlayerWhoIsFirst()
        {
            RunState run = Start("mhz-race-1990");
            var ports = new Ports();
            RunEngine engine = Engine(run, ports);

            RunPassResult mine = engine.OnCpuReleased(ReleasedCpu(1, D(1992, 6, 1), 100000), true, "Garage CPU", D(1992, 6, 1), Live(D(1992, 6, 1)));
            Assert.True(HasEvent(mine.ScenarioEvents, ScenarioOutcomeKind.GoalDone, "g1"));
            Assert.True(mine.Achievements.Contains("mhz-100"));
            Assert.Equal(ScenarioStatus.Active, run.ScenarioState.Status);

            RunPassResult rival = engine.OnCpuReleased(ReleasedCpu(2, D(1993, 6, 1), 100000), false, "Intel", D(1993, 6, 1), Live(D(1993, 6, 1)));
            Assert.Equal(0, rival.ScenarioEvents.Count, "the race is decided");
            Assert.Equal(ScenarioStatus.Active, run.ScenarioState.Status);
        }

        [Test]
        public void ACpuThatCameByATransfer_IsNoRelease_NotCounted_NotJudged()
        {
            RunState run = Start(null);
            var ports = new Ports();
            RunEngine engine = Engine(run, ports);

            engine.OnCpuTransferredToPlayer(5);
            RunPassResult release = engine.OnCpuReleased(ReleasedCpu(5, D(1980, 5, 1), 2000000), true, "Garage CPU", D(1980, 5, 1), Live(D(1980, 5, 1)));

            Assert.True(release.IsEmpty);
            Assert.Equal(0, engine.Events.Count("cpu.released.player"));
            Assert.False(ports.Model.View.IsUnlocked("first-cpu"));
            Assert.False(ports.Model.View.IsUnlocked("ghz-1"), "a gigahertz CPU bought from a rival is not the player's gigahertz");
            Assert.True(run.Metrics.TransferredCpuIds.Contains(5), "the id goes into the section of the run");

            // a CPU of the player's own does count
            RunPassResult own = engine.OnCpuReleased(ReleasedCpu(6, D(1980, 6, 1), 3000), true, "Garage CPU", D(1980, 6, 1), Live(D(1980, 6, 1)));
            Assert.True(own.Achievements.Contains("first-cpu"));
        }

        [Test]
        public void ACpuOfARivalThatTheScenarioDoesNotWatch_IsOnlyCounted()
        {
            RunState run = Start(null);
            var ports = new Ports();
            RunEngine engine = Engine(run, ports);
            RunPassResult release = engine.OnCpuReleased(ReleasedCpu(9, D(1980, 5, 1)), false, "AMD", D(1980, 5, 1), Live(D(1980, 5, 1)));
            Assert.True(release.IsEmpty);
            Assert.Equal(1, engine.Events.Count("cpu.released.ai"));
        }

        // ---- bankruptcies ----

        [Test]
        public void ThePlayersBankruptcy_LosesTheScenarioWithItsOwnText_AndIsCounted()
        {
            RunState run = Start("garage-1975");
            var ports = new Ports();
            RunEngine engine = Engine(run, ports);

            RunPassResult result = engine.OnPlayerBankrupt(Live(D(1976, 2, 1)));
            Assert.NotNull(result.Finished);
            Assert.Equal(ScenarioStatus.Lost, run.ScenarioState.Status);
            Assert.Equal("bankrupt", result.Finished.FailId);
            Assert.Equal("scn.fail.bankrupt", result.Finished.FailTextKey);
            Assert.Equal(1, engine.Events.Count("player.bankrupt"));
            Assert.True(ports.Model.View.IsUnlocked("scenario-first") == false, "a loss opens no 'Scenario complete'");
        }

        [Test]
        public void AFreePlayBankruptcy_IsOnlyCounted()
        {
            RunState run = Start(null);
            RunEngine engine = Engine(run, new Ports());
            RunPassResult result = engine.OnPlayerBankrupt(Live(D(1980, 6, 1)));
            Assert.Null(result.Finished);
            Assert.Equal(1, engine.Events.Count("player.bankrupt"));
        }

        [Test]
        public void ARivalThatWentBankrupt_IsCounted_AC123_AsLongAsThePlayerDidNotAbsorbIt()
        {
            RunState run = Start(null);
            var ports = new Ports();
            RunEngine engine = Engine(run, ports);

            RunPassResult absorbed = engine.OnRivalBankrupt(true, Live(D(1985, 1, 1)));
            Assert.True(absorbed.IsEmpty);
            Assert.Equal(0.0, engine.Snapshot.Get("rivals.bankruptCount"));
            Assert.Equal(0, engine.Events.Count("rival.bankrupt"));
            Assert.False(ports.Model.View.IsUnlocked("outlived-rival"));

            RunPassResult counted = engine.OnRivalBankrupt(false, Live(D(1985, 2, 1)));
            Assert.True(counted.IsEmpty, "'Outlived a rival' is a metric of the day: it comes with the next day's pass");
            Assert.Equal(1.0, engine.Snapshot.Get("rivals.bankruptCount"));
            Assert.Equal(1, engine.Events.Count("rival.bankrupt"));
            RunPassResult next = engine.OnDay(Today(D(1985, 2, 2)), Live(D(1985, 2, 2)));
            Assert.True(next.Achievements.Contains("outlived-rival"));
        }

        [Test]
        public void AFoundryContract_IsAnEvent()
        {
            RunState run = Start(null);
            RunEngine engine = Engine(run, new Ports());
            RunPassResult result = engine.OnBusinessContractSigned(Live(D(1990, 1, 1)));
            Assert.True(result.IsEmpty);
            Assert.Equal(1, engine.Events.Count("foundry.signed"));
        }

        // ---- events from outside ----

        [Test]
        public void AnExternalEvent_IsCounted_AndOpensWhatWaitsForIt()
        {
            RunState run = Start(null);
            var ports = new Ports();
            RunEngine engine = Engine(run, ports);

            RunPassResult graduate = engine.OnEvent("onboarding.completed.clean", Live(D(1980, 6, 1)));
            Assert.Equal(new[] { "tutorial-graduate" }, graduate.Achievements.ToArray());

            Assert.True(engine.OnEvent("", Live(D(1980, 6, 1))).IsEmpty);
            Assert.True(engine.OnEvent(null, Live(D(1980, 6, 1))).IsEmpty);
        }

        [Test]
        public void TheEndOfTheEra_NeedsAnUnbankruptRun()
        {
            RunState clean = Start(null);
            var ports = new Ports();
            RunEngine engine = Engine(clean, ports);
            RunPassResult end = engine.OnEvent("endyear.reached", Live(D(2030, 1, 1)));
            Assert.True(end.Achievements.Contains("end-of-era"));

            RunState broke = Start(null);
            var otherPorts = new Ports();
            RunEngine other = Engine(broke, otherPorts);
            other.OnPlayerBankrupt(Live(D(2000, 1, 1)));
            Assert.False(other.OnEvent("endyear.reached", Live(D(2030, 1, 1))).Achievements.Contains("end-of-era"));
        }

        [Test]
        public void TheStartSweep_FindsTheEventsAnEarlierSessionHadCounted()
        {
            RunState run = Start(null);
            run.Metrics.EventCounts["scenario.won"] = 1;   // a load brought the counts back
            var ports = new Ports();
            RunEngine engine = Engine(run, ports);

            RunPassResult sweep = engine.OnStart(Live(D(1985, 1, 1)));
            Assert.True(sweep.Achievements.Contains("scenario-first"));
            Assert.True(engine.OnStart(Live(D(1985, 1, 1))).IsEmpty, "what was earned is not earned twice");
        }

        // ---- the assisted flag and the strict mode ----

        [Test]
        public void AnAssistedRun_EarnsAchievementsMarkedWithHelp_AC152()
        {
            RunState run = Start(null);
            run.MarkAssisted("cheats.enabled");
            var ports = new Ports();
            RunEngine engine = Engine(run, ports);

            RunPassResult release = engine.OnCpuReleased(ReleasedCpu(1, D(1980, 3, 1)), true, "Garage CPU", D(1980, 3, 1), Live(D(1980, 3, 1)));
            Assert.Equal(new[] { "first-cpu" }, release.Achievements.ToArray());
            AchievementEntry entry = ports.Model.View.EntryOf("first-cpu");
            Assert.NotNull(entry);
            Assert.True(entry.Assisted);
            Assert.Equal("Garage CPU", entry.Company);
            Assert.Equal("run-1", entry.RunId);
            Assert.Equal(D(1980, 3, 1), entry.GameDate);
            Assert.Equal(new DateTime(2026, 10, 10, 12, 0, 0, DateTimeKind.Utc), entry.UnlockedAt);
        }

        [Test]
        public void TheStrictMode_EarnsNothingInAnAssistedRun()
        {
            RunState run = Start(null);
            run.MarkAssisted("feature.disabled:event-system");
            var ports = new Ports();
            RunEngine engine = Engine(run, ports);

            RunPassResult release = engine.OnCpuReleased(ReleasedCpu(1, D(1980, 3, 1)), true, "Garage CPU", D(1980, 3, 1), Live(D(1980, 3, 1), strict: true));
            Assert.Equal(0, release.Achievements.Count);
            Assert.False(ports.Model.View.IsUnlocked("first-cpu"));
        }

        [Test]
        public void AnUnreadableProfile_JudgesNothing_AndDoesNotThrow()
        {
            RunState run = Start(null);
            var ports = new Ports { ProfileUnreadable = true };
            RunEngine engine = Engine(run, ports);
            RunPassResult release = engine.OnCpuReleased(ReleasedCpu(1, D(1980, 3, 1)), true, "Garage CPU", D(1980, 3, 1), Live(D(1980, 3, 1)));
            Assert.Equal(0, release.Achievements.Count);
        }

        [Test]
        public void ARefusedUnlock_IsNotReportedNorListedInTheRun()
        {
            RunState run = Start(null);
            var ports = new Ports();
            ports.Model.Unlock("first-cpu", new AchievementEntry { UnlockedAt = DateTime.UtcNow });
            RunEngine engine = Engine(run, ports);

            RunPassResult release = engine.OnCpuReleased(ReleasedCpu(1, D(1980, 3, 1)), true, "Garage CPU", D(1980, 3, 1), Live(D(1980, 3, 1)));
            Assert.Equal(0, release.Achievements.Count, "AC-12.2: no notification for an achievement from another game");
            Assert.Equal(0, run.AchievementsThisRun.Count);
        }

        // ---- the metrics ----

        [Test]
        public void TheDaysOfTheRun_AreCountedFromTheStart_AndTheStreaksGrow()
        {
            RunState run = Start(null);
            RunEngine engine = Engine(run, new Ports());

            for (int day = 1; day <= 5; day++) engine.OnDay(Today(D(1980, 1, 1).AddDays(day), 100), Live(D(1980, 1, 1).AddDays(day)));

            Assert.Equal(5, run.Metrics.RunDays);
            Assert.Equal(5, run.Metrics.SolventStreakDays);
            Assert.Equal(5.0, engine.Snapshot.Get("run.days"));
            Assert.Equal(100.0, engine.Snapshot.Get("money"));
            Assert.Equal(100.0, engine.Snapshot.Get("money.min"));
            Assert.NotNull(engine.LastDay);
        }

        [Test]
        public void ASampleWithoutADate_TakesTheDateOfTheGame()
        {
            RunState run = Start(null);
            RunEngine engine = Engine(run, new Ports());
            engine.OnDay(() => new DaySample { Money = 7 }, Live(D(1980, 1, 11)));
            Assert.Equal(10, run.Metrics.RunDays);
            Assert.Equal(D(1980, 1, 11), engine.LastDay.Date);
        }

        [Test]
        public void TheLastMonthSetByALoad_ShowsInTheSnapshot_WithoutCountingTheStreak()
        {
            RunState run = Start(null);
            RunEngine engine = Engine(run, new Ports());
            engine.SetLastMonth(Month(1980, 3, 500, 100, 5000));
            Assert.Equal(400.0, engine.Snapshot.Get("profit.lastMonth"));
            Assert.Equal(5000.0, engine.Snapshot.Get("revenue.last12m"));
            Assert.Equal(0, run.Metrics.ProfitStreakMonths);
        }

        [Test]
        public void AMonthWithoutAReport_ChangesNothing()
        {
            RunState run = Start(null);
            RunEngine engine = Engine(run, new Ports());
            RunPassResult month = engine.OnMonth(() => new MonthSample { Year = 1980, Month = 1, HasReport = false }, Live(D(1980, 2, 1)));
            Assert.True(month.IsEmpty);
            Assert.Equal(0, run.Metrics.ProfitStreakMonths);
            Assert.False(engine.Snapshot.Has("profit.lastMonth"));
        }

        // ---- save and load ----

        [Test]
        public void AScenarioSavedAndLoadedInTheMiddle_ContinuesToTheSameResult_AC161()
        {
            RunState run = Start("garage-1975");
            var ports = new Ports();
            RunEngine engine = Engine(run, ports);
            engine.OnCpuReleased(ReleasedCpu(1, D(1975, 9, 1)), true, "Garage CPU", D(1975, 9, 1), Live(D(1975, 9, 1)));
            engine.OnMonth(LastMonth(1975, 10, 20000, 8000), Live(D(1975, 11, 1)));
            for (int i = 1; i <= 3; i++) engine.OnDay(Today(D(1975, 11, 1).AddDays(i), 1000000), Live(D(1975, 11, 1).AddDays(i)));

            // 10 cycles of save and load: the statuses of the goals do not drift
            RunState current = run;
            RunEngine currentEngine = engine;
            for (int cycle = 0; cycle < 10; cycle++)
            {
                JsonObject section = RunSaveCodec.Write(current);
                Assert.NotNull(section);
                current = RunSaveCodec.Read(section, new List<string>());
                Assert.NotNull(current);
                Assert.Equal(GoalStatus.Done, current.ScenarioState.Goals["g1"].Status);
                Assert.Equal(GoalStatus.Done, current.ScenarioState.Goals["g2"].Status);
                Assert.Equal(GoalStatus.Open, current.ScenarioState.Goals["g3"].Status);
                currentEngine = Engine(current, ports);
            }

            RunPassResult oem = currentEngine.OnOemSigned(Live(D(1976, 3, 1)));
            RunPassResult last = currentEngine.OnDay(Today(D(1977, 1, 1), 6000000), Live(D(1977, 1, 1)));
            Assert.NotNull(last.Finished);
            Assert.Equal(600, last.Finished.Score, "the same AC-8.1 result as without the saves");
            Assert.Equal("silver", last.Finished.Medal);
            Assert.True(current.ScenarioState.Status == ScenarioStatus.Won);
        }

        [Test]
        public void ALegacyRun_JudgesOnlyWhatDoesNotNeedTheWholeHistory()
        {
            RunState legacy = RunState.CreateLegacy("run-legacy", D(2029, 1, 1), 2, false, Features);
            var ports = new Ports();
            RunEngine engine = Engine(legacy, ports);

            Assert.Null(engine.Scenario);
            RunPassResult end = engine.OnEvent("endyear.reached", Live(D(2030, 1, 1)));
            Assert.False(end.Achievements.Contains("end-of-era"), "countsOnlyNewGames: the history of a legacy run is unknown");

            RunPassResult release = engine.OnCpuReleased(ReleasedCpu(1, D(2029, 6, 1)), true, "Garage CPU", D(2029, 6, 1), Live(D(2029, 6, 1)));
            Assert.True(release.Achievements.Contains("first-cpu"));
        }

        // ---- construction ----

        [Test]
        public void TheEngine_NeedsARun_AnEvaluator_AndPorts()
        {
            var evaluator = new AchievementEvaluator(Standard);
            Assert.Throws<ArgumentNullException>(() => new RunEngine(null, null, evaluator, null, new Ports()));
            Assert.Throws<ArgumentNullException>(() => new RunEngine(Start(null), null, null, null, new Ports()));
            Assert.Throws<ArgumentNullException>(() => new RunEngine(Start(null), null, evaluator, null, null));
        }

        [Test]
        public void AFreePlayRun_HasNoScenarioRuntime_AndAScenarioRunHasOne()
        {
            Assert.Null(Engine(Start(null), new Ports()).Scenario);
            RunEngine engine = Engine(Start("garage-1975"), new Ports());
            Assert.NotNull(engine.Scenario);
            Assert.Equal("garage-1975", engine.Scenario.Definition.Id);
            Assert.Equal(0, engine.ChallengeDefinitions.Count);
        }

        [Test]
        public void TheStateOnlyMetrics_AreThereBeforeTheFirstDay()
        {
            RunEngine engine = Engine(Start(null), new Ports());
            Assert.True(engine.Snapshot.Has("sales.total"));
            Assert.False(engine.Snapshot.Has("money"), "the metrics of the day need a day");
        }
    }
}
