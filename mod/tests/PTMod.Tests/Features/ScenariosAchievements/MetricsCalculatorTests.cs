using System;
using System.Collections.Generic;
using PTMod.Features.ScenariosAchievements.Logic;
using static PTMod.Tests.Features.ScenariosAchievements.RunTestData;

namespace PTMod.Tests.Features.ScenariosAchievements
{
    /// <summary>
    /// TSK-006: every metric of the table of design.md is computed from the day and month samples - the streaks (profit, solvency, negative
    /// days, days without missed sales, the lead in performance), <c>money.min</c>, the exit from default, the shares, the minimum over segments, the
    /// characteristics of the CPUs without those that came by a transfer - and the snapshot says which metrics it does not know yet.
    /// </summary>
    [Covers(typeof(MetricsCalculator), typeof(MetricsState), typeof(MetricsSnapshot), typeof(DaySample), typeof(MonthSample), typeof(CpuInfo))]
    public class MetricsCalculatorTests
    {
        private static double Get(MetricsState state, string metric, DaySample day = null, MonthSample month = null)
        {
            MetricsSnapshot snapshot = MetricsCalculator.Snapshot(state, day, month);
            Assert.True(snapshot.Has(metric), metric + " is not known");
            return snapshot.Get(metric);
        }

        // ---- money ----

        [Test]
        public void SolventAndNegativeStreaks_CountDaysInARow_AndZeroIsSolvent()
        {
            var state = new MetricsState();
            double[] money = { 5, 0, 7, -1, -2, -3, 4 };
            int[] solvent = { 1, 2, 3, 0, 0, 0, 1 };
            int[] negative = { 0, 0, 0, 1, 2, 3, 0 };
            for (int i = 0; i < money.Length; i++)
            {
                MetricsCalculator.ApplyDay(state, Day(D(1980, 1, 1 + i), money[i]));
                Assert.Equal(solvent[i], state.SolventStreakDays, "solvent after day " + i);
                Assert.Equal(negative[i], state.NegativeStreakDays, "negative after day " + i);
            }
            MetricsSnapshot snapshot = MetricsCalculator.Snapshot(state, Day(D(1980, 1, 7), 4), null);
            Assert.Equal(1.0, snapshot.Get("money.solventStreakDays"));
            Assert.Equal(0.0, snapshot.Get("money.negativeStreakDays"));
        }

        [Test]
        public void MoneyMin_IsTheLowestMoneyOfTheRun_AndIsUnknownBeforeTheFirstDay()
        {
            var state = new MetricsState();
            Assert.False(MetricsCalculator.Snapshot(state, null, null).Has("money.min"), "no day yet");

            foreach (double money in new[] { 50.0, -20.0, 10.0, -5.0, 300.0 })
            {
                MetricsCalculator.ApplyDay(state, Day(D(1980, 1, 1), money));
            }
            Assert.Equal(-20.0, state.MinMoney);
            Assert.Equal(-20.0, Get(state, "money.min"));
            Assert.Equal(300.0, Get(state, "money", Day(D(1980, 1, 5), 300)));
        }

        // ---- profit ----

        [Test]
        public void ProfitStreak_GrowsOnProfitableMonths_AndStartsOverOnAnyOther()
        {
            var state = new MetricsState();
            MetricsCalculator.ApplyMonth(state, Month(1980, 1, 100, 40));
            MetricsCalculator.ApplyMonth(state, Month(1980, 2, 100, 99));
            MetricsCalculator.ApplyMonth(state, Month(1980, 3, 100, 10));
            Assert.Equal(3, state.ProfitStreakMonths);

            MetricsCalculator.ApplyMonth(state, Month(1980, 4, 100, 100)); // exactly zero is not a profit
            Assert.Equal(0, state.ProfitStreakMonths);

            MetricsCalculator.ApplyMonth(state, Month(1980, 5, 100, 50));
            MetricsCalculator.ApplyMonth(state, Month(1980, 6, 10, 50)); // a loss
            Assert.Equal(0, state.ProfitStreakMonths);
        }

