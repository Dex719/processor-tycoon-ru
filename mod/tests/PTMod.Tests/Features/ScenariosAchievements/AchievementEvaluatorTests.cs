using System;
using System.Collections.Generic;
using System.Linq;
using PTMod.Core.Logic.Json;
using PTMod.Features.ScenariosAchievements.Logic;
using static PTMod.Tests.Features.ScenariosAchievements.RunTestData;

namespace PTMod.Tests.Features.ScenariosAchievements
{
    /// <summary>
    /// TSK-012: which achievements have just come true (FR-12). Every kind of check is read on the real 37 achievements of the content or on small
    /// ones of our own - <c>event</c>, <c>metric</c>, <c>cpuReleased</c>, <c>condition</c>, <c>all</c>, <c>profile</c>, <c>require</c> - the earned ones are not
    /// looked at again, a legacy run does not count the achievements that need the whole history, a pass of many gives three pop-ups and
    /// "and N more", the CPUs received by a transfer do not open "Gigahertz", and a company the player absorbed does not count as outlived.
    /// </summary>
    [Covers(typeof(AchievementEvaluator), typeof(AchievementToasts), typeof(EventBook))]
    public class AchievementEvaluatorTests
    {
        private static readonly List<AchievementDefinition> Standard = ScenarioTestData.StandardAchievements();

        private static AchievementEvaluator Evaluator()
        {
            return new AchievementEvaluator(Standard, ScenarioTestData.ScenarioIds);
        }

        private static RunInfo Run(bool legacy = false, int difficultyAtStart = 2, int difficulty = 2, string companyType = "idm", int challenges = 0,
                                   bool assisted = false, bool strict = false)
        {
            return new RunInfo
            {
                RunId = "run-1",
                IsLegacy = legacy,
                DifficultyAtStart = difficultyAtStart,
                DifficultyLevel = difficulty,
                CompanyType = companyType,
                GameDate = D(1990, 1, 1),
                Assisted = assisted,
                StrictAssisted = strict,
                Challenges = Enumerable.Range(0, challenges).Select(i => "chl-" + i).ToList(),
            };
        }

        private static ProfileView EmptyProfile()
        {
            return new ProfileModel(new JsonObject()).View;
        }

        private static AchievementDefinition Custom(string id, CheckSpec check, string cadence = null, bool onlyNew = false)
        {
            check.Cadence = cadence ?? check.Cadence;
            return new AchievementDefinition { Id = id, Category = "meta", Points = 1, TitleKey = "ach." + id + ".title", DescKey = "ach." + id + ".desc", Check = check, CountsOnlyNewGames = onlyNew };
        }

        private static IList<string> Daily(AchievementEvaluator e, MetricsSnapshot m, EventBook book = null, ProfileView profile = null, RunInfo run = null,
                                           IConditionAdapter conditions = null)
        {
            return e.Evaluate("daily", m, book ?? Book(), profile ?? EmptyProfile(), run ?? Run(), conditions);
        }

        // ---- event ----

        [Test]
        public void AnEvent_OpensItsAchievement_WhenItHappens_AC121()
        {
            EventBook book = Book();
            book.RecordCpuRelease(Cpu(1), true);

            IList<string> earned = Evaluator().OnEvent("cpu.released.player", book, Snap(), EmptyProfile(), Run());
            Assert.True(earned.Contains("first-cpu"), "'First silicon' on the first CPU of the player");
            Assert.False(earned.Contains("scenario-first"));
        }

        [Test]
        public void AnEventAchievement_WaitsForItsCount()
        {
            var many = Custom("ten-cpus", Event("cpu.released.player", 10));
            var evaluator = new AchievementEvaluator(new[] { many });
            EventBook book = Book();
            for (int i = 0; i < 9; i++) book.RecordCpuRelease(Cpu(i), true);
            Assert.Equal(0, evaluator.OnEvent("cpu.released.player", book, Snap(), EmptyProfile(), Run()).Count);

            book.RecordCpuRelease(Cpu(9), true);
            Assert.Equal(new[] { "ten-cpus" }, evaluator.OnEvent("cpu.released.player", book, Snap(), EmptyProfile(), Run()).ToArray());
        }

