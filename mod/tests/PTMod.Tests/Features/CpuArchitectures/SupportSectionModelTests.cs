using System.Collections.Generic;
using PTMod.Features.CpuArchitectures;

namespace PTMod.Tests.Features.CpuArchitectures
{
    /// <summary>
    /// What the section "Программная поддержка" of the panel shows (FR-10 item 7, TSK-023), without the game: one row per researched family in the order of the
    /// catalog, the numbers of a row (level, tier, target, cost of a month, the level in a year, the multiplier the level gives the rating), the cost that follows
    /// a new tier at once, and M9: the tier "Высокая" doubles the cost, the level heads for 95 and the forecast agrees with the real levels of twelve monthly
    /// updates to within one point. Also when the section is shown at all (stage 2 of the balance file, a working feature) and which strings its list uses.
    /// </summary>
    [Covers(typeof(SupportSectionModel), typeof(SupportRowInfo))]
    public class SupportSectionModelTests
    {
        private static ArchCatalog _catalog;

        private static ArchCatalog Catalog
        {
            get { return _catalog ?? (_catalog = ArchTestKit.BuiltInCatalog()); }
        }

        private static BalanceConfig Balance(int stage = 2)
        {
            BalanceConfig balance = ArchTestKit.BuiltInBalance();
            balance.stage = stage;
            return balance;
        }

        private static CompanyArchState Player()
        {
            var state = new CompanyArchState(1, true);
            state.Families["proprietary"] = new FamilyState("proprietary") { Researched = true, Progress = 1f, Support = 31f, Tier = SupportTier.Off };
            state.Families["x86"] = new FamilyState("x86") { Researched = true, Progress = 1f, Support = 30f, Tier = SupportTier.Normal };
            state.Families["arm"] = new FamilyState("arm") { Researched = false, Progress = 0.4f, Support = 0f, Tier = SupportTier.Off };
            return state;
        }

        private static SupportRowInfo RowOf(CompanyArchState state, string id, int year = 1990, float inflation = 1f, bool editable = true)
        {
            return SupportSectionModel.RowOf(Catalog.Get(id), state.GetFamily(id), Balance(), year, inflation, editable);
        }

        // ---- the rows ----

        [Test]
        public void TheRows_AreTheResearchedFamilies_InTheOrderOfTheCatalog()
        {
            List<SupportRowInfo> rows = SupportSectionModel.Rows(Catalog, Player(), Balance(), 1990, 1f, true);
            Assert.Equal("proprietary,x86", string.Join(",", rows.ConvertAll(r => r.FamilyId).ToArray()), "ARM is not researched: no row");

            CompanyArchState later = Player();
            later.Families["arm"].Researched = true;
            rows = SupportSectionModel.Rows(Catalog, later, Balance(), 1990, 1f, true);
            Assert.Equal("proprietary,x86,arm", string.Join(",", rows.ConvertAll(r => r.FamilyId).ToArray()), "a family researched later gets its row, in the catalog order (x86, risc, arm)");
        }

        [Test]
        public void NoRows_ForNoState_NoCatalog_OrAFamilyTheCompanyDoesNotHave()
        {
            Assert.Equal(0, SupportSectionModel.Rows(Catalog, null, Balance(), 1990, 1f, true).Count);
            Assert.Equal(0, SupportSectionModel.Rows(null, Player(), Balance(), 1990, 1f, true).Count);
            Assert.Equal(0, SupportSectionModel.Rows(Catalog, Player(), null, 1990, 1f, true).Count);

            Assert.Null(SupportSectionModel.RowOf(Catalog.Get("risc"), null, Balance(), 1990, 1f, true), "no state for the family");
            Assert.Null(SupportSectionModel.RowOf(null, new FamilyState("x86") { Researched = true }, Balance(), 1990, 1f, true));
            Assert.Null(SupportSectionModel.RowOf(Catalog.Get("arm"), Player().Families["arm"], Balance(), 1990, 1f, true), "not researched");
        }