        [Test]
        public void AMonth_IsCountedOnce_AndAMonthWithoutAReportIsIgnored()
        {
            var state = new MetricsState();
            MonthSample january = Month(1980, 1, 100, 10);
            MetricsCalculator.ApplyMonth(state, january);
            MetricsCalculator.ApplyMonth(state, january);
            MetricsCalculator.ApplyMonth(state, Month(1979, 12, 100, 10)); // an older report
            Assert.Equal(1, state.ProfitStreakMonths, "the same or an older month does not count again");

            MetricsCalculator.ApplyMonth(state, new MonthSample { Year = 1980, Month = 2, HasReport = false });
            Assert.Equal(1, state.ProfitStreakMonths);
            MetricsCalculator.ApplyMonth(state, Month(1980, 2, 100, 10));
            Assert.Equal(2, state.ProfitStreakMonths, "a month that had no report can still be counted when the report appears");
        }

        [Test]
        public void TheMonthMetrics_ComeFromTheMonthSample()
        {
            var state = new MetricsState();
            MonthSample month = Month(1981, 4, 500, 200, revenue12m: 4800, clients12m: 1200);
            MetricsCalculator.ApplyMonth(state, month);

            Assert.Equal(300.0, Get(state, "profit.lastMonth", null, month));
            Assert.Equal(4800.0, Get(state, "revenue.last12m", null, month));
            Assert.Equal(1200.0, Get(state, "revenue.clients.last12m", null, month));
            Assert.Equal(1.0, Get(state, "profit.streakMonths"));

            MetricsSnapshot withoutMonth = MetricsCalculator.Snapshot(state, null, null);
            Assert.False(withoutMonth.Has("profit.lastMonth"));
            Assert.False(withoutMonth.Has("revenue.last12m"));
            Assert.False(withoutMonth.Has("revenue.clients.last12m"));
            Assert.False(MetricsCalculator.Snapshot(state, null, new MonthSample { Year = 1981, Month = 5, HasReport = false }).Has("profit.lastMonth"),
                "a month without a report is not a month");
        }

        // ---- sales, shares ----

        [Test]
        public void SalesTotal_AddsUpThePlayersSales()
        {
            var state = new MetricsState();
            MetricsCalculator.ApplyDay(state, Day(D(1980, 1, 1), 10, Sales(30, 30), Sales(100, 100)));
            MetricsCalculator.ApplyDay(state, Day(D(1980, 1, 2), 10, Sales(12, 0, 12), Sales(100, 0, 100)));
            Assert.Equal(42L, state.SalesTotal);
            Assert.Equal(42.0, Get(state, "sales.total"));
        }

        [Test]
        public void Shares_AreThirtyDayShares_PerSegment()
        {
            var state = new MetricsState();
            // 10 days: the player sells 10 of 100 in High End and 30 of 100 in Mid Range, nothing in the other two
            for (int day = 0; day < 10; day++)
            {
                MetricsCalculator.ApplyDay(state, Day(D(1985, 1, 1 + day), 10, Sales(40, 10, 30), Sales(300, 100, 100, 50, 50)));
            }
            MetricsSnapshot s = MetricsCalculator.Snapshot(state, Day(D(1985, 1, 10), 10), null);

            Assert.Near(40.0 / 300.0, s.Get("share.total.avg30"), 1e-12);
            Assert.Near(0.1, s.Get("share.highend.avg30"), 1e-12);
            Assert.Near(0.3, s.Get("share.midrange.avg30"), 1e-12);
            Assert.Equal(0.0, s.Get("share.lowend.avg30"));
            Assert.Equal(0.0, s.Get("share.industries.avg30"));
        }

        [Test]
        public void ShareMinSegment_IsTheSmallestOfTheFourSegmentShares()
        {
            var state = new MetricsState();
            MetricsCalculator.ApplyDay(state, Day(D(1985, 1, 1), 10, Sales(100, 40, 30, 20, 10), Sales(400, 100, 100, 100, 100)));
            // shares: 0.40, 0.30, 0.20, 0.10
            Assert.Near(0.10, Get(state, "share.minSegment.avg30"), 1e-12);

            var other = new MetricsState();
            MetricsCalculator.ApplyDay(other, Day(D(1985, 1, 1), 10, Sales(70, 40, 30, 0, 0), Sales(400, 100, 100, 100, 100)));
            Assert.Equal(0.0, Get(other, "share.minSegment.avg30"), "a segment with no sales of the player makes the minimum 0");
        }

