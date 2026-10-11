using System;
using System.Collections.Generic;
using PTMod.Core;
using PTMod.Core.Logic.Rng;
using PTMod.Core.Logic.Segments;
using PTMod.Features.PressReviews;

namespace PTMod.Tests.PressReviews
{
    /// <summary>
    /// The queue of reviews without the game (FR-1, TSK-007): which releases are queued (not the fast-forward, not variants, not CPUs seen before), the wait of
    /// 7–21 days from the stream of the feature or none in the instant mode, the sales a player's CPU adds up while it waits, the day a review is due, the
    /// three attempts, the move to the archive, and the queue surviving a save.
    /// </summary>
    [Covers(typeof(ReviewQueue), typeof(QueueVerdict))]
    public class ReviewQueueTests
    {
        private static readonly DateTime Day = new DateTime(1995, 3, 1);

        /// <summary>A stream that answers with the minimum plus a fixed offset.</summary>
        private sealed class FixedRandom : IRandomSource
        {
            public int Offset;
            public int Draws;

            public float NextGaussian()
            {
                return 0f;
            }

            public int Range(int minInclusive, int maxExclusive)
            {
                Draws++;
                return minInclusive + Offset;
            }
        }

        private static ReviewRecord Record(int cpuSaveId, string published = "1995-03-15")
        {
            return new ReviewRecord { cpuSaveId = cpuSaveId, cpuName = "CPU " + cpuSaveId, companyKey = "player", publishedOn = published, releasedOn = "1995-03-01", metascore = 70 };
        }

        // ---- who is queued ----

        [Test]
        public void Admit_AnOrdinaryRelease_IsQueued()
        {
            Assert.Equal(QueueVerdict.Queue, ReviewQueue.Admit(new PressState(), 10, "player", false, false, true));
            Assert.Equal(QueueVerdict.Queue, ReviewQueue.Admit(new PressState(), 11, "ai:3", false, false, true), "a CPU of an AI company too");
        }

        [Test]
        public void Admit_TheFastForwardOfANewGame_IsNeverReviewed()
        {
            Assert.Equal(QueueVerdict.FastForward, ReviewQueue.Admit(new PressState(), 10, "ai:1", false, true, true));
            Assert.Equal(QueueVerdict.FastForward, ReviewQueue.Admit(new PressState(), 10, "ai:1", true, true, true), "the fast-forward is decided first");
        }

        [Test]
        public void Admit_AVariant_IsNotReviewedOnItsOwn()
        {
            Assert.Equal(QueueVerdict.Variant, ReviewQueue.Admit(new PressState(), 10, "player", true, false, true));
        }

        [Test]
        public void Admit_ACpuThatIsNotReleased_IsTurnedAway()
        {
            Assert.Equal(QueueVerdict.NotReleased, ReviewQueue.Admit(new PressState(), 10, "player", false, false, false));
        }

        [Test]
        public void Admit_AnOwnerWithoutAKey_IsTurnedAway()
        {
            Assert.Equal(QueueVerdict.NoCompany, ReviewQueue.Admit(new PressState(), 10, null, false, false, true));
            Assert.Equal(QueueVerdict.NoCompany, ReviewQueue.Admit(new PressState(), 10, string.Empty, false, false, true));
        }

        [Test]
        public void Admit_ACpuInTheQueueOrTheArchive_IsKnown_OneReviewPerCpu()
        {
            var state = new PressState();
            ReviewQueue.Enqueue(state, 10, "player", Day, 14);
            state.reviews.Add(Record(20));

            Assert.Equal(QueueVerdict.AlreadyKnown, ReviewQueue.Admit(state, 10, "player", false, false, true), "waiting for its review");
            Assert.Equal(QueueVerdict.AlreadyKnown, ReviewQueue.Admit(state, 20, "player", false, false, true), "already reviewed");
            Assert.Equal(QueueVerdict.Queue, ReviewQueue.Admit(state, 30, "player", false, false, true), "a new CPU");
            Assert.True(ReviewQueue.IsKnown(state, 10) && ReviewQueue.IsKnown(state, 20) && !ReviewQueue.IsKnown(state, 30));
            Assert.False(ReviewQueue.IsKnown(null, 10), "no state, nothing known");
        }

        [Test]
        public void Admit_ASaveNeverMakesANewReview_OnlyTheQueueOfTheSaveRemains()
        {
            // The core does not raise CpuReleased while a save loads; what the load brings back is the saved queue and archive, and nothing else.
            PressState before = new PressState();
            ReviewQueue.Enqueue(before, 10, "player", Day, 14);
            before.reviews.Add(Record(20));

            PressState after = PressStateCodec.Read(PressStateCodec.Write(before), PressStateCodec.SectionVersion, null);
            Assert.Equal(1, after.pending.Count, "the queue comes back as it was");
            Assert.Equal(1, after.reviews.Count, "and so does the archive");
            Assert.Equal("1995-03-15", after.pending[0].publishOn);
            Assert.Equal(QueueVerdict.AlreadyKnown, ReviewQueue.Admit(after, 10, "player", false, false, true), "a CPU of the save is not queued again");
        }

