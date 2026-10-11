using System.Collections.Generic;
using System.Linq;
using PTMod.Features.RivalAcquisitions;

namespace PTMod.Tests.Features.RivalAcquisitions
{
    /// <summary>
    /// <c>AcquisitionPlanBuilder</c> (TSK-008, FR-7…FR-9, FR-12): technologies, lines, CPU families, contracts, the foundry flag, the consequences for the
    /// historical companies and the rules that block a plan.
    /// </summary>
    [Covers(typeof(AcquisitionPlanBuilder), typeof(AcquisitionPlan), typeof(PlanOptions), typeof(LineMove), typeof(LineSale), typeof(CpuFate), typeof(FamilyMember),
        typeof(CpuFamily), typeof(PlanNote))]
    public class AcquisitionPlanBuilderTests
    {
        private const double Million = 1000000.0;

        private static AcquisitionPlan Plan(CompanySnapshot t, PlayerSnapshot p = null, bool keep = true, bool ack = true, DealType type = DealType.Hostile,
            AcquisitionsState s = null)
        {
            p = p ?? RivalTestData.Player();
            DealQuote q = RivalTestData.Quote(type, t, p, s);
            return AcquisitionPlanBuilder.Build(q, t, p, new PlanOptions { KeepLines = keep, ConsequencesAcknowledged = ack });
        }

        /// <summary>The 1985 target without the CPUs: tests add their own.</summary>
        private static CompanySnapshot Target()
        {
            CompanySnapshot t = RivalTestData.ExampleTarget();
            t.Cpus.Clear();
            return t;
        }

        private static CpuSnapshot Live(int id, int baseId = -1, bool inHouse = true, int makerId = -1)
        {
            CpuSnapshot c = RivalTestData.Cpu(id, baseId);
            c.MadeInHouseByTarget = inHouse;
            c.IsOutsourced = makerId >= 0;
            c.OutsourcedId = makerId;
            if (makerId >= 0) c.MakerName = "Foundry" + makerId;
            return c;
        }

        private static FamilyMember Member(AcquisitionPlan plan, int saveId)
        {
            return plan.TransferFamilies.SelectMany(f => f.Members).First(m => m.SaveId == saveId);
        }

        // ---- the example ----

        [Test]
        public void TheExampleDeal_TakesTheTechnologiesTheLinesAndBothCpus()
        {
            AcquisitionPlan plan = Plan(RivalTestData.ExampleTarget());

            Assert.True(plan.CanExecute, string.Join(",", plan.BlockingErrors));
            Assert.Equal(DealType.Hostile, plan.Type);
            Assert.True(plan.KeepLines);
            Assert.Equal(3, plan.TechUnlockIds.Count);
            Assert.Equal(1, plan.LineMoves.Count);
            Assert.Equal(40, plan.LinesMoved);
            Assert.Equal(0, plan.LineSales.Count);
            Assert.Equal(1, plan.TransferFamilies.Count);
            Assert.Equal(new List<int> { 1201, 1207 }, plan.TransferredCpuIds());
            Assert.Equal(0, plan.RetireCpuIds.Count);
        }

        // ---- technologies and nodes ----

        [Test]
        public void Technologies_AreUnlockedShallowestFirst_ThenById()
        {
            CompanySnapshot t = Target();
            t.TechGap.Clear();
            t.TechGap.Add(RivalTestData.Tech(30, depth: 2));
            t.TechGap.Add(RivalTestData.Tech(10, depth: 0));
            t.TechGap.Add(RivalTestData.Tech(20, depth: 1));
            t.TechGap.Add(RivalTestData.Tech(5, depth: 2));
            t.TechGap.Add(RivalTestData.Tech(10, depth: 0));   // a repeated id is dropped

            AcquisitionPlan plan = Plan(t);

            Assert.Equal(new List<int> { 10, 20, 5, 30 }, plan.TechUnlockIds);
            Assert.Equal(plan.TechUnlockIds, plan.TechUnlocks.Select(x => x.Id).ToList(), "the objects follow the ids");
        }

        [Test]
        public void PartialProgress_IsPlannedOnlyWhereTheTargetIsFurtherAlong()
        {
            CompanySnapshot t = Target();
            t.PartialTech.Add(new TechProgressSnapshot { Id = 7, TargetProgress = 0.6, PlayerProgress = 0.2 });
            t.PartialTech.Add(new TechProgressSnapshot { Id = 8, TargetProgress = 0.3, PlayerProgress = 0.3 });
            t.PartialTech.Add(new TechProgressSnapshot { Id = 9, TargetProgress = 0.1, PlayerProgress = 0.5 });
            t.PartialTech.Add(new TechProgressSnapshot { Id = 100, TargetProgress = 1.0, PlayerProgress = 0.0 });   // unlocked in full anyway

            AcquisitionPlan plan = Plan(t);

            Assert.Equal(new List<int> { 7 }, plan.TechProgress.Select(x => x.Id).ToList());
        }

