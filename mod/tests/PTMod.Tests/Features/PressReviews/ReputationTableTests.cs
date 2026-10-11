using System;
using System.Collections.Generic;
using PTMod.Core;
using PTMod.Features.PressReviews;

namespace PTMod.Tests.PressReviews
{
    /// <summary>
    /// The table of the reputation (FR-7, AC-7.1, TSK-013): the levels, the change over 12 months and the latest reasons from the history of the core (only
    /// <c>Add</c> and <c>Set</c>, never the drift), the reasons of the features in words (<c>press.review</c>, <c>press.award</c>, <c>dfr.*</c>, <c>rec.*</c>,
    /// <c>acq.*</c>, <c>event:&lt;id&gt;</c>, the rest "other"), the effect on popularity, and the line "68 Respected, +8 over the year, ×1.11".
    /// </summary>
    [Covers(typeof(ReputationTable), typeof(ReputationRow), typeof(ReputationCompany), typeof(ReputationLevels), typeof(ReputationReasons), typeof(ReasonKind))]
    public class ReputationTableTests
    {
        private static readonly DateTime Today = new DateTime(1996, 4, 10);

        private static ReputationChange Change(DateTime date, float delta, float value, string reason)
        {
            return new ReputationChange(date, delta, value, reason);
        }

        /// <summary>AC-7.1: the reputation was 60 a year ago and is 68 now.</summary>
        private static List<ReputationChange> PlayerHistory()
        {
            return new List<ReputationChange>
            {
                Change(Today.AddMonths(-13), 2f, 60f, "press.review:101"),
                Change(Today.AddMonths(-9), 5f, 65f, "press.review:102"),
                Change(Today.AddMonths(-3), 3f, 68f, "press.award:1995"),
            };
        }

        private static Func<string, IReadOnlyList<ReputationChange>> Histories(string key, List<ReputationChange> history)
        {
            return k => k == key ? history : new List<ReputationChange>();
        }

        private static string CpuName(int id)
        {
            return id == 102 ? "K6-300" : id == 101 ? "P5-90" : null;
        }

        private static List<ReputationRow> Table(PressText text, Func<string, float> reputationOf, Func<string, IReadOnlyList<ReputationChange>> historyOf,
            Func<string, float> effectOf, params ReputationCompany[] companies)
        {
            return ReputationTable.Build(companies, reputationOf, historyOf, effectOf, Today, ReviewConfig.CreateDefault(), CpuName, text);
        }

        // ---- AC-7.1 ----

        [Test]
        public void ThePlayersRow_Is68Respected_Plus8OverTheYear_Times1_11_Ac71()
        {
            PressText text = PressUiFixture.Text("ru");
            List<ReputationRow> rows = Table(text, key => 68f, Histories("player", PlayerHistory()), null,
                new ReputationCompany { Key = "player", Name = "Me", IsPlayer = true });

            Assert.Equal(1, rows.Count);
            ReputationRow row = rows[0];
            Assert.Equal("68 Уважаемый, +8 за год, ×1.11", row.Line);
            Assert.Equal("press.rep.respected", row.LevelKey);
            Assert.Equal("Уважаемый", row.Level);
            Assert.Near(8.0, row.Change, 1e-4);
            Assert.Near(1.108, row.Effect, 1e-4);
            Assert.True(row.IsPlayer);
            Assert.Equal("Me", row.CompanyName);
        }

        [Test]
        public void TheRow_ReadsInEnglishToo()
        {
            List<ReputationRow> rows = Table(PressUiFixture.Text("en"), key => 68f, Histories("player", PlayerHistory()), null,
                new ReputationCompany { Key = "player", Name = "Me", IsPlayer = true });
            Assert.Equal("68 Respected, +8 over the year, ×1.11", rows[0].Line);
        }

        [Test]
        public void TheLatestThreeReasons_AreInWords_TheNewestFirst()
        {
            var history = PlayerHistory();
            history.Add(Change(Today.AddDays(-5), -8f, 60f, "dfr.recall.reputation"));
            List<ReputationRow> rows = Table(PressUiFixture.Text("ru"), key => 60f, Histories("player", history), null,
                new ReputationCompany { Key = "player", Name = "Me", IsPlayer = true });

            List<string> reasons = rows[0].Reasons;
            Assert.Equal(3, reasons.Count);
            Assert.Equal("отзыв процессора -8", reasons[0]);
            Assert.Equal("итоги года +3", reasons[1]);
            Assert.Equal("обзор K6-300 +5", reasons[2]);
        }

