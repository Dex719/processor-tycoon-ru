using System;
using System.Collections.Generic;
using PTMod.Features.CpuArchitectures;

namespace PTMod.Tests.Features.CpuArchitectures
{
    /// <summary>
    /// The arithmetic of researching a family in the shared lab (FR-2, TSK-004): the year factor is the game's <c>TechnologyTimeUpdater</c>, the
    /// progress and the cost are the formulas of <c>ResearchSector</c>, the ETA is <c>Research.TimeLeft</c>; AC-2.1 (x86 in 1980 for an IDM company),
    /// a family researched at once, the cost of the "unlucky" company (M4 = 6), the endless ETA and the availability by requirements.
    /// </summary>
    [Covers(typeof(ArchResearchMath))]
    public class ArchResearchMathTests
    {
        /// <summary>The game's serialized numbers of <c>TechnologyTimeUpdater</c> (design.md: penaltyPerYear 350, bonusPerYear 15).</summary>
        private const float GamePenaltyPerYear = 350f;
        private const float GameBonusPerYear = 15f;

        /// <summary>The IDM bonus of <c>Modifiers.GetModifier(ResearchSpeed)</c>.</summary>
        private const float IdmSpeed = 1.05f;

        /// <summary><c>TechnologyTimeUpdater.UpdateTechnologyTimes</c> for one technology, written as the game writes it (floats, the same order).</summary>
        private static float GameTime(float baseTime, int technologyYear, DateTime today)
        {
            int year = today.Year;
            if (technologyYear > year)
            {
                float v4 = ((float)Math.Abs((today - new DateTime(technologyYear, 1, 1)).Days) / 365.0f) * (GamePenaltyPerYear / 1e+02f);
                return baseTime + baseTime * v4;
            }
            if (technologyYear < year)
            {
                float raw = (((float)Math.Abs((new DateTime(technologyYear, 1, 1) - today).Days) - 365.0f) / 365.0f) * (GameBonusPerYear / 1e+02f);
                float v5 = raw < 0f ? 0f : (raw > 0.75f ? 0.75f : raw);
                return baseTime * ((v5 + 1.0f) - (v5 * 2.0f));
            }
            return baseTime;
        }

        private static ResearchConfig Config()
        {
            return BalanceConfig.Defaults().research;
        }

        // ---- the year factor ----

        [Test]
        public void YearFactor_MatchesTheGameFormula_OnAGridOfYearsAndDates()
        {
            ResearchConfig config = Config();
            Assert.Near(GamePenaltyPerYear / 100f, config.penaltyPerYear, 1e-6, "balance.json keeps the game's penalty as a fraction");
            Assert.Near(GameBonusPerYear / 100f, config.bonusPerYear, 1e-6, "balance.json keeps the game's bonus as a fraction");
            Assert.Near(0.75, config.bonusCap, 1e-6, "the game's cap");

            int compared = 0;
            for (int familyYear = 1971; familyYear <= 2030; familyYear += 3)
            {
                for (var today = new DateTime(1971, 1, 1); today.Year <= 2030; today = today.AddDays(97))
                {
                    const float baseTime = 240f;
                    float expected = GameTime(baseTime, familyYear, today);
                    float actual = baseTime * ArchResearchMath.YearFactor(familyYear, today, config);
                    Assert.Near(expected, actual, Math.Max(1e-3, expected * 1e-5),
                        "family " + familyYear + " on " + today.ToString("yyyy-MM-dd") + ": the game gives " + expected + ", the mod " + actual);
                    compared++;
                }
            }
            Assert.True(compared > 2000, "the grid must be dense, compared " + compared);
        }

        [Test]
        public void YearFactor_FuturePastCurrentAndTheCap()
        {
            ResearchConfig config = Config();
            var jan1980 = new DateTime(1980, 1, 1);

            // Past: x86 (1978) on 1 January 1980 - 730 days since its year, (730 − 365)/365 × 0.15 = 0.15 bonus.
            Assert.Near(0.85, ArchResearchMath.YearFactor(1978, jan1980, config), 1e-6);

            // Future: a 1982 family on 1 January 1980 - 731 days (1980 is a leap year) × 3.5 / 365.
            Assert.Near(1.0 + 731.0 / 365.0 * 3.5, ArchResearchMath.YearFactor(1982, jan1980, config), 1e-4);

            // The current year is ×1, wherever in the year.
            Assert.Equal(1f, ArchResearchMath.YearFactor(1980, new DateTime(1980, 12, 31), config));

            // The bonus stops at 0.75: a 1971 family in 2000.
            Assert.Near(0.25, ArchResearchMath.YearFactor(1971, new DateTime(2000, 6, 1), config), 1e-6);

            // The first year after the family's: no bonus yet (fewer than 365 days beyond its first year).
            Assert.Equal(1f, ArchResearchMath.YearFactor(1979, new DateTime(1980, 1, 1), config));
        }

