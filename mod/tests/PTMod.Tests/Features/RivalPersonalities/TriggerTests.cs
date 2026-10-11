using System;
using System.Collections.Generic;
using PTMod.Core;
using PTMod.Features.RivalPersonalities;

namespace PTMod.Tests.Features.RivalPersonalities
{
    /// <summary>
    /// The triggers of the player's moves (TSK-011; design.md Component 7; FR-5 items 1, 4, 5, 6, 8): the thresholds, the guards against a load and the
    /// fast-forward, which rivals notice a launch, a technology, a lost tender, a contract, the deltas on the attitude, and the single subscription
    /// to the contract manager of a scene. The game side (<c>PlayerActionMonitor</c>, <c>TriggerPatches</c>) only reads the game and hands these
    /// classes the facts.
    /// </summary>
    [Covers(typeof(TriggerRules), typeof(PlayerTrigger), typeof(TriggerRecorder), typeof(ContractMove), typeof(TriggerEvaluator),
            typeof(RivalFacts), typeof(LaunchFacts), typeof(SingleSubscription<>))]
    public class TriggerTests
    {
        private static LaunchFacts Launch(Segment segment, float ips, string name = "Zeta 1")
        {
            return new LaunchFacts { ByPlayer = true, Segment = segment, Ips = ips, CpuName = name };
        }

        private static RivalFacts Rival(int uid, bool active = true, bool sells = true, float bestIps = 100f)
        {
            return new RivalFacts { UniqueId = uid, ActiveInSegment = active, SellsCpuInSegment = sells, BestIps = bestIps };
        }

        // ---- the rules ----

        [Test]
        public void Guard_BlocksALoad_TheFastForward_ACpuOfTheFastForward_AndAGameThatIsNotReady()
        {
            Assert.False(TriggerRules.IsBlocked(false, false, false, true), "the running game");
            Assert.True(TriggerRules.IsBlocked(true, false, false, true), "ModTicks.IsLoading");
            Assert.True(TriggerRules.IsBlocked(false, true, false, true), "ModTicks.IsFastForwarding");
            Assert.True(TriggerRules.IsBlocked(false, false, true, true), "duringFastForward of CpuReleased");
            Assert.True(TriggerRules.IsBlocked(false, false, false, false), "before GameReady");
        }

        [Test]
        public void StrongLaunch_IsDecidedByTheThresholdOf115()
        {
            // AC-5.1: 120 against 100 -> 120 >= 115
            Assert.True(TriggerRules.IsStrongLaunch(120f, true, 100f, 1.15f));
            Assert.True(TriggerRules.IsStrongLaunch(115f, true, 100f, 1.15f), "exactly 1.15 times counts");
            Assert.False(TriggerRules.IsStrongLaunch(114f, true, 100f, 1.15f));
            Assert.False(TriggerRules.IsStrongLaunch(100f, true, 100f, 1.15f));
            Assert.True(TriggerRules.IsStrongLaunch(50f, false, 0f, 1.15f), "the rival is active in the segment but sells no CPU there");
            Assert.True(TriggerRules.IsStrongLaunch(50f, true, 0f, 1.15f), "a best CPU without a performance is beaten too");
        }

        [Test]
        public void StrongLaunch_NeverComesFromAUselessNewCpu()
        {
            Assert.False(TriggerRules.IsStrongLaunch(float.NaN, true, 100f, 1.15f));
            Assert.False(TriggerRules.IsStrongLaunch(float.PositiveInfinity, true, 100f, 1.15f));
            Assert.False(TriggerRules.IsStrongLaunch(0f, false, 0f, 1.15f));
            Assert.False(TriggerRules.IsStrongLaunch(-5f, true, 100f, 1.15f));
        }