        [Test]
        public void ACompanyWithoutHistory_HasNoChangeAndNoReasons_AndTheLineSaysSo()
        {
            List<ReputationRow> rows = Table(PressUiFixture.Text("ru"), key => 50f, Histories("none", new List<ReputationChange>()), null,
                new ReputationCompany { Key = "ai:3", Name = "Cyrix" });

            Assert.Equal(0f, rows[0].Change);
            Assert.Equal(0, rows[0].Reasons.Count);
            Assert.Equal("50 Обычный, без изменений за год, ×1.00", rows[0].Line);
            Assert.False(rows[0].IsPlayer);
        }

        [Test]
        public void ThePlayerComesFirst_InTheOrderTheCallerGives()
        {
            List<ReputationRow> rows = Table(PressUiFixture.Text("ru"), key => key == "player" ? 72f : 40f, Histories("x", null), null,
                new ReputationCompany { Key = "player", Name = "Me", IsPlayer = true },
                new ReputationCompany { Key = "ai:0", Name = "Intel" },
                new ReputationCompany { Key = "ai:1", Name = "AMD" });

            Assert.Equal(3, rows.Count);
            Assert.Equal("player", rows[0].CompanyKey);
            Assert.Equal("ai:0", rows[1].CompanyKey);
            Assert.Equal("ai:1", rows[2].CompanyKey);
            Assert.Equal("press.rep.respected", rows[0].LevelKey);
            Assert.Equal("press.rep.ordinary", rows[1].LevelKey);
        }

        [Test]
        public void TheEffect_IsWhatTheFeatureReallyApplies_AnAiCompanyCanHaveNone()
        {
            List<ReputationRow> rows = Table(PressUiFixture.Text("ru"), key => 90f, Histories("x", null), key => key == "player" ? 1.24f : 1f,
                new ReputationCompany { Key = "player", Name = "Me", IsPlayer = true },
                new ReputationCompany { Key = "ai:0", Name = "Intel" });

            Assert.True(rows[0].Line.EndsWith("×1.24", StringComparison.Ordinal), rows[0].Line);
            Assert.True(rows[1].Line.EndsWith("×1.00", StringComparison.Ordinal), rows[1].Line);
        }

        [Test]
        public void ANameWithATag_CannotOpenOne_AndNullsAreSkipped()
        {
            List<ReputationRow> rows = ReputationTable.Build(
                new[] { null, new ReputationCompany { Key = string.Empty, Name = "No key" }, new ReputationCompany { Key = "ai:4", Name = "<b>Evil</b>" } },
                key => 50f, null, null, Today, null, CpuName, PressUiFixture.Text("ru"));

            Assert.Equal(1, rows.Count);
            Assert.Equal("bEvil/b", rows[0].CompanyName);
            Assert.Equal(0, ReputationTable.Build(null, key => 50f, null, null, Today, null, CpuName, PressUiFixture.Text("ru")).Count);
        }

        // ---- the change over 12 months ----

        [Test]
        public void TheChange_IsTheValueNowMinusTheValueAYearAgo_FromTheLastChangeBeforeThatDay()
        {
            Assert.Near(8.0, ReputationTable.ChangeOverYear(PlayerHistory(), 68f, Today), 1e-4);
            Assert.Near(-3.0, ReputationTable.ChangeOverYear(PlayerHistory(), 57f, Today), 1e-4, "the reputation fell back");
        }

        [Test]
        public void TheChange_WithAHistoryThatStartsInsideTheYear_CountsFromTheValueBeforeItsFirstChange()
        {
            var history = new List<ReputationChange>
            {
                Change(Today.AddMonths(-4), 6f, 56f, "press.review:1"),
                Change(Today.AddMonths(-1), 4f, 60f, "press.award:1995"),
            };
            Assert.Near(10.0, ReputationTable.ChangeOverYear(history, 60f, Today), 1e-4, "50 before the first change");
        }

