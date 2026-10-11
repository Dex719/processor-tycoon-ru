using System;
using System.Collections.Generic;
using PTMod.Core;
using PTMod.Features.CpuArchitectures;

namespace PTMod.Tests.Features.CpuArchitectures
{
    /// <summary>
    /// What the adaptive AI reads and what other features can set (FR-14, FR-15, TSK-027, TSK-028): the shares of the sales by segment (the sales of the CPUs of the
    /// company; with no sales the votes of the price segments; with no CPUs a quarter each), the weights that <c>ArchitectureApi.SetAiWeightOverride</c> puts over
    /// the record of a company, and the table of the rivals.
    /// </summary>
    [Covers(typeof(SegmentShares), typeof(AiOverrides), typeof(AiOverrideResult), typeof(RivalsModel), typeof(RivalRow), typeof(RivalInput))]
    public class AiMarketTests
    {
        // ---- the shares of the segments ----

        [Test]
        public void TheShares_FollowTheSalesOfTheCompany()
        {
            float[] shares = SegmentShares.Resolve(600, 0, 100, 300, new[] { 9, 9, 9, 9 });
            Assert.Near(0.6, shares[0], 1e-6);
            Assert.Near(0.0, shares[1], 1e-6);
            Assert.Near(0.1, shares[2], 1e-6);
            Assert.Near(0.3, shares[3], 1e-6);
            Assert.Near(1.0, shares[0] + shares[1] + shares[2] + shares[3], 1e-6);
        }

        [Test]
        public void WithoutSales_TheVotesOfThePriceSegmentsCount_OneForEachCpu()
        {
            // The votes are what ModSegments.Classify gave the current CPUs of the company: the feature has no classifier of its own.
            float[] shares = SegmentShares.Resolve(0, 0, 0, 0, new[] { 1, 2, 0, 1 });
            Assert.Near(0.25, shares[0], 1e-6);
            Assert.Near(0.5, shares[1], 1e-6);
            Assert.Near(0.0, shares[2], 1e-6);
            Assert.Near(0.25, shares[3], 1e-6);
        }

        [Test]
        public void WithoutSalesAndWithoutCpus_EachSegmentGetsAQuarter()
        {
            foreach (float[] shares in new[] { SegmentShares.Resolve(0, 0, 0, 0, new[] { 0, 0, 0, 0 }), SegmentShares.Resolve(0, 0, 0, 0, null), SegmentShares.Equal() })
            {
                Assert.Equal(SegmentShares.Count, shares.Length);
                foreach (float share in shares) Assert.Near(0.25, share, 1e-6);
            }
            Assert.Null(SegmentShares.FromSales(0, 0, 0, 0));
            Assert.Null(SegmentShares.FromVotes(new[] { 0, 0, 0, 0 }));
            Assert.Null(SegmentShares.FromVotes(new[] { 1, 2 }), "a wrong length");
            Assert.Null(SegmentShares.FromSales(-5, -1, 0, 0), "negative counts are no sales");
        }

        [Test]
        public void TheSegmentIndex_IsTheOrderOfSegment_AnyHasNone()
        {
            Assert.Equal(0, SegmentShares.IndexOf(Segment.HighEnd));
            Assert.Equal(1, SegmentShares.IndexOf(Segment.MidRange));
            Assert.Equal(2, SegmentShares.IndexOf(Segment.LowEnd));
            Assert.Equal(3, SegmentShares.IndexOf(Segment.Industries));
            Assert.Equal(-1, SegmentShares.IndexOf(Segment.Any));
        }

        [Test]
        public void UsableShares_AreFourFiniteNonNegativeNumbersWithASum()
        {
            Assert.True(SegmentShares.IsUsable(new[] { 0.5f, 0.5f, 0f, 0f }));
            Assert.False(SegmentShares.IsUsable(null));
            Assert.False(SegmentShares.IsUsable(new[] { 1f, 1f }));
            Assert.False(SegmentShares.IsUsable(new[] { 0f, 0f, 0f, 0f }), "no weight at all");
            Assert.False(SegmentShares.IsUsable(new[] { 1f, float.NaN, 0f, 0f }));
            Assert.False(SegmentShares.IsUsable(new[] { 1f, -1f, 0f, 0f }));
            Assert.False(SegmentShares.IsUsable(new[] { 1f, float.PositiveInfinity, 0f, 0f }));
        }

