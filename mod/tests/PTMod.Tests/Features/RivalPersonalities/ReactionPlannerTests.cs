using System;
using System.Collections.Generic;
using System.Linq;
using PTMod.Core;
using PTMod.Core.Logic.Json;
using PTMod.Features.RivalPersonalities;

namespace PTMod.Tests.Features.RivalPersonalities
{
    /// <summary>
    /// The planner of the reactions (TSK-013; design.md Component 8; FR-6, FR-7 items 1, 8, 9): the rolls in the order of the table, the limits of the
    /// honesty rules, the intelligence mail and the day the reaction is carried out, which starts at the delivery of the mail. The game is a fake
    /// (<see cref="FakeEnv"/>), the dice are scripted (<see cref="ScriptedRng"/>), the content is the shipped one.
    /// </summary>
    [Covers(typeof(ReactionPlannerCore), typeof(PlanResult), typeof(PlanStatus), typeof(TickReport), typeof(IPlannerEnvironment), typeof(IIntelOutput),
            typeof(IntelNotice), typeof(IReactionLauncher), typeof(LaunchOutcome))]
    public class ReactionPlannerTests
    {
        private const int Intel = AttitudeRig.Intel;     // hegemon
        private const int Amd = AttitudeRig.Amd;         // price_warrior
        private const int Ibm = AttitudeRig.Ibm;         // engineer

        // ---- the rolls ----

        [Test]
        public void StrongLaunch_AgainstThePriceWarrior_PlansAPriceCut_WithTheIntelMail()
        {
            var rig = new ReactionRig();
            rig.Base.SetAttitude(Amd, -25.4f);

            PendingReaction pending = rig.PlanOne(TriggerType.StrongLaunch, Amd, Segment.MidRange);

            Assert.Equal("PriceCut", pending.type);
            Assert.Equal(1, pending.segment);
            Assert.Equal(Amd, pending.uid);
            Assert.Equal("StrongLaunch", pending.trigger);
            Assert.Equal(RivalDates.ToText(rig.Env.Today), pending.plannedOn);
            Assert.Equal(100, pending.mailId, "the id the mail service gave is kept");
            Assert.InRange(pending.telegraphDays, 14, 45);
            Assert.Equal(string.Empty, pending.fireOn, "the day of carrying out waits for the delivery of the mail");

            Assert.Equal(1, rig.Intel.Sent.Count);
            IntelNotice notice = rig.Intel.Sent[0];
            Assert.Equal(pending.id, notice.ReactionId);
            Assert.Equal(ReactionType.PriceCut, notice.Type);
            Assert.Equal(Segment.MidRange, notice.Segment);
            Assert.Equal(2, notice.WeeksFrom, "AC-7.1: 2-7 weeks");
            Assert.Equal(7, notice.WeeksTo);

            Assert.Equal(1, rig.Planner.Pending.Count);
            List<HistoryEntry> history = rig.Reactions.HistoryOf(Amd, 10);
            Assert.Equal(1, history.Count);
            Assert.Equal(HistoryOutcomes.Planned, history[0].outcome);
        }

        [Test]
        public void TheChance_IsTheFormulaOfTheDesign()
        {
            // AC-6.3: price_warrior, PriceCut base 0.40, attitude -50, difficulty 2: clamp(0.40 x 1.5 x 1.0) = 0.60
            var rig = new ReactionRig();
            rig.Base.SetAttitude(Amd, -50f);

            rig.PlanOne(TriggerType.StrongLaunch, Amd, Segment.MidRange);

            Assert.Equal(1, rig.Rng.Probabilities.Count);
            Assert.Near(0.60, rig.Rng.Probabilities[0], 1e-5);
        }

        [Test]
        public void TheDifficulty_ScalesTheChance_AndTheCapHolds()
        {
            var hard = new ReactionRig();
            hard.Base.SetAttitude(Amd, -50f);
            hard.Env.Difficulty = 5;
            hard.PlanOne(TriggerType.StrongLaunch, Amd, Segment.MidRange);
            Assert.Near(0.9, hard.Rng.Probabilities[0], 1e-5, "0.4 x 1.5 x 1.6 = 0.96 is held to the cap of 0.9");

            var easy = new ReactionRig();
            easy.Base.SetAttitude(Amd, -50f);
            easy.Env.Difficulty = 0;
            easy.PlanOne(TriggerType.StrongLaunch, Amd, Segment.MidRange);
            Assert.Near(0.24, easy.Rng.Probabilities[0], 1e-5, "0.4 x 1.5 x 0.4");
        }

        [Test]
        public void TheFirstSuccessfulRow_IsPlanned_AndTheRestAreNotRolled()
        {
            // hegemon, StrongLaunch: CounterLaunch 0.3, then OemPush 0.2
            var first = new ReactionRig();
            first.Base.SetAttitude(Intel, -30f);
            PendingReaction counter = first.PlanOne(TriggerType.StrongLaunch, Intel, Segment.HighEnd);
            Assert.Equal("CounterLaunch", counter.type);
            Assert.Equal(1, first.Rng.Probabilities.Count, "the second row is not considered");
            Assert.Near(0.39, first.Rng.Probabilities[0], 1e-5);

            var second = new ReactionRig();
            second.Base.SetAttitude(Intel, -30f);
            second.Rng.Chances.Enqueue(false);
            PendingReaction oem = second.PlanOne(TriggerType.StrongLaunch, Intel, Segment.HighEnd);
            Assert.Equal("OemPush", oem.type);
            Assert.Equal(2, second.Rng.Probabilities.Count);
            Assert.Near(0.26, second.Rng.Probabilities[1], 1e-5);
        }