        [Test]
        public void TheChange_OfACompanyWithoutHistory_IsZero_AndTheDayOfTheCutoffCounts()
        {
            Assert.Equal(0f, ReputationTable.ChangeOverYear(null, 70f, Today));
            Assert.Equal(0f, ReputationTable.ChangeOverYear(new List<ReputationChange>(), 70f, Today));

            var onTheDay = new List<ReputationChange> { Change(Today.AddMonths(-12), 5f, 55f, "press.review:1") };
            Assert.Near(10.0, ReputationTable.ChangeOverYear(onTheDay, 65f, Today), 1e-4, "a change 12 months ago to the day is the value a year ago");
        }

        [Test]
        public void TheDriftOfTheCore_IsInNoHistory_SoItIsInTheChangeOnlyThroughTheValueNow()
        {
            // The core's monthly pull towards 50 writes no entry; the value now already has it, the old value is the last entry's.
            var history = new List<ReputationChange> { Change(Today.AddMonths(-14), 10f, 70f, "press.review:1") };
            Assert.Near(-5.0, ReputationTable.ChangeOverYear(history, 65f, Today), 1e-4);
        }

        // ---- the step of a review ----

        [Test]
        public void TheStepOfAReview_IsFoundByItsReason_TheNewestEntryWins()
        {
            var history = new List<ReputationChange>
            {
                Change(Today.AddDays(-20), 2f, 57.4f, "press.review:7"),
                Change(Today.AddDays(-10), 3f, 60.4f, "press.award:1995"),
                Change(Today.AddDays(-5), 1f, 58.4f, "press.review:7"),
            };
            ReputationStep step = ReputationTable.StepOf(history, EffectMath.ReviewReason(7));
            Assert.NotNull(step);
            Assert.Equal(57, step.Before);
            Assert.Equal(58, step.After);

            Assert.Null(ReputationTable.StepOf(history, EffectMath.ReviewReason(8)));
            Assert.Null(ReputationTable.StepOf(null, EffectMath.ReviewReason(7)));
            Assert.Null(ReputationTable.StepOf(history, null));
        }

        [Test]
        public void TheStep_IsHeldToTheScale()
        {
            var history = new List<ReputationChange> { Change(Today, 12f, 100f, "press.review:7") };
            Assert.Equal(88, ReputationTable.StepOf(history, EffectMath.ReviewReason(7)).Before);
            history = new List<ReputationChange> { Change(Today, -9f, 2f, "press.review:7") };
            Assert.Equal(11, ReputationTable.StepOf(history, EffectMath.ReviewReason(7)).Before);
        }

        // ---- the levels ----

        [Test]
        public void TheLevels_FollowTheRequirement_80Cult_65To79Respected_36To64Ordinary_21To35Dubious_20AndBelowToxic()
        {
            ReviewConfig cfg = PressContentFixture.Config();
            Assert.Equal("press.rep.cult", ReputationLevels.KeyFor(100f, cfg));
            Assert.Equal("press.rep.cult", ReputationLevels.KeyFor(80f, cfg));
            Assert.Equal("press.rep.respected", ReputationLevels.KeyFor(79f, cfg));
            Assert.Equal("press.rep.respected", ReputationLevels.KeyFor(65f, cfg));
            Assert.Equal("press.rep.ordinary", ReputationLevels.KeyFor(64f, cfg));
            Assert.Equal("press.rep.ordinary", ReputationLevels.KeyFor(50f, cfg));
            Assert.Equal("press.rep.ordinary", ReputationLevels.KeyFor(36f, cfg));
            Assert.Equal("press.rep.dubious", ReputationLevels.KeyFor(35f, cfg));
            Assert.Equal("press.rep.dubious", ReputationLevels.KeyFor(21f, cfg));
            Assert.Equal("press.rep.toxic", ReputationLevels.KeyFor(20f, cfg));
            Assert.Equal("press.rep.toxic", ReputationLevels.KeyFor(0f, cfg));
        }

        [Test]
        public void TheLevels_HaveTheBuiltInThresholds_WhenThereIsNoConfig_AndAnUnknownValueIsOrdinary()
        {
            Assert.Equal("press.rep.cult", ReputationLevels.KeyFor(85f, (ReviewConfig)null));
            Assert.Equal("press.rep.ordinary", ReputationLevels.KeyFor(float.NaN, (ReviewConfig)null));
            Assert.Equal(string.Empty, ReputationLevels.KeyFor(50f, (IList<ReputationLevel>)null));
            Assert.Equal(string.Empty, ReputationLevels.KeyFor(50f, new List<ReputationLevel>()));
            Assert.Equal("low", ReputationLevels.KeyFor(-5f, new List<ReputationLevel> { new ReputationLevel { Min = 10, Key = "low" }, new ReputationLevel { Min = 60, Key = "high" } }),
                "a value below every level uses the lowest");
        }

