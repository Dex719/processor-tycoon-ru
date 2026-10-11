using System;
using PTMod.Features.ScenariosAchievements.Logic;
using static PTMod.Tests.Features.ScenariosAchievements.RunTestData;

namespace PTMod.Tests.Features.ScenariosAchievements
{
    /// <summary>
    /// TSK-007: the kinds of goal - <c>metric</c> (with <c>holdDays</c>), <c>cpuReleased</c> (frequency, cores, watts, SMT, the type of the company
    /// at the release, the date, transferred CPUs do not count), <c>event</c>, <c>condition</c> (through a fake adapter), <c>all</c> and the race
    /// as the book of races has decided it - the failure by <c>by</c>, the rule that a decided goal never changes, and the progress 0..1.
    /// </summary>
    [Covers(typeof(GoalEvaluator), typeof(GoalState), typeof(GoalStatus), typeof(CheckEvaluator), typeof(CheckContext), typeof(CpuMatcher),
        typeof(IConditionAdapter), typeof(NullConditionAdapter))]
    public class GoalEvaluatorTests
    {
        private readonly GoalEvaluator _evaluator = new GoalEvaluator();

        private GoalStatus Eval(GoalSpec goal, GoalState state, MetricsSnapshot m, DateTime today, IConditionAdapter conditions = null,
                                RaceBook races = null, EventBook events = null)
        {
            return _evaluator.Evaluate(goal, state, m, today, conditions, races ?? new RaceBook(), events);
        }

        // ---- metric ----

        [Test]
        public void AMetricGoal_IsDone_OnTheDayTheComparisonHolds_WithTheDate()
        {
            GoalSpec goal = Goal("g1", Metric("share.midrange.avg30", ">=", 0.15), points: 300);
            var state = new GoalState();

            Assert.Equal(GoalStatus.Open, Eval(goal, state, Snap("share.midrange.avg30", 0.149), D(1982, 3, 1)));
            Assert.Equal(GoalStatus.Done, Eval(goal, state, Snap("share.midrange.avg30", 0.152), D(1982, 3, 2)));
            Assert.Equal(GoalStatus.Done, state.Status);
            Assert.Equal(D(1982, 3, 2), state.DoneOn.Value);
        }

        [Test]
        public void EveryOperator_ComparesAsItReads()
        {
            Assert.True(CheckEvaluator.Compare(5, ">=", 5));
            Assert.False(CheckEvaluator.Compare(4.99, ">=", 5));
            Assert.True(CheckEvaluator.Compare(5.01, ">", 5));
            Assert.False(CheckEvaluator.Compare(5, ">", 5));
            Assert.True(CheckEvaluator.Compare(5, "<=", 5));
            Assert.False(CheckEvaluator.Compare(5.01, "<=", 5));
            Assert.True(CheckEvaluator.Compare(-81, "<", -80));
            Assert.False(CheckEvaluator.Compare(-80, "<", -80));
            Assert.True(CheckEvaluator.Compare(3, "==", 3));
            Assert.False(CheckEvaluator.Compare(3.1, "==", 3));
            Assert.False(CheckEvaluator.Compare(3, "!=", 4), "an unknown operator is false");
            Assert.False(CheckEvaluator.Compare(double.NaN, ">=", 0), "an unknown metric reads as NaN and never passes");
            Assert.False(CheckEvaluator.Compare(1, "<", double.NaN));
        }

        [Test]
        public void AMetricGoal_StaysOpen_WhenTheMetricIsNotKnown()
        {
            GoalSpec goal = Goal("g1", Metric("profit.lastMonth", ">", 0));
            var state = new GoalState();
            Assert.Equal(GoalStatus.Open, Eval(goal, state, Snap(), D(1980, 1, 1)));
            Assert.Equal(GoalStatus.Open, Eval(goal, state, null, D(1980, 1, 2)), "no snapshot at all");
        }

