using System;
using System.Collections.Generic;
using PTMod.Features.CpuArchitectures;

namespace PTMod.Tests.Features.CpuArchitectures
{
    /// <summary>
    /// One month of the software support of one company and the rules of the tiers (FR-10, TSK-022): the upkeep goes to the article of the whole company in
    /// one sum and the levels move by <see cref="SupportMath"/> (AC-10.1, AC-10.2 through the cycle), a family the company has not researched is left alone,
    /// an AI company that cannot pay lets a tier decay as "off" (the player always pays, FR-10 item 3), the player in default has every tier turned off and
    /// cannot raise one (FR-10 item 6, M14), a change of tier counts from the next month (FR-10 item 5) and the result does not depend on the order the
    /// families were added in. The spending and the notification of the service itself are checked in <c>SupportWiringTests</c>.
    /// </summary>
    [Covers(typeof(SupportCycle), typeof(SupportMonthResult), typeof(SupportSetResult))]
    public class SupportCycleTests
    {
        private static ArchCatalog _catalog;

        private static ArchCatalog Catalog
        {
            get { return _catalog ?? (_catalog = ArchTestKit.BuiltInCatalog()); }
        }

        private static SupportConfig Config()
        {
            return ArchTestKit.BuiltInBalance().support;
        }

        private static FamilyState Add(CompanyArchState state, string id, float support, SupportTier tier, bool researched = true)
        {
            var family = new FamilyState(id) { Researched = researched, Progress = researched ? 1f : 0.3f, Support = support, Tier = tier };
            state.Families[id] = family;
            return family;
        }

        /// <summary>A company with the proprietary ISA (31, off), x86 (30, normal), ARM (60, off) and RISC not researched (it has a tier and a level left over).</summary>
        private static CompanyArchState Company(bool isPlayer = true, bool reverse = false)
        {
            var state = new CompanyArchState(1, isPlayer);
            var steps = new List<Action>
            {
                () => Add(state, "proprietary", 31f, SupportTier.Off),
                () => Add(state, "x86", 30f, SupportTier.Normal),
                () => Add(state, "arm", 60f, SupportTier.Off),
                () => Add(state, "risc", 10f, SupportTier.High, false),
            };
            if (reverse) steps.Reverse();
            foreach (Action step in steps) step();
            return state;
        }

        private static float Upkeep(string id, SupportTier tier, int year, float inflation)
        {
            return SupportMath.MonthlyUpkeep(Catalog.Get(id).Support, tier, year, inflation, Config());
        }

        // ---- the month ----

        [Test]
        public void AMonth_ChargesTheUpkeepOfTheTiers_AndMovesTheLevels()
        {
            CompanyArchState state = Company();
            SupportMonthResult month = SupportCycle.RunMonth(state, Catalog, Config(), 1990, 1.4f, float.PositiveInfinity);

            Assert.Near(Upkeep("x86", SupportTier.Normal, 1990, 1.4f), month.Upkeep, 1.0, "only x86 is funded: the others are off");
            Assert.Equal(3, month.Families, "three researched families took part");
            Assert.Equal(0, month.Unfunded);
            Assert.True(month.Changed);

            Assert.Near(34.0, state.Families["x86"].Support, 1e-4, "30 + 40 x 0.1");
            Assert.Near(31.0 * 0.95, state.Families["proprietary"].Support, 1e-4, "off decays by the 5 % of the family");
            Assert.Near(60.0 * 0.96, state.Families["arm"].Support, 1e-4, "ARM decays by 4 %");
        }

        [Test]
        public void TheUpkeepIsOneSumOfAllTheFamilies()
        {
            CompanyArchState state = Company();
            state.Families["arm"].Tier = SupportTier.High;
            state.Families["proprietary"].Tier = SupportTier.Low;

            SupportMonthResult month = SupportCycle.RunMonth(state, Catalog, Config(), 1995, 1.2f, float.PositiveInfinity);

            double expected = Upkeep("proprietary", SupportTier.Low, 1995, 1.2f) + Upkeep("x86", SupportTier.Normal, 1995, 1.2f) + Upkeep("arm", SupportTier.High, 1995, 1.2f);
            Assert.Near(expected, month.Upkeep, 1.0);
            Assert.True(month.Upkeep > 0f);
        }

