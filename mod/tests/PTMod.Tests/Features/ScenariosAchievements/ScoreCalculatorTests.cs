using System;
using System.Collections.Generic;
using PTMod.Features.ScenariosAchievements.Logic;
using static PTMod.Tests.Features.ScenariosAchievements.RunTestData;

namespace PTMod.Tests.Features.ScenariosAchievements
{
    /// <summary>
    /// TSK-010: the score and the medals - the example of AC-8.1 (600 and silver), the additive bonus of the challenges (15% + 10% = ×1.25),
    /// a loss is 0, and a score on a threshold gets the medal of that threshold (checked on all eight standard scenarios).
    /// </summary>
    [Covers(typeof(ScoreCalculator), typeof(ScoreBreakdown))]
    public class ScoreCalculatorTests
    {
        private readonly ScoreCalculator _calculator = new ScoreCalculator();

        private static ScenarioState StateWith(ScenarioDefinition definition, DateTime start, ScenarioStatus status, params string[] done)
        {
            ScenarioState state = ScenarioState.Start(definition, start);
            state.Status = status;
            foreach (string goalId in done) state.GoalOf(goalId).Status = GoalStatus.Done;
            return state;
        }

        [Test]
        public void TheExampleOfAC81_IsSixHundred_Silver()
        {
            // garage-1975 won on 1977-01-01 with all four goals, no challenges
            ScenarioDefinition garage = ScenarioTestData.Scenario("garage-1975");
            ScenarioState state = StateWith(garage, D(1975, 1, 1), ScenarioStatus.Won, "g1", "g2", "g3", "g4");

            ScoreBreakdown score = _calculator.Compute(garage, state, D(1977, 1, 1), null);
            Assert.Equal(500, score.GoalPoints);
            Assert.Equal(1096, score.TotalDays);
            Assert.Equal(365, score.DaysLeft);
            Assert.Equal(100, score.TimeBonus, "round(300 × 365 / 1096)");
            Assert.Equal(0, score.ChallengeBonusPercent);
            Assert.Equal(600, score.Score);
            Assert.Equal("silver", _calculator.Medal(garage, score.Score));
        }

        [Test]
        public void TheBonusesOfTheChallenges_Add_NotMultiply()
        {
            ScenarioDefinition garage = ScenarioTestData.Scenario("garage-1975");
            ScenarioState state = StateWith(garage, D(1975, 1, 1), ScenarioStatus.Won, "g1", "g2", "g3", "g4");
            var challenges = new List<ChallengeDefinition> { Challenge("a", 0.15f), Challenge("b", 0.10f) };

            ScoreBreakdown score = _calculator.Compute(garage, state, D(1977, 1, 1), challenges);
            Assert.Equal(25, score.ChallengeBonusPercent);
            Assert.Equal(750, score.Score, "600 × 1.25, not 600 × 1.15 × 1.10 = 759");
            Assert.Equal(25, ScoreCalculator.BonusPercent(challenges));
        }

        [Test]
        public void TheBonusOfTheStandardChallenges_IsExact()
        {
            // 0.15f + 0.10f + 0.20f + 0.25f is not exact in binary floating point; the score must not wobble on it
            var all = ScenarioTestData.StandardChallenges();
            decimal expected = 0;
            foreach (ChallengeDefinition c in all) expected += (decimal)c.ScoreBonus;
            Assert.Equal((int)Math.Round(expected * 100m), ScoreCalculator.BonusPercent(all));

            var one = new List<ChallengeDefinition> { Challenge("x", 0.15f) };
            ScenarioDefinition d = Scenario(new[] { Goal("g1", Metric("money", ">=", 1), points: 100) });
            d.TimeBonusMax = 0;
            ScenarioState state = StateWith(d, D(1980, 1, 1), ScenarioStatus.Won, "g1");
            Assert.Equal(115, _calculator.Compute(d, state, D(1980, 3, 1), one).Score);
        }

        [Test]
        public void AChallengeListedTwice_CountsOnce_AndANullIsSkipped()
        {
            var list = new List<ChallengeDefinition> { Challenge("a", 0.15f), Challenge("a", 0.15f), null, Challenge("b", 0.10f) };
            Assert.Equal(25, ScoreCalculator.BonusPercent(list));
            Assert.Equal(0, ScoreCalculator.BonusPercent(null));
            Assert.Equal(0, ScoreCalculator.BonusPercent(new List<ChallengeDefinition>()));
        }

        [Test]
        public void ALoss_IsZero_ButTheGoalsDoneAreStillListed()
        {
            ScenarioDefinition garage = ScenarioTestData.Scenario("garage-1975");
            ScenarioState state = StateWith(garage, D(1975, 1, 1), ScenarioStatus.Lost, "g1", "g2");

            ScoreBreakdown score = _calculator.Compute(garage, state, D(1976, 6, 1), new List<ChallengeDefinition> { Challenge("a", 0.2f) });
            Assert.Equal(0, score.Score);
            Assert.Equal(0, score.TimeBonus);
            Assert.Equal(200, score.GoalPoints);
        }

        [Test]
        public void TheTimeBonus_ShrinksWithTheTimeSpent()
        {
            ScenarioDefinition d = Scenario(new[] { Goal("g1", Metric("money", ">=", 1), points: 0) }); // 731 days, bonus 100
            ScenarioState state = StateWith(d, D(1980, 1, 1), ScenarioStatus.Won, "g1");

            Assert.Equal(100, _calculator.Compute(d, state, D(1980, 1, 1), null).TimeBonus, "won on the first day");
            Assert.Equal(50, _calculator.Compute(d, state, D(1981, 1, 1), null).TimeBonus, "365 of 731 days left rounds to 50");
            Assert.Equal(0, _calculator.Compute(d, state, D(1982, 1, 1), null).TimeBonus, "won on the deadline");
            Assert.Equal(0, _calculator.Compute(d, state, D(1983, 1, 1), null).TimeBonus, "after the deadline: none, never negative");
            Assert.Equal(100, _calculator.Compute(d, state, D(1979, 1, 1), null).TimeBonus, "before the start: capped at the maximum");
        }

