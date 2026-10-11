using System;
using System.Collections.Generic;
using PTMod.Core;
using PTMod.Features.ScenariosAchievements.Logic;
using static PTMod.Tests.Features.ScenariosAchievements.RunTestData;

namespace PTMod.Tests.Features.ScenariosAchievements
{
    /// <summary>
    /// TSK-016: the arithmetic of <c>GameMetricsSource.SampleDay</c> - one pass over the CPUs of the game folded into a <see cref="DaySample"/>
    /// (sales of the player and of the market by segment, missed sales, the best <c>Ips</c> of the rivals on sale, the popularity of the player's own
    /// CPUs, the list of the player's released CPUs), the exclusion of the CPUs that came by a transfer, and the pool that keeps the pass free of
    /// allocations. The feeding of the metrics (the streaks, the shares over 30 days) is checked with the calculator.
    /// </summary>
    [Covers(typeof(DaySampler), typeof(CpuRow))]
    public class DaySamplerTests
    {
        private static CpuRow Row(int id, bool player, bool released = true, bool retired = false, long sold = 0, long high = 0, long mid = 0, long low = 0,
                                  long industries = 0, int missed = 0, double ips = 0, double popularity = 0, float frequency = 0, int cores = 1)
        {
            return new CpuRow
            {
                SaveId = id,
                Name = "cpu-" + id,
                IsPlayerCpu = player,
                IsReleased = released,
                IsRetired = retired,
                Sold = sold,
                SoldHighEnd = high,
                SoldMidRange = mid,
                SoldLowEnd = low,
                SoldIndustries = industries,
                MissedSales = missed,
                Ips = ips,
                Popularity = popularity,
                Frequency = frequency,
                Cores = cores,
            };
        }

        private static DaySample Sample(DaySampler sampler, HashSet<int> transferred, params CpuRow[] rows)
        {
            var day = new DaySample();
            sampler.Begin(day, transferred);
            for (int i = 0; i < rows.Length; i++) sampler.Add(ref rows[i]);
            sampler.End();
            return day;
        }

        // ---- the sales ----

        [Test]
        public void TheSales_AreSummedByColumn_ForThePlayerAndForTheWholeMarket()
        {
            DaySample day = Sample(new DaySampler(), null,
                Row(1, true, sold: 100, high: 10, mid: 60, low: 20, industries: 10),
                Row(2, true, sold: 50, high: 0, mid: 50),
                Row(3, false, sold: 300, high: 100, mid: 100, low: 50, industries: 50),
                Row(4, false, sold: 550, high: 200, mid: 100, low: 100, industries: 150));

            // column 0 is every segment, 1 + (int)Segment the four segments
            Assert.Equal(new long[] { 150, 10, 110, 20, 10 }, day.PlayerSold);
            Assert.Equal(new long[] { 1000, 310, 310, 170, 210 }, day.TotalSold);
            Assert.Equal(RollingWindow.ColumnOf(Segment.MidRange), 2);
            Assert.Equal(110L, day.PlayerSold[RollingWindow.ColumnOf(Segment.MidRange)]);
        }

        [Test]
        public void TheSamplesOfTwoDays_DoNotLeakIntoEachOther_WhenTheInstanceIsReused()
        {
            var sampler = new DaySampler();
            var day = new DaySample();

            CpuRow a = Row(1, true, sold: 100, high: 100, missed: 7, ips: 5, popularity: 0.9);
            CpuRow b = Row(2, false, sold: 40, high: 40, ips: 999);
            sampler.Begin(day, null);
            sampler.Add(ref a);
            sampler.Add(ref b);
            sampler.End();
            Assert.Equal(7, day.MissedSales);
            Assert.Equal(999.0, day.RivalBestIps);
            Assert.Equal(0.9, day.MaxPopularity);
            Assert.Equal(1, day.PlayerCpus.Count);

            sampler.Begin(day, null);
            sampler.End();
            Assert.Equal(new long[] { 0, 0, 0, 0, 0 }, day.PlayerSold);
            Assert.Equal(new long[] { 0, 0, 0, 0, 0 }, day.TotalSold);
            Assert.Equal(0, day.MissedSales);
            Assert.Equal(0.0, day.RivalBestIps);
            Assert.Equal(0.0, day.MaxPopularity);
            Assert.Equal(0, day.PlayerCpus.Count);
        }

