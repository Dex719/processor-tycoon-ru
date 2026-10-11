using System;
using System.Collections.Generic;
using System.Linq;
using PTMod.Features.ScenariosAchievements.Logic;
using static PTMod.Tests.Features.ScenariosAchievements.RunTestData;
using static PTMod.Tests.Features.ScenariosAchievements.UiTestData;

namespace PTMod.Tests.Features.ScenariosAchievements
{
    /// <summary>
    /// TSK-023, TSK-025: the results of a scenario (FR-9 items 1, 2 and 5, FR-8, AC-8.1, AC-9.1): the outcome, the reason, the goals with their dates, the score taken apart, the medal, the flags, the
    /// achievements and the challenges of the run, the text of the table that compares the companies, and the arguments of the letter with the result.
    /// </summary>
    [Covers(typeof(ResultBuilder), typeof(ResultView), typeof(ResultGoalLine), typeof(ComparisonTableBuilder), typeof(ComparisonCells))]
    public class ResultBuilderTests
    {
        private static readonly string[] AllGoals = { "g1", "g2", "g3", "g4" };

        // garage-1975 won on 1977-01-01 with the four goals: AC-8.1
        private static RunState WonGarage(out ScenarioResult result, params ChallengeDefinition[] challenges)
        {
            RunState run = Run("garage-1975", challenges.Select(c => c.Id).ToArray());
            DateTime day = D(1975, 7, 1);
            foreach (string goal in AllGoals) Done(run, goal, day);
            result = Won(run, D(1977, 1, 1), challenges);
            return run;
        }

        private static ResultView Build(RunState run, ScenarioResult result, TestTexts texts, Func<string, string> featureName = null)
        {
            return ResultBuilder.Build(run, result, AchievementLookup(), ChallengeLookup(), texts, featureName);
        }

        [Test]
        public void AWin_ShowsTheOutcomeTheScoreTheMedalAndTheDate()
        {
            // AC-8.1: 600 and silver
            var texts = new TestTexts();
            ScenarioResult result;
            RunState run = WonGarage(out result);
            ResultView view = Build(run, result, texts);

            Assert.True(view.IsWin);
            Assert.Equal("Гаражный стартап (1975)", view.ScenarioTitle);
            Assert.Equal("Сценарий выполнен", view.Headline);
            Assert.Null(view.Reason);
            Assert.Equal("Дата: 01.01.1977", view.Date);
            Assert.Equal(600, view.Score);
            Assert.Equal("Счёт: 600", view.ScoreText);
            Assert.Equal("silver", view.Medal);
            Assert.Equal("Медаль: Серебро", view.MedalText);
            texts.AssertNothingMissing();
        }

        [Test]
        public void TheScore_IsShownInItsParts()
        {
            var texts = new TestTexts();
            ScenarioResult result;
            RunState run = WonGarage(out result);
            string[] lines = Build(run, result, texts).Breakdown.Select(l => l.Text).ToArray();

            Assert.Equal(new[] { "Цели: 500", "Бонус за время: 100", "Итого: 600" }, lines, "no line for a bonus of challenges that were not chosen");
        }

        [Test]
        public void TheBonusOfTheChallenges_HasItsLine_AndIsPartOfTheTotal()
        {
            // (500 + 100) x (1 + 15% + 10%) = 750, which is gold for the garage
            var texts = new TestTexts();
            ScenarioResult result;
            RunState run = WonGarage(out result, Challenge("expensive-research"), Challenge("taxman"));
            ResultView view = Build(run, result, texts);

            Assert.Equal(750, view.Score);
            Assert.Equal("gold", view.Medal);
            Assert.Equal(new[] { "Цели: 500", "Бонус за время: 100", "Бонус испытаний: +25%", "Итого: 750" }, view.Breakdown.Select(l => l.Text).ToArray());
        }