        [Test]
        public void EveryRollMissed_PlansNothing()
        {
            var rig = new ReactionRig();
            rig.Base.SetAttitude(Intel, -30f);

            PlanResult result = rig.Planner.OnTrigger(rig.Trigger(TriggerType.StrongLaunch, Intel, Segment.HighEnd));

            Assert.Equal(PlanStatus.Missed, result.Status);
            Assert.Equal(2, result.Rolls);
            Assert.Equal(0, rig.Planner.Pending.Count);
            Assert.Equal(0, rig.Intel.Sent.Count);
            Assert.Equal(0, rig.Reactions.HistoryOf(Intel, 10).Count);
        }

        [Test]
        public void ATriggerWithoutARow_ForTheArchetype_PlansNothing()
        {
            var rig = new ReactionRig();
            rig.Rng.DefaultChance = true;

            // the foundry partner has no reactions at all; the price warrior has none for TechLead
            Assert.Equal(PlanStatus.NoRules, rig.Planner.OnTrigger(rig.Trigger(TriggerType.StrongLaunch, AttitudeRig.Tsmc, Segment.HighEnd)).Status);
            Assert.Equal(PlanStatus.NoRules, rig.Planner.OnTrigger(rig.Trigger(TriggerType.TechLead, Amd, Segment.Any)).Status);
            Assert.Equal(PlanStatus.NoArchetype, rig.Planner.OnTrigger(rig.Trigger(TriggerType.StrongLaunch, 99, Segment.HighEnd)).Status);
            Assert.Equal(0, rig.Rng.Probabilities.Count);
        }

        [Test]
        public void Poach_IsMoreLikelyWhenTheResearchOfThePlayerIsUnderfunded()
        {
            // engineer, TechLead: Poach 0.25; underfundedChanceMult 1.5 (formula 6)
            var plain = new ReactionRig();
            plain.Base.SetAttitude(Ibm, -30f);
            plain.PlanOne(TriggerType.TechLead, Ibm, Segment.Any);
            Assert.Near(0.325, plain.Rng.Probabilities[0], 1e-5);

            var poor = new ReactionRig();
            poor.Base.SetAttitude(Ibm, -30f);
            poor.Env.PlayerResearchUnderfunded = true;
            PendingReaction pending = poor.PlanOne(TriggerType.TechLead, Ibm, Segment.Any);
            Assert.Near(0.4875, poor.Rng.Probabilities[0], 1e-5);
            Assert.Equal(-1, pending.segment, "a poaching has no segment");
        }

        // ---- the limits ----

        [Test]
        public void TheGracePeriod_HoldsEverythingBack_ForTheFirst180Days()
        {
            // AC-6.1
            var rig = new ReactionRig();
            rig.Base.SetAttitude(Amd, -30f);
            rig.Rng.DefaultChance = true;
            rig.SetToday(ReactionRig.GameStart.AddDays(100));

            PlanResult result = rig.Planner.OnTrigger(rig.Trigger(TriggerType.StrongLaunch, Amd, Segment.MidRange));

            Assert.Equal(PlanStatus.Refused, result.Status);
            Assert.Equal(GateResult.GracePeriod, result.Gate);
            Assert.Equal(0, rig.Rng.Probabilities.Count, "nothing is rolled for a reaction that may not happen");
            Assert.Equal(0, rig.Intel.Sent.Count);

            rig.SetToday(ReactionRig.GameStart.AddDays(179));
            Assert.Equal(GateResult.GracePeriod, rig.Planner.OnTrigger(rig.Trigger(TriggerType.StrongLaunch, Amd, Segment.MidRange)).Gate);
            rig.SetToday(ReactionRig.GameStart.AddDays(180));
            Assert.True(rig.Planner.OnTrigger(rig.Trigger(TriggerType.StrongLaunch, Amd, Segment.MidRange)).IsPlanned, "day 180 is the first day it may happen");
        }

        [Test]
        public void TheCooldownOfTheArchetype_CountsFromTheLastReactionOfTheRival()
        {
            // price_warrior: 150 days
            var rig = new ReactionRig();
            rig.Base.SetAttitude(Amd, -30f);
            rig.Rng.DefaultChance = true;

            rig.Reactions.MarkExecuted(Amd, rig.Env.Today.AddDays(-100), true);
            Assert.Equal(GateResult.Cooldown, rig.Planner.OnTrigger(rig.Trigger(TriggerType.StrongLaunch, Amd, Segment.MidRange)).Gate);

            rig.Reactions.MarkExecuted(Amd, rig.Env.Today.AddDays(-150), true);
            Assert.True(rig.Planner.OnTrigger(rig.Trigger(TriggerType.StrongLaunch, Amd, Segment.MidRange)).IsPlanned, "150 days are over");
        }

