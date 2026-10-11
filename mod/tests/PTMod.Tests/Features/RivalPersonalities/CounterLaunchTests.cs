using System;
using System.Collections.Generic;
using System.Linq;
using PTMod.Core;
using PTMod.Features.RivalPersonalities;

namespace PTMod.Tests.Features.RivalPersonalities
{
    /// <summary>
    /// The close of a counter-strike and the behaviour without the event system (TSK-016; design.md Component 9 and "Подписки на события"; FR-7 items 4 and
    /// 8): the first CPU a rival releases in the segment of its <c>CounterLaunch</c> that is in effect is its answer, once per reaction; releases that do
    /// not count; and no reaction at all is planned while the event system is unavailable, with one line in the log and the rest of the feature going on.
    /// </summary>
    [Covers(typeof(CounterLaunchRules))]
    public class CounterLaunchTests
    {
        private const int Intel = AttitudeRig.Intel;
        private const int Toshiba = AttitudeRig.Toshiba;   // fast_follower: CounterLaunch on StrongLaunch

        /// <summary>A CounterLaunch of the rival in the segment, launched and announced like the handler does it.</summary>
        private static ActiveReaction Running(ReactionRig rig, string id, int uid, Segment segment)
        {
            var pending = new PendingReaction
            {
                id = id, uid = uid, type = "CounterLaunch", segment = (int)segment, trigger = "StrongLaunch",
                plannedOn = RivalDates.ToText(rig.Env.Today.AddDays(-20)), mailId = 100, telegraphDays = 20, fireOn = RivalDates.ToText(rig.Env.Today),
            };
            rig.Reactions.AddPending(pending);
            string reason;
            Assert.Equal(LaunchOutcome.Started, rig.Executor.Launch(pending, out reason));
            rig.Executor.OnStarted(id, uid, ReactionType.CounterLaunch, segment, rig.Env.Today.AddDays(150), "Mid Range");
            return rig.Reactions.FindActive(id);
        }

        // ---- which releases count ----

        [Test]
        public void OnlyTheReleaseOfARival_NotAVariant_NotInTheFastForward_NotWhileLoading_Counts()
        {
            Assert.True(CounterLaunchRules.IsRelevantRelease(true, false, false, false, false));

            Assert.False(CounterLaunchRules.IsRelevantRelease(false, false, false, false, false), "the player's CPU, or a company that is no rival");
            Assert.False(CounterLaunchRules.IsRelevantRelease(true, true, false, false, false), "a variant of a CPU does not count");
            Assert.False(CounterLaunchRules.IsRelevantRelease(true, false, true, false, false), "released in the fast-forward");
            Assert.False(CounterLaunchRules.IsRelevantRelease(true, false, false, true, false), "a save is loading");
            Assert.False(CounterLaunchRules.IsRelevantRelease(true, false, false, false, true), "the fast-forward runs");
        }

        // ---- the reaction it answers ----

        [Test]
        public void ACpuInTheSegment_OfACounterLaunchInEffect_IsItsAnswer()
        {
            var rig = new ReactionRig();
            ActiveReaction active = Running(rig, "r-7", Toshiba, Segment.HighEnd);

            ActiveReaction found = CounterLaunchRules.FindOpen(rig.Reactions, Toshiba, Segment.HighEnd);

            Assert.True(ReferenceEquals(active, found));
        }

        [Test]
        public void ADifferentSegment_ADifferentRival_OrNoSegment_AnswersNothing()
        {
            var rig = new ReactionRig();
            Running(rig, "r-7", Toshiba, Segment.HighEnd);

            Assert.Null(CounterLaunchRules.FindOpen(rig.Reactions, Toshiba, Segment.MidRange));
            Assert.Null(CounterLaunchRules.FindOpen(rig.Reactions, Intel, Segment.HighEnd));
            Assert.Null(CounterLaunchRules.FindOpen(rig.Reactions, Toshiba, Segment.Any));
            Assert.Null(CounterLaunchRules.FindOpen(null, Toshiba, Segment.HighEnd));
        }

        [Test]
        public void AReactionOfAnotherType_IsNotACounterStrike()
        {
            var rig = new ReactionRig();
            rig.Reactions.BeginActive("r-1", Toshiba, ReactionType.PriceCut, (int)Segment.HighEnd, rig.Env.Today, rig.Env.Today.AddDays(180));
            rig.Reactions.AddHistory(Toshiba, rig.Env.Today, "PriceCut", (int)Segment.HighEnd, HistoryOutcomes.Started);

            Assert.Null(CounterLaunchRules.FindOpen(rig.Reactions, Toshiba, Segment.HighEnd));
        }

        [Test]
        public void ACounterLaunchThatHasNotStarted_CannotBeAnswered()
        {
            // the record is there from the firing, the handler has not reported the start yet
            var rig = new ReactionRig();
            rig.Reactions.BeginActive("r-7", Toshiba, ReactionType.CounterLaunch, (int)Segment.HighEnd, rig.Env.Today, rig.Env.Today.AddDays(150));

            Assert.Null(CounterLaunchRules.FindOpen(rig.Reactions, Toshiba, Segment.HighEnd));

            rig.Executor.OnStarted("r-7", Toshiba, ReactionType.CounterLaunch, Segment.HighEnd, rig.Env.Today.AddDays(150));
            Assert.NotNull(CounterLaunchRules.FindOpen(rig.Reactions, Toshiba, Segment.HighEnd));
        }

