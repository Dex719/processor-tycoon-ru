using System;
using System.Collections.Generic;
using System.Linq;
using PTMod.Features.ScenariosAchievements.Logic;
using static PTMod.Tests.Features.ScenariosAchievements.RunTestData;
using static PTMod.Tests.Features.ScenariosAchievements.UiTestData;

namespace PTMod.Tests.Features.ScenariosAchievements
{
    /// <summary>
    /// TSK-024: the model of the goal tracker (FR-10, FR-7 item 4, AC-10.1): the goals of the shipped scenarios with their progress, the race, the deadline with the days left, the score so
    /// far, the badges of the run. The numbers are written by the formats of the test, the words come from the real string tables.
    /// </summary>
    [Covers(typeof(TrackerBuilder), typeof(TrackerView), typeof(TrackerGoalLine), typeof(RaceStanding), typeof(BadgeLines), typeof(MetricFormat))]
    public class TrackerBuilderTests
    {
        private static TrackerView Build(RunState run, MetricsSnapshot metrics, DateTime today, TestTexts texts, RaceStanding race = null, Func<string, string> featureName = null)
        {
            return TrackerBuilder.Build(run, metrics, today, race, texts, featureName);
        }

        private static TrackerGoalLine Goal(TrackerView view, string id)
        {
            TrackerGoalLine line = view.Goals.FirstOrDefault(g => g.GoalId == id);
            Assert.NotNull(line, "no line for goal " + id);
            return line;
        }

        [Test]
        public void AFreePlayRun_HasNothingToShow()
        {
            var texts = new TestTexts();
            Assert.False(Build(Run(null), Snap(), D(1975, 1, 2), texts).HasScenario);
            Assert.False(Build(null, Snap(), D(1975, 1, 2), texts).HasScenario);
            Assert.Equal(0, Build(Run(null), Snap(), D(1975, 1, 2), texts).Goals.Count);
        }

        [Test]
        public void TheGoalsOfTheScenario_AreListedWithTheirTitles_InOrder()
        {
            var texts = new TestTexts();
            RunState run = Run("garage-1975");
            TrackerView view = Build(run, Snap(), D(1975, 1, 2), texts);

            Assert.True(view.HasScenario);
            Assert.Equal("Гаражный стартап (1975)", view.Title);
            Assert.Equal(run.Scenario.Goals.Select(g => g.Id).ToArray(), view.Goals.Select(g => g.GoalId).ToArray());
            Assert.Equal("Выпустить первый процессор до 1 июля 1976", Goal(view, "g1").Title);
            Assert.True(view.Goals.All(g => g.Status == GoalStatus.Open));
            texts.AssertNothingMissing();
        }

        [Test]
        public void AGoalWithAProgressLine_ShowsWhereItStands_AsAShareOfTheTarget()
        {
            // AC-5.1 / AC-10.1: the share in Mid Range is 9.4% of 15%
            var texts = new TestTexts();
            RunState run = Run("pc-boom-1980");
            TrackerView view = Build(run, Snap("share.midrange.avg30", 0.094), D(1980, 6, 1), texts);

            TrackerGoalLine goal = Goal(view, "g1");
            Assert.Equal("9.40% / 15.00%", goal.Detail);
            Assert.Near(0.094 / 0.15, goal.Progress, 1e-9);
            Assert.Equal(UiTone.Normal, goal.Tone);

            Assert.Equal("1 / 3", Goal(Build(run, Snap("oem.signed.total", 1), D(1980, 6, 1), texts), "g2").Detail, "a count");
        }

        [Test]
        public void AMoneyGoal_IsWrittenInMoney()
        {
            var texts = new TestTexts();
            TrackerView view = Build(Run("garage-1975"), Snap("money", 3200000), D(1975, 6, 1), texts);
            Assert.Equal("$3,200,000 / $5,000,000", Goal(view, "g3").Detail);
        }

        [Test]
        public void AMetricThatHasNoValueYet_ShowsADash()
        {
            var texts = new TestTexts();
            TrackerView view = Build(Run("garage-1975"), Snap(), D(1975, 1, 2), texts);
            Assert.Equal("— / $5,000,000", Goal(view, "g3").Detail);

            TrackerView noSnapshot = Build(Run("garage-1975"), null, D(1975, 1, 2), texts);
            Assert.Equal("— / $5,000,000", Goal(noSnapshot, "g3").Detail, "the first moment of a game has no snapshot at all");
        }

