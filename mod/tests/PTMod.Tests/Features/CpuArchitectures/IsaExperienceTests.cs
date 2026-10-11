using System;
using System.Collections.Generic;
using PTMod.Features.CpuArchitectures;

namespace PTMod.Tests.Features.CpuArchitectures
{
    /// <summary>
    /// The experience of an ISA and the immature tools of a new architecture (FR-12, TSK-025): a family of an ISA the company already knows takes 0.6 of the time
    /// and starts with half of the support of its relative (AC-12.1: x86 at 80 gives x86-64 a start of 40), a family of an unknown ISA starts from 30; for twelve months
    /// after the research a project on it costs up to 25 % more (AC-12.2: ARM three months old, devCost 0.85 × 1.1875).
    /// </summary>
    [Covers(typeof(ImmatureTools))]
    public class IsaExperienceTests
    {
        private static SupportConfig Support()
        {
            return ArchTestKit.BuiltInBalance().support;
        }

        private static MigrationConfig Migration()
        {
            return ArchTestKit.BuiltInBalance().migration;
        }

        private static Dictionary<string, FamilyState> States(params object[] idSupportPairs)
        {
            var states = new Dictionary<string, FamilyState>(StringComparer.Ordinal);
            for (int i = 0; i < idSupportPairs.Length; i += 2)
            {
                string id = (string)idSupportPairs[i];
                states[id] = new FamilyState(id) { Researched = true, Progress = 1f, Support = Convert.ToSingle(idSupportPairs[i + 1]) };
            }
            return states;
        }

        // ---- AC-12.1: the time and the starting support ----

        [Test]
        public void AC12_1_X86At80_MakesTheResearchOfX86_64SixtyPercentOfTheTime_AndStartsItAt40()
        {
            ArchCatalog catalog = ArchTestKit.BuiltInCatalog();
            Dictionary<string, FamilyState> states = States("proprietary", 35f, "x86", 80f);
            ArchFamily x8664 = catalog.Get("x86_64");

            float isa = ArchResearchMath.IsaFactor(x8664, states, catalog, ArchTestKit.BuiltInBalance().research.isaExperienceFactor, true);
            Assert.Near(0.6, isa, 1e-6, "FR-12 item 1: ×0.6");

            float yearFactor = ArchResearchMath.YearFactor(x8664.Year, new DateTime(2003, 1, 1), ArchTestKit.BuiltInBalance().research);
            float without = ArchResearchMath.EffectiveDays(x8664.Research.BaseDays, yearFactor, 1f);
            float with = ArchResearchMath.EffectiveDays(x8664.Research.BaseDays, yearFactor, isa);
            Assert.Near(without * 0.6, with, 1e-3, "the time was ×0.6");
            Assert.Near(360.0, without, 1e-3, "the year of the family itself: ×1");

            Assert.Near(40.0, SupportMath.StartSupport(x8664, states, catalog, Support()), 1e-6, "max(30, 0.5 × 80)");
        }

        [Test]
        public void TheStartingSupport_IsAtLeast30_AndHalfTheBestRelative()
        {
            ArchCatalog catalog = ArchTestKit.BuiltInCatalog();
            SupportConfig config = Support();

            Assert.Near(30.0, SupportMath.StartSupport(catalog.Get("x86_64"), States("x86", 50f), catalog, config), 1e-6, "half of 50 is 25: the 30 stays");
            Assert.Near(45.0, SupportMath.StartSupport(catalog.Get("x86_64"), States("x86", 90f), catalog, config), 1e-6);
            Assert.Near(50.0, SupportMath.StartSupport(catalog.Get("x86_64"), States("x86", 100f), catalog, config), 1e-6, "the top of the scale");
            Assert.Near(30.0, SupportMath.StartSupport(catalog.Get("arm64"), States("x86", 95f), catalog, config), 1e-6, "another ISA does not help");
            Assert.Near(40.0, SupportMath.StartSupport(catalog.Get("arm64"), States("arm", 80f, "x86", 95f), catalog, config), 1e-6, "ARM does");
            Assert.Near(30.0, SupportMath.StartSupport(catalog.Get("x86"), States("x86", 100f), catalog, config), 1e-6, "the family itself does not count");
            Assert.Near(30.0, SupportMath.StartSupport(catalog.Get("riscv"), States("proprietary", 60f), catalog, config), 1e-6, "the proprietary ISA is its own group");
        }