        // ---- the wait ----

        [Test]
        public void PickDelay_InstantMode_IsZero_AndDrawsNothing()
        {
            var rng = new FixedRandom { Offset = 5 };
            Assert.Equal(0, ReviewQueue.PickDelay(ReviewConfig.CreateDefault(), true, rng));
            Assert.Equal(0, rng.Draws, "no dice for no wait");
        }

        [Test]
        public void PickDelay_IsBetween7And21Days_BothEndsIncluded()
        {
            ReviewConfig cfg = ReviewConfig.CreateDefault();
            Assert.Equal(7, ReviewQueue.PickDelay(cfg, false, new FixedRandom { Offset = 0 }), "the lowest draw");
            Assert.Equal(21, ReviewQueue.PickDelay(cfg, false, new FixedRandom { Offset = 14 }), "the highest draw of Range(7, 22)");
            Assert.Equal(21, ReviewQueue.PickDelay(cfg, false, new FixedRandom { Offset = 99 }), "a draw outside the range is held to it");
        }

        [Test]
        public void PickDelay_FromTheModStream_StaysInRange_ReachesBothEnds_AndRepeatsForTheSameSeed()
        {
            ReviewConfig cfg = ReviewConfig.CreateDefault();
            var first = new ModRngSource(new Xoshiro128(2024));
            var second = new ModRngSource(new Xoshiro128(2024));
            var seen = new HashSet<int>();
            for (int i = 0; i < 3000; i++)
            {
                int a = ReviewQueue.PickDelay(cfg, false, first);
                int b = ReviewQueue.PickDelay(cfg, false, second);
                Assert.Equal(a, b, "one seed, one sequence of waits");
                Assert.InRange(a, 7, 21);
                seen.Add(a);
            }
            Assert.Equal(15, seen.Count, "every wait from 7 to 21 days comes up");
        }

        [Test]
        public void PickDelay_ABrokenConfig_StillGivesAUsableWait()
        {
            var cfg = ReviewConfig.CreateDefault();
            cfg.DelayMinDays = 5;
            cfg.DelayMaxDays = 5;
            var rng = new FixedRandom { Offset = 3 };
            Assert.Equal(5, ReviewQueue.PickDelay(cfg, false, rng), "min = max needs no draw");
            Assert.Equal(0, rng.Draws);

            cfg.DelayMinDays = 9;
            cfg.DelayMaxDays = 3;
            Assert.Equal(9, ReviewQueue.PickDelay(cfg, false, rng), "max below min counts as min");

            cfg.DelayMinDays = -4;
            cfg.DelayMaxDays = -1;
            Assert.Equal(0, ReviewQueue.PickDelay(cfg, false, rng), "no negative wait");
            Assert.Equal(7, ReviewQueue.PickDelay(null, false, new FixedRandom()), "no config: the design's 7 days");
            Assert.Equal(7, ReviewQueue.PickDelay(ReviewConfig.CreateDefault(), false, null), "no stream: the shortest wait");
        }

        // ---- the entry ----

        [Test]
        public void Enqueue_WritesTheDatesAndStartsEmpty()
        {
            var state = new PressState();
            PendingReview entry = ReviewQueue.Enqueue(state, 5120, "player", Day, 14);

            Assert.Equal(5120, entry.cpuSaveId);
            Assert.Equal("player", entry.companyKey);
            Assert.Equal("1995-03-01", entry.releasedOn);
            Assert.Equal("1995-03-15", entry.publishOn);
            Assert.Equal(new[] { 0, 0, 0, 0 }, entry.soldBySegment);
            Assert.Equal(0, entry.attempts);
            Assert.True(ReferenceEquals(entry, state.pending[0]), "the entry is in the queue");
        }

        [Test]
        public void Enqueue_InstantMode_IsDueTheSameDay()
        {
            var state = new PressState();
            PendingReview entry = ReviewQueue.Enqueue(state, 1, "ai:2", Day, 0);
            Assert.Equal(entry.releasedOn, entry.publishOn);
            Assert.Equal(1, ReviewQueue.DueOn(state, Day).Count);

            Assert.Equal("1995-03-01", ReviewQueue.Enqueue(state, 2, "ai:2", Day, -5).publishOn, "a negative wait is no wait");
        }

