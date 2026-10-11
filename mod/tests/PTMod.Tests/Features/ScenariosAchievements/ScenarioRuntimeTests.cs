using System;
using System.Collections.Generic;
using PTMod.Features.ScenariosAchievements.Logic;
using static PTMod.Tests.Features.ScenariosAchievements.RunTestData;

namespace PTMod.Tests.Features.ScenariosAchievements
{
    /// <summary>
    /// TSK-009: the course of a scenario - a win by the required goals before the deadline (AC-8.1 end to end), a loss by the deadline ("time is
    /// out"), by bankruptcy, by a fail rule (with <c>holdDays</c>), by a required goal that failed (AC-5.2) and by a lost race (AC-6.1); the
    /// events for the notifications; and after the result nothing changes any more (E8).
    /// </summary>
    [Covers(typeof(ScenarioRuntime), typeof(ScenarioState), typeof(ScenarioStatus), typeof(ScenarioResult), typeof(ScenarioOutcomeEvent),
        typeof(ScenarioOutcomeKind))]
    public class ScenarioRuntimeTests
    {
        private static ScenarioRuntime Start(string scenarioId, out EventBook events, IList<ChallengeDefinition> challenges = null)
        {
            ScenarioDefinition definition = ScenarioTestData.Scenario(scenarioId);
            events = Book();
            return Runtime(definition, D(definition.Start.Year, 1, 1), null, challenges);
        }

        private static ScenarioOutcomeEvent Find(IList<ScenarioOutcomeEvent> events, ScenarioOutcomeKind kind, string goalId = null)
        {
            foreach (ScenarioOutcomeEvent e in events)
            {
                if (e.Kind == kind && (goalId == null || e.GoalId == goalId)) return e;
            }
            Assert.Fail("no " + kind + " " + goalId + " among " + events.Count + " events");
            return null;
        }

        // ---- a win ----

        [Test]
        public void GarageScenario_IsWonOnTheLastGoal_WithTheScoreOfAC81()
        {
            EventBook events;
            ScenarioRuntime runtime = Start("garage-1975", out events);

            // g1: the first CPU, long before 1976-07-01
            CpuInfo first = Cpu(1, 3000, releasedOn: D(1975, 9, 1));
            events.RecordCpuRelease(first, true);
            IList<ScenarioOutcomeEvent> released = runtime.OnCpuReleased(first, true, "Garage CPU", D(1975, 9, 1), events);
            Assert.Equal(1, released.Count);
            Assert.Equal(ScenarioOutcomeKind.GoalDone, released[0].Kind);
            Assert.Equal("g1", released[0].GoalId);
            Assert.Equal(100, released[0].Points);

            // g2: a profitable month (monthly cadence)
            IList<ScenarioOutcomeEvent> month = runtime.EvaluateMonth(Snap("profit.lastMonth", 12000), D(1975, 11, 1), events);
            Assert.True(HasEvent(month, ScenarioOutcomeKind.GoalDone, "g2"));
            Assert.Equal(ScenarioStatus.Active, runtime.Status);

            // g3 and g4 on the same day, 1977-01-01: the last required goal wins
            IList<ScenarioOutcomeEvent> last = runtime.EvaluateDay(Snap("money", 6000000, "oem.signed.total", 1), D(1977, 1, 1), events);
            Assert.Equal(3, last.Count, "g3, g4, the win");
            Assert.Equal(ScenarioOutcomeKind.GoalDone, last[0].Kind);
            Assert.Equal(ScenarioOutcomeKind.GoalDone, last[1].Kind);
            Assert.Equal(ScenarioOutcomeKind.Won, last[2].Kind);

            Assert.Equal(ScenarioStatus.Won, runtime.Status);
            ScenarioResult result = runtime.Result;
            Assert.NotNull(result);
            Assert.True(result.IsWin);
            Assert.Equal(D(1977, 1, 1), result.FinishedOn);
            Assert.Equal(500, result.Breakdown.GoalPoints);
            Assert.Equal(100, result.Breakdown.TimeBonus, "round(300 × 365 / 1096)");
            Assert.Equal(600, result.Score);
            Assert.Equal("silver", result.Medal);
            Assert.Null(result.FailId);
            Assert.True(ReferenceEquals(result, last[2].Result), "the same object");
        }