        [Test]
        public void ARivalWithAReactionOnItsWay_IsInItsCooldown_OneAnswerAtATime()
        {
            var rig = new ReactionRig();
            rig.Base.SetAttitude(Amd, -30f);
            rig.PlanOne(TriggerType.StrongLaunch, Amd, Segment.MidRange);
            rig.Rng.DefaultChance = true;

            PlanResult second = rig.Planner.OnTrigger(rig.Trigger(TriggerType.PriceUndercut, Amd, Segment.MidRange));

            Assert.Equal(PlanStatus.Refused, second.Status);
            Assert.Equal(GateResult.Cooldown, second.Gate);
            Assert.Equal(1, rig.Planner.Pending.Count);
        }

        [Test]
        public void TwoHostileReactionsAtATime_IsTheCap()
        {
            // AC-6.2
            var rig = new ReactionRig();
            rig.Base.SetAttitude(Amd, -30f);
            rig.Reactions.BeginActive("r-90", Intel, ReactionType.OemPush, -1, rig.Env.Today, rig.Env.Today.AddDays(365));
            rig.Reactions.BeginActive("r-91", Ibm, ReactionType.Poach, -1, rig.Env.Today, rig.Env.Today.AddDays(120));
            rig.Rng.DefaultChance = true;

            PlanResult result = rig.Planner.OnTrigger(rig.Trigger(TriggerType.StrongLaunch, Amd, Segment.MidRange));

            Assert.Equal(GateResult.ConcurrentCap, result.Gate);
            Assert.True(rig.Log.Any(line => line.Contains("cap reached")), "the log says: cap reached");
            Assert.Equal(0, rig.Rng.Probabilities.Count);

            rig.Reactions.RemoveActive("r-91");
            Assert.True(rig.Planner.OnTrigger(rig.Trigger(TriggerType.StrongLaunch, Amd, Segment.MidRange)).IsPlanned, "one slot is free again");
        }

        [Test]
        public void FourHostileReactionsAYear_IsTheCap_AndANewYearStartsAgain()
        {
            var rig = new ReactionRig();
            rig.Base.SetAttitude(Amd, -30f);
            rig.Rng.DefaultChance = true;
            rig.State.counters.year = rig.Env.Today.Year;
            rig.State.counters.hostileExecutedThisYear = 4;

            PlanResult refused = rig.Planner.OnTrigger(rig.Trigger(TriggerType.StrongLaunch, Amd, Segment.MidRange));
            Assert.Equal(GateResult.YearlyCap, refused.Gate);
            Assert.True(rig.Log.Any(line => line.Contains("cap reached")));

            rig.SetToday(new DateTime(rig.Env.Today.Year + 1, 1, 2));
            Assert.True(rig.Planner.OnTrigger(rig.Trigger(TriggerType.StrongLaunch, Amd, Segment.MidRange)).IsPlanned, "the count belongs to the old year");
        }

        [Test]
        public void TheAttitudeGate_ForbidsHostileReactions_UnlessTheRowSaysAnyAttitude()
        {
            // hegemon at -10 (its baseline): above the gate of -20
            var hegemon = new ReactionRig();
            hegemon.Rng.DefaultChance = true;
            PlanResult refused = hegemon.Planner.OnTrigger(hegemon.Trigger(TriggerType.StrongLaunch, Intel, Segment.HighEnd));
            Assert.Equal(PlanStatus.Refused, refused.Status);
            Assert.Equal(GateResult.AttitudeTooHigh, refused.Gate);
            Assert.Equal(0, hegemon.Rng.Probabilities.Count, "both rows are gated, nothing is rolled");

            // exactly -20 is allowed
            hegemon.Base.SetAttitude(Intel, -20f);
            Assert.True(hegemon.Planner.OnTrigger(hegemon.Trigger(TriggerType.StrongLaunch, Intel, Segment.HighEnd)).IsPlanned);

            // price_warrior at -10: PriceCut is anyAttitude, CounterLaunch is not
            var warrior = new ReactionRig();
            warrior.Base.SetAttitude(Amd, -10f);
            PendingReaction cut = warrior.PlanOne(TriggerType.StrongLaunch, Amd, Segment.MidRange);
            Assert.Equal("PriceCut", cut.type);
        }

        [Test]
        public void ARowThatIsGatedForItsAttitude_IsSkipped_AndTheNextRowIsTried()
        {
            // price_warrior at -10 and a PriceCut already in effect: row 1 (PriceCut) is gated by the same type, row 2 (CounterLaunch) by the attitude
            var rig = new ReactionRig();
            rig.Base.SetAttitude(Amd, -10f);
            rig.Reactions.BeginActive("r-90", Amd, ReactionType.PriceCut, 1, rig.Env.Today, rig.Env.Today.AddDays(180));
            rig.Rng.DefaultChance = true;

            PlanResult result = rig.Planner.OnTrigger(rig.Trigger(TriggerType.StrongLaunch, Amd, Segment.MidRange));
            Assert.Equal(PlanStatus.Refused, result.Status);
            Assert.Equal(GateResult.AttitudeTooHigh, result.Gate, "the last reason found");
            Assert.Equal(0, rig.Rng.Probabilities.Count);

            // at -30 the second row passes its gate and is rolled
            rig.Base.SetAttitude(Amd, -30f);
            PlanResult planned = rig.Planner.OnTrigger(rig.Trigger(TriggerType.StrongLaunch, Amd, Segment.MidRange));
            Assert.True(planned.IsPlanned);
            Assert.Equal("CounterLaunch", planned.Reaction.type);
            Assert.Equal(1, rig.Rng.Probabilities.Count);
            Assert.Near(0.13, rig.Rng.Probabilities[0], 1e-5, "0.1 x 1.3");
        }