        [Test]
        public void HoldDays_NeedsTheConditionToHoldThatManyDaysInARow()
        {
            GoalSpec goal = Goal("g1", Metric("money", ">=", 100, holdDays: 3));
            var state = new GoalState();
            MetricsSnapshot rich = Snap("money", 150);
            MetricsSnapshot poor = Snap("money", 50);

            Assert.Equal(GoalStatus.Open, Eval(goal, state, rich, D(1980, 1, 1)));
            Assert.Equal(1, state.HoldDays);
            Assert.Equal(GoalStatus.Open, Eval(goal, state, rich, D(1980, 1, 2)));
            Assert.Equal(GoalStatus.Open, Eval(goal, state, poor, D(1980, 1, 3)), "a day below breaks the run");
            Assert.Equal(0, state.HoldDays);

            Assert.Equal(GoalStatus.Open, Eval(goal, state, rich, D(1980, 1, 4)));
            Assert.Equal(GoalStatus.Open, Eval(goal, state, rich, D(1980, 1, 5)));
            Assert.Equal(GoalStatus.Done, Eval(goal, state, rich, D(1980, 1, 6)));
            Assert.Equal(D(1980, 1, 6), state.DoneOn.Value, "the goal is done on the day the hold is complete");
        }

        [Test]
        public void HoldDays_AlsoApplyToAConditionGoal()
        {
            var conditions = new FakeConditions { Answer = true };
            CheckSpec check = Condition();
            check.HoldDays = 2;
            GoalSpec goal = Goal("g1", check);
            var state = new GoalState();

            Assert.Equal(GoalStatus.Open, Eval(goal, state, Snap(), D(1980, 1, 1), conditions));
            Assert.Equal(GoalStatus.Done, Eval(goal, state, Snap(), D(1980, 1, 2), conditions));
        }

        // ---- by ----

        [Test]
        public void ByDate_FailsAGoalThatIsNotDoneByThen()
        {
            GoalSpec goal = Goal("g1", Metric("money", ">=", 1000), by: "1980-07-01");
            var state = new GoalState();
            MetricsSnapshot poor = Snap("money", 10);

            Assert.Equal(GoalStatus.Open, Eval(goal, state, poor, D(1980, 6, 30)));
            Assert.Equal(GoalStatus.Failed, Eval(goal, state, poor, D(1980, 7, 1)));
            Assert.Equal(D(1980, 7, 1), state.FailedOn.Value);
            Assert.Null(state.DoneOn);
        }

        [Test]
        public void ByDate_DoesNotFailAGoalThatIsDoneOnThatDay()
        {
            GoalSpec goal = Goal("g1", Metric("money", ">=", 1000), by: "1980-07-01");
            var state = new GoalState();
            Assert.Equal(GoalStatus.Done, Eval(goal, state, Snap("money", 1000), D(1980, 7, 1)),
                "the check comes before the date: done on the day is done by the day");
        }

        [Test]
        public void ADecidedGoal_NeverChanges()
        {
            GoalSpec goal = Goal("g1", Metric("money", ">=", 1000), by: "1980-07-01");
            var done = new GoalState();
            Eval(goal, done, Snap("money", 5000), D(1980, 3, 1));
            Assert.Equal(GoalStatus.Done, Eval(goal, done, Snap("money", 0), D(1981, 1, 1)), "past the date and poor again: still done");
            Assert.Equal(D(1980, 3, 1), done.DoneOn.Value);

            var failed = new GoalState();
            Eval(goal, failed, Snap("money", 10), D(1980, 7, 1));
            Assert.Equal(GoalStatus.Failed, Eval(goal, failed, Snap("money", 99999), D(1980, 8, 1)), "rich later: still failed");
            Assert.Null(failed.DoneOn);
        }

        [Test]
        public void CheckBy_LooksOnlyAtTheDate()
        {
            GoalSpec goal = Goal("g1", Metric("money", ">=", 1), by: "1980-07-01");
            var state = new GoalState();
            Assert.Equal(GoalStatus.Open, _evaluator.CheckBy(goal, state, D(1980, 6, 30)));
            Assert.Equal(GoalStatus.Failed, _evaluator.CheckBy(goal, state, D(1980, 7, 1)));

            GoalSpec noDate = Goal("g2", Metric("money", ">=", 1));
            var open = new GoalState();
            Assert.Equal(GoalStatus.Open, _evaluator.CheckBy(noDate, open, D(2020, 1, 1)));
        }

        // ---- cpuReleased ----