        [Test]
        public void TheWin_BringsOnlyTheRequiredGoals_AndSomeOptionalOnesLeftOpenScoreLess()
        {
            EventBook events;
            ScenarioRuntime runtime = Start("garage-1975", out events);
            CpuInfo cpu = Cpu(1, 3000, releasedOn: D(1975, 3, 1));
            events.RecordCpuRelease(cpu, true);
            runtime.OnCpuReleased(cpu, true, "Me", D(1975, 3, 1), events);
            runtime.EvaluateMonth(Snap("profit.lastMonth", 1), D(1975, 4, 1), events);

            IList<ScenarioOutcomeEvent> last = runtime.EvaluateDay(Snap("money", 5000000, "oem.signed.total", 0), D(1976, 1, 1), events);
            Assert.True(HasEvent(last, ScenarioOutcomeKind.Won));
            Assert.Equal(ScenarioStatus.Won, runtime.Status);
            Assert.Equal(GoalStatus.Open, runtime.State.GoalOf("g4").Status, "the optional goal was not reached");
            Assert.Equal(400, runtime.Result.Breakdown.GoalPoints);
            // 1976-01-01: 731 days left out of 1096 -> round(300 * 731 / 1096) = 200
            Assert.Equal(200, runtime.Result.Breakdown.TimeBonus);
            Assert.Equal(600, runtime.Result.Score);
        }

        [Test]
        public void AGoalDoneThatDay_ButAnotherRequiredOne_KeepsTheScenarioGoing()
        {
            EventBook events;
            ScenarioRuntime runtime = Start("pc-boom-1980", out events);
            IList<ScenarioOutcomeEvent> day = runtime.EvaluateDay(Snap("share.midrange.avg30", 0.152, "oem.signed.total", 0), D(1980, 6, 1), events);
            Assert.Equal(1, day.Count);
            Assert.Equal("g1", day[0].GoalId);
            Assert.Equal(300, day[0].Points, "+300 on the day the 30-day share reached 15% (AC-5.1)");
            Assert.Equal(D(1980, 6, 1), day[0].Date);
            Assert.Equal(ScenarioStatus.Active, runtime.Status);
            Assert.Null(runtime.Result);
        }

        [Test]
        public void ADoneGoal_IsReportedOnce()
        {
            EventBook events;
            ScenarioRuntime runtime = Start("pc-boom-1980", out events);
            MetricsSnapshot m = Snap("share.midrange.avg30", 0.2);
            Assert.Equal(1, runtime.EvaluateDay(m, D(1980, 6, 1), events).Count);
            Assert.Equal(0, runtime.EvaluateDay(m, D(1980, 6, 2), events).Count);
        }

        [Test]
        public void AMonthlyGoal_IsCheckedInTheMonthPass_NotInTheDailyOne()
        {
            EventBook events;
            ScenarioRuntime runtime = Start("pc-boom-1980", out events); // g3: profit.streakMonths >= 6, monthly
            MetricsSnapshot m = Snap("profit.streakMonths", 6);

            Assert.False(HasEvent(runtime.EvaluateDay(m, D(1980, 8, 1), events), ScenarioOutcomeKind.GoalDone, "g3"));
            Assert.Equal(GoalStatus.Open, runtime.State.GoalOf("g3").Status);
            Assert.True(HasEvent(runtime.EvaluateMonth(m, D(1980, 8, 1), events), ScenarioOutcomeKind.GoalDone, "g3"));
        }

        [Test]
        public void TheMonthPass_NeverEvaluatesTheDeadlineOrTheFailRules()
        {
            EventBook events;
            ScenarioRuntime runtime = Start("fabless-1985", out events);
            IList<ScenarioOutcomeEvent> month = runtime.EvaluateMonth(Snap("factory.lines", 5), D(1991, 6, 1), events);
            Assert.Equal(0, month.Count, "those are decided by the daily pass");
            Assert.Equal(ScenarioStatus.Active, runtime.Status);
        }

        // ---- the deadline ----

        [Test]
        public void TheDeadline_LosesTheScenario_TimeIsOut()
        {
            EventBook events;
            ScenarioRuntime runtime = Start("pc-boom-1980", out events);
            Assert.Equal(0, runtime.EvaluateDay(Snap(), D(1985, 12, 31), events).Count, "the day before the deadline");
            Assert.Equal(ScenarioStatus.Active, runtime.Status);

            IList<ScenarioOutcomeEvent> end = runtime.EvaluateDay(Snap(), D(1986, 1, 1), events);
            Assert.Equal(1, end.Count);
            Assert.Equal(ScenarioOutcomeKind.Lost, end[0].Kind);
            Assert.Equal("scn.fail.deadline", end[0].TextKey);

            Assert.Equal(ScenarioStatus.Lost, runtime.Status);
            Assert.Equal(0, runtime.Result.Score);
            Assert.Equal("none", runtime.Result.Medal);
            Assert.Equal("deadline", runtime.Result.FailId);
            Assert.Equal("scn.fail.deadline", runtime.Result.FailTextKey);
            Assert.Equal("deadline", runtime.State.Fail.TriggeredId);
            Assert.Equal(D(1986, 1, 1), runtime.State.Fail.TriggeredOn.Value);
            Assert.False(runtime.Result.IsWin);
        }