        [Test]
        public void PriceUndercut_NeedsNinetyPercentOfThePerformanceAtEightyFivePercentOfThePrice()
        {
            Assert.True(TriggerRules.IsPriceUndercut(90f, 85f, 100f, 100f, 0.90f, 0.85f), "exactly at both borders");
            Assert.True(TriggerRules.IsPriceUndercut(120f, 60f, 100f, 100f, 0.90f, 0.85f));
            Assert.False(TriggerRules.IsPriceUndercut(89f, 85f, 100f, 100f, 0.90f, 0.85f), "too slow");
            Assert.False(TriggerRules.IsPriceUndercut(90f, 86f, 100f, 100f, 0.90f, 0.85f), "not cheap enough");
            Assert.False(TriggerRules.IsPriceUndercut(float.NaN, 50f, 100f, 100f, 0.90f, 0.85f));
            Assert.False(TriggerRules.IsPriceUndercut(100f, 50f, 0f, 100f, 0.90f, 0.85f), "a rival CPU without a performance is no yardstick");
            Assert.False(TriggerRules.IsPriceUndercut(100f, 0f, 100f, 100f, 0.90f, 0.85f), "a CPU without a price is not for sale");
        }

        [Test]
        public void ShareGain_NeedsThreePointsForThePlayer_AndALossForTheRival()
        {
            // AC-5.4: 10 % -> 14 %, the rival 60 % -> 55 %
            Assert.True(TriggerRules.IsShareGain(0.10f, 0.14f, 0.60f, 0.55f, 3f));
            Assert.True(TriggerRules.IsShareGain(0.10f, 0.13f, 0.60f, 0.55f, 3f), "exactly three points");
            Assert.False(TriggerRules.IsShareGain(0.10f, 0.129f, 0.60f, 0.55f, 3f), "2.9 points");
            Assert.False(TriggerRules.IsShareGain(0.10f, 0.14f, 0.60f, 0.60f, 3f), "the rival did not lose");
            Assert.False(TriggerRules.IsShareGain(0.10f, 0.14f, 0.50f, 0.55f, 3f), "the rival gained too");
            Assert.False(TriggerRules.IsShareGain(0.10f, 0.14f, 0f, 0f, 3f), "a rival with no share has nothing to lose");
            Assert.False(TriggerRules.IsShareGain(float.NaN, 0.14f, 0.6f, 0.5f, 3f));
            Assert.True(TriggerRules.PlayerShareGrew(0.10f, 0.14f, 3f));
            Assert.False(TriggerRules.PlayerShareGrew(0.14f, 0.10f, 3f));
        }

        [Test]
        public void TechLead_IsOnlyForLithographyFrequencyAndMulticore()
        {
            var types = new List<string> { "Lithography", "Frequency", "Multicore" };

            Assert.True(TriggerRules.IsTechLeadType("Lithography", types));
            Assert.True(TriggerRules.IsTechLeadType("Frequency", types));
            Assert.True(TriggerRules.IsTechLeadType("Multicore", types));
            Assert.False(TriggerRules.IsTechLeadType("Memory", types));
            Assert.False(TriggerRules.IsTechLeadType("Package", types));
            Assert.False(TriggerRules.IsTechLeadType("lithography", types), "the spelling is exact");
            Assert.False(TriggerRules.IsTechLeadType(null, types));
            Assert.False(TriggerRules.IsTechLeadType("Frequency", null));
        }

        // ---- the recorder: the deltas ----

        [Test]
        public void Record_StrongLaunch_ChangesTheAttitudeBy8TimesTheSensitivity_AndRaisesTheTrigger()
        {
            // AC-5.1: AMD -8 x 1.3
            AttitudeRig rig = new AttitudeRig().Assign(AttitudeRig.Amd);

            Assert.True(rig.Recorder.Record(TriggerType.StrongLaunch, AttitudeRig.Amd, Segment.MidRange, "Zeta 1"));

            Assert.Near(-25.4, rig.Ledger.Get(AttitudeRig.Amd), 1e-4);
            Assert.Equal("StrongLaunch:1", rig.Ledger.History(AttitudeRig.Amd)[0].reason);
            Assert.Equal(1, rig.Triggers.Count);
            Assert.Equal(TriggerType.StrongLaunch, rig.Triggers[0].Type);
            Assert.Equal(AttitudeRig.Amd, rig.Triggers[0].UniqueId);
            Assert.Equal(Segment.MidRange, rig.Triggers[0].Segment);
            Assert.Equal("Zeta 1", rig.Triggers[0].Detail);
            Assert.Equal(1, rig.Recorder.Recorded);
            Assert.Equal(1, rig.LevelEvents.Count, "AC-4.1: the level changes to Rivalry with the notification");
        }