        [Test]
        public void YearFactor_PlainArguments_AreTheGameOrder()
        {
            // The overload with the days counted by the caller, as DateController gives them.
            Assert.Near(1.0 + 365.0 / 365.0 * 3.5, ArchResearchMath.YearFactor(1981, 365, 365, 1980, 3.5f, 0.15f, 0.75f), 1e-6);
            Assert.Near(0.7, ArchResearchMath.YearFactor(1970, 0, 365 * 3, 1980, 3.5f, 0.15f, 0.75f), 1e-6);
            Assert.Near(0.9, ArchResearchMath.YearFactor(1970, 0, 365 * 3, 1980, 3.5f, 0.15f, 0.1f), 1e-6, "a smaller cap from the content");
            Assert.Equal(1f, ArchResearchMath.YearFactor(1980, 0, 0, 1980, 3.5f, 0.15f, 0.75f));
            Assert.Equal(1f, ArchResearchMath.YearFactor(1982, 731, 0, 1980, float.NaN, 0.15f, 0.75f), "an unusable penalty gives ×1");
        }

        [Test]
        public void DaysBetween_IsTheAbsoluteNumberOfDays()
        {
            var a = new DateTime(1980, 1, 1);
            var b = new DateTime(1982, 1, 1);
            Assert.Equal(731, ArchResearchMath.DaysBetween(a, b));
            Assert.Equal(731, ArchResearchMath.DaysBetween(b, a));
            Assert.Equal(0, ArchResearchMath.DaysBetween(a, a));
        }

        // ---- AC-2.1: x86 in 1980 ----

        [Test]
        public void X86In1980_IdmCompanyFullFunding_Takes194Days()
        {
            ArchFamily x86 = ArchTestKit.BuiltInCatalog().Get("x86");
            Assert.Equal(1978, x86.Year);
            Assert.Near(240.0, x86.Research.BaseDays, 1e-6);

            float year = ArchResearchMath.YearFactor(x86.Year, new DateTime(1980, 1, 1), Config());
            float days = ArchResearchMath.EffectiveDays(x86.Research.BaseDays, year, 1f);
            Assert.Near(204.0, days, 1e-3, "240 × 0.85");

            float speed = (float)Math.Sqrt(1.0) * IdmSpeed;   // ResearchMath.ResearchSpeed(F = 1, IDM)
            float daily = ArchResearchMath.DailyProgress(speed, days);
            Assert.Equal(194, ArchResearchMath.EtaDays(0f, daily), "204 / 1.05 = 194.3 days");

            float cost = ArchResearchMath.DailyCost(x86.Research.MonthlyCost, 1f, 1f);
            Assert.Near(166666.67, cost, 0.01, "5M a month / 30 at 100 %");
            Assert.Near(32.3e6, cost * 194, 0.05e6, "≈ 32.3M for the whole research");
        }

        [Test]
        public void Progress_FollowsTheFunding()
        {
            // √F: at 400 % the lab is twice as fast, at 25 % half as fast; the cost scales with F itself.
            float days = 200f;
            Assert.Near(2.0 * ArchResearchMath.DailyProgress(1f, days), ArchResearchMath.DailyProgress((float)Math.Sqrt(4.0), days), 1e-7);
            Assert.Near(0.5 * ArchResearchMath.DailyProgress(1f, days), ArchResearchMath.DailyProgress((float)Math.Sqrt(0.25), days), 1e-7);
            Assert.Near(4.0 * ArchResearchMath.DailyCost(3e6f, 1f, 1f), ArchResearchMath.DailyCost(3e6f, 1f, 4f), 1e-2);
        }

        // ---- a family researched at once ----

        [Test]
        public void BaseDaysZero_IsResearchedAtOnce()
        {
            ArchFamily proprietary = ArchTestKit.BuiltInCatalog().Get(ArchCatalog.DefaultId);
            Assert.True(ArchResearchMath.IsInstant(proprietary.Research), "proprietary has baseDays 0");
            Assert.False(ArchResearchMath.IsInstant(new ResearchDef { BaseDays = 1f }));
            Assert.True(ArchResearchMath.IsInstant(null), "no research block: at once");

            Assert.Equal(0f, ArchResearchMath.EffectiveDays(0f, 0.85f, 1f));
            Assert.Equal(1f, ArchResearchMath.DailyProgress(IdmSpeed, 0f), "one tick finishes it");
            Assert.Equal(0, ArchResearchMath.EtaDays(1f, 1f), "finished: no days left");
            Assert.True(ArchResearchMath.CountsAsResearched(proprietary, new Dictionary<string, FamilyState>()),
                "a family researched at once counts as researched for every company");
        }

