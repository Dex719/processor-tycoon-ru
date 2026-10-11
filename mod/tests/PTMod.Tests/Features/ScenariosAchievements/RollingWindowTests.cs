using System;
using PTMod.Core;
using PTMod.Features.ScenariosAchievements.Logic;

namespace PTMod.Tests.Features.ScenariosAchievements
{
    /// <summary>
    /// TSK-006: the ring of 30 days that the shares of the market are made of - it fills, it turns over, a share is the sum of the player
    /// over the sum of the market, a market that sold nothing gives 0, the columns follow <c>1 + (int)Segment</c>, and the save codec can
    /// rebuild a window from its raw slots.
    /// </summary>
    [Covers(typeof(RollingWindow))]
    public class RollingWindowTests
    {
        private static long[] Row(long all, long high = 0, long mid = 0, long low = 0, long industries = 0)
        {
            return RunTestData.Sales(all, high, mid, low, industries);
        }

        [Test]
        public void TheDefaultWindow_IsThirtyDays_AndStartsEmpty()
        {
            var window = new RollingWindow();
            Assert.Equal(30, window.Days);
            Assert.Equal(0, window.Head);
            for (int column = 0; column < RollingWindow.Columns; column++)
            {
                Assert.Equal(0L, window.PlayerSum(column));
                Assert.Equal(0L, window.TotalSum(column));
                Assert.Equal(0.0, window.Share(column));
            }
        }

        [Test]
        public void ADayIsWrittenAtTheHead_AndTheHeadMoves()
        {
            var window = new RollingWindow(3);
            window.Push(Row(1), Row(10));
            Assert.Equal(1, window.Head);
            window.Push(Row(2), Row(20));
            window.Push(Row(3), Row(30));
            Assert.Equal(0, window.Head, "after a full turn the head is back at the first slot");
            Assert.Equal(1L, window.PlayerAt(0, 0));
            Assert.Equal(20L, window.TotalAt(1, 0));
            Assert.Equal(3L, window.PlayerAt(2, 0));
        }

        [Test]
        public void TheOldestDayFallsOut_WhenTheRingTurnsOver()
        {
            var window = new RollingWindow(3);
            window.Push(Row(100), Row(1000)); // will fall out
            window.Push(Row(2), Row(20));
            window.Push(Row(3), Row(30));
            Assert.Equal(105L, window.PlayerSum(0));
            Assert.Equal(1050L, window.TotalSum(0));

            window.Push(Row(4), Row(40)); // the first day is overwritten
            Assert.Equal(9L, window.PlayerSum(0));
            Assert.Equal(90L, window.TotalSum(0));
        }

        [Test]
        public void ThirtyFiveDays_LeaveTheLastThirty()
        {
            var window = new RollingWindow();
            for (int day = 1; day <= 35; day++) window.Push(Row(day), Row(100));

            // days 6..35 are in the ring
            long expected = 0;
            for (int day = 6; day <= 35; day++) expected += day;
            Assert.Equal(expected, window.PlayerSum(0));
            Assert.Equal(3000L, window.TotalSum(0));
            Assert.Equal(5, window.Head);
        }

        [Test]
        public void AShare_IsTheSumOfThePlayerOverTheSumOfTheMarket_NotAnAverageOfDailyShares()
        {
            var window = new RollingWindow();
            window.Push(Row(1), Row(100)); // 1% on a big day
            window.Push(Row(9), Row(10));  // 90% on a small day
            Assert.Near(10.0 / 110.0, window.Share(0), 1e-12);
        }

        [Test]
        public void AShare_IsZero_WhenTheMarketSoldNothing()
        {
            var window = new RollingWindow();
            window.Push(Row(0), Row(0));
            window.Push(Row(5), Row(0)); // nonsense input (the player sold on a dead market): still no division by zero
            Assert.Equal(0.0, window.Share(0));
            Assert.Equal(0.0, window.Share(RollingWindow.ColumnOf(Segment.HighEnd)));
        }

        [Test]
        public void AShare_NeverExceedsOne()
        {
            var window = new RollingWindow();
            window.Push(Row(50), Row(10)); // the player "sold more than the market": clamp, do not report 500%
            Assert.Equal(1.0, window.Share(0));
        }

        [Test]
        public void EachColumnIsItsOwnShare()
        {
            var window = new RollingWindow();
            window.Push(Row(10, high: 1, mid: 2, low: 3, industries: 4), Row(40, high: 10, mid: 10, low: 10, industries: 10));
            Assert.Near(0.25, window.Share(0), 1e-12);
            Assert.Near(0.1, window.Share(RollingWindow.ColumnOf(Segment.HighEnd)), 1e-12);
            Assert.Near(0.2, window.Share(RollingWindow.ColumnOf(Segment.MidRange)), 1e-12);
            Assert.Near(0.3, window.Share(RollingWindow.ColumnOf(Segment.LowEnd)), 1e-12);
            Assert.Near(0.4, window.Share(RollingWindow.ColumnOf(Segment.Industries)), 1e-12);
        }

