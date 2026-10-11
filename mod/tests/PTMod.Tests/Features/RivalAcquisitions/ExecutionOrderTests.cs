using System;
using System.Collections.Generic;
using System.Linq;
using PTMod.Features.RivalAcquisitions;
using PTMod.Tests.Architecture;

namespace PTMod.Tests.Features.RivalAcquisitions
{
    /// <summary>
    /// Step 9 of a deal on fakes (TSK-024; FR-16, AC-16.1 to AC-16.4, Edge Case 17): the state of <c>cpu-architectures</c> and <c>gpu-product-line</c> passes to the
    /// player BEFORE the target leaves the game, only while those features are active and their API is there, a <c>false</c> of the GPU call is a note in the record of the
    /// deal, the assets of a bankrupt company call no GPU API, and a call that throws makes the deal partial without stopping it. Both the order of the calls and the
    /// preview that promises them (<see cref="RelatedStatePlan"/>, <see cref="DealPreviewModel"/>) are checked. The calls of the real APIs
    /// (<c>GameRelatedFeatures</c>) are read from its source and checked in the game (M1, TSK-022).
    /// </summary>
    [Covers(typeof(RelatedStateTransfer), typeof(RelatedStatePlan), typeof(IRelatedFeatures))]
    public class ExecutionOrderTests
    {
        private const double Million = 1000000.0;

        // ---- the fakes ----

        /// <summary>Both related features, as the deal sees them; records every call in the order it came, into the same list as the fake game.</summary>
        private sealed class FakeRelated : IRelatedFeatures
        {
            private readonly List<string> _calls;

            public bool ArchActive = true;
            public bool GpuActive = true;
            public bool ArchApi = true;
            public bool GpuApi = true;
            public bool DivisionFounded = true;
            public bool GpuResult = true;
            public bool ArchThrows;
            public bool GpuThrows;
            public bool GpuCheckThrows;

            public string ArchArgs = "";

            public FakeRelated(List<string> calls)
            {
                _calls = calls;
            }

            public bool IsActive(string featureId)
            {
                if (featureId == RelatedStateTransfer.ArchitecturesFeature) return ArchActive;
                if (featureId == RelatedStateTransfer.GpuFeature) return GpuActive;
                return false;
            }

            public bool ArchitecturesApiFound
            {
                get { return ArchApi; }
            }

            public bool GpuApiFound
            {
                get { return GpuApi; }
            }

            public void TransferArchitectures()
            {
                _calls.Add("Arch.TransferCompanyState(target, player, true)");
                ArchArgs = "target,player,true";
                if (ArchThrows) throw new InvalidOperationException("arch");
            }

            public bool IsGpuDivisionFounded()
            {
                _calls.Add("Gpu.IsDivisionFounded(target)");
                if (GpuCheckThrows) throw new InvalidOperationException("gpu check");
                return DivisionFounded;
            }

            public bool TransferGpuProducts()
            {
                _calls.Add("Gpu.TransferVendorProducts(target, player)");
                if (GpuThrows) throw new InvalidOperationException("gpu");
                return GpuResult;
            }
        }

        /// <summary>The game of a deal reduced to the order of its steps; step 9 runs the real <see cref="RelatedStateTransfer"/> on the fake features.</summary>
        private sealed class OrderWorld : IDealWorld
        {
            public readonly List<string> Calls = new List<string>();
            public readonly FakeRelated Related;
            public Func<string, Action, bool> StepGuard;

            public OrderWorld()
            {
                Related = new FakeRelated(Calls);
            }

            public string Recheck(DealRequest request)
            {
                return null;
            }

            public void Begin(DealRequest request)
            {
                Calls.Add("Begin");
            }

            public void UnlockTechnologies(AcquisitionPlan plan, DealLog log)
            {
                Calls.Add("Technologies");
            }

            public void RaiseNodeExperience(AcquisitionPlan plan, DealLog log)
            {
                Calls.Add("NodeExperience");
            }

            public void DetachCpus(AcquisitionPlan plan, DealLog log)
            {
                Calls.Add("DetachCpus");
            }

            public void MoveLines(AcquisitionPlan plan, DealLog log)
            {
                Calls.Add("Lines");
            }

            public void MoveContracts(AcquisitionPlan plan, DealLog log)
            {
                Calls.Add("Contracts");
            }