        [Test]
        public void NodeExperience_IsRaisedOnlyWhereThePlayerIsBehind()
        {
            CompanySnapshot t = Target();
            t.Nodes.Add(new NodeSnapshot { NodeId = 3, TargetExperience = 0.8, PlayerExperience = 0.2 });
            t.Nodes.Add(new NodeSnapshot { NodeId = 2, TargetExperience = 0.5, PlayerExperience = 0.5 });
            t.Nodes.Add(new NodeSnapshot { NodeId = 1, TargetExperience = 0.1, PlayerExperience = 0.9 });
            t.Nodes.Add(new NodeSnapshot { NodeId = 0, TargetExperience = 0.4, PlayerExperience = 0.1 });

            AcquisitionPlan plan = Plan(t);

            Assert.Equal(new List<int> { 0, 3 }, plan.NodeRaises.Select(n => n.NodeId).ToList());
            Assert.Near(0.6, plan.NodeRaises[1].Gap, 1e-12);
        }

        // ---- lines ----

        [Test]
        public void KeepingTheLines_MovesThemToTheSameWaferSize_AndRaisesTheUpkeep()
        {
            AcquisitionPlan plan = Plan(RivalTestData.ExampleTarget());

            LineMove move = plan.LineMoves[0];
            Assert.Equal(RivalTestData.WaferOld, move.FromWaferId);
            Assert.Equal(RivalTestData.WaferOld, move.ToWaferId);
            Assert.Equal(40, move.Lines);
            Assert.False(move.Remapped);
            Assert.Near(358884.0 * 40, plan.OpexDeltaPerMonth, 1.0, "40 lines at the base upkeep of a line");
        }

        [Test]
        public void TheUpkeep_CountsTheWaferMultiplierOfTheDestination()
        {
            CompanySnapshot t = Target();
            t.Lines.Clear();
            t.Lines.Add(RivalTestData.Wafer(RivalTestData.WaferOld, 100, 10, 1.0, 1.0));
            t.Lines.Add(RivalTestData.Wafer(RivalTestData.WaferNew, 150, 20, 1.5, 1.0));
            t.TechGap.Add(RivalTestData.Tech(RivalTestData.WaferNew, type: TechSnapshot.WaferType));   // the player gets the 150 mm wafer with the deal

            AcquisitionPlan plan = Plan(t);

            Assert.Near(358884.0 * (10 * 1.0 + 20 * 1.5), plan.OpexDeltaPerMonth, 1.0);
        }

        [Test]
        public void ANewWaferSize_UnlockedByTheDeal_KeepsItsLines()
        {
            CompanySnapshot t = Target();
            t.Lines.Clear();
            t.Lines.Add(RivalTestData.Wafer(RivalTestData.WaferNew, 150, 8, 1.2, 1.0));
            t.TechGap.Add(RivalTestData.Tech(RivalTestData.WaferNew, type: TechSnapshot.WaferType));

            AcquisitionPlan plan = Plan(t);

            Assert.Equal(RivalTestData.WaferNew, plan.LineMoves[0].ToWaferId, "AC-7.2: the 150 mm lines go on 150 mm");
            Assert.False(RivalTestData.HasNote(plan.Warnings, AcqKeys.WarnLinesRemapped));
        }

        [Test]
        public void AWaferSizeThePlayerCannotHave_MapsToTheLargestSmallerOne()
        {
            CompanySnapshot t = Target();
            t.Lines.Clear();
            t.Lines.Add(RivalTestData.Wafer(RivalTestData.WaferNew, 150, 8, 1.2, 1.0));   // not known and not in the technology gap

            AcquisitionPlan plan = Plan(t);

            LineMove move = plan.LineMoves[0];
            Assert.Equal(RivalTestData.WaferOld, move.ToWaferId, "100 mm is the largest the player knows below 150 mm");
            Assert.True(move.Remapped);
            Assert.Near(100.0, move.ToValue, 1e-9);
            Assert.True(RivalTestData.HasNote(plan.Warnings, AcqKeys.WarnLinesRemapped));
            Assert.Near(358884.0 * 8, plan.OpexDeltaPerMonth, 1.0, "the upkeep is that of the wafer the lines land on");
        }

        [Test]
        public void WhenAllKnownWafersAreLarger_TheSmallestOfThemTakesTheLines()
        {
            CompanySnapshot t = Target();
            t.Lines.Clear();
            t.Lines.Add(RivalTestData.Wafer(10, 50, 4, 1.0, 1.0));

            AcquisitionPlan plan = Plan(t);

            Assert.Equal(RivalTestData.WaferSmall, plan.LineMoves[0].ToWaferId, "75 mm is the smallest the player has");
        }