        [Test]
        public void SharesWindow_ForgetsDaysOlderThanThirty()
        {
            var state = new MetricsState();
            MetricsCalculator.ApplyDay(state, Day(D(1985, 1, 1), 10, Sales(100), Sales(100))); // 100% on the first day
            for (int day = 0; day < 30; day++) MetricsCalculator.ApplyDay(state, Day(D(1985, 1, 2).AddDays(day), 10, Sales(0), Sales(100)));
            Assert.Equal(0.0, Get(state, "share.total.avg30"), "the first day fell out of the window");
        }

        // ---- days without missed sales ----

        [Test]
        public void MissedZeroStreak_NeedsSales_AndNoMissedSales()
        {
            var state = new MetricsState();
            Func<long, long, DaySample> day = (sold, missed) =>
            {
                DaySample d = Day(D(1990, 1, 1), 10, Sales(sold), Sales(1000));
                d.MissedSales = missed;
                return d;
            };

            MetricsCalculator.ApplyDay(state, day(5, 0));
            MetricsCalculator.ApplyDay(state, day(5, 0));
            Assert.Equal(2, state.MissedZeroStreakDays);

            MetricsCalculator.ApplyDay(state, day(5, 3)); // something was missed
            Assert.Equal(0, state.MissedZeroStreakDays);

            MetricsCalculator.ApplyDay(state, day(5, 0));
            MetricsCalculator.ApplyDay(state, day(0, 0)); // sold nothing at all: not a good day
            Assert.Equal(0, state.MissedZeroStreakDays);
            Assert.Equal(0.0, Get(state, "missed.zeroStreakDays"));
        }

        // ---- the lead in performance ----

        [Test]
        public void IpsLeaderStreak_CountsDaysWhenThePlayersFastestCpuBeatsEveryRival()
        {
            var state = new MetricsState();
            Func<double, double, DaySample> day = (playerIps, rivalIps) =>
            {
                DaySample d = Day(D(1995, 1, 1), 10);
                d.PlayerCpus.Add(Cpu(1, ips: playerIps));
                d.RivalBestIps = rivalIps;
                return d;
            };

            MetricsCalculator.ApplyDay(state, day(200, 100));
            MetricsCalculator.ApplyDay(state, day(210, 100));
            Assert.Equal(2, state.IpsLeaderStreakDays);

            MetricsCalculator.ApplyDay(state, day(210, 210)); // a tie is not a lead
            Assert.Equal(0, state.IpsLeaderStreakDays);

            MetricsCalculator.ApplyDay(state, day(300, 0)); // no rival CPU on the market at all: the player leads
            Assert.Equal(1, state.IpsLeaderStreakDays);
            Assert.Equal(1.0, Get(state, "ips.leaderStreakDays"));
        }

        [Test]
        public void IpsLeaderStreak_IgnoresWithdrawnAndTransferredCpus_AndAPlayerWithoutCpus()
        {
            var state = new MetricsState();
            MetricsCalculator.OnCpuTransferred(state, 7);

            DaySample d = Day(D(1995, 1, 1), 10);
            d.PlayerCpus.Add(Cpu(1, ips: 900, onMarket: false)); // withdrawn
            d.PlayerCpus.Add(Cpu(7, ips: 800));                  // came by a transfer
            d.PlayerCpus.Add(Cpu(2, ips: 50));
            d.RivalBestIps = 60;
            MetricsCalculator.ApplyDay(state, d);
            Assert.Equal(0, state.IpsLeaderStreakDays, "only the 50 counts, and a rival has 60");

            d.RivalBestIps = 40;
            MetricsCalculator.ApplyDay(state, d);
            Assert.Equal(1, state.IpsLeaderStreakDays);

            MetricsCalculator.ApplyDay(state, Day(D(1995, 1, 3), 10)); // no CPUs
            Assert.Equal(0, state.IpsLeaderStreakDays);
        }

        // ---- default ----