        [Test]
        public void AnEvent_LooksOnlyAtTheAchievementsItCanChange()
        {
            EventBook book = Book();
            book.Record("cpu.released.player");
            book.Record("scenario.won");

            IList<string> onWon = Evaluator().OnEvent("scenario.won", book, Snap(), EmptyProfile(), Run());
            Assert.True(onWon.Contains("scenario-first"));
            Assert.False(onWon.Contains("first-cpu"), "its own event did not come: it is the CPU event's business");

            IList<string> onCpu = Evaluator().OnEvent("cpu.released.player", book, Snap(), EmptyProfile(), Run());
            Assert.True(onCpu.Contains("first-cpu"));
            Assert.False(onCpu.Contains("scenario-first"));

            Assert.Equal(0, Evaluator().OnEvent(null, book, Snap(), EmptyProfile(), Run()).Count);
            Assert.Equal(0, Evaluator().OnEvent("", book, Snap(), EmptyProfile(), Run()).Count);
            Assert.Equal(0, Evaluator().OnEvent("oem.signed", book, Snap(), EmptyProfile(), Run()).Count, "nobody waits for it");
        }

        [Test]
        public void TheEventSweep_FindsWhatWaitsForEvents_OnStartOrLoad()
        {
            EventBook book = Book();
            book.Record("scenario.won");
            book.Record("scenario.gold");
            book.Record("challenge.passed");

            IList<string> earned = Evaluator().Evaluate("event", Snap(), book, EmptyProfile(), Run(), null);
            Assert.True(earned.Contains("scenario-first") && earned.Contains("scenario-gold") && earned.Contains("challenger"));
            Assert.False(earned.Contains("cash-100m"), "daily and monthly achievements are not part of the event sweep");
        }

        // ---- metric ----

        [Test]
        public void AMetric_OpensItsAchievement_OnItsCadence()
        {
            var daily = Daily(Evaluator(), Snap("money", 150000000.0));
            Assert.True(daily.Contains("cash-100m"));
            Assert.False(daily.Contains("cash-1b"), "a billion is far");

            IList<string> monthly = Evaluator().Evaluate("monthly", Snap("profit.lastMonth", 1000.0), Book(), EmptyProfile(), Run(), null);
            Assert.True(monthly.Contains("first-profit"), "'In the black' is checked when a month closes");
            Assert.False(Daily(Evaluator(), Snap("profit.lastMonth", 1000.0)).Contains("first-profit"), "not on the daily pass");
            Assert.False(Evaluator().Evaluate("monthly", Snap("money", 150000000.0), Book(), EmptyProfile(), Run(), null).Contains("cash-100m"));
        }

        [Test]
        public void TheMetricsOfTheStreaks_AndTheShares_AreRead()
        {
            IList<string> earned = Daily(Evaluator(), Snap(
                "money.solventStreakDays", 1900, "share.total.avg30", 0.12, "share.minSegment.avg30", 0.02, "ips.leaderStreakDays", 400,
                "oem.active.count", 10, "factory.lines", 100, "popularity.max", 0.9995));

            Assert.True(earned.Contains("no-debt-5y"));
            Assert.True(earned.Contains("share-10"));
            Assert.False(earned.Contains("share-50"));
            Assert.False(earned.Contains("all-segments"), "the weakest segment is at 2%");
            Assert.True(earned.Contains("performance-crown"));
            Assert.True(earned.Contains("oem-magnate"));
            Assert.True(earned.Contains("megafab"));
            Assert.True(earned.Contains("peoples-cpu"), "a hidden achievement is judged like any other");
        }

        [Test]
        public void AMetricThatIsNotInTheSnapshot_KeepsTheAchievementClosed()
        {
            Assert.Equal(0, Daily(Evaluator(), Snap()).Count);
            Assert.Equal(0, Daily(Evaluator(), null).Count, "no snapshot at all");
        }

        // ---- cpuReleased ----