        [Test]
        public void ShortOrMissingArraysOfTheSample_AreMadeAgain()
        {
            var day = new DaySample { PlayerSold = new long[2], TotalSold = null };
            var sampler = new DaySampler();
            sampler.Begin(day, null);
            CpuRow row = Row(1, true, sold: 5, industries: 5);
            sampler.Add(ref row);
            sampler.End();

            Assert.Equal(RollingWindow.Columns, day.PlayerSold.Length);
            Assert.Equal(RollingWindow.Columns, day.TotalSold.Length);
            Assert.Equal(5L, day.PlayerSold[0]);
            Assert.Equal(5L, day.TotalSold[4]);
        }

        [Test]
        public void TheMissedSales_AreThePlayersOnly()
        {
            DaySample day = Sample(new DaySampler(), null,
                Row(1, true, sold: 10, missed: 3),
                Row(2, true, sold: 10, missed: 4),
                Row(3, false, sold: 10, missed: 100));
            Assert.Equal(7, day.MissedSales);
        }

        // ---- the rivals ----

        [Test]
        public void TheBestIpsOfTheRivals_LooksAtReleasedCpusOnSaleOnly()
        {
            DaySample day = Sample(new DaySampler(), null,
                Row(1, false, ips: 50),
                Row(2, false, ips: 80, released: false),     // still in development
                Row(3, false, ips: 90, retired: true),       // withdrawn
                Row(4, false, ips: 70),
                Row(5, true, ips: 500));                     // the player's own, not a rival
            Assert.Equal(70.0, day.RivalBestIps);
        }

        [Test]
        public void NoRivalsOnSale_MeanARivalIpsOfZero()
        {
            DaySample day = Sample(new DaySampler(), null, Row(1, true, ips: 10), Row(2, false, ips: 10, retired: true));
            Assert.Equal(0.0, day.RivalBestIps);
        }

        // ---- the player's CPUs ----

        [Test]
        public void ThePlayersReleasedCpus_AreListed_WithTheirCharacteristics_RetiredOnesToo()
        {
            DaySample day = Sample(new DaySampler(), null,
                Row(1, true, frequency: 20000, cores: 2, ips: 40),
                Row(2, true, retired: true, frequency: 5000),
                Row(3, true, released: false, frequency: 99999),   // not released: not listed
                Row(4, false, frequency: 77777));                  // a rival's

            Assert.Equal(2, day.PlayerCpus.Count);
            Assert.Equal(1, day.PlayerCpus[0].SaveId);
            Assert.Equal(20000f, day.PlayerCpus[0].Frequency);
            Assert.Equal(2, day.PlayerCpus[0].Cores);
            Assert.Equal(40.0, day.PlayerCpus[0].Ips);
            Assert.True(day.PlayerCpus[0].OnMarket);
            Assert.Equal(2, day.PlayerCpus[1].SaveId);
            Assert.False(day.PlayerCpus[1].OnMarket, "a withdrawn CPU is out of the performance crown but still released");
            Assert.Equal("cpu-1", day.PlayerCpus[0].Name);
        }

        [Test]
        public void ThePopularity_IsTheHighestOfThePlayersCpusOnSale()
        {
            DaySample day = Sample(new DaySampler(), null,
                Row(1, true, popularity: 0.30),
                Row(2, true, popularity: 0.55),
                Row(3, true, popularity: 0.90, retired: true),   // withdrawn: not counted
                Row(4, false, popularity: 0.99));                // a rival's
            Assert.Equal(0.55, day.MaxPopularity);
        }

        // ---- the transfers ----

        [Test]
        public void ACpuReceivedByATransfer_StaysOutOfThePopularity_ButItsSalesAreThePlayersAndItIsListed()
        {
            var transferred = new HashSet<int> { 2 };
            DaySample day = Sample(new DaySampler(), transferred,
                Row(1, true, sold: 10, mid: 10, popularity: 0.2),
                Row(2, true, sold: 90, mid: 90, popularity: 0.95));

            Assert.Equal(0.2, day.MaxPopularity, "the transferred CPU's popularity is not the player's");
            Assert.Equal(100L, day.PlayerSold[0], "the game counts the sales of a CPU for the company it belongs to");
            Assert.Equal(2, day.PlayerCpus.Count, "the calculator leaves it out of the characteristics");

            // and so the metrics do: the transferred CPU is not released, not fast, not many-cored
            DaySample withCharacteristics = Sample(new DaySampler(), transferred,
                Row(1, true, frequency: 1000, cores: 1),
                Row(2, true, frequency: 900000, cores: 8));
            var state = new MetricsState();
            state.TransferredCpuIds.Add(2);
            withCharacteristics.Date = D(1990, 1, 1);
            MetricsCalculator.ApplyDay(state, withCharacteristics);
            MetricsSnapshot snapshot = MetricsCalculator.Snapshot(state, withCharacteristics, null);
            Assert.Equal(1.0, snapshot.Get("cpu.released.count"));
            Assert.Equal(1000.0, snapshot.Get("cpu.maxFrequency"));
            Assert.Equal(1.0, snapshot.Get("cpu.maxCores"));
        }