        [Test]
        public void Record_EveryTrigger_UsesItsDeltaFromTheConfig()
        {
            // Toshiba: sensitivity 1.0, so the change is the delta itself
            var expected = new Dictionary<TriggerType, float>
            {
                { TriggerType.StrongLaunch, -8f },
                { TriggerType.ShareGain, -5f },
                { TriggerType.PriceUndercut, -4f },
                { TriggerType.OemLoss, -6f },
                { TriggerType.TechLead, -3f },
            };

            foreach (KeyValuePair<TriggerType, float> pair in expected)
            {
                AttitudeRig rig = new AttitudeRig().Assign(AttitudeRig.Toshiba);
                Assert.Equal(pair.Value, rig.Recorder.DeltaOf(pair.Key), pair.Key + " in the config");

                rig.Recorder.Record(pair.Key, AttitudeRig.Toshiba, Segment.HighEnd, null);
                Assert.Near(pair.Value, rig.Ledger.Get(AttitudeRig.Toshiba), 1e-5, pair.Key.ToString());
            }
        }

        [Test]
        public void Record_TakesTheDeltasFromTheContentNotFromTheCode()
        {
            AttitudeRig rig = new AttitudeRig().Assign(AttitudeRig.Toshiba);
            rig.Catalog.Config.triggerDeltas.StrongLaunch = -20f;

            rig.Recorder.Record(TriggerType.StrongLaunch, AttitudeRig.Toshiba, Segment.LowEnd, null);

            Assert.Near(-20f, rig.Ledger.Get(AttitudeRig.Toshiba), 1e-5);
        }

        [Test]
        public void Record_WithoutASegment_WritesOnlyTheNameOfTheTriggerAsTheReason()
        {
            AttitudeRig rig = new AttitudeRig().Assign(AttitudeRig.Toshiba);

            rig.Recorder.Record(TriggerType.TechLead, AttitudeRig.Toshiba, Segment.Any, "Lithography 1.0");

            Assert.Equal("TechLead", rig.Ledger.History(AttitudeRig.Toshiba)[0].reason);
            Assert.Equal("TechLead", TriggerRecorder.ReasonOf(TriggerType.TechLead, Segment.Any));
            Assert.Equal("ShareGain:3", TriggerRecorder.ReasonOf(TriggerType.ShareGain, Segment.Industries));
        }

        [Test]
        public void ContractMoves_UseTheirDeltas_AndRaiseNoTrigger()
        {
            // FR-5 item 6: +15 signed, -25 broken by the player, +2 a month for an active contract
            AttitudeRig rig = new AttitudeRig().Assign(AttitudeRig.Toshiba);

            Assert.True(rig.Recorder.RecordContract(ContractMove.Signed, AttitudeRig.Toshiba));
            Assert.Near(15f, rig.Ledger.Get(AttitudeRig.Toshiba), 1e-5);

            Assert.True(rig.Recorder.RecordContract(ContractMove.BrokenByPlayer, AttitudeRig.Toshiba));
            Assert.Near(-10f, rig.Ledger.Get(AttitudeRig.Toshiba), 1e-5);

            Assert.True(rig.Recorder.RecordContract(ContractMove.ActiveMonthly, AttitudeRig.Toshiba));
            Assert.Near(-8f, rig.Ledger.Get(AttitudeRig.Toshiba), 1e-5);

            IReadOnlyList<AttitudeEntry> history = rig.Ledger.History(AttitudeRig.Toshiba);
            Assert.Equal("BusinessSigned", history[0].reason);
            Assert.Equal("BusinessBrokenByPlayer", history[1].reason);
            Assert.Equal("BusinessActiveMonthly", history[2].reason);
            Assert.Equal(0, rig.Triggers.Count, "a contract is no threat to answer");
            Assert.Equal(15f, rig.Recorder.DeltaOf(ContractMove.Signed));
            Assert.Equal(-25f, rig.Recorder.DeltaOf(ContractMove.BrokenByPlayer));
            Assert.Equal(2f, rig.Recorder.DeltaOf(ContractMove.ActiveMonthly));
        }