        [Test]
        public void Enqueue_AtTheLimit_LetsTheOldestGo()
        {
            var state = new PressState();
            for (int i = 0; i < PressStateCodec.MaxPending; i++) ReviewQueue.Enqueue(state, i, "ai:1", Day, 10);
            Assert.Equal(PressStateCodec.MaxPending, state.pending.Count);

            ReviewQueue.Enqueue(state, 5000, "ai:1", Day, 10);
            Assert.Equal(PressStateCodec.MaxPending, state.pending.Count, "the queue does not grow past its limit");
            Assert.Equal(1, state.pending[0].cpuSaveId, "the oldest entry went");
            Assert.Equal(5000, state.pending[state.pending.Count - 1].cpuSaveId, "the new one is last");
        }

        // ---- the day it is due ----

        [Test]
        public void DueOn_ReviewIsDueOnItsDay_NotBefore()
        {
            var state = new PressState();
            ReviewQueue.Enqueue(state, 10, "player", Day, 10);

            Assert.Equal(0, ReviewQueue.DueOn(state, Day.AddDays(9)).Count, "the day before");
            Assert.Equal(1, ReviewQueue.DueOn(state, Day.AddDays(10)).Count, "on the day");
            Assert.Equal(1, ReviewQueue.DueOn(state, Day.AddDays(10).AddHours(15)).Count, "the time of the day does not matter");
        }

        [Test]
        public void DueOn_AReviewThatWasOverdue_IsDueAtTheNextTick_AFrozenQueueCatchesUp()
        {
            var state = new PressState();
            ReviewQueue.Enqueue(state, 10, "player", Day, 10);
            Assert.Equal(1, ReviewQueue.DueOn(state, Day.AddDays(40)).Count, "the feature was off for a month: the review is written when it is on again");
        }

        [Test]
        public void DueOn_KeepsTheOrderOfTheQueue_AndSkipsTheOnesNotDue()
        {
            var state = new PressState();
            ReviewQueue.Enqueue(state, 1, "ai:1", Day, 5);
            ReviewQueue.Enqueue(state, 2, "ai:1", Day, 20);
            ReviewQueue.Enqueue(state, 3, "ai:1", Day, 5);

            List<PendingReview> due = ReviewQueue.DueOn(state, Day.AddDays(6));
            Assert.Equal(2, due.Count);
            Assert.Equal(1, due[0].cpuSaveId);
            Assert.Equal(3, due[1].cpuSaveId);
            Assert.Equal(0, ReviewQueue.DueOn(null, Day).Count, "no state, nothing due");
        }

        [Test]
        public void DueOn_ADateThatDoesNotParse_IsDue_SoItCannotStickInTheQueueForever()
        {
            var state = new PressState();
            state.pending.Add(new PendingReview { cpuSaveId = 9, companyKey = "ai:1", releasedOn = "1995-03-01", publishOn = "soon" });
            Assert.Equal(1, ReviewQueue.DueOn(state, Day).Count);
        }

        // ---- the sales of a player's CPU ----

        [Test]
        public void AddSales_AddsUpTheDaysOfTheWait()
        {
            var state = new PressState();
            PendingReview entry = ReviewQueue.Enqueue(state, 10, "player", Day, 10);

            ReviewQueue.AddSales(entry, 100, 200, 0, 5);
            ReviewQueue.AddSales(entry, 50, 0, 30, 0);
            Assert.Equal(new[] { 150, 200, 30, 5 }, entry.soldBySegment);
        }

        [Test]
        public void AddSales_ANegativeNumberIsIgnored_AHugeSumDoesNotOverflow_AndABrokenArrayIsRepaired()
        {
            var entry = new PendingReview { cpuSaveId = 1, soldBySegment = new[] { int.MaxValue - 1, 0, 0, 0 } };
            ReviewQueue.AddSales(entry, 10, -50, 0, 0);
            Assert.Equal(int.MaxValue, entry.soldBySegment[0], "saturates");
            Assert.Equal(0, entry.soldBySegment[1], "a negative number adds nothing");

            entry.soldBySegment = null;
            ReviewQueue.AddSales(entry, 1, 2, 3, 4);
            Assert.Equal(new[] { 1, 2, 3, 4 }, entry.soldBySegment, "a missing array starts again");

            entry.soldBySegment = new[] { 1, 2 };
            ReviewQueue.AddSales(entry, 1, 1, 1, 1);
            Assert.Equal(new[] { 1, 1, 1, 1 }, entry.soldBySegment, "a short array starts again");

            ReviewQueue.AddSales(null, 1, 1, 1, 1);   // must not throw
        }