        [Test]
        public void OnTheDeadlineDay_TheLastRequiredGoalStillWins()
        {
            EventBook events;
            ScenarioRuntime runtime = Start("pc-boom-1980", out events);
            runtime.EvaluateMonth(Snap("profit.streakMonths", 6), D(1985, 6, 1), events);

            IList<ScenarioOutcomeEvent> end = runtime.EvaluateDay(Snap("share.midrange.avg30", 0.2, "oem.signed.total", 3), D(1986, 1, 1), events);
            Assert.True(HasEvent(end, ScenarioOutcomeKind.Won), "goals are decided before the deadline is");
            Assert.False(HasEvent(end, ScenarioOutcomeKind.Lost));
            Assert.Equal(0, runtime.Result.Breakdown.DaysLeft);
            Assert.Equal(0, runtime.Result.Breakdown.TimeBonus);
            Assert.Equal(700, runtime.Result.Score);
            Assert.Equal("bronze", runtime.Result.Medal);
        }

        [Test]
        public void ADayLongAfterTheDeadline_StillLosesOnce()
        {
            EventBook events;
            ScenarioRuntime runtime = Start("pc-boom-1980", out events);
            Assert.True(HasEvent(runtime.EvaluateDay(Snap(), D(1990, 1, 1), events), ScenarioOutcomeKind.Lost), "the game was fast-forwarded past it");
            Assert.Equal(0, runtime.EvaluateDay(Snap(), D(1990, 1, 2), events).Count);
        }

        [Test]
        public void ADefinitionWithoutRequiredGoals_NeverWins_OnlyTheDeadlineEndsIt()
        {
            ScenarioDefinition definition = Scenario(new[] { Goal("g1", Metric("money", ">=", 1), required: false) });
            ScenarioRuntime runtime = Runtime(definition, D(1980, 1, 1));
            Assert.True(HasEvent(runtime.EvaluateDay(Snap("money", 5), D(1980, 2, 1), Book()), ScenarioOutcomeKind.GoalDone, "g1"));
            Assert.Equal(ScenarioStatus.Active, runtime.Status);
            Assert.True(HasEvent(runtime.EvaluateDay(Snap("money", 5), D(1982, 1, 1), Book()), ScenarioOutcomeKind.Lost));
        }

        // ---- bankruptcy ----

        [Test]
        public void TheBankruptcyOfThePlayer_LosesTheScenario_WithTheTextOfTheScenarioRule()
        {
            EventBook events;
            ScenarioRuntime runtime = Start("comeback-1985", out events);
            IList<ScenarioOutcomeEvent> lost = runtime.OnPlayerBankrupt(D(1986, 3, 1));

            Assert.Equal(1, lost.Count);
            Assert.Equal(ScenarioOutcomeKind.Lost, lost[0].Kind);
            Assert.Equal("scn.fail.bankrupt", lost[0].TextKey);
            Assert.Equal(ScenarioStatus.Lost, runtime.Status);
            Assert.Equal("bankrupt", runtime.Result.FailId);
            Assert.Equal(0, runtime.Result.Score);
        }

        [Test]
        public void TheBankruptcyRule_AlsoWorksAsAnEventOnTheDailyPass()
        {
            EventBook events;
            ScenarioRuntime runtime = Start("foundry-1990", out events);
            events.Record("player.bankrupt");
            IList<ScenarioOutcomeEvent> day = runtime.EvaluateDay(Snap(), D(1991, 1, 1), events);
            Assert.True(HasEvent(day, ScenarioOutcomeKind.Lost));
            Assert.Equal("bankrupt", runtime.Result.FailId);
            Assert.Equal("scn.fail.bankrupt", runtime.Result.FailTextKey);
        }

        [Test]
        public void ADefinitionWithoutABankruptcyRule_StillLosesOnBankruptcy()
        {
            ScenarioDefinition definition = Scenario(new[] { Goal("g1", Metric("money", ">=", 1)) });
            ScenarioRuntime runtime = Runtime(definition, D(1980, 1, 1));
            runtime.OnPlayerBankrupt(D(1980, 5, 5));
            Assert.Equal("scn.fail.bankrupt", runtime.Result.FailTextKey);
            Assert.Equal("bankrupt", runtime.Result.FailId);
        }

        // ---- fail rules ----

