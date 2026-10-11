using System;
using System.Collections.Generic;
using System.Linq;
using PTMod.Features.ScenariosAchievements.Logic;
using static PTMod.Tests.Features.ScenariosAchievements.UiTestData;

namespace PTMod.Tests.Features.ScenariosAchievements
{
    /// <summary>
    /// TSK-027: the pop-ups after a pass of the engine (FR-5 item 2, FR-12 items 3 and 4, NFR-6, AC-12.1): a line per goal and per challenge, then the achievements - three by name and one
    /// line "and N more" - and none for the end of the scenario, whose window opens instead.
    /// </summary>
    [Covers(typeof(ToastPlanner))]
    public class ToastPlannerTests
    {
        private static List<string> Plan(RunPassResult pass, TestTexts texts, bool assisted = false)
        {
            return ToastPlanner.Plan(pass, assisted, AchievementLookup(), ChallengeLookup(), texts);
        }

        private static RunPassResult Pass()
        {
            return new RunPassResult();
        }

        private static ScenarioOutcomeEvent Goal(ScenarioOutcomeKind kind, string titleKey)
        {
            return new ScenarioOutcomeEvent { Kind = kind, GoalId = "g1", TextKey = titleKey, Date = new DateTime(1976, 1, 1) };
        }

        [Test]
        public void ADoneGoal_AndAFailedGoal_EachGetALine()
        {
            var texts = new TestTexts();
            RunPassResult pass = Pass();
            pass.Add(Goal(ScenarioOutcomeKind.GoalDone, "scn.garage-1975.g2"));
            pass.Add(Goal(ScenarioOutcomeKind.GoalFailed, "scn.garage-1975.g1"));

            List<string> lines = Plan(pass, texts);

            Assert.Equal(2, lines.Count);
            Assert.True(lines[0].StartsWith("Цель выполнена: "), lines[0]);
            Assert.True(lines[0].EndsWith(texts.Text("scn.garage-1975.g2")), lines[0]);
            Assert.True(lines[1].StartsWith("Цель провалена: "), lines[1]);
            texts.AssertNothingMissing();
        }

        [Test]
        public void TheEndOfTheScenario_HasNoPopUp_ItsWindowOpensInstead()
        {
            var texts = new TestTexts();
            RunPassResult pass = Pass();
            pass.Add(new ScenarioOutcomeEvent { Kind = ScenarioOutcomeKind.Won, Result = new ScenarioResult { Outcome = ScenarioStatus.Won } });
            pass.Add(new ScenarioOutcomeEvent { Kind = ScenarioOutcomeKind.Lost, TextKey = "scn.fail.deadline", Result = new ScenarioResult { Outcome = ScenarioStatus.Lost } });
            Assert.Equal(0, Plan(pass, texts).Count);
        }

        [Test]
        public void AChallengeThatFailedOrWasPassed_GetsALine()
        {
            var texts = new TestTexts();
            RunPassResult pass = Pass();
            pass.Add(new ChallengeEvent { Kind = ChallengeEventKind.Failed, ChallengeId = "no-debt" });
            pass.Add(new ChallengeEvent { Kind = ChallengeEventKind.Passed, ChallengeId = "expensive-research" });
            pass.Add(new ChallengeEvent { Kind = ChallengeEventKind.Passed, ChallengeId = "gone-from-the-catalog" });

            List<string> lines = Plan(pass, texts);

            Assert.Equal(new[] { "Испытание «Без долгов» провалено", "Испытание «Дорогая наука» пройдено", "Испытание «gone-from-the-catalog» пройдено" }, lines.ToArray());
            texts.AssertNothingMissing();
        }

        [Test]
        public void OneAchievement_GetsItsLine()
        {
            // AC-12.1
            var texts = new TestTexts();
            RunPassResult pass = Pass();
            pass.AddAchievement("first-cpu");

            Assert.Equal(new[] { "Достижение: Первый кремний" }, Plan(pass, texts).ToArray());
        }

        [Test]
        public void AnAssistedRun_MarksTheAchievements()
        {
            var texts = new TestTexts();
            RunPassResult pass = Pass();
            pass.AddAchievement("first-cpu");

            Assert.Equal(new[] { "Достижение: Первый кремний (с помощью)" }, Plan(pass, texts, true).ToArray());
        }

