using System;
using System.Collections.Generic;
using PTMod.Features.ScenariosAchievements.Logic;
using static PTMod.Tests.Features.ScenariosAchievements.RunTestData;

namespace PTMod.Tests.Features.ScenariosAchievements
{
    /// <summary>
    /// TSK-016: the arithmetic of <c>GameMetricsSource.SampleLastFullMonth</c> - the report of the last full month is found by its (year, month),
    /// not by its position, and the twelve last full months are summed (<c>revenue.last12m</c>, <c>revenue.clients.last12m</c>); a month without a
    /// report gives a sample the calculator ignores.
    /// </summary>
    [Covers(typeof(MonthSampler), typeof(IFinanceReports))]
    public class MonthSamplerTests
    {
        private sealed class Reports : IFinanceReports
        {
            public readonly List<double[]> Items = new List<double[]>();   // year, month, revenue, expenses, clients

            public Reports Add(int year, int month, double revenue, double expenses = 0, double clients = 0)
            {
                Items.Add(new[] { year, month, revenue, expenses, clients });
                return this;
            }

            /// <summary>Every month from (year, month) for <paramref name="months"/> months, revenue 100 each.</summary>
            public Reports Run(int year, int month, int months, double revenue = 100, double expenses = 40, double clients = 10)
            {
                for (int i = 0; i < months; i++)
                {
                    Add(year, month, revenue, expenses, clients);
                    month++;
                    if (month > 12)
                    {
                        month = 1;
                        year++;
                    }
                }
                return this;
            }

            public int Count { get { return Items.Count; } }
            public int YearAt(int index) { return (int)Items[index][0]; }
            public int MonthAt(int index) { return (int)Items[index][1]; }
            public double RevenueAt(int index) { return Items[index][2]; }
            public double ExpensesAt(int index) { return Items[index][3]; }
            public double ClientsRevenueAt(int index) { return Items[index][4]; }
        }

        // ---- the month ----

        [Test]
        public void TheLastFullMonth_IsTheMonthBeforeTheOneToday_IsIn()
        {
            int year, month;
            MonthSampler.PreviousMonth(D(1985, 2, 1), out year, out month);
            Assert.Equal(1985, year);
            Assert.Equal(1, month);

            MonthSampler.PreviousMonth(D(1985, 1, 1), out year, out month);
            Assert.Equal(1984, year);
            Assert.Equal(12, month);

            MonthSampler.PreviousMonth(D(1985, 7, 31), out year, out month);
            Assert.Equal(6, month);
        }

        [Test]
        public void TheReport_IsFoundByYearAndMonth_NotByPosition()
        {
            // the month in progress (Feb) is the last one, as the game keeps it
            var reports = new Reports().Add(1985, 1, 500, 200).Add(1985, 2, 7, 7);
            MonthSample sample = MonthSampler.Build(reports, D(1985, 2, 1));
            Assert.True(sample.HasReport);
            Assert.Equal(1985, sample.Year);
            Assert.Equal(1, sample.Month);
            Assert.Equal(500.0, sample.Revenue);
            Assert.Equal(200.0, sample.Expenses);
            Assert.Equal(300.0, sample.Profit);

            // the game's own handler of the month may already have opened the next report, or not yet: the answer is the same
            var late = new Reports().Add(1985, 1, 500, 200);
            Assert.Equal(500.0, MonthSampler.Build(late, D(1985, 2, 1)).Revenue);
            var later = new Reports().Add(1985, 1, 500, 200).Add(1985, 2, 7, 7).Add(1985, 3, 9, 9);
            Assert.Equal(500.0, MonthSampler.Build(later, D(1985, 2, 1)).Revenue, "a newer report is the month in progress, not the one asked for");
        }

        [Test]
        public void AMonthWithoutAReport_GivesNoReport_AndTheCalculatorIgnoresIt()
        {
            var reports = new Reports().Add(1985, 2, 100);
            MonthSample sample = MonthSampler.Build(reports, D(1985, 2, 1));
            Assert.False(sample.HasReport, "the report of January does not exist: a new game starts in January");
            Assert.Equal(1, sample.Month);

            var state = new MetricsState();
            MetricsCalculator.ApplyMonth(state, sample);
            Assert.Equal(0, state.ProfitStreakMonths);
            Assert.Equal(0, state.LastMonthKey);
        }

