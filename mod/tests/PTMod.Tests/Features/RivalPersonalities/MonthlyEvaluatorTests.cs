using System;
using System.Collections.Generic;
using PTMod.Core;
using PTMod.Core.Logic.Json;
using PTMod.Features.RivalPersonalities;

namespace PTMod.Tests.Features.RivalPersonalities
{
    /// <summary>
    /// The monthly pass (TSK-012; design.md Component 7, <c>MonthlyEvaluator.Run</c>; FR-4 item 3, FR-5 items 2, 3, 6): the snapshot of the shares in the
    /// section, <c>ShareGain</c> at +3 points of the player against a falling rival, <c>PriceUndercut</c> at 0.9 of the performance and 0.85 of the price
    /// with a cooldown of 90 days for a pair, +2 for an active contract, the drift, and the cleaning of what ran out. The game side (collecting the
    /// CPUs, ending the external price cuts) is read-only glue around <c>MonthlyEvaluatorCore</c>.
    /// </summary>
    [Covers(typeof(MonthlyEvaluatorCore), typeof(MonthlyInput), typeof(MonthlyResult), typeof(RivalMarketView), typeof(PlayerCpuView), typeof(CpuBest),
            typeof(UndercutBook), typeof(ShareSnapshots), typeof(CautionCleaner))]
    public class MonthlyEvaluatorTests
    {
        private static readonly DateTime May = new DateTime(1990, 5, 1);
        private static readonly DateTime June = new DateTime(1990, 6, 1);

        private static MonthlyInput Input(DateTime today, params SalesRow[] sales)
        {
            var input = new MonthlyInput { Today = today };
            input.Sales.AddRange(sales);
            return input;
        }

        private static RivalMarketView Rival(int uid)
        {
            return new RivalMarketView { UniqueId = uid };
        }

        private static RivalMarketView RivalWithBest(int uid, Segment segment, float ips, float price)
        {
            RivalMarketView view = Rival(uid);
            view.Best[(int)segment] = new CpuBest(ips, price);
            return view;
        }

        private static AttitudeRig RigAt(DateTime today)
        {
            AttitudeRig rig = new AttitudeRig().AssignAll();
            rig.Today = today;
            return rig;
        }

        // ---- the snapshot of the shares ----

        [Test]
        public void FirstMonth_StoresTheSnapshot_AndComparesNothing()
        {
            AttitudeRig rig = RigAt(May);
            MonthlyInput input = Input(May, new SalesRow("player", 10, 0, 0, 0), new SalesRow("ai:0", 90, 0, 0, 0));
            input.Rivals.Add(Rival(AttitudeRig.Intel));

            MonthlyResult result = rig.Monthly.Run(input);

            Assert.False(result.ComparedShares);
            Assert.Equal(0, result.ShareGains);
            Assert.Equal(ShareSnapshots.MonthNumber(May), rig.State.shareSnapshot.monthIndex);
            Assert.Equal(2, rig.State.shareSnapshot.rows.Count);
            Assert.Equal("ai:0", rig.State.shareSnapshot.rows[0].key, "sorted by key");
            Assert.Near(0.9, rig.State.shareSnapshot.rows[0].s[0], 1e-6);
            Assert.Near(0.1, rig.State.shareSnapshot.rows[1].s[0], 1e-6);
            Assert.Equal(0, result.Failures);
        }