        [Test]
        public void ACpuRelease_OpensTheTechAchievements_ItMatches()
        {
            EventBook book = Book();
            Assert.True(book.RecordCpuRelease(Cpu(1, frequency: 1200000, cores: 2, smt: true, releasedOn: D(1998, 6, 1)), true));

            IList<string> earned = Evaluator().OnEvent("cpu.released.player", book, Snap(), EmptyProfile(), Run());
            foreach (string id in new[] { "mhz-100", "ghz-1", "dual-core", "smt", "ahead-of-time" })
            {
                Assert.True(earned.Contains(id), id);
            }
            Assert.False(earned.Contains("octa-core"), "two cores are not eight");
            Assert.False(earned.Contains("ghz-2-fabless"), "1.2 GHz is not 2 GHz");
        }

        [Test]
        public void TheFablessAchievement_NeedsTheCompanyTypeAtTheMomentOfRelease()
        {
            EventBook idm = Book();
            idm.RecordCpuRelease(Cpu(1, frequency: 2500000, companyType: "idm"), true);
            Assert.False(Evaluator().OnEvent("cpu.released.player", idm, Snap(), EmptyProfile(), Run()).Contains("ghz-2-fabless"));

            EventBook fabless = Book();
            fabless.RecordCpuRelease(Cpu(2, frequency: 2500000, companyType: "fabless"), true);
            Assert.True(Evaluator().OnEvent("cpu.released.player", fabless, Snap(), EmptyProfile(), Run()).Contains("ghz-2-fabless"));
        }

        [Test]
        public void TheGigahertzBefore2000_IsAHiddenAchievement_ThatNeedsTheDate()
        {
            EventBook late = Book();
            late.RecordCpuRelease(Cpu(1, frequency: 1500000, releasedOn: D(2001, 1, 1)), true);
            IList<string> lateEarned = Evaluator().OnEvent("cpu.released.player", late, Snap(), EmptyProfile(), Run());
            Assert.True(lateEarned.Contains("ghz-1"));
            Assert.False(lateEarned.Contains("ahead-of-time"), "after the year 2000 it is no longer ahead of time");

            EventBook early = Book();
            early.RecordCpuRelease(Cpu(2, frequency: 1500000, releasedOn: D(1999, 12, 31)), true);
            Assert.True(Evaluator().OnEvent("cpu.released.player", early, Snap(), EmptyProfile(), Run()).Contains("ahead-of-time"));
        }

        [Test]
        public void ACpuReceivedByATransfer_DoesNotOpenGigahertz()
        {
            EventBook book = Book();
            book.RecordTransfer(77);
            Assert.True(book.IsTransferred(77));
            Assert.False(book.RecordCpuRelease(Cpu(77, frequency: 3000000, cores: 8, smt: true), true), "a transferred CPU is not a release");
            Assert.Equal(0, book.PlayerReleases.Count);
            Assert.Equal(0, book.Count("cpu.released.player"));

            IList<string> earned = Evaluator().OnEvent("cpu.released.player", book, Snap(), EmptyProfile(), Run());
            Assert.False(earned.Contains("ghz-1") || earned.Contains("octa-core") || earned.Contains("first-cpu"));

            Assert.True(book.RecordCpuRelease(Cpu(78, frequency: 1100000), true), "the player's own CPU still counts");
            Assert.True(Evaluator().OnEvent("cpu.released.player", book, Snap(), EmptyProfile(), Run()).Contains("ghz-1"));
        }

        // ---- all ----

        [Test]
        public void AnAll_NeedsEveryChild_Phoenix()
        {
            Assert.Equal(0, Daily(Evaluator(), Snap("money.min", -5000000.0, "money", 200000000.0)).Count(id => id == "phoenix"), "the dip was too shallow");
            Assert.Equal(0, Daily(Evaluator(), Snap("money.min", -20000000.0, "money", 50000000.0)).Count(id => id == "phoenix"), "not rich yet");
            Assert.True(Daily(Evaluator(), Snap("money.min", -20000000.0, "money", 200000000.0)).Contains("phoenix"));
        }

        // ---- condition ----

        [Test]
        public void ACondition_GoesThroughTheAdapter()
        {
            AchievementDefinition def = Custom("cond", Condition());
            var evaluator = new AchievementEvaluator(new[] { def });
            var fake = new FakeConditions { Answer = true };

            Assert.Equal(new[] { "cond" }, Daily(evaluator, Snap(), conditions: fake).ToArray());
            fake.Answer = false;
            Assert.Equal(0, Daily(evaluator, Snap(), conditions: fake).Count);
            fake.Answer = null;
            Assert.Equal(0, Daily(evaluator, Snap(), conditions: fake).Count, "'cannot be evaluated' is not true");
            Assert.Equal(0, Daily(evaluator, Snap(), conditions: null).Count, "no adapter at all");
        }

