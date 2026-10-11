using System;
using System.Collections.Generic;
using System.Linq;
using PTMod.Features.ScenariosAchievements.Logic;
using static PTMod.Tests.Features.ScenariosAchievements.UiTestData;

namespace PTMod.Tests.Features.ScenariosAchievements
{
    /// <summary>
    /// TSK-023: the briefing of a scenario (FR-4 item 2, AC-4.1, AC-4.2): the pieces the window shows and the letter carries - the title "Briefing: ...", the story, the starting
    /// parameters, the goals with their points, the deadline, the challenges - and the arguments of the letter, which the core fills into the keys <c>scn.briefing.title</c> and
    /// <c>scn.mail.briefing.body</c>.
    /// </summary>
    [Covers(typeof(BriefingBuilder), typeof(BriefingView))]
    public class BriefingBuilderTests
    {
        private static BriefingView Build(string scenarioId, TestTexts texts, string[] challenges = null, RunState run = null)
        {
            ScenarioDefinition scenario = ScenarioTestData.Scenario(scenarioId);
            return BriefingBuilder.Build(scenario, challenges ?? new string[0], ChallengeLookup(), run, texts);
        }

        [Test]
        public void TheTitle_IsBriefingAndTheNameOfTheScenario()
        {
            // AC-4.1: the letter "Briefing: Second chance"
            var texts = new TestTexts();
            BriefingView view = Build("comeback-1985", texts);

            Assert.Equal("Второй шанс (1985)", view.ScenarioTitle);
            Assert.Equal("Брифинг: Второй шанс (1985)", view.Title);
            Assert.True(view.Story.StartsWith("1985 год. Компания в долгах"), view.Story);
            texts.AssertNothingMissing();

            var english = new TestTexts("en");
            Assert.True(Build("comeback-1985", english).Title.StartsWith("Briefing: "));
            english.AssertNothingMissing();
        }

        [Test]
        public void TheGoals_AreBulletedWithTheirKindAndTheirPoints()
        {
            var texts = new TestTexts();
            ScenarioDefinition scenario = ScenarioTestData.Scenario("garage-1975");
            BriefingView view = Build("garage-1975", texts);

            string[] lines = view.Goals.Split('\n');
            Assert.Equal("Цели", lines[0]);
            Assert.Equal(scenario.Goals.Length + 1, lines.Length);
            Assert.Equal("• Выпустить первый процессор до 1 июля 1976 — обязательная, 100 очков", lines[1]);
            Assert.True(lines.Any(l => l.Contains("по желанию")), "an optional goal says so");
        }

        [Test]
        public void TheDeadline_IsTheDateOfTheScenario()
        {
            var texts = new TestTexts();
            Assert.Equal("Срок: до 01.01.1978", Build("garage-1975", texts).Deadline);
            Assert.Equal("Срок: до 01.01.1989", Build("comeback-1985", texts).Deadline);
        }

        [Test]
        public void TheStartingParameters_AreTheOnesTheScenarioWritesIntoTheNewGame()
        {
            // AC-4.2: fabless, 125 million, no lines
            var texts = new TestTexts();
            BriefingView view = Build("fabless-1985", texts);

            string[] lines = view.Start.Split('\n');
            Assert.Equal("Параметры старта", lines[0]);
            Assert.Equal("1985, CPU, Fabless, Нормальная, $125,000,000, 0 линий, до 01.01.1991", lines[1]);
            Assert.True(lines[2].StartsWith("Технологии: "), lines[2]);

            BriefingView comeback = Build("comeback-1985", texts);
            Assert.True(comeback.Start.Contains("$-20,000,000"), "a debt is written with its sign: " + comeback.Start);
            Assert.True(comeback.Start.Contains("30 линий"), comeback.Start);
            texts.AssertNothingMissing();
        }

        [Test]
        public void TheChallenges_AreListedWithTheirBonus_AndUnknownOnesAreSkipped()
        {
            var texts = new TestTexts();
            BriefingView view = Build("garage-1975", texts, new[] { "expensive-research", "taxman" });
            Assert.Equal("Испытания: Дорогая наука, Налоговый пресс\nБонус к счёту: +25%", view.Challenges);

            Assert.Null(Build("garage-1975", texts).Challenges, "none chosen: no line");
            Assert.Null(Build("garage-1975", texts, new[] { "no-such-challenge" }).Challenges, "none that the catalog knows: no line");
            Assert.Equal("Испытания: Дорогая наука\nБонус к счёту: +15%", Build("garage-1975", texts, new[] { "no-such-challenge", "expensive-research" }).Challenges);
        }

        [Test]
        public void TheBadges_FollowTheRun()
        {
            var texts = new TestTexts();
            RunState run = Run("garage-1975");
            run.MarkAssisted("cheats.enabled");
            BriefingView view = Build("garage-1975", texts, null, run);
            Assert.Equal(1, view.Badges.Count);
            Assert.Equal("С помощью: включены читы", view.Badges[0].Text);
            Assert.Equal(0, Build("garage-1975", texts, null, Run("garage-1975")).Badges.Count);
        }

        [Test]
        public void TheLetter_CarriesTheTitleTheStoryTheGoalsAndTheTail()
        {
            var texts = new TestTexts();
            BriefingView view = Build("garage-1975", texts, new[] { "taxman" });
            string[] args = BriefingBuilder.MailArgs(view);

            Assert.Equal(4, args.Length);
            Assert.Equal(view.ScenarioTitle, args[0]);
            Assert.True(args[1].StartsWith(view.Story) && args[1].EndsWith(view.Start), "the story and the starting parameters");
            Assert.Equal(view.Goals, args[2]);
            Assert.Equal(view.Deadline + "\n" + view.Challenges, args[3]);

            // what the core makes of the keys and the arguments
            Assert.Equal("Брифинг: Гаражный стартап (1975)", texts.Text("scn.briefing.title", args.Cast<object>().ToArray()));
            string body = texts.Text("scn.mail.briefing.body", args.Cast<object>().ToArray());
            Assert.Equal(args[1] + "\n\n" + args[2] + "\n\n" + args[3], body);
            texts.AssertNothingMissing();
        }

        [Test]
        public void EveryShippedScenario_HasABriefingInBothLanguages_WithoutMissingTexts()
        {
            foreach (string language in new[] { "ru", "en" })
            {
                var texts = new TestTexts(language);
                foreach (string id in ScenarioTestData.ScenarioIds)
                {
                    BriefingView view = Build(id, texts, new[] { "taxman" });
                    Assert.True(view.Story.Length > 20, id);
                    Assert.NotNull(view.Deadline, id);
                    Assert.True(BriefingBuilder.MailArgs(view).All(a => a != null), id);
                }
                texts.AssertNothingMissing();
            }
        }

        [Test]
        public void ANullScenarioOrTexts_IsRefused()
        {
            Assert.Throws<ArgumentNullException>(() => BriefingBuilder.Build(null, null, null, null, new TestTexts()));
            Assert.Throws<ArgumentNullException>(() => BriefingBuilder.Build(ScenarioTestData.Scenario("garage-1975"), null, null, null, null));
            Assert.Throws<ArgumentNullException>(() => BriefingBuilder.MailArgs(null));
        }
    }
}
