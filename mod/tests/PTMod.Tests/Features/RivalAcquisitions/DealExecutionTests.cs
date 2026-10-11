using System;
using System.Collections.Generic;
using PTMod.Features.RivalAcquisitions;

namespace PTMod.Tests.Features.RivalAcquisitions
{
    /// <summary>
    /// <c>DealExecution</c> (TSK-010…TSK-012; FR-3…FR-5, FR-7…FR-10, NFR-2) on a fake game: the order of the steps, the record before the target leaves
    /// the game, the payment last and only for a complete transfer of the assets, the partial deals, the checks before any change, and that the target
    /// object keeps its identity. The game side (<c>AcquisitionExecutor</c>) is checked by reading its source (<see cref="ExecutorSourceTests"/>) and in
    /// the game (M1-M7 of design.md).
    /// </summary>
    [Covers(typeof(DealExecution), typeof(DealStep), typeof(DealLog), typeof(DealRequest), typeof(DealOutcome), typeof(IDealWorld))]
    public class DealExecutionTests
    {
        private const double Million = 1000000.0;

        // ---- the fake game ----

        /// <summary>The target company as the fake game holds it: the identity must survive the deal untouched.</summary>
        private sealed class FakeCompany
        {
            public int UniqueId;
            public int SaveId;
            public string Name;
            public double Money;
            public bool IsBankrupt;
        }

        private sealed class FakeWorld : IDealWorld
        {
            public readonly List<string> Calls = new List<string>();
            public readonly List<string> Payments = new List<string>();
            public readonly List<int> Reputation = new List<int>();
            public readonly List<string> ReputationKeys = new List<string>();
            public readonly List<KeyValuePair<int, double>> StakeEvents = new List<KeyValuePair<int, double>>();
            public readonly List<DealRecord> Completed = new List<DealRecord>();
            public readonly List<DealRecord> Mails = new List<DealRecord>();

            public FakeCompany Target;
            public AcquisitionsState State;
            public string RecheckResult;
            public bool RecheckThrows;
            public DealStep ThrowAt = DealStep.None;
            public DealStep SoftFailAt = DealStep.None;

            /// <summary>What <c>IsAbsorbedByPlayer</c> would have answered inside the game's bankruptcy event.</summary>
            public bool? AbsorbedInsideTheEvent;

            public double PlayerMoney;

            public string Recheck(DealRequest request)
            {
                Calls.Add("Recheck");
                if (RecheckThrows) throw new InvalidOperationException("recheck");
                return RecheckResult;
            }

            public void Begin(DealRequest request)
            {
                Calls.Add("Begin");
            }

            public void UnlockTechnologies(AcquisitionPlan plan, DealLog log)
            {
                Do(DealStep.Technologies, "Technologies", log);
            }

            public void RaiseNodeExperience(AcquisitionPlan plan, DealLog log)
            {
                Do(DealStep.NodeExperience, "NodeExperience", log);
            }

            public void DetachCpus(AcquisitionPlan plan, DealLog log)
            {
                Do(DealStep.DetachCpus, "DetachCpus", log);
            }

            public void MoveLines(AcquisitionPlan plan, DealLog log)
            {
                Do(DealStep.Lines, "Lines", log);
            }

            public void MoveContracts(AcquisitionPlan plan, DealLog log)
            {
                Do(DealStep.Contracts, "Contracts", log);
            }

            public void AttachCpus(AcquisitionPlan plan, DealLog log)
            {
                Do(DealStep.AttachCpus, "AttachCpus", log);
            }

            public void TransferRelatedState(DealRequest request, DealLog log)
            {
                Do(DealStep.RelatedFeatures, "RelatedFeatures", log);
            }

            public void TakeOutTarget(DealLog log)
            {
                Do(DealStep.TakeOut, "TakeOut", log);
                Target.Money = 0.0;
                Target.IsBankrupt = true;
                AbsorbedInsideTheEvent = State.IsAbsorbed(Target.UniqueId);
            }

            public void PayOut(double amount, string op, string details)
            {
                if (ThrowAt == DealStep.Payment) throw new InvalidOperationException("pay");
                Calls.Add("PayOut " + op);
                Payments.Add("-" + op);
                PlayerMoney -= amount;
            }

