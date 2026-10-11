using System;
using PTMod.Features.CpuArchitectures;

namespace PTMod.Tests.Features.CpuArchitectures
{
    /// <summary>
    /// The architectures of an absorbed company going to its buyer (<c>ArchitectureApi.TransferCompanyState</c>, <c>rival-acquisitions</c>; design.md, "Migration and Compatibility"):
    /// the families researched by either are researched by the buyer, with the best support of the two when asked for it; what the buyer has stays his; the unfinished research,
    /// the active family and the tiers of the absorbed company do not move over.
    /// </summary>
    [Covers(typeof(StateTransfer))]
    public class StateTransferTests
    {
        private static CompanyArchState Company(int id, bool isPlayer, string active, params object[] familySupportResearched)
        {
            var state = new CompanyArchState(id, isPlayer) { Active = active };
            for (int i = 0; i + 2 < familySupportResearched.Length + 0; i += 3)
            {
                string familyId = (string)familySupportResearched[i];
                state.Families[familyId] = new FamilyState(familyId)
                {
                    Researched = (bool)familySupportResearched[i + 2],
                    Progress = (bool)familySupportResearched[i + 2] ? 1f : 0.4f,
                    Support = Convert.ToSingle(familySupportResearched[i + 1]),
                    Tier = SupportTier.Normal,
                    ResearchedYear = 1985,
                    ResearchedMonth = 6,
                };
            }
            return state;
        }

        [Test]
        public void TheBuyer_GetsTheFamiliesTheTargetResearched_WithTheBestSupport()
        {
            CompanyArchState target = Company(5, false, "arm", "proprietary", 40f, true, "arm", 70f, true, "risc", 55f, true);
            CompanyArchState player = Company(3, true, "x86", "proprietary", 60f, true, "x86", 80f, true);

            int changed = StateTransfer.Merge(target, player, true);

            Assert.True(player.IsResearched("arm") && player.IsResearched("risc"), "M1: the buyer now has ARM and RISC");
            Assert.Equal(70f, player.Families["arm"].Support, "the support of the target for the family the buyer lacked");
            Assert.Equal(1f, player.Families["arm"].Progress);
            Assert.Equal(1985, player.Families["arm"].ResearchedYear, "the date of the research goes with the family");
            Assert.Equal(60f, player.Families["proprietary"].Support, "the better of 40 and 60");
            Assert.Equal(80f, player.Families["x86"].Support, "what only the buyer has is untouched");
            Assert.Equal(2, changed, "ARM and RISC were added; the proprietary ISA had the larger support already");
        }

        [Test]
        public void TheMaximumOfTheSupport_IsTakenPerFamily_WhenAsked()
        {
            CompanyArchState target = Company(5, false, "x86", "x86", 90f, true);
            CompanyArchState buyerKeeps = Company(3, true, "x86", "x86", 50f, true);
            CompanyArchState buyerMerges = Company(3, true, "x86", "x86", 50f, true);

            Assert.Equal(0, StateTransfer.Merge(target, buyerKeeps, false), "without the flag the buyer's own level stays");
            Assert.Equal(50f, buyerKeeps.Families["x86"].Support);

            Assert.Equal(1, StateTransfer.Merge(target, buyerMerges, true));
            Assert.Equal(90f, buyerMerges.Families["x86"].Support, "mergeKeepBest: the larger of the two");
        }

        [Test]
        public void TheNewFamily_StartsWithTierOff_ChosenByTheBuyerLater_AndTheBuyerKeepsHisTiers()
        {
            CompanyArchState target = Company(5, false, "arm", "arm", 70f, true);
            CompanyArchState player = Company(3, true, "x86", "x86", 50f, true);
            player.Families["x86"].Tier = SupportTier.High;

            StateTransfer.Merge(target, player, true);
            Assert.Equal(SupportTier.Off, player.Families["arm"].Tier, "the target's tier is not the buyer's choice");
            Assert.Equal(SupportTier.High, player.Families["x86"].Tier, "the buyer pays for the support he chose");
        }