        [Test]
        public void ContractMoves_GoToTheDebugLog_ButTriggersToTheNormalOne()
        {
            AttitudeRig rig = new AttitudeRig().Assign(AttitudeRig.Toshiba);
            var info = new List<string>();
            var debug = new List<string>();
            var recorder = new TriggerRecorder(rig.Ledger, rig.Book, () => rig.Catalog.Config, () => false, info.Add, debug.Add);

            recorder.RecordContract(ContractMove.ActiveMonthly, AttitudeRig.Toshiba);
            Assert.Equal(0, info.Count, "a monthly bonus is not worth a line in the normal log");
            Assert.Equal(1, debug.Count);

            recorder.Record(TriggerType.StrongLaunch, AttitudeRig.Toshiba, Segment.MidRange, "Zeta 1");
            Assert.Equal(1, info.Count);
            Assert.True(info[0].Contains("StrongLaunch:1") && info[0].Contains("Zeta 1"), info[0]);
        }

        [Test]
        public void ContractMoves_UseTheSensitivityToo()
        {
            AttitudeRig rig = new AttitudeRig().Assign(AttitudeRig.Tsmc);   // sensitivity 0.5, baseline +20

            rig.Recorder.RecordContract(ContractMove.Signed, AttitudeRig.Tsmc);

            Assert.Near(27.5, rig.Ledger.Get(AttitudeRig.Tsmc), 1e-4);
        }

        // ---- the recorder: what is not recorded ----

        [Test]
        public void Record_WhileBlocked_ChangesNothingAndRaisesNothing()
        {
            AttitudeRig rig = new AttitudeRig().Assign(AttitudeRig.Amd);
            rig.Blocked = true;

            Assert.True(rig.Recorder.IsBlocked);
            Assert.False(rig.Recorder.Record(TriggerType.StrongLaunch, AttitudeRig.Amd, Segment.MidRange, "x"));
            Assert.False(rig.Recorder.RecordContract(ContractMove.Signed, AttitudeRig.Amd));

            Assert.Near(-15f, rig.Ledger.Get(AttitudeRig.Amd), 1e-5);
            Assert.Equal(0, rig.Triggers.Count);
            Assert.Equal(0, rig.Recorder.Recorded);
            Assert.Equal(0, rig.Ledger.History(AttitudeRig.Amd).Count);
        }

        [Test]
        public void Record_ForADormantCompany_ChangesNothing()
        {
            AttitudeRig rig = new AttitudeRig().Assign(AttitudeRig.Amd);
            rig.State.dormant.Add(AttitudeRig.Amd);

            Assert.False(rig.Recorder.Record(TriggerType.StrongLaunch, AttitudeRig.Amd, Segment.MidRange, null));
            Assert.False(rig.Recorder.RecordContract(ContractMove.Signed, AttitudeRig.Amd));

            Assert.Near(-15f, rig.Ledger.Get(AttitudeRig.Amd), 1e-5);
            Assert.Equal(0, rig.Triggers.Count);
        }

        [Test]
        public void Record_ForABankruptOrUnknownCompany_ChangesNothing()
        {
            AttitudeRig rig = new AttitudeRig().Assign(AttitudeRig.Amd);
            rig.Bankrupt.Add(AttitudeRig.Amd);

            Assert.False(rig.Recorder.Record(TriggerType.StrongLaunch, AttitudeRig.Amd, Segment.MidRange, null));
            Assert.False(rig.Recorder.Record(TriggerType.StrongLaunch, 99, Segment.MidRange, null), "no archetype");

            Assert.Equal(0, rig.Triggers.Count);
            Assert.Equal(0, rig.Recorder.Recorded);
        }