        [Test]
        public void APlayerWithoutWafers_BlocksTheLineMove()
        {
            CompanySnapshot t = Target();
            t.Lines.Clear();
            t.Lines.Add(RivalTestData.Wafer(10, 50, 4, 1.0, 1.0));
            PlayerSnapshot p = RivalTestData.Player();
            p.Wafers.Clear();

            AcquisitionPlan plan = Plan(t, p);

            Assert.False(plan.CanExecute);
            Assert.True(RivalTestData.Has(plan.BlockingErrors, AcqKeys.PlanNoWafer));
        }

        [Test]
        public void SellingTheLines_PaysTheSaleCostAndMovesNothing()
        {
            CompanySnapshot t = Target();
            t.Lines.Clear();
            t.Lines.Add(RivalTestData.Wafer(RivalTestData.WaferOld, 100, 30, 1.0, 100.0 * Million));
            t.Lines.Add(RivalTestData.Wafer(RivalTestData.WaferNew, 150, 10, 1.2, 40.0 * Million));
            t.Lines.Add(RivalTestData.Wafer(99, 200, 0, 1.0, 0.0));   // a wafer size without lines is no sale

            AcquisitionPlan plan = Plan(t, keep: false);

            Assert.True(plan.CanExecute, string.Join(",", plan.BlockingErrors));
            Assert.False(plan.KeepLines);
            Assert.Equal(0, plan.LineMoves.Count);
            Assert.Equal(2, plan.LineSales.Count);
            Assert.Equal(40, plan.LinesSold);
            Assert.Near(140.0 * Million, plan.SaleIncome, 1.0);
            Assert.Near(0.0, plan.OpexDeltaPerMonth, 1e-9, "no lines kept, no upkeep");
            Assert.False(plan.EnableFoundryServices);
            Assert.Equal(0, plan.RepointProviderContractIds.Count);
        }

        [Test]
        public void SellingTheLines_IsForbidden_WhenTheTargetMakesCpusOfThePlayer()
        {
            CompanySnapshot t = Target();
            t.PlayerCpuIdsMadeByTarget.Add(555);

            AcquisitionPlan sell = Plan(t, keep: false);
            Assert.True(sell.KeepLinesRequired);
            Assert.False(sell.CanExecute);
            Assert.True(RivalTestData.Has(sell.BlockingErrors, AcqKeys.PlanMustKeepLines));

            AcquisitionPlan keep = Plan(t, keep: true);
            Assert.True(keep.CanExecute, string.Join(",", keep.BlockingErrors));
            Assert.Equal(new List<int> { 555 }, keep.PlayerCpusToInHouse, "the player's CPU becomes the player's own production");
        }

        [Test]
        public void ATargetWithoutCpusOfThePlayer_DoesNotForceTheLines()
        {
            AcquisitionPlan plan = Plan(RivalTestData.ExampleTarget(), keep: false);

            Assert.False(plan.KeepLinesRequired);
            Assert.Equal(0, plan.PlayerCpusToInHouse.Count);
        }

        // ---- CPU families ----

        [Test]
        public void Families_PassTogether_TheBaseFirst_ARetiredBaseIncluded()
        {
            CompanySnapshot t = Target();
            t.Cpus.Add(Live(11, 10));                          // a variant, listed before its base
            CpuSnapshot oldBase = Live(10);
            oldBase.IsRetired = true;                          // the base is retired but its variant lives
            t.Cpus.Add(oldBase);
            t.Cpus.Add(Live(21, 20));
            t.Cpus.Add(Live(20));

            AcquisitionPlan plan = Plan(t);

            Assert.True(plan.CanExecute, string.Join(",", plan.BlockingErrors));
            Assert.Equal(2, plan.TransferFamilies.Count);
            Assert.Equal(10, plan.TransferFamilies[0].BaseSaveId);
            Assert.Equal(20, plan.TransferFamilies[1].BaseSaveId);
            Assert.Equal(new List<int> { 10, 11, 20, 21 }, plan.TransferredCpuIds(), "base first in every family");
            Assert.Equal(CpuFate.StaysRetired, Member(plan, 10).Fate);
            Assert.Equal(CpuFate.Live, Member(plan, 11).Fate);
            Assert.Equal(new List<int> { 10 }, plan.TransferredRetiredCpuIds(), "the retired base passes retired");
            Assert.Equal(0, plan.RetireCpuIds.Count, "nothing living is retired");
        }