        [Test]
        public void TwelveMonthsOfNormalX86_AC101_AndTwelveOfOffArm_AC102()
        {
            CompanyArchState state = Company();
            state.Families["x86"].Support = 30f;
            state.Families["arm"].Support = 60f;

            double paid = 0;
            for (int month = 0; month < 12; month++)
            {
                paid += SupportCycle.RunMonth(state, Catalog, Config(), 1993, 1.5f, float.PositiveInfinity).Upkeep;
            }

            Assert.Near(58.7, state.Families["x86"].Support, 0.05, "AC-10.1: about 59");
            Assert.Near(36.76, state.Families["arm"].Support, 0.01, "AC-10.2: about 37");
            Assert.Near(12.0 * 1500000.0 * 1.5, paid, 5.0, "12 x 1.5M x inflation; ARM and the proprietary ISA are off and cost nothing");
        }

        [Test]
        public void AFamilyTheCompanyHasNotResearched_IsLeftAlone()
        {
            CompanyArchState state = Company();
            SupportCycle.RunMonth(state, Catalog, Config(), 1990, 1f, float.PositiveInfinity);

            FamilyState risc = state.Families["risc"];
            Assert.Equal(10f, risc.Support, "not researched: no update");
            Assert.Equal(SupportTier.High, risc.Tier, "and its tier is kept as it is");
        }

        [Test]
        public void AFamilyTheCatalogDoesNotKnow_IsKeptAsItIs()
        {
            CompanyArchState state = Company();
            Add(state, "mystery", 44f, SupportTier.High);

            SupportMonthResult month = SupportCycle.RunMonth(state, Catalog, Config(), 1990, 1f, float.PositiveInfinity);
            Assert.Equal(44f, state.Families["mystery"].Support);
            Assert.Equal(SupportTier.High, state.Families["mystery"].Tier);
            Assert.Equal(3, month.Families);
        }

        [Test]
        public void ACompanyWithNothingResearched_PaysNothing()
        {
            var state = new CompanyArchState(2, true);
            SupportMonthResult month = SupportCycle.RunMonth(state, Catalog, Config(), 1990, 1f, float.PositiveInfinity);
            Assert.Equal(0f, month.Upkeep);
            Assert.Equal(0, month.Families);
            Assert.False(month.Changed);

            Assert.Equal(0f, SupportCycle.RunMonth(null, Catalog, Config(), 1990, 1f, 1f).Upkeep, "no state");
            Assert.Equal(0f, SupportCycle.RunMonth(Company(), null, Config(), 1990, 1f, 1f).Upkeep, "no catalog");
        }

        [Test]
        public void ALevelAtItsTarget_DoesNotReportAChange()
        {
            CompanyArchState state = new CompanyArchState(1, true);
            Add(state, "x86", 70f, SupportTier.Normal);
            SupportMonthResult month = SupportCycle.RunMonth(state, Catalog, Config(), 1990, 1f, float.PositiveInfinity);
            Assert.False(month.Changed, "70 stays 70: the rating cache is not stale");
            Assert.True(month.Upkeep > 0f, "but the upkeep is still paid");
        }

        // ---- who pays ----

        [Test]
        public void ThePlayerPaysWhateverHisMoneyIs()
        {
            CompanyArchState state = Company();
            SupportMonthResult month = SupportCycle.RunMonth(state, Catalog, Config(), 1990, 1f, float.PositiveInfinity);
            Assert.Equal(0, month.Unfunded);
            Assert.Near(1500000.0, month.Upkeep, 1.0, "FR-10 item 3: the charge happens even if it drives the balance below zero");
        }

        [Test]
        public void AnAiCompanyThatCannotPay_LetsTheTierDecayAsOff()
        {
            CompanyArchState state = Company(false);
            float x86Upkeep = Upkeep("x86", SupportTier.Normal, 1990, 1f);

            SupportMonthResult poor = SupportCycle.RunMonth(state, Catalog, Config(), 1990, 1f, x86Upkeep - 1f);
            Assert.Equal(0f, poor.Upkeep, "nothing was paid");
            Assert.Equal(1, poor.Unfunded);
            Assert.Near(30.0 * 0.94, state.Families["x86"].Support, 1e-4, "it decayed as off with the 6 % of x86, not rose to 34");
            Assert.Equal(SupportTier.Normal, state.Families["x86"].Tier, "the tier stays: it works again when the money is back");

            SupportMonthResult rich = SupportCycle.RunMonth(state, Catalog, Config(), 1990, 1f, x86Upkeep);
            Assert.Near(x86Upkeep, rich.Upkeep, 1.0, "exactly enough pays");
            Assert.Equal(0, rich.Unfunded);
        }