        [Test]
        public void TheUnfinishedResearchOfTheTarget_ActiveFamilyAndLab_DoNotMoveOver()
        {
            CompanyArchState target = Company(5, false, "risc", "proprietary", 40f, true, "risc", 60f, true, "arm", 20f, false);
            target.CurrentResearch = "arm";
            CompanyArchState player = Company(3, true, "x86", "proprietary", 40f, true, "x86", 70f, true);

            StateTransfer.Merge(target, player, true);
            Assert.False(player.IsResearched("arm"), "a research that was not finished is not transferred (the lab belongs to the company)");
            Assert.False(player.Families.ContainsKey("arm"), "no empty entry either");
            Assert.Equal("x86", player.Active, "he keeps designing on what he designs on");
            Assert.Null(player.CurrentResearch);
        }

        [Test]
        public void ABuyerWithoutAnActiveFamily_TakesTheTargetsWhenItIsNowResearched()
        {
            CompanyArchState target = Company(5, false, "risc", "risc", 60f, true);
            CompanyArchState buyer = Company(3, true, null);
            StateTransfer.Merge(target, buyer, true);
            Assert.Equal("risc", buyer.Active);

            CompanyArchState other = Company(3, true, null);
            CompanyArchState unresearchedActive = Company(5, false, "arm", "arm", 20f, false);
            StateTransfer.Merge(unresearchedActive, other, true);
            Assert.Null(other.Active, "an active family that did not come over is not taken");
        }

        [Test]
        public void ABuyerThatWasStillResearchingTheFamily_HasItResearchedNow_TheTickGivesTheLabBack()
        {
            CompanyArchState target = Company(5, false, "risc", "risc", 60f, true);
            CompanyArchState buyer = Company(3, true, "x86", "x86", 50f, true, "risc", 15f, false);
            buyer.CurrentResearch = "risc";

            StateTransfer.Merge(target, buyer, true);
            Assert.True(buyer.Families["risc"].Researched);
            Assert.Equal(1f, buyer.Families["risc"].Progress);
            Assert.Equal(60f, buyer.Families["risc"].Support, "the larger of the leftover 15 and 60");
            Assert.Equal("risc", buyer.CurrentResearch, "left to the daily tick, which sees the family researched and releases the lab");
        }

        [Test]
        public void Merging_IsSafeForMissingStates_TheSameState_AndAnEmptyTarget_AndIsRepeatable()
        {
            CompanyArchState player = Company(3, true, "x86", "x86", 80f, true);
            Assert.Equal(0, StateTransfer.Merge(null, player, true));
            Assert.Equal(0, StateTransfer.Merge(player, null, true));
            Assert.Equal(0, StateTransfer.Merge(player, player, true));
            Assert.Equal(0, StateTransfer.Merge(new CompanyArchState(9, false), player, true));

            CompanyArchState target = Company(5, false, "arm", "arm", 70f, true);
            Assert.Equal(1, StateTransfer.Merge(target, player, true));
            Assert.Equal(0, StateTransfer.Merge(target, player, true), "the second time there is nothing new");
            Assert.Equal(80f, player.Families["x86"].Support);
        }

        [Test]
        public void TheResult_DoesNotDependOnTheOrderOfTheFamilies()
        {
            Func<bool, CompanyArchState> merged = reverse =>
            {
                CompanyArchState target = reverse
                    ? Company(5, false, "arm", "risc", 60f, true, "arm", 70f, true, "proprietary", 40f, true)
                    : Company(5, false, "arm", "proprietary", 40f, true, "arm", 70f, true, "risc", 60f, true);
                CompanyArchState player = Company(3, true, "x86", "proprietary", 55f, true);
                StateTransfer.Merge(target, player, true);
                return player;
            };

            CompanyArchState a = merged(false);
            CompanyArchState b = merged(true);
            Assert.Equal(a.Families.Count, b.Families.Count);
            foreach (string id in a.Families.Keys)
            {
                Assert.Equal(a.Families[id].Support, b.Families[id].Support, id);
                Assert.Equal(a.Families[id].Researched, b.Families[id].Researched, id);
            }
        }
    }
}
