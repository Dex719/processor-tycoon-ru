using System;
using System.Collections.Generic;
using System.Linq;
using PTMod.Core.Logic.Json;
using PTMod.Features.ScenariosAchievements.Logic;
using static PTMod.Tests.Features.ScenariosAchievements.RunTestData;
using static PTMod.Tests.Features.ScenariosAchievements.UiTestData;

namespace PTMod.Tests.Features.ScenariosAchievements
{
    /// <summary>
    /// TSK-028: the summary of the career on 1 January of the last year of the game (FR-17, AC-17.1): the words above the table of the companies, the flags, the achievements of the game
    /// and its challenges, a scenario that ended earlier, a run that began with a load - and the whole way from the engine ("End of an era" opens with <c>endyear.reached</c>, a challenge is
    /// passed by the end year) to the lines of the window.
    /// </summary>
    [Covers(typeof(CareerSummaryBuilder), typeof(CareerSummaryView))]
    public class CareerSummaryBuilderTests
    {
        private static readonly DateTime End = D(2030, 1, 1);

        // a free game that began in 1985 and reached the end year
        private static RunState FreeGame(DateTime? start = null)
        {
            return RunState.StartNew("run-1", start ?? D(1985, 3, 15), 2, null, new string[0], new[] { "scenarios-achievements" });
        }

        private static CareerSummaryView Build(RunState run, TestTexts texts, DateTime? today = null, Func<string, string> featureName = null)
        {
            return CareerSummaryBuilder.Build(run, today ?? End, AchievementLookup(), ChallengeLookup(), texts, featureName);
        }

        // ---- the words ----

        [Test]
        public void TheIntro_NamesTheYear_AndTheSpan_CountsTheWholeYearsOfTheCareer()
        {
            var texts = new TestTexts();
            CareerSummaryView view = Build(FreeGame(), texts);

            Assert.Equal("Наступил 2030 год — игра дошла до конца эпохи. Так сложилась ваша карьера.", view.Intro);
            Assert.Equal("Карьера: с 15.03.1985 по 01.01.2030 (44 года).", view.Span, "the 45th year of the career is not over yet");
            Assert.Null(view.Scenario);
            Assert.Equal(0, view.Flags.Count);
            texts.AssertNothingMissing();

            var english = new TestTexts("en");
            CareerSummaryView en = Build(FreeGame(), english);
            Assert.Equal("The year 2030 has come, and the game has reached the end of the era. This is how your career went.", en.Intro);
            Assert.Equal("Career: from 15.03.1985 to 01.01.2030 (44 years).", en.Span);
            english.AssertNothingMissing();
        }

        [Test]
        public void TheYearsOfTheCareer_AreWrittenInTheFormThatFitsTheNumber()
        {
            var texts = new TestTexts();
            Assert.Equal("Карьера: с 01.01.2029 по 01.01.2030 (1 год).", Build(FreeGame(D(2029, 1, 1)), texts).Span);
            Assert.Equal("Карьера: с 01.01.2027 по 01.01.2030 (3 года).", Build(FreeGame(D(2027, 1, 1)), texts).Span);
            Assert.Equal("Карьера: с 01.01.2000 по 01.01.2030 (30 лет).", Build(FreeGame(D(2000, 1, 1)), texts).Span);
            Assert.Equal("Карьера: с 01.06.2029 по 01.01.2030 (0 лет).", Build(FreeGame(D(2029, 6, 1)), texts).Span);
            texts.AssertNothingMissing();

            var english = new TestTexts("en");
            Assert.Equal("Career: from 01.01.2029 to 01.01.2030 (1 year).", Build(FreeGame(D(2029, 1, 1)), english).Span);
            Assert.Equal("Career: from 01.01.2000 to 01.01.2030 (30 years).", Build(FreeGame(D(2000, 1, 1)), english).Span);
            english.AssertNothingMissing();
        }

