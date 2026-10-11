using System;
using System.IO;
using System.Text.RegularExpressions;
using PTMod.Core;
using PTMod.Features.RivalPersonalities;
using PTMod.Tests.Architecture;

namespace PTMod.Tests.Features.RivalPersonalities
{
    /// <summary>
    /// The pace of launches and the research funding of the AI (TSK-008; design.md Component 4, formulas 1 and 2; FR-3 items 1, 2, 7, 8; FR-7 item 4):
    /// the arithmetic of the two postfixes, the multipliers of the archetypes with the <c>CounterLaunch</c> boosts by segment, the neutral values during
    /// the fast-forward and before a character is applied, and the declaration of both targets.
    /// </summary>
    [Covers(typeof(BehaviourMath), typeof(BehaviourModel), typeof(ReactionBoost))]
    public class BehaviourModifierTests
    {
        private const int Ibm = 4;
        private const int Amd = 1;
        private const double Tolerance = 1e-4;

        // ---- AIBehaviourCreation.OnTick (formula 1) ----

        [Test]
        public void Rating_IsMultiplied_WhenAtLeast105()
        {
            // AC-3.2: IBM (engineer, 0.92), rating 1.5 -> 1.38 (below the threshold 1.4, the counter of approval days is reset)
            Assert.Near(1.38, BehaviourMath.ApprovalRating(1.5f, 0.92f), Tolerance);
            Assert.Near(1.05 * 1.12, BehaviourMath.ApprovalRating(1.05f, 1.12f), Tolerance, "1.05 itself is multiplied");
        }

        [Test]
        public void Rating_NaN_ZeroNegativeAndBelow105_StayAsTheyAre()
        {
            Assert.True(float.IsNaN(BehaviourMath.ApprovalRating(float.NaN, 1.35f)));
            Assert.Equal(0f, BehaviourMath.ApprovalRating(0f, 1.35f));
            Assert.Equal(-2f, BehaviourMath.ApprovalRating(-2f, 1.35f));
            Assert.Equal(1.04f, BehaviourMath.ApprovalRating(1.04f, 1.35f), "a CPU weaker than the current one is never approved by the boost");
            Assert.Equal(0.5f, BehaviourMath.ApprovalRating(0.5f, 0.9f));
        }

        [Test]
        public void Rating_IsAtMost10()
        {
            Assert.Equal(10f, BehaviourMath.ApprovalRating(9f, 1.5f));
            Assert.Equal(10f, BehaviourMath.ApprovalRating(50f, 1.12f));
        }

        [Test]
        public void Rating_WithMultiplierOne_IsTheGamesValue()
        {
            Assert.Equal(50f, BehaviourMath.ApprovalRating(50f, 1f), "no clip without a multiplier: the vanilla value");
            Assert.Equal(1.5f, BehaviourMath.ApprovalRating(1.5f, float.NaN));
            Assert.Equal(1.5f, BehaviourMath.ApprovalRating(1.5f, 0f));
        }

        [Test]
        public void Rating_UsesTheThresholdOfTheConfig()
        {
            Assert.Equal(1.1f, BehaviourMath.ApprovalRating(1.1f, 1.35f, 1.2f), "below a threshold of 1.2");
            Assert.Near(1.1 * 1.35, BehaviourMath.ApprovalRating(1.1f, 1.35f, 1.0f), Tolerance);
        }

        // ---- AIBehaviourResearch.GetMaxFunding (formula 2) ----

        [Test]
        public void Funding_IsMultipliedAndClampedTo001_5()
        {
            // design.md: IBM (engineer 1.2), Normal, base 1.5 -> 1.8; with CounterLaunch (x1.25) -> 2.25
            Assert.Near(1.8, BehaviourMath.MaxFundingOf(1.5f, 1.2f), Tolerance);
            Assert.Near(2.25, BehaviourMath.MaxFundingOf(1.5f, 1.2f * 1.25f), Tolerance);
            Assert.Equal(5f, BehaviourMath.MaxFundingOf(5f, 1.2f), "at most 5");
            Assert.Equal(0.01f, BehaviourMath.MaxFundingOf(0f, 0.9f), "at least 0.01");
            Assert.Equal(0.01f, BehaviourMath.MaxFundingOf(-1f, 0.9f));
        }

        [Test]
        public void Funding_WithMultiplierOne_OrNaN_IsTheGamesValue()
        {
            Assert.Equal(6.25f, BehaviourMath.MaxFundingOf(6.25f, 1f));
            Assert.True(float.IsNaN(BehaviourMath.MaxFundingOf(float.NaN, 1.2f)));
            Assert.Equal(1.5f, BehaviourMath.MaxFundingOf(1.5f, float.PositiveInfinity));
        }

        // ---- the model ----