        [Test]
        public void TheSameTypeIsNotPlannedTwice_ForOneRival()
        {
            // AC-6.4: an OemPush of Intel is in effect, an OemLoss comes
            var rig = new ReactionRig();
            rig.Base.SetAttitude(Intel, -30f);
            rig.Reactions.BeginActive("r-90", Intel, ReactionType.OemPush, -1, rig.Env.Today, rig.Env.Today.AddDays(365));
            rig.Rng.DefaultChance = true;

            PlanResult result = rig.Planner.OnTrigger(rig.Trigger(TriggerType.OemLoss, Intel, Segment.Any));

            Assert.Equal(PlanStatus.Refused, result.Status);
            Assert.Equal(GateResult.SameTypeActive, result.Gate);
            Assert.Equal(0, rig.Planner.Pending.Count);
            Assert.Equal(0, rig.Rng.Probabilities.Count);
        }

        [Test]
        public void ABankruptOrDormantRival_GetsNoReaction()
        {
            var rig = new ReactionRig();
            rig.Base.SetAttitude(Amd, -30f);
            rig.Rng.DefaultChance = true;

            rig.Env.Bankrupt.Add(Amd);
            Assert.Equal(GateResult.Bankrupt, rig.Planner.OnTrigger(rig.Trigger(TriggerType.StrongLaunch, Amd, Segment.MidRange)).Gate);

            rig.Env.Bankrupt.Clear();
            rig.State.dormant.Add(Amd);
            Assert.Equal(GateResult.Dormant, rig.Planner.OnTrigger(rig.Trigger(TriggerType.StrongLaunch, Amd, Segment.MidRange)).Gate);
        }

        [Test]
        public void WhileASaveLoadsOrTheGameFastForwards_NothingIsPlanned()
        {
            var rig = new ReactionRig();
            rig.Base.SetAttitude(Amd, -30f);
            rig.Rng.DefaultChance = true;
            rig.Env.Blocked = true;

            PlanResult result = rig.Planner.OnTrigger(rig.Trigger(TriggerType.StrongLaunch, Amd, Segment.MidRange));

            Assert.Equal(PlanStatus.Blocked, result.Status);
            Assert.Equal(0, rig.Rng.Probabilities.Count);
            Assert.Equal(0, rig.Intel.Sent.Count);
        }

        // ---- the rows that cannot happen ----

        [Test]
        public void APriceCut_NeedsACpuToCut_ElseTheRowIsSkipped()
        {
            var rig = new ReactionRig();
            rig.Base.SetAttitude(Amd, -30f);
            rig.Env.NoCpu.Add(Amd * 10 + (int)Segment.MidRange);

            PendingReaction pending = rig.PlanOne(TriggerType.StrongLaunch, Amd, Segment.MidRange);

            Assert.Equal("CounterLaunch", pending.type, "the PriceCut row was skipped without a roll");
            Assert.Equal(1, rig.Rng.Probabilities.Count);
        }

        [Test]
        public void ARowThatNeedsASegment_UsesTheMainSegmentOfTheRival_WhenTheTriggerHasNone()
        {
            var rig = new ReactionRig();
            rig.Base.SetAttitude(Amd, -30f);
            rig.Env.MainSegments[Amd] = Segment.LowEnd;

            PendingReaction pending = rig.PlanOne(TriggerType.StrongLaunch, Amd, Segment.Any);

            Assert.Equal("PriceCut", pending.type);
            Assert.Equal((int)Segment.LowEnd, pending.segment);

            var none = new ReactionRig();
            none.Base.SetAttitude(Amd, -30f);
            none.Env.MainSegments[Amd] = Segment.Any;
            none.Rng.DefaultChance = true;
            Assert.Equal(PlanStatus.NoCandidate, none.Planner.OnTrigger(none.Trigger(TriggerType.StrongLaunch, Amd, Segment.Any)).Status);
            Assert.Equal(0, none.Rng.Probabilities.Count);
        }

        // ---- the event system ----