        [Test]
        public void ShareGain_WhenThePlayerGainsThreePointsAndTheRivalLoses()
        {
            // AC-5.4: High End, the player 10 % -> 14 %, Intel 60 % -> 55 %
            AttitudeRig rig = RigAt(May);
            MonthlyInput first = Input(May, new SalesRow("player", 10, 0, 0, 0), new SalesRow("ai:0", 60, 0, 0, 0), new SalesRow("ai:1", 30, 0, 0, 0));
            rig.Monthly.Run(first);

            rig.Today = June;
            MonthlyInput second = Input(June, new SalesRow("player", 14, 0, 0, 0), new SalesRow("ai:0", 55, 0, 0, 0), new SalesRow("ai:1", 31, 0, 0, 0));
            second.Rivals.Add(Rival(AttitudeRig.Intel));
            second.Rivals.Add(Rival(AttitudeRig.Amd));
            MonthlyResult result = rig.Monthly.Run(second);

            Assert.True(result.ComparedShares);
            Assert.Equal(1, result.ShareGains);
            Assert.Equal(1, rig.Triggers.Count);
            Assert.Equal(TriggerType.ShareGain, rig.Triggers[0].Type);
            Assert.Equal(AttitudeRig.Intel, rig.Triggers[0].UniqueId);
            Assert.Equal(Segment.HighEnd, rig.Triggers[0].Segment);
            Assert.Near(-10f - 5f * 1.2f, rig.Ledger.Get(AttitudeRig.Intel), 0.7, "-5 x 1.2, then the drift of the pass moved it a little back");
            Assert.Near(-15f, rig.Ledger.Get(AttitudeRig.Amd), 1e-4, "AMD gained a share and is not hurt");
            Assert.Equal(ShareSnapshots.MonthNumber(June), rig.State.shareSnapshot.monthIndex, "the snapshot moved on");
        }

        [Test]
        public void ShareGain_NeedsThreePoints()
        {
            AttitudeRig rig = RigAt(May);
            rig.Monthly.Run(Input(May, new SalesRow("player", 100, 0, 0, 0), new SalesRow("ai:0", 900, 0, 0, 0)));

            rig.Today = June;
            MonthlyInput second = Input(June, new SalesRow("player", 129, 0, 0, 0), new SalesRow("ai:0", 871, 0, 0, 0));   // 10 % -> 12.9 %
            second.Rivals.Add(Rival(AttitudeRig.Intel));
            MonthlyResult result = rig.Monthly.Run(second);

            Assert.Equal(0, result.ShareGains);
            Assert.Equal(0, rig.Triggers.Count);
        }

        [Test]
        public void ShareGain_IsPerSegment_AndTheSnapshotIsIndexedBySegmentNumber()
        {
            AttitudeRig rig = RigAt(May);
            rig.Monthly.Run(Input(May, new SalesRow("player", 10, 10, 0, 0), new SalesRow("ai:0", 90, 90, 0, 0)));

            rig.Today = June;
            MonthlyInput second = Input(June, new SalesRow("player", 20, 10, 0, 0), new SalesRow("ai:0", 80, 90, 0, 0));
            second.Rivals.Add(Rival(AttitudeRig.Intel));
            rig.Monthly.Run(second);

            Assert.Equal(1, rig.Triggers.Count, "only High End moved");
            Assert.Equal(Segment.HighEnd, rig.Triggers[0].Segment);
        }

        [Test]
        public void ShareGain_TwoSegments_GiveTwoTriggersForTheSameRival()
        {
            AttitudeRig rig = RigAt(May);
            rig.Monthly.Run(Input(May, new SalesRow("player", 10, 0, 10, 0), new SalesRow("ai:0", 90, 0, 90, 0)));

            rig.Today = June;
            MonthlyInput second = Input(June, new SalesRow("player", 20, 0, 20, 0), new SalesRow("ai:0", 80, 0, 80, 0));
            second.Rivals.Add(Rival(AttitudeRig.Intel));
            MonthlyResult result = rig.Monthly.Run(second);

            Assert.Equal(2, result.ShareGains);
            Assert.Equal(Segment.HighEnd, rig.Triggers[0].Segment);
            Assert.Equal(Segment.LowEnd, rig.Triggers[1].Segment);
        }

        [Test]
        public void ShareGain_IgnoresBankruptAndFoundryRivals()
        {
            AttitudeRig rig = RigAt(May);
            rig.Monthly.Run(Input(May, new SalesRow("player", 10, 0, 0, 0), new SalesRow("ai:0", 90, 0, 0, 0)));

            rig.Today = June;
            MonthlyInput second = Input(June, new SalesRow("player", 20, 0, 0, 0), new SalesRow("ai:0", 80, 0, 0, 0));
            second.Rivals.Add(new RivalMarketView { UniqueId = AttitudeRig.Intel, IsBankrupt = true });
            second.Rivals.Add(new RivalMarketView { UniqueId = AttitudeRig.Tsmc, IsFoundry = true });
            MonthlyResult result = rig.Monthly.Run(second);

            Assert.Equal(0, result.ShareGains);
        }