        [Test]
        public void ColumnOf_FollowsTheSegmentNumbers()
        {
            Assert.Equal(0, RollingWindow.ColumnOf(Segment.Any));
            Assert.Equal(1, RollingWindow.ColumnOf(Segment.HighEnd));
            Assert.Equal(2, RollingWindow.ColumnOf(Segment.MidRange));
            Assert.Equal(3, RollingWindow.ColumnOf(Segment.LowEnd));
            Assert.Equal(4, RollingWindow.ColumnOf(Segment.Industries));
            Assert.Equal(-1, RollingWindow.ColumnOf((Segment)7));
            Assert.Equal(-1, RollingWindow.ColumnOf((Segment)(-5)));
        }

        [Test]
        public void ShortOrMissingArrays_CountAsZeros()
        {
            var window = new RollingWindow(2);
            window.Push(new long[] { 7 }, null);
            Assert.Equal(7L, window.PlayerSum(0));
            Assert.Equal(0L, window.PlayerSum(1));
            Assert.Equal(0L, window.TotalSum(0));
        }

        [Test]
        public void Clear_EmptiesTheRingAndTheHead()
        {
            var window = new RollingWindow(4);
            window.Push(Row(3), Row(9));
            window.Push(Row(3), Row(9));
            window.Clear();
            Assert.Equal(0, window.Head);
            Assert.Equal(0L, window.PlayerSum(0));
            Assert.Equal(0L, window.TotalSum(0));
        }

        [Test]
        public void ARestoredWindow_HasTheSameSlotsHeadAndShares()
        {
            var original = new RollingWindow();
            for (int day = 1; day <= 41; day++) original.Push(Row(day % 7, day % 3, day % 5, day % 2, day % 4), Row(50, 10, 10, 10, 20));

            long[][] player = new long[original.Days][];
            long[][] total = new long[original.Days][];
            for (int slot = 0; slot < original.Days; slot++)
            {
                player[slot] = new long[RollingWindow.Columns];
                total[slot] = new long[RollingWindow.Columns];
                for (int column = 0; column < RollingWindow.Columns; column++)
                {
                    player[slot][column] = original.PlayerAt(slot, column);
                    total[slot][column] = original.TotalAt(slot, column);
                }
            }

            RollingWindow copy = RollingWindow.Restore(original.Days, original.Head, player, total);
            Assert.Equal(original.Days, copy.Days);
            Assert.Equal(original.Head, copy.Head);
            for (int column = 0; column < RollingWindow.Columns; column++)
            {
                Assert.Equal(original.Share(column), copy.Share(column), "share of column " + column);
            }

            // the next day lands in the same slot in both
            original.Push(Row(5), Row(5));
            copy.Push(Row(5), Row(5));
            Assert.Equal(original.PlayerSum(0), copy.PlayerSum(0));
        }

        [Test]
        public void Restore_ToleratesADamagedSave()
        {
            // too few rows, a short row, a head far outside the ring, a nonsense length
            RollingWindow window = RollingWindow.Restore(5, 12, new[] { new long[] { 4, 1 } }, new[] { new long[] { 8 } });
            Assert.Equal(5, window.Days);
            Assert.Equal(2, window.Head, "12 wraps around a ring of 5");
            Assert.Equal(4L, window.PlayerSum(0));
            Assert.Equal(1L, window.PlayerSum(1));
            Assert.Equal(8L, window.TotalSum(0));

            RollingWindow negative = RollingWindow.Restore(5, -1, null, null);
            Assert.Equal(4, negative.Head);

            RollingWindow noLength = RollingWindow.Restore(0, 0, null, null);
            Assert.Equal(RollingWindow.DefaultDays, noLength.Days);
        }

        [Test]
        public void SetSlot_WritesWithoutMovingTheHead()
        {
            var window = new RollingWindow(4);
            window.SetSlot(2, Row(9), Row(18));
            Assert.Equal(0, window.Head);
            Assert.Equal(9L, window.PlayerAt(2, 0));
            Assert.Near(0.5, window.Share(0), 1e-12);
        }

        [Test]
        public void BadArguments_Throw()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new RollingWindow(0));
            var window = new RollingWindow(3);
            Assert.Throws<ArgumentOutOfRangeException>(() => window.PlayerSum(-1));
            Assert.Throws<ArgumentOutOfRangeException>(() => window.TotalSum(RollingWindow.Columns));
            Assert.Throws<ArgumentOutOfRangeException>(() => window.PlayerAt(3, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => window.TotalAt(0, 9));
            Assert.Throws<ArgumentOutOfRangeException>(() => window.SetSlot(-1, null, null));
        }
    }
}