        // ---- profile ----

        [Test]
        public void TheProfileRule_NeedsAGoldInEveryStandardScenario()
        {
            var model = new ProfileModel(new JsonObject());
            foreach (string id in ScenarioTestData.ScenarioIds.Take(7))
            {
                model.RecordResult(new ScenarioResult { Outcome = ScenarioStatus.Won, Score = 900, Medal = "gold", FinishedOn = D(1980, 1, 1) },
                    new RunInfo { ScenarioId = id, RunId = "r-" + id });
            }
            Assert.False(Evaluator().OnEvent("scenario.gold", Book(), Snap(), model.View, Run()).Contains("scenario-all-gold"), "seven of eight");

            model.RecordResult(new ScenarioResult { Outcome = ScenarioStatus.Won, Score = 900, Medal = "gold", FinishedOn = D(1980, 1, 1) },
                new RunInfo { ScenarioId = ScenarioTestData.ScenarioIds[7], RunId = "r-last" });
            Assert.True(Evaluator().OnEvent("scenario.gold", Book(), Snap(), model.View, Run()).Contains("scenario-all-gold"));
            Assert.True(Evaluator().OnEvent("scenario.won", Book(), Snap(), model.View, Run()).Contains("scenario-all-gold"));
            Assert.False(Evaluator().OnEvent("cpu.released.player", Book(), Snap(), model.View, Run()).Contains("scenario-all-gold"), "a CPU cannot change the profile");
        }

        [Test]
        public void TheProfileRule_IsNeverTrue_WithoutTheStandardScenariosKnown()
        {
            var model = new ProfileModel(new JsonObject());
            model.RecordResult(new ScenarioResult { Outcome = ScenarioStatus.Won, Score = 900, Medal = "gold" }, new RunInfo { ScenarioId = "garage-1975" });
            var evaluator = new AchievementEvaluator(Standard);
            Assert.False(evaluator.OnEvent("scenario.gold", Book(), Snap(), model.View, Run()).Contains("scenario-all-gold"));
        }

        // ---- require ----

        [Test]
        public void TheImpossibleDecade_NeedsTheDifficulty_AndNoBankruptcy()
        {
            MetricsSnapshot ten = Snap("run.days", 3650);
            Assert.True(Daily(Evaluator(), ten, run: Run(difficultyAtStart: 5, difficulty: 5)).Contains("impossible-decade"));
            Assert.False(Daily(Evaluator(), ten, run: Run(difficultyAtStart: 4, difficulty: 4)).Contains("impossible-decade"), "Hard is not Impossible");
            Assert.False(Daily(Evaluator(), Snap("run.days", 3649), run: Run(difficultyAtStart: 5, difficulty: 5)).Contains("impossible-decade"), "one day short");

            EventBook bankrupt = Book();
            bankrupt.RecordPlayerBankrupt();
            Assert.False(Daily(Evaluator(), ten, bankrupt, run: Run(difficultyAtStart: 5, difficulty: 5)).Contains("impossible-decade"));
        }

        [Test]
        public void TheDifficulty_MustHaveBeenHeldFromTheStart()
        {
            MetricsSnapshot ten = Snap("run.days", 3650);
            Assert.False(Daily(Evaluator(), ten, run: Run(difficultyAtStart: 5, difficulty: 3)).Contains("impossible-decade"), "lowered later");
            Assert.False(Daily(Evaluator(), ten, run: Run(difficultyAtStart: 2, difficulty: 5)).Contains("impossible-decade"), "raised only at the end");
        }

        [Test]
        public void TheCompanyTypeRequirement_ReadsTheLiveType()
        {
            var check = Metric("money", ">=", 1);
            check.Require = new RequireSpec { CompanyType = "fabless" };
            var evaluator = new AchievementEvaluator(new[] { Custom("fab", check) });
            Assert.Equal(1, Daily(evaluator, Snap("money", 5), run: Run(companyType: "fabless")).Count);
            Assert.Equal(0, Daily(evaluator, Snap("money", 5), run: Run(companyType: "idm")).Count);
            Assert.Equal(0, Daily(evaluator, Snap("money", 5), run: Run(companyType: null)).Count, "an unknown type is not fabless");
        }