        [Test]
        public void ShareGain_IsNotJudgedAgainstASnapshotOfAnotherMonth()
        {
            AttitudeRig rig = RigAt(May);
            rig.Monthly.Run(Input(May, new SalesRow("player", 10, 0, 0, 0), new SalesRow("ai:0", 90, 0, 0, 0)));

            // the feature was off for two months: the saved snapshot is of the month before May, not of the month before August
            DateTime august = new DateTime(1990, 8, 1);
            rig.Today = august;
            MonthlyInput later = Input(august, new SalesRow("player", 30, 0, 0, 0), new SalesRow("ai:0", 70, 0, 0, 0));
            later.Rivals.Add(Rival(AttitudeRig.Intel));
            MonthlyResult result = rig.Monthly.Run(later);

            Assert.False(result.ComparedShares);
            Assert.Equal(0, rig.Triggers.Count);
            Assert.Equal(ShareSnapshots.MonthNumber(august), rig.State.shareSnapshot.monthIndex, "but the snapshot is renewed");
        }

        [Test]
        public void ShareGain_AcrossTheNewYear_ComparesDecemberWithNovember()
        {
            AttitudeRig rig = RigAt(new DateTime(1989, 12, 1));
            rig.Monthly.Run(Input(new DateTime(1989, 12, 1), new SalesRow("player", 10, 0, 0, 0), new SalesRow("ai:0", 90, 0, 0, 0)));

            DateTime january = new DateTime(1990, 1, 1);
            rig.Today = january;
            MonthlyInput second = Input(january, new SalesRow("player", 20, 0, 0, 0), new SalesRow("ai:0", 80, 0, 0, 0));
            second.Rivals.Add(Rival(AttitudeRig.Intel));

            Assert.True(rig.Monthly.Run(second).ComparedShares);
            Assert.Equal(1, rig.Triggers.Count);
        }

        [Test]
        public void TheSnapshot_SurvivesSavingAndLoading()
        {
            AttitudeRig rig = RigAt(May);
            rig.Monthly.Run(Input(May, new SalesRow("player", 10, 0, 0, 0), new SalesRow("ai:0", 90, 0, 0, 0)));

            string text = ModJson.Write(RivalSaveCodec.Write(rig.State), false);
            JsonNode node;
            JsonError error;
            Assert.True(ModJson.TryParse(text, out node, out error), "" + error);
            AttitudeRig loaded = RigAt(June);
            loaded.State = RivalSaveCodec.Read((JsonObject)node, null);
            loaded.AssignAll();

            MonthlyInput second = Input(June, new SalesRow("player", 20, 0, 0, 0), new SalesRow("ai:0", 80, 0, 0, 0));
            second.Rivals.Add(Rival(AttitudeRig.Intel));
            MonthlyResult result = loaded.Monthly.Run(second);

            Assert.True(result.ComparedShares, "the month before came back with the save");
            Assert.Equal(1, result.ShareGains);
        }

        // ---- the price undercut ----