        [Test]
        public void TheSalesOfTheWait_DecideTheSegmentThroughTheCore()
        {
            // The queue stores the four sums; the rule of the core (ModSegments.FromSales is a call of SegmentRule.FromSales) turns them into a segment:
            // most sales wins, a tie goes to the smaller index.
            var state = new PressState();
            PendingReview entry = ReviewQueue.Enqueue(state, 10, "player", Day, 10);
            ReviewQueue.AddSales(entry, 100, 4300, 0, 0);
            Assert.Equal(Segment.MidRange, SegmentRule.FromSales(entry.soldBySegment[0], entry.soldBySegment[1], entry.soldBySegment[2], entry.soldBySegment[3]));
        }

        // ---- the attempts ----

        [Test]
        public void Fail_ThreeAttempts_ThenTheReviewIsCancelled()
        {
            var state = new PressState();
            PendingReview entry = ReviewQueue.Enqueue(state, 10, "player", Day, 10);

            Assert.False(ReviewQueue.Fail(state, entry), "the first failure");
            Assert.Equal(1, entry.attempts);
            Assert.Equal(1, state.pending.Count, "still waiting for tomorrow");
            Assert.False(ReviewQueue.Fail(state, entry), "the second failure");
            Assert.True(ReviewQueue.Fail(state, entry), "the third failure cancels it");
            Assert.Equal(0, state.pending.Count, "and takes it out of the queue");
            Assert.Equal(3, ReviewQueue.MaxAttempts);
            Assert.False(ReviewQueue.Fail(state, null), "nothing to fail");
        }

        [Test]
        public void Cancel_TakesTheEntryOut_OnlyOnce()
        {
            var state = new PressState();
            PendingReview entry = ReviewQueue.Enqueue(state, 10, "player", Day, 10);
            PendingReview other = ReviewQueue.Enqueue(state, 11, "player", Day, 10);

            Assert.True(ReviewQueue.Cancel(state, entry));
            Assert.False(ReviewQueue.Cancel(state, entry), "already gone");
            Assert.False(ReviewQueue.Cancel(null, entry));
            Assert.False(ReviewQueue.Cancel(state, null));
            Assert.Equal(1, state.pending.Count);
            Assert.True(ReferenceEquals(other, state.pending[0]));
        }

        // ---- the archive ----

        [Test]
        public void Publish_MovesTheEntryToTheArchive_AndTheCpuIsKnownAfterwards()
        {
            var state = new PressState();
            PendingReview entry = ReviewQueue.Enqueue(state, 10, "player", Day, 10);
            ReviewRecord record = Record(10);

            ReviewQueue.Publish(state, entry, record, new ArchiveConfig());
            Assert.Equal(0, state.pending.Count, "the entry left the queue");
            Assert.Equal(1, state.reviews.Count);
            Assert.True(ReferenceEquals(record, state.reviews[0]));
            Assert.Equal(QueueVerdict.AlreadyKnown, ReviewQueue.Admit(state, 10, "player", false, false, true), "one review per CPU");
        }

        [Test]
        public void Publish_KeepsTheArchiveWithinItsLimit_TheOldestGoFirst()
        {
            var state = new PressState();
            var archive = new ArchiveConfig { MaxFull = 2, MaxTotal = 3 };
            for (int i = 1; i <= 5; i++) ReviewQueue.Publish(state, null, Record(i), archive);

            Assert.Equal(3, state.reviews.Count);
            Assert.Equal(3, state.reviews[0].cpuSaveId);
            Assert.Equal(5, state.reviews[2].cpuSaveId);

            ReviewQueue.Publish(state, null, Record(6), new ArchiveConfig { MaxTotal = 0 });
            Assert.Equal(1, state.reviews.Count, "a limit below 1 still keeps the newest review");
        }

        // ---- from the release to the review ----

        [Test]
        public void ARelease_IsQueuedWithAWait_AndTheReviewComesOnTheDay()
        {
            var state = new PressState();
            ReviewConfig cfg = ReviewConfig.CreateDefault();
            var rng = new FixedRandom { Offset = 3 };   // 10 days

            Assert.Equal(QueueVerdict.Queue, ReviewQueue.Admit(state, 10, "player", false, false, true));
            int delay = ReviewQueue.PickDelay(cfg, false, rng);
            ReviewQueue.Enqueue(state, 10, "player", Day, delay);
            Assert.Equal(10, delay);

            for (int d = 1; d < 10; d++) Assert.Equal(0, ReviewQueue.DueOn(state, Day.AddDays(d)).Count, "day " + d);
            List<PendingReview> due = ReviewQueue.DueOn(state, Day.AddDays(10));
            Assert.Equal(1, due.Count, "day 10");

            ReviewQueue.Publish(state, due[0], Record(10, "1995-03-11"), cfg.Archive);
            Assert.Equal(0, ReviewQueue.DueOn(state, Day.AddDays(11)).Count, "nothing is left to publish");
        }
    }
}