        [Test]
        public void TheFablessRule_LosesAtTheFirstProductionLine_AC71()
        {
            EventBook events;
            ScenarioRuntime runtime = Start("fabless-1985", out events);
            Assert.Equal(0, runtime.EvaluateDay(Snap("factory.lines", 0), D(1985, 3, 1), events).Count);

            IList<ScenarioOutcomeEvent> day = runtime.EvaluateDay(Snap("factory.lines", 1), D(1985, 3, 2), events);
            Assert.Equal(1, day.Count);
            Assert.Equal("scn.fail.fablessRule", day[0].TextKey);
            Assert.Equal("fabless-rule", runtime.Result.FailId);
        }

        [Test]
        public void TheDebtRule_LosesBelowEightyMillion_AC72()
        {
            EventBook events;
            ScenarioRuntime runtime = Start("comeback-1985", out events);
            Assert.Equal(0, runtime.EvaluateDay(Snap("money", -80000000), D(1985, 3, 1), events).Count, "exactly -$80M is not below it");

            IList<ScenarioOutcomeEvent> day = runtime.EvaluateDay(Snap("money", -80000001), D(1985, 3, 2), events);
            Assert.True(HasEvent(day, ScenarioOutcomeKind.Lost));
            Assert.Equal("scn.fail.debtLimit", runtime.Result.FailTextKey);
        }

        [Test]
        public void AFailRuleWithHoldDays_NeedsTheDaysInARow()
        {
            FailSpec debt = Fail("debt", Metric("money", "<", 0, holdDays: 3));
            ScenarioDefinition definition = Scenario(new[] { Goal("g1", Metric("share.total.avg30", ">=", 0.9)) }, new[] { debt });
            ScenarioRuntime runtime = Runtime(definition, D(1980, 1, 1));
            EventBook events = Book();

            Assert.Equal(0, runtime.EvaluateDay(Snap("money", -1), D(1980, 2, 1), events).Count);
            Assert.Equal(0, runtime.EvaluateDay(Snap("money", -1), D(1980, 2, 2), events).Count);
            Assert.Equal(0, runtime.EvaluateDay(Snap("money", 1), D(1980, 2, 3), events).Count, "a day above breaks it");
            Assert.Equal(0, runtime.EvaluateDay(Snap("money", -1), D(1980, 2, 4), events).Count);
            Assert.Equal(0, runtime.EvaluateDay(Snap("money", -1), D(1980, 2, 5), events).Count);
            Assert.Equal(2, runtime.State.Fail.HoldOf("debt"));

            IList<ScenarioOutcomeEvent> third = runtime.EvaluateDay(Snap("money", -1), D(1980, 2, 6), events);
            Assert.True(HasEvent(third, ScenarioOutcomeKind.Lost));
            Assert.Equal("test.fail.debt", runtime.Result.FailTextKey);
            Assert.Equal("debt", runtime.Result.FailId);
        }

        [Test]
        public void AFailRuleOnACondition_UsesTheAdapter()
        {
            var conditions = new FakeConditions { Answer = false };
            ScenarioDefinition definition = Scenario(new[] { Goal("g1", Metric("share.total.avg30", ">=", 0.9)) },
                new[] { Fail("rule", Condition()) });
            ScenarioRuntime runtime = Runtime(definition, D(1980, 1, 1), conditions);
            EventBook events = Book();

            Assert.Equal(0, runtime.EvaluateDay(Snap(), D(1980, 2, 1), events).Count);
            conditions.Answer = true;
            Assert.True(HasEvent(runtime.EvaluateDay(Snap(), D(1980, 2, 2), events), ScenarioOutcomeKind.Lost));
        }

        [Test]
        public void ALossRule_BeatsAWinOfTheSameDay()
        {
            ScenarioDefinition definition = Scenario(
                new[] { Goal("g1", Metric("money", ">=", 100)) },
                new[] { Fail("lines", Metric("factory.lines", ">", 0)) });
            ScenarioRuntime runtime = Runtime(definition, D(1980, 1, 1));
            IList<ScenarioOutcomeEvent> day = runtime.EvaluateDay(Snap("money", 500, "factory.lines", 3), D(1980, 2, 1), Book());
            Assert.True(HasEvent(day, ScenarioOutcomeKind.GoalDone, "g1"), "the goal was reached");
            Assert.True(HasEvent(day, ScenarioOutcomeKind.Lost));
            Assert.False(HasEvent(day, ScenarioOutcomeKind.Won));
            Assert.Equal(0, runtime.Result.Score);
        }

        // ---- goals that fail ----