        [Test]
        public void Undercut_FiresAtTheBorders_AndWaitsNinetyDaysForThePair()
        {
            AttitudeRig rig = RigAt(May);
            var rivals = new List<RivalMarketView> { RivalWithBest(AttitudeRig.Amd, Segment.MidRange, 100f, 200f) };

            MonthlyInput first = Input(May);
            first.Rivals.AddRange(rivals);
            first.PlayerCpus.Add(new PlayerCpuView("Zeta 1", 90f, 170f, Segment.MidRange));   // 0.9 of the performance, 0.85 of the price
            MonthlyResult r1 = rig.Monthly.Run(first);

            Assert.Equal(1, r1.Undercuts);
            Assert.Equal(TriggerType.PriceUndercut, rig.Triggers[0].Type);
            Assert.Equal(AttitudeRig.Amd, rig.Triggers[0].UniqueId);
            Assert.Equal(Segment.MidRange, rig.Triggers[0].Segment);
            Assert.Equal("Zeta 1", rig.Triggers[0].Detail);
            Assert.Equal("1990-07-30", rig.State.undercutCooldowns[0].until, "90 days after the first of May");

            rig.Today = June;
            MonthlyInput second = Input(June);
            second.Rivals.AddRange(rivals);
            second.PlayerCpus.Add(new PlayerCpuView("Zeta 1", 90f, 170f, Segment.MidRange));
            Assert.Equal(0, rig.Monthly.Run(second).Undercuts, "June: still cooling down");

            DateTime august = new DateTime(1990, 8, 1);
            rig.Today = august;
            MonthlyInput third = Input(august);
            third.Rivals.AddRange(rivals);
            third.PlayerCpus.Add(new PlayerCpuView("Zeta 1", 90f, 170f, Segment.MidRange));
            Assert.Equal(1, rig.Monthly.Run(third).Undercuts, "August: the cooldown is over");
            Assert.Equal(2, rig.Triggers.Count);
        }

        [Test]
        public void Undercut_NeedsBothTheSpeedAndThePrice()
        {
            AttitudeRig rig = RigAt(May);
            MonthlyInput input = Input(May);
            input.Rivals.Add(RivalWithBest(AttitudeRig.Amd, Segment.MidRange, 100f, 200f));
            input.PlayerCpus.Add(new PlayerCpuView("Slow", 80f, 100f, Segment.MidRange));      // cheap but slow
            input.PlayerCpus.Add(new PlayerCpuView("Dear", 120f, 190f, Segment.MidRange));     // fast but not cheap enough (0.95)

            Assert.Equal(0, rig.Monthly.Run(input).Undercuts);
            Assert.Equal(0, rig.State.undercutCooldowns.Count);
        }

        [Test]
        public void Undercut_IsJudgedInTheSegmentOfTheRivalsBestCpu()
        {
            AttitudeRig rig = RigAt(May);
            MonthlyInput input = Input(May);
            input.Rivals.Add(RivalWithBest(AttitudeRig.Amd, Segment.MidRange, 100f, 200f));
            input.PlayerCpus.Add(new PlayerCpuView("Low End chip", 120f, 50f, Segment.LowEnd));

            Assert.Equal(0, rig.Monthly.Run(input).Undercuts, "a Low End chip does not undercut a Mid Range one");
        }

        [Test]
        public void Undercut_IsPerRivalAndSegment_AndNamesTheFastestUndercutter()
        {
            AttitudeRig rig = RigAt(May);
            MonthlyInput first = Input(May);
            first.Rivals.Add(RivalWithBest(AttitudeRig.Amd, Segment.MidRange, 100f, 200f));
            first.Rivals.Add(RivalWithBest(AttitudeRig.Intel, Segment.MidRange, 100f, 200f));
            first.PlayerCpus.Add(new PlayerCpuView("Zeta 1", 92f, 150f, Segment.MidRange));
            first.PlayerCpus.Add(new PlayerCpuView("Zeta 2", 110f, 160f, Segment.MidRange));
            Assert.Equal(2, rig.Monthly.Run(first).Undercuts);
            Assert.Equal("Zeta 2", rig.Triggers[0].Detail, "the fastest of the CPUs that undercut");
            Assert.Equal(2, rig.State.undercutCooldowns.Count);

            // Intel's Mid Range pair is still cooling down, its High End pair is new
            rig.Today = June;
            MonthlyInput second = Input(June);
            RivalMarketView intel = RivalWithBest(AttitudeRig.Intel, Segment.MidRange, 100f, 200f);
            intel.Best[(int)Segment.HighEnd] = new CpuBest(100f, 300f);
            second.Rivals.Add(intel);
            second.PlayerCpus.Add(new PlayerCpuView("Zeta 9", 100f, 250f, Segment.HighEnd));
            Assert.Equal(1, rig.Monthly.Run(second).Undercuts);
            Assert.Equal(Segment.HighEnd, rig.Triggers[2].Segment);
        }