        // ---- the cost ----

        [Test]
        public void Cost_TheUnluckyCompanyPaysSixTimes()
        {
            // Modifiers.GetModifier(ResearchCost) is 6 for the "unlucky" company of the game (M4 = 6).
            float normal = ArchResearchMath.DailyCost(5e6f, 1f, 1f);
            float unlucky = ArchResearchMath.DailyCost(5e6f, 6f, 1f);
            Assert.Near(1e6, unlucky, 1e-1, "5M × 6 / 30");
            Assert.Near(6.0 * normal, unlucky, 1e-1);
            Assert.Near(30e6, ArchResearchMath.MonthlyCost(5e6f, 6f, 1f), 1.0, "a month of it");
            Assert.Near(0.5e6, ArchResearchMath.DailyCost(5e6f, 6f, 0.5f), 1e-1, "the funding scales the cost");
        }

        [Test]
        public void Cost_UnusableNumbers_CostNothing()
        {
            Assert.Equal(0f, ArchResearchMath.DailyCost(0f, 1f, 1f));
            Assert.Equal(0f, ArchResearchMath.DailyCost(-5e6f, 1f, 1f));
            Assert.Equal(0f, ArchResearchMath.DailyCost(float.NaN, 1f, 1f));
            Assert.Equal(0f, ArchResearchMath.DailyCost(5e6f, float.PositiveInfinity, 1f));
            Assert.Equal(0f, ArchResearchMath.DailyCost(5e6f, 1f, 0f));
        }

        // ---- the ETA ----

        [Test]
        public void Eta_SpeedZero_IsInfinity()
        {
            Assert.Equal(ArchResearchMath.InfiniteEta, ArchResearchMath.EtaDays(0.3f, 0f));
            Assert.Equal(ArchResearchMath.InfiniteEta, ArchResearchMath.EtaDays(0.3f, ArchResearchMath.DailyProgress(0f, 200f)), "funding 0: the lab stands");
            Assert.Equal(ArchResearchMath.InfiniteEta, ArchResearchMath.EtaDays(0.3f, float.NaN));
            Assert.Equal("∞", ArchResearchMath.FormatEta(ArchResearchMath.InfiniteEta));
            Assert.Equal(ArchResearchMath.InfinitySign, ArchResearchMath.FormatEta(-1, d => d + " дн."));
            Assert.Equal("12", ArchResearchMath.FormatEta(12));
            Assert.Equal("12 дн.", ArchResearchMath.FormatEta(12, d => d + " дн."));
        }

        [Test]
        public void Eta_RoundsLikeTheGame()
        {
            // Mathf.RoundToInt rounds halves to the even number: 2.5 -> 2, 3.5 -> 4.
            Assert.Equal(2, ArchResearchMath.EtaDays(0f, 0.4f));
            Assert.Equal(4, ArchResearchMath.EtaDays(0.3f, 0.2f));
            Assert.Equal(60, ArchResearchMath.EtaDays(0.4f, 0.01f), "the progress done counts: 60 % left at 1 % a day");
            Assert.Equal(0, ArchResearchMath.EtaDays(1.2f, 0.01f), "past the end");
        }

        // ---- availability ----

        [Test]
        public void Availability_FollowsTheRequirements()
        {
            ArchCatalog catalog = ArchTestKit.BuiltInCatalog();
            ArchFamily x86 = catalog.Get("x86");
            ArchFamily x8664 = catalog.Get("x86_64");
            var states = new Dictionary<string, FamilyState>();

            Assert.True(ArchResearchMath.IsAvailable(x86, states), "x86 has no requirements");
            Assert.False(ArchResearchMath.IsAvailable(x8664, states), "x86-64 needs x86");
            Assert.Equal(1, ArchResearchMath.MissingRequirements(x8664, states).Count);
            Assert.Equal("x86", ArchResearchMath.MissingRequirements(x8664, states)[0]);

            states["x86"] = new FamilyState("x86") { Progress = 0.9f };
            Assert.False(ArchResearchMath.IsAvailable(x8664, states), "progress is not researched");

            states["x86"].Researched = true;
            Assert.True(ArchResearchMath.IsAvailable(x8664, states));
            Assert.False(ArchResearchMath.IsAvailable(x86, states), "a researched family is not available any more");
            Assert.True(ArchResearchMath.IsResearched("x86", states));
            Assert.Equal(0, ArchResearchMath.MissingRequirements(x8664, states).Count);

            Assert.False(ArchResearchMath.IsAvailable(null, states));
            Assert.False(ArchResearchMath.IsResearched(null, states));
            Assert.False(ArchResearchMath.IsResearched("x86", null));
            Assert.True(ArchResearchMath.RequirementsMet(x86, null), "no requirements, no states needed");
            Assert.False(ArchResearchMath.CountsAsResearched(x8664, new Dictionary<string, FamilyState>()), "a long research does not count before it is done");
        }