        [Test]
        public void DefaultRecovered_CountsTheExitsFromDefault_NotBankruptcies()
        {
            var state = new MetricsState();
            Func<bool, bool, DaySample> day = (inDefault, bankrupt) =>
            {
                DaySample d = Day(D(1986, 1, 1), -5);
                d.IsInDefault = inDefault;
                d.IsBankrupt = bankrupt;
                return d;
            };

            MetricsCalculator.ApplyDay(state, day(false, false));
            MetricsCalculator.ApplyDay(state, day(true, false));
            MetricsCalculator.ApplyDay(state, day(true, false));
            Assert.Equal(0, state.DefaultRecovered, "still in default");
            Assert.True(state.WasInDefaultYesterday);

            MetricsCalculator.ApplyDay(state, day(false, false));
            Assert.Equal(1, state.DefaultRecovered, "out of default");
            Assert.False(state.WasInDefaultYesterday);

            MetricsCalculator.ApplyDay(state, day(true, false));
            MetricsCalculator.ApplyDay(state, day(false, true)); // gone: bankrupt, not recovered
            Assert.Equal(1, state.DefaultRecovered);

            MetricsCalculator.ApplyDay(state, day(true, false));
            MetricsCalculator.ApplyDay(state, day(false, false));
            Assert.Equal(2, state.DefaultRecovered);
            Assert.Equal(2.0, Get(state, "default.recoveredCount"));
        }

        // ---- characteristics, transferred CPUs ----

        [Test]
        public void TheCpuMetrics_CountReleasedCpusWithWithdrawnOnes_AndTakeTheBestCharacteristics()
        {
            var state = new MetricsState();
            DaySample d = Day(D(2001, 1, 1), 10);
            d.PlayerCpus.Add(Cpu(1, frequency: 500000, cores: 1));
            d.PlayerCpus.Add(Cpu(2, frequency: 1500000, cores: 2, smt: true, onMarket: false)); // withdrawn, still released
            d.PlayerCpus.Add(Cpu(3, frequency: 900000, cores: 4));
            MetricsCalculator.ApplyDay(state, d);

            MetricsSnapshot s = MetricsCalculator.Snapshot(state, d, null);
            Assert.Equal(3.0, s.Get("cpu.released.count"));
            Assert.Equal(1500000.0, s.Get("cpu.maxFrequency"));
            Assert.Equal(4.0, s.Get("cpu.maxCores"));
            Assert.Equal(1.0, s.Get("cpu.hasSmt"));
        }

        [Test]
        public void TransferredCpus_AreLeftOutOfEveryCharacteristic()
        {
            var state = new MetricsState();
            MetricsCalculator.OnCpuTransferred(state, 2);
            MetricsCalculator.OnCpuTransferred(state, 2); // twice is the same CPU
            Assert.Equal(1, state.TransferredCpuIds.Count);

            DaySample d = Day(D(2001, 1, 1), 10);
            d.PlayerCpus.Add(Cpu(1, frequency: 200000, cores: 1));
            d.PlayerCpus.Add(Cpu(2, frequency: 3000000, cores: 8, smt: true)); // the absorbed rival's flagship
            MetricsCalculator.ApplyDay(state, d);

            MetricsSnapshot s = MetricsCalculator.Snapshot(state, d, null);
            Assert.Equal(1.0, s.Get("cpu.released.count"), "the transferred CPU is not a release");
            Assert.Equal(200000.0, s.Get("cpu.maxFrequency"));
            Assert.Equal(1.0, s.Get("cpu.maxCores"));
            Assert.Equal(0.0, s.Get("cpu.hasSmt"));
        }

        [Test]
        public void WithoutCpus_TheCharacteristicsAreZero_NotUnknown()
        {
            var state = new MetricsState();
            DaySample d = Day(D(1975, 1, 2), 10);
            MetricsSnapshot s = MetricsCalculator.Snapshot(state, d, null);
            foreach (string metric in new[] { "cpu.released.count", "cpu.maxFrequency", "cpu.maxCores", "cpu.hasSmt" })
            {
                Assert.True(s.Has(metric), metric);
                Assert.Equal(0.0, s.Get(metric), metric);
            }
        }

        // ---- counters of events ----

        [Test]
        public void TheCountersOfEvents_AreInTheSnapshot()
        {
            var state = new MetricsState();
            MetricsCalculator.OnOemSigned(state);
            MetricsCalculator.OnOemSigned(state);
            MetricsCalculator.OnRivalBankrupt(state);
            Assert.Equal(2.0, Get(state, "oem.signed.total"));
            Assert.Equal(1.0, Get(state, "rivals.bankruptCount"));
        }