        [Test]
        public void Undercut_UsesTheThresholdsOfTheConfig()
        {
            AttitudeRig rig = RigAt(May);
            rig.Catalog.Config.triggerThresholds.undercutPriceRatio = 0.5f;
            MonthlyInput input = Input(May);
            input.Rivals.Add(RivalWithBest(AttitudeRig.Amd, Segment.MidRange, 100f, 200f));
            input.PlayerCpus.Add(new PlayerCpuView("Zeta 1", 90f, 170f, Segment.MidRange));

            Assert.Equal(0, rig.Monthly.Run(input).Undercuts, "170 is more than half of 200");
        }

        [Test]
        public void Undercut_IgnoresBankruptRivals_AndStartsNoCooldownWhenNothingWasRecorded()
        {
            AttitudeRig rig = RigAt(May);
            MonthlyInput input = Input(May);
            RivalMarketView bankrupt = RivalWithBest(AttitudeRig.Amd, Segment.MidRange, 100f, 200f);
            bankrupt.IsBankrupt = true;
            input.Rivals.Add(bankrupt);
            input.PlayerCpus.Add(new PlayerCpuView("Zeta 1", 90f, 170f, Segment.MidRange));
            Assert.Equal(0, rig.Monthly.Run(input).Undercuts);

            // a dormant rival: the recorder refuses, so the pair is not put on cooldown
            rig.State.dormant.Add(AttitudeRig.Intel);
            MonthlyInput second = Input(May);
            second.Rivals.Add(RivalWithBest(AttitudeRig.Intel, Segment.MidRange, 100f, 200f));
            second.PlayerCpus.Add(new PlayerCpuView("Zeta 1", 90f, 170f, Segment.MidRange));
            Assert.Equal(0, rig.Monthly.Run(second).Undercuts);
            Assert.Equal(0, rig.State.undercutCooldowns.Count);
        }

        // ---- contracts, drift, order ----

        [Test]
        public void ActiveContracts_GivePlusTwoEach()
        {
            // two contracts with the foundry (sensitivity 0.5: +1 each) and one with IBM (0.8: +1.6)
            AttitudeRig rig = RigAt(May);
            MonthlyInput input = Input(May);
            input.ContractRivals.AddRange(new[] { AttitudeRig.Tsmc, AttitudeRig.Tsmc, AttitudeRig.Ibm });

            MonthlyResult result = rig.Monthly.Run(input);

            Assert.Equal(3, result.ContractBonuses);
            Assert.Equal(0, rig.Triggers.Count, "no trigger for a contract");
            Assert.Near(21.8, rig.Ledger.Get(AttitudeRig.Tsmc), 1e-3, "20 + 1 + 1 = 22, then a tenth of the way back to 20");
            Assert.Near(1.44, rig.Ledger.Get(AttitudeRig.Ibm), 1e-3, "0 + 1.6, then a tenth of the way back to 0");
        }

        [Test]
        public void TheMonthlyPass_AddsTheContractBonus_BeforeTheDrift()
        {
            // Toshiba: -40, baseline 0. +2, then 10 % of the way to 0: -38 -> -34.2 (the other order would give -36 + 2 = -34)
            AttitudeRig rig = RigAt(May).SetAttitude(AttitudeRig.Toshiba, -40f);
            MonthlyInput input = Input(May);
            input.ContractRivals.Add(AttitudeRig.Toshiba);

            MonthlyResult result = rig.Monthly.Run(input);

            Assert.Near(-34.2, rig.Ledger.Get(AttitudeRig.Toshiba), 1e-3);
            Assert.True(result.Drifted >= 1);
        }