        [Test]
        public void AFamilyOfRetiredCpus_StaysWithTheTarget()
        {
            CompanySnapshot t = Target();
            CpuSnapshot b = Live(40);
            b.IsRetired = true;
            CpuSnapshot v = Live(41, 40);
            v.IsRetired = true;
            t.Cpus.Add(b);
            t.Cpus.Add(v);
            t.Cpus.Add(Live(50));

            AcquisitionPlan plan = Plan(t);

            Assert.Equal(1, plan.TransferFamilies.Count);
            Assert.Equal(50, plan.TransferFamilies[0].BaseSaveId);
            Assert.Equal(0, plan.RetireCpuIds.Count);
        }

        [Test]
        public void ChildOfAMissingBase_BlocksThePlan()
        {
            CompanySnapshot t = Target();
            CpuSnapshot orphan = Live(11, 10);
            orphan.Name = "Orphan";
            t.Cpus.Add(orphan);

            AcquisitionPlan plan = Plan(t);

            Assert.False(plan.CanExecute);
            Assert.True(RivalTestData.Has(plan.BlockingErrors, AcqKeys.PlanFamilyBase));
            Assert.Equal("Orphan", plan.BlockingArgs[plan.BlockingErrors.IndexOf(AcqKeys.PlanFamilyBase)]);
        }

        [Test]
        public void SellingTheLines_RetiresTheCpusMadeInTheTargetsFactory()
        {
            CompanySnapshot t = Target();
            t.Cpus.Add(Live(1, inHouse: true));
            t.Cpus.Add(Live(2, 1, inHouse: true));
            t.Cpus.Add(Live(3, inHouse: false, makerId: 77));   // made by a foundry: it can go on

            AcquisitionPlan plan = Plan(t, keep: false);

            Assert.True(plan.CanExecute, string.Join(",", plan.BlockingErrors));
            Assert.Equal(new List<int> { 1, 2 }, plan.RetireCpuIds, "AC-8.2: A1 and A1-L are retired");
            Assert.Equal(1, plan.TransferFamilies.Count);
            Assert.Equal(3, plan.TransferFamilies[0].BaseSaveId);
            Assert.Equal(CpuFate.Live, Member(plan, 3).Fate);
        }

        [Test]
        public void SellingTheLines_ARetiredBaseOfALivingVariantStillPasses()
        {
            CompanySnapshot t = Target();
            t.Cpus.Add(Live(1, inHouse: true));                    // base made at the target: retired by the sale
            t.Cpus.Add(Live(2, 1, inHouse: false, makerId: 77));   // its variant is made by a foundry and lives

            AcquisitionPlan plan = Plan(t, keep: false);

            Assert.Equal(1, plan.TransferFamilies.Count);
            Assert.Equal(CpuFate.Retire, Member(plan, 1).Fate);
            Assert.Equal(CpuFate.Live, Member(plan, 2).Fate);
            Assert.Equal(new List<int> { 1 }, plan.RetireCpuIds);
            Assert.Equal(new List<int> { 1 }, plan.TransferredRetiredCpuIds());
            Assert.Equal(1, plan.TransferFamilies[0].Members[0].SaveId, "the base is still first");
        }

        [Test]
        public void KeepingTheLines_KeepsTheInHouseCpusAlive()
        {
            CompanySnapshot t = Target();
            t.Cpus.Add(Live(1, inHouse: true));

            AcquisitionPlan plan = Plan(t, keep: true);

            Assert.Equal(CpuFate.Live, Member(plan, 1).Fate);
            Assert.Equal(0, plan.RetireCpuIds.Count);
        }

        [Test]
        public void APlayerWithoutACpuDivision_GetsNoCpus_TheyAreRetired()
        {
            CompanySnapshot t = Target();
            t.Cpus.Add(Live(1));
            t.Cpus.Add(Live(2, 1));
            CpuSnapshot old = Live(3);
            old.IsRetired = true;
            t.Cpus.Add(old);
            PlayerSnapshot p = RivalTestData.Player();
            p.IsCpuCompany = false;
            p.IsFoundry = true;

            AcquisitionPlan plan = Plan(t, p);

            Assert.True(plan.CanExecute, string.Join(",", plan.BlockingErrors));
            Assert.Equal(0, plan.TransferFamilies.Count);
            Assert.Equal(new List<int> { 1, 2 }, plan.RetireCpuIds, "FR-8 item 4: the CPUs go with the target; the retired one is not touched again");
            Assert.True(RivalTestData.HasNote(plan.Warnings, AcqKeys.WarnNoCpuDivision));
            Assert.Equal(40, plan.LinesMoved, "the lines still come");
        }

        [Test]
        public void ACpuMadeByThePlayer_BecomesOwnProduction()
        {
            CompanySnapshot t = Target();
            CpuSnapshot cpu = Live(1, inHouse: false, makerId: 1);
            cpu.MadeByPlayer = true;
            t.Cpus.Add(cpu);
            t.Cpus.Add(Live(2));

            AcquisitionPlan plan = Plan(t);

            Assert.True(Member(plan, 1).ToInHouse);
            Assert.False(Member(plan, 2).ToInHouse);
            Assert.False(RivalTestData.HasNote(plan.Warnings, AcqKeys.WarnNoContract), "the player is the maker: no contract is missing");
        }