        [Test]
        public void ABudgetIsSpentInTheOrderOfTheCatalog_ThenTheRestDecays()
        {
            CompanyArchState state = Company(false);
            state.Families["proprietary"].Tier = SupportTier.Normal;
            state.Families["arm"].Tier = SupportTier.Normal;

            float own = Upkeep("proprietary", SupportTier.Normal, 1990, 1f);
            float arm = Upkeep("arm", SupportTier.Normal, 1990, 1f);
            float x86 = Upkeep("x86", SupportTier.Normal, 1990, 1f);
            Assert.True(own < x86 && arm < x86, "the test needs x86 to be the most expensive");

            // The catalog lists proprietary, x86, risc, arm: money for proprietary and ARM but not for x86 in between.
            float budget = own + arm + 1f;
            SupportMonthResult month = SupportCycle.RunMonth(state, Catalog, Config(), 1990, 1f, budget);

            Assert.Near(own + arm, month.Upkeep, 1.0, "proprietary and ARM are paid, x86 is skipped");
            Assert.Equal(1, month.Unfunded);
            Assert.True(state.Families["x86"].Support < 30f, "x86 decayed");
            Assert.Near(61.0, state.Families["arm"].Support, 1e-3, "ARM is paid on normal (70) and rises from 60 to 61");
        }

        [Test]
        public void AnAiBudgetOfZeroOrLess_PaysNothing()
        {
            foreach (float budget in new[] { 0f, -5f, float.NaN })
            {
                CompanyArchState state = Company(false);
                SupportMonthResult month = SupportCycle.RunMonth(state, Catalog, Config(), 1990, 1f, budget);
                Assert.Equal(0f, month.Upkeep, "budget " + budget);
                Assert.Equal(1, month.Unfunded, "budget " + budget);
            }
        }

        // ---- default ----

        [Test]
        public void ThePlayerInDefault_HasEveryTierTurnedOff_M14()
        {
            CompanyArchState state = Company();
            state.Families["arm"].Tier = SupportTier.High;
            state.Families["proprietary"].Tier = SupportTier.Low;

            int changed = SupportCycle.ForceOff(state);
            Assert.Equal(4, changed, "x86, ARM, the proprietary ISA and the unresearched RISC all had a tier");
            foreach (FamilyState family in state.Families.Values) Assert.Equal(SupportTier.Off, family.Tier, family.Id);
            Assert.Equal(0, SupportCycle.ForceOff(state), "a second time there is nothing to turn off (no second notification)");

            // The month after that costs nothing and the levels only decay.
            SupportMonthResult month = SupportCycle.RunMonth(state, Catalog, Config(), 1990, 2f, float.PositiveInfinity);
            Assert.Equal(0f, month.Upkeep);
            Assert.True(state.Families["x86"].Support < 30f);
        }

        [Test]
        public void ForceOff_KeepsTheLevels()
        {
            CompanyArchState state = Company();
            SupportCycle.ForceOff(state);
            Assert.Equal(30f, state.Families["x86"].Support);
            Assert.Equal(60f, state.Families["arm"].Support);
            Assert.Equal(0, SupportCycle.ForceOff(null));
        }

        [Test]
        public void InDefault_OnlyOffCanBeSet()
        {
            CompanyArchState state = Company();
            Assert.Equal(SupportSetResult.InDefault, SupportCycle.SetTier(state, Catalog, "x86", SupportTier.Low, true), "raising is refused");
            Assert.Equal(SupportTier.Normal, state.Families["x86"].Tier, "and nothing changed");
            Assert.Equal(SupportSetResult.Set, SupportCycle.SetTier(state, Catalog, "x86", SupportTier.Off, true), "off is always allowed");
            Assert.Equal(SupportTier.Off, state.Families["x86"].Tier);
            Assert.Equal(SupportSetResult.Unchanged, SupportCycle.SetTier(state, Catalog, "x86", SupportTier.Off, true));
        }

        // ---- setting a tier ----