        [Test]
        public void BeforeTheCharacterIsApplied_EveryMultiplierIsOne()
        {
            var model = new BehaviourModel();
            model.AddBoost(new ReactionBoost("r-1", Ibm, Segment.HighEnd, 1.35f, 1.25f));

            Assert.False(model.IsApplied(Ibm));
            Assert.Equal(1f, model.ApprovalMultiplier(Ibm, Segment.HighEnd, false), "a boost alone does nothing before the character");
            Assert.Equal(1f, model.ResearchFundingMultiplier(Ibm, false));
            Assert.Equal(1f, model.LaunchPriceMultiplier(Ibm, false));
        }

        [Test]
        public void DuringTheFastForward_EveryMultiplierIsOne()
        {
            var model = new BehaviourModel();
            model.SetApplied(Ibm, 0.92f, 1.2f, 1.08f);
            model.AddBoost(new ReactionBoost("r-1", Ibm, Segment.HighEnd, 1.35f, 1.25f));

            Assert.Equal(1f, model.ApprovalMultiplier(Ibm, Segment.HighEnd, true));
            Assert.Equal(1f, model.ResearchFundingMultiplier(Ibm, true));
            Assert.Equal(1f, model.LaunchPriceMultiplier(Ibm, true));
        }

        [Test]
        public void TheArchetype_GivesItsNumbers()
        {
            var model = new BehaviourModel();
            var behaviour = new ArchetypeBehaviour { approvalBias = 0.92f, researchFundingMult = 1.2f, launchPriceMult = 1.08f };
            model.SetApplied(Ibm, behaviour);

            Assert.True(model.IsApplied(Ibm));
            Assert.Near(0.92, model.ApprovalMultiplier(Ibm, Segment.MidRange, false), Tolerance);
            Assert.Near(1.2, model.ResearchFundingMultiplier(Ibm, false), Tolerance);
            Assert.Near(1.08, model.LaunchPriceMultiplier(Ibm, false), Tolerance);
            Assert.Equal(1f, model.ApprovalMultiplier(Amd, Segment.MidRange, false), "another company");
        }

        [Test]
        public void Boosts_CountForTheirSegmentOnly_AndForTheFundingOfTheCompany()
        {
            // FR-7 item 4: CounterLaunch in High End -> the High End designer x1.35, the funding x1.25
            var model = new BehaviourModel();
            model.SetApplied(Amd, 1.12f, 1f, 0.96f);
            model.AddBoost(new ReactionBoost("r-1", Amd, Segment.HighEnd, 1.35f, 1.25f));

            Assert.Near(1.12 * 1.35, model.ApprovalMultiplier(Amd, Segment.HighEnd, false), Tolerance, "the designer of the segment");
            Assert.Near(1.12, model.ApprovalMultiplier(Amd, Segment.MidRange, false), Tolerance, "a designer of another segment");
            Assert.Near(1.25, model.ResearchFundingMultiplier(Amd, false), Tolerance, "the funding is the company's");
            Assert.Near(0.96, model.LaunchPriceMultiplier(Amd, false), Tolerance, "no boost on the launch price");
            Assert.Equal(1f, model.ApprovalMultiplier(Ibm, Segment.HighEnd, false), "not another company's boost");
        }

        [Test]
        public void Boosts_OfSegmentAny_CountForEveryDesigner_AndMultiply()
        {
            var model = new BehaviourModel();
            model.SetApplied(Amd, 1f, 1f, 1f);
            model.AddBoost(new ReactionBoost("r-1", Amd, Segment.LowEnd, 1.35f, 1.25f));
            model.AddBoost(new ReactionBoost("hist:x", Amd, Segment.Any, 1.1f, 1.0f));

            Assert.Near(1.35 * 1.1, model.ApprovalMultiplier(Amd, Segment.LowEnd, false), Tolerance);
            Assert.Near(1.1, model.ApprovalMultiplier(Amd, Segment.Industries, false), Tolerance);
            Assert.Equal(2, model.BoostCount);
        }

        [Test]
        public void Boosts_AreReplacedByIdAndRemoved()
        {
            var model = new BehaviourModel();
            model.SetApplied(Amd, 1f, 1f, 1f);
            Assert.True(model.AddBoost(new ReactionBoost("r-1", Amd, Segment.HighEnd, 1.35f, 1.25f)));
            Assert.True(model.AddBoost(new ReactionBoost("r-1", Amd, Segment.HighEnd, 1.2f, 1.1f)), "the same id replaces");
            Assert.Equal(1, model.BoostCount);
            Assert.Near(1.2, model.ApprovalMultiplier(Amd, Segment.HighEnd, false), Tolerance);

            Assert.True(model.RemoveBoost("r-1"));
            Assert.False(model.RemoveBoost("r-1"));
            Assert.Equal(1f, model.ApprovalMultiplier(Amd, Segment.HighEnd, false));
            Assert.False(model.AddBoost(new ReactionBoost(null, Amd, Segment.HighEnd, 1.2f, 1f)), "a boost needs an id");

            model.AddBoost(new ReactionBoost("r-2", Amd, Segment.HighEnd, 1.2f, 1f));
            model.AddBoost(new ReactionBoost("r-3", Ibm, Segment.HighEnd, 1.2f, 1f));
            Assert.Equal(1, model.RemoveBoostsOf(Amd));
            Assert.True(model.HasBoost("r-3"));
        }