        [Test]
        public void TheGoals_ShowHowEachEnded()
        {
            var texts = new TestTexts();
            RunState run = Run("garage-1975");
            Done(run, "g1", D(1975, 11, 12));
            Failed(run, "g2", D(1976, 2, 3));
            ScenarioResult result = Lost(run, D(1976, 2, 3), "goal:g2", "scn.fail.goal", "scn.garage-1975.g2");
            ResultView view = Build(run, result, texts);

            Assert.Equal(4, view.Goals.Count);
            ResultGoalLine done = view.Goals.First(g => g.GoalId == "g1");
            Assert.Equal("выполнена 12.11.1975, +100", done.Detail);
            Assert.Equal(UiTone.Good, done.Tone);
            Assert.Equal(100, done.Points);
            Assert.True(done.Required);

            ResultGoalLine failed = view.Goals.First(g => g.GoalId == "g2");
            Assert.Equal("провалена 03.02.1976", failed.Detail);
            Assert.Equal(UiTone.Bad, failed.Tone);

            ResultGoalLine open = view.Goals.First(g => g.GoalId == "g3");
            Assert.Equal("не выполнена", open.Detail);
            Assert.Equal(UiTone.Muted, open.Tone);
            Assert.False(view.Goals.First(g => g.GoalId == "g4").Required, "the optional goal says it is optional");
        }

        [Test]
        public void ALoss_ShowsTheReason_AndAScoreOfZero_WithoutAMedal()
        {
            var texts = new TestTexts();
            RunState run = Run("garage-1975");
            Done(run, "g2", D(1975, 5, 5));
            ScenarioResult result = Lost(run, D(1978, 1, 1), "deadline", "scn.fail.deadline");
            ResultView view = Build(run, result, texts);

            Assert.False(view.IsWin);
            Assert.Equal("Сценарий провален", view.Headline);
            Assert.Equal("Причина: Время вышло", view.Reason);
            Assert.Equal(0, view.Score);
            Assert.Equal("Счёт: 0", view.ScoreText);
            Assert.Null(view.MedalText);
            Assert.Equal("none", view.Medal);
            Assert.Equal(UiTone.Bad, view.Breakdown.Last().Tone);
            Assert.Equal("Итого: 0", view.Breakdown.Last().Text);
            texts.AssertNothingMissing();
        }

        [Test]
        public void AFailedGoalAsTheReason_IsNamedByItsTitle_NotByItsKey()
        {
            // AC-5.2: the first CPU was not out by 1 July 1976
            var texts = new TestTexts();
            RunState run = Run("garage-1975");
            ScenarioResult result = Lost(run, D(1976, 7, 1), "goal:g1", "scn.fail.goal", "scn.garage-1975.g1");

            Assert.Equal("Причина: Не выполнена цель: Выпустить первый процессор до 1 июля 1976", Build(run, result, texts).Reason);
        }

        [Test]
        public void ALostRace_NamesTheWinnerAndTheDate()
        {
            var texts = new TestTexts();
            RunState run = Run("mhz-race-1990");
            ScenarioResult result = Lost(run, D(1993, 3, 14), "race:g1", "scn.fail.raceLost", "Intel", "1993-03-14");

            Assert.Equal("Причина: Гонку выиграла Intel (14.03.1993)", Build(run, result, texts).Reason);

            var english = new TestTexts("en");
            Assert.Equal("Reason: Intel won the race (14.03.1993)", Build(run, result, english).Reason);
        }

        [Test]
        public void TheReasonOfARule_IsItsOwnText()
        {
            var texts = new TestTexts();
            Assert.Equal("Нарушено правило fabless: построена фабрика",
                ResultBuilder.ReasonText(new ScenarioResult { FailTextKey = "scn.fail.fablessRule" }, null, texts));
            Assert.Equal(string.Empty, ResultBuilder.ReasonText(new ScenarioResult(), null, texts), "a win has none");
            Assert.Equal(string.Empty, ResultBuilder.ReasonText(null, null, texts));
        }

        [Test]
        public void TheFlags_TellWhyTheRunIsAssisted_AndThatTheScenarioWasChanged()
        {
            var texts = new TestTexts();
            ScenarioResult result;
            RunState run = WonGarage(out result);
            Assert.Equal(0, Build(run, result, texts).Flags.Count);

            run.MarkAssisted("cheats.enabled");
            run.Custom = true;
            string[] flags = Build(run, result, texts).Flags.Select(f => f.Text).ToArray();
            Assert.Equal(new[] { "С помощью: включены читы", "Изменённый сценарий: рекорды не записываются" }, flags);
        }