        [Test]
        public void YearsBetween_IsTheNumberOfCompletedYears()
        {
            Assert.Equal(30, CareerSummaryBuilder.YearsBetween(D(2000, 1, 1), D(2030, 1, 1)));
            Assert.Equal(29, CareerSummaryBuilder.YearsBetween(D(2000, 1, 2), D(2030, 1, 1)), "one day short of the anniversary");
            Assert.Equal(29, CareerSummaryBuilder.YearsBetween(D(2000, 2, 1), D(2030, 1, 1)));
            Assert.Equal(1, CareerSummaryBuilder.YearsBetween(D(2000, 2, 29), D(2001, 3, 1)), "a leap day");
            Assert.Equal(0, CareerSummaryBuilder.YearsBetween(D(2030, 1, 1), D(2030, 1, 1)));
            Assert.Equal(0, CareerSummaryBuilder.YearsBetween(D(2031, 1, 1), D(2030, 1, 1)), "a start in the future is no career");
            Assert.Equal(0, CareerSummaryBuilder.YearsBetween(D(2029, 12, 31), D(2030, 1, 1)));
        }

        [Test]
        public void ARunThatBeganWithALoad_CountsFromTheDayOfTheLoad_AndSaysThatTheOldHistoryIsUnknown()
        {
            var texts = new TestTexts();
            RunState legacy = RunState.CreateLegacy("run-legacy", D(2029, 3, 10), 2, false, new[] { "scenarios-achievements" });
            CareerSummaryView view = Build(legacy, texts);

            Assert.Equal("Учтено с 10.03.2029: сохранение сделано без мода, прежняя история неизвестна.", view.Span);
            texts.AssertNothingMissing();

            var english = new TestTexts("en");
            Assert.Equal("Counted from 10.03.2029: the save was made without the mod, so the earlier history is unknown.", Build(legacy, english).Span);
            english.AssertNothingMissing();
        }

        // ---- the achievements and the challenges ----

        [Test]
        public void ANewGameWithNothingEarned_SaysSo()
        {
            var texts = new TestTexts();
            CareerSummaryView view = Build(FreeGame(), texts);

            Assert.Equal("Достижения этой игры", view.AchievementsHeading);
            Assert.Equal(0, view.Achievements.Count);
            Assert.Equal("В этой игре новых достижений не получено.", view.NoAchievements);
            Assert.Equal("Испытания", view.ChallengesHeading);
            Assert.Equal(0, view.Challenges.Count);
            Assert.Equal("Испытания для этой игры не выбирались.", view.NoChallenges);
            texts.AssertNothingMissing();
        }

        [Test]
        public void TheAchievementsOfTheGame_AreListedByTitle_AndMarkedWhenAssisted()
        {
            var texts = new TestTexts();
            RunState run = FreeGame();
            run.AchievementsThisRun.AddRange(new[] { "first-cpu", "no-such-achievement", "end-of-era" });

            CareerSummaryView view = Build(run, texts);
            Assert.Equal(new[] { "Первый кремний", "Конец эпохи" }, view.Achievements.ToArray(), "an id the catalog does not know is left out");
            Assert.Null(view.NoAchievements);

            run.MarkAssisted("cheats.enabled");
            CareerSummaryView assisted = Build(run, texts);
            Assert.Equal(new[] { "Первый кремний (с помощью)", "Конец эпохи (с помощью)" }, assisted.Achievements.ToArray());
            Assert.Equal(new[] { "С помощью: включены читы" }, assisted.Flags.Select(f => f.Text).ToArray());
            Assert.Equal(UiTone.Warn, assisted.Flags[0].Tone);
            texts.AssertNothingMissing();
        }

        [Test]
        public void TheChallengesOfTheRun_ShowHowEachOneStands()
        {
            var texts = new TestTexts();
            RunState run = RunState.StartNew("run-1", D(2018, 1, 1), 2, null, new[] { "expensive-research", "taxman", "no-debt" }, new[] { "scenarios-achievements" });
            run.ChallengeStates.Add("expensive-research", new string[0]);
            run.ChallengeStates.Add("taxman", new string[0]).Status = ChallengeStatus.Passed;
            run.ChallengeStates.Add("no-debt", new string[0]).Status = ChallengeStatus.Failed;

            CareerSummaryView view = Build(run, texts);
            Assert.Equal(new[] { "Дорогая наука — идёт", "Налоговый пресс — пройдено", "Без долгов — провалено" }, view.Challenges.Select(c => c.Text).ToArray());
            Assert.Equal(new[] { UiTone.Normal, UiTone.Good, UiTone.Bad }, view.Challenges.Select(c => c.Tone).ToArray());
            Assert.Null(view.NoChallenges);
            texts.AssertNothingMissing();
        }