        [Test]
        public void CpuReleased_NeedsAFittingCpuInTheJournal()
        {
            GoalSpec goal = Goal("g4", CpuReleased(new CpuFilter { MinFrequency = 20000 }));
            var state = new GoalState();
            EventBook events = Book();

            Assert.Equal(GoalStatus.Open, Eval(goal, state, Snap(), D(1986, 1, 1), null, null, events));
            events.RecordCpuRelease(Cpu(1, frequency: 15000), true);
            Assert.Equal(GoalStatus.Open, Eval(goal, state, Snap(), D(1986, 1, 2), null, null, events), "15 MHz is below 20 MHz");
            events.RecordCpuRelease(Cpu(2, frequency: 20000), true);
            Assert.Equal(GoalStatus.Done, Eval(goal, state, Snap(), D(1986, 1, 3), null, null, events), "20 MHz is enough");
        }

        [Test]
        public void CpuReleased_ChecksFrequencyCoresWattsAndSmtTogether()
        {
            // power-wall-2000: at least 2 cores, 2 GHz, at most 65 W
            var filter = new CpuFilter { MinCores = 2, MinFrequency = 2000000, MaxPower = 65 };
            Assert.False(CpuMatcher.Matches(filter, Cpu(1, 2000000, cores: 1, power: 50), D(2005, 1, 1)), "one core");
            Assert.False(CpuMatcher.Matches(filter, Cpu(1, 1999999, cores: 2, power: 50), D(2005, 1, 1)), "too slow");
            Assert.False(CpuMatcher.Matches(filter, Cpu(1, 2000000, cores: 2, power: 65.5f), D(2005, 1, 1)), "too hot");
            Assert.True(CpuMatcher.Matches(filter, Cpu(1, 2000000, cores: 2, power: 65), D(2005, 1, 1)), "the limits themselves are allowed");
            Assert.True(CpuMatcher.Matches(filter, Cpu(1, 3400000, cores: 4, power: 40), D(2005, 1, 1)));

            var smt = new CpuFilter { RequireSmt = true };
            Assert.False(CpuMatcher.Matches(smt, Cpu(1, 100, smt: false), D(1996, 1, 1)));
            Assert.True(CpuMatcher.Matches(smt, Cpu(1, 100, smt: true), D(1996, 1, 1)));
        }

        [Test]
        public void CpuReleased_ChecksTheTypeOfTheCompanyAtTheRelease()
        {
            var filter = new CpuFilter { MinFrequency = 2000000, CompanyTypeAtRelease = "fabless" };
            Assert.False(CpuMatcher.Matches(filter, Cpu(1, 2500000, companyType: "idm"), D(2002, 1, 1)));
            Assert.False(CpuMatcher.Matches(filter, Cpu(1, 2500000, companyType: null), D(2002, 1, 1)), "an unknown type is not fabless");
            Assert.True(CpuMatcher.Matches(filter, Cpu(1, 2500000, companyType: "fabless"), D(2002, 1, 1)));
        }

        [Test]
        public void CpuReleased_ChecksTheDateOfTheRelease()
        {
            var filter = new CpuFilter { MinFrequency = 1000000, ReleasedBefore = "2000-01-01" };
            Assert.True(CpuMatcher.Matches(filter, Cpu(1, 1100000), D(1999, 12, 31)));
            Assert.False(CpuMatcher.Matches(filter, Cpu(1, 1100000), D(2000, 1, 1)), "the day itself is not before it");
            Assert.False(CpuMatcher.Matches(filter, Cpu(1, 1100000), default(DateTime)), "an unknown date passes nothing");
            Assert.True(CpuMatcher.Matches(filter, Cpu(1, 1100000, releasedOn: D(1999, 6, 1))), "the CPU's own date is used by the short form");
            Assert.False(CpuMatcher.Matches(filter, null, D(1999, 6, 1)));
            Assert.False(CpuMatcher.Matches(null, Cpu(1, 1100000), D(1999, 6, 1)));
            Assert.False(CpuMatcher.Matches(new CpuFilter(), Cpu(1, 1100000), D(1999, 6, 1)), "a filter with no criterion matches nothing");
        }

        [Test]
        public void CpuReleased_DoesNotCountATransferredCpu()
        {
            var metrics = new MetricsState();
            MetricsCalculator.OnCpuTransferred(metrics, 9);
            EventBook events = Book(metrics);
            GoalSpec goal = Goal("g1", CpuReleased(new CpuFilter { MinFrequency = 1000000 }));
            var state = new GoalState();

            Assert.False(events.RecordCpuRelease(Cpu(9, 1500000), true), "a transferred CPU is not a release");
            Assert.Equal(GoalStatus.Open, Eval(goal, state, Snap(), D(2001, 1, 1), null, null, events));
            Assert.Equal(0, events.Count("cpu.released.player"));

            Assert.True(events.RecordCpuRelease(Cpu(10, 1500000), true));
            Assert.Equal(GoalStatus.Done, Eval(goal, state, Snap(), D(2001, 1, 2), null, null, events));
        }