        // ---- the assets of a bankrupt company ----

        [Test]
        public void BankruptAssets_BringBackTheCpusOfTheSnapshot()
        {
            CompanySnapshot t = Target();
            t.Status = CompanyStatus.BankruptAuction;
            CpuSnapshot a = Live(1);
            a.IsRetired = true;
            a.ActiveAtBankruptcy = true;
            CpuSnapshot b = Live(2, 1);
            b.IsRetired = true;
            b.ActiveAtBankruptcy = true;
            CpuSnapshot old = Live(3);
            old.IsRetired = true;   // not in the snapshot of the auction: retired long before
            t.Cpus.Add(a);
            t.Cpus.Add(b);
            t.Cpus.Add(old);
            var s = new AcquisitionsState();
            s.auctions.Add(RivalTestData.Auction(t, "1986-01-01"));

            AcquisitionPlan plan = Plan(t, type: DealType.BankruptAssets, s: s);

            Assert.True(plan.CanExecute, string.Join(",", plan.BlockingErrors));
            Assert.Equal(1, plan.TransferFamilies.Count, "the family of the old CPU has nobody to bring back");
            Assert.Equal(CpuFate.Revive, Member(plan, 1).Fate);
            Assert.Equal(CpuFate.Revive, Member(plan, 2).Fate);
            Assert.Equal(0, plan.RetireCpuIds.Count, "they are retired already");
            Assert.Equal(0, plan.TransferredRetiredCpuIds().Count);
        }

        [Test]
        public void BankruptAssets_SoldLines_DoNotBringBackInHouseCpus()
        {
            CompanySnapshot t = Target();
            t.Status = CompanyStatus.BankruptAuction;
            CpuSnapshot a = Live(1);
            a.IsRetired = true;
            a.ActiveAtBankruptcy = true;
            t.Cpus.Add(a);
            var s = new AcquisitionsState();
            s.auctions.Add(RivalTestData.Auction(t, "1986-01-01"));

            AcquisitionPlan plan = Plan(t, keep: false, type: DealType.BankruptAssets, s: s);

            Assert.Equal(0, plan.TransferFamilies.Count);
            Assert.Equal(0, plan.RetireCpuIds.Count, "nothing to retire, it is retired already");
        }

        // ---- contracts ----

        [Test]
        public void ProviderContracts_GoWithTheLines()
        {
            CompanySnapshot t = Target();
            t.IsFoundry = true;
            t.ProviderContracts.Add(RivalTestData.Contract(12, t.SaveId, 600));
            t.ProviderContracts.Add(RivalTestData.Contract(11, t.SaveId, 601));

            AcquisitionPlan keep = Plan(t, keep: true);
            Assert.Equal(new List<int> { 11, 12 }, keep.RepointProviderContractIds, "in the order of the snapshot's ids");
            Assert.Equal(0, keep.DroppedProviderContractIds.Count);

            AcquisitionPlan sell = Plan(t, keep: false);
            Assert.Equal(0, sell.RepointProviderContractIds.Count);
            Assert.Equal(new List<int> { 11, 12 }, sell.DroppedProviderContractIds, "AC: with sold lines the contracts end");
        }

        [Test]
        public void AContractBetweenThePlayerAndTheTarget_IsNotTakenOver()
        {
            CompanySnapshot t = Target();
            PlayerSnapshot p = RivalTestData.Player();
            t.ProviderContracts.Add(RivalTestData.Contract(1, t.SaveId, p.SaveId));
            t.ClientContracts.Add(RivalTestData.Contract(2, p.SaveId, t.SaveId));

            AcquisitionPlan plan = Plan(t, p);

            Assert.Equal(0, plan.RepointProviderContractIds.Count + plan.DroppedProviderContractIds.Count);
            Assert.Equal(0, plan.RepointClientContractIds.Count + plan.DroppedClientContractIds.Count, "the game ends them without a fine (FR-9 item 5)");
        }

        [Test]
        public void AClientContract_FollowsTheCpusMadeAtThatProvider()
        {
            CompanySnapshot t = Target();
            t.Cpus.Add(Live(1, inHouse: false, makerId: 700));
            t.ClientContracts.Add(RivalTestData.Contract(5, 700, t.SaveId, exclusive: true));

            AcquisitionPlan plan = Plan(t);

            Assert.Equal(new List<int> { 5 }, plan.RepointClientContractIds, "AC-9.3: the exclusive contract passes to the player");
            Assert.Equal(0, plan.DroppedClientContractIds.Count);
            Assert.False(RivalTestData.HasNote(plan.Warnings, AcqKeys.WarnNoContract));
        }