        [Test]
        public void ARequiredGoalThatMissesItsDate_LosesTheScenario_AC52()
        {
            EventBook events;
            ScenarioRuntime runtime = Start("garage-1975", out events); // g1: a CPU by 1976-07-01
            Assert.Equal(0, runtime.EvaluateDay(Snap("money", 100), D(1976, 6, 30), events).Count);

            IList<ScenarioOutcomeEvent> day = runtime.EvaluateDay(Snap("money", 100), D(1976, 7, 1), events);
            Assert.Equal(2, day.Count);
            Assert.Equal(ScenarioOutcomeKind.GoalFailed, day[0].Kind);
            Assert.Equal("g1", day[0].GoalId);
            Assert.Equal(ScenarioOutcomeKind.Lost, day[1].Kind);
            Assert.Equal("scn.fail.goal", day[1].TextKey);
            Assert.Equal("goal:g1", runtime.Result.FailId);
            Assert.Equal(1, runtime.Result.FailArgs.Length);
            Assert.Equal("scn.garage-1975.g1", runtime.Result.FailArgs[0], "the title of the goal, to be put into the text");
        }

        [Test]
        public void AnOptionalGoalThatMissesItsDate_OnlyFailsTheGoal()
        {
            ScenarioDefinition definition = Scenario(new[]
            {
                Goal("g1", Metric("money", ">=", 100)),
                Goal("g2", Metric("money", ">=", 1000000), required: false, by: "1980-06-01"),
            });
            ScenarioRuntime runtime = Runtime(definition, D(1980, 1, 1));
            IList<ScenarioOutcomeEvent> day = runtime.EvaluateDay(Snap("money", 5), D(1980, 6, 1), Book());

            Assert.Equal(1, day.Count);
            Assert.Equal(ScenarioOutcomeKind.GoalFailed, day[0].Kind);
            Assert.Equal("g2", day[0].GoalId);
            Assert.Equal(ScenarioStatus.Active, runtime.Status);
            Assert.Equal(GoalStatus.Failed, runtime.State.GoalOf("g2").Status);
        }

        [Test]
        public void AFailedGoalByDate_StillCountsNothing_WhenTheScenarioIsWonLater()
        {
            ScenarioDefinition definition = Scenario(new[]
            {
                Goal("g1", Metric("money", ">=", 100), points: 100),
                Goal("g2", Metric("money", ">=", 1000000), required: false, points: 50, by: "1980-06-01"),
            });
            ScenarioRuntime runtime = Runtime(definition, D(1980, 1, 1));
            runtime.EvaluateDay(Snap("money", 5), D(1980, 6, 1), Book());
            runtime.EvaluateDay(Snap("money", 5000000), D(1980, 7, 1), Book()); // g1 done; g2 is failed for good
            Assert.Equal(ScenarioStatus.Won, runtime.Status);
            Assert.Equal(100, runtime.Result.Breakdown.GoalPoints);
        }

        // ---- races ----

        [Test]
        public void ALostRace_LosesTheScenario_WithTheWinnerAndTheDate_AC61()
        {
            EventBook events;
            ScenarioRuntime runtime = Start("mhz-race-1990", out events);
            CpuInfo rival = Cpu(50, 100000);
            events.RecordCpuRelease(rival, false);

            IList<ScenarioOutcomeEvent> lost = runtime.OnCpuReleased(rival, false, "Inlet", D(1994, 3, 14), events);
            Assert.Equal(2, lost.Count);
            Assert.Equal(ScenarioOutcomeKind.GoalFailed, lost[0].Kind);
            Assert.Equal("g1", lost[0].GoalId);
            Assert.Equal(ScenarioOutcomeKind.Lost, lost[1].Kind);
            Assert.Equal("scn.fail.raceLost", lost[1].TextKey);
            Assert.Equal(new[] { "Inlet", "1994-03-14" }, lost[1].TextArgs);
            Assert.Equal("race:g1", runtime.Result.FailId);
            Assert.Equal(ScenarioStatus.Lost, runtime.Status);
        }

        [Test]
        public void AWonRace_DoneTheGoal_AndTheScenarioGoesOn()
        {
            EventBook events;
            ScenarioRuntime runtime = Start("mhz-race-1990", out events);
            CpuInfo mine = Cpu(1, 100000);
            events.RecordCpuRelease(mine, true);

            IList<ScenarioOutcomeEvent> won = runtime.OnCpuReleased(mine, true, "Garage CPU", D(1993, 8, 1), events);
            Assert.Equal(1, won.Count);
            Assert.Equal(ScenarioOutcomeKind.GoalDone, won[0].Kind);
            Assert.Equal(400, won[0].Points);
            Assert.Equal(D(1993, 8, 1), runtime.State.GoalOf("g1").DoneOn.Value);

            // a rival catching up later changes nothing
            CpuInfo late = Cpu(60, 120000);
            events.RecordCpuRelease(late, false);
            Assert.Equal(0, runtime.OnCpuReleased(late, false, "Inlet", D(1993, 9, 1), events).Count);
            Assert.Equal(ScenarioStatus.Active, runtime.Status);
        }