        [Test]
        public void Record_AtTheEdgeOfTheScale_StillRaisesTheTrigger()
        {
            // a rival that already hates the player must still be able to answer the next launch
            AttitudeRig rig = new AttitudeRig().Assign(AttitudeRig.Amd).SetAttitude(AttitudeRig.Amd, -100f);

            Assert.True(rig.Recorder.Record(TriggerType.StrongLaunch, AttitudeRig.Amd, Segment.MidRange, null));

            Assert.Equal(-100f, rig.Ledger.Get(AttitudeRig.Amd));
            Assert.Equal(1, rig.Triggers.Count);
        }

        [Test]
        public void AFailingHandlerOfTriggerRaised_DoesNotStopTheNextOne()
        {
            AttitudeRig rig = new AttitudeRig().Assign(AttitudeRig.Amd);
            int calls = 0;
            rig.Recorder.TriggerRaised += t => { throw new InvalidOperationException("planner failed"); };
            rig.Recorder.TriggerRaised += t => calls++;

            Assert.True(rig.Recorder.Record(TriggerType.StrongLaunch, AttitudeRig.Amd, Segment.MidRange, null));

            Assert.Equal(1, calls);
            Assert.True(rig.Log.Exists(l => l.Contains("planner failed")), "the failure is logged: " + string.Join("; ", rig.Log));
        }

        // ---- the evaluator: a launch ----

        [Test]
        public void Launch_BeatsOnlyTheRivalsItClearlyOutperforms()
        {
            // AC-5.1: AMD's best Mid Range CPU has Ips 100, the player's has 120 -> StrongLaunch for AMD. Intel's best is 200 -> nothing.
            AttitudeRig rig = new AttitudeRig().AssignAll();
            var rivals = new List<RivalFacts> { Rival(AttitudeRig.Amd, bestIps: 100f), Rival(AttitudeRig.Intel, bestIps: 200f) };

            int count = rig.Evaluator.OnLaunch(Launch(Segment.MidRange, 120f), rivals);

            Assert.Equal(1, count);
            Assert.Equal(1, rig.Triggers.Count);
            Assert.Equal(AttitudeRig.Amd, rig.Triggers[0].UniqueId);
            Assert.Equal(Segment.MidRange, rig.Triggers[0].Segment);
            Assert.Near(-25.4, rig.Ledger.Get(AttitudeRig.Amd), 1e-4);
            Assert.Near(-10f, rig.Ledger.Get(AttitudeRig.Intel), 1e-5, "Intel did not notice");
        }

        [Test]
        public void Launch_IgnoresRivalsNotActiveInTheSegment_Foundries_AndBankrupts()
        {
            AttitudeRig rig = new AttitudeRig().AssignAll();
            var rivals = new List<RivalFacts>
            {
                Rival(AttitudeRig.Amd, active: false, sells: false),
                new RivalFacts { UniqueId = AttitudeRig.Tsmc, IsFoundry = true, ActiveInSegment = true, SellsCpuInSegment = true, BestIps = 10f },
                new RivalFacts { UniqueId = AttitudeRig.Intel, IsBankrupt = true, ActiveInSegment = true, SellsCpuInSegment = true, BestIps = 10f },
                null,
            };

            Assert.Equal(0, rig.Evaluator.OnLaunch(Launch(Segment.MidRange, 500f), rivals));
            Assert.Equal(0, rig.Triggers.Count);
        }

        [Test]
        public void Launch_AgainstARivalThatSellsNothingThereYet_IsStrong()
        {
            AttitudeRig rig = new AttitudeRig().AssignAll();
            var rivals = new List<RivalFacts> { Rival(AttitudeRig.Amd, active: true, sells: false, bestIps: 0f) };

            Assert.Equal(1, rig.Evaluator.OnLaunch(Launch(Segment.LowEnd, 30f), rivals));
        }

        [Test]
        public void Launch_DuringTheFastForward_IsNotRecorded()
        {
            // AC-5.5: duringFastForward = true
            AttitudeRig rig = new AttitudeRig().AssignAll();
            LaunchFacts launch = Launch(Segment.MidRange, 120f);
            launch.DuringFastForward = true;

            Assert.Equal(0, rig.Evaluator.OnLaunch(launch, new List<RivalFacts> { Rival(AttitudeRig.Amd) }));
            Assert.Equal(0, rig.Triggers.Count);
            Assert.Near(-15f, rig.Ledger.Get(AttitudeRig.Amd), 1e-5);
        }