        [Test]
        public void TheMasochist_NeedsThreeChallenges_AndAWin()
        {
            EventBook won = Book();
            won.Record("scenario.won");
            Assert.True(Evaluator().OnEvent("scenario.won", won, Snap(), EmptyProfile(), Run(challenges: 3)).Contains("masochist"));
            Assert.True(Evaluator().OnEvent("scenario.won", won, Snap(), EmptyProfile(), Run(challenges: 4)).Contains("masochist"));
            Assert.False(Evaluator().OnEvent("scenario.won", won, Snap(), EmptyProfile(), Run(challenges: 2)).Contains("masochist"));
            Assert.True(Evaluator().OnEvent("scenario.won", won, Snap(), EmptyProfile(), Run(challenges: 2)).Contains("scenario-first"));
        }

        [Test]
        public void ARequirementThatCannotBeVerified_IsNotTrue()
        {
            // 'not bankrupt' needs the book of events; without it the run cannot be shown to be clean
            var evaluator = new AchievementEvaluator(Standard, ScenarioTestData.ScenarioIds);
            IList<string> earned = evaluator.Evaluate("daily", Snap("run.days", 3650), null, EmptyProfile(), Run(difficultyAtStart: 5, difficulty: 5), null);
            Assert.False(earned.Contains("impossible-decade"));
        }

        [Test]
        public void TheEndOfEra_NeedsNoBankruptcy()
        {
            EventBook clean = Book();
            clean.Record("endyear.reached");
            Assert.True(Evaluator().OnEvent("endyear.reached", clean, Snap(), EmptyProfile(), Run()).Contains("end-of-era"));

            EventBook bankrupt = Book();
            bankrupt.Record("endyear.reached");
            bankrupt.RecordPlayerBankrupt();
            Assert.False(Evaluator().OnEvent("endyear.reached", bankrupt, Snap(), EmptyProfile(), Run()).Contains("end-of-era"));
        }

        // ---- the earned ones, the legacy runs ----

        [Test]
        public void AnEarnedAchievement_IsNotCheckedAgain_AC122()
        {
            AchievementDefinition def = Custom("cond", Condition());
            var evaluator = new AchievementEvaluator(new[] { def });
            var model = new ProfileModel(new JsonObject());
            var fake = new FakeConditions { Answer = true };

            Assert.Equal(1, Daily(evaluator, Snap(), profile: model.View, conditions: fake).Count);
            Assert.Equal(1, fake.Calls);

            model.Unlock("cond", new AchievementEntry { RunId = "earlier-game", GameDate = D(1980, 1, 1) });
            Assert.Equal(0, Daily(evaluator, Snap(), profile: model.View, conditions: fake).Count, "earned in another game: no second notice");
            Assert.Equal(1, fake.Calls, "the check was not even run");

            EventBook book = Book();
            book.RecordCpuRelease(Cpu(1), true);
            model.Unlock("first-cpu", new AchievementEntry());
            Assert.False(Evaluator().OnEvent("cpu.released.player", book, Snap(), model.View, Run()).Contains("first-cpu"));
        }

        [Test]
        public void ALegacyRun_DoesNotCountTheAchievementsThatNeedTheWholeHistory()
        {
            EventBook book = Book();
            book.Record("endyear.reached");
            Assert.False(Evaluator().OnEvent("endyear.reached", book, Snap(), EmptyProfile(), Run(legacy: true)).Contains("end-of-era"));
            Assert.True(Evaluator().OnEvent("endyear.reached", book, Snap(), EmptyProfile(), Run(legacy: false)).Contains("end-of-era"));

            MetricsSnapshot ten = Snap("run.days", 3650);
            Assert.False(Daily(Evaluator(), ten, run: Run(legacy: true, difficultyAtStart: 5, difficulty: 5)).Contains("impossible-decade"));

            // the others are checked as usual, in a legacy run too (AC-16.2)
            Assert.True(Evaluator().Evaluate("monthly", Snap("profit.lastMonth", 100.0), Book(), EmptyProfile(), Run(legacy: true), null).Contains("first-profit"));
            Assert.True(Daily(Evaluator(), Snap("money", 150000000.0), run: Run(legacy: true)).Contains("cash-100m"));
        }