            public void AttachCpus(AcquisitionPlan plan, DealLog log)
            {
                Calls.Add("AttachCpus");
            }

            public void TransferRelatedState(DealRequest request, DealLog log)
            {
                Calls.Add("Step9");
                RelatedStateTransfer.Run(Related, request.Type, log, StepGuard);
            }

            public void TakeOutTarget(DealLog log)
            {
                Calls.Add("TakeOut");
            }

            public void PayOut(double amount, string op, string details)
            {
                Calls.Add("PayOut " + op);
            }

            public void PayIn(double amount, string op, string details)
            {
                Calls.Add("PayIn " + op);
            }

            public void AddReputation(int delta, string reasonKey)
            {
                Calls.Add("Reputation");
            }

            public void RaiseStakeChanged(int uniqueId, double share)
            {
            }

            public void RaiseDealCompleted(DealRecord record)
            {
                Calls.Add("DealCompleted");
            }

            public void SendDealMail(DealRecord record)
            {
                Calls.Add("Mail");
            }

            public void End()
            {
                Calls.Add("End");
            }
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

        private static DealRequest Request(DealType type, CompanySnapshot t, AcquisitionsState s)
        {
            PlayerSnapshot p = RivalTestData.Player();
            DealQuote quote = RivalTestData.Quote(type, t, p, s);
            AcquisitionPlan plan = AcquisitionPlanBuilder.Build(quote, t, p, new PlanOptions { KeepLines = true, ConsequencesAcknowledged = true });
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

        private static CompanySnapshot Healthy()
        {
            return RivalTestData.ExampleTarget();
        }

        private static CompanySnapshot InDefault()
        {
            CompanySnapshot t = RivalTestData.ExampleTarget();
            t.Status = CompanyStatus.InDefault;
            t.DaysInDefault = 30;
            t.Money = -60.0 * Million;
            return t;
        }

        private static CompanySnapshot Bankrupt(AcquisitionsState s)
        {
            CompanySnapshot t = RivalTestData.ExampleTarget();
            t.Status = CompanyStatus.BankruptAuction;
            t.Money = -200.0 * Million;
            foreach (CpuSnapshot cpu in t.Cpus)
            {
                cpu.IsRetired = true;
                cpu.ActiveAtBankruptcy = true;
            }
            s.auctions.Add(RivalTestData.Auction(t, "1985-12-31"));
            return t;
        }

        private static OrderWorld Run(DealType type, CompanySnapshot t, AcquisitionsState s, Action<FakeRelated> setup, out DealOutcome outcome)
        {
            var world = new OrderWorld { StepGuard = Guard };
            if (setup != null) setup(world.Related);
            outcome = DealExecution.Run(world, Request(type, t, s), s, Guard);
            Assert.True(outcome.Executed, "the deal went ahead: " + outcome.CancelReasonKey);
            return world;
        }

        private static string Order(OrderWorld world)
        {
            return string.Join(" | ", world.Calls.ToArray());
        }

        private static int IndexOf(OrderWorld world, string call)
        {
            return world.Calls.IndexOf(call);
        }

        // ---- the order (AC-16.1, AC-16.2) ----

        [Test]
        public void ATakeover_PassesTheArchitectures_ThenTheGpuLineup_BeforeTheTargetLeaves()
        {
            foreach (DealType type in new[] { DealType.Hostile, DealType.Distressed })
            {
                var s = new AcquisitionsState();
                CompanySnapshot t = type == DealType.Hostile ? Healthy() : InDefault();
                DealOutcome outcome;

                OrderWorld world = Run(type, t, s, null, out outcome);

                var expected = new List<string>
                {
                    "Begin", "Technologies", "NodeExperience", "DetachCpus", "Lines", "Contracts", "AttachCpus", "Step9",
                    "Arch.TransferCompanyState(target, player, true)", "Gpu.IsDivisionFounded(target)", "Gpu.TransferVendorProducts(target, player)",
                    "TakeOut", "PayOut acq.price", type == DealType.Hostile ? "PayIn acq.cash" : "PayOut acq.debt",
                };
                if (type == DealType.Hostile) expected.Add("Reputation");   // -5 for a hostile takeover, 0 for a company bought in default
                expected.AddRange(new[] { "DealCompleted", "Mail", "End" });
                Assert.Equal(string.Join(" | ", expected.ToArray()), Order(world), type.ToString());
                Assert.False(outcome.Record.partial, type + ": nothing failed");
                Assert.True(outcome.Record.notes.Contains("architectures: researched families merged"), outcome.Record.notes);
                Assert.True(outcome.Record.notes.Contains("gpu: the GPU lineup of the target passed to the player"), outcome.Record.notes);
            }
        }

        [Test]
        public void TheArchitectures_AreAskedToMergeAndKeepTheBest_BeforeTheTargetIsTakenOut()
        {
            // AC-16.1: TransferCompanyState(target, player, true) before the target leaves.
            var s = new AcquisitionsState();
            DealOutcome outcome;

            OrderWorld world = Run(DealType.Hostile, Healthy(), s, null, out outcome);

            Assert.Equal("target,player,true", world.Related.ArchArgs);
            Assert.True(IndexOf(world, "Arch.TransferCompanyState(target, player, true)") < IndexOf(world, "TakeOut"), "before the target leaves");
            Assert.True(IndexOf(world, "Arch.TransferCompanyState(target, player, true)") > IndexOf(world, "AttachCpus"), "after the CPUs passed (the bindings of the CPUs stay by SaveID)");
        }

        [Test]
        public void TheGpuLineup_IsPassedBeforeTheTargetLeaves_SoTheVendorIsNotTakenOffTheMarketWithIt()
        {
            // AC-16.2: TransferVendorProducts is called before TriggerBankruptcy of the target, which the GPU feature answers by removing the vendor.
            var s = new AcquisitionsState();
            DealOutcome outcome;

            OrderWorld world = Run(DealType.Hostile, Healthy(), s, null, out outcome);

            int check = IndexOf(world, "Gpu.IsDivisionFounded(target)");
            int transfer = IndexOf(world, "Gpu.TransferVendorProducts(target, player)");
            Assert.True(check >= 0 && transfer > check, "the division is checked, then the lineup passes");
            Assert.True(transfer < IndexOf(world, "TakeOut"), "before the target is taken out");
            Assert.True(transfer < IndexOf(world, "PayOut acq.price"), "and before the payment");
        }

        // ---- the assets of a bankrupt company (AC-16.4) ----

        [Test]
        public void TheAssetsOfABankrupt_PassTheArchitectures_ButCallNoGpuApi_AndSayWhyInTheRecord()
        {
            var s = new AcquisitionsState();
            CompanySnapshot t = Bankrupt(s);
            DealOutcome outcome;

            OrderWorld world = Run(DealType.BankruptAssets, t, s, null, out outcome);

            Assert.True(world.Calls.Contains("Arch.TransferCompanyState(target, player, true)"), "FR-16 item 1 covers all three purchases");
            Assert.False(world.Calls.Any(c => c.StartsWith("Gpu.")), "no GpuApi call at all for the assets of a bankrupt: " + Order(world));
            Assert.False(world.Calls.Contains("TakeOut"), "the bankrupt is out already");
            Assert.True(outcome.Record.notes.Contains("gpu: the GPU lineup, if the company had one, is not transferred"), "the note in the record: " + outcome.Record.notes);
            Assert.False(outcome.Record.partial, "that is how it is meant to be, not a failure");
            Assert.True(outcome.Paid);
        }

        // ---- a false result (the note of FR-16 item 2) ----

        [Test]
        public void AFalseFromTheGpuFeature_IsANoteInTheRecord_AndDoesNotBlockTheDeal()
        {
            var s = new AcquisitionsState();
            DealOutcome outcome;

            OrderWorld world = Run(DealType.Hostile, Healthy(), s, r => r.GpuResult = false, out outcome);

            Assert.True(outcome.Record.notes.Contains("GPU lineup was not transferred"), outcome.Record.notes);
            Assert.True(outcome.Paid, "the deal is paid");
            Assert.True(world.Calls.Contains("TakeOut"), "the target is taken out");
            Assert.Equal(1, s.deals.Count);
            Assert.True(outcome.Record.targetName.Length > 0);
        }

        [Test]
        public void ATargetWithoutAGpuDivision_PassesNoLineup()
        {
            var s = new AcquisitionsState();
            DealOutcome outcome;

            OrderWorld world = Run(DealType.Hostile, Healthy(), s, r => r.DivisionFounded = false, out outcome);

            Assert.True(world.Calls.Contains("Gpu.IsDivisionFounded(target)"));
            Assert.False(world.Calls.Contains("Gpu.TransferVendorProducts(target, player)"), "nothing to pass");
            Assert.False(outcome.Record.partial);
            Assert.True(outcome.Record.notes.Contains("no GPU division"), outcome.Record.notes);
        }

        // ---- the features are off or not there (AC-16.3, Edge Case 17) ----

        [Test]
        public void WithBothFeaturesOff_NoCallIsMade_AndTheDealGoesThrough()
        {
            foreach (DealType type in new[] { DealType.Hostile, DealType.Distressed, DealType.BankruptAssets })
            {
                var s = new AcquisitionsState();
                CompanySnapshot t = type == DealType.Hostile ? Healthy() : type == DealType.Distressed ? InDefault() : Bankrupt(s);
                DealOutcome outcome;

                OrderWorld world = Run(type, t, s, r => { r.ArchActive = false; r.GpuActive = false; }, out outcome);

                Assert.False(world.Calls.Any(c => c.StartsWith("Arch.") || c.StartsWith("Gpu.")), type + ": no call: " + Order(world));
                Assert.False(outcome.Record.partial, type.ToString());
                Assert.True(outcome.Paid, type.ToString());
                Assert.Equal("", outcome.Record.notes, type + ": the record does not mention features that are off");
            }
        }

        [Test]
        public void WithOnlyOneFeatureOn_OnlyItsCallIsMade()
        {
            var s = new AcquisitionsState();
            DealOutcome outcome;

            OrderWorld onlyArch = Run(DealType.Hostile, Healthy(), s, r => r.GpuActive = false, out outcome);
            Assert.True(onlyArch.Calls.Contains("Arch.TransferCompanyState(target, player, true)"));
            Assert.False(onlyArch.Calls.Any(c => c.StartsWith("Gpu.")), "AC-16.3: no GPU call with gpu-product-line off");

            s = new AcquisitionsState();
            OrderWorld onlyGpu = Run(DealType.Hostile, Healthy(), s, r => r.ArchActive = false, out outcome);
            Assert.False(onlyGpu.Calls.Any(c => c.StartsWith("Arch.")), "no architectures call with cpu-architectures off");
            Assert.True(onlyGpu.Calls.Contains("Gpu.TransferVendorProducts(target, player)"));
        }

        [Test]
        public void AFeatureThatIsOnButHasNoApiInTheBuild_IsSkipped_ForTheDealDoesNotDependOnIt()
        {
            var s = new AcquisitionsState();
            DealOutcome outcome;

            OrderWorld world = Run(DealType.Hostile, Healthy(), s, r => { r.ArchApi = false; r.GpuApi = false; }, out outcome);

            Assert.False(world.Calls.Any(c => c.StartsWith("Arch.") || c.StartsWith("Gpu.")), Order(world));
            Assert.False(outcome.Record.partial, "FR-16 item 3: not blocked, not even partial");
            Assert.True(outcome.Paid);
            Assert.True(outcome.Record.notes.Contains("API was not found"), outcome.Record.notes);
        }

        // ---- a call that throws (FR-16 item 4) ----

        [Test]
        public void AnExceptionOfTheArchitecturesCall_MakesTheDealPartial_TheGpuCallStillRuns_AndThePriceIsCharged()
        {
            var s = new AcquisitionsState();
            DealOutcome outcome;

            OrderWorld world = Run(DealType.Hostile, Healthy(), s, r => r.ArchThrows = true, out outcome);

            Assert.True(outcome.Record.partial);
            Assert.True(outcome.Log.HasFailed(DealStep.RelatedFeatures), "step 9 is the one named");
            Assert.True(world.Calls.Contains("Gpu.TransferVendorProducts(target, player)"), "the other call is not skipped");
            Assert.True(world.Calls.Contains("TakeOut"), "the target is taken out");
            Assert.True(outcome.Paid, "the assets of the steps 3 to 8 are the player's, so is the bill");
            Assert.True(outcome.Record.notes.Contains("architectures: the transfer failed"), outcome.Record.notes);
        }

        [Test]
        public void AnExceptionOfTheGpuCall_MakesTheDealPartial_AndNothingAfterItIsStopped()
        {
            foreach (bool inTheCheck in new[] { false, true })
            {
                var s = new AcquisitionsState();
                DealOutcome outcome;

                OrderWorld world = Run(DealType.Distressed, InDefault(), s, r => { r.GpuThrows = !inTheCheck; r.GpuCheckThrows = inTheCheck; }, out outcome);

                Assert.True(outcome.Record.partial, inTheCheck.ToString());
                Assert.True(outcome.Log.HasFailed(DealStep.RelatedFeatures));
                Assert.True(world.Calls.Contains("Arch.TransferCompanyState(target, player, true)"), "the architectures went first");
                Assert.True(world.Calls.Contains("TakeOut") && world.Calls.Contains("PayOut acq.price") && world.Calls.Contains("DealCompleted"), Order(world));
                Assert.True(outcome.Record.notes.Contains("gpu: the transfer failed"), outcome.Record.notes);
                Assert.Equal("End", world.Calls[world.Calls.Count - 1]);
            }
        }

        [Test]
        public void ABrokenAssetStep_StillLetsStep9Run_ButNothingIsCharged()
        {
            // The pipeline is unchanged by step 9: a failed step 3-8 holds the payment back, whatever the related features do.
            var s = new AcquisitionsState();
            var world = new OrderWorld { StepGuard = Guard };
            DealRequest request = Request(DealType.Hostile, Healthy(), s);
            var failing = new FailingLinesWorld(world);

            DealOutcome outcome = DealExecution.Run(failing, request, s, Guard);

            Assert.True(outcome.Executed);
            Assert.True(outcome.Record.partial);
            Assert.False(outcome.Paid);
            Assert.True(world.Calls.Contains("Arch.TransferCompanyState(target, player, true)"), "step 9 ran");
            Assert.False(world.Calls.Any(c => c.StartsWith("PayOut") || c.StartsWith("PayIn")), "no money moved");
        }

        /// <summary>The order world with a lines step that fails.</summary>
        private sealed class FailingLinesWorld : IDealWorld
        {
            private readonly OrderWorld _inner;

            public FailingLinesWorld(OrderWorld inner)
            {
                _inner = inner;
            }

            public string Recheck(DealRequest request) { return _inner.Recheck(request); }
            public void Begin(DealRequest request) { _inner.Begin(request); }
            public void UnlockTechnologies(AcquisitionPlan plan, DealLog log) { _inner.UnlockTechnologies(plan, log); }
            public void RaiseNodeExperience(AcquisitionPlan plan, DealLog log) { _inner.RaiseNodeExperience(plan, log); }
            public void DetachCpus(AcquisitionPlan plan, DealLog log) { _inner.DetachCpus(plan, log); }
            public void MoveLines(AcquisitionPlan plan, DealLog log) { throw new InvalidOperationException("lines"); }
            public void MoveContracts(AcquisitionPlan plan, DealLog log) { _inner.MoveContracts(plan, log); }
            public void AttachCpus(AcquisitionPlan plan, DealLog log) { _inner.AttachCpus(plan, log); }
            public void TransferRelatedState(DealRequest request, DealLog log) { _inner.TransferRelatedState(request, log); }
            public void TakeOutTarget(DealLog log) { _inner.TakeOutTarget(log); }
            public void PayOut(double amount, string op, string details) { _inner.PayOut(amount, op, details); }
            public void PayIn(double amount, string op, string details) { _inner.PayIn(amount, op, details); }
            public void AddReputation(int delta, string reasonKey) { _inner.AddReputation(delta, reasonKey); }
            public void RaiseStakeChanged(int uniqueId, double share) { _inner.RaiseStakeChanged(uniqueId, share); }
            public void RaiseDealCompleted(DealRecord record) { _inner.RaiseDealCompleted(record); }
            public void SendDealMail(DealRecord record) { _inner.SendDealMail(record); }
            public void End() { _inner.End(); }
        }

        [Test]
        public void TheCalls_AreNotMadeWithoutAGuardOrAnObject()
        {
            var log = new DealLog();
            var calls = new List<string>();
            var related = new FakeRelated(calls);

            RelatedStateTransfer.Run(null, DealType.Hostile, log, Guard);
            RelatedStateTransfer.Run(related, DealType.Hostile, null, Guard);
            RelatedStateTransfer.Run(related, DealType.Hostile, log, null);

            Assert.Equal(0, calls.Count, "a missing part of the call is a no-op, never an exception");
            Assert.Equal(0, log.Notes.Count);
        }

        [Test]
        public void EachCall_RunsUnderItsOwnGuardSite()
        {
            var sites = new List<string>();
            var log = new DealLog();
            var related = new FakeRelated(new List<string>());

            RelatedStateTransfer.Run(related, DealType.Hostile, log, (site, body) =>
            {
                sites.Add(site);
                body();
                return true;
            });

            Assert.Equal(new[] { "AcquisitionExecutor.step9.architectures", "AcquisitionExecutor.step9.gpu" }, sites.ToArray());
        }

        // ---- the preview promises what the deal does ----

        [Test]
        public void ThePlan_ForTheFeaturesThatAreOn_ListsWhatWillPass()
        {
            var calls = new List<string>();
            var related = new FakeRelated(calls);

            RelatedStatePlan hostile = RelatedStatePlan.Of(DealType.Hostile, related);
            Assert.True(hostile.Architectures);
            Assert.True(hostile.GpuLineup);
            Assert.False(hostile.GpuNotPassed);
            Assert.True(hostile.Any);
            Assert.True(RelatedStatePlan.Of(DealType.Distressed, related).GpuLineup);

            RelatedStatePlan bankrupt = RelatedStatePlan.Of(DealType.BankruptAssets, related);
            Assert.True(bankrupt.Architectures);
            Assert.False(bankrupt.GpuLineup);
            Assert.True(bankrupt.GpuNotPassed, "the player is told the lineup does not come with the assets of a bankrupt");
            Assert.False(calls.Any(c => c == "Gpu.TransferVendorProducts(target, player)"), "reading the plan changes nothing");
        }

        [Test]
        public void ThePlan_OmitsWhatIsOffMissingOrAbsent()
        {
            var related = new FakeRelated(new List<string>());

            related.DivisionFounded = false;
            Assert.False(RelatedStatePlan.Of(DealType.Hostile, related).GpuLineup, "no GPU division, no promise");

            related.ArchActive = false;
            related.GpuActive = false;
            RelatedStatePlan off = RelatedStatePlan.Of(DealType.BankruptAssets, related);
            Assert.False(off.Any, "both off: the preview says nothing");

            related.ArchActive = true;
            related.GpuActive = true;
            related.ArchApi = false;
            related.GpuApi = false;
            related.DivisionFounded = true;
            RelatedStatePlan noApi = RelatedStatePlan.Of(DealType.Hostile, related);
            Assert.False(noApi.Architectures);
            Assert.False(noApi.GpuLineup);

            Assert.False(RelatedStatePlan.Of(DealType.Hostile, null).Any);
        }

        [Test]
        public void ThePlan_NeverThrows_WhenAFeatureFailsWhileThePlayerReadsThePreview()
        {
            var related = new FakeRelated(new List<string>()) { GpuCheckThrows = true };

            RelatedStatePlan plan = RelatedStatePlan.Of(DealType.Hostile, related);

            Assert.True(plan.Architectures);
            Assert.False(plan.GpuLineup);
        }

        [Test]
        public void ThePreview_ShowsTheStateThatPassesInTheBlockOfWhatPasses()
        {
            var s = new AcquisitionsState();
            CompanySnapshot t = Healthy();
            PlayerSnapshot p = RivalTestData.Player();
            DealQuote quote = RivalTestData.Quote(DealType.Hostile, t, p, s);
            AcquisitionPlan plan = AcquisitionPlanBuilder.Build(quote, t, p, new PlanOptions { KeepLines = true, ConsequencesAcknowledged = true });
            var related = new RelatedStatePlan { Architectures = true, GpuLineup = true };

            DealPreviewModel with = DealPreviewModel.Build(quote, plan, t, p, UiTestData.Format(), related);
            DealPreviewModel without = DealPreviewModel.Build(quote, plan, t, p, UiTestData.Format());

            PreviewSection transfers = with.Sections.First(x => x.TitleKey == UiKeys.SectionTransfers);
            Assert.True(transfers.Lines.Any(l => l.Text.Contains(UiKeys.PreviewRelatedArchitectures)), "the architectures");
            Assert.True(transfers.Lines.Any(l => l.Text.Contains(UiKeys.PreviewRelatedGpu)), "the GPU lineup");
            Assert.False(transfers.Lines.Any(l => l.Text.Contains(UiKeys.PreviewGpuNotPassed)));

            PreviewSection plain = without.Sections.First(x => x.TitleKey == UiKeys.SectionTransfers);
            Assert.False(plain.Lines.Any(l => l.Text.Contains(UiKeys.PreviewRelatedArchitectures) || l.Text.Contains(UiKeys.PreviewRelatedGpu)), "without a plan nothing is promised");
            Assert.Equal(plain.Lines.Count + 2, transfers.Lines.Count);
        }

        [Test]
        public void ThePreviewOfABankruptsAssets_WarnsThatNoGpuLineupPasses()
        {
            var s = new AcquisitionsState();
            CompanySnapshot t = Bankrupt(s);
            PlayerSnapshot p = RivalTestData.Player();
            DealQuote quote = RivalTestData.Quote(DealType.BankruptAssets, t, p, s);
            AcquisitionPlan plan = AcquisitionPlanBuilder.Build(quote, t, p, new PlanOptions { KeepLines = true, ConsequencesAcknowledged = true });

            DealPreviewModel model = DealPreviewModel.Build(quote, plan, t, p, UiTestData.Format(), new RelatedStatePlan { Architectures = true, GpuNotPassed = true });

            PreviewLine warning = model.Sections.First(x => x.TitleKey == UiKeys.SectionTransfers).Lines.First(l => l.Text.Contains(UiKeys.PreviewGpuNotPassed));
            Assert.Equal(ViewTone.Warning, warning.Tone);
        }

        // ---- the game side, read from its source ----

        [Test]
        public void TheGameSide_CallsTheRealApisByName_UnderTheActiveCheck_WithoutAHardDependency()
        {
            string bridge = SourceOf("Services", "GameRelatedFeatures.cs");
            foreach (string needle in new[]
                     {
                         "PTMod.Features.CpuArchitectures.ArchitectureApi", "\"TransferCompanyState\"", "PTMod.Features.GpuProductLine.GpuApi", "\"IsDivisionFounded\"",
                         "\"TransferVendorProducts\"", "ModFeatures.IsActive(featureId)", "typeof(bool)",
                     })
            {
                Assert.True(bridge.Contains(needle), "the bridge does not contain: " + needle);
            }

            string finish = SourceOf("Services", "AcquisitionExecutor.Finish.cs");
            Assert.True(finish.Contains("RelatedStateTransfer.Run(features, request.Type, log,"), "step 9 runs the pure rules");
            Assert.True(finish.Contains("ModHooks.Guard(Feature, site, body)"), "each call under the guard of the feature");

            // no compile-time reference to the other features, and no DependsOn on them
            foreach (string path in System.IO.Directory.GetFiles(System.IO.Path.Combine(TestEnv.SrcDir, "Features", "RivalAcquisitions"), "*.cs", System.IO.SearchOption.AllDirectories))
            {
                string text = SourceScan.Strip(SourceScan.ReadText(path), true);   // code only: no comments, no text of literals
                Assert.False(text.Contains("using PTMod.Features.CpuArchitectures") || text.Contains("using PTMod.Features.GpuProductLine"), path + ": a compile-time reference");
                Assert.False(System.Text.RegularExpressions.Regex.IsMatch(text, @"\b(ArchitectureApi|GpuApi)\s*\."), path + ": a direct call of another feature's class");
            }
            string feature = SourceOf("RivalAcquisitionsFeature.cs");
            Assert.True(feature.Contains("return new string[0];"), "DependsOn stays empty");
        }

        private static string SourceOf(params string[] parts)
        {
            Assert.NotNull(TestEnv.SrcDir, "source folder not found; run the tests through python mod/build.py --tests");
            string path = System.IO.Path.Combine(TestEnv.SrcDir, "Features", "RivalAcquisitions", System.IO.Path.Combine(parts));
            Assert.True(System.IO.File.Exists(path), "missing " + path);
            return System.IO.File.ReadAllText(path);
        }
    }
}