        [Test]
        public void Launch_WhileASaveLoadsOrTheGameIsNotReady_IsNotRecorded()
        {
            // AC-5.2 and the guard of the monitor: ModTicks.IsLoading / IsFastForwarding / not ready all come through the same test
            AttitudeRig rig = new AttitudeRig().AssignAll();
            rig.Blocked = true;

            Assert.True(rig.Evaluator.IsBlocked);
            Assert.Equal(0, rig.Evaluator.OnLaunch(Launch(Segment.MidRange, 120f), new List<RivalFacts> { Rival(AttitudeRig.Amd) }));
            Assert.Equal(0, rig.Evaluator.OnTechnology("Lithography", "Lithography 1.0", new List<RivalFacts> { new RivalFacts { UniqueId = AttitudeRig.Amd, LacksTechnology = true } }));
            Assert.Equal(0, rig.Evaluator.OnOemContractWon(new List<RivalFacts> { new RivalFacts { UniqueId = AttitudeRig.Intel } }, "Dell"));
            Assert.False(rig.Evaluator.OnBusinessContractSigned(AttitudeRig.Tsmc));
            Assert.False(rig.Evaluator.OnBusinessContractBroken(AttitudeRig.Tsmc));
            Assert.Equal(0, rig.Triggers.Count);
            Assert.Near(-15f, rig.Ledger.Get(AttitudeRig.Amd), 1e-5);
            Assert.Near(20f, rig.Ledger.Get(AttitudeRig.Tsmc), 1e-5);
        }

        [Test]
        public void Launch_OfAVariantOrOfARival_IsNotAStrongLaunch()
        {
            AttitudeRig rig = new AttitudeRig().AssignAll();
            var rivals = new List<RivalFacts> { Rival(AttitudeRig.Amd) };

            LaunchFacts variant = Launch(Segment.MidRange, 120f);
            variant.IsVariant = true;
            LaunchFacts others = Launch(Segment.MidRange, 120f);
            others.ByPlayer = false;

            Assert.Equal(0, rig.Evaluator.OnLaunch(variant, rivals), "a variant of an earlier design");
            Assert.Equal(0, rig.Evaluator.OnLaunch(others, rivals), "a CPU of an AI company");
            Assert.Equal(0, rig.Evaluator.OnLaunch(null, rivals));
            Assert.Equal(0, rig.Evaluator.OnLaunch(Launch(Segment.MidRange, 120f), null));
        }

        [Test]
        public void Launch_InAnUnknownSegment_GivesNoTriggerAndAWarning()
        {
            AttitudeRig rig = new AttitudeRig().AssignAll();

            int count = rig.Evaluator.OnLaunch(Launch(Segment.Any, 500f, "Zeta 9"), new List<RivalFacts> { Rival(AttitudeRig.Amd) });

            Assert.Equal(0, count);
            Assert.Equal(1, rig.Warnings.Count);
            Assert.True(rig.Warnings[0].Contains("Zeta 9"), rig.Warnings[0]);
            Assert.Near(-15f, rig.Ledger.Get(AttitudeRig.Amd), 1e-5);
        }

        [Test]
        public void Launch_UsesTheRatioOfTheConfig()
        {
            AttitudeRig rig = new AttitudeRig().AssignAll();
            rig.Catalog.Config.triggerThresholds.strongLaunchIpsRatio = 1.5f;

            Assert.Equal(0, rig.Evaluator.OnLaunch(Launch(Segment.MidRange, 120f), new List<RivalFacts> { Rival(AttitudeRig.Amd) }), "120 < 150");
            Assert.Equal(1, rig.Evaluator.OnLaunch(Launch(Segment.MidRange, 150f), new List<RivalFacts> { Rival(AttitudeRig.Amd) }));
        }

        // ---- the evaluator: a technology ----