        [Test]
        public void TheAchievementsOfTheRun_AreListedByTitle_AndMarkedWhenAssisted()
        {
            var texts = new TestTexts();
            ScenarioResult result;
            RunState run = WonGarage(out result);
            run.AchievementsThisRun.AddRange(new[] { "first-cpu", "no-such-achievement", "first-sale" });

            ResultView view = Build(run, result, texts);
            Assert.Equal(new[] { texts.Text("ach.first-cpu.title"), texts.Text("ach.first-sale.title") }, view.Achievements.ToArray(), "an id the catalog does not know is left out");
            Assert.Equal("Первый кремний", view.Achievements[0]);

            run.MarkAssisted("cheats.enabled");
            Assert.Equal("Первый кремний (с помощью)", Build(run, result, texts).Achievements[0]);
        }

        [Test]
        public void TheChallenges_ShowHowTheyStand()
        {
            var texts = new TestTexts();
            RunState run = Run("garage-1975", "expensive-research", "taxman", "no-debt");
            run.ChallengeStates.Add("expensive-research", new string[0]);
            run.ChallengeStates.Add("taxman", new string[0]).Status = ChallengeStatus.Passed;
            run.ChallengeStates.Add("no-debt", new string[0]).Status = ChallengeStatus.Failed;
            ScenarioResult result = Lost(run, D(1976, 1, 1), "deadline", "scn.fail.deadline");

            ResultView view = Build(run, result, texts);
            Assert.Equal(new[] { "Дорогая наука — идёт", "Налоговый пресс — пройдено", "Без долгов — провалено" }, view.Challenges.Select(c => c.Text).ToArray());
            Assert.Equal(new[] { UiTone.Normal, UiTone.Good, UiTone.Bad }, view.Challenges.Select(c => c.Tone).ToArray());
            texts.AssertNothingMissing();
        }

        [Test]
        public void TheLetter_CarriesTheOutcomeTheGoalsAndTheScore()
        {
            var texts = new TestTexts();
            ScenarioResult result;
            RunState run = WonGarage(out result);
            ResultView view = Build(run, result, texts);
            string[] args = ResultBuilder.MailArgs(view, texts);

            Assert.Equal(4, args.Length);
            Assert.Equal("Гаражный стартап (1975)", args[0]);
            Assert.Equal("Сценарий выполнен\nДата: 01.01.1977\nСчёт: 600\nМедаль: Серебро", args[1]);
            string[] goals = args[2].Split('\n');
            Assert.Equal("Цели", goals[0]);
            Assert.Equal(5, goals.Length);
            Assert.True(goals[1].StartsWith("• Выпустить первый процессор до 1 июля 1976 — выполнена 01.07.1975"), goals[1]);
            Assert.True(args[3].Contains("Итого: 600"), args[3]);

            // what the core makes of the keys and the arguments
            object[] objects = args.Cast<object>().ToArray();
            Assert.Equal("Итоги: Гаражный стартап (1975)", texts.Text("scn.result.mailSubject", objects));
            Assert.Equal(args[1] + "\n\n" + args[2] + "\n\n" + args[3], texts.Text("scn.mail.result.body", objects));
            texts.AssertNothingMissing();
        }

        [Test]
        public void ALossLetter_CarriesTheReason()
        {
            var texts = new TestTexts();
            RunState run = Run("garage-1975");
            ScenarioResult result = Lost(run, D(1978, 1, 1), "deadline", "scn.fail.deadline");
            string[] args = ResultBuilder.MailArgs(Build(run, result, texts), texts);

            Assert.Equal("Сценарий провален\nПричина: Время вышло\nДата: 01.01.1978\nСчёт: 0", args[1]);
        }