        [Test]
        public void ThreeAchievements_AreNamedAll()
        {
            var texts = new TestTexts();
            RunPassResult pass = Pass();
            foreach (string id in new[] { "first-cpu", "first-sale", "first-oem" }) pass.AddAchievement(id);

            List<string> lines = Plan(pass, texts);
            Assert.Equal(3, lines.Count);
            Assert.False(lines.Any(l => l.StartsWith("и ещё")), "three fit without a summary");
        }

        [Test]
        public void MoreThanThreeAchievements_GiveThreeLinesAndOneSummary()
        {
            // FR-12 item 4: 3 pop-ups and "and N more"
            var texts = new TestTexts();
            RunPassResult pass = Pass();
            foreach (string id in new[] { "first-cpu", "first-sale", "first-oem", "first-profit", "cash-100m", "mhz-100" }) pass.AddAchievement(id);

            List<string> lines = Plan(pass, texts);

            Assert.Equal(4, lines.Count);
            Assert.Equal("Достижение: Первый кремний", lines[0]);
            Assert.Equal("Достижение: Первая продажа", lines[1]);
            Assert.Equal("и ещё 3", lines[3]);
            Assert.True(lines[2].StartsWith("Достижение: "));
        }

        [Test]
        public void AnAchievementTheCatalogDoesNotKnow_IsNeitherNamedNorCounted()
        {
            var texts = new TestTexts();
            RunPassResult pass = Pass();
            foreach (string id in new[] { "gone-1", "first-cpu", "gone-2", "gone-3", "gone-4" }) pass.AddAchievement(id);

            Assert.Equal(new[] { "Достижение: Первый кремний" }, Plan(pass, texts).ToArray());
        }

        [Test]
        public void TheOrder_IsGoalsThenChallengesThenAchievements()
        {
            var texts = new TestTexts();
            RunPassResult pass = Pass();
            pass.AddAchievement("first-cpu");
            pass.Add(new ChallengeEvent { Kind = ChallengeEventKind.Failed, ChallengeId = "no-debt" });
            pass.Add(Goal(ScenarioOutcomeKind.GoalDone, "scn.garage-1975.g2"));

            List<string> lines = Plan(pass, texts);
            Assert.True(lines[0].StartsWith("Цель выполнена"), lines[0]);
            Assert.True(lines[1].StartsWith("Испытание"), lines[1]);
            Assert.True(lines[2].StartsWith("Достижение"), lines[2]);
        }

        [Test]
        public void AnEmptyPass_AndNoPass_PlanNothing()
        {
            var texts = new TestTexts();
            Assert.Equal(0, Plan(Pass(), texts).Count);
            Assert.Equal(0, Plan(null, texts).Count);
        }

        [Test]
        public void TheLimit_CanBeLowered_ForTheRestOfADaysBudget()
        {
            var texts = new TestTexts();
            RunPassResult pass = Pass();
            foreach (string id in new[] { "first-cpu", "first-sale", "first-oem" }) pass.AddAchievement(id);

            List<string> lines = ToastPlanner.Plan(pass, false, AchievementLookup(), ChallengeLookup(), texts, 1);
            Assert.Equal(2, lines.Count);
            Assert.Equal("и ещё 2", lines[1]);
        }

        [Test]
        public void TheEnglishTables_AnswerEverything()
        {
            var texts = new TestTexts("en");
            RunPassResult pass = Pass();
            pass.Add(Goal(ScenarioOutcomeKind.GoalDone, "scn.garage-1975.g2"));
            pass.Add(new ChallengeEvent { Kind = ChallengeEventKind.Failed, ChallengeId = "no-debt" });
            foreach (string id in new[] { "first-cpu", "first-sale", "first-oem", "first-profit" }) pass.AddAchievement(id);

            List<string> lines = Plan(pass, texts, true);
            Assert.True(lines[0].StartsWith("Goal done: "), lines[0]);
            Assert.Equal("Challenge \"No debt\" failed", lines[1]);
            Assert.Equal("Achievement: First silicon (assisted)", lines[2]);
            Assert.Equal("and 1 more", lines[5]);
            texts.AssertNothingMissing();
        }

        [Test]
        public void ANullTexts_IsRefused()
        {
            Assert.Throws<ArgumentNullException>(() => ToastPlanner.Plan(Pass(), false, null, null, null));
        }
    }
}