        [Test]
        public void TheFlags_TellWhyTheRunIsAssisted_AndThatTheScenarioWasChanged_AsOnTheResults()
        {
            var texts = new TestTexts();
            RunState run = FreeGame();
            run.MarkAssisted("feature.disabled:event-system");
            run.Custom = true;

            CareerSummaryView view = Build(run, texts, null, id => id == "event-system" ? "Система событий" : null);
            Assert.Equal(new[] { "С помощью: отключена фича «Система событий»", "Изменённый сценарий: рекорды не записываются" }, view.Flags.Select(f => f.Text).ToArray());
        }

        // ---- a scenario that ended earlier ----

        [Test]
        public void AScenarioThatWasWon_IsMentionedWithItsScoreAndMedal()
        {
            var texts = new TestTexts();
            RunState run = Run("garage-1975");
            foreach (string goal in new[] { "g1", "g2", "g3", "g4" }) Done(run, goal, D(1975, 7, 1));
            Won(run, D(1977, 1, 1));

            CareerSummaryView view = Build(run, texts);
            Assert.Equal("Сценарий «Гаражный стартап (1975)» выполнен: счёт 600, медаль — Серебро.", view.Scenario);
            Assert.Equal(UiTone.Good, view.ScenarioTone);

            var english = new TestTexts("en");
            Assert.Equal("Scenario \"Garage startup (1975)\" complete: score 600, medal — Silver.", Build(run, english).Scenario);
            texts.AssertNothingMissing();
            english.AssertNothingMissing();
        }

        [Test]
        public void AScenarioThatWasLost_IsMentioned_AndOneWithoutAResultStillIs()
        {
            var texts = new TestTexts();
            RunState lost = Run("garage-1975");
            Lost(lost, D(1978, 1, 1), "deadline", "scn.fail.deadline");
            CareerSummaryView view = Build(lost, texts);
            Assert.Equal("Сценарий «Гаражный стартап (1975)» провален.", view.Scenario);
            Assert.Equal(UiTone.Bad, view.ScenarioTone);

            RunState noResult = Run("garage-1975");
            noResult.ScenarioState.Status = ScenarioStatus.Won;
            noResult.ScenarioState.Result = null;
            Assert.Equal("Сценарий «Гаражный стартап (1975)» выполнен.", Build(noResult, texts).Scenario);

            RunState going = Run("garage-1975");
            Assert.Null(Build(going, texts).Scenario, "a scenario that goes has the tracker, not the summary");
            texts.AssertNothingMissing();
        }

        // ---- the engine to the window (AC-17.1) ----

        private sealed class Ports : IRunPorts
        {
            public readonly ProfileModel Model = new ProfileModel(new JsonObject());

            public ProfileView Profile
            {
                get { return Model.View; }
            }

            public bool UnlockAchievement(string achievementId, AchievementEntry entry)
            {
                return Model.Unlock(achievementId, entry);
            }

            public void RecordScenarioResult(ScenarioResult result, RunInfo run)
            {
                Model.RecordResult(result, run);
            }

            public void RecordChallengePassed(string challengeId, int years)
            {
                Model.RecordChallenge(challengeId, years);
            }

            public void RemoveModifierEntries(IList<string> entryIds)
            {
            }
        }

        private static LiveRun Live(DateTime today, bool strict = false)
        {
            return new LiveRun { Today = today, CompanyType = "idm", CompanyName = "Garage CPU", Difficulty = 2, StrictAssisted = strict };
        }