        [Test]
        public void CpuReleased_IgnoresTheReleasesOfRivals()
        {
            EventBook events = Book();
            GoalSpec goal = Goal("g1", CpuReleased(new CpuFilter { MinFrequency = 1000 }));
            events.RecordCpuRelease(Cpu(1, 99999), false);
            Assert.Equal(GoalStatus.Open, Eval(goal, new GoalState(), Snap(), D(1980, 1, 1), null, null, events));
            Assert.Equal(1, events.Count("cpu.released.ai"));
        }

        // ---- event ----

        [Test]
        public void AnEventGoal_NeedsTheEventToHaveHappenedEnoughTimes()
        {
            EventBook events = Book();
            GoalSpec goal = Goal("g1", Event("oem.signed", 2));
            var state = new GoalState();

            Assert.Equal(GoalStatus.Open, Eval(goal, state, Snap(), D(1980, 1, 1), null, null, events));
            events.Record("oem.signed");
            Assert.Equal(GoalStatus.Open, Eval(goal, state, Snap(), D(1980, 1, 2), null, null, events));
            events.Record("oem.signed");
            Assert.Equal(GoalStatus.Done, Eval(goal, state, Snap(), D(1980, 1, 3), null, null, events));
        }

        [Test]
        public void AnEventGoal_WithoutABook_IsNotDone()
        {
            Assert.Equal(GoalStatus.Open, Eval(Goal("g1", Event("oem.signed")), new GoalState(), Snap(), D(1980, 1, 1)));
        }

        // ---- condition ----

        [Test]
        public void AConditionGoal_FollowsTheAdapter_AndNeverPassesWhenItCannotBeEvaluated()
        {
            GoalSpec goal = Goal("g1", Condition());
            var conditions = new FakeConditions();

            conditions.Answer = false;
            Assert.Equal(GoalStatus.Open, Eval(goal, new GoalState(), Snap(), D(1980, 1, 1), conditions));

            conditions.Answer = null; // the owner of the predicate is off
            Assert.Equal(GoalStatus.Open, Eval(goal, new GoalState(), Snap(), D(1980, 1, 2), conditions));

            conditions.Answer = true;
            var state = new GoalState();
            Assert.Equal(GoalStatus.Done, Eval(goal, state, Snap(), D(1980, 1, 3), conditions));
            Assert.True(conditions.Calls >= 3);

            Assert.Equal(GoalStatus.Open, Eval(goal, new GoalState(), Snap(), D(1980, 1, 4), null), "no adapter at all");
            Assert.Equal(GoalStatus.Open, Eval(goal, new GoalState(), Snap(), D(1980, 1, 5), NullConditionAdapter.Instance));
        }

        // ---- all ----

        [Test]
        public void AnAllGoal_NeedsEveryChild()
        {
            GoalSpec goal = Goal("g1", All(Metric("money.min", "<=", -10), Metric("money", ">=", 100)));
            var state = new GoalState();

            Assert.Equal(GoalStatus.Open, Eval(goal, state, Snap("money.min", -50, "money", 10), D(1980, 1, 1)));
            Assert.Equal(GoalStatus.Open, Eval(goal, state, Snap("money.min", 0, "money", 500), D(1980, 1, 2)));
            Assert.Equal(GoalStatus.Done, Eval(goal, state, Snap("money.min", -50, "money", 500), D(1980, 1, 3)));
        }

        [Test]
        public void AnAllGoal_WithNoChildren_IsNeverTrue()
        {
            Assert.Equal(GoalStatus.Open, Eval(Goal("g1", All()), new GoalState(), Snap(), D(1980, 1, 1)));
        }

        // ---- require, profile ----