        // ---- once per reaction ----

        [Test]
        public void TheAnswer_IsWrittenToTheHistory_AndTheSameReactionAnswersOnce()
        {
            var rig = new ReactionRig();
            ActiveReaction active = Running(rig, "r-7", Toshiba, Segment.HighEnd);

            CounterLaunchRules.MarkDone(rig.Reactions, active, rig.Env.Today.AddDays(40));

            HistoryEntry last = rig.Reactions.HistoryOf(Toshiba, 10).Last();
            Assert.Equal(HistoryOutcomes.CounterLaunchDone, last.outcome);
            Assert.Equal("CounterLaunch", last.type);
            Assert.Equal((int)Segment.HighEnd, last.segment);
            Assert.Equal(RivalDates.ToText(rig.Env.Today.AddDays(40)), last.date);
            Assert.Null(CounterLaunchRules.FindOpen(rig.Reactions, Toshiba, Segment.HighEnd), "a second CPU does not send a second mail");

            CounterLaunchRules.MarkDone(rig.Reactions, null, rig.Env.Today);   // no exception
            CounterLaunchRules.MarkDone(null, active, rig.Env.Today);
        }

        [Test]
        public void ALaterCounterLaunchOfTheSameRival_IsAnsweredAgain()
        {
            var rig = new ReactionRig();
            ActiveReaction first = Running(rig, "r-7", Toshiba, Segment.HighEnd);
            CounterLaunchRules.MarkDone(rig.Reactions, first, rig.Env.Today.AddDays(40));
            rig.SetToday(rig.Env.Today.AddDays(150));
            Assert.True(rig.Executor.OnEnded("r-7"));

            rig.SetToday(rig.Env.Today.AddDays(400));
            ActiveReaction second = Running(rig, "r-12", Toshiba, Segment.HighEnd);

            Assert.True(ReferenceEquals(second, CounterLaunchRules.FindOpen(rig.Reactions, Toshiba, Segment.HighEnd)), "the answer to the first one does not count for the new one");
        }

        [Test]
        public void TwoSegments_AreAnsweredEachByItsOwnCpu()
        {
            var rig = new ReactionRig();
            ActiveReaction high = Running(rig, "r-7", Toshiba, Segment.HighEnd);
            ActiveReaction low = Running(rig, "r-8", Toshiba, Segment.LowEnd);

            CounterLaunchRules.MarkDone(rig.Reactions, high, rig.Env.Today);

            Assert.Null(CounterLaunchRules.FindOpen(rig.Reactions, Toshiba, Segment.HighEnd));
            Assert.True(ReferenceEquals(low, CounterLaunchRules.FindOpen(rig.Reactions, Toshiba, Segment.LowEnd)));
        }

        // ---- without the event system ----

        [Test]
        public void WithoutTheEventSystem_NoReactionIsPlanned_OneLineIsLogged_AndTheRestGoesOn()
        {
            var rig = new ReactionRig();
            rig.Env.EventSystemAvailable = false;
            rig.Base.SetAttitude(Toshiba, -30f);
            rig.Rng.DefaultChance = true;

            // a trigger comes: the attitude has been changed by the recorder already, the planner plans nothing
            rig.Base.Recorder.Record(TriggerType.StrongLaunch, Toshiba, Segment.HighEnd, "Test CPU");
            Assert.True(rig.Base.Triggers.Count == 1, "the recorder still raises the trigger");
            PlanResult first = rig.Planner.OnTrigger(rig.Base.Triggers[0]);
            PlanResult second = rig.Planner.OnTrigger(rig.Trigger(TriggerType.StrongLaunch, Toshiba, Segment.HighEnd));

            Assert.Equal(GateResult.NoEventSystem, first.Gate);
            Assert.Equal(GateResult.NoEventSystem, second.Gate);
            Assert.Equal(0, rig.Planner.Pending.Count);
            Assert.Equal(0, rig.Intel.Sent.Count);
            Assert.Equal(0, rig.Runtime.Fires.Count);
            Assert.Equal(1, rig.Log.Count(line => line.Contains("reactions disabled: event-system unavailable")));

            // the attitude, the drift and the history of the feature work as they did
            float before = rig.Base.Ledger.Get(Toshiba);
            Assert.True(rig.Base.Ledger.Change(Toshiba, 10f, "BusinessSigned"));
            Assert.True(rig.Base.Ledger.Get(Toshiba) > before);
            Assert.Equal(0, rig.Executor.ExpireOverdue(rig.Env.Today));
        }

        [Test]
        public void WithoutTheEventSystem_NoCounterLaunchIsInEffect_SoNoMailIsEverSent()
        {
            var rig = new ReactionRig();
            rig.Env.EventSystemAvailable = false;

            Assert.Null(CounterLaunchRules.FindOpen(rig.Reactions, Toshiba, Segment.HighEnd));
            Assert.Null(CounterLaunchRules.FindOpen(rig.Reactions, Intel, Segment.HighEnd));
            Assert.Equal(0, rig.Executor.Active.Count);
        }
    }
}