        [Test]
        public void AGoalWithoutAProgressLine_HasNoDetail_WhileItIsOpen()
        {
            var texts = new TestTexts();
            TrackerView view = Build(Run("garage-1975"), Snap(), D(1975, 1, 2), texts);
            Assert.Equal(string.Empty, Goal(view, "g1").Detail, "release the first CPU: nothing to count");
            Assert.Equal(string.Empty, Goal(view, "g2").Detail);
        }

        [Test]
        public void ADoneGoal_SaysWhenItWasDone_AndIsGood()
        {
            var texts = new TestTexts();
            RunState run = Run("garage-1975");
            Done(run, "g1", D(1975, 11, 12));
            TrackerView view = Build(run, Snap(), D(1975, 12, 1), texts);

            TrackerGoalLine goal = Goal(view, "g1");
            Assert.Equal(GoalStatus.Done, goal.Status);
            Assert.Equal("выполнено 12.11.1975", goal.Detail);
            Assert.Equal(UiTone.Good, goal.Tone);
            Assert.Near(1.0, goal.Progress);
        }

        [Test]
        public void AFailedGoal_IsBad()
        {
            var texts = new TestTexts();
            RunState run = Run("garage-1975");
            Failed(run, "g1", D(1976, 7, 1));
            TrackerGoalLine goal = Goal(Build(run, Snap(), D(1976, 7, 2), texts), "g1");

            Assert.Equal("провалено", goal.Detail);
            Assert.Equal(UiTone.Bad, goal.Tone);
            Assert.Near(0.0, goal.Progress);
        }

        [Test]
        public void ARaceThatIsOn_ShowsTheBestCpuOfThePlayerAndOfTheRivals()
        {
            // AC-10.1: "the first to ship a CPU of 1 GHz - yours is 500 MHz, the rivals' 750 MHz"
            var texts = new TestTexts();
            RunState run = Run("ghz-race-1995");
            TrackerView view = Build(run, Snap(), D(1996, 3, 1), texts, new RaceStanding { PlayerBest = 500000, RivalBest = 750000 });

            TrackerGoalLine race = Goal(view, "g1");
            Assert.Equal("Первым выпустить CPU на 1 ГГц", race.Title);
            Assert.Equal("Гонка: ваш лучший CPU — 500 MHz, у соперников — 750 MHz", race.Detail);
            Assert.True(view.NeedsRaceStanding, "the layer of the game then reads the CPUs of the rivals");
            texts.AssertNothingMissing();
        }

        [Test]
        public void ARaceWithoutData_ShowsDashes_AndOnlyARaceAsksForTheStanding()
        {
            var texts = new TestTexts();
            TrackerView race = Build(Run("mhz-race-1990"), Snap(), D(1990, 2, 1), texts, new RaceStanding { PlayerBest = 0, RivalBest = 0 });
            Assert.Equal("Гонка: ваш лучший CPU — —, у соперников — —", Goal(race, "g1").Detail);
            Assert.Equal("Гонка: ваш лучший CPU — —, у соперников — —", Goal(Build(Run("mhz-race-1990"), Snap(), D(1990, 2, 1), texts, null), "g1").Detail);

            Assert.False(Build(Run("garage-1975"), Snap(), D(1975, 2, 1), texts).NeedsRaceStanding, "no race, no pass over the CPUs");
        }

        [Test]
        public void ADecidedRace_StopsAskingForTheStanding_AndAFailedOneNamesTheWinner()
        {
            var texts = new TestTexts();
            RunState run = Run("mhz-race-1990");
            Failed(run, "g1", D(1991, 5, 1));
            run.ScenarioState.Races.Entries["g1"] = new RaceEntry { GoalId = "g1", Status = RaceStatus.Lost, WinnerName = "Intel", WonOn = D(1991, 5, 1) };
            TrackerView view = Build(run, Snap(), D(1991, 5, 2), texts);

            Assert.Equal("Гонку выиграла Intel (01.05.1991)", Goal(view, "g1").Detail);
            Assert.False(view.NeedsRaceStanding, "the race is decided");
        }