        [Test]
        public void TheMonthlyPass_DriftsEveryAttitude_ByTenPercent()
        {
            AttitudeRig rig = RigAt(May).SetAttitude(AttitudeRig.Toshiba, -40f);   // AC-4.2

            MonthlyResult result = rig.Monthly.Run(Input(May));

            Assert.Near(-36.0, rig.Ledger.Get(AttitudeRig.Toshiba), 1e-4);
            Assert.Equal(1, result.Drifted);
        }

        [Test]
        public void TheMonthlyPass_ShareGainCountsBeforeTheDrift()
        {
            AttitudeRig rig = RigAt(May);
            rig.Monthly.Run(Input(May, new SalesRow("player", 10, 0, 0, 0), new SalesRow("ai:6", 90, 0, 0, 0)));
            rig.Today = June;
            rig.SetAttitude(AttitudeRig.Toshiba, 0f);

            MonthlyInput second = Input(June, new SalesRow("player", 20, 0, 0, 0), new SalesRow("ai:6", 80, 0, 0, 0));
            second.Rivals.Add(Rival(AttitudeRig.Toshiba));
            rig.Monthly.Run(second);

            // -5 (sensitivity 1.0) -> -5, then the drift: -5 + 0.5 = -4.5
            Assert.Near(-4.5, rig.Ledger.Get(AttitudeRig.Toshiba), 1e-4);
        }

        // ---- housekeeping and failures ----

        [Test]
        public void TheMonthlyPass_RemovesTheCautionsAndCooldownsThatRanOut()
        {
            AttitudeRig rig = RigAt(May);
            rig.State.cautions.Add(new CautionEntry { uid = 4, flawId = 1, until = "1990-04-01" });   // over
            rig.State.cautions.Add(new CautionEntry { uid = 5, flawId = 2, until = "1991-01-01" });   // still on
            rig.State.undercutCooldowns.Add(new UndercutCooldown { uid = 1, segment = 1, until = "1990-04-30" });   // over
            rig.State.undercutCooldowns.Add(new UndercutCooldown { uid = 2, segment = 1, until = "1990-09-01" });   // still on

            MonthlyResult result = rig.Monthly.Run(Input(May));

            Assert.Equal(2, result.Expired);
            Assert.Equal(1, rig.State.cautions.Count);
            Assert.Equal(5, rig.State.cautions[0].uid);
            Assert.Equal(1, rig.State.undercutCooldowns.Count);
            Assert.Equal(2, rig.State.undercutCooldowns[0].uid);
        }

        [Test]
        public void AFailingStep_IsLogged_AndTheOthersStillRun()
        {
            AttitudeRig rig = RigAt(May).SetAttitude(AttitudeRig.Toshiba, -40f);
            var log = new List<string>();
            var core = new MonthlyEvaluatorCore(() => { throw new InvalidOperationException("no state"); }, () => rig.Catalog.Config, rig.Recorder, rig.Ledger, log.Add);
            MonthlyInput input = Input(May);
            input.ContractRivals.Add(AttitudeRig.Ibm);

            MonthlyResult result = core.Run(input);

            Assert.Equal(3, result.Failures, "shares, undercut and housekeeping read the state of the core");
            Assert.Equal(3, log.Count);
            Assert.True(log[0].Contains("shares"), log[0]);
            Assert.Equal(1, result.ContractBonuses, "the contract step ran");
            Assert.Near(-36.0, rig.Ledger.Get(AttitudeRig.Toshiba), 1e-4, "and so did the drift");
        }

        [Test]
        public void Run_WithoutInput_DoesNothing()
        {
            AttitudeRig rig = RigAt(May);

            MonthlyResult result = rig.Monthly.Run(null);

            Assert.Equal(0, result.Failures + result.ShareGains + result.Undercuts + result.ContractBonuses + result.Drifted + result.Expired);
            Assert.False(result.ComparedShares);
        }

        [Test]
        public void Run_WithEmptyInput_Works_AndNothingIsBlockedByAMissingList()
        {
            AttitudeRig rig = RigAt(May);
            var input = new MonthlyInput { Today = May };

            MonthlyResult result = rig.Monthly.Run(input);

            Assert.Equal(0, result.Failures);
            Assert.Equal(0, rig.State.shareSnapshot.rows.Count);
        }