        [Test]
        public void AClientContract_WithoutACpuMadeThere_IsDropped()
        {
            CompanySnapshot t = Target();
            t.Cpus.Add(Live(1, inHouse: true));
            t.ClientContracts.Add(RivalTestData.Contract(5, 700, t.SaveId));

            AcquisitionPlan plan = Plan(t);

            Assert.Equal(0, plan.RepointClientContractIds.Count);
            Assert.Equal(new List<int> { 5 }, plan.DroppedClientContractIds);
        }

        [Test]
        public void ARetiredCpuAtTheProvider_DoesNotKeepTheContractAlive()
        {
            CompanySnapshot t = Target();
            t.Cpus.Add(Live(1, inHouse: false, makerId: 700));
            t.ClientContracts.Add(RivalTestData.Contract(5, 700, t.SaveId));
            t.Cpus[0].IsRetired = true;
            t.Cpus.Add(Live(2));   // so the plan has a living CPU elsewhere

            AcquisitionPlan plan = Plan(t);

            Assert.Equal(new List<int> { 5 }, plan.DroppedClientContractIds);
        }

        [Test]
        public void AnExclusiveClientContract_IsDropped_WhenThePlayerHoldsOneAlready()
        {
            CompanySnapshot t = Target();
            t.Cpus.Add(Live(1, inHouse: false, makerId: 700));
            t.ClientContracts.Add(RivalTestData.Contract(5, 700, t.SaveId, exclusive: true));
            PlayerSnapshot p = RivalTestData.Player();
            p.HasExclusiveClientContract = true;

            AcquisitionPlan plan = Plan(t, p);

            Assert.Equal(0, plan.RepointClientContractIds.Count, "AC-9.4");
            Assert.Equal(new List<int> { 5 }, plan.DroppedClientContractIds);
            Assert.True(RivalTestData.HasNote(plan.Warnings, AcqKeys.WarnNoContract), "the CPU is made without a contract now: the player is told");
        }

        [Test]
        public void OnlyOneExclusiveContract_PassesWhenTheTargetHasTwo()
        {
            CompanySnapshot t = Target();
            t.Cpus.Add(Live(1, inHouse: false, makerId: 700));
            t.Cpus.Add(Live(2, inHouse: false, makerId: 701));
            t.ClientContracts.Add(RivalTestData.Contract(8, 701, t.SaveId, exclusive: true));
            t.ClientContracts.Add(RivalTestData.Contract(7, 700, t.SaveId, exclusive: true));
            t.ClientContracts.Add(RivalTestData.Contract(9, 702, t.SaveId, exclusive: false));
            t.Cpus.Add(Live(3, inHouse: false, makerId: 702));

            AcquisitionPlan plan = Plan(t);

            Assert.Equal(new List<int> { 7, 9 }, plan.RepointClientContractIds, "the first exclusive one by id passes; a plain one always");
            Assert.Equal(new List<int> { 8 }, plan.DroppedClientContractIds);
        }

        [Test]
        public void ClientContracts_DoNotPass_ToAPlayerWithoutACpuDivision()
        {
            CompanySnapshot t = Target();
            t.Cpus.Add(Live(1, inHouse: false, makerId: 700));
            t.ClientContracts.Add(RivalTestData.Contract(5, 700, t.SaveId));
            PlayerSnapshot p = RivalTestData.Player();
            p.IsCpuCompany = false;

            AcquisitionPlan plan = Plan(t, p);

            Assert.Equal(new List<int> { 5 }, plan.DroppedClientContractIds);
            Assert.Equal(new List<int> { 1 }, plan.RetireCpuIds);
        }

        [Test]
        public void ClientContracts_PassEvenWhenTheLinesAreSold()
        {
            CompanySnapshot t = Target();
            t.Cpus.Add(Live(1, inHouse: false, makerId: 700));
            t.ClientContracts.Add(RivalTestData.Contract(5, 700, t.SaveId));

            AcquisitionPlan plan = Plan(t, keep: false);

            Assert.Equal(new List<int> { 5 }, plan.RepointClientContractIds, "design step 7: independent of KeepLines");
        }

        [Test]
        public void ACpuWhoseContractIsDropped_IsMadeAtTheBaseMarkup_AndTheWarningNamesTheFoundry()
        {
            CompanySnapshot t = Target();
            CpuSnapshot cpu = Live(1, inHouse: false, makerId: 700);
            cpu.Name = "Chip";
            t.Cpus.Add(cpu);

            AcquisitionPlan plan = Plan(t);   // no contract in the snapshot at all

            PlanNote warning = plan.Warnings.First(w => w.Key == AcqKeys.WarnNoContract);
            Assert.Equal("Chip", warning.Args[0]);
            Assert.Equal("Foundry700", warning.Args[1]);
            Assert.False(warning.RequiresAck);
        }

