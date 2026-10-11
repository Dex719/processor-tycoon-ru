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
    /// TSK-026: the models of the window of the achievements and records (FR-14, FR-12 item 6, AC-14.1): the catalog by category with what the profile has earned, "???" for a hidden
    /// achievement that is not earned, the mark "with help", the progress of a counted achievement while a game goes, and the records of the scenarios and the challenges.
    /// </summary>
    [Covers(typeof(AchievementsViewBuilder), typeof(AchievementsView), typeof(AchievementCategory), typeof(AchievementRow), typeof(RecordsView), typeof(RecordRow))]
    public class AchievementsViewBuilderTests
    {
        private static ProfileModel NewProfile()
        {
            return new ProfileModel(new JsonObject(), () => Real);
        }

        private static AchievementsView Build(ProfileModel profile, TestTexts texts, MetricsSnapshot metrics = null, EventBook events = null)
        {
            return AchievementsViewBuilder.Build(ScenarioTestData.StandardAchievements(), profile.View, metrics, events, texts);
        }

        private static AchievementRow Row(AchievementsView view, string id)
        {
            AchievementRow row = view.Categories.SelectMany(c => c.Rows).FirstOrDefault(r => r.Id == id);
            Assert.NotNull(row, "no row for " + id);
            return row;
        }

        [Test]
        public void AnEmptyProfile_ShowsNothingEarned_OutOfThirtySeven()
        {
            var texts = new TestTexts();
            AchievementsView view = Build(NewProfile(), texts);

            Assert.Equal(0, view.Unlocked);
            Assert.Equal(37, view.Total);
            Assert.Equal("Достижения: 0 / 37", view.Title);
            Assert.Equal(ContentVocabulary.Categories.ToArray(), view.Categories.Select(c => c.Id).ToArray(), "the categories in the order of the vocabulary");
            Assert.Equal(37, view.Categories.Sum(c => c.Total));
            Assert.True(view.Categories.SelectMany(c => c.Rows).All(r => !r.Unlocked && r.UnlockedText == null));
            texts.AssertNothingMissing();
        }

        [Test]
        public void SevenEarned_ReadSevenOutOfThirtySeven_AndAHiddenOneStaysHidden()
        {
            // AC-14.1
            var texts = new TestTexts();
            ProfileModel profile = NewProfile();
            foreach (string id in new[] { "first-cpu", "first-sale", "first-oem", "first-profit", "cash-100m", "mhz-100", "share-10" }) profile.Unlock(id, Entry());

            AchievementsView view = Build(profile, texts);
            Assert.Equal(7, view.Unlocked);
            Assert.Equal("Достижения: 7 / 37", view.Title);

            AchievementRow hidden = Row(view, "phoenix");
            Assert.True(hidden.Hidden);
            Assert.False(hidden.Unlocked);
            Assert.Equal("???", hidden.Title);
            Assert.Equal("Скрытое достижение. Откроется, когда вы его получите.", hidden.Description);
            Assert.Null(hidden.Progress, "a hidden one gives nothing away");
            texts.AssertNothingMissing();
        }

        [Test]
        public void ACategory_CountsItsOwnEarned()
        {
            var texts = new TestTexts();
            ProfileModel profile = NewProfile();
            profile.Unlock("first-cpu", Entry());
            profile.Unlock("first-sale", Entry());
            profile.Unlock("cash-100m", Entry());
            AchievementsView view = Build(profile, texts);

            AchievementCategory career = view.Categories.First(c => c.Id == "career");
            Assert.Equal(2, career.Unlocked);
            Assert.Equal(5, career.Total);
            Assert.Equal("Карьера: 2 / 5", career.Title);
            AchievementCategory money = view.Categories.First(c => c.Id == "money");
            Assert.Equal("Деньги: 1 / 6", money.Title);
        }

        [Test]
        public void AnEarnedAchievement_SaysWhenItWasEarned_AndAHiddenOneShowsItsRealName()
        {
            var texts = new TestTexts();
            ProfileModel profile = NewProfile();
            profile.Unlock("first-cpu", Entry());
            profile.Unlock("phoenix", Entry(true));
            AchievementsView view = Build(profile, texts);

            AchievementRow first = Row(view, "first-cpu");
            Assert.True(first.Unlocked);
            Assert.Equal("Первый кремний", first.Title);
            Assert.Equal("Выпустите свой первый процессор.", first.Description);
            Assert.Equal("Получено 10.10.2026", first.UnlockedText);
            Assert.False(first.Assisted);

            AchievementRow phoenix = Row(view, "phoenix");
            Assert.True(phoenix.Unlocked);
            Assert.True(phoenix.Assisted, "earned in an assisted run: the window marks it");
            Assert.True(phoenix.Title != "???" && phoenix.Description != null && !phoenix.Description.StartsWith("Скрытое"), "an earned hidden achievement is shown in full");
        }

        [Test]
        public void ACountedAchievement_ShowsTheProgress_WhileAGameGoes()
        {
            var texts = new TestTexts();
            ProfileModel profile = NewProfile();

            AchievementsView outside = Build(profile, texts);
            Assert.Null(Row(outside, "cash-100m").Progress, "in the menu there is no game to measure");

            MetricsSnapshot m = Snap("money", 34000000, "oem.active.count", 3, "share.total.avg30", 0.061, "factory.lines", 40, "cpu.maxFrequency", 0);
            AchievementsView inGame = Build(profile, texts, m, Book());
            Assert.Equal("$34,000,000 / $100,000,000", Row(inGame, "cash-100m").Progress);
            Assert.Equal("3 / 10", Row(inGame, "oem-magnate").Progress);
            Assert.Equal("40 / 100", Row(inGame, "megafab").Progress);
            Assert.Equal("6.10% / 10.00%", Row(inGame, "share-10").Progress, "a share is a fraction of one, but it is reached little by little");
            Assert.Null(Row(inGame, "first-oem").Progress, "a plain yes or no has no progress");

            // even with a value in the snapshot the yes-or-no ones stay without a line: one OEM contract, one sale, one profitable month
            MetricsSnapshot withValues = Snap("oem.signed.total", 0, "sales.total", 0, "profit.lastMonth", -5, "rivals.bankruptCount", 0);
            AchievementsView yesNo = Build(profile, texts, withValues, Book());
            foreach (string id in new[] { "first-oem", "first-sale", "first-profit", "outlived-rival" }) Assert.Null(Row(yesNo, id).Progress, id);
            Assert.Null(Row(inGame, "first-cpu").Progress, "an event that happens once has none");
            Assert.Null(Row(inGame, "survivor").Progress);
        }

        [Test]
        public void AnEarnedAchievement_HasNoProgress_AndAnEventThatCountsShowsItsCounter()
        {
            var texts = new TestTexts();
            ProfileModel profile = NewProfile();
            profile.Unlock("cash-100m", Entry());
            AchievementsView view = Build(profile, texts, Snap("money", 500000000), Book());
            Assert.Null(Row(view, "cash-100m").Progress);

            // an event check that needs several happenings: the counter of the book
            var catalog = new List<AchievementDefinition>
            {
                new AchievementDefinition { Id = "three-wins", Category = "meta", TitleKey = "ach.scenario-first.title", DescKey = "ach.scenario-first.desc", Check = ScenarioTestData.Event("scenario.won", 3) },
            };
            EventBook book = Book();
            book.Record("scenario.won");
            book.Record("scenario.won");
            AchievementsView counted = AchievementsViewBuilder.Build(catalog, profile.View, Snap(), book, texts);
            Assert.Equal("2 / 3", counted.Categories[0].Rows[0].Progress);
        }

        [Test]
        public void ACheckThatNeedsNothingMoreThanZero_HasNoProgress_EvenForAShare()
        {
            var texts = new TestTexts();
            var catalog = new List<AchievementDefinition>
            {
                new AchievementDefinition { Id = "any-share", Category = "market", TitleKey = "ach.share-10.title", DescKey = "ach.share-10.desc", Check = ScenarioTestData.Metric("share.total.avg30", ">", 0) },
                new AchievementDefinition { Id = "less-than", Category = "market", TitleKey = "ach.share-10.title", DescKey = "ach.share-10.desc", Check = ScenarioTestData.Metric("money", "<=", 500) },
            };
            AchievementsView view = AchievementsViewBuilder.Build(catalog, NewProfile().View, Snap("share.total.avg30", 0.02, "money", 100), Book(), texts);

            Assert.Null(view.Categories[0].Rows[0].Progress, "'above zero' is reached or it is not");
            Assert.Null(view.Categories[0].Rows[1].Progress, "only 'at least' and 'more than' are progress towards a number");
        }

        [Test]
        public void TheEnglishTables_AnswerEverything()
        {
            var texts = new TestTexts("en");
            ProfileModel profile = NewProfile();
            profile.Unlock("first-cpu", Entry(true));
            AchievementsView view = Build(profile, texts, Snap("money", 1000), Book());

            Assert.Equal("Achievements: 1 / 37", view.Title);
            Assert.Equal("First silicon", Row(view, "first-cpu").Title);
            Assert.Equal("Earned 10.10.2026", Row(view, "first-cpu").UnlockedText);
            Assert.Equal("???", Row(view, "last-standing").Title);
            texts.AssertNothingMissing();
        }

        [Test]
        public void ACategoryOutsideTheVocabulary_IsStillShown_AfterTheOthers()
        {
            var texts = new TestTexts();
            var catalog = new List<AchievementDefinition>
            {
                new AchievementDefinition { Id = "odd", Category = "career", TitleKey = "ach.first-cpu.title", DescKey = "ach.first-cpu.desc", Check = ScenarioTestData.Event("cpu.released.player") },
            };
            AchievementsView view = AchievementsViewBuilder.Build(catalog, NewProfile().View, null, null, texts);
            Assert.Equal(1, view.Categories.Count);
            Assert.Equal(1, view.Total);
        }

        // ---- the records ----

        private static RunInfo Info(string scenarioId = "garage-1975", bool assisted = false)
        {
            return new RunInfo { RunId = "run-1", ScenarioId = scenarioId, Assisted = assisted, Custom = false, Challenges = new string[0] };
        }

        [Test]
        public void AnEmptyProfile_HasNoRecords_ButListsTheStandardScenarios()
        {
            var texts = new TestTexts();
            RecordsView view = AchievementsViewBuilder.BuildRecords(ScenarioTestData.StandardScenarios(), ScenarioTestData.StandardChallenges(), NewProfile().View, texts);

            Assert.True(view.IsEmpty);
            Assert.Equal(8, view.Scenarios.Count);
            Assert.True(view.Scenarios.All(r => !r.HasBest && r.Best == "Лучший результат: пока нет" && r.Attempts == "Попыток: 0, побед: 0"));
            Assert.Equal(8, view.Challenges.Count);
            Assert.True(view.Challenges.All(c => c.Tone == UiTone.Muted && c.Text.EndsWith("ещё не пройдено")));
            texts.AssertNothingMissing();
        }

        [Test]
        public void TheBestResult_ShowsTheScoreTheMedalAndTheDate_WithTheAttempts()
        {
            var texts = new TestTexts();
            ProfileModel profile = NewProfile();
            ScenarioResult lost = new ScenarioResult { Outcome = ScenarioStatus.Lost, Score = 0, Medal = "none", FinishedOn = D(1976, 3, 1) };
            ScenarioResult won = new ScenarioResult { Outcome = ScenarioStatus.Won, Score = 600, Medal = "silver", FinishedOn = D(1977, 1, 1) };
            profile.RecordResult(lost, Info());
            profile.RecordResult(won, Info());

            RecordsView view = AchievementsViewBuilder.BuildRecords(ScenarioTestData.StandardScenarios(), ScenarioTestData.StandardChallenges(), profile.View, texts);
            RecordRow garage = view.Scenarios.First(r => r.ScenarioId == "garage-1975");

            Assert.True(garage.HasBest);
            Assert.Equal("Лучший счёт: 600 — Серебро (01.01.1977)", garage.Best);
            Assert.Equal("Попыток: 2, побед: 1", garage.Attempts);
            Assert.Equal("Гаражный стартап (1975)", garage.Title);
            Assert.False(view.IsEmpty);
        }

        [Test]
        public void AnAssistedWin_MakesNoRecord_ButCountsAsAnAttempt()
        {
            var texts = new TestTexts();
            ProfileModel profile = NewProfile();
            profile.RecordResult(new ScenarioResult { Outcome = ScenarioStatus.Won, Score = 900, Medal = "gold", FinishedOn = D(1977, 1, 1) }, Info(assisted: true));

            RecordRow garage = AchievementsViewBuilder.BuildRecords(ScenarioTestData.StandardScenarios(), null, profile.View, texts).Scenarios.First(r => r.ScenarioId == "garage-1975");
            Assert.False(garage.HasBest);
            Assert.Equal("Попыток: 1, побед: 1", garage.Attempts);
        }

        [Test]
        public void APassedChallenge_ShowsHowManyTimesAndTheBestTime_InTheRightForm()
        {
            var texts = new TestTexts();
            ProfileModel profile = NewProfile();
            profile.RecordChallenge("no-debt", 6);
            profile.RecordChallenge("no-debt", 10);   // the best time is the longest one
            profile.RecordChallenge("taxman", 1);
            profile.RecordChallenge("fickle-market", 3);

            RecordsView view = AchievementsViewBuilder.BuildRecords(null, ScenarioTestData.StandardChallenges(), profile.View, texts);
            string[] lines = view.Challenges.Select(c => c.Text).ToArray();

            Assert.True(lines.Contains("Без долгов: пройдено раз — 2, лучший срок — 10 лет"), string.Join(" | ", lines));
            Assert.True(lines.Contains("Налоговый пресс: пройдено раз — 1, лучший срок — 1 год"), string.Join(" | ", lines));
            Assert.True(lines.Contains("Капризный рынок: пройдено раз — 1, лучший срок — 3 года"), string.Join(" | ", lines));
            Assert.True(lines.Contains("Дорогая наука: ещё не пройдено"));
            Assert.False(view.IsEmpty);
        }

        [Test]
        public void TheCounters_AreShown()
        {
            var texts = new TestTexts();
            ProfileModel profile = NewProfile();
            profile.RecordRunStarted();
            profile.RecordRunStarted();
            profile.RecordResult(new ScenarioResult { Outcome = ScenarioStatus.Lost, Score = 0, Medal = "none", FinishedOn = D(1976, 1, 1) }, Info());

            RecordsView view = AchievementsViewBuilder.BuildRecords(null, null, profile.View, texts);
            Assert.Equal("Игр начато: 2, сценариев выиграно: 0, проиграно: 1", view.Stats);
            Assert.False(view.IsEmpty);
        }

        [Test]
        public void AScenarioThatWasChanged_MakesNoRecordRow()
        {
            var texts = new TestTexts();
            List<ScenarioDefinition> scenarios = ScenarioTestData.StandardScenarios();
            scenarios[0].IsCustom = true;
            RecordsView view = AchievementsViewBuilder.BuildRecords(scenarios, null, NewProfile().View, texts);
            Assert.Equal(7, view.Scenarios.Count);
            Assert.False(view.Scenarios.Any(r => r.ScenarioId == scenarios[0].Id));
        }

        [Test]
        public void TheYearsOfAChallenge_FollowThePluralFormsOfBothLanguages()
        {
            var ru = new TestTexts();
            Assert.Equal("1 год", ru.Plural("ach.records.years", 1, 1));
            Assert.Equal("3 года", ru.Plural("ach.records.years", 3, 3));
            Assert.Equal("11 лет", ru.Plural("ach.records.years", 11, 11));
            Assert.Equal("21 год", ru.Plural("ach.records.years", 21, 21));

            var en = new TestTexts("en");
            Assert.Equal("1 year", en.Plural("ach.records.years", 1, 1));
            Assert.Equal("10 years", en.Plural("ach.records.years", 10, 10));
            ru.AssertNothingMissing();
            en.AssertNothingMissing();
        }

        [Test]
        public void ARequiredArgumentThatIsNull_IsRefused()
        {
            var texts = new TestTexts();
            Assert.Throws<ArgumentNullException>(() => AchievementsViewBuilder.Build(null, null, null, null, texts));
            Assert.Throws<ArgumentNullException>(() => AchievementsViewBuilder.Build(null, NewProfile().View, null, null, null));
            Assert.Throws<ArgumentNullException>(() => AchievementsViewBuilder.BuildRecords(null, null, null, texts));
            Assert.Equal(0, AchievementsViewBuilder.Build(null, NewProfile().View, null, null, texts).Total, "no catalog: an empty page");
        }
    }
}