        [Test]
        public void ARaceReleaseBeforeTheStartOfTheRun_IsNotPartOfIt()
        {
            EventBook events;
            ScenarioRuntime runtime = Start("mhz-race-1990", out events);
            CpuInfo old = Cpu(50, 120000);
            Assert.Equal(0, runtime.OnCpuReleased(old, false, "Inlet", D(1989, 12, 31), events).Count);
            Assert.Equal(ScenarioStatus.Active, runtime.Status);
            Assert.Equal(RaceStatus.Open, runtime.State.Races.StatusOf("g1"));
        }

        [Test]
        public void ALostRaceWithoutFailOnLose_FailsTheGoal_ButOnlyARequiredGoalLosesTheScenario()
        {
            GoalSpec optional = Goal("race", Race(new CpuFilter { MinFrequency = 100 }, failOnLose: false), required: false);
            GoalSpec other = Goal("g2", Metric("money", ">=", 99999999));
            ScenarioRuntime runtime = Runtime(Scenario(new[] { other, optional }), D(1980, 1, 1));
            EventBook events = Book();
            IList<ScenarioOutcomeEvent> lost = runtime.OnCpuReleased(Cpu(9, 500), false, "Rival", D(1980, 5, 5), events);
            Assert.Equal(1, lost.Count);
            Assert.Equal(ScenarioOutcomeKind.GoalFailed, lost[0].Kind);
            Assert.Equal(ScenarioStatus.Active, runtime.Status);

            GoalSpec required = Goal("race", Race(new CpuFilter { MinFrequency = 100 }, failOnLose: false), required: true);
            ScenarioRuntime strict = Runtime(Scenario(new[] { required }), D(1980, 1, 1));
            IList<ScenarioOutcomeEvent> lostStrict = strict.OnCpuReleased(Cpu(9, 500), false, "Rival", D(1980, 5, 5), events);
            Assert.True(HasEvent(lostStrict, ScenarioOutcomeKind.Lost));
            Assert.Equal("scn.fail.goal", strict.Result.FailTextKey, "a required goal that can no longer be done");
        }

        [Test]
        public void ATransferredCpu_IsNotARelease_ForTheRaceOrAnyGoal()
        {
            var metrics = new MetricsState();
            MetricsCalculator.OnCpuTransferred(metrics, 77);
            EventBook events = Book(metrics);
            ScenarioDefinition definition = ScenarioTestData.Scenario("mhz-race-1990");
            ScenarioRuntime runtime = Runtime(definition, D(1990, 1, 1));

            CpuInfo absorbed = Cpu(77, 500000);
            Assert.Equal(0, runtime.OnCpuReleased(absorbed, true, "Garage CPU", D(1991, 1, 1), events).Count);
            Assert.Equal(RaceStatus.Open, runtime.State.Races.StatusOf("g1"), "taking over a rival's CPU does not win the race");
        }

        [Test]
        public void ACpuGoal_IsSettledAtTheMomentOfTheRelease()
        {
            EventBook events;
            ScenarioRuntime runtime = Start("ghz-race-1995", out events); // g3: a CPU with SMT (optional)
            CpuInfo smt = Cpu(1, 300000, smt: true);
            events.RecordCpuRelease(smt, true);
            IList<ScenarioOutcomeEvent> day = runtime.OnCpuReleased(smt, true, "Garage CPU", D(1996, 2, 2), events);
            Assert.True(HasEvent(day, ScenarioOutcomeKind.GoalDone, "g3"));
            Assert.Equal(100, Find(day, ScenarioOutcomeKind.GoalDone, "g3").Points);
        }

        // ---- the result is final (E8) ----

        [Test]
        public void AfterAWin_NothingChangesAnyMore()
        {
            EventBook events;
            ScenarioRuntime runtime = Start("pc-boom-1980", out events);
            runtime.EvaluateMonth(Snap("profit.streakMonths", 6), D(1982, 1, 1), events);
            runtime.EvaluateDay(Snap("share.midrange.avg30", 0.2, "oem.signed.total", 3), D(1982, 1, 2), events);
            Assert.Equal(ScenarioStatus.Won, runtime.Status);
            ScenarioResult result = runtime.Result;
            int score = result.Score;

            Assert.Equal(0, runtime.OnPlayerBankrupt(D(1983, 1, 1)).Count, "E8: the result is recorded, the later bankruptcy is not a loss");
            Assert.Equal(0, runtime.EvaluateDay(Snap("money", -999999999), D(1990, 1, 1), events).Count, "not even past the deadline");
            Assert.Equal(0, runtime.EvaluateMonth(Snap("profit.streakMonths", 0), D(1990, 2, 1), events).Count);
            Assert.Equal(0, runtime.Lose("x", "y", null, D(1990, 3, 1)).Count);
            Assert.Equal(0, runtime.OnCpuReleased(Cpu(1, 1), true, "Me", D(1990, 4, 1), events).Count);
            Assert.Equal(ScenarioStatus.Won, runtime.Status);
            Assert.True(ReferenceEquals(result, runtime.Result), "the same object");
            Assert.Equal(score, runtime.Result.Score);
            Assert.False(runtime.State.Fail.IsTriggered);
        }