        // ---- the foundry flag ----

        [Test]
        public void TheFoundryFlag_IsEnabledOnlyWhenTheLinesAreKept_AndTheTargetHadIt()
        {
            CompanySnapshot t = Target();
            t.HadFoundryServices = true;

            Assert.True(Plan(t, keep: true).EnableFoundryServices);
            Assert.False(Plan(t, keep: false).EnableFoundryServices, "no lines, no foundry");

            t.HadFoundryServices = false;
            Assert.False(Plan(t, keep: true).EnableFoundryServices);

            t.HadFoundryServices = true;
            PlayerSnapshot p = RivalTestData.Player();
            p.FoundryServicesEnabled = true;
            Assert.False(Plan(t, p, keep: true).EnableFoundryServices, "already on");
        }

        // ---- consequences ----

        [Test]
        public void Intel_RevokesTheX86License_AndNeedsTheTick()
        {
            CompanySnapshot t = Target();
            t.UniqueId = AcquisitionPlanBuilder.IntelId;
            t.HasOtherFoundry = true;   // so the foundry warning is not part of this test

            AcquisitionPlan unticked = Plan(t, ack: false);
            Assert.False(unticked.CanExecute, "AC-12.1: the button waits for the tick");
            Assert.True(RivalTestData.Has(unticked.BlockingErrors, AcqKeys.PlanAckRequired));
            Assert.True(RivalTestData.HasNote(unticked.Consequences, AcqKeys.ConsequenceX86));

            AcquisitionPlan ticked = Plan(t, ack: true);
            Assert.True(ticked.CanExecute, string.Join(",", ticked.BlockingErrors));
        }

        [Test]
        public void Amd_RevokesTheX86License_AndBlocksTheSpinOffBeforeTheDate()
        {
            CompanySnapshot t = Target();
            t.UniqueId = AcquisitionPlanBuilder.AmdId;

            AcquisitionPlan plan = Plan(t);   // 1985, no GlobalFoundries yet
            Assert.True(RivalTestData.HasNote(plan.Consequences, AcqKeys.ConsequenceX86));
            Assert.True(RivalTestData.HasNote(plan.Consequences, AcqKeys.ConsequenceGlobalFoundries));

            PlayerSnapshot spawned = RivalTestData.Player();
            spawned.GlobalFoundriesSpawned = true;
            Assert.False(RivalTestData.HasNote(Plan(t, spawned).Consequences, AcqKeys.ConsequenceGlobalFoundries), "already spun off");

            PlayerSnapshot late = RivalTestData.Player();
            late.Today = new System.DateTime(2009, 3, 2);
            Assert.False(RivalTestData.HasNote(Plan(t, late).Consequences, AcqKeys.ConsequenceGlobalFoundries), "from 2009-03-02 the date no longer holds it back");

            PlayerSnapshot early = RivalTestData.Player();
            early.Today = new System.DateTime(2009, 3, 1);
            Assert.True(RivalTestData.HasNote(Plan(t, early).Consequences, AcqKeys.ConsequenceGlobalFoundries), "the day before");
        }

        [Test]
        public void Intel_WithNoFoundriesInTheGame_WillNotOpenFoundryServices()
        {
            CompanySnapshot t = Target();
            t.UniqueId = AcquisitionPlanBuilder.IntelId;

            Assert.True(RivalTestData.HasNote(Plan(t).Consequences, AcqKeys.ConsequenceIntelFoundry));

            t.HasOtherFoundry = true;
            Assert.False(RivalTestData.HasNote(Plan(t).Consequences, AcqKeys.ConsequenceIntelFoundry), "another foundry exists");

            t.HasOtherFoundry = false;
            t.HadFoundryServices = true;
            Assert.False(RivalTestData.HasNote(Plan(t).Consequences, AcqKeys.ConsequenceIntelFoundry), "Intel runs foundry services already");
        }

        [Test]
        public void GlobalFoundries_ShowsTheContractWithAmd_WhenTheLinesAreKept()
        {
            CompanySnapshot t = Target();
            t.UniqueId = AcquisitionPlanBuilder.GlobalFoundriesId;
            t.ProviderContracts.Add(RivalTestData.Contract(1, t.SaveId, 601, exclusive: true, clientName: "AMD"));

            AcquisitionPlan keep = Plan(t, ack: false, keep: true);
            PlanNote note = keep.Consequences.First(c => c.Key == AcqKeys.ConsequenceGfAmdContract);
            Assert.Equal("AMD", note.Args[0]);
            Assert.False(note.RequiresAck, "information only");
            Assert.True(keep.CanExecute, "no tick needed for information: " + string.Join(",", keep.BlockingErrors));

            Assert.False(RivalTestData.HasNote(Plan(t, keep: false).Consequences, AcqKeys.ConsequenceGfAmdContract), "with sold lines the contract ends");
        }

