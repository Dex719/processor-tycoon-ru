using System;
using System.Collections.Generic;
using System.Linq;
using PTMod.Features.RivalAcquisitions;
using PTMod.Tests.Architecture;

namespace PTMod.Tests.Features.RivalAcquisitions
{
    /// <summary>
    /// The edge cases 1 to 18 of requirements.md (TSK-020; NFR-2, NFR-3, FR-14), one test (or two) for each, as far as the case is pure logic: the plan, the rules of the
    /// prices, the arithmetic of the stakes, the order of the steps. What needs the game (the real windows, the mails, the running bankruptcy of the game) is read from the
    /// source of the game side or left to the owner (M1 to M9 of design.md, TSK-022); a case like that says so in its comment. The numbers are those of the 1985
    /// example of design.md.
    /// </summary>
    [Covers(typeof(DealExecution), typeof(DealRules), typeof(AcquisitionPlanBuilder), typeof(StakeMath), typeof(StakeBook), typeof(DistressRules))]
    public class EdgeCaseTests
    {
        private const double Million = 1000000.0;

        // ---- a fake game that only keeps the order and the events ----

        private sealed class RecordingWorld : IDealWorld
        {
            public readonly List<string> Calls = new List<string>();
            public readonly List<KeyValuePair<int, double>> StakeEvents = new List<KeyValuePair<int, double>>();
            public string RecheckResult;

            public string Recheck(DealRequest request)
            {
                Calls.Add("Recheck");
                return RecheckResult;
            }

            public void Begin(DealRequest request) { Calls.Add("Begin"); }
            public void UnlockTechnologies(AcquisitionPlan plan, DealLog log) { Calls.Add("Technologies"); }
            public void RaiseNodeExperience(AcquisitionPlan plan, DealLog log) { Calls.Add("NodeExperience"); }
            public void DetachCpus(AcquisitionPlan plan, DealLog log) { Calls.Add("DetachCpus"); }
            public void MoveLines(AcquisitionPlan plan, DealLog log) { Calls.Add("Lines"); }
            public void MoveContracts(AcquisitionPlan plan, DealLog log) { Calls.Add("Contracts"); }
            public void AttachCpus(AcquisitionPlan plan, DealLog log) { Calls.Add("AttachCpus"); }
            public void TransferRelatedState(DealRequest request, DealLog log) { Calls.Add("RelatedFeatures"); }
            public void TakeOutTarget(DealLog log) { Calls.Add("TakeOut"); }
            public void PayOut(double amount, string op, string details) { Calls.Add("PayOut " + op); }
            public void PayIn(double amount, string op, string details) { Calls.Add("PayIn " + op); }
            public void AddReputation(int delta, string reasonKey) { Calls.Add("Reputation"); }

            public void RaiseStakeChanged(int uniqueId, double share)
            {
                Calls.Add("StakeChanged");
                StakeEvents.Add(new KeyValuePair<int, double>(uniqueId, share));
            }

            public void RaiseDealCompleted(DealRecord record) { Calls.Add("DealCompleted"); }
            public void SendDealMail(DealRecord record) { Calls.Add("Mail"); }
            public void End() { Calls.Add("End"); }
        }