        private static RunEngine Engine(RunState run, Ports ports, params ChallengeDefinition[] challenges)
        {
            return new RunEngine(run, challenges, new AchievementEvaluator(ScenarioTestData.StandardAchievements(), ScenarioTestData.ScenarioIds), null,
                ports, null, () => new DateTime(2026, 10, 10, 12, 0, 0, DateTimeKind.Utc), 10);
        }

        [Test]
        public void FreePlayAtTheEndYear_ShowsEndOfAnEra_AndTheChallengePassedByTheEndYear_AC171()
        {
            // the game reaches 1 January 2030: DateController raises the day first (the challenge is passed by the end year), then the year (the director counts endyear.reached)
            var texts = new TestTexts();
            ChallengeDefinition taxman = Challenge("taxman");
            RunState run = RunState.StartNew("run-1", D(2025, 1, 1), 2, null, new[] { "taxman" }, new[] { "scenarios-achievements" });
            run.ChallengeStates.Add("taxman", new string[0]);
            var ports = new Ports();
            RunEngine engine = Engine(run, ports, taxman);

            engine.OnDay(() => Day(End, 1000), Live(End));
            RunPassResult year = engine.OnEvent("endyear.reached", Live(End));
            Assert.True(year.Achievements.Contains("end-of-era"), "the event opens the achievement before the window is asked for its lines");

            CareerSummaryView view = Build(run, texts);
            Assert.True(view.Achievements.Contains("Конец эпохи"), string.Join(", ", view.Achievements));
            Assert.Equal(new[] { "Налоговый пресс — пройдено" }, view.Challenges.Select(c => c.Text).ToArray());
            Assert.Equal("Карьера: с 01.01.2025 по 01.01.2030 (5 лет).", view.Span);
            texts.AssertNothingMissing();
        }

        [Test]
        public void ABankruptGame_DoesNotShowEndOfAnEra_ButStillGetsItsSummary()
        {
            var texts = new TestTexts();
            RunState run = FreeGame(D(2000, 1, 1));
            var ports = new Ports();
            RunEngine engine = Engine(run, ports);
            engine.OnPlayerBankrupt(Live(D(2010, 1, 1)));
            engine.OnEvent("endyear.reached", Live(End));

            CareerSummaryView view = Build(run, texts);
            Assert.False(view.Achievements.Contains("Конец эпохи"));
            Assert.Equal("Карьера: с 01.01.2000 по 01.01.2030 (30 лет).", view.Span);
        }

        [Test]
        public void EveryTextOfTheSummary_IsThereInBothLanguages_ForEveryKindOfRun()
        {
            foreach (string language in new[] { "ru", "en" })
            {
                var texts = new TestTexts(language);
                RunState scenarioRun = Run("pc-boom-1980", "taxman");
                scenarioRun.ChallengeStates.Add("taxman", new string[0]);
                Lost(scenarioRun, D(1983, 1, 1), "bankrupt", "scn.fail.bankrupt");
                scenarioRun.MarkAssisted("cheats.enabled");
                scenarioRun.AchievementsThisRun.Add("first-cpu");

                foreach (RunState run in new[] { FreeGame(), scenarioRun, RunState.CreateLegacy("l", D(2029, 6, 6), 1, true, new string[0]) })
                {
                    CareerSummaryView view = Build(run, texts);
                    Assert.True(view.Intro.Length > 0 && view.Span.Length > 0 && view.AchievementsHeading.Length > 0 && view.ChallengesHeading.Length > 0, language);
                }
                texts.AssertNothingMissing();
            }
        }

        [Test]
        public void ARequiredArgumentThatIsNull_IsRefused()
        {
            var texts = new TestTexts();
            Assert.Throws<ArgumentNullException>(() => CareerSummaryBuilder.Build(null, End, null, null, texts));
            Assert.Throws<ArgumentNullException>(() => CareerSummaryBuilder.Build(FreeGame(), End, null, null, null));

            CareerSummaryView bare = CareerSummaryBuilder.Build(FreeGame(), End, null, null, texts);
            Assert.Equal(0, bare.Achievements.Count, "no catalog: nothing to name");
        }
    }
}