        [Test]
        public void TheStartingSupport_OnlyCountsResearchedRelatives_AndIsSafeForBadInput()
        {
            ArchCatalog catalog = ArchTestKit.BuiltInCatalog();
            Dictionary<string, FamilyState> states = States("x86", 80f);
            states["x86"].Researched = false;   // a research that is still going has a level of its own (leftover) but no tools yet
            Assert.Near(30.0, SupportMath.StartSupport(catalog.Get("x86_64"), states, catalog, Support()), 1e-6);

            Assert.Near(30.0, SupportMath.StartSupport(null, states, catalog, Support()), 1e-6);
            Assert.Near(30.0, SupportMath.StartSupport(catalog.Get("x86_64"), null, catalog, Support()), 1e-6);
            Assert.Near(30.0, SupportMath.StartSupport(catalog.Get("x86_64"), states, null, Support()), 1e-6);
            Assert.Near(30.0, SupportMath.StartSupport(catalog.Get("x86_64"), States("x86", float.NaN), catalog, null), 1e-6, "no config: the defaults");
            Assert.Near(10.0, SupportMath.StartSupport(catalog.Get("x86_64"), States("x86", 20f), catalog, new SupportConfig { startOnResearch = 10f, inheritSameIsa = 0.5f }), 1e-6, "the numbers come from balance.json");
            Assert.Near(60.0, SupportMath.StartSupport(catalog.Get("x86_64"), States("x86", 80f), catalog, new SupportConfig { startOnResearch = 10f, inheritSameIsa = 0.75f }), 1e-6);
        }

        // ---- AC-12.2: the immature tools ----

        [Test]
        public void AC12_2_ArmResearchedThreeMonthsAgo_CostsDevCostTimes1_1875()
        {
            ArchCatalog catalog = ArchTestKit.BuiltInCatalog();
            ArchFamily arm = catalog.Get("arm");
            var researchedOn = new FamilyState("arm") { Researched = true, ResearchedYear = 1990, ResearchedMonth = 1 };
            var today = new DateTime(1990, 4, 12);

            int months = ImmatureTools.MonthsSince(researchedOn.ResearchedYear, researchedOn.ResearchedMonth, today);
            Assert.Equal(3, months);

            float surcharge = ImmatureTools.Surcharge(researchedOn, today, Migration());
            Assert.Near(0.1875, surcharge, 1e-6, "0.25 × (1 − 3/12)");
            Assert.Near(0.85 * 1.1875, DesignMath.CostFactor(arm.Design, surcharge), 1e-5, "AC-12.2: × 0.85 × 1.1875");
            Assert.Near(50e6 * 0.85 * 1.1875, DesignMath.Cost(50e6f, arm.Design, surcharge), 100.0);
            Assert.Equal(19, ImmatureTools.Percent(surcharge), "+19 % in the designer");
            Assert.Equal(9, ImmatureTools.MonthsLeft(months, Migration()), "9 months left");
        }

        [Test]
        public void TheSurcharge_FallsLinearlyToZeroInTwelveMonths_ThenStaysZero()
        {
            MigrationConfig config = Migration();
            Assert.Near(0.25, ImmatureTools.Surcharge(0, config), 1e-6, "the month of the research: the full 25 %");
            Assert.Near(0.125, ImmatureTools.Surcharge(6, config), 1e-6);
            Assert.Near(0.25 / 12.0, ImmatureTools.Surcharge(11, config), 1e-6);
            Assert.Equal(0f, ImmatureTools.Surcharge(12, config), "FR-12 item 3: only the first 12 months");
            Assert.Equal(0f, ImmatureTools.Surcharge(100, config));

            float previous = float.MaxValue;
            for (int months = 0; months <= 14; months++)
            {
                float surcharge = ImmatureTools.Surcharge(months, config);
                Assert.True(surcharge <= previous, "never rises, month " + months);
                Assert.InRange((double)surcharge, 0.0, 0.25);
                previous = surcharge;
            }
        }