        [Test]
        public void Technology_OfALeadingType_GivesTechLeadToTheRivalsThatLackIt()
        {
            AttitudeRig rig = new AttitudeRig().AssignAll();
            var rivals = new List<RivalFacts>
            {
                new RivalFacts { UniqueId = AttitudeRig.Amd, LacksTechnology = true },
                new RivalFacts { UniqueId = AttitudeRig.Intel, LacksTechnology = false },
                new RivalFacts { UniqueId = AttitudeRig.Tsmc, LacksTechnology = true, IsFoundry = true },
            };

            int count = rig.Evaluator.OnTechnology("Lithography", "Lithography 2.0", rivals);

            Assert.Equal(1, count);
            Assert.Equal(AttitudeRig.Amd, rig.Triggers[0].UniqueId);
            Assert.Equal(TriggerType.TechLead, rig.Triggers[0].Type);
            Assert.Equal(Segment.Any, rig.Triggers[0].Segment);
            Assert.Equal("Lithography 2.0", rig.Triggers[0].Detail);
            Assert.Near(-15f - 3f * 1.3f, rig.Ledger.Get(AttitudeRig.Amd), 1e-4);
            Assert.Near(20f, rig.Ledger.Get(AttitudeRig.Tsmc), 1e-5, "a foundry is not a rival in technology");
        }

        [Test]
        public void Technology_OfAnotherType_GivesNothing()
        {
            AttitudeRig rig = new AttitudeRig().AssignAll();
            var rivals = new List<RivalFacts> { new RivalFacts { UniqueId = AttitudeRig.Amd, LacksTechnology = true } };

            Assert.Equal(0, rig.Evaluator.OnTechnology("Memory", "DRAM", rivals));
            Assert.Equal(0, rig.Evaluator.OnTechnology("Package", "DIP", rivals));
            Assert.Equal(0, rig.Evaluator.OnTechnology(null, "?", rivals));
            Assert.Equal(0, rig.Triggers.Count);
        }

        [Test]
        public void Technology_TypesComeFromTheConfig()
        {
            AttitudeRig rig = new AttitudeRig().AssignAll();
            rig.Catalog.Config.triggerThresholds.techLeadTypes = new List<string> { "Memory" };
            var rivals = new List<RivalFacts> { new RivalFacts { UniqueId = AttitudeRig.Amd, LacksTechnology = true } };

            Assert.Equal(1, rig.Evaluator.OnTechnology("Memory", "DRAM", rivals));
            Assert.Equal(0, rig.Evaluator.OnTechnology("Lithography", "x", rivals));
            Assert.Equal(1, rig.Evaluator.TechLeadTypes.Count);
        }

        // ---- the evaluator: a tender and the contracts ----

        [Test]
        public void OemContract_GivesOemLossToEveryRivalThatOffered_OnlyThose()
        {
            // AC-5.3: the offers were the player's CPU and Intel's; the caller hands over the rivals that offered
            AttitudeRig rig = new AttitudeRig().AssignAll();
            var offerers = new List<RivalFacts> { new RivalFacts { UniqueId = AttitudeRig.Intel, OfferSegment = Segment.LowEnd } };

            int count = rig.Evaluator.OnOemContractWon(offerers, "Dell");

            Assert.Equal(1, count);
            Assert.Equal(AttitudeRig.Intel, rig.Triggers[0].UniqueId);
            Assert.Equal(TriggerType.OemLoss, rig.Triggers[0].Type);
            Assert.Equal(Segment.LowEnd, rig.Triggers[0].Segment);
            Assert.Equal("Dell", rig.Triggers[0].Detail);
            Assert.Near(-10f - 6f * 1.2f, rig.Ledger.Get(AttitudeRig.Intel), 1e-4);
            Assert.Near(-15f, rig.Ledger.Get(AttitudeRig.Amd), 1e-5, "AMD did not offer");
        }

        [Test]
        public void BusinessContract_SignedGivesPlus15_BrokenGivesMinus25()
        {
            AttitudeRig rig = new AttitudeRig().AssignAll();

            Assert.True(rig.Evaluator.OnBusinessContractSigned(AttitudeRig.Ibm));
            Assert.Near(12f, rig.Ledger.Get(AttitudeRig.Ibm), 1e-4, "+15 x 0.8");

            Assert.True(rig.Evaluator.OnBusinessContractBroken(AttitudeRig.Ibm));
            Assert.Near(12f - 20f, rig.Ledger.Get(AttitudeRig.Ibm), 1e-4, "-25 x 0.8");
        }