        private static readonly Func<string, Action, bool> Guard = (site, body) =>
        {
            try
            {
                body();
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        };

        // ---- builders ----

        private static AcquisitionPlan Plan(CompanySnapshot t, PlayerSnapshot p = null, bool keep = true, DealType type = DealType.Hostile, AcquisitionsState s = null)
        {
            p = p ?? RivalTestData.Player();
            DealQuote q = RivalTestData.Quote(type, t, p, s);
            return AcquisitionPlanBuilder.Build(q, t, p, new PlanOptions { KeepLines = keep, ConsequencesAcknowledged = true });
        }

        private static DealRequest Request(DealType type, CompanySnapshot t, AcquisitionsState s, bool keep = true)
        {
            PlayerSnapshot p = RivalTestData.Player();
            DealQuote quote = RivalTestData.Quote(type, t, p, s);
            AcquisitionPlan plan = AcquisitionPlanBuilder.Build(quote, t, p, new PlanOptions { KeepLines = keep, ConsequencesAcknowledged = true });
            return new DealRequest
            {
                Type = type,
                Quote = quote,
                Plan = plan,
                Target = t,
                Player = p,
                ExpectedUniqueId = t.UniqueId,
                ExpectedSaveId = t.SaveId,
                ExpectedStatus = t.Status,
                ShownPrice = quote.Price,
                Today = RivalTestData.Today,
            };
        }

        private static CompanySnapshot TargetWithoutCpus()
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

        // ---- 1. the target went bankrupt while the window was open ----

        [Test]
        public void Case01_ATargetThatChangedItsStatus_CancelsTheDealBeforeTheFirstChange()
        {
            var s = new AcquisitionsState();
            CompanySnapshot healthy = RivalTestData.ExampleTarget();
            DealRequest request = Request(DealType.Hostile, healthy, s);
            request.ExpectedStatus = CompanyStatus.InDefault;   // what the window showed; the target is healthy now (or the other way round)

            string arg;
            Assert.Equal(AcqKeys.ReasonStatusChanged, DealExecution.Validate(request, out arg));

            var world = new RecordingWorld();
            DealOutcome outcome = DealExecution.Run(world, request, s, Guard);
            Assert.False(outcome.Executed);
            Assert.Equal(AcqKeys.ReasonStatusChanged, outcome.CancelReasonKey);
            Assert.Equal(0, world.Calls.Count, "nothing at all was done");
            Assert.Equal(0, s.deals.Count);

            // and when the game's own recheck finds the target bankrupt after the window was drawn
            var world2 = new RecordingWorld { RecheckResult = AcqKeys.ReasonStatusChanged };
            DealOutcome late = DealExecution.Run(world2, Request(DealType.Hostile, healthy, s), s, Guard);
            Assert.False(late.Executed);
            Assert.Equal("Recheck", string.Join(" | ", world2.Calls.ToArray()), "not even Begin");
        }

        [Test]
        public void Case01_ATargetThatIsAnotherCompanyNow_IsRefused()
        {
            var s = new AcquisitionsState();
            DealRequest wrongSave = Request(DealType.Hostile, RivalTestData.ExampleTarget(), s);
            wrongSave.ExpectedSaveId = 413;
            DealRequest wrongId = Request(DealType.Hostile, RivalTestData.ExampleTarget(), s);
            wrongId.ExpectedUniqueId = 4;
            string arg;

            Assert.Equal(AcqKeys.ReasonTargetChanged, DealExecution.Validate(wrongSave, out arg), "NFR-2: not the SaveID of the record");
            Assert.Equal(AcqKeys.ReasonTargetChanged, DealExecution.Validate(wrongId, out arg), "NFR-2: not the UniqueID of the record");
        }

        // ---- 2. the player does not know the wafer size of the target ----

        [Test]
        public void Case02_LinesOnAWaferSizeThePlayerDoesNotKnow_GoToTheLargestSmallerOne_AndTheRecordSaysWhere()
        {
            CompanySnapshot t = TargetWithoutCpus();
            t.Lines.Clear();
            t.Lines.Add(RivalTestData.Wafer(RivalTestData.WaferNew, 150, 8, 1.2, 50.0 * Million));
            PlayerSnapshot p = RivalTestData.Player();
            DealQuote q = RivalTestData.Quote(DealType.Hostile, t, p, new AcquisitionsState());
            AcquisitionPlan plan = AcquisitionPlanBuilder.Build(q, t, p, new PlanOptions());

            Assert.Equal(RivalTestData.WaferOld, plan.LineMoves[0].ToWaferId, "the largest the player knows that is not above 150 mm: 100 mm");
            Assert.True(plan.LineMoves[0].Remapped);
            Assert.True(RivalTestData.HasNote(plan.Warnings, AcqKeys.WarnLinesRemapped), "the preview says so");

            DealRecord record = DealExecution.Draft(new DealRequest { Type = DealType.Hostile, Quote = q, Plan = plan, Target = t, Player = p, Today = RivalTestData.Today });
            Assert.Equal(1, record.lines.Count);
            Assert.Equal(RivalTestData.WaferOld, record.lines[0].waferId, "the journal names the wafer size the lines landed on");
            Assert.Equal(8, record.lines[0].lines);
        }

        // ---- 3. the only foundry that fabless AIs lean on ----

        [Test]
        public void Case03_ASoleFoundry_KeptLinesMakeThePlayerTheirSupplier_SoldLinesEndTheContracts()
        {
            CompanySnapshot t = TargetWithoutCpus();
            t.IsFoundry = true;
            t.ProviderContracts.Add(RivalTestData.Contract(1, t.SaveId, 600, providerName: "Foundry"));
            t.ProviderContracts.Add(RivalTestData.Contract(2, t.SaveId, 601, providerName: "Foundry"));
            var s = new AcquisitionsState();

            DealRequest keep = Request(DealType.Hostile, t, s, keep: true);
            Assert.Equal(new List<int> { 1, 2 }, keep.Plan.RepointProviderContractIds, "FR-9: the player becomes the supplier");
            Assert.Equal(2, DealExecution.Draft(keep).providerContracts);

            DealRequest sell = Request(DealType.Hostile, t, s, keep: false);
            Assert.Equal(0, sell.Plan.RepointProviderContractIds.Count);
            Assert.Equal(new List<int> { 1, 2 }, sell.Plan.DroppedProviderContractIds, "the clients get the game's handling of a bankrupt supplier");
            Assert.Equal(0, DealExecution.Draft(sell).providerContracts);
        }

        // ---- 4. the player is fabless and the target makes the player's CPUs ----

        [Test]
        public void Case04_WhenTheTargetMakesThePlayersCpu_SellingTheLinesIsClosed()
        {
            CompanySnapshot t = TargetWithoutCpus();
            t.PlayerCpuIdsMadeByTarget.Add(555);
            PlayerSnapshot p = RivalTestData.Player();
            p.IsFabless = true;

            AcquisitionPlan sell = Plan(t, p, keep: false);
            AcquisitionPlan keep = Plan(t, p, keep: true);

            Assert.True(sell.KeepLinesRequired);
            Assert.False(sell.CanExecute);
            Assert.True(sell.BlockingErrors.Contains(AcqKeys.PlanMustKeepLines));
            Assert.True(keep.CanExecute, string.Join(",", keep.BlockingErrors.ToArray()));
            Assert.Equal(new List<int> { 555 }, keep.PlayerCpusToInHouse);

            // and DealExecution refuses to run the closed variant
            string arg;
            DealRequest closed = Request(DealType.Hostile, t, new AcquisitionsState(), keep: false);
            Assert.Equal(AcqKeys.PlanMustKeepLines, DealExecution.Validate(closed, out arg));
        }

        // ---- 5. the player is a foundry ----

        [Test]
        public void Case05_AFoundryPlayer_GetsNoCpusAndNoClientContracts_ButLinesAndTechnologies()
        {
            CompanySnapshot t = RivalTestData.ExampleTarget();
            t.Cpus.Add(Live(1301, inHouse: false, makerId: 700));
            t.ClientContracts.Add(RivalTestData.Contract(5, 700, t.SaveId));
            PlayerSnapshot p = RivalTestData.Player();
            p.IsCpuCompany = false;
            p.IsFoundry = true;

            AcquisitionPlan plan = Plan(t, p);

            Assert.True(plan.CanExecute, string.Join(",", plan.BlockingErrors.ToArray()));
            Assert.Equal(0, plan.TransferFamilies.Count, "FR-8 item 4: no CPU passes");
            Assert.Equal(0, plan.RepointClientContractIds.Count, "the contracts of the client do not pass");
            Assert.Equal(new List<int> { 5 }, plan.DroppedClientContractIds);
            Assert.Equal(40, plan.LinesMoved, "the lines pass");
            Assert.Equal(3, plan.TechUnlockIds.Count, "and the technologies");
        }

        // ---- 6. a stake in a company that is taken over later ----

        [Test]
        public void Case06_TheStakeLowersThePrice_AndIsClosedByTheDeal()
        {
            CompanySnapshot t = RivalTestData.ExampleTarget();
            var empty = new AcquisitionsState();
            var withStake = new AcquisitionsState();
            withStake.stakes.Add(RivalTestData.Stake(t, 0.25));
            withStake.rescueOrders.Add(new RescueOrderData { targetUniqueId = t.UniqueId, targetSaveId = t.SaveId, maxAmount = 1e8, active = true });
            PlayerSnapshot p = RivalTestData.Player();

            DealQuote plain = RivalTestData.Quote(DealType.Hostile, t, p, empty);
            DealQuote held = RivalTestData.Quote(DealType.Hostile, t, p, withStake);

            Assert.Near(0.25, held.OwnedShare, 1e-12);
            Assert.Near(0.75 * plain.Price, held.Price, 1000.0, "the price is lower by the share (1 - sigma_p), up to the rounding step");
            Assert.True(held.NetCash < 0.0, "the invariant holds with a stake");

            var world = new RecordingWorld();
            DealRequest request = Request(DealType.Hostile, t, withStake);
            DealOutcome outcome = DealExecution.Run(world, request, withStake, Guard);

            Assert.True(outcome.Executed);
            Assert.Equal(0, withStake.stakes.Count, "the stake is closed with the deal");
            Assert.Equal(0, withStake.rescueOrders.Count, "and so is the order (Case 18)");
            Assert.Equal(1, world.StakeEvents.Count);
            Assert.Equal(t.UniqueId, world.StakeEvents[0].Key);
            Assert.Near(0.0, world.StakeEvents[0].Value, 1e-12, "StakeChanged tells the share is 0 now");
        }

        // ---- 7. a company with a stake went bankrupt ----

        [Test]
        public void Case07_TheStakeIsWrittenOff_AndAnOrdinaryAuctionOpens()
        {
            var s = new AcquisitionsState();
            CompanySnapshot t = RivalTestData.ExampleTarget();
            s.stakes.Add(RivalTestData.Stake(t, 0.25));
            s.rescueOrders.Add(new RescueOrderData { targetUniqueId = t.UniqueId, targetSaveId = t.SaveId, maxAmount = 1e8, active = true });

            Assert.False(DistressRules.IgnoresBankruptcy(false, false, false, false, false), "a rival's own bankruptcy is the feature's business");
            double had = StakeBook.RemoveStake(s, t.UniqueId);
            StakeBook.RemoveOrder(s, t.UniqueId);
            DistressRules.OpenAuction(s, t.UniqueId, t.SaveId, RivalTestData.Today, 365, new[] { 1201, 1207 });

            Assert.Near(0.25, had, 1e-12, "the share that was lost is known, for the mail");
            Assert.Near(0.0, s.ShareOf(t.UniqueId), 1e-12);
            Assert.Equal(0, s.rescueOrders.Count);
            Assert.NotNull(s.OpenAuctionOf(t.UniqueId), "the auction of the assets is open");
            Assert.Equal("1986-03-01", s.OpenAuctionOf(t.UniqueId).until, "365 days");
            Assert.Equal(CompanyStatus.BankruptAuction, CompanyStatusRules.Resolve(true, false, false, s.OpenAuctionOf(t.UniqueId), t.SaveId, RivalTestData.Today));
        }

        // ---- 8. a rescue order and the player cannot pay ----

        [Test]
        public void Case08_ARescueTheTheyCannotPay_DoesNothing_AndTheGameGoesOnAsWithoutTheMod()
        {
            Valuation v = new Valuation { E = 600.0 * Million, EMin = 27.75 * Million };
            StakeConfig config = RivalTestData.Balance().Stake;

            Assert.True(StakeBook.ShouldTryRescue(true, -111.0 * Million, true, -150.0 * Million), "the order applies");
            RescueDecision d = StakeMath.Rescue(-150.0 * Million, 111.0 * Million, 0.0, v, 50.0 * Million, 100.0 * Million, config);

            Assert.False(d.Ok, "nothing is injected");
            Assert.Equal(AcqKeys.RescueNoCash, d.ReasonKey, "the reason of the mail 'the rescue failed'");
            Assert.Equal("44500000", d.ReasonArg, "short by 94.5 - 50 million");

            // the order itself stays, the stake does not change
            var s = new AcquisitionsState();
            StakeBook.SetOrder(s, 10, 388, 100.0 * Million, true);
            Assert.True(StakeBook.Order(s, 10, 388).active);
            Assert.Near(0.0, s.ShareOf(10), 1e-12);
        }

        // ---- 9. bankruptcies during the fast-forward of a new game ----

        [Test]
        public void Case09_BankruptciesOfAFastForwardOrALoad_OpenNoAuctionAndSendNoMail()
        {
            Assert.True(DistressRules.IgnoresBankruptcy(false, false, false, false, true), "the new-game fast-forward");
            Assert.True(DistressRules.IgnoresBankruptcy(false, false, false, true, false), "a save being loaded");
            Assert.True(DistressRules.IgnoresBankruptcy(true, false, false, false, false), "the player's own");
            Assert.True(DistressRules.IgnoresBankruptcy(false, true, false, false, false), "the deal's own removal of its target");
            Assert.True(DistressRules.IgnoresBankruptcy(false, false, true, false, false), "a company the player bought");

            string feature = SourceOf("RivalAcquisitionsFeature.cs");
            Assert.True(feature.Contains("State.backfillDone = true"), "a new game marks the backfill done, so its bankrupts get no auction");
            string monitor = SourceOf("Services", "RivalDistressMonitor.cs");
            Assert.True(monitor.Contains("ModTicks.IsFastForwarding") && monitor.Contains("ModTicks.IsLoading"), "the monitor asks the clock of the core");
        }

        // ---- 10. an old save: bankrupts without snapshots ----

        [Test]
        public void Case10_AnAuctionOfTheBackfill_HasNoCpusToBringBack_AndStillCanBeBought()
        {
            var s = new AcquisitionsState();
            CompanySnapshot t = TargetWithoutCpus();
            t.Status = CompanyStatus.BankruptAuction;
            t.Money = -200.0 * Million;

            Assert.True(DistressRules.NeedsBackfill(s, t.UniqueId, t.SaveId, true), "a bankrupt without a record gets an auction");
            AuctionData auction = DistressRules.OpenAuction(s, t.UniqueId, t.SaveId, RivalTestData.Today, 365, null);
            Assert.Equal(0, auction.snapshotCpuSaveIds.Count, "no snapshot of CPUs");
            Assert.False(DistressRules.NeedsBackfill(s, t.UniqueId, t.SaveId, true), "once");

            DealQuote q = RivalTestData.Quote(DealType.BankruptAssets, t, RivalTestData.Player(), s);
            AcquisitionPlan plan = AcquisitionPlanBuilder.Build(q, t, RivalTestData.Player(), new PlanOptions { ConsequencesAcknowledged = true });
            Assert.True(q.Available, q.ReasonKey);
            Assert.True(plan.CanExecute, string.Join(",", plan.BlockingErrors.ToArray()));
            Assert.Equal(0, plan.TransferFamilies.Count, "no CPUs come back");
            Assert.Equal(40, plan.LinesMoved, "the lines and technologies are still bought");
        }

        // ---- 11. a variant whose base was retired long ago ----

        [Test]
        public void Case11_AVariantBringsItsRetiredBase_TheBaseFirst()
        {
            CompanySnapshot t = TargetWithoutCpus();
            t.Cpus.Add(Live(11, 10));
            CpuSnapshot oldBase = Live(10);
            oldBase.IsRetired = true;
            t.Cpus.Add(oldBase);

            DealRequest request = Request(DealType.Hostile, t, new AcquisitionsState());
            DealRecord record = DealExecution.Draft(request);

            Assert.True(request.Plan.CanExecute, string.Join(",", request.Plan.BlockingErrors.ToArray()));
            Assert.Equal(new[] { 10, 11 }, record.cpuSaveIds.ToArray(), "the base is listed first, so the variant finds it when the save loads");
            Assert.Equal(new[] { 10 }, record.retiredCpuSaveIds.ToArray(), "and passes retired");
        }

        // ---- 12. huge sums in a late game ----

        [Test]
        public void Case12_HugeSums_AreFiniteWholeThousands_AndStillNeverBringMoney()
        {
            foreach (double scale in new[] { 1e9, 1e11, 1e13, 1e14 })
            {
                CompanySnapshot t = RivalTestData.Plain(scale, scale / 3.0, scale / 4.0);
                t.Lines.Add(RivalTestData.Wafer(RivalTestData.WaferOld, 100, 40, 1.0, scale / 2.0));
                PlayerSnapshot p = RivalTestData.Player(scale * 100.0);

                foreach (DealType type in new[] { DealType.Hostile, DealType.Distressed })
                {
                    CompanySnapshot target = t;
                    if (type == DealType.Distressed)
                    {
                        target = RivalTestData.Plain(scale, -scale / 5.0, scale / 4.0, CompanyStatus.InDefault);
                    }
                    DealQuote q = RivalTestData.Quote(type, target, p, new AcquisitionsState());

                    Assert.True(q.Available, scale + " " + type + ": " + q.ReasonKey);
                    Assert.False(double.IsNaN(q.Price) || double.IsInfinity(q.Price), scale + " " + type + ": finite");
                    Assert.False(float.IsInfinity((float)q.Price) || float.IsNaN((float)q.Price), scale + " " + type + ": the game's float holds it");
                    Assert.Near(0.0, q.Price % 1000.0, 1e-3 + scale * 1e-15, scale + " " + type + ": rounded up to 1000 dollars");
                    Assert.True(q.NetCash < 0.0, scale + " " + type + ": NetCash < 0");
                }
            }
        }

        // ---- 13. the feature is off between the deal and the save ----

        [Test]
        public void Case13_ThePlayersAssetsAreInTheVanillaSave_TheRecordInTheSectionThatIsKept()
        {
            // The assets are the game's own (nothing of them is in the section); the record is. What the section keeps while the feature is off is the test
            // AFeatureSwitchedOffAfterADeal_KeepsItsLastSavedSection (AcquisitionsStateSerializationTests); here: the record holds only what the section may hold.
            var s = new AcquisitionsState();
            DealRequest request = Request(DealType.Hostile, RivalTestData.ExampleTarget(), s);
            DealOutcome outcome = DealExecution.Run(new RecordingWorld(), request, s, Guard);

            Assert.True(outcome.Executed);
            var problems = new List<string>();
            Assert.Equal(0, s.Sanitize(problems), "what a deal writes is valid for the section: " + string.Join(" | ", problems.ToArray()));
            JsonRoundTrip(s);
        }

        private static void JsonRoundTrip(AcquisitionsState s)
        {
            var problems = new List<string>();
            PTMod.Core.Logic.Json.JsonObject written = AcquisitionsSection.Write(s, problems);
            AcquisitionsState back = AcquisitionsSection.Read(written, problems);
            Assert.Equal(0, problems.Count, string.Join(" | ", problems.ToArray()));
            Assert.Equal(s.deals.Count, back.deals.Count);
            Assert.Equal(s.deals[0].price, back.deals[0].price, "the price of the deal survives the file");
            Assert.Equal(s.nextDealId, back.nextDealId);
        }

        // ---- 14. a target in default with a positive balance ----

        [Test]
        public void Case14_InDefaultWithCash_ThePriceNeverFallsBelowTheCashPlusTheFloor()
        {
            // E = 100 million, C = +900 million, D = 111 million: 0.7 x 1000 = 700 million would be below the cash; the floor C+ + E_min = 927.75 million rules.
            CompanySnapshot t = RivalTestData.Plain(100.0 * Million, 900.0 * Million, RivalTestData.DebtLimit, CompanyStatus.InDefault);

            DealQuote q = RivalTestData.Quote(DealType.Distressed, t, RivalTestData.Player(), new AcquisitionsState());

            Assert.True(q.Available, q.ReasonKey);
            Assert.True(q.CashFloorApplied, "the preview says the floor set the price");
            Assert.True(q.Price >= 927.75 * Million, "price " + q.Price);
            Assert.True(q.NetCash < 0.0 && q.NetCash >= -28.0 * Million, "the player pays about E_min for the whole company: " + q.NetCash);
        }

        // ---- 15. the player already has an exclusive contract like the client's ----

        [Test]
        public void Case15_TheExclusiveContractOfTheClient_IsNotTakenOver_AndThePreviewShowsItAsTerminated()
        {
            CompanySnapshot t = TargetWithoutCpus();
            t.Cpus.Add(Live(1, inHouse: false, makerId: 700));
            t.ClientContracts.Add(RivalTestData.Contract(5, 700, t.SaveId, exclusive: true, providerName: "TSMC", clientName: "Texas Instruments"));
            PlayerSnapshot p = RivalTestData.Player();
            p.HasExclusiveClientContract = true;
            DealQuote q = RivalTestData.Quote(DealType.Hostile, t, p, new AcquisitionsState());
            AcquisitionPlan plan = AcquisitionPlanBuilder.Build(q, t, p, new PlanOptions { ConsequencesAcknowledged = true });

            Assert.Equal(0, plan.RepointClientContractIds.Count);
            Assert.Equal(new List<int> { 5 }, plan.DroppedClientContractIds);

            DealPreviewModel model = DealPreviewModel.Build(q, plan, t, p, UiTestData.Format());
            PreviewSection removed = UiTestData.Section(model, UiKeys.SectionRemoved);
            Assert.NotNull(removed, "the block of what is terminated");
            Assert.True(UiTestData.Contains(removed.Lines, UiKeys.PreviewContractDropped), "the contract is listed there");
        }

        // ---- 16. a batch run "next month" ----

        [Test]
        public void Case16_TheFeatureDoesNoDailyWork_AndItsMailsAndRescuesAreNotTiedToABatch()
        {
            // The core shows the mails of a batch run after ModTicks.BatchEnded (mod-framework, MailScheduler); the feature only hands them to ModMail. The rescue
            // runs on the day of TickBankruptcies, inside the game's own method, batch or not.
            foreach (string path in System.IO.Directory.GetFiles(System.IO.Path.Combine(TestEnv.SrcDir, "Features", "RivalAcquisitions"), "*.cs", System.IO.SearchOption.AllDirectories))
            {
                string code = SourceScan.Strip(SourceScan.ReadText(path), true);
                Assert.False(code.Contains("TickPhase.DayPassed"), path + ": NFR-1: no daily work");
                Assert.False(code.Contains("IsBatchRun") || code.Contains("BatchEnded"), path + ": nothing depends on a batch run (the core handles it)");
            }
            Assert.True(SourceOf("Services", "AcquisitionMail.cs").Contains("ModMail.Send("), "the mails go through the mail service of the core");
        }

        // ---- 17. the related features are off or not there ----

        [Test]
        public void Case17_WithoutTheRelatedFeatures_TheStateIsNotTransferred_AndTheDealGoesOn()
        {
            // the order of the calls with the features off or without an API is checked in ExecutionOrderTests; here the contract of the interface:
            var log = new DealLog();
            var off = new OffFeatures();

            RelatedStateTransfer.Run(off, DealType.Hostile, log, Guard);

            Assert.Equal(0, off.Calls, "no call");
            Assert.False(log.Partial);
            Assert.True(log.AssetsComplete);
            Assert.Equal(0, log.Notes.Count);
        }

        private sealed class OffFeatures : IRelatedFeatures
        {
            public int Calls;

            public bool IsActive(string featureId) { return false; }
            public bool ArchitecturesApiFound { get { return true; } }
            public bool GpuApiFound { get { return true; } }
            public void TransferArchitectures() { Calls++; }
            public bool IsGpuDivisionFounded() { Calls++; return true; }
            public bool TransferGpuProducts() { Calls++; return true; }
        }

        // ---- 18. a target with a rescue order bought whole ----

        [Test]
        public void Case18_TheRescueOrderAndTheStakeOfABoughtTarget_AreClosedTogetherWithTheDeal()
        {
            var s = new AcquisitionsState();
            CompanySnapshot t = RivalTestData.ExampleTarget();
            s.stakes.Add(RivalTestData.Stake(t, 0.10));
            StakeBook.SetOrder(s, t.UniqueId, t.SaveId, 50.0 * Million, true);
            StakeBook.SetOrder(s, 11, 411, 10.0 * Million, true);   // the order of another company is not touched

            DealOutcome outcome = DealExecution.Run(new RecordingWorld(), Request(DealType.Hostile, t, s), s, Guard);

            Assert.True(outcome.Executed);
            Assert.Null(StakeBook.Order(s, t.UniqueId, t.SaveId), "the order of the bought company is gone");
            Assert.NotNull(StakeBook.Order(s, 11, 411), "the order of another one stays");
            Assert.Near(0.0, s.ShareOf(t.UniqueId), 1e-12);
            Assert.False(StakeBook.ShouldTryRescue(StakeBook.Order(s, t.UniqueId, t.SaveId) != null, -111.0 * Million, true, -150.0 * Million), "so no rescue is tried for it");
        }

        // ---- helpers ----

        private static string SourceOf(params string[] parts)
        {
            Assert.NotNull(TestEnv.SrcDir, "source folder not found; run the tests through python mod/build.py --tests");
            string path = System.IO.Path.Combine(TestEnv.SrcDir, "Features", "RivalAcquisitions", System.IO.Path.Combine(parts));
            Assert.True(System.IO.File.Exists(path), "missing " + path);
            return SourceScan.Strip(SourceScan.ReadText(path), false);
        }
    }
}