        [Test]
        public void ARow_CarriesTheNumbersOfTheFormulas()
        {
            SupportRowInfo row = RowOf(Player(), "x86", 1990, 1.4f);

            Assert.Equal("x86", row.FamilyId);
            Assert.Equal(30f, row.Support);
            Assert.Equal(SupportTier.Normal, row.Tier);
            Assert.Equal(70f, row.Target);
            Assert.Equal(0.10f, row.Rate);
            Assert.Equal(100f, row.Cap);
            Assert.False(row.Capped);
            Assert.Equal(1500000f, row.BaseUpkeep);
            Assert.Equal(1f, row.TierFactor);
            Assert.Equal(1f, row.EraFactor, "x86 has no era points");
            Assert.Near(1.4, row.Inflation, 1e-6);
            Assert.Near(1500000.0 * 1.4, row.Upkeep, 1.0, "the product of the four terms");
            Assert.Near(58.7, row.Forecast, 0.05, "AC-10.1 in the panel");
            Assert.Near(0.90 + 0.20 * 0.30, row.RatingFactor, 1e-5, "f_sup of the level 30");
            Assert.True(row.Editable);
        }

        [Test]
        public void TheTerms_OfTheUpkeep_MultiplyToTheShownCost()
        {
            CompanyArchState state = Player();
            state.Families["arm"].Researched = true;
            state.Families["arm"].Tier = SupportTier.High;

            SupportRowInfo row = RowOf(state, "arm", 1995, 1.8f);
            Assert.Near(600000.0, row.BaseUpkeep, 1e-3);
            Assert.Equal(2f, row.TierFactor);
            Assert.Near(1.115, row.EraFactor, 0.001, "ARM in 1995");
            Assert.Near(row.BaseUpkeep * row.TierFactor * row.EraFactor * row.Inflation, row.Upkeep, 1.0, "what the tooltip writes as a product is what the row says");
        }

        // ---- M9: the tier "high" ----

        [Test]
        public void M9_HighDoublesTheCost_TheLevelHeadsFor95_AndTheForecastMatchesTheFact()
        {
            CompanyArchState state = Player();
            SupportRowInfo normal = RowOf(state, "x86", 1990, 1.3f);

            // The player picks "Высокая": the cost shown follows at once, before any month has passed.
            Assert.Equal(SupportSetResult.Set, SupportCycle.SetTier(state, Catalog, "x86", SupportTier.High, false));
            SupportRowInfo high = RowOf(state, "x86", 1990, 1.3f);
            Assert.Near(2.0 * normal.Upkeep, high.Upkeep, 1.0, "the cost of a month x2");
            Assert.Equal(95f, high.Target, "it grows towards 95");
            Assert.Equal(30f, high.Support, "the level has not moved yet: the tier counts from the next month");
            Assert.True(high.Forecast > normal.Forecast);

            // Twelve real monthly updates: the forecast agrees with the fact to within one point.
            for (int month = 0; month < 12; month++) SupportCycle.RunMonth(state, Catalog, Balance().support, 1990, 1.3f, float.PositiveInfinity);
            Assert.Near(high.Forecast, state.Families["x86"].Support, 1.0, "forecast " + high.Forecast + " vs fact " + state.Families["x86"].Support);
        }

        [Test]
        public void ForEveryTier_TheForecastIsTheFactWithinOnePoint()
        {
            foreach (SupportTier tier in SupportCycle.Tiers)
            {
                CompanyArchState state = Player();
                state.Families["x86"].Tier = tier;
                SupportRowInfo row = RowOf(state, "x86");
                for (int month = 0; month < 12; month++) SupportCycle.RunMonth(state, Catalog, Balance().support, 1990, 1f, float.PositiveInfinity);
                Assert.Near(row.Forecast, state.Families["x86"].Support, 1.0, tier.ToString());
            }
        }