        [Test]
        public void WithoutTheEventSystem_NothingIsPlanned_AndOneLineIsLoggedForTheSession()
        {
            // AC-7.4
            var rig = new ReactionRig();
            rig.Base.SetAttitude(Amd, -30f);
            rig.Base.SetAttitude(Intel, -30f);
            rig.Rng.DefaultChance = true;
            rig.Env.EventSystemAvailable = false;

            PlanResult first = rig.Planner.OnTrigger(rig.Trigger(TriggerType.StrongLaunch, Amd, Segment.MidRange));
            rig.Planner.OnTrigger(rig.Trigger(TriggerType.StrongLaunch, Intel, Segment.HighEnd));
            rig.Planner.OnTrigger(rig.Trigger(TriggerType.PriceUndercut, Amd, Segment.MidRange));

            Assert.Equal(GateResult.NoEventSystem, first.Gate);
            Assert.Equal(0, rig.Planner.Pending.Count);
            Assert.Equal(0, rig.Intel.Sent.Count);
            Assert.Equal(0, rig.Rng.Probabilities.Count);
            Assert.Equal(1, rig.Log.Count(line => line.Contains(ReactionPlannerCore.NoEventSystemLine)), "one line per session");
            Assert.Equal("reactions disabled: event-system unavailable", ReactionPlannerCore.NoEventSystemLine);
            Assert.True(rig.Base.Ledger.IsKnown(Amd), "the attitude, and with it the rest of the feature, is not touched");

            rig.Env.EventSystemAvailable = true;
            Assert.True(rig.Planner.OnTrigger(rig.Trigger(TriggerType.StrongLaunch, Amd, Segment.MidRange)).IsPlanned, "planning resumes when the event system is back");
        }

        // ---- the debug flag ----

        [Test]
        public void ForceReactions_MakesTheChanceOne_TheWarningOneDay_AndEndsTheGracePeriod()
        {
            var rig = new ReactionRig();
            rig.Env.ForceReactions = true;
            rig.SetToday(ReactionRig.GameStart.AddDays(5));
            rig.Base.SetAttitude(Amd, -30f);
            rig.Rng.RangeValue = 30;

            PendingReaction pending = rig.PlanOne(TriggerType.StrongLaunch, Amd, Segment.MidRange);

            Assert.Near(1.0, rig.Rng.Probabilities[0], 1e-9);
            Assert.Equal(1, pending.telegraphDays);
            Assert.Equal(0, rig.Rng.RangeDraws, "no dice for a warning of one day");
            Assert.Equal(1, rig.Intel.Sent[0].WeeksFrom);
            Assert.Equal(1, rig.Intel.Sent[0].WeeksTo);

            // the other limits stay: the attitude gate still holds
            var gated = new ReactionRig();
            gated.Env.ForceReactions = true;
            gated.Rng.DefaultChance = true;
            Assert.Equal(GateResult.AttitudeTooHigh, gated.Planner.OnTrigger(gated.Trigger(TriggerType.StrongLaunch, Intel, Segment.HighEnd)).Gate);
        }

        [Test]
        public void PlanManual_SkipsTheLimits_ButNotTheThreeThatCannotBeSetAside()
        {
            var rig = new ReactionRig();
            rig.SetToday(ReactionRig.GameStart.AddDays(10));   // grace period, attitude above the gate

            PlanResult planned = rig.Planner.PlanManual(Ibm, ReactionType.Poach, Segment.Any);
            Assert.True(planned.IsPlanned);
            Assert.Equal(1, planned.Reaction.telegraphDays);
            Assert.Equal(0, rig.Rng.Probabilities.Count, "no roll");
            Assert.Equal(1, rig.Intel.Sent[0].WeeksTo);

            Assert.Equal(GateResult.SameTypeActive, rig.Planner.PlanManual(Ibm, ReactionType.Poach, Segment.Any).Gate, "the same type is on its way");

            rig.Env.Bankrupt.Add(Amd);
            Assert.Equal(GateResult.Bankrupt, rig.Planner.PlanManual(Amd, ReactionType.Smear, Segment.Any).Gate);
            rig.State.dormant.Add(Intel);
            Assert.Equal(GateResult.Dormant, rig.Planner.PlanManual(Intel, ReactionType.Smear, Segment.Any).Gate);
            rig.Env.EventSystemAvailable = false;
            Assert.Equal(GateResult.NoEventSystem, rig.Planner.PlanManual(AttitudeRig.Toshiba, ReactionType.Smear, Segment.Any).Gate);
        }

        [Test]
        public void PlanManual_NeedsASegment_ForPriceCutAndCounterLaunch()
        {
            var rig = new ReactionRig();
            rig.Env.MainSegments[Amd] = Segment.Any;

            Assert.Equal(PlanStatus.NoCandidate, rig.Planner.PlanManual(Amd, ReactionType.PriceCut, Segment.Any).Status);
            Assert.Equal(PlanStatus.NoCandidate, rig.Planner.PlanManual(Amd, ReactionType.Partnership, Segment.Any).Status, "the friendly offer is not planned here");

            rig.Env.MainSegments[Amd] = Segment.HighEnd;
            PlanResult result = rig.Planner.PlanManual(Amd, ReactionType.CounterLaunch, Segment.Any);
            Assert.True(result.IsPlanned);
            Assert.Equal((int)Segment.HighEnd, result.Reaction.segment);
            Assert.Equal("Manual", result.Reaction.trigger);
        }

        // ---- the intelligence mail ----

        [Test]
        public void IfTheMailIsNotAccepted_TheReactionIsNotPlanned()
        {
            var rig = new ReactionRig();
            rig.Base.SetAttitude(Amd, -30f);
            rig.Intel.Refuse = true;
            rig.Rng.Chances.Enqueue(true);

            PlanResult result = rig.Planner.OnTrigger(rig.Trigger(TriggerType.StrongLaunch, Amd, Segment.MidRange));

            Assert.Equal(PlanStatus.MailRefused, result.Status);
            Assert.Equal(0, rig.Planner.Pending.Count, "a warning that cannot be given is not optional");
            Assert.Equal(0, rig.Reactions.HistoryOf(Amd, 10).Count);
            Assert.True(rig.Log.Any(line => line.Contains("mail was not accepted")));
        }