        // ---- the single subscription ----

        private sealed class FakeManager
        {
            public event Action<int> Signed;

            public int Handlers
            {
                get { return Signed == null ? 0 : Signed.GetInvocationList().Length; }
            }

            public void Raise(int value)
            {
                Action<int> handlers = Signed;
                if (handlers != null) handlers(value);
            }
        }

        private static SingleSubscription<FakeManager> Subscription(List<int> heard)
        {
            Action<int> handler = heard.Add;
            return new SingleSubscription<FakeManager>(m => m.Signed += handler, m => m.Signed -= handler);
        }

        [Test]
        public void Subscription_AttachedTwiceToTheSameManager_ListensOnce()
        {
            var heard = new List<int>();
            SingleSubscription<FakeManager> subscription = Subscription(heard);
            var manager = new FakeManager();

            Assert.True(subscription.Attach(manager));
            Assert.False(subscription.Attach(manager), "the second attach is a no-op");
            manager.Raise(7);

            Assert.Equal(1, manager.Handlers);
            Assert.Equal(1, subscription.Active);
            Assert.Equal(1, subscription.Made);
            Assert.Equal(1, heard.Count);
        }

        [Test]
        public void Subscription_AfterAReloadOfTheScene_MovesToTheNewManager_WithoutDuplicates()
        {
            var heard = new List<int>();
            SingleSubscription<FakeManager> subscription = Subscription(heard);
            var scene1 = new FakeManager();
            var scene2 = new FakeManager();

            subscription.Attach(scene1);
            subscription.Detach();                   // SceneLeaving
            subscription.Attach(scene2);             // GameReady of the new scene
            subscription.Attach(scene2);             // ...and the feature switched on in the ready game

            Assert.Equal(0, scene1.Handlers);
            Assert.Equal(1, scene2.Handlers);
            Assert.Equal(1, subscription.Active, "the number the log shows");
            Assert.Equal(2, subscription.Made);

            scene2.Raise(3);
            Assert.Equal(1, heard.Count, "one signed contract is heard once");
        }

        [Test]
        public void Subscription_ANewManagerWithoutALeavingEvent_ReplacesTheOldOne()
        {
            var heard = new List<int>();
            SingleSubscription<FakeManager> subscription = Subscription(heard);
            var scene1 = new FakeManager();
            var scene2 = new FakeManager();

            subscription.Attach(scene1);
            Assert.True(subscription.Attach(scene2));

            Assert.Equal(0, scene1.Handlers, "the old manager is let go");
            Assert.Equal(1, scene2.Handlers);
            Assert.Equal(1, subscription.Active);
        }

        [Test]
        public void Subscription_DetachAndNull_LetGo_AndAFailingUnsubscribeIsHarmless()
        {
            var heard = new List<int>();
            SingleSubscription<FakeManager> subscription = Subscription(heard);
            var manager = new FakeManager();

            subscription.Attach(manager);
            Assert.False(subscription.Attach(null), "null lets go and subscribes to nothing");
            Assert.Equal(0, manager.Handlers);
            Assert.Equal(0, subscription.Active);
            subscription.Detach();   // nothing to do

            var broken = new SingleSubscription<FakeManager>(m => { }, m => { throw new InvalidOperationException("destroyed"); });
            broken.Attach(manager);
            broken.Detach();
            Assert.Equal(0, broken.Active, "the old scene's object may be gone");
        }

        [Test]
        public void PlayerTrigger_CarriesWhatItWasGiven()
        {
            var trigger = new PlayerTrigger(TriggerType.OemLoss, 3, Segment.Industries, "Dell");

            Assert.Equal(TriggerType.OemLoss, trigger.Type);
            Assert.Equal(3, trigger.UniqueId);
            Assert.Equal(Segment.Industries, trigger.Segment);
            Assert.Equal("Dell", trigger.Detail);
        }
    }
}