        [Test]
        public void Boosts_AreClipped()
        {
            var model = new BehaviourModel();
            model.SetApplied(Amd, 1f, 1f, 1f);
            model.AddBoost(new ReactionBoost("r-1", Amd, Segment.Any, float.NaN, 1000f));
            Assert.Equal(1f, model.ApprovalMultiplier(Amd, Segment.HighEnd, false), "NaN is 1");
            Assert.Equal(BehaviourModel.MaxBoost, model.ResearchFundingMultiplier(Amd, false));
        }

        [Test]
        public void ClearingTheCharacter_GivesOneAgain_AndKeepsTheBoosts()
        {
            var model = new BehaviourModel();
            model.SetApplied(Amd, 1.12f, 0.95f, 0.92f);
            model.AddBoost(new ReactionBoost("r-1", Amd, Segment.HighEnd, 1.35f, 1.25f));

            Assert.True(model.ClearApplied(Amd));
            Assert.Equal(1f, model.ApprovalMultiplier(Amd, Segment.HighEnd, false));
            Assert.Equal(1, model.BoostCount, "the event system ends the boost, not the revert");

            model.SetApplied(Amd, 1.12f, 0.95f, 0.92f);
            model.ClearAllApplied();
            Assert.Equal(0, model.AppliedCount);
        }

        [Test]
        public void UnusableNumbersOfTheArchetype_CountAsOne()
        {
            var model = new BehaviourModel();
            model.SetApplied(Amd, float.NaN, -1f, 0f);
            Assert.Equal(1f, model.ApprovalMultiplier(Amd, Segment.HighEnd, false));
            Assert.Equal(1f, model.ResearchFundingMultiplier(Amd, false));
            Assert.Equal(1f, model.LaunchPriceMultiplier(Amd, false));
            model.SetApplied(Ibm, null);
            Assert.True(model.IsApplied(Ibm));
            Assert.Equal(1f, model.LaunchPriceMultiplier(Ibm, false));
        }

        [Test]
        public void TheMultipliers_AllocateNothing()
        {
            var model = new BehaviourModel();
            for (int uid = 0; uid < 13; uid++) model.SetApplied(uid, 1.05f, 1.1f, 0.95f);
            model.AddBoost(new ReactionBoost("r-1", Amd, Segment.HighEnd, 1.35f, 1.25f));

            Action day = () =>
            {
                float sum = 0f;
                for (int n = 0; n < 1000; n++)
                {
                    sum += model.ApprovalMultiplier(n % 13, (Segment)(n % 4), false);
                    sum += model.ResearchFundingMultiplier(n % 13, false);
                    sum += model.LaunchPriceMultiplier(n % 13, false);
                }
                GC.KeepAlive(sum);
            };
            day();
            long before = GC.GetAllocatedBytesForCurrentThread();
            day();
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.True(allocated < 1024, "the multipliers allocate: " + allocated + " bytes");
        }

        // ---- the patches are declared (the self-check of the core checks every declared target) ----

        [Test]
        public void BothTargets_AreDeclaredPostfixes_AndRegisteredThroughTheContext()
        {
            string folder = Path.Combine(SourceScan.SrcDirOrFail(), "Features", "RivalPersonalities");
            string patches = SourceScan.Strip(SourceScan.ReadText(Path.Combine(folder, "Behaviour", "CreationAndResearchPatches.cs")), false);
            string feature = SourceScan.Strip(SourceScan.ReadText(Path.Combine(folder, "RivalPersonalitiesFeature.cs")), false);

            Assert.True(Regex.IsMatch(patches, @"\[ModPatch\(\s*typeof\(\s*GameCreation\s*\)\s*,\s*""OnTick""\s*,\s*PatchKind\.Postfix\s*\)\]"),
                "postfix on AIBehaviourCreation.OnTick");
            Assert.True(Regex.IsMatch(patches, @"\[ModPatch\(\s*typeof\(\s*GameResearch\s*\)\s*,\s*""GetMaxFunding""\s*,\s*PatchKind\.Postfix\s*\)\]"),
                "postfix on AIBehaviourResearch.GetMaxFunding");
            Assert.True(Regex.IsMatch(patches, @"using\s+GameCreation\s*=\s*ProcessorTycoon\.AISystem\.AIBehaviourCreation\s*;"));
            Assert.True(Regex.IsMatch(patches, @"using\s+GameResearch\s*=\s*ProcessorTycoon\.AISystem\.AIBehaviourResearch\s*;"));
            Assert.True(Regex.IsMatch(feature, @"ctx\.Patch\(\s*typeof\(\s*CreationAndResearchPatches\s*\)\s*\)"),
                "declared with ctx.Patch: the registry of the core and its self-check know the targets");
        }
    }
}