            public void PayIn(double amount, string op, string details)
            {
                Calls.Add("PayIn " + op);
                Payments.Add("+" + op);
                PlayerMoney += amount;
            }

            public void AddReputation(int delta, string reasonKey)
            {
                Calls.Add("Reputation");
                Reputation.Add(delta);
                ReputationKeys.Add(reasonKey);
            }

            public void RaiseStakeChanged(int uniqueId, double share)
            {
                Calls.Add("StakeChanged");
                StakeEvents.Add(new KeyValuePair<int, double>(uniqueId, share));
            }

            public void RaiseDealCompleted(DealRecord record)
            {
                Calls.Add("DealCompleted");
                Completed.Add(record);
            }

            public void SendDealMail(DealRecord record)
            {
                Calls.Add("Mail");
                Mails.Add(record);
            }

            public void End()
            {
                Calls.Add("End");
            }

            private void Do(DealStep step, string name, DealLog log)
            {
                Calls.Add(name);
                if (ThrowAt == step) throw new InvalidOperationException(name);
                if (SoftFailAt == step) log.Fail(name + ": fewer than planned");
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

        private static DealRequest Request(DealType type, CompanySnapshot t, AcquisitionsState s, PlanOptions options = null)
        {
            PlayerSnapshot p = RivalTestData.Player();
            DealQuote quote = RivalTestData.Quote(type, t, p, s);
            AcquisitionPlan plan = AcquisitionPlanBuilder.Build(quote, t, p, options ?? new PlanOptions { KeepLines = true, ConsequencesAcknowledged = true });
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

        private static FakeWorld World(CompanySnapshot t, AcquisitionsState s)
        {
            return new FakeWorld
            {
                Target = new FakeCompany { UniqueId = t.UniqueId, SaveId = t.SaveId, Name = t.Name, Money = t.Money },
                State = s,
                PlayerMoney = 5000.0 * Million,
            };
        }

        private static CompanySnapshot InDefault()
        {
            CompanySnapshot t = RivalTestData.ExampleTarget();
            t.Status = CompanyStatus.InDefault;
            t.DaysInDefault = 30;
            t.Money = -60.0 * Million;
            return t;
        }

        private static CompanySnapshot BankruptWithAuction(AcquisitionsState s)
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

        // ---- the order of the steps ----

        [Test]
        public void Run_GoesThroughTheStepsOfTheDesign_InOrder_WithThePaymentLastOfTheChanges()
        {
            var s = new AcquisitionsState();
            CompanySnapshot t = RivalTestData.ExampleTarget();
            FakeWorld world = World(t, s);

            DealOutcome outcome = DealExecution.Run(world, Request(DealType.Hostile, t, s), s, Guard);

            Assert.True(outcome.Executed, "the deal went ahead: " + outcome.CancelReasonKey);
            string[] expected =
            {
                "Recheck", "Begin", "Technologies", "NodeExperience", "DetachCpus", "Lines", "Contracts", "AttachCpus", "RelatedFeatures", "TakeOut",
                "PayOut acq.price", "PayIn acq.cash", "Reputation", "DealCompleted", "Mail", "End",
            };
            Assert.Equal(string.Join(" | ", expected), string.Join(" | ", world.Calls.ToArray()));
        }

        [Test]
        public void Run_PutsTheRecordIntoTheBooks_BeforeTheTargetLeavesTheGame()
        {
            // FR-10 item 5, AC-10.3: a subscriber of OnCompanyWentBankrupt that asks IsAbsorbedByPlayer gets true.
            var s = new AcquisitionsState();
            CompanySnapshot t = RivalTestData.ExampleTarget();
            FakeWorld world = World(t, s);

            DealExecution.Run(world, Request(DealType.Hostile, t, s), s, Guard);

            Assert.True(world.AbsorbedInsideTheEvent.HasValue, "the target was taken out");
            Assert.True(world.AbsorbedInsideTheEvent.Value, "the record was in the books inside the bankruptcy event");
            Assert.Equal(1, s.deals.Count);
            Assert.True(s.IsAbsorbed(t.UniqueId));
        }

        [Test]
        public void Run_KeepsTheIdentityOfTheTargetObject()
        {
            // D1, FR-10 item 2: the company object, its UniqueID, SaveID and Name are not touched; it only becomes bankrupt with a balance of 0.
            var s = new AcquisitionsState();
            CompanySnapshot t = RivalTestData.ExampleTarget();
            FakeWorld world = World(t, s);
            FakeCompany before = world.Target;

            DealExecution.Run(world, Request(DealType.Hostile, t, s), s, Guard);

            Assert.True(ReferenceEquals(before, world.Target), "the same object");
            Assert.Equal(3, world.Target.UniqueId);
            Assert.Equal(412, world.Target.SaveId);
            Assert.Equal("Texas Instruments", world.Target.Name);
            Assert.True(world.Target.IsBankrupt);
            Assert.Near(0.0, world.Target.Money);
            Assert.Equal(3, t.UniqueId, "the snapshot is not changed either");
            Assert.Equal("Texas Instruments", t.Name);
        }

        [Test]
        public void Run_EndIsCalled_EvenWhenAStepThrows()
        {
            foreach (DealStep step in new[] { DealStep.Technologies, DealStep.AttachCpus, DealStep.TakeOut, DealStep.Payment })
            {
                var s = new AcquisitionsState();
                CompanySnapshot t = RivalTestData.ExampleTarget();
                FakeWorld world = World(t, s);
                world.ThrowAt = step;

                DealOutcome outcome = DealExecution.Run(world, Request(DealType.Hostile, t, s), s, Guard);

                Assert.True(outcome.Executed, step + ": the deal still went ahead");
                Assert.Equal("End", world.Calls[world.Calls.Count - 1], step + ": End comes last");
            }
        }

        // ---- the money ----

        [Test]
        public void Run_Hostile_PaysThePrice_AndTakesTheCashOver_TheNetIsNegative()
        {
            // AC-5.1: price 696.9 million for E = 434.6 million and C = +30 million; the net for the player's balance is about -666.9 million.
            var s = new AcquisitionsState();
            CompanySnapshot t = RivalTestData.ExampleTarget();
            FakeWorld world = World(t, s);
            DealRequest request = Request(DealType.Hostile, t, s);

            DealOutcome outcome = DealExecution.Run(world, request, s, Guard);

            Assert.True(outcome.Paid);
            Assert.Equal("-acq.price | +acq.cash", string.Join(" | ", world.Payments.ToArray()));
            Assert.Near(696.9 * Million, outcome.Record.price, 0.1 * Million);
            Assert.Near(30.0 * Million, outcome.Record.assumedCash, 1.0);
            Assert.Near(5000.0 * Million - 666.9 * Million, world.PlayerMoney, 0.1 * Million);
            Assert.True(world.PlayerMoney < 5000.0 * Million, "NetCash < 0");
        }

        [Test]
        public void Run_Distressed_PaysThePriceAndTheDebt()
        {
            // AC-4.1: price 304.2 million, the debt of 60 million comes along: -364.2 million.
            var s = new AcquisitionsState();
            CompanySnapshot t = InDefault();
            FakeWorld world = World(t, s);

            DealOutcome outcome = DealExecution.Run(world, Request(DealType.Distressed, t, s), s, Guard);

            Assert.True(outcome.Executed, outcome.CancelReasonKey);
            Assert.Equal("-acq.price | -acq.debt", string.Join(" | ", world.Payments.ToArray()));
            Assert.Near(5000.0 * Million - 364.2 * Million, world.PlayerMoney, 0.1 * Million);
            Assert.Near(-60.0 * Million, outcome.Record.assumedCash, 1.0);
            Assert.True(world.Calls.Contains("TakeOut"), "a company bought in default leaves the game");
        }

        [Test]
        public void Run_BankruptAssets_NeitherTakesTheTargetOutNorItsDebt_AndClosesTheAuction()
        {
            var s = new AcquisitionsState();
            CompanySnapshot t = BankruptWithAuction(s);
            FakeWorld world = World(t, s);

            DealOutcome outcome = DealExecution.Run(world, Request(DealType.BankruptAssets, t, s), s, Guard);

            Assert.True(outcome.Executed, outcome.CancelReasonKey);
            Assert.False(world.Calls.Contains("TakeOut"), "the bankrupt is out already");
            Assert.Equal("-acq.price", string.Join(" | ", world.Payments.ToArray()), "only the price; the debt stays with the bankrupt");
            Assert.Near(0.0, outcome.Record.assumedCash);
            Assert.True(s.auctions[0].closed, "the auction is closed (FR-3 item 4)");
            Assert.True(s.IsAbsorbed(t.UniqueId));
        }

        [Test]
        public void Run_AFailedAssetStep_MakesTheDealPartial_AndChargesNothing()
        {
            // design.md "Error Handling": the price is charged only when the steps 3-8 went through; the cash is not taken over either.
            foreach (DealStep step in new[] { DealStep.Technologies, DealStep.NodeExperience, DealStep.DetachCpus, DealStep.Lines, DealStep.Contracts, DealStep.AttachCpus })
            {
                var s = new AcquisitionsState();
                CompanySnapshot t = RivalTestData.ExampleTarget();
                FakeWorld world = World(t, s);
                world.ThrowAt = step;

                DealOutcome outcome = DealExecution.Run(world, Request(DealType.Hostile, t, s), s, Guard);

                Assert.True(outcome.Executed, step + ": the deal stands");
                Assert.True(outcome.Record.partial, step + ": partial");
                Assert.False(outcome.Paid, step + ": not paid");
                Assert.Equal(0, world.Payments.Count, step + ": no money moved");
                Assert.Near(0.0, outcome.Record.price, 0.0, step + ": the record says nothing was paid");
                Assert.Near(0.0, outcome.Record.assumedCash, 0.0, step.ToString());
                Assert.True(outcome.Log.HasFailed(step), step + ": the step is named");
                Assert.True(world.Calls.Contains("TakeOut"), step + ": the later steps still run");
                Assert.Equal(1, world.Completed.Count, step + ": DealCompleted");
            }
        }

        [Test]
        public void Run_AStepThatReportsAProblemWithoutThrowing_IsPartialToo()
        {
            var s = new AcquisitionsState();
            CompanySnapshot t = RivalTestData.ExampleTarget();
            FakeWorld world = World(t, s);
            world.SoftFailAt = DealStep.Lines;

            DealOutcome outcome = DealExecution.Run(world, Request(DealType.Hostile, t, s), s, Guard);

            Assert.True(outcome.Record.partial);
            Assert.False(outcome.Paid);
            Assert.True(outcome.Record.notes.Contains("step 6: Lines: fewer than planned"), outcome.Record.notes);
        }

        [Test]
        public void Run_AFailureAfterTheAssets_IsPartial_ButThePriceIsCharged()
        {
            foreach (DealStep step in new[] { DealStep.RelatedFeatures, DealStep.TakeOut })
            {
                var s = new AcquisitionsState();
                CompanySnapshot t = RivalTestData.ExampleTarget();
                FakeWorld world = World(t, s);
                world.ThrowAt = step;

                DealOutcome outcome = DealExecution.Run(world, Request(DealType.Hostile, t, s), s, Guard);

                Assert.True(outcome.Record.partial, step + ": partial");
                Assert.True(outcome.Paid, step + ": the assets are the player's, so is the bill");
                Assert.Equal(2, world.Payments.Count, step.ToString());
            }
        }

        [Test]
        public void Run_AFailedPayment_IsPartial()
        {
            var s = new AcquisitionsState();
            CompanySnapshot t = RivalTestData.ExampleTarget();
            FakeWorld world = World(t, s);
            world.ThrowAt = DealStep.Payment;

            DealOutcome outcome = DealExecution.Run(world, Request(DealType.Hostile, t, s), s, Guard);

            Assert.False(outcome.Paid);
            Assert.True(outcome.Record.partial);
            Assert.True(outcome.Log.HasFailed(DealStep.Payment));
        }

        // ---- the checks before any change ----

        [Test]
        public void Run_ARefusedRecheck_ChangesNothing()
        {
            var s = new AcquisitionsState();
            CompanySnapshot t = RivalTestData.ExampleTarget();
            FakeWorld world = World(t, s);
            world.RecheckResult = AcqKeys.ReasonStatusChanged;

            DealOutcome outcome = DealExecution.Run(world, Request(DealType.Hostile, t, s), s, Guard);

            Assert.False(outcome.Executed);
            Assert.Equal(AcqKeys.ReasonStatusChanged, outcome.CancelReasonKey);
            Assert.Equal("Recheck", string.Join(" | ", world.Calls.ToArray()), "nothing after the recheck, not even Begin");
            Assert.Equal(0, s.deals.Count);
        }

        [Test]
        public void Run_ARecheckThatThrows_Cancels()
        {
            var s = new AcquisitionsState();
            CompanySnapshot t = RivalTestData.ExampleTarget();
            FakeWorld world = World(t, s);
            world.RecheckThrows = true;

            DealOutcome outcome = DealExecution.Run(world, Request(DealType.Hostile, t, s), s, Guard);

            Assert.False(outcome.Executed);
            Assert.Equal(AcqKeys.ReasonUnsupported, outcome.CancelReasonKey);
            Assert.False(world.Calls.Contains("Begin"));
        }

        [Test]
        public void Validate_RefusesARequestThatIsNotTheOneThePlayerSaw()
        {
            var s = new AcquisitionsState();
            string arg;

            DealRequest other = Request(DealType.Hostile, RivalTestData.ExampleTarget(), s);
            other.ExpectedSaveId = 999;
            Assert.Equal(AcqKeys.ReasonTargetChanged, DealExecution.Validate(other, out arg));

            DealRequest status = Request(DealType.Hostile, RivalTestData.ExampleTarget(), s);
            status.ExpectedStatus = CompanyStatus.InDefault;
            Assert.Equal(AcqKeys.ReasonStatusChanged, DealExecution.Validate(status, out arg));

            DealRequest dearer = Request(DealType.Hostile, RivalTestData.ExampleTarget(), s);
            dearer.ShownPrice = dearer.Quote.Price - 1000.0;
            Assert.Equal(AcqKeys.ReasonPriceChanged, DealExecution.Validate(dearer, out arg));
            Assert.Equal(dearer.Quote.Price.ToString("F0", System.Globalization.CultureInfo.InvariantCulture), arg);

            DealRequest same = Request(DealType.Hostile, RivalTestData.ExampleTarget(), s);
            Assert.Null(DealExecution.Validate(same, out arg), "the price the player saw is fine");
            same.ShownPrice = 0.0;
            Assert.Null(DealExecution.Validate(same, out arg), "no price shown: not checked");
        }

        [Test]
        public void Validate_RefusesAnUnavailableQuoteOrABlockedPlan_WithTheirReason()
        {
            var s = new AcquisitionsState();

            // The player cannot pay: the quote's reason and its argument.
            PlayerSnapshot poor = RivalTestData.Player(1.0 * Million);
            CompanySnapshot t = RivalTestData.ExampleTarget();
            DealRequest broke = Request(DealType.Hostile, t, s);
            broke.Quote = RivalTestData.Quote(DealType.Hostile, t, poor, s);
            string arg;
            Assert.Equal(AcqKeys.ReasonNoCash, DealExecution.Validate(broke, out arg));
            Assert.NotNull(arg);

            // Intel without the tick: the plan is blocked.
            CompanySnapshot intel = RivalTestData.ExampleTarget();
            intel.UniqueId = AcquisitionPlanBuilder.IntelId;
            DealRequest blocked = Request(DealType.Hostile, intel, s, new PlanOptions { KeepLines = true, ConsequencesAcknowledged = false });
            Assert.Equal(AcqKeys.PlanAckRequired, DealExecution.Validate(blocked, out arg));

            // A world that is asked nothing.
            FakeWorld world = World(intel, s);
            DealOutcome outcome = DealExecution.Run(world, blocked, s, Guard);
            Assert.False(outcome.Executed);
            Assert.Equal(0, world.Calls.Count, "not even the recheck");
        }

        [Test]
        public void Validate_RefusesWhatIsNotAPurchaseOfAWholeCompany_AndANonNegativeNet()
        {
            var s = new AcquisitionsState();
            string arg;

            DealRequest stake = Request(DealType.Hostile, RivalTestData.ExampleTarget(), s);
            stake.Type = DealType.Stake;
            Assert.Equal(AcqKeys.PlanNotPurchase, DealExecution.Validate(stake, out arg));

            DealRequest free = Request(DealType.Hostile, RivalTestData.ExampleTarget(), s);
            DealQuote q = free.Quote;
            q.NetCash = 0.0;
            free.Quote = q;
            Assert.Equal(AcqKeys.ReasonUnsupported, DealExecution.Validate(free, out arg), "the invariant NetCash < 0 is checked again");

            Assert.Equal(AcqKeys.ReasonUnsupported, DealExecution.Validate(null, out arg));
        }

        // ---- the books ----

        [Test]
        public void Run_Hostile_StartsTheCooldown_EndsTheStakeAndTheRescueOrder_AndCostsReputation()
        {
            // Edge cases 6 and 18: the stake made the price lower and is closed with the deal; the rescue order too. FR-5 item 4: -5 reputation.
            var s = new AcquisitionsState();
            CompanySnapshot t = RivalTestData.ExampleTarget();
            s.stakes.Add(RivalTestData.Stake(t, 0.25));
            s.rescueOrders.Add(new RescueOrderData { targetUniqueId = t.UniqueId, targetSaveId = t.SaveId, maxAmount = 1.0, active = true });
            FakeWorld world = World(t, s);
            DealRequest request = Request(DealType.Hostile, t, s);
            Assert.Near(0.25, request.Quote.OwnedShare, 1e-9, "the price took the stake into account");

            DealExecution.Run(world, request, s, Guard);

            Assert.Equal("1985-03-01", s.lastHostileOn);
            Assert.Equal(0, s.stakes.Count, "the stake is closed");
            Assert.Equal(0, s.rescueOrders.Count, "the rescue order is closed");
            Assert.Equal(1, world.StakeEvents.Count);
            Assert.Equal(t.UniqueId, world.StakeEvents[0].Key);
            Assert.Near(0.0, world.StakeEvents[0].Value);
            Assert.Equal(-5, world.Reputation[0]);
            Assert.Equal(AcqKeys.ReputationHostile, world.ReputationKeys[0]);
        }

        [Test]
        public void Run_Distressed_LeavesTheCooldownAndTheReputationAlone()
        {
            var s = new AcquisitionsState();
            CompanySnapshot t = InDefault();
            FakeWorld world = World(t, s);

            DealExecution.Run(world, Request(DealType.Distressed, t, s), s, Guard);

            Assert.Equal("", s.lastHostileOn);
            Assert.Equal(0, world.Reputation.Count, "0 points: no call");
            Assert.Equal(0, world.StakeEvents.Count, "no stake, no StakeChanged");
        }

        [Test]
        public void Run_TheRecordOfTheEventAndTheMail_IsTheOneInTheBooks()
        {
            var s = new AcquisitionsState();
            s.nextDealId = 7;
            CompanySnapshot t = RivalTestData.ExampleTarget();
            FakeWorld world = World(t, s);

            DealOutcome outcome = DealExecution.Run(world, Request(DealType.Hostile, t, s), s, Guard);

            Assert.Equal(7, outcome.Record.id);
            Assert.Equal(8, s.nextDealId);
            Assert.True(ReferenceEquals(outcome.Record, s.deals[0]));
            Assert.True(ReferenceEquals(outcome.Record, world.Completed[0]), "DealCompleted gets the record of the books");
            Assert.True(ReferenceEquals(outcome.Record, world.Mails[0]), "the mail too");
            Assert.False(outcome.Record.partial);
            Assert.Equal("", outcome.Record.notes);
        }

        [Test]
        public void Draft_ListsWhatThePlanMoves()
        {
            var s = new AcquisitionsState();
            CompanySnapshot t = RivalTestData.ExampleTarget();
            t.ProviderContracts.Add(RivalTestData.Contract(0, t.SaveId, 77));
            DealRequest request = Request(DealType.Hostile, t, s);

            DealRecord record = DealExecution.Draft(request);

            Assert.Equal("Hostile", record.type);
            Assert.Equal(3, record.targetUniqueId);
            Assert.Equal(412, record.targetSaveId);
            Assert.Equal("Texas Instruments", record.targetName);
            Assert.Equal("1985-03-01", record.date);
            Assert.True(record.keptLines);
            Assert.Equal(1, record.lines.Count);
            Assert.Equal(RivalTestData.WaferOld, record.lines[0].waferId);
            Assert.Equal(40, record.lines[0].lines);
            Assert.Equal(3, record.techCount);
            Assert.Equal("1201,1207", string.Join(",", record.cpuSaveIds.ConvertAll(x => x.ToString()).ToArray()), "the base first");
            Assert.Equal(1, record.providerContracts);
            Assert.Equal(0, record.clientContracts);
            Assert.Near(0.0, record.share);
        }

        [Test]
        public void Draft_WithTheLinesSold_ListsTheSoldLines_AndNoSupplyContract()
        {
            var s = new AcquisitionsState();
            CompanySnapshot t = RivalTestData.ExampleTarget();
            t.ProviderContracts.Add(RivalTestData.Contract(0, t.SaveId, 77));
            DealRequest request = Request(DealType.Hostile, t, s, new PlanOptions { KeepLines = false, ConsequencesAcknowledged = true });

            DealRecord record = DealExecution.Draft(request);

            Assert.False(record.keptLines);
            Assert.Equal(40, record.lines[0].lines);
            Assert.Equal(0, record.providerContracts, "the supply contracts end with the sold lines");
        }

        [Test]
        public void ReputationKeys_AreTheKeysOfTheDesign()
        {
            Assert.Equal("acq.reputation.hostile", DealExecution.ReputationKey(DealType.Hostile));
            Assert.Equal("acq.reputation.distressed", DealExecution.ReputationKey(DealType.Distressed));
            Assert.Equal("acq.reputation.bankrupt", DealExecution.ReputationKey(DealType.BankruptAssets));
            Assert.Equal("acq.reputation.rescue", DealExecution.ReputationKey(DealType.RescueOrder));
            Assert.True(DealExecution.TakesTargetOut(DealType.Hostile) && DealExecution.TakesTargetOut(DealType.Distressed));
            Assert.False(DealExecution.TakesTargetOut(DealType.BankruptAssets));
            Assert.False(DealExecution.IsPurchase(DealType.Stake) || DealExecution.IsPurchase(DealType.RescueOrder));
            Assert.Equal("AcquisitionExecutor.step6", DealExecution.SiteOf(DealStep.Lines));
        }

        [Test]
        public void DealLog_AssetsComplete_OnlyLooksAtTheStepsThreeToEight()
        {
            var log = new DealLog();
            Assert.True(log.AssetsComplete);
            log.MarkFailed(DealStep.RelatedFeatures);
            log.MarkFailed(DealStep.TakeOut);
            Assert.True(log.AssetsComplete);
            Assert.True(log.Partial);
            log.Current = DealStep.Contracts;
            log.Fail("x");
            Assert.False(log.AssetsComplete);
            Assert.Equal("step 7: x", log.Notes[0]);
            Assert.Equal("step 7: x", log.Joined());
        }

        // ---- AcquisitionsState.AddDeal ----

        [Test]
        public void AddDeal_NumbersTheDeals_PastEveryNumberUsed()
        {
            var s = new AcquisitionsState();
            s.deals.Add(new DealRecord { id = 4, type = "Stake", date = "1985-01-01" });
            s.nextDealId = 2;

            int first = s.AddDeal(new DealRecord { type = "Hostile" });
            int second = s.AddDeal(new DealRecord { type = "Stake" });

            Assert.Equal(5, first, "never a number in use");
            Assert.Equal(6, second);
            Assert.Equal(7, s.nextDealId);
            Assert.Throws<ArgumentNullException>(() => s.AddDeal(null));
        }
    }
}