        [Test]
        public void MonthsSince_CountsWholeMonths_AcrossTheNewYear_AndKnowsWhenThereIsNoDate()
        {
            Assert.Equal(0, ImmatureTools.MonthsSince(1990, 4, new DateTime(1990, 4, 30)));
            Assert.Equal(3, ImmatureTools.MonthsSince(1990, 1, new DateTime(1990, 4, 1)));
            Assert.Equal(5, ImmatureTools.MonthsSince(1989, 11, new DateTime(1990, 4, 1)), "across the new year");
            Assert.Equal(24, ImmatureTools.MonthsSince(1988, 4, new DateTime(1990, 4, 1)));
            Assert.Equal(0, ImmatureTools.MonthsSince(1990, 6, new DateTime(1990, 4, 1)), "a date in the future (a changed clock) is not negative");
            Assert.Equal(-1, ImmatureTools.MonthsSince(0, 0, new DateTime(1990, 4, 1)), "given at the start: no date");
            Assert.Equal(-1, ImmatureTools.MonthsSince(1990, 13, new DateTime(1990, 4, 1)));
            Assert.Equal(-1, ImmatureTools.MonthsSince(1990, 0, new DateTime(1990, 4, 1)));
        }

        [Test]
        public void AFamilyGivenAtTheStart_ANotResearchedOne_AndAMissingOne_HaveNoSurcharge()
        {
            var today = new DateTime(1990, 4, 12);
            MigrationConfig config = Migration();
            Assert.Equal(0f, ImmatureTools.Surcharge(new FamilyState("x86") { Researched = true }, today, config), "no date of research (the seed)");
            Assert.Equal(0f, ImmatureTools.Surcharge(new FamilyState("x86") { Researched = false, ResearchedYear = 1990, ResearchedMonth = 4 }, today, config));
            Assert.Equal(0f, ImmatureTools.Surcharge((FamilyState)null, today, config));
            Assert.Equal(0f, ImmatureTools.Surcharge(new FamilyState("x86") { Researched = true, ResearchedYear = 1990, ResearchedMonth = 4 }, default(DateTime), config), "no date today");
            Assert.Equal(-1, ImmatureTools.MonthsSince(0, 0, today));
            Assert.Equal(0, ImmatureTools.MonthsLeft(-1, config), "unknown: nothing to tell");
            Assert.Equal(0, ImmatureTools.MonthsLeft(12, config));
            Assert.Equal(12, ImmatureTools.MonthsLeft(0, config));
        }

        [Test]
        public void TheNumbersComeFromBalanceJson_AndBadOnesAreSafe()
        {
            var shorter = new MigrationConfig { immatureMonths = 6, immatureDevCost = 0.5f };
            Assert.Near(0.25, ImmatureTools.Surcharge(3, shorter), 1e-6);
            Assert.Equal(0f, ImmatureTools.Surcharge(6, shorter));
            Assert.Equal(3, ImmatureTools.MonthsLeft(3, shorter));

            Assert.Equal(0f, ImmatureTools.Surcharge(0, new MigrationConfig { immatureMonths = 0 }), "no period, no surcharge, no division by zero");
            Assert.Equal(0f, ImmatureTools.Surcharge(0, new MigrationConfig { immatureMonths = 12, immatureDevCost = float.NaN }));
            Assert.Near(0.25, ImmatureTools.Surcharge(0, null), 1e-6, "no config: the defaults");
            Assert.Equal(0, ImmatureTools.Percent(0f));
            Assert.Equal(0, ImmatureTools.Percent(-1f));
            Assert.Equal(0, ImmatureTools.Percent(float.NaN));
            Assert.Equal(25, ImmatureTools.Percent(0.25f));
        }

        [Test]
        public void TheProviderPrice_IsDevCostTimesOnePlusTheSurcharge_ForTheWholeLadder()
        {
            ArchCatalog catalog = ArchTestKit.BuiltInCatalog();
            foreach (ArchFamily family in catalog.Families)
            {
                for (int months = 0; months <= 12; months += 3)
                {
                    float surcharge = ImmatureTools.Surcharge(months, Migration());
                    float factor = DesignMath.CostFactor(family.Design, surcharge);
                    Assert.Near(family.Design.DevCost * (1f + surcharge), factor, 1e-6, family.Id + " month " + months);
                    Assert.InRange((double)factor, 0.5, 2.5, "inside the limits of the design.md (0.80-1.44 with the built-in numbers)");
                }
            }
        }
    }
}