        [Test]
        public void SetTier_Results()
        {
            CompanyArchState state = Company();

            Assert.Equal(SupportSetResult.Set, SupportCycle.SetTier(state, Catalog, "arm", SupportTier.High, false));
            Assert.Equal(SupportTier.High, state.Families["arm"].Tier);
            Assert.Equal(SupportSetResult.Unchanged, SupportCycle.SetTier(state, Catalog, "arm", SupportTier.High, false));

            Assert.Equal(SupportSetResult.NotResearched, SupportCycle.SetTier(state, Catalog, "risc", SupportTier.High, false), "RISC is in the state but not researched");
            Assert.Equal(SupportSetResult.NotResearched, SupportCycle.SetTier(state, Catalog, "arm64", SupportTier.High, false), "arm64 is not in the state at all");
            Assert.Equal(SupportSetResult.UnknownFamily, SupportCycle.SetTier(state, Catalog, "nonsense", SupportTier.High, false));
            Assert.Equal(SupportSetResult.UnknownFamily, SupportCycle.SetTier(state, Catalog, null, SupportTier.High, false));
            Assert.Equal(SupportSetResult.NoState, SupportCycle.SetTier(null, Catalog, "arm", SupportTier.High, false));
            Assert.Equal(SupportSetResult.BadTier, SupportCycle.SetTier(state, Catalog, "arm", (SupportTier)9, false));
            Assert.Equal(SupportSetResult.BadTier, SupportCycle.SetTier(state, Catalog, "arm", (SupportTier)(-1), false));
        }

        [Test]
        public void ANewTier_CountsFromTheNextMonthlyUpdate_FR105()
        {
            CompanyArchState state = Company();
            float before = SupportCycle.RunMonth(Company(), Catalog, Config(), 1990, 1f, float.PositiveInfinity).Upkeep;

            SupportCycle.SetTier(state, Catalog, "x86", SupportTier.High, false);
            Assert.Equal(30f, state.Families["x86"].Support, "setting the tier does not move the level by itself");

            SupportMonthResult month = SupportCycle.RunMonth(state, Catalog, Config(), 1990, 1f, float.PositiveInfinity);
            Assert.Near(2.0 * before, month.Upkeep, 1.0, "the next update charges double and heads for 95 (M9)");
            Assert.Near(30.0 + 65.0 * 0.1, state.Families["x86"].Support, 1e-3);
        }

        [Test]
        public void TheTiersListedByThePanel_AreTheFourInTheOrderOfTheEnum()
        {
            Assert.Equal(4, SupportCycle.Tiers.Length);
            for (int i = 0; i < SupportCycle.Tiers.Length; i++)
            {
                Assert.Equal(i, (int)SupportCycle.Tiers[i], "the index of the list is the value of the tier");
                Assert.True(SupportCycle.IsTier(SupportCycle.Tiers[i]));
            }
            Assert.False(SupportCycle.IsTier((SupportTier)4));
            Assert.False(SupportCycle.IsTier((SupportTier)(-1)));
        }

        // ---- determinism ----

        [Test]
        public void TheResult_DoesNotDependOnTheOrderTheFamiliesWereAddedIn()
        {
            CompanyArchState forward = Company(false, false);
            CompanyArchState backward = Company(false, true);
            float budget = Upkeep("x86", SupportTier.Normal, 1990, 1f) * 1.2f;
            forward.Families["arm"].Tier = SupportTier.Normal;
            backward.Families["arm"].Tier = SupportTier.Normal;

            SupportMonthResult a = SupportCycle.RunMonth(forward, Catalog, Config(), 1990, 1f, budget);
            SupportMonthResult b = SupportCycle.RunMonth(backward, Catalog, Config(), 1990, 1f, budget);

            Assert.Equal(a.Upkeep, b.Upkeep);
            Assert.Equal(a.Unfunded, b.Unfunded);
            foreach (string id in new[] { "proprietary", "x86", "arm", "risc" })
            {
                Assert.Equal(forward.Families[id].Support, backward.Families[id].Support, id);
            }
        }

        [Test]
        public void ARunningCompany_StaysInsideTheScaleForTenYears()
        {
            CompanyArchState state = Company();
            state.Families["arm"].Tier = SupportTier.High;
            state.Families["proprietary"].Tier = SupportTier.High;
            for (int month = 0; month < 120; month++)
            {
                SupportCycle.RunMonth(state, Catalog, Config(), 1990 + month / 12, 1.0f + month * 0.01f, float.PositiveInfinity);
                foreach (FamilyState family in state.Families.Values) Assert.InRange(family.Support, 0f, 100f, family.Id + " month " + month);
            }
            Assert.True(state.Families["proprietary"].Support <= 60f + 1e-3f, "the proprietary ISA is capped at 60 through the cycle too");
            Assert.True(state.Families["arm"].Support > 90f, "ARM on high is near 95 after ten years");
        }
    }
}