        [Test]
        public void TheHalfPoint_RoundsUp()
        {
            ScenarioDefinition d = Scenario(new[] { Goal("g1", Metric("money", ">=", 1), points: 1) });
            d.TimeBonusMax = 1;
            d.Deadline = "1980-01-03"; // 2 days
            ScenarioState state = StateWith(d, D(1980, 1, 1), ScenarioStatus.Won, "g1");
            Assert.Equal(1, _calculator.Compute(d, state, D(1980, 1, 2), null).TimeBonus, "1 × 1 / 2 = 0.5 rounds away from zero");
        }

        [Test]
        public void ADefinitionWithoutAUsableDeadline_HasNoTimeBonus()
        {
            ScenarioDefinition d = Scenario(new[] { Goal("g1", Metric("money", ">=", 1), points: 10) });
            d.Deadline = "not a date";
            ScenarioState state = StateWith(d, D(1980, 1, 1), ScenarioStatus.Won, "g1");
            ScoreBreakdown score = _calculator.Compute(d, state, D(1980, 6, 1), null);
            Assert.Equal(0, score.TimeBonus);
            Assert.Equal(10, score.Score);
        }

        [Test]
        public void AScenarioStillGoing_IsScoredAsIfItEndedNow()
        {
            ScenarioDefinition garage = ScenarioTestData.Scenario("garage-1975");
            ScenarioState state = StateWith(garage, D(1975, 1, 1), ScenarioStatus.Active, "g1");
            Assert.Equal(100 + (int)Math.Round(300.0 * 731 / 1096), _calculator.Compute(garage, state, D(1976, 1, 1), null).Score);
        }

        [Test]
        public void TheMedal_IsTheBestThresholdReached_OnEveryStandardScenario()
        {
            foreach (string id in ScenarioTestData.ScenarioIds)
            {
                ScenarioDefinition s = ScenarioTestData.Scenario(id);
                MedalThresholds m = s.Medals;
                Assert.Equal("gold", _calculator.Medal(s, m.Gold), id + " at gold");
                Assert.Equal("gold", _calculator.Medal(s, m.Gold + 500), id + " above gold");
                Assert.Equal("silver", _calculator.Medal(s, m.Gold - 1), id + " just below gold");
                Assert.Equal("silver", _calculator.Medal(s, m.Silver), id + " at silver");
                Assert.Equal("bronze", _calculator.Medal(s, m.Silver - 1), id + " just below silver");
                Assert.Equal("bronze", _calculator.Medal(s, m.Bronze), id + " at bronze");
                Assert.Equal("completed", _calculator.Medal(s, m.Bronze - 1), id + " just below bronze");
                Assert.Equal("completed", _calculator.Medal(s, 0), id + " at zero");
            }
        }

        [Test]
        public void ARunThatDoesEverything_ReachesGold_OnEveryStandardScenario()
        {
            // all goals and a win on the first day: the most a run can score without challenges must be at least gold
            foreach (string id in ScenarioTestData.ScenarioIds)
            {
                ScenarioDefinition s = ScenarioTestData.Scenario(id);
                DateTime start = D(s.Start.Year, 1, 1);
                var ids = new List<string>();
                foreach (GoalSpec goal in s.Goals) ids.Add(goal.Id);
                ScenarioState state = StateWith(s, start, ScenarioStatus.Won, ids.ToArray());

                ScoreBreakdown score = _calculator.Compute(s, state, start, null);
                Assert.Equal(s.MaxScoreWithoutChallenges(), score.Score, id + " maximum");
                Assert.Equal("gold", _calculator.Medal(s, score.Score), id);
            }
        }

        [Test]
        public void ARunWithOnlyTheRequiredGoals_WonOnTheDeadline_IsBronze_OnEveryStandardScenario()
        {
            foreach (string id in ScenarioTestData.ScenarioIds)
            {
                ScenarioDefinition s = ScenarioTestData.Scenario(id);
                DateTime start = D(s.Start.Year, 1, 1);
                DateTime deadline;
                Assert.True(s.TryGetDeadline(out deadline));
                var required = new List<string>();
                foreach (GoalSpec goal in s.Goals)
                {
                    if (goal.Required) required.Add(goal.Id);
                }
                ScenarioState state = StateWith(s, start, ScenarioStatus.Won, required.ToArray());

                ScoreBreakdown score = _calculator.Compute(s, state, deadline, null);
                Assert.Equal(s.RequiredPoints(), score.Score, id);
                Assert.Equal("bronze", _calculator.Medal(s, score.Score), id + ": the required goals alone, at the last minute, are exactly the bronze");
            }
        }

        [Test]
        public void TheCalculator_RefusesNulls()
        {
            ScenarioDefinition garage = ScenarioTestData.Scenario("garage-1975");
            Assert.Throws<ArgumentNullException>(() => _calculator.Compute(null, new ScenarioState(), D(1975, 1, 1), null));
            Assert.Throws<ArgumentNullException>(() => _calculator.Compute(garage, null, D(1975, 1, 1), null));
            Assert.Throws<ArgumentNullException>(() => _calculator.Medal(null, 5));
        }

        [Test]
        public void ADefinitionWithoutMedals_GivesTheLowestOne()
        {
            ScenarioDefinition d = Scenario(new[] { Goal("g1", Metric("money", ">=", 1)) });
            d.Medals = null;
            Assert.Equal("completed", _calculator.Medal(d, 99999));
        }
    }
}