        [Test]
        public void AMetricTheBuildDoesNotKnow_IsUnavailable_NotAnError()
        {
            var texts = new TestTexts();
            ScenarioDefinition scenario = Scenario(new[]
            {
                RunTestData.Goal("g1", Metric("nope.metric", ">=", 5), true, 100),
            });
            scenario.Goals[0].Progress = new ProgressSpec { Metric = "nope.metric", Target = 5, Format = "count" };
            scenario.TitleKey = "scn.garage-1975.title";
            scenario.Goals[0].TitleKey = "scn.garage-1975.g1";
            RunState run = RunState.StartNew("run-x", D(1980, 1, 1), 2, scenario, new string[0], new string[0]);

            TrackerGoalLine goal = TrackerBuilder.Build(run, Snap(), D(1980, 2, 1), null, texts).Goals[0];
            Assert.Equal("недоступно", goal.Detail);
            Assert.Equal(UiTone.Muted, goal.Tone);
        }

        [Test]
        public void AnOptionalGoal_IsMarkedInItsTitle()
        {
            var texts = new TestTexts();
            RunState run = Run("garage-1975");
            GoalSpec optional = run.Scenario.Goals.First(g => !g.Required);
            TrackerView view = Build(run, Snap(), D(1975, 2, 1), texts);

            Assert.True(Goal(view, optional.Id).Title.EndsWith(" (по желанию)"), Goal(view, optional.Id).Title);
            Assert.False(Goal(view, run.Scenario.Goals.First(g => g.Required).Id).Title.Contains("по желанию"));
        }

        [Test]
        public void TheDeadline_ShowsTheDateAndTheDaysLeft_InTheRightForm()
        {
            var texts = new TestTexts();
            RunState run = Run("garage-1975");   // the deadline is 1978-01-01

            Assert.Equal("Срок: 01.01.1978, осталось 1 день", Build(run, Snap(), D(1977, 12, 31), texts).Deadline);
            Assert.Equal("Срок: 01.01.1978, осталось 22 дня", Build(run, Snap(), D(1977, 12, 10), texts).Deadline);
            Assert.Equal("Срок: 01.01.1978, осталось 5 дней", Build(run, Snap(), D(1977, 12, 27), texts).Deadline);
            Assert.Equal(22, Build(run, Snap(), D(1977, 12, 10), texts).DaysLeft);
            Assert.Equal(0, Build(run, Snap(), D(1978, 1, 1), texts).DaysLeft, "on the deadline itself");
            texts.AssertNothingMissing();

            var english = new TestTexts("en");
            Assert.Equal("Deadline: 01.01.1978, 1 day left", Build(run, Snap(), D(1977, 12, 31), english).Deadline);
            Assert.Equal("Deadline: 01.01.1978, 5 days left", Build(run, Snap(), D(1977, 12, 27), english).Deadline);
            english.AssertNothingMissing();
        }

        [Test]
        public void TheScoreSoFar_IsWhatTheCalculatorGivesForToday()
        {
            var texts = new TestTexts();
            RunState run = Run("garage-1975");
            Done(run, "g1", D(1975, 6, 1));
            DateTime today = D(1976, 1, 1);

            int expected = new ScoreCalculator().Compute(run.Scenario, run.ScenarioState, today, new ChallengeDefinition[0]).Score;
            TrackerView view = Build(run, Snap(), today, texts);

            Assert.True(expected > 0);
            Assert.Equal("Счёт сейчас: " + expected, view.Score);
            Assert.Null(view.Outcome);
        }

        [Test]
        public void AScenarioThatIsOver_ShowsTheOutcome_AndNoDeadline()
        {
            var texts = new TestTexts();
            RunState run = Run("garage-1975");
            Done(run, "g1", D(1975, 6, 1));
            ScenarioResult result = Lost(run, D(1976, 3, 3), "bankrupt", "scn.fail.bankrupt");
            TrackerView view = Build(run, Snap(), D(1976, 3, 4), texts);

            Assert.True(view.IsFinished);
            Assert.Equal("Сценарий провален", view.Outcome);
            Assert.Equal("Счёт: 0", view.Score);
            Assert.Null(view.Deadline);
            Assert.Equal(-1, view.DaysLeft);

            RunState won = Run("garage-1975");
            Won(won, D(1977, 1, 1));
            Assert.Equal("Сценарий выполнен", Build(won, Snap(), D(1977, 1, 2), texts).Outcome);
        }