        // ---- the pool ----

        [Test]
        public void ThePool_GrowsToTheSizeOfThePlayersList_AndIsReusedAfterwards()
        {
            var sampler = new DaySampler();
            var day = new DaySample();
            var rows = new CpuRow[40];
            for (int i = 0; i < rows.Length; i++) rows[i] = Row(i, i % 2 == 0, sold: 1);

            sampler.Begin(day, null);
            for (int i = 0; i < rows.Length; i++) sampler.Add(ref rows[i]);
            sampler.End();
            int pool = sampler.PoolSize;
            Assert.Equal(20, pool, "one per CPU of the player");
            CpuInfo first = day.PlayerCpus[0];

            sampler.Begin(day, null);
            for (int i = 0; i < rows.Length; i++) sampler.Add(ref rows[i]);
            sampler.End();
            Assert.Equal(pool, sampler.PoolSize, "no new objects the second day");
            Assert.True(ReferenceEquals(first, day.PlayerCpus[0]), "the same objects are filled again");
        }

        [Test]
        public void APooledObject_IsFilledAgain_NothingOfTheOldCpuStays()
        {
            var sampler = new DaySampler();
            var day = new DaySample();
            CpuRow a = Row(1, true, frequency: 100, cores: 4, ips: 9);
            a.Smt = true;
            a.Power = 99;
            sampler.Begin(day, null);
            sampler.Add(ref a);
            day.PlayerCpus[0].CompanyType = "idm";   // the release event's copy carries these; a sample's must not keep them
            day.PlayerCpus[0].ReleasedOn = D(1980, 1, 1);
            sampler.End();

            CpuRow b = Row(7, true, frequency: 5, cores: 1);
            sampler.Begin(day, null);
            sampler.Add(ref b);
            sampler.End();

            CpuInfo info = day.PlayerCpus[0];
            Assert.Equal(7, info.SaveId);
            Assert.Equal(5f, info.Frequency);
            Assert.Equal(1, info.Cores);
            Assert.False(info.Smt);
            Assert.Equal(0f, info.Power);
            Assert.Null(info.CompanyType);
            Assert.Equal(default(DateTime), info.ReleasedOn);
        }

        [Test]
        public void AddWithoutBegin_Throws_AndBeginWithoutASample_Throws()
        {
            var sampler = new DaySampler();
            CpuRow row = Row(1, true);
            Assert.Throws<InvalidOperationException>(() => sampler.Add(ref row));
            Assert.Throws<ArgumentNullException>(() => sampler.Begin(null, null));
        }

        // ---- the whole day goes through the calculator ----

        [Test]
        public void ADaySample_FeedsTheShares_AndTheLeadInIps()
        {
            var state = new MetricsState();
            var sampler = new DaySampler();
            var day = new DaySample { Date = D(1985, 3, 1), Money = 5000 };
            for (int i = 0; i < 30; i++)
            {
                sampler.Begin(day, null);
                CpuRow mine = Row(1, true, sold: 15, mid: 15, ips: 200, frequency: 8000);
                CpuRow theirs = Row(2, false, sold: 85, mid: 85, ips: 100);
                sampler.Add(ref mine);
                sampler.Add(ref theirs);
                sampler.End();
                MetricsCalculator.ApplyDay(state, day);
            }

            MetricsSnapshot m = MetricsCalculator.Snapshot(state, day, null);
            Assert.Near(0.15, m.Get("share.total.avg30"), 1e-9);
            Assert.Near(0.15, m.Get("share.midrange.avg30"), 1e-9);
            Assert.Near(0.0, m.Get("share.highend.avg30"), 1e-9);
            Assert.Equal(30.0, m.Get("ips.leaderStreakDays"), "the player's CPU is the fastest every day");
            Assert.Equal(30.0, m.Get("money.solventStreakDays"));
        }
    }
}