        [Test]
        public void ARequire_IsAppliedThroughTheContext_AndFailsClosedWithoutOne()
        {
            CheckSpec check = Metric("run.days", ">=", 10);
            check.Require = new RequireSpec { DifficultyLevel = 5 };
            GoalSpec goal = Goal("g1", check);

            Assert.Equal(GoalStatus.Open, Eval(goal, new GoalState(), Snap("run.days", 20), D(1980, 1, 1)), "nobody can vouch for the difficulty");

            var ctx = new CheckContext { Metrics = Snap("run.days", 20), Today = D(1980, 1, 2), Require = r => r.DifficultyLevel <= 5 };
            Assert.Equal(GoalStatus.Done, _evaluator.Evaluate(goal, new GoalState(), ctx));

            var strict = new CheckContext { Metrics = Snap("run.days", 20), Today = D(1980, 1, 2), Require = r => false };
            Assert.Equal(GoalStatus.Open, _evaluator.Evaluate(goal, new GoalState(), strict));

            check.Require = new RequireSpec(); // an empty require asks for nothing
            Assert.Equal(GoalStatus.Done, Eval(goal, new GoalState(), Snap("run.days", 20), D(1980, 1, 3)));
        }

        [Test]
        public void AProfileCheck_AsksTheProfileHook()
        {
            var check = new CheckSpec { Kind = CheckSpec.KindProfile, Profile = new ProfileSpec { Rule = ProfileSpec.RuleAllScenarioMedals, Medal = "gold" } };
            Assert.False(CheckEvaluator.Evaluate(check, new CheckContext()));
            Assert.True(CheckEvaluator.Evaluate(check, new CheckContext { Profile = p => p.Medal == "gold" }));
            Assert.False(CheckEvaluator.Evaluate(check, new CheckContext { Profile = p => false }));
            Assert.False(CheckEvaluator.Evaluate(null, new CheckContext()));
            Assert.False(CheckEvaluator.Evaluate(check, null));
            Assert.False(CheckEvaluator.Evaluate(new CheckSpec { Kind = "nonsense" }, new CheckContext()));
        }

        // ---- race goals, as the book has them ----

        [Test]
        public void ARaceGoal_FollowsTheBookOfRaces()
        {
            GoalSpec goal = Goal("g1", Race(new CpuFilter { MinFrequency = 100000 }, failOnLose: true), by: null);
            var book = new RaceBook();

            Assert.Equal(GoalStatus.Open, Eval(goal, new GoalState(), Snap(), D(1992, 1, 1), null, book));

            book.Entries["g1"] = new RaceEntry { GoalId = "g1", Status = RaceStatus.Won, WinnerName = "Us", WonOn = D(1993, 5, 5) };
            var won = new GoalState();
            Assert.Equal(GoalStatus.Done, Eval(goal, won, Snap(), D(1993, 5, 6), null, book));
            Assert.Equal(D(1993, 5, 5), won.DoneOn.Value, "the goal is done on the day of the release, not of the evaluation");

            var lostBook = new RaceBook();
            lostBook.Entries["g1"] = new RaceEntry { GoalId = "g1", Status = RaceStatus.Lost, WinnerName = "Inlet", WonOn = D(1994, 3, 14) };
            var lost = new GoalState();
            Assert.Equal(GoalStatus.Failed, Eval(goal, lost, Snap(), D(1994, 3, 15), null, lostBook));
            Assert.Equal(D(1994, 3, 14), lost.FailedOn.Value);
        }

        [Test]
        public void ARaceGoal_WithoutABook_StaysOpen_AndStillHasItsByDate()
        {
            GoalSpec goal = Goal("g1", Race(new CpuFilter { MinFrequency = 100000 }), by: "1993-01-01");
            var state = new GoalState();
            Assert.Equal(GoalStatus.Open, _evaluator.Evaluate(goal, state, new CheckContext { Today = D(1992, 12, 31) }));
            Assert.Equal(GoalStatus.Failed, _evaluator.Evaluate(goal, state, new CheckContext { Today = D(1993, 1, 1) }));
        }

        // ---- progress ----

        [Test]
        public void Progress_IsTheMetricOverTheTarget_ClampedToZeroAndOne()
        {
            GoalSpec percent = Goal("g1", Metric("share.midrange.avg30", ">=", 0.15));
            percent.Progress = new ProgressSpec { Metric = "share.midrange.avg30", Target = 0.15, Format = "percent" };
            Assert.Near(0.5, _evaluator.Progress(percent, Snap("share.midrange.avg30", 0.075)), 1e-12);
            Assert.Equal(1.0, _evaluator.Progress(percent, Snap("share.midrange.avg30", 0.5)));
            Assert.Equal(0.0, _evaluator.Progress(percent, Snap("share.midrange.avg30", -1)));

            GoalSpec money = Goal("g2", Metric("money", ">=", 5000000));
            money.Progress = new ProgressSpec { Metric = "money", Target = 5000000, Format = "money" };
            Assert.Near(0.25, _evaluator.Progress(money, Snap("money", 1250000)), 1e-12);
            Assert.Equal(0.0, _evaluator.Progress(money, Snap("money", -20000000)), "debt is no progress");

            GoalSpec count = Goal("g3", Metric("oem.signed.total", ">=", 3));
            count.Progress = new ProgressSpec { Metric = "oem.signed.total", Target = 3, Format = "count" };
            Assert.Near(2.0 / 3.0, _evaluator.Progress(count, Snap("oem.signed.total", 2)), 1e-12);
        }