        [Test]
        public void TheAchievementsThatCountOnlyNewGames_AreThoseTheContentSays()
        {
            var onlyNew = Standard.Where(a => a.CountsOnlyNewGames).Select(a => a.Id).OrderBy(i => i, StringComparer.Ordinal).ToArray();
            Assert.Equal(new[] { "end-of-era", "impossible-decade" }, onlyNew);
        }

        // ---- the strict mode, the pass itself ----

        [Test]
        public void StrictAssisted_EarnsNothing()
        {
            Assert.Equal(0, Daily(Evaluator(), Snap("money", 150000000.0), run: Run(assisted: true, strict: true)).Count);
            Assert.True(Daily(Evaluator(), Snap("money", 150000000.0), run: Run(assisted: true, strict: false)).Contains("cash-100m"));
            Assert.True(Daily(Evaluator(), Snap("money", 150000000.0), run: Run(assisted: false, strict: true)).Contains("cash-100m"));
        }

        [Test]
        public void WithoutAProfileOrARun_NothingCanBeJudged()
        {
            MetricsSnapshot m = Snap("money", 150000000.0);
            Assert.Equal(0, Evaluator().Evaluate("daily", m, Book(), null, Run(), null).Count);
            Assert.Equal(0, Evaluator().Evaluate("daily", m, Book(), EmptyProfile(), null, null).Count);
            Assert.Equal(0, Evaluator().OnEvent("scenario.won", Book(), m, null, Run()).Count);
        }

        [Test]
        public void EachIdComesOnce_InTheOrderOfTheCatalog()
        {
            var a = Custom("a-first", Metric("money", ">=", 1));
            var b = Custom("b-second", Metric("money", ">=", 2));
            var again = Custom("a-first", Metric("money", ">=", 1));
            var evaluator = new AchievementEvaluator(new[] { a, b, again });
            Assert.Equal(new[] { "a-first", "b-second" }, Daily(evaluator, Snap("money", 10)).ToArray());
            Assert.Equal(3, evaluator.Catalog.Count, "the catalog keeps what it was given");
        }

        [Test]
        public void TheCatalog_SkipsWhatCannotBeChecked()
        {
            var noCheck = new AchievementDefinition { Id = "x" };
            var noId = Custom("", Metric("money", ">=", 1));
            var evaluator = new AchievementEvaluator(new AchievementDefinition[] { null, noCheck, noId, Custom("ok", Metric("money", ">=", 1)) });
            Assert.Equal(1, evaluator.Catalog.Count);
            Assert.Throws<ArgumentNullException>(() => new AchievementEvaluator(null));
        }

        [Test]
        public void AnAchievement_WithoutACadenceInTheDefinition_FollowsTheDefaultOfItsCheck()
        {
            var handMade = new AchievementDefinition { Id = "hand", Check = new CheckSpec { Kind = CheckSpec.KindMetric, Metric = "money", Op = ">=", Value = 1 } };
            var evaluator = new AchievementEvaluator(new[] { handMade });
            Assert.Equal(1, Daily(evaluator, Snap("money", 5)).Count);
        }

        // ---- the rivals ----

        [Test]
        public void AnAbsorbedCompany_DoesNotCountAsOutlived_AC123()
        {
            var state = new MetricsState();
            var book = new EventBook(state);

            Assert.False(book.RecordRivalBankrupt(true), "the player absorbed it: not a bankruptcy to outlive");
            Assert.Equal(0, state.RivalsBankrupt);
            Assert.Equal(0, book.Count("rival.bankrupt"));
            MetricsSnapshot none = MetricsCalculator.Snapshot(state, null, null);
            Assert.Equal(0.0, none.Get("rivals.bankruptCount"));
            Assert.False(Daily(Evaluator(), none, book).Contains("outlived-rival"));

            Assert.True(book.RecordRivalBankrupt(false));
            Assert.Equal(1, state.RivalsBankrupt);
            Assert.Equal(1, book.Count("rival.bankrupt"));
            MetricsSnapshot one = MetricsCalculator.Snapshot(state, null, null);
            Assert.True(Daily(Evaluator(), one, book).Contains("outlived-rival"));
            Assert.False(Daily(Evaluator(), one, book).Contains("last-standing"));

            for (int i = 0; i < 4; i++) book.RecordRivalBankrupt(false);
            book.RecordRivalBankrupt(true);
            Assert.True(Daily(Evaluator(), MetricsCalculator.Snapshot(state, null, null), book).Contains("last-standing"), "five real bankruptcies; the absorbed one is not the sixth");
        }