        [Test]
        public void TheDeliveryOfTheMail_StartsTheCountOfTheWarning()
        {
            // AC-7.6: the mail comes 4 days after the planning, the day of carrying out is counted from the delivery
            var rig = new ReactionRig();
            rig.Base.SetAttitude(Amd, -30f);
            rig.Rng.RangeValue = 33;
            DateTime planned = rig.Env.Today;
            PendingReaction pending = rig.PlanOne(TriggerType.StrongLaunch, Amd, Segment.MidRange);
            Assert.Equal(33, pending.telegraphDays);

            DateTime delivered = planned.AddDays(4);
            Assert.True(rig.Deliver(pending, delivered));

            Assert.Equal(RivalDates.ToText(delivered.AddDays(33)), pending.fireOn);
            DateTime fire;
            Assert.True(RivalDates.TryParse(pending.fireOn, out fire));
            Assert.True((fire - delivered).Days >= 14, "at least 14 days between the delivery and the carrying out");
        }

        [Test]
        public void AMailOfAnotherKind_OrOfAReactionThatIsGone_IsIgnored_AndASecondDeliveryDoesNotMoveTheDay()
        {
            var rig = new ReactionRig();
            rig.Base.SetAttitude(Amd, -30f);
            PendingReaction pending = rig.PlanOne(TriggerType.StrongLaunch, Amd, Segment.MidRange);
            DateTime day = rig.Env.Today;

            Assert.False(rig.Planner.OnMailDelivered("onb:welcome", day));
            Assert.False(rig.Planner.OnMailDelivered(ReactionTags.Intel("r-999"), day));
            Assert.False(rig.Planner.OnMailDelivered(ReactionTags.Intel(pending.id), null), "no delivery date");
            Assert.Equal(string.Empty, pending.fireOn);

            Assert.True(rig.Deliver(pending, day));
            string fireOn = pending.fireOn;
            Assert.False(rig.Deliver(pending, day.AddDays(10)));
            Assert.Equal(fireOn, pending.fireOn);
        }

        // ---- every day ----

        [Test]
        public void ADueReaction_IsCarriedOut_OnItsDay_AndNotBefore()
        {
            var rig = new ReactionRig();
            rig.Base.SetAttitude(Amd, -30f);
            rig.Rng.RangeValue = 20;
            PendingReaction pending = rig.PlanOne(TriggerType.StrongLaunch, Amd, Segment.MidRange);
            DateTime delivered = rig.Env.Today;
            rig.Deliver(pending, delivered);

            rig.SetToday(delivered.AddDays(19));
            TickReport early = rig.Planner.DailyTick();
            Assert.Equal(0, early.Launched);
            Assert.Equal(1, rig.Planner.Pending.Count);

            rig.SetToday(delivered.AddDays(20));
            TickReport due = rig.Planner.DailyTick();
            Assert.Equal(1, due.Launched);
            Assert.Equal(0, rig.Planner.Pending.Count, "the planned record goes when the event is fired");
            Assert.Equal(1, rig.Runtime.Fires.Count);
            Assert.Equal("rival.priceCut", rig.Runtime.Fires[0].EventId);
            Assert.Equal(1, rig.Reactions.HostileExecutedThisYear(rig.Env.Today));
            Assert.Equal(0, rig.Reactions.DaysSinceLastReaction(Amd, rig.Env.Today), "the cooldown starts");
            Assert.Equal(1, rig.Executor.Active.Count);
        }

        [Test]
        public void IfTheMailIsNotDeliveredIn30Days_TheReactionIsCancelled()
        {
            var rig = new ReactionRig();
            rig.Base.SetAttitude(Amd, -30f);
            DateTime planned = rig.Env.Today;
            rig.PlanOne(TriggerType.StrongLaunch, Amd, Segment.MidRange);

            rig.SetToday(planned.AddDays(29));
            Assert.Equal(0, rig.Planner.DailyTick().Cancelled);
            Assert.Equal(1, rig.Planner.Pending.Count);

            rig.SetToday(planned.AddDays(30));
            Assert.Equal(1, rig.Planner.DailyTick().Cancelled);
            Assert.Equal(0, rig.Planner.Pending.Count);
            Assert.Equal(0, rig.Runtime.Fires.Count, "never carried out");
            List<HistoryEntry> history = rig.Reactions.HistoryOf(Amd, 10);
            Assert.Equal(HistoryOutcomes.Cancelled, history[history.Count - 1].outcome);
            Assert.True(rig.Log.Any(line => line.Contains("not delivered")), "the log says why");
        }