        [Test]
        public void TheShortLine_IsTheValueAndTheLevel()
        {
            Assert.Equal("68 — Уважаемый", ReputationTable.Short(68f, null, PressUiFixture.Text("ru")));
            Assert.Equal("30 — Dubious", ReputationTable.Short(30.4f, null, PressUiFixture.Text("en")));
        }

        // ---- the reasons ----

        [Test]
        public void TheReasons_AreSortedByThePrefixOfTheFeatureThatWroteThem()
        {
            string argument;
            Assert.Equal(ReasonKind.Review, ReputationReasons.Classify("press.review:123", out argument));
            Assert.Equal("123", argument);
            Assert.Equal(ReasonKind.Award, ReputationReasons.Classify("press.award:cpu", out argument));
            Assert.Equal("cpu", argument);
            Assert.Equal(ReasonKind.Award, ReputationReasons.Classify("press.award.company:1995", out argument));
            Assert.Equal(ReasonKind.Recall, ReputationReasons.Classify("dfr.recall.reputation", out argument));
            Assert.Equal(ReasonKind.Recall, ReputationReasons.Classify("dfr.flaw.public:17", out argument));
            Assert.Equal(ReasonKind.Recovery, ReputationReasons.Classify("rec.bankruptcy.penalty", out argument));
            Assert.Equal(ReasonKind.Acquisition, ReputationReasons.Classify("acq.absorbed", out argument));
            Assert.Equal(ReasonKind.Event, ReputationReasons.Classify("event:ms_crash_1987", out argument));
            Assert.Equal("ms_crash_1987", argument);
            Assert.Equal(ReasonKind.Other, ReputationReasons.Classify("something else", out argument));
            Assert.Equal(ReasonKind.Other, ReputationReasons.Classify("press.drift", out argument), "the drift is not a reason of this feature");
            Assert.Equal(ReasonKind.Other, ReputationReasons.Classify(string.Empty, out argument));
            Assert.Equal(ReasonKind.Other, ReputationReasons.Classify(null, out argument));
            Assert.Equal(ReasonKind.Other, ReputationReasons.Classify("events:oops", out argument), "only event: is an event");
        }

        [Test]
        public void EveryKindOfReason_ReadsInWords_InBothLanguages()
        {
            var reasons = new Dictionary<string, string>
            {
                { "press.review:102", "обзор K6-300 +2" },
                { "press.review:999", "обзор процессора +2" },
                { "press.award:1995", "итоги года +2" },
                { "dfr.recall", "отзыв процессора +2" },
                { "rec.penalty", "после банкротства +2" },
                { "acq.deal", "поглощение +2" },
                { "event:crash", "событие +2" },
                { "who knows", "прочее +2" },
            };
            PressText ru = PressUiFixture.Text("ru");
            foreach (KeyValuePair<string, string> pair in reasons)
                Assert.Equal(pair.Value, ReputationReasons.Describe(Change(Today, 2f, 52f, pair.Key), CpuName, ru), pair.Key);

            PressText en = PressUiFixture.Text("en");
            foreach (string reason in reasons.Keys)
                Assert.True(ReputationReasons.Describe(Change(Today, -2.4f, 48f, reason), CpuName, en).EndsWith("-2", StringComparison.Ordinal), reason);
        }

        [Test]
        public void EveryKindOfReason_HasItsOwnKey()
        {
            var keys = new HashSet<string>();
            foreach (ReasonKind kind in Enum.GetValues(typeof(ReasonKind))) Assert.True(keys.Add(ReputationReasons.KeyOf(kind)), kind.ToString());
            Assert.Equal(7, keys.Count);
        }

        [Test]
        public void TheDescription_NeedsTheTexts()
        {
            Assert.Throws<ArgumentNullException>(() => ReputationReasons.Describe(Change(Today, 1f, 51f, "x"), CpuName, null));
            Assert.Throws<ArgumentNullException>(() => ReputationTable.Line(null, PressUiFixture.Text("ru")));
            Assert.Throws<ArgumentNullException>(() => ReputationTable.Build(null, null, null, null, Today, null, null, PressUiFixture.Text("ru")));
        }
    }
}