        [Test]
        public void TheValuesOfTheDay_AreReadFromTheDaySample()
        {
            var state = new MetricsState();
            DaySample d = Day(D(1990, 5, 5), 123.5);
            d.MaxPopularity = 0.42;
            d.ActiveOemContracts = 4;
            d.FoundryClientsActive = 3;
            d.FoundryAsClientCount = 2;
            d.FactoryLines = 77;
            d.Difficulty = 3;
            d.RunDays = 99;
            MetricsCalculator.ApplyDay(state, d);

            MetricsSnapshot s = MetricsCalculator.Snapshot(state, d, null);
            Assert.Equal(123.5, s.Get("money"));
            Assert.Equal(0.42, s.Get("popularity.max"));
            Assert.Equal(4.0, s.Get("oem.active.count"));
            Assert.Equal(3.0, s.Get("foundry.clients.active"));
            Assert.Equal(2.0, s.Get("foundry.asClient.count"));
            Assert.Equal(77.0, s.Get("factory.lines"));
            Assert.Equal(3.0, s.Get("run.difficulty"));
            Assert.Equal(99.0, s.Get("run.days"));
        }

        // ---- the snapshot ----

        [Test]
        public void BeforeTheFirstDay_OnlyTheMetricsOfTheStateAreKnown()
        {
            MetricsSnapshot s = MetricsCalculator.Snapshot(new MetricsState(), null, null);
            Assert.True(s.Has("profit.streakMonths"));
            Assert.True(s.Has("share.total.avg30"));
            Assert.True(s.Has("oem.signed.total"));
            Assert.False(s.Has("money"));
            Assert.False(s.Has("factory.lines"));
            Assert.False(s.Has("profit.lastMonth"));
            Assert.False(s.Has("money.min"));
            Assert.True(double.IsNaN(s.Get("money")), "an unknown metric reads as NaN, which no comparison satisfies");
        }

        [Test]
        public void EveryMetricOfTheVocabulary_IsComputed_OnceADayAndAMonthAreKnown()
        {
            var state = new MetricsState();
            DaySample d = Day(D(1980, 2, 1), 10, Sales(1), Sales(2));
            MonthSample m = Month(1980, 1, 10, 5, 100, 50);
            MetricsCalculator.ApplyDay(state, d);
            MetricsCalculator.ApplyMonth(state, m);

            MetricsSnapshot s = MetricsCalculator.Snapshot(state, d, m);
            foreach (MetricInfo metric in ContentVocabulary.Metrics)
            {
                Assert.True(s.Has(metric.Id), "the calculator does not compute the metric " + metric.Id);
            }
            Assert.Equal(ContentVocabulary.Metrics.Count, s.KnownCount);
        }

        [Test]
        public void TheSnapshot_RejectsAnIdOutsideTheVocabulary_AndForgetsACleared()
        {
            var s = new MetricsSnapshot();
            Assert.Throws<ArgumentException>(() => s.Set("not.a.metric", 1));
            Assert.Throws<ArgumentException>(() => s.Set(null, 1));
            Assert.False(s.Has("not.a.metric"));
            Assert.False(s.Has(null));

            s.Set("money", 5);
            Assert.True(s.Has("money"));
            double value;
            Assert.True(s.TryGet("money", out value));
            Assert.Equal(5.0, value);
            s.Clear("money");
            Assert.False(s.Has("money"));
            Assert.False(s.TryGet("money", out value));
            s.Clear("not.a.metric"); // no throw
        }

        [Test]
        public void TheCalculator_RefusesNulls()
        {
            Assert.Throws<ArgumentNullException>(() => MetricsCalculator.ApplyDay(null, Day(D(1980, 1, 1))));
            Assert.Throws<ArgumentNullException>(() => MetricsCalculator.ApplyDay(new MetricsState(), null));
            Assert.Throws<ArgumentNullException>(() => MetricsCalculator.ApplyMonth(new MetricsState(), null));
            Assert.Throws<ArgumentNullException>(() => MetricsCalculator.Snapshot(null, null, null));
        }
    }
}