        [Test]
        public void AfterALoss_ReachingTheGoalsDoesNotWin()
        {
            EventBook events;
            ScenarioRuntime runtime = Start("pc-boom-1980", out events);
            runtime.OnPlayerBankrupt(D(1980, 6, 1));
            Assert.Equal(ScenarioStatus.Lost, runtime.Status);

            runtime.EvaluateMonth(Snap("profit.streakMonths", 6), D(1982, 1, 1), events);
            Assert.Equal(0, runtime.EvaluateDay(Snap("share.midrange.avg30", 0.5, "oem.signed.total", 9), D(1982, 1, 2), events).Count);
            Assert.Equal(ScenarioStatus.Lost, runtime.Status);
            Assert.Equal(0, runtime.Result.Score);
            Assert.Equal(GoalStatus.Open, runtime.State.GoalOf("g1").Status, "the goals no longer move");
            Assert.Equal(D(1980, 6, 1), runtime.State.Fail.TriggeredOn.Value);
        }

        [Test]
        public void AScenarioThatIsNotActive_DoesNothing()
        {
            ScenarioDefinition definition = ScenarioTestData.Scenario("pc-boom-1980");
            var state = new ScenarioState { Id = definition.Id }; // Status None: no scenario in this run
            ScenarioRuntime runtime = Runtime(definition, D(1980, 1, 1), null, null, state);
            Assert.Equal(ScenarioStatus.None, runtime.Status);
            Assert.Equal(0, runtime.EvaluateDay(Snap("share.midrange.avg30", 1), D(1980, 2, 1), Book()).Count);
            Assert.Equal(0, runtime.EvaluateMonth(Snap(), D(1980, 2, 1), Book()).Count);
            Assert.Equal(0, runtime.OnPlayerBankrupt(D(1980, 2, 1)).Count);
            Assert.Equal(0, runtime.OnCpuReleased(Cpu(1, 1), true, "Me", D(1980, 2, 1), Book()).Count);
            Assert.Equal(ScenarioStatus.None, runtime.Status);
        }

        [Test]
        public void TheResult_NamesTheInternalEventsOfTheAchievements()
        {
            var silver = new ScenarioResult { Outcome = ScenarioStatus.Won, Medal = "silver" };
            Assert.Equal(new[] { "scenario.won" }, silver.EventIds());
            var gold = new ScenarioResult { Outcome = ScenarioStatus.Won, Medal = "gold" };
            Assert.Equal(new[] { "scenario.won", "scenario.gold" }, gold.EventIds());
            var lost = new ScenarioResult { Outcome = ScenarioStatus.Lost, Medal = ScoreCalculator.MedalNone };
            Assert.Equal(new[] { "scenario.lost" }, lost.EventIds());
            Assert.Equal(0, new ScenarioResult { Outcome = ScenarioStatus.Active }.EventIds().Length);
            foreach (ScenarioResult result in new[] { silver, gold, lost })
            {
                foreach (string id in result.EventIds()) Assert.True(ContentVocabulary.IsKnownEvent(id), id);
            }
        }

        // ---- the score with challenges, the caller's own loss ----

        [Test]
        public void TheBonusesOfTheChallenges_AreAddedToTheScoreOfTheWin()
        {
            ScenarioDefinition definition = Scenario(new[] { Goal("g1", Metric("money", ">=", 1), points: 100) });
            var challenges = new List<ChallengeDefinition> { Challenge("a", 0.15f), Challenge("b", 0.10f) };
            ScenarioRuntime runtime = Runtime(definition, D(1980, 1, 1), null, challenges);

            runtime.EvaluateDay(Snap("money", 5), D(1980, 1, 2), Book());
            Assert.Equal(ScenarioStatus.Won, runtime.Status);
            // 731 days, 730 left: timeBonus round(100 * 730 / 731) = 100 -> (100 + 100) * 1.25
            Assert.Equal(25, runtime.Result.Breakdown.ChallengeBonusPercent);
            Assert.Equal(250, runtime.Result.Score);
            Assert.Equal("gold", runtime.Result.Medal);
        }