        [Test]
        public void TheBadges_TellWhyTheRunIsAssisted_AndThatTheScenarioWasChanged()
        {
            var texts = new TestTexts();
            RunState run = Run("garage-1975");
            Assert.Equal(0, Build(run, Snap(), D(1975, 2, 1), texts).Badges.Count, "a clean run has no badge");

            run.MarkAssisted("cheats.enabled");
            run.MarkAssisted("feature.disabled:market-shocks");
            run.MarkAssisted("some-other-feature:reason");
            run.Custom = true;

            TrackerView view = Build(run, Snap(), D(1975, 2, 1), texts, null, id => id == "market-shocks" ? "Рыночные потрясения" : null);
            string[] lines = view.Badges.Select(b => b.Text).ToArray();
            Assert.Equal(new[]
            {
                "С помощью: включены читы",
                "С помощью: отключена фича «Рыночные потрясения»",
                "С помощью: some-other-feature:reason",
                "Изменённый сценарий: рекорды не записываются",
            }, lines);
            Assert.True(view.Badges.All(b => b.Tone == UiTone.Warn));

            TrackerView unnamed = Build(run, Snap(), D(1975, 2, 1), texts);
            Assert.True(unnamed.Badges.Any(b => b.Text == "С помощью: отключена фича «market-shocks»"), "without a name the id is shown");
            texts.AssertNothingMissing();
        }

        [Test]
        public void TheFormatOfAMetric_FollowsWhatItMeasures()
        {
            Assert.Equal(MetricFormat.Money, MetricFormat.Of("money"));
            Assert.Equal(MetricFormat.Money, MetricFormat.Of("revenue.last12m"));
            Assert.Equal(MetricFormat.Money, MetricFormat.Of("profit.lastMonth"));
            Assert.Equal(MetricFormat.Percent, MetricFormat.Of("share.total.avg30"));
            Assert.Equal(MetricFormat.Percent, MetricFormat.Of("popularity.max"));
            Assert.Equal(MetricFormat.Frequency, MetricFormat.Of("cpu.maxFrequency"));
            Assert.Equal(MetricFormat.Count, MetricFormat.Of("oem.active.count"));
            Assert.Equal(MetricFormat.Count, MetricFormat.Of(null));

            var texts = new TestTexts();
            Assert.Equal("$5", MetricFormat.Format("money", 5, texts));
            Assert.Equal("50.00%", MetricFormat.Format("percent", 0.5, texts));
            Assert.Equal("1.5 GHz", MetricFormat.Format("frequency", 1500000, texts));
            Assert.Equal("7", MetricFormat.Format("count", 7.2, texts));
            Assert.Equal("7", MetricFormat.Format("anything", 7.2, texts));
        }

        [Test]
        public void EveryMetricOfTheVocabulary_HasAFormat_AndEveryShippedProgressLineKeepsItsOwn()
        {
            foreach (MetricInfo metric in ContentVocabulary.Metrics)
            {
                string format = MetricFormat.Of(metric.Id);
                Assert.True(format == MetricFormat.Money || format == MetricFormat.Percent || format == MetricFormat.Count || format == MetricFormat.Frequency, metric.Id);
            }

            // a progress line of the shipped scenarios names its own format; it matches what the metric measures
            foreach (ScenarioDefinition scenario in ScenarioTestData.StandardScenarios())
            {
                foreach (GoalSpec goal in scenario.Goals.Where(g => g.Progress != null))
                {
                    Assert.Equal(MetricFormat.Of(goal.Progress.Metric), goal.Progress.Format, scenario.Id + "/" + goal.Id);
                }
            }
        }

        [Test]
        public void EveryShippedScenario_BuildsATrackerWithoutMissingTexts_InBothLanguages()
        {
            foreach (string language in new[] { "ru", "en" })
            {
                var texts = new TestTexts(language);
                foreach (ScenarioDefinition scenario in ScenarioTestData.StandardScenarios())
                {
                    RunState run = Run(scenario.Id);
                    TrackerView view = Build(run, Snap("money", 1, "share.total.avg30", 0.01), D(scenario.Start.Year, 3, 3), texts, new RaceStanding { PlayerBest = 90000, RivalBest = 95000 });
                    Assert.Equal(scenario.Goals.Length, view.Goals.Count, scenario.Id);
                    Assert.NotNull(view.Deadline, scenario.Id);
                }
                texts.AssertNothingMissing();
            }
        }
    }
}