        [Test]
        public void ADeliveryTheEventDidNotAnnounce_IsCaughtUpByTheTick()
        {
            var rig = new ReactionRig();
            rig.Base.SetAttitude(Amd, -30f);
            rig.Rng.RangeValue = 25;
            DateTime planned = rig.Env.Today;
            PendingReaction pending = rig.PlanOne(TriggerType.StrongLaunch, Amd, Segment.MidRange);

            rig.Env.Deliveries[pending.mailId] = planned.AddDays(3);   // the event came while the feature was off
            rig.SetToday(planned.AddDays(8));
            TickReport report = rig.Planner.DailyTick();

            Assert.Equal(1, report.Synced);
            Assert.Equal(RivalDates.ToText(planned.AddDays(3 + 25)), pending.fireOn);
        }

        [Test]
        public void TheReactionsOfABankruptOrDormantRival_AreDropped()
        {
            var rig = new ReactionRig();
            rig.Base.SetAttitude(Amd, -30f);
            rig.Base.SetAttitude(Intel, -30f);
            rig.PlanOne(TriggerType.StrongLaunch, Amd, Segment.MidRange);
            rig.PlanOne(TriggerType.StrongLaunch, Intel, Segment.HighEnd);

            rig.Env.Bankrupt.Add(Amd);
            rig.State.dormant.Add(Intel);
            rig.AddDays(1);
            TickReport report = rig.Planner.DailyTick();

            Assert.Equal(2, report.Cancelled);
            Assert.Equal(0, rig.Planner.Pending.Count);
            Assert.Equal(HistoryOutcomes.Cancelled, rig.Reactions.HistoryOf(Amd, 10).Last().outcome);
            Assert.Equal(HistoryOutcomes.Cancelled, rig.Reactions.HistoryOf(Intel, 10).Last().outcome);
        }

        [Test]
        public void WhileTheEventSystemIsOff_ADueReactionWaits_AndAStaleOneIsDropped()
        {
            var rig = new ReactionRig();
            rig.Base.SetAttitude(Amd, -30f);
            rig.Rng.RangeValue = 14;
            PendingReaction pending = rig.PlanOne(TriggerType.StrongLaunch, Amd, Segment.MidRange);
            DateTime delivered = rig.Env.Today;
            rig.Deliver(pending, delivered);
            DateTime fireDay = delivered.AddDays(14);

            rig.Env.EventSystemAvailable = false;
            rig.SetToday(fireDay);
            TickReport waiting = rig.Planner.DailyTick();
            Assert.Equal(1, waiting.Waiting);
            Assert.Equal(1, rig.Planner.Pending.Count);
            Assert.Equal(1, rig.Log.Count(line => line.Contains(ReactionPlannerCore.NoEventSystemLine)));

            rig.SetToday(fireDay.AddDays(30));
            Assert.Equal(1, rig.Planner.DailyTick().Waiting, "30 days late is still waiting");

            rig.SetToday(fireDay.AddDays(31));
            Assert.Equal(1, rig.Planner.DailyTick().Cancelled, "31 days late is stale");
            Assert.Equal(0, rig.Planner.Pending.Count);
            Assert.True(rig.Log.Any(line => line.Contains("overdue")));
        }

        [Test]
        public void IfTheEventSystemAnswersDisabled_TheReactionWaitsForTomorrow()
        {
            var rig = new ReactionRig();
            rig.Base.SetAttitude(Amd, -30f);
            rig.Rng.RangeValue = 14;
            PendingReaction pending = rig.PlanOne(TriggerType.StrongLaunch, Amd, Segment.MidRange);
            rig.Deliver(pending, rig.Env.Today);
            rig.AddDays(14);

            rig.Runtime.Result = FireResult.Disabled;
            TickReport first = rig.Planner.DailyTick();
            Assert.Equal(1, first.Waiting);
            Assert.Equal(1, rig.Planner.Pending.Count);
            Assert.Equal(0, rig.Executor.Active.Count, "nothing is left in effect by a firing that did not happen");
            Assert.Equal(0, rig.Reactions.HostileExecutedThisYear(rig.Env.Today), "and nothing is counted");

            rig.Runtime.Result = FireResult.Fired;
            rig.AddDays(1);
            Assert.Equal(1, rig.Planner.DailyTick().Launched);
            Assert.Equal(0, rig.Planner.Pending.Count);
        }

        [Test]
        public void IfTheEventCannotBeFired_TheReactionIsDropped_WithAHistoryLine()
        {
            var rig = new ReactionRig();
            rig.Base.SetAttitude(Amd, -30f);
            rig.Rng.RangeValue = 14;
            PendingReaction pending = rig.PlanOne(TriggerType.StrongLaunch, Amd, Segment.MidRange);
            rig.Deliver(pending, rig.Env.Today);
            rig.AddDays(14);

            rig.Runtime.Result = FireResult.Skipped;
            TickReport report = rig.Planner.DailyTick();

            Assert.Equal(1, report.Cancelled);
            Assert.Equal(0, rig.Planner.Pending.Count);
            Assert.Equal(0, rig.Executor.Active.Count);
            Assert.Equal(HistoryOutcomes.Cancelled, rig.Reactions.HistoryOf(Amd, 10).Last().outcome);
            Assert.Equal(0, rig.Reactions.HostileExecutedThisYear(rig.Env.Today));
        }

        // ---- cancelling ----