        [Test]
        public void ARegularRival_HasNoHistoricalConsequences()
        {
            AcquisitionPlan plan = Plan(RivalTestData.ExampleTarget(), ack: false);

            Assert.Equal(0, plan.Consequences.Count);
            Assert.True(plan.CanExecute, "no consequences, no tick needed");
        }

        [Test]
        public void AFablessPlayerWithLines_BecomesAnIdm_AndNeedsTheTick()
        {
            PlayerSnapshot p = RivalTestData.Player();
            p.IsFabless = true;

            AcquisitionPlan plan = Plan(RivalTestData.ExampleTarget(), p, ack: false);
            Assert.True(RivalTestData.HasNote(plan.Consequences, AcqKeys.ConsequenceBecomesIdm), "AC-7.3");
            Assert.False(plan.CanExecute);

            Assert.False(RivalTestData.HasNote(Plan(RivalTestData.ExampleTarget(), p, keep: false, ack: false).Consequences, AcqKeys.ConsequenceBecomesIdm),
                "selling the lines leaves the player fabless");

            CompanySnapshot noLines = Target();
            noLines.Lines.Clear();
            Assert.False(RivalTestData.HasNote(Plan(noLines, p, ack: false).Consequences, AcqKeys.ConsequenceBecomesIdm), "no lines move");
        }

        // ---- what is not a plan ----

        [Test]
        public void AQuoteThatIsNotAvailable_BlocksThePlan_WithItsReason()
        {
            PlayerSnapshot p = RivalTestData.Player();
            p.InDefault = true;

            AcquisitionPlan plan = Plan(RivalTestData.ExampleTarget(), p);

            Assert.False(plan.CanExecute);
            Assert.Equal(AcqKeys.ReasonPlayerDefault, plan.BlockingErrors[0]);
            Assert.Equal(40, plan.LinesMoved, "the rest is still planned, so the preview can show what it would be");
        }

        [Test]
        public void ADeal_ThatIsNotAPurchaseOfACompany_HasNoPlan()
        {
            CompanySnapshot t = RivalTestData.ExampleTarget();
            DealQuote q = RivalTestData.Quote(DealType.Stake, t, RivalTestData.Player());

            AcquisitionPlan plan = AcquisitionPlanBuilder.Build(q, t, RivalTestData.Player(), new PlanOptions());

            Assert.False(plan.CanExecute);
            Assert.True(RivalTestData.Has(plan.BlockingErrors, AcqKeys.PlanNotPurchase));
            Assert.Equal(0, plan.LineMoves.Count);
        }

        [Test]
        public void NoSnapshots_OrNoOptions_DoNotThrow()
        {
            Assert.False(AcquisitionPlanBuilder.Build(new DealQuote { Type = DealType.Hostile, Available = true }, null, null, null).CanExecute);

            CompanySnapshot t = RivalTestData.ExampleTarget();
            PlayerSnapshot p = RivalTestData.Player();
            AcquisitionPlan plan = AcquisitionPlanBuilder.Build(RivalTestData.Quote(DealType.Hostile, t, p), t, p, null);
            Assert.True(plan.KeepLines, "the default option keeps the lines");
        }

        [Test]
        public void ThePlan_IsTheSameEveryTime()
        {
            CompanySnapshot t = RivalTestData.ExampleTarget();
            t.Cpus.Reverse();
            t.TechGap.Reverse();

            AcquisitionPlan a = Plan(RivalTestData.ExampleTarget());
            AcquisitionPlan b = Plan(t);

            Assert.Equal(a.TechUnlockIds, b.TechUnlockIds, "the order does not depend on the order of the snapshot");
            Assert.Equal(a.TransferredCpuIds(), b.TransferredCpuIds());
        }

        [Test]
        public void ThePlanOfTheDealRecord_CountsWhatWasMoved()
        {
            CompanySnapshot t = Target();
            t.Cpus.Add(Live(1));
            t.Cpus.Add(Live(2, 1));
            t.ProviderContracts.Add(RivalTestData.Contract(1, t.SaveId, 600));
            t.Cpus.Add(Live(3, inHouse: false, makerId: 700));
            t.ClientContracts.Add(RivalTestData.Contract(2, 700, t.SaveId));

            AcquisitionPlan plan = Plan(t);

            Assert.Equal(new List<int> { 1, 2, 3 }, plan.TransferredCpuIds());
            Assert.Equal(1, plan.RepointProviderContractIds.Count);
            Assert.Equal(1, plan.RepointClientContractIds.Count);
            Assert.Equal(3, plan.TechUnlockIds.Count);
        }
    }
}