        // ---- the weights of other features ----

        [Test]
        public void SetAiWeightOverride_PutsAWeightOverTheRecord_AndTakesItAway()
        {
            var list = new List<AiOverrideSave>();
            Assert.Equal(AiOverrideResult.Set, AiOverrides.Set(list, 5, "risc", 2.5f, 100));
            Assert.Equal(1, list.Count);
            Assert.Equal(2.5f, AiOverrides.Get(list, 5, "risc").Value);
            Assert.Null(AiOverrides.Get(list, 5, "arm"));
            Assert.Null(AiOverrides.Get(list, 6, "risc"));

            Assert.Equal(AiOverrideResult.Set, AiOverrides.Set(list, 5, "risc", 0.5f, 100));
            Assert.Equal(1, list.Count, "the same company and family: replaced, not added");
            Assert.Equal(0.5f, list[0].weight);

            Assert.Equal(AiOverrideResult.Removed, AiOverrides.Set(list, 5, "risc", null, 100));
            Assert.Equal(0, list.Count);
            Assert.Equal(AiOverrideResult.NothingToRemove, AiOverrides.Set(list, 5, "risc", null, 100));
        }

        [Test]
        public void SetAiWeightOverride_RefusesBadInput_HoldsTheWeightToTheScale_AndTheList()
        {
            var list = new List<AiOverrideSave>();
            Assert.Equal(AiOverrideResult.BadId, AiOverrides.Set(list, -1, "risc", 1f, 100));
            Assert.Equal(AiOverrideResult.BadId, AiOverrides.Set(list, 1, "RISC!", 1f, 100));
            Assert.Equal(AiOverrideResult.BadId, AiOverrides.Set(list, 1, null, 1f, 100));
            Assert.Equal(AiOverrideResult.BadWeight, AiOverrides.Set(list, 1, "risc", float.NaN, 100));
            Assert.Equal(AiOverrideResult.BadWeight, AiOverrides.Set(list, 1, "risc", float.PositiveInfinity, 100));
            Assert.Equal(0, list.Count, "nothing was kept");

            AiOverrides.Set(list, 1, "risc", 99f, 100);
            Assert.Equal(AiPreference.MaxWeight, list[0].weight, "held to 3");
            AiOverrides.Set(list, 1, "arm", -4f, 100);
            Assert.Equal(0f, list[1].weight, "held to 0");

            Assert.Equal(AiOverrideResult.Full, AiOverrides.Set(list, 2, "x86", 1f, 2), "the list is full");
            Assert.Equal(AiOverrideResult.Set, AiOverrides.Set(list, 1, "arm", 1f, 2), "but an existing entry may still change");
            Assert.Throws<ArgumentNullException>(() => AiOverrides.Set(null, 1, "risc", 1f, 5));
        }

        [Test]
        public void TheEffectiveRecord_HasTheOverriddenWeights_TheTableRecordIsNeverChanged()
        {
            ArchCatalog catalog = ArchTestKit.BuiltInCatalog();
            AiPreference motorola = ArchTestKit.BuiltInPreferences(catalog).Match(7);
            var list = new List<AiOverrideSave>
            {
                new AiOverrideSave { company = 5, arch = "risc", weight = 2.5f },
                new AiOverrideSave { company = 5, arch = "riscv", weight = 1.5f },
                new AiOverrideSave { company = 9, arch = "arm", weight = 0f },
                new AiOverrideSave { company = 5, arch = "BAD id", weight = 1f },
                new AiOverrideSave { company = 5, arch = "x86", weight = float.NaN },
            };

            AiPreference effective = AiOverrides.Apply(motorola, 5, list);
            Assert.False(ReferenceEquals(motorola, effective));
            Assert.Equal(2.5f, effective.WeightOf("risc"));
            Assert.Equal(1.5f, effective.WeightOf("riscv"), "a family the record does not mention gets the override as its weight");
            Assert.Equal(motorola.WeightOf("arm"), effective.WeightOf("arm"), "the override of another company does not count");
            Assert.Equal(0f, effective.WeightOf("x86"), "a broken entry is skipped");
            Assert.Equal(motorola.LagYears, effective.LagYears);
            Assert.Equal(motorola.UniqueId, effective.UniqueId);
            Assert.Equal(1f, motorola.WeightOf("risc"), "the record of the table keeps its weights");
            Assert.Equal(0f, motorola.WeightOf("riscv"));

            Assert.True(ReferenceEquals(motorola, AiOverrides.Apply(motorola, 7, list)), "a company without overrides decides on the record itself");
            Assert.True(ReferenceEquals(motorola, AiOverrides.Apply(motorola, 5, null)));
            Assert.True(ReferenceEquals(motorola, AiOverrides.Apply(motorola, 5, new List<AiOverrideSave>())));
            Assert.Null(AiOverrides.Apply(null, 5, list));
        }