        [Test]
        public void AnOemContract_CountsTheMetricAndTheEvent()
        {
            var state = new MetricsState();
            var book = new EventBook(state);
            book.RecordOemSigned();
            Assert.Equal(1, state.OemSignedTotal);
            Assert.Equal(1, book.Count("oem.signed"));
            Assert.True(Daily(Evaluator(), MetricsCalculator.Snapshot(state, null, null), book).Contains("first-oem"));
        }

        // ---- the pop-ups ----

        [Test]
        public void ManyAchievementsAtOnce_GiveThreePopUps_AndAnAndNMore()
        {
            string[] five = { "a", "b", "c", "d", "e" };
            AchievementToasts plan = AchievementEvaluator.PlanToasts(five);
            Assert.Equal(new[] { "a", "b", "c" }, plan.Shown);
            Assert.Equal(2, plan.More);
            Assert.Equal(3, AchievementEvaluator.MaxToasts);

            AchievementToasts four = AchievementEvaluator.PlanToasts(new[] { "a", "b", "c", "d" });
            Assert.Equal(3, four.Shown.Length);
            Assert.Equal(1, four.More);
        }

        [Test]
        public void ThreeOrFewer_AreAllShown_AndNothingIsNothing()
        {
            AchievementToasts three = AchievementEvaluator.PlanToasts(new[] { "a", "b", "c" });
            Assert.Equal(new[] { "a", "b", "c" }, three.Shown);
            Assert.Equal(0, three.More);

            AchievementToasts one = AchievementEvaluator.PlanToasts(new[] { "a" });
            Assert.Equal(1, one.Shown.Length);
            Assert.Equal(0, one.More);

            AchievementToasts empty = AchievementEvaluator.PlanToasts(new string[0]);
            Assert.Equal(0, empty.Shown.Length);
            Assert.Equal(0, empty.More);
            Assert.Equal(0, AchievementEvaluator.PlanToasts(null).Shown.Length);
        }

        [Test]
        public void AFullDayOfAchievements_IsPlannedFromWhatTheEvaluatorReturned()
        {
            IList<string> earned = Daily(Evaluator(), Snap(
                "money", 2000000000.0, "money.solventStreakDays", 2000, "share.total.avg30", 0.6, "oem.active.count", 12, "factory.lines", 120,
                "foundry.clients.active", 4, "sales.total", 1000, "oem.signed.total", 2));

            Assert.True(earned.Count > AchievementEvaluator.MaxToasts, "enough for the 'and N more' line: " + string.Join(", ", earned.ToArray()));
            AchievementToasts plan = AchievementEvaluator.PlanToasts(earned);
            Assert.Equal(AchievementEvaluator.MaxToasts, plan.Shown.Length);
            Assert.Equal(earned.Count - AchievementEvaluator.MaxToasts, plan.More);
            Assert.Equal(earned.Take(3).ToArray(), plan.Shown);
        }

        // ---- the whole catalog ----

        [Test]
        public void EveryStandardAchievement_CanComeTrue_ThroughSomePass()
        {
            // each of the 37 has a cadence the evaluator serves and a check it can read
            foreach (AchievementDefinition def in Standard)
            {
                Assert.True(def.Check.Cadence == "daily" || def.Check.Cadence == "monthly" || def.Check.Cadence == "event", def.Id + " cadence: " + def.Check.Cadence);
            }
            Assert.Equal(37, Standard.Count);
            Assert.Equal(37, Evaluator().Catalog.Count);
        }
    }
}