        [Test]
        public void CancelFor_DropsThePlannedAndTakesBackWhatIsInEffect()
        {
            var rig = new ReactionRig();
            rig.Base.SetAttitude(Amd, -30f);
            rig.PlanOne(TriggerType.StrongLaunch, Amd, Segment.MidRange);
            rig.Reactions.BeginActive("r-90", Amd, ReactionType.CounterLaunch, 1, rig.Env.Today, rig.Env.Today.AddDays(150));
            rig.Reactions.BeginActive("r-91", Intel, ReactionType.OemPush, -1, rig.Env.Today, rig.Env.Today.AddDays(365));

            int dropped = rig.Planner.CancelFor(Amd, "put aside");

            Assert.Equal(2, dropped);
            Assert.Equal(0, rig.Planner.Pending.Count);
            Assert.Equal(1, rig.Executor.Active.Count, "only the other rival's reaction is left");
            Assert.Equal("r-91", rig.Executor.Active[0].id);
            Assert.True(rig.Runtime.Cleanups.Contains("r-90"), "what the reaction held is given back");
            Assert.Equal(0, rig.Runtime.StartedNotes.Count + rig.Runtime.EndedNotes.Count, "no notification for a reaction that was taken back");
        }

        [Test]
        public void CancelBankrupt_FindsTheRivalsThatWentBankrupt()
        {
            var rig = new ReactionRig();
            rig.Reactions.BeginActive("r-90", Amd, ReactionType.PriceCut, 1, rig.Env.Today, rig.Env.Today.AddDays(180));
            rig.Reactions.BeginActive("r-91", Intel, ReactionType.OemPush, -1, rig.Env.Today, rig.Env.Today.AddDays(365));
            rig.Env.Bankrupt.Add(Amd);

            Assert.Equal(1, rig.Planner.CancelBankrupt());
            Assert.Equal(1, rig.Executor.Active.Count);
            Assert.Equal(0, rig.Planner.CancelBankrupt(), "nothing left to drop");
        }

        // ---- the warning in weeks ----

        [Test]
        public void TheWeekRange_RoundsTheLowestDown_AndTheHighestUp()
        {
            int from, to;
            ReactionPlannerCore.WeekRange(RivalConfig.Defaults(), false, out from, out to);
            Assert.Equal(2, from);
            Assert.Equal(7, to);

            ReactionPlannerCore.WeekRange(RivalConfig.Defaults(), true, out from, out to);
            Assert.Equal(1, from);
            Assert.Equal(1, to);

            var config = RivalConfig.Defaults();
            config.telegraphDays = new[] { 10, 28 };
            ReactionPlannerCore.WeekRange(config, false, out from, out to);
            Assert.Equal(1, from, "at least one week");
            Assert.Equal(4, to);

            config.telegraphDays = new[] { 7, 7 };
            ReactionPlannerCore.WeekRange(config, false, out from, out to);
            Assert.Equal(1, from);
            Assert.Equal(1, to);

            ReactionPlannerCore.WeekRange(null, false, out from, out to);
            Assert.Equal(2, from, "no config: the defaults");
        }

        // ---- saving ----

        [Test]
        public void ThePlannedReactions_AndTheCounters_SurviveTheSave()
        {
            var rig = new ReactionRig();
            rig.Base.SetAttitude(Amd, -30f);
            rig.Rng.RangeValue = 33;
            PendingReaction pending = rig.PlanOne(TriggerType.StrongLaunch, Amd, Segment.MidRange);
            rig.Deliver(pending, rig.Env.Today.AddDays(2));
            rig.Reactions.MarkExecuted(Intel, rig.Env.Today.AddDays(-5), true);
            string activeId = rig.Reactions.NextId();   // a real id: the section is read back with the next number above every id in use
            rig.Reactions.BeginActive(activeId, Intel, ReactionType.OemPush, -1, rig.Env.Today, rig.Env.Today.AddDays(365));

            string text = ModJson.Write(RivalSaveCodec.Write(rig.State), false);
            JsonNode node;
            JsonError error;
            Assert.True(ModJson.TryParse(text, out node, out error), "the saved text is JSON: " + error);
            RivalState loaded = RivalSaveCodec.Read((JsonObject)node, null);
            var book = new ReactionBook(() => loaded);

            PendingReaction again = book.FindPending(pending.id);
            Assert.NotNull(again);
            Assert.Equal(pending.uid, again.uid);
            Assert.Equal(pending.type, again.type);
            Assert.Equal(pending.segment, again.segment);
            Assert.Equal(pending.trigger, again.trigger);
            Assert.Equal(pending.plannedOn, again.plannedOn);
            Assert.Equal(pending.mailId, again.mailId);
            Assert.Equal(33, again.telegraphDays);
            Assert.Equal(pending.fireOn, again.fireOn);
            Assert.NotNull(book.FindPendingByMail(pending.mailId));

            Assert.Equal(1, book.HostileExecutedThisYear(rig.Env.Today));
            Assert.Equal(5, book.DaysSinceLastReaction(Intel, rig.Env.Today));
            Assert.Equal(rig.State.nextReactionSeq, loaded.nextReactionSeq);
            Assert.NotNull(book.FindActive(activeId));
            Assert.Equal(1, book.HistoryOf(Amd, 10).Count);
        }
    }
}