        [Test]
        public void WhileBlocked_NoTriggerIsRecorded_ButTheSnapshotAndTheDriftGoOn()
        {
            AttitudeRig rig = RigAt(May);
            rig.Monthly.Run(Input(May, new SalesRow("player", 10, 0, 0, 0), new SalesRow("ai:0", 90, 0, 0, 0)));
            rig.Today = June;
            rig.Blocked = true;
            rig.SetAttitude(AttitudeRig.Toshiba, -40f);

            MonthlyInput second = Input(June, new SalesRow("player", 20, 0, 0, 0), new SalesRow("ai:0", 80, 0, 0, 0));
            second.Rivals.Add(Rival(AttitudeRig.Intel));
            second.ContractRivals.Add(AttitudeRig.Ibm);
            MonthlyResult result = rig.Monthly.Run(second);

            Assert.Equal(0, result.ShareGains);
            Assert.Equal(0, result.ContractBonuses);
            Assert.Equal(0, rig.Triggers.Count);
            Assert.Near(-36.0, rig.Ledger.Get(AttitudeRig.Toshiba), 1e-4, "the drift is the ledger's, not a trigger");
        }

        [Test]
        public void ThousandCpus_AreJudgedQuickly_AndRowsOfOneCompanyAreAddedUp()
        {
            // The aim of TSK-012 is 3 ms for 1000 CPUs; the game side measures it in the debug log. Here the judging part must not grow faster than
            // the number of rows (a generous bound, it only catches a quadratic slip).
            AttitudeRig rig = RigAt(May);
            MonthlyInput first = Input(May);
            MonthlyInput second = Input(June);
            for (int i = 0; i < 1000; i++)
            {
                int company = i % 13;
                first.Sales.Add(new SalesRow("ai:" + company, 10, 5, 3, 1));
                second.Sales.Add(new SalesRow("ai:" + company, 11, 5, 3, 1));
            }
            first.Sales.Add(new SalesRow("player", 10, 0, 0, 0));
            second.Sales.Add(new SalesRow("player", 400, 0, 0, 0));
            for (int uid = 0; uid <= 12; uid++) second.Rivals.Add(Rival(uid));
            rig.Monthly.Run(first);
            rig.Today = June;

            var clock = System.Diagnostics.Stopwatch.StartNew();
            MonthlyResult result = rig.Monthly.Run(second);
            clock.Stop();

            Assert.True(result.ComparedShares);
            Assert.Equal(14, rig.State.shareSnapshot.rows.Count, "13 rivals and the player");
            Assert.Equal(13, result.ShareGains, "the player took a lot of High End from everyone");
            Assert.True(clock.ElapsedMilliseconds < 250, "1000 rows took " + clock.ElapsedMilliseconds + " ms");
        }

        // ---- the small books ----

        [Test]
        public void MonthNumber_CountsMonthsAcrossTheYears()
        {
            Assert.Equal(1, ShareSnapshots.MonthNumber(new DateTime(1990, 6, 15)) - ShareSnapshots.MonthNumber(new DateTime(1990, 5, 1)));
            Assert.Equal(1, ShareSnapshots.MonthNumber(new DateTime(1990, 1, 1)) - ShareSnapshots.MonthNumber(new DateTime(1989, 12, 1)));
            Assert.Equal(1990 * 12 + 3, ShareSnapshots.MonthNumber(new DateTime(1990, 5, 1)), "the last full month on the first of May is April");
        }