        // ---- ISA experience (stage 2) ----

        [Test]
        public void IsaFactor_OnlyAtStage2_ForAnotherFamilyOfTheSameIsa()
        {
            ArchCatalog catalog = ArchTestKit.BuiltInCatalog();
            var states = new Dictionary<string, FamilyState> { { "x86", new FamilyState("x86") { Researched = true } } };

            Assert.Equal(1f, ArchResearchMath.IsaFactor(catalog.Get("x86_64"), states, catalog, 0.6f, false), "stage 1: ×1");
            Assert.Near(0.6, ArchResearchMath.IsaFactor(catalog.Get("x86_64"), states, catalog, 0.6f, true), 1e-6, "x86 researched: ×0.6");
            Assert.Equal(1f, ArchResearchMath.IsaFactor(catalog.Get("arm"), states, catalog, 0.6f, true), "another ISA: ×1");

            var onlyItself = new Dictionary<string, FamilyState> { { "x86", new FamilyState("x86") { Researched = true } } };
            Assert.Equal(1f, ArchResearchMath.IsaFactor(catalog.Get("x86"), onlyItself, catalog, 0.6f, true), "the family itself does not count");
            Assert.Near(240.0 * 0.85 * 0.6, ArchResearchMath.EffectiveDays(240f, 0.85f, 0.6f), 1e-3);
        }

        [Test]
        public void AC12_1_X86_64_WithX86At80_TakesSixtyPercentOfTheTime_AndTheDaysAreWhatTheServiceUses()
        {
            ArchCatalog catalog = ArchTestKit.BuiltInCatalog();
            BalanceConfig balance = ArchTestKit.BuiltInBalance();
            ArchFamily x8664 = catalog.Get("x86_64");
            var withX86 = new Dictionary<string, FamilyState> { { "x86", new FamilyState("x86") { Researched = true, Support = 80f } } };
            var without = new Dictionary<string, FamilyState> { { "proprietary", new FamilyState("proprietary") { Researched = true } } };
            var today = new DateTime(2003, 1, 1);

            // The service: baseDays × the year factor × the ISA factor (ArchResearchService.EffectiveDays).
            Func<IReadOnlyDictionary<string, FamilyState>, float> days = states =>
                ArchResearchMath.EffectiveDays(x8664.Research.BaseDays,
                    ArchResearchMath.YearFactor(x8664.Year, today, balance.research),
                    ArchResearchMath.IsaFactor(x8664, states, catalog, balance.research.isaExperienceFactor, true));

            float experienced = days(withX86);
            float newcomer = days(without);
            Assert.Near(360.0, newcomer, 1e-3, "no relative: the full 360 days of 2003");
            Assert.Near(216.0, experienced, 1e-3, "AC-12.1: 360 × 0.6");
            Assert.Near(0.6, experienced / newcomer, 1e-6);
            Assert.Near(0.6, balance.research.isaExperienceFactor, 1e-6, "the factor is in balance.json");

            // And the progress a day is faster by the same factor.
            Assert.Near(ArchResearchMath.DailyProgress(1.05f, newcomer) / 0.6, ArchResearchMath.DailyProgress(1.05f, experienced), 1e-6);
        }

        // ---- unusable inputs ----

        [Test]
        public void UnusableInputs_NeverGiveNaN()
        {
            Assert.Equal(0f, ArchResearchMath.DailyProgress(float.NaN, 100f));
            Assert.Equal(0f, ArchResearchMath.DailyProgress(-1f, 100f));
            Assert.Equal(0f, ArchResearchMath.DailyProgress(float.PositiveInfinity, 100f));
            Assert.Equal(0f, ArchResearchMath.EffectiveDays(float.NaN, 1f, 1f));
            Assert.Near(100.0, ArchResearchMath.EffectiveDays(100f, float.NaN, 1f), 1e-6, "an unusable factor is ×1");
            Assert.Near(0.85, ArchResearchMath.YearFactor(1978, new DateTime(1980, 1, 1), null), 1e-6, "null config: the defaults");
        }
    }
}