        [Test]
        public void NoReportsAtAll_OrNoDate_GiveAnEmptySample_NeverAnException()
        {
            Assert.False(MonthSampler.Build(null, D(1985, 2, 1)).HasReport);
            Assert.False(MonthSampler.Build(new Reports(), D(1985, 2, 1)).HasReport);
            Assert.False(MonthSampler.Build(new Reports().Add(1985, 1, 5), default(DateTime)).HasReport);
            Assert.Equal(0.0, MonthSampler.Revenue12m(null, D(1985, 2, 1)));
        }

        // ---- the twelve months ----

        [Test]
        public void TheTwelveMonthSums_CoverTheTwelveLastFullMonths()
        {
            // Jan 1984 .. Dec 1985 (24 months) and the month in progress, Jan 1986
            var reports = new Reports().Run(1984, 1, 24, revenue: 100, clients: 10);
            reports.Add(1986, 1, 999, 0, 999);

            MonthSample sample = MonthSampler.Build(reports, D(1986, 1, 1));
            Assert.True(sample.HasReport);
            Assert.Equal(12, sample.Month);
            Assert.Equal(1200.0, sample.Revenue12m, "Jan..Dec 1985");
            Assert.Equal(120.0, sample.ClientsRevenue12m);
            Assert.Equal(100.0, sample.Revenue);
        }

        [Test]
        public void ARecentCompany_SumsWhatItHas()
        {
            var reports = new Reports().Run(1985, 1, 5, revenue: 50);   // Jan..May, the month in progress is May
            MonthSample sample = MonthSampler.Build(reports, D(1985, 5, 1));
            Assert.Equal(200.0, sample.Revenue12m, "Jan..Apr");
            Assert.Equal(50.0, sample.Revenue);
        }

        [Test]
        public void AMonthThatTheGameKeptTwice_CountsOnce()
        {
            var reports = new Reports().Add(1985, 1, 100).Add(1985, 2, 100).Add(1985, 2, 100).Add(1985, 3, 1);
            Assert.Equal(200.0, MonthSampler.Build(reports, D(1985, 3, 1)).Revenue12m, "Jan and Feb; the second Feb is the same month");
        }

        [Test]
        public void AGapInTheReports_IsNotFilled()
        {
            // March is missing: the sum is of what exists
            var reports = new Reports().Add(1985, 1, 10).Add(1985, 2, 20).Add(1985, 4, 40).Add(1985, 5, 1);
            MonthSample sample = MonthSampler.Build(reports, D(1985, 5, 1));
            Assert.Equal(70.0, sample.Revenue12m);
            Assert.Equal(40.0, sample.Revenue);
        }

        [Test]
        public void ReportsOlderThanTheWindow_AreNotSummed()
        {
            var reports = new Reports().Add(1983, 6, 1000).Run(1984, 7, 13, revenue: 10);   // Jul 1984 .. Jul 1985 (13 months)
            MonthSample sample = MonthSampler.Build(reports, D(1985, 7, 1));                  // last full: Jun 1985; window Jul 1984 .. Jun 1985
            Assert.Equal(120.0, sample.Revenue12m);
        }

        // ---- the calculator takes it ----

        [Test]
        public void TheSample_FeedsTheProfitStreak_AndTheRevenueMetrics()
        {
            var state = new MetricsState();
            var reports = new Reports().Add(1985, 1, 300, 100, 20).Add(1985, 2, 50, 100, 0).Add(1985, 3, 1);
            MonthSample january = MonthSampler.Build(reports, D(1985, 2, 1));
            MetricsCalculator.ApplyMonth(state, january);
            Assert.Equal(1, state.ProfitStreakMonths);

            MonthSample february = MonthSampler.Build(reports, D(1985, 3, 1));
            MetricsCalculator.ApplyMonth(state, february);
            Assert.Equal(0, state.ProfitStreakMonths, "a loss starts the streak over");

            MetricsSnapshot m = MetricsCalculator.Snapshot(state, null, february);
            Assert.Equal(-50.0, m.Get("profit.lastMonth"));
            Assert.Equal(350.0, m.Get("revenue.last12m"));
            Assert.Equal(20.0, m.Get("revenue.clients.last12m"));
        }
    }
}