        // ---- the table of the rivals ----

        private static RivalInput Rival(int id, string name, string active = null, string research = null, float progress = 0f, bool paused = false,
                                        bool foundry = false, bool bankrupt = false, bool unsown = false)
        {
            CompanyArchState state = null;
            if (!unsown)
            {
                state = new CompanyArchState(id, false) { Active = active, CurrentResearch = research, Paused = paused };
                if (research != null) state.Families[research] = new FamilyState(research) { Progress = progress };
            }
            return new RivalInput { CompanyId = id, Name = name, IsFoundry = foundry, IsBankrupt = bankrupt, State = state };
        }

        [Test]
        public void TheTable_ListsTheCompaniesThatDesignCpus_SortedByName()
        {
            List<RivalRow> rows = RivalsModel.Rows(new[]
            {
                Rival(3, "Motorola", "risc"),
                Rival(1, "AMD", "x86", "x86_64", 0.4f),
                Rival(2, "TSMC", foundry: true),
                Rival(4, "Cyrix", "x86", bankrupt: true),
                Rival(5, "intel", "x86", unsown: true),
                null,
            });

            Assert.Equal(3, rows.Count, "a foundry, a bankrupt company and a null are not listed");
            Assert.Equal("AMD", rows[0].Name);
            Assert.Equal("intel", rows[1].Name, "sorted ignoring case");
            Assert.Equal("Motorola", rows[2].Name);

            Assert.Equal("x86", rows[0].ActiveId);
            Assert.Equal("x86_64", rows[0].ResearchId);
            Assert.Equal(40, rows[0].ResearchPercent);
            Assert.False(rows[0].Paused);

            Assert.Null(rows[1].ActiveId, "a company the seed has not reached: dashes");
            Assert.Null(rows[1].ResearchId);
            Assert.Equal("risc", rows[2].ActiveId);
            Assert.Null(rows[2].ResearchId);
        }

        [Test]
        public void TheTable_ShowsAPausedResearch_AndTheProgressRoundsDown()
        {
            List<RivalRow> rows = RivalsModel.Rows(new[] { Rival(1, "AMD", "x86", "arm", 0.999f, paused: true) });
            Assert.True(rows[0].Paused);
            Assert.Equal(99, rows[0].ResearchPercent, "99.9 % is not yet 100");
            Assert.Equal(100, RivalsModel.Percent(1f));
            Assert.Equal(100, RivalsModel.Percent(1.5f));
            Assert.Equal(0, RivalsModel.Percent(-1f));
            Assert.Equal(0, RivalsModel.Percent(float.NaN));
            Assert.Equal(7, RivalsModel.Percent(0.0749f));
        }

        [Test]
        public void TheTable_SameNamesAreOrderedById_AndNothingIsAnEmptyList()
        {
            List<RivalRow> rows = RivalsModel.Rows(new[] { Rival(9, "Same", "x86"), Rival(2, "Same", "arm") });
            Assert.Equal(2, rows[0].CompanyId);
            Assert.Equal(9, rows[1].CompanyId);
            Assert.Equal(0, RivalsModel.Rows(null).Count);
            Assert.Equal(0, RivalsModel.Rows(new RivalInput[0]).Count);
            Assert.Equal(string.Empty, RivalsModel.Rows(new[] { new RivalInput { CompanyId = 1, Name = null } })[0].Name);
        }
    }
}