        [Test]
        public void EveryShippedScenario_BuildsItsResults_InBothLanguages_WithoutMissingTexts()
        {
            foreach (string language in new[] { "ru", "en" })
            {
                var texts = new TestTexts(language);
                foreach (string id in ScenarioTestData.ScenarioIds)
                {
                    RunState won = Run(id);
                    foreach (GoalSpec goal in won.Scenario.Goals) Done(won, goal.Id, won.StartDate.AddDays(30));
                    ScenarioResult result = Won(won, won.StartDate.AddDays(400));
                    ResultView view = Build(won, result, texts);
                    Assert.Equal(won.Scenario.Goals.Length, view.Goals.Count, id);
                    Assert.True(ResultBuilder.MailArgs(view, texts).All(a => a != null), id);

                    RunState lost = Run(id);
                    ScenarioResult loss = Lost(lost, lost.StartDate.AddDays(10), "bankrupt", "scn.fail.bankrupt");
                    Assert.Equal(language == "ru" ? "Причина: Банкротство" : "Reason: Bankruptcy", Build(lost, loss, texts).Reason, id);
                }
                texts.AssertNothingMissing();
            }
        }

        // ---- the table that compares the companies ----

        private static ComparisonRow Company(string name, bool player, double money, double revenue, double share, int cpus, float frequency, bool bankrupt = false)
        {
            return new ComparisonRow { Name = name, IsPlayer = player, Money = money, Revenue12m = revenue, MarketShare = share, CpuCount = cpus, MaxFrequency = frequency, IsBankrupt = bankrupt };
        }

        [Test]
        public void TheTable_HasSevenColumns_AndARowPerCompany()
        {
            // AC-9.1: at least two rows - the player and an AI company
            var texts = new TestTexts();
            string[] header = ComparisonTableBuilder.Header(texts);
            Assert.Equal(new[] { "Компания", "Деньги", "Выручка 12 мес.", "Доля рынка", "CPU", "Макс. частота", "Банкрот" }, header);

            List<ComparisonCells> rows = ComparisonTableBuilder.Rows(new[]
            {
                Company("Garage CPU Inc.", true, 5200000, 12000000, 0.125, 7, 450000),
                Company("Intel", false, 900000000, 400000000, 0.4, 31, 1200000),
                Company("Dead Corp", false, -3000, 0, 0, 2, 0, true),
            }, texts);

            Assert.Equal(3, rows.Count);
            Assert.True(rows[0].IsPlayer);
            Assert.Equal("Garage CPU Inc.", rows[0].Name);
            Assert.Equal("$5,200,000", rows[0].Money);
            Assert.Equal("$12,000,000", rows[0].Revenue);
            Assert.Equal("12.50%", rows[0].Share);
            Assert.Equal("7", rows[0].Cpus);
            Assert.Equal("450 MHz", rows[0].Frequency);
            Assert.Equal(string.Empty, rows[0].Bankrupt);

            Assert.Equal("1.2 GHz", rows[1].Frequency);
            Assert.True(rows[2].IsBankrupt);
            Assert.Equal("да", rows[2].Bankrupt);
            Assert.Equal("—", rows[2].Frequency, "no CPU released: a dash, not 0 MHz");
            texts.AssertNothingMissing();
        }

        [Test]
        public void TheTable_OfNothing_IsEmpty()
        {
            var texts = new TestTexts();
            Assert.Equal(0, ComparisonTableBuilder.Rows(null, texts).Count);
            Assert.Equal(0, ComparisonTableBuilder.Rows(new ComparisonRow[0], texts).Count);
            Assert.Equal(1, ComparisonTableBuilder.Rows(new ComparisonRow[] { null, Company("A", true, 1, 1, 1, 1, 1) }, texts).Count, "a null row is skipped");
        }

        [Test]
        public void ARequiredArgumentThatIsNull_IsRefused()
        {
            var texts = new TestTexts();
            ScenarioResult result;
            RunState run = WonGarage(out result);
            Assert.Throws<ArgumentNullException>(() => ResultBuilder.Build(null, result, null, null, texts));
            Assert.Throws<ArgumentNullException>(() => ResultBuilder.Build(run, null, null, null, texts));
            Assert.Throws<ArgumentNullException>(() => ResultBuilder.Build(run, result, null, null, null));
            Assert.Throws<ArgumentNullException>(() => ResultBuilder.MailArgs(null, texts));
        }
    }
}