        [Test]
        public void TheCaller_CanLoseTheScenario_ForItsOwnReason()
        {
            EventBook events;
            ScenarioRuntime runtime = Start("pc-boom-1980", out events);
            IList<ScenarioOutcomeEvent> lost = runtime.Lose("challenge:no-oem", "chl.no-oem.fail", new[] { "x" }, D(1981, 1, 1));
            Assert.Equal(1, lost.Count);
            Assert.Equal("challenge:no-oem", runtime.Result.FailId);
            Assert.Equal("chl.no-oem.fail", runtime.Result.FailTextKey);
            Assert.Equal(new[] { "x" }, runtime.Result.FailArgs);
            Assert.Equal(0, runtime.Lose("again", "again", null, D(1981, 1, 2)).Count);
        }

        [Test]
        public void Progress_OfAGoal_IsAvailableToTheTracker()
        {
            EventBook events;
            ScenarioRuntime runtime = Start("pc-boom-1980", out events);
            GoalSpec g1 = runtime.Definition.FindGoal("g1");
            Assert.Near(0.5, runtime.ProgressOf(g1, Snap("share.midrange.avg30", 0.075)), 1e-9);
            runtime.EvaluateDay(Snap("share.midrange.avg30", 0.16), D(1980, 2, 1), events);
            Assert.Equal(1.0, runtime.ProgressOf(g1, Snap("share.midrange.avg30", 0.01)), "a done goal stays at 100%");
            Assert.Equal(0.0, runtime.ProgressOf(null, Snap()));
        }

        // ---- construction ----

        [Test]
        public void StartingAScenario_MakesEveryGoalOpen_AndTheRacesKnowTheStart()
        {
            ScenarioDefinition definition = ScenarioTestData.Scenario("mhz-race-1990");
            ScenarioState state = ScenarioState.Start(definition, D(1990, 1, 1));
            Assert.Equal(ScenarioStatus.Active, state.Status);
            Assert.Equal("mhz-race-1990", state.Id);
            Assert.Equal(definition.ContentHash, state.ContentHash);
            Assert.Equal(definition.Goals.Length, state.Goals.Count);
            foreach (GoalSpec goal in definition.Goals) Assert.Equal(GoalStatus.Open, state.Goals[goal.Id].Status);
            Assert.Equal(D(1990, 1, 1), state.StartDate);
            Assert.Equal(D(1990, 1, 1), state.Races.StartDate);
            Assert.Null(state.FindGoal("nope"));
            Assert.Null(state.Result);
        }

        [Test]
        public void TheRuntime_FillsInWhatAnOlderStateLacks()
        {
            ScenarioDefinition definition = ScenarioTestData.Scenario("pc-boom-1980");
            var state = new ScenarioState { Id = definition.Id, Status = ScenarioStatus.Active, StartDate = D(1980, 1, 1), Races = null, Fail = null };
            ScenarioRuntime runtime = Runtime(definition, D(1980, 1, 1), null, null, state);
            Assert.NotNull(state.Races);
            Assert.NotNull(state.Fail);
            Assert.Equal(D(1980, 1, 1), state.Races.StartDate);
            Assert.Equal(definition.Goals.Length, state.Goals.Count);
            Assert.True(ReferenceEquals(definition, runtime.Definition), "the same object");
            Assert.True(ReferenceEquals(state, runtime.State), "the same object");
        }

        [Test]
        public void TheRuntime_RefusesNulls()
        {
            ScenarioDefinition definition = ScenarioTestData.Scenario("pc-boom-1980");
            Assert.Throws<ArgumentNullException>(() => new ScenarioRuntime(null, new ScenarioState(), null, null, null));
            Assert.Throws<ArgumentNullException>(() => new ScenarioRuntime(definition, null, null, null, null));
            Assert.Throws<ArgumentNullException>(() => ScenarioState.Start(null, D(1980, 1, 1)));
        }

        // ---- the whole standard set runs without surprises ----

        [Test]
        public void EveryStandardScenario_StartsActive_AndAnEmptyDayChangesNothing()
        {
            foreach (string id in ScenarioTestData.ScenarioIds)
            {
                EventBook events;
                ScenarioRuntime runtime = Start(id, out events);
                Assert.Equal(ScenarioStatus.Active, runtime.Status, id);
                IList<ScenarioOutcomeEvent> day = runtime.EvaluateDay(Snap(), D(runtime.Definition.Start.Year, 1, 2), events);
                Assert.Equal(0, day.Count, id + " on its second day");
                Assert.Equal(0, runtime.EvaluateMonth(Snap(), D(runtime.Definition.Start.Year, 2, 1), events).Count, id);
            }
        }
    }
}