        [Test]
        public void Snapshot_RoundTripsThroughATable_InTheOrderOfTheKeys()
        {
            var table = new Dictionary<string, float[]>
            {
                { "player", new[] { 0.1f, 0.2f, 0.3f, 0.4f } },
                { "ai:12", new[] { 0.5f, 0f, 0f, 0f } },
                { "ai:3", new[] { 0.4f, 0f, 0f, 0f } },
            };

            ShareSnapshot snapshot = ShareSnapshots.FromTable(23883, table);

            Assert.Equal(23883, snapshot.monthIndex);
            Assert.Equal("ai:12", snapshot.rows[0].key);
            Assert.Equal("ai:3", snapshot.rows[1].key);
            Assert.Equal("player", snapshot.rows[2].key);
            Dictionary<string, float[]> back = ShareSnapshots.ToTable(snapshot);
            Assert.Equal(3, back.Count);
            Assert.Equal(0.3f, back["player"][2]);
            Assert.True(ShareSnapshots.IsComparable(snapshot, 23884));
            Assert.False(ShareSnapshots.IsComparable(snapshot, 23885), "two months later");
            Assert.False(ShareSnapshots.IsComparable(new ShareSnapshot(), 1), "no rows");
            Assert.False(ShareSnapshots.IsComparable(null, 1));
            Assert.Equal(0, ShareSnapshots.ToTable(null).Count);
        }

        [Test]
        public void UndercutBook_TracksTheCooldownOfAPair()
        {
            var state = new RivalState();
            DateTime day = new DateTime(1990, 5, 1);

            Assert.False(UndercutBook.IsCoolingDown(state, 1, Segment.MidRange, day));

            UndercutBook.Start(state, 1, Segment.MidRange, day, 90);
            Assert.True(UndercutBook.IsCoolingDown(state, 1, Segment.MidRange, day));
            Assert.True(UndercutBook.IsCoolingDown(state, 1, Segment.MidRange, new DateTime(1990, 7, 29)));
            Assert.False(UndercutBook.IsCoolingDown(state, 1, Segment.MidRange, new DateTime(1990, 7, 30)), "on the day it runs out the pair counts again");
            Assert.False(UndercutBook.IsCoolingDown(state, 1, Segment.LowEnd, day), "another segment");
            Assert.False(UndercutBook.IsCoolingDown(state, 2, Segment.MidRange, day), "another rival");

            UndercutBook.Start(state, 1, Segment.MidRange, new DateTime(1990, 8, 1), 90);
            Assert.Equal(1, state.undercutCooldowns.Count, "the same pair has one entry");
            Assert.Equal("1990-10-30", state.undercutCooldowns[0].until);
        }

        [Test]
        public void UndercutBook_PrunesWhatRanOut_AndWhatCannotBeRead()
        {
            var state = new RivalState();
            state.undercutCooldowns.Add(new UndercutCooldown { uid = 1, segment = 0, until = "1990-04-01" });
            state.undercutCooldowns.Add(new UndercutCooldown { uid = 2, segment = 0, until = "1990-12-01" });
            state.undercutCooldowns.Add(new UndercutCooldown { uid = 3, segment = 0, until = "garbage" });
            state.undercutCooldowns.Add(null);

            int removed = UndercutBook.Prune(state, new DateTime(1990, 5, 1));

            Assert.Equal(3, removed);
            Assert.Equal(1, state.undercutCooldowns.Count);
            Assert.Equal(2, state.undercutCooldowns[0].uid);
            Assert.Equal(0, UndercutBook.Prune(null, May));
            UndercutBook.Start(null, 1, Segment.MidRange, May, 90);   // no state: nothing happens
        }

        [Test]
        public void CautionCleaner_RemovesTheCautionsFromTheDayTheyEnd()
        {
            var state = new RivalState();
            state.cautions.Add(new CautionEntry { uid = 1, until = "1990-05-01" });   // ends today: gone ("while today < until")
            state.cautions.Add(new CautionEntry { uid = 2, until = "1990-05-02" });
            state.cautions.Add(new CautionEntry { uid = 3, until = "" });             // unreadable
            state.cautions.Add(new CautionEntry { uid = 4, until = "1992-01-01" });

            int removed = CautionCleaner.RemoveExpired(state, May);

            Assert.Equal(2, removed);
            Assert.Equal(2, state.cautions.Count);
            Assert.Equal(2, state.cautions[0].uid);
            Assert.Equal(4, state.cautions[1].uid);
            Assert.Equal(0, CautionCleaner.RemoveExpired(null, May));
        }
    }
}