        [Test]
        public void ARowFollowsTheTierAtOnce_NotTheNextMonth()
        {
            CompanyArchState state = Player();
            var costs = new List<float>();
            foreach (SupportTier tier in SupportCycle.Tiers)
            {
                state.Families["x86"].Tier = tier;
                costs.Add(RowOf(state, "x86").Upkeep);
            }
            Assert.Equal(0f, costs[0], "off");
            Assert.Near(750000.0, costs[1], 1e-3, "low");
            Assert.Near(1500000.0, costs[2], 1e-3, "normal");
            Assert.Near(3000000.0, costs[3], 1e-3, "high");
        }

        // ---- the proprietary ISA ----

        [Test]
        public void TheProprietaryIsa_IsCappedAt60_AndSaysSo()
        {
            CompanyArchState state = Player();
            state.Families["proprietary"].Tier = SupportTier.High;
            SupportRowInfo row = RowOf(state, "proprietary");

            Assert.True(row.Capped);
            Assert.Equal(60f, row.Cap);
            Assert.Equal(60f, row.Target, "the tier would lead to 95 but the cap holds it");
            Assert.True(row.Forecast <= 60f);
        }

        // ---- editing and robustness ----

        [Test]
        public void TheEditableFlag_IsPassedThrough()
        {
            Assert.False(RowOf(Player(), "x86", editable: false).Editable, "in default the list is locked");
            Assert.True(RowOf(Player(), "x86", editable: true).Editable);
        }

        [Test]
        public void AnUnusableTierOrLevel_IsShownAsOffAndInRange()
        {
            CompanyArchState state = Player();
            state.Families["x86"].Tier = (SupportTier)7;
            state.Families["x86"].Support = float.NaN;
            SupportRowInfo row = RowOf(state, "x86");
            Assert.Equal(SupportTier.Off, row.Tier);
            Assert.Equal(0f, row.Support);
            Assert.Equal(0f, row.Upkeep);

            state.Families["x86"].Support = 400f;
            Assert.Equal(100f, RowOf(state, "x86").Support);
        }

        [Test]
        public void TotalUpkeep_AddsTheRows()
        {
            CompanyArchState state = Player();
            state.Families["proprietary"].Tier = SupportTier.Normal;
            List<SupportRowInfo> rows = SupportSectionModel.Rows(Catalog, state, Balance(), 1990, 1f, true);
            Assert.Near(300000.0 + 1500000.0, SupportSectionModel.TotalUpkeep(rows), 1.0);
            Assert.Equal(0f, SupportSectionModel.TotalUpkeep(null));
            Assert.Equal(0f, SupportSectionModel.TotalUpkeep(new List<SupportRowInfo>()));
        }

        // ---- when the section is there ----

        [Test]
        public void TheSection_IsShownAtStageTwoOnly_WhileTheFeatureWorks()
        {
            Assert.True(SupportSectionModel.IsShown(Balance(2), true));
            Assert.False(SupportSectionModel.IsShown(Balance(1), true), "stage 1: support gives x1 and costs nothing, nothing to show");
            Assert.False(SupportSectionModel.IsShown(Balance(2), false), "the feature is off or fast-forwarding");
            Assert.False(SupportSectionModel.IsShown(null, true));
        }

        [Test]
        public void TheBuiltInBalanceFile_StartsAtStageOne_TheOwnerSwitchesStageTwoOn()
        {
            Assert.Equal(1, ArchTestKit.BuiltInBalance().stage, "TSK-020: the full version stays off until the owner has checked the MVP");
        }

        [Test]
        public void TheTierNames_AreFourDifferentKeys_OfTheSupportFamily()
        {
            var keys = new HashSet<string>();
            foreach (SupportTier tier in SupportCycle.Tiers)
            {
                string key = SupportSectionModel.TierKey(tier);
                Assert.True(key.StartsWith("arch.support.tier.", System.StringComparison.Ordinal), key);
                Assert.True(keys.Add(key), "each tier has its own name: " + key);
            }
            Assert.Equal("arch.support.tier.off", SupportSectionModel.TierKey(SupportTier.Off));
            Assert.Equal("arch.support.tier.high", SupportSectionModel.TierKey(SupportTier.High));
            Assert.Equal("arch.support.tier.off", SupportSectionModel.TierKey((SupportTier)9), "an unusable tier reads as off");
        }
    }
}