        [Test]
        public void Progress_FallsBackToTheCheckOfAMetricGoal_AndIsZeroWhenThereIsNothingToShow()
        {
            GoalSpec noLine = Goal("g1", Metric("profit.streakMonths", ">=", 6));
            Assert.Near(0.5, _evaluator.Progress(noLine, Snap("profit.streakMonths", 3)), 1e-12);

            GoalSpec upperBound = Goal("g2", Metric("money", "<", 6));
            Assert.Equal(0.0, _evaluator.Progress(upperBound, Snap("money", 3)), "a ceiling has no progress towards it");

            GoalSpec cpu = Goal("g3", CpuReleased(new CpuFilter { MinFrequency = 1 }));
            Assert.Equal(0.0, _evaluator.Progress(cpu, Snap()));

            Assert.Equal(0.0, _evaluator.Progress(noLine, Snap()), "an unknown metric");
            Assert.Equal(0.0, _evaluator.Progress(noLine, null));
            Assert.Equal(0.0, _evaluator.Progress(null, Snap()));
        }

        [Test]
        public void Progress_OfATargetAtOrBelowZero_IsReachedOrNot()
        {
            GoalSpec goal = Goal("g1", Metric("money", ">=", 0));
            Assert.Equal(0.0, _evaluator.Progress(goal, Snap("money", -1)));
            Assert.Equal(1.0, _evaluator.Progress(goal, Snap("money", 0)));
        }

        [Test]
        public void Progress_WithTheState_IsOneWhenDone_ZeroWhenFailed()
        {
            GoalSpec goal = Goal("g1", Metric("money", ">=", 1000));
            Assert.Equal(1.0, _evaluator.Progress(goal, new GoalState { Status = GoalStatus.Done }, Snap("money", 1)));
            Assert.Equal(0.0, _evaluator.Progress(goal, new GoalState { Status = GoalStatus.Failed }, Snap("money", 999)));
            Assert.Near(0.5, _evaluator.Progress(goal, new GoalState(), Snap("money", 500)), 1e-12);
            Assert.Near(0.5, _evaluator.Progress(goal, null, Snap("money", 500)), 1e-12);
        }

        // ---- the standard content ----

        [Test]
        public void TheGoalsOfTheStandardScenarios_AreEvaluable_WithTheirOwnProgress()
        {
            // every goal of the shipped scenarios has a check that this evaluator understands, and a progress line when it is a threshold
            foreach (string id in ScenarioTestData.ScenarioIds)
            {
                ScenarioDefinition scenario = ScenarioTestData.Scenario(id);
                foreach (GoalSpec goal in scenario.Goals)
                {
                    var state = new GoalState();
                    GoalStatus status = Eval(goal, state, Snap(), D(scenario.Start.Year, 1, 2));
                    Assert.Equal(GoalStatus.Open, status, id + "." + goal.Id + " must not be decided by an empty snapshot");
                    double progress = _evaluator.Progress(goal, Snap());
                    Assert.InRange(progress, 0.0, 1.0, id + "." + goal.Id);
                }
            }
        }

        [Test]
        public void TheEvaluator_RefusesNulls()
        {
            GoalSpec goal = Goal("g1", Metric("money", ">=", 1));
            Assert.Throws<ArgumentNullException>(() => _evaluator.Evaluate(null, new GoalState(), new CheckContext()));
            Assert.Throws<ArgumentNullException>(() => _evaluator.Evaluate(goal, null, new CheckContext()));
            Assert.Throws<ArgumentNullException>(() => _evaluator.Evaluate(goal, new GoalState(), (CheckContext)null));
            Assert.Throws<ArgumentNullException>(() => _evaluator.CheckBy(null, new GoalState(), D(1980, 1, 1)));
        }
    }
}
