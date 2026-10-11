using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using PTMod.Core;
using PTMod.Core.Logic.Segments;
using PTMod.Features.HistoricalEvents;
using PTMod.Tests.Architecture;

namespace PTMod.Tests.Features.HistoricalEvents
{
    /// <summary>
    /// The market shares and the world view (TSK-004; FR-3, NFR-1; design.md, "Формулы и баланс", 1): the window of full months without the current one,
    /// the segments and "all", a zero denominator, a tie, the main segment of a CPU (D15), the cash headroom, and the speed of a rebuild.
    /// </summary>
    [Covers(typeof(MarketShareCache), typeof(SegmentMask), typeof(WorldMath), typeof(IWorldView), typeof(CompanyInfo), typeof(CpuInfo))]
    public class ShareMathTests
    {
        // ---- the window ----

        [Test]
        public void Window_IsTheFullMonthsWithoutTheCurrentOne()
        {
            var world = new FakeWorld();
            CompanyInfo a = world.AddAi(0, "A");
            CompanyInfo b = world.AddAi(1, "B");
            // Count = 5: the elements 0..3 are full months, element 4 is the month that is not over.
            world.AddCpu(a, Segment.HighEnd, new[] { 1, 2, 3, 4, 1000 });
            world.AddCpu(b, Segment.HighEnd, new[] { 10, 20, 30, 40, 1 });
            MarketShareCache cache = world.Shares();

            // M = 1 is the index Count-2 = 3 only; M = 2 is the indices 2 and 3; the current month (index 4) never counts.
            Assert.Equal(4L, cache.Sales(a.Key, SegmentMask.All, 1));
            Assert.Equal(40L, cache.Sales(b.Key, SegmentMask.All, 1));
            Assert.Equal(3L + 4L, cache.Sales(a.Key, SegmentMask.All, 2));
            Assert.Equal(30L + 40L, cache.Sales(b.Key, SegmentMask.All, 2));
            Assert.Equal(1L + 2L + 3L + 4L, cache.Sales(a.Key, SegmentMask.All, 4));
            Assert.Equal(44L, cache.TotalSales(SegmentMask.All, 1));
            Assert.Near(4.0 / 44.0, cache.Share(a.Key, SegmentMask.All, 1), 1e-6);
            Assert.Near(10.0 / 110.0, cache.Share(a.Key, SegmentMask.All, 4), 1e-6, "A sold 10 of the 110 units of the four full months");
        }

        [Test]
        public void Window_WiderThanTheJournalCountsTheMonthsItHas()
        {
            var world = new FakeWorld();
            CompanyInfo a = world.AddAi(0, "A");
            CompanyInfo b = world.AddAi(1, "B");
            world.AddCpu(a, Segment.LowEnd, new[] { 5, 7, 99 });
            world.AddCpu(b, Segment.LowEnd, new[] { 5, 3, 99 });
            MarketShareCache cache = world.Shares(36);

            Assert.Equal(12L, cache.Sales(a.Key, SegmentMask.All, 36));
            Assert.Near(12.0 / 20.0, cache.Share(a.Key, SegmentMask.All, 36), 1e-6);
        }

        [Test]
        public void Window_OfAJournalWithOneElementIsEmpty()
        {
            var world = new FakeWorld();
            CompanyInfo a = world.AddAi(0, "A");
            world.AddCpu(a, Segment.LowEnd, new[] { 50 });
            MarketShareCache cache = world.Shares();

            Assert.Equal(0L, cache.TotalSales(SegmentMask.All, 36), "a lone current month is no full month");
            Assert.Equal(0f, cache.Share(a.Key, SegmentMask.All, 36));
        }

        [Test]
        public void MonthsAbove_TheWidthOfTheCache_AreCutToIt()
        {
            var world = new FakeWorld();
            CompanyInfo a = world.AddAi(0, "A");
            world.AddCpu(a, Segment.MidRange, FakeWorld.Repeat(1, 50));
            var cache = new MarketShareCache();
            cache.Rebuild(world, 12);

            Assert.Equal(12, cache.MaxMonths);
            Assert.Equal(12L, cache.Sales(a.Key, SegmentMask.All, 36), "a question for 36 months gets the 12 the cache keeps");
            Assert.Equal(1L, cache.Sales(a.Key, SegmentMask.All, 0), "less than one month is one month");

            cache.Rebuild(world, 1000);
            Assert.Equal(MarketShareCache.HardMaxMonths, cache.MaxMonths, "the width is held to 120");
        }

        // ---- segments and "all" ----

        [Test]
        public void Segments_CountOnlyTheirOwnJournals()
        {
            var world = new FakeWorld();
            CompanyInfo a = world.AddAi(0, "A");
            CompanyInfo b = world.AddAi(1, "B");
            world.AddSteadyCpu(a, Segment.HighEnd, 30);
            world.AddSteadyCpu(b, Segment.HighEnd, 10);
            world.AddSteadyCpu(a, Segment.LowEnd, 5);
            world.AddSteadyCpu(b, Segment.LowEnd, 45);
            MarketShareCache cache = world.Shares();

            Assert.Near(0.75, cache.Share(a.Key, SegmentMask.HighEnd, 12), 1e-6);
            Assert.Near(0.10, cache.Share(a.Key, SegmentMask.LowEnd, 12), 1e-6);
            Assert.Near(0.0, cache.Share(a.Key, SegmentMask.MidRange, 12), 1e-6, "nobody sold in the mid range");
            Assert.Near(35.0 / 90.0, cache.Share(a.Key, SegmentMask.HighEnd | SegmentMask.LowEnd, 12), 1e-6, "two segments together: 35 of 90 units");
            Assert.Equal(WorldMath.MaskOf(Segment.HighEnd), SegmentMask.HighEnd);
        }

        [Test]
        public void All_CountsSoldPlusSoldByContracts_NotTheFourSegmentJournals()
        {
            var world = new FakeWorld();
            CompanyInfo a = world.AddAi(0, "A");
            CompanyInfo b = world.AddAi(1, "B");
            // A sells 10 a month on the market and 30 under contracts; B sells 20 on the market and nothing under contracts.
            world.AddCpu(a, Segment.MidRange, new[] { 10, 10, 10, 10, 1 }, new[] { 30, 30, 30, 30, 1 });
            world.AddCpu(b, Segment.MidRange, new[] { 20, 20, 20, 20, 1 });
            MarketShareCache cache = world.Shares();

            Assert.Near(40.0 / 60.0, cache.Share(a.Key, SegmentMask.All, 3), 1e-6, "all = (10 + 30) / (10 + 30 + 20)");
            Assert.Near(10.0 / 30.0, cache.Share(a.Key, SegmentMask.MidRange, 3), 1e-6, "the segment journal has the market sales only");
            Assert.Equal(WorldMath.MaskOf(new[] { Segment.HighEnd, Segment.MidRange, Segment.LowEnd, Segment.Industries }), SegmentMask.All,
                "all four segments listed one by one are 'all'");
        }

        [Test]
        public void MaskOf_AnyIsEveryone_AndAnEmptyListToo()
        {
            Assert.Equal(SegmentMask.All, WorldMath.MaskOf(new[] { Segment.Any }));
            Assert.Equal(SegmentMask.All, WorldMath.MaskOf(new Segment[0]));
            Assert.Equal(SegmentMask.All, WorldMath.MaskOf((IEnumerable<Segment>)null));
            Assert.Equal(SegmentMask.All, WorldMath.MaskOf(Segment.Any));
            Assert.Equal(SegmentMask.HighEnd | SegmentMask.Industries, WorldMath.MaskOf(new[] { Segment.HighEnd, Segment.Industries }));
            Assert.Equal(SegmentMask.LowEnd, WorldMath.MaskOf(Segment.LowEnd));
        }

        // ---- zero, unknown, tie ----

        [Test]
        public void ZeroDenominator_GivesZero_NotNaN()
        {
            var world = new FakeWorld();
            CompanyInfo a = world.AddAi(0, "A");
            world.AddCpu(a, Segment.HighEnd, new[] { 0, 0, 0, 0 });
            MarketShareCache cache = world.Shares();

            Assert.Equal(0f, cache.Share(a.Key, SegmentMask.All, 3));
            Assert.Equal(0f, cache.Share(a.Key, SegmentMask.HighEnd, 3));
            Assert.Equal(0, cache.Ranking(SegmentMask.All, 3).Count, "nobody sold, nobody has a place");
        }

        [Test]
        public void UnknownCompany_AnEmptyMask_AndAnUnbuiltCache_GiveZero()
        {
            var world = new FakeWorld();
            CompanyInfo a = world.AddAi(0, "A");
            world.AddSteadyCpu(a, Segment.HighEnd, 10);
            MarketShareCache cache = world.Shares();

            Assert.Equal(0f, cache.Share("ai:77", SegmentMask.All, 12));
            Assert.Equal(0f, cache.Share(null, SegmentMask.All, 12));
            Assert.Equal(0f, cache.Share(a.Key, SegmentMask.None, 12));
            Assert.Equal(0f, new MarketShareCache().Share(a.Key, SegmentMask.All, 12), "never built");
            Assert.False(new MarketShareCache().IsBuilt);
            Assert.Equal(0, new MarketShareCache().Ranking(SegmentMask.All, 12).Count);
        }

        [Test]
        public void ACpuWithoutAnOwner_StaysInTheDenominator()
        {
            var world = new FakeWorld();
            CompanyInfo a = world.AddAi(0, "A");
            world.AddSteadyCpu(a, Segment.HighEnd, 10);
            world.AddSteadyCpu(null, Segment.HighEnd, 30);
            MarketShareCache cache = world.Shares();

            Assert.Near(0.25, cache.Share(a.Key, SegmentMask.All, 12), 1e-6);
        }

        [Test]
        public void Tie_GoesToThePlayer_ThenToTheSmallerUniqueId_AlwaysTheSameWay()
        {
            var world = new FakeWorld();
            CompanyInfo ai5 = world.AddAi(5, "Five");
            CompanyInfo ai2 = world.AddAi(2, "Two");
            CompanyInfo player = world.AddPlayer();
            CompanyInfo ai9 = world.AddAi(9, "Nine");
            world.AddSteadyCpu(ai5, Segment.HighEnd, 10);
            world.AddSteadyCpu(ai9, Segment.HighEnd, 10);
            world.AddSteadyCpu(player, Segment.HighEnd, 10);
            world.AddSteadyCpu(ai2, Segment.HighEnd, 10);
            MarketShareCache cache = world.Shares();

            Assert.Equal(new[] { "player", "ai:2", "ai:5", "ai:9" }, cache.Ranking(SegmentMask.All, 12).ToArray());

            // The order of the list does not matter, nor does a second rebuild.
            world.CompanyList.Reverse();
            cache.Rebuild(world, 36);
            Assert.Equal(new[] { "player", "ai:2", "ai:5", "ai:9" }, cache.Ranking(SegmentMask.All, 12).ToArray());
        }

        [Test]
        public void Ranking_IsBySales_LeavesOutBankruptCompanies_AndKeepsTheirSalesInTheDenominator()
        {
            var world = new FakeWorld();
            CompanyInfo big = world.AddAi(0, "Big", c => c.IsBankrupt = true);
            CompanyInfo mid = world.AddAi(1, "Mid");
            CompanyInfo small = world.AddAi(2, "Small");
            CompanyInfo none = world.AddAi(3, "None");
            world.AddSteadyCpu(big, Segment.HighEnd, 50);
            world.AddSteadyCpu(small, Segment.HighEnd, 10);
            world.AddSteadyCpu(mid, Segment.HighEnd, 40);
            MarketShareCache cache = world.Shares();

            Assert.Equal(new[] { "ai:1", "ai:2" }, cache.Ranking(SegmentMask.All, 12).ToArray());
            Assert.Near(0.4, cache.Share(mid.Key, SegmentMask.All, 12), 1e-6, "the bankrupt company's sales are part of the market");
            Assert.Near(0.5, cache.Share(big.Key, SegmentMask.All, 12), 1e-6, "its share can still be asked for");
            Assert.Equal(0f, cache.Share(none.Key, SegmentMask.All, 12));
        }

        [Test]
        public void ACompanyThatIsNotInTheList_StillCountsInTheDenominator()
        {
            var world = new FakeWorld();
            CompanyInfo a = world.AddAi(0, "A");
            world.AddSteadyCpu(a, Segment.HighEnd, 10);
            world.AddSteadyCpu(new CompanyInfo { Key = "ai:40" }, Segment.HighEnd, 10);
            MarketShareCache cache = world.Shares();

            Assert.Near(0.5, cache.Share(a.Key, SegmentMask.All, 12), 1e-6);
            Assert.Equal(new[] { "ai:0" }, cache.Ranking(SegmentMask.All, 12).ToArray());
        }

        // ---- the rebuild ----

        [Test]
        public void EnsureFresh_RebuildsWhenTheMonthChanges_AndOtherwiseDoesNothing()
        {
            var world = new FakeWorld();
            CompanyInfo a = world.AddAi(0, "A");
            world.AddSteadyCpu(a, Segment.HighEnd, 10);
            var cache = new MarketShareCache();

            cache.EnsureFresh(world, 36);
            Assert.Equal(1, cache.Rebuilds);
            Assert.Equal(world.MonthsSinceStart, cache.BuiltAtMonth);

            cache.EnsureFresh(world, 36);
            Assert.Equal(1, cache.Rebuilds, "the same month: nothing to do");

            world.MonthsSinceStart++;
            cache.EnsureFresh(world, 36);
            Assert.Equal(2, cache.Rebuilds, "a new month");

            cache.EnsureFresh(world, 24);
            Assert.Equal(3, cache.Rebuilds, "another width");

            cache.Clear();
            Assert.False(cache.IsBuilt);
            Assert.Equal(-1, cache.BuiltAtMonth);
        }

        [Test]
        public void Rebuild_ReadsTheJournalsAgain()
        {
            var world = new FakeWorld();
            CompanyInfo a = world.AddAi(0, "A");
            CompanyInfo b = world.AddAi(1, "B");
            CpuInfo cpuA = world.AddSteadyCpu(a, Segment.HighEnd, 10);
            world.AddSteadyCpu(b, Segment.HighEnd, 10);
            MarketShareCache cache = world.Shares();
            Assert.Near(0.5, cache.Share(a.Key, SegmentMask.All, 12), 1e-6);

            cpuA.Sold = cpuA.SoldByHighEnd = FakeWorld.Repeat(30, 41);
            Assert.Near(0.5, cache.Share(a.Key, SegmentMask.All, 12), 1e-6, "until the next rebuild the old numbers stand");
            cache.Rebuild(world, 36);
            Assert.Near(0.75, cache.Share(a.Key, SegmentMask.All, 12), 1e-6);
        }

        [Test]
        public void Rebuild_ToleratesNullJournalsNullCpusAndNullCompanies()
        {
            var world = new FakeWorld();
            CompanyInfo a = world.AddAi(0, "A");
            world.CompanyList.Add(null);
            world.CpuList.Add(null);
            world.CpuList.Add(new CpuInfo { SaveId = 99, CompanyKey = a.Key });
            world.AddSteadyCpu(a, Segment.HighEnd, 10);
            MarketShareCache cache = world.Shares();

            Assert.Near(1.0, cache.Share(a.Key, SegmentMask.All, 12), 1e-6);
            Assert.Throws<ArgumentNullException>(() => new MarketShareCache().Rebuild(null, 36));
        }

        [Test]
        public void Rebuild_OfFourHundredCpusOverThirtySixMonths_TakesUnderTwoMilliseconds()
        {
            var world = new FakeWorld();
            var companies = new List<CompanyInfo> { world.AddPlayer() };
            for (int i = 0; i < 12; i++) companies.Add(world.AddAi(i, "AI " + i));
            var random = new Random(7);
            for (int i = 0; i < 400; i++)
            {
                var monthly = new int[37];
                for (int m = 0; m < monthly.Length; m++) monthly[m] = random.Next(0, 5000);
                world.AddCpu(companies[i % companies.Count], (Segment)(i % 4), monthly, FakeWorld.Repeat(random.Next(0, 100), 37));
            }

            var cache = new MarketShareCache();
            cache.Rebuild(world, 36);   // the first run pays for the compiler
            cache.Rebuild(world, 36);

            double best = double.MaxValue;
            for (int run = 0; run < 9; run++)
            {
                Stopwatch watch = Stopwatch.StartNew();
                cache.Rebuild(world, 36);
                watch.Stop();
                best = Math.Min(best, watch.Elapsed.TotalMilliseconds);
            }

            Assert.True(best <= 2.0, "a rebuild of 400 CPUs x 36 months took " + best.ToString("0.00") + " ms at best; the budget is 2 ms");
            Assert.True(cache.Share("player", SegmentMask.All, 36) > 0f, "and it counted something");
        }

        // ---- the main segment of a CPU (D15, AC-3.4) ----

        [Test]
        public void PrimarySegment_OfAnAiCpu_IsItsTargetMarket()
        {
            for (int target = 0; target < 4; target++)
            {
                Segment segment = WorldMath.PrimarySegment(false, target, Segment.LowEnd, () => Segment.Industries);
                Assert.Equal((Segment)target, segment, "target market " + target + ": the sales and the price do not matter for an AI CPU");
            }
        }

        [Test]
        public void PrimarySegment_OfAnAiCpu_FallsBackToThePriceWhenTheTargetIsOutsideTheSegments()
        {
            Assert.Equal(Segment.LowEnd, WorldMath.PrimarySegment(false, -1, Segment.Any, () => Segment.LowEnd));
            Assert.Equal(Segment.MidRange, WorldMath.PrimarySegment(false, 7, Segment.Any, () => Segment.MidRange));
            Assert.Equal(Segment.Any, WorldMath.PrimarySegment(false, -1, Segment.Any, null), "no price rule: it cannot be said");
        }

        [Test]
        public void PrimarySegment_OfAPlayerCpu_IsWhereItSoldMostInTheLastFullMonth()
        {
            // The sales of the last full month go through the rule of the core (ModSegments.FromSales is SegmentRule.FromSales).
            Segment bySales = SegmentRule.FromSales(10, 80, 30, 0);
            Assert.Equal(Segment.MidRange, WorldMath.PrimarySegment(true, 0, bySales, () => Segment.HighEnd), "where it sold most, not the strategy and not the price");
            Segment tie = SegmentRule.FromSales(40, 40, 40, 40);
            Assert.Equal(Segment.HighEnd, WorldMath.PrimarySegment(true, 3, tie, () => Segment.LowEnd), "a tie goes to the smaller index, like the core's rule");
        }

        [Test]
        public void PrimarySegment_OfAPlayerCpuWithoutSales_IsTheSegmentOfItsPrice_AC_3_4()
        {
            // Budgets of the market: mid 400, low 150, industries 60, band 1.25. A price of 700 is above 400 x 1.25 = 500: high end.
            Func<Segment> byPrice = () => SegmentRule.ByPrice(700f, 400f, 150f, 60f, 1.25f);
            Segment noSales = SegmentRule.FromSales(0, 0, 0, 0);
            Assert.Equal(Segment.Any, noSales, "nothing sold in the last full month");
            Assert.Equal(Segment.HighEnd, WorldMath.PrimarySegment(true, 2, noSales, byPrice), "the AI strategy of a player's CPU is not read");
            Assert.Equal(Segment.LowEnd, WorldMath.PrimarySegment(true, 0, noSales, () => SegmentRule.ByPrice(100f, 400f, 150f, 60f, 1.25f)));
        }

        // ---- the small rules of the world ----

        [Test]
        public void CashHeadroom_IsMoneyPlusTheDebtLimitTheGameApplies()
        {
            Assert.Near(110, WorldMath.CashHeadroom(10f, -100f));
            Assert.Near(-40, WorldMath.CashHeadroom(-140f, -100f), 1e-6, "below the threshold already: negative");
            Assert.Near(10, WorldMath.CashHeadroom(10f, float.NaN), 1e-6, "a threshold that is not a number counts as 0");
            Assert.Near(0, WorldMath.CashHeadroom(float.PositiveInfinity, -100f), 1e-6);

            var world = new FakeWorld();
            CompanyInfo a = world.AddAi(0, "A");
            world.Money[a.Key] = 5_000_000f;
            world.Threshold[a.Key] = -20_000_000f;
            Assert.Near(25_000_000.0, world.CashHeadroom(a.Key), 1.0);
            Assert.Equal(0f, world.CashHeadroom("ai:5"), "an unknown company has nothing");
        }

        [Test]
        public void IsRecent_CountsTheCalendarYearOfTheCpu()
        {
            var cpu = new CpuInfo { Released = true, Year = 2018 };
            Assert.True(WorldMath.IsRecent(cpu, new SimpleDate(2021, 3, 1), 36), "2021-03 minus 36 months is 2018-03: the year 2018 counts");
            Assert.False(WorldMath.IsRecent(cpu, new SimpleDate(2021, 3, 1), 24), "2019-03: the year 2018 is too old");
            Assert.True(WorldMath.IsRecent(new CpuInfo { Released = true, Year = 2021 }, new SimpleDate(2021, 3, 1), 1));
            Assert.False(WorldMath.IsRecent(new CpuInfo { Released = false, Year = 2021 }, new SimpleDate(2021, 3, 1), 36), "not released");
            Assert.False(WorldMath.IsRecent(cpu, default(SimpleDate), 36), "no date, no answer");
            Assert.False(WorldMath.IsRecent(null, new SimpleDate(2021, 3, 1), 36));
        }

        [Test]
        public void IsOnMarket_NeedsReleasedNotRetiredAndSoldOnTheMarket()
        {
            Assert.True(WorldMath.IsOnMarket(new CpuInfo { Released = true, OnMarket = true }));
            Assert.False(WorldMath.IsOnMarket(new CpuInfo { Released = true, OnMarket = true, Retired = true }));
            Assert.False(WorldMath.IsOnMarket(new CpuInfo { Released = false, OnMarket = true }));
            Assert.False(WorldMath.IsOnMarket(new CpuInfo { Released = true, OnMarket = false }));
            Assert.False(WorldMath.IsOnMarket(null));
        }

        [Test]
        public void FoundedIndex_OrdersByYearAndMonth()
        {
            var early = new CompanyInfo { FoundedYear = 1988, FoundedMonth = 12 };
            var late = new CompanyInfo { FoundedYear = 1989, FoundedMonth = 1 };
            Assert.True(WorldMath.FoundedIndex(late) > WorldMath.FoundedIndex(early));
            Assert.True(WorldMath.FoundedIndex(null) < WorldMath.FoundedIndex(early));
            Assert.Equal("ai:3", new CompanyInfo { Key = "ai:3" }.ToString());
            Assert.Equal("(no key)", new CompanyInfo().ToString());
            Assert.Equal("cpu 4 of nobody", new CpuInfo { SaveId = 4 }.ToString());
        }

        // ---- the game side reads what the task says it reads ----

        [Test]
        public void GameWorldView_ReadsTheSourcesOfTheTask_AndNeverTheBankDebtLimit()
        {
            string code = SourceScan.Strip(SourceScan.ReadText(Path.Combine(FeatureDir(), "Game", "GameWorldView.cs")), true);

            foreach (string needed in new[]
            {
                "ModCompanies.All", "GetAllCpus", "Reports", "AverageIps", "ModCredit.ThresholdOf", "ModFeatures.IsActive", "MonthsSinceStart",
                "ModSegments.Classify", "ModSegments.FromSales", "WorldMath.PrimarySegment", "WorldMath.CashHeadroom", "TargetMarket",
            })
            {
                Assert.True(code.Contains(needed), "GameWorldView.cs does not use " + needed);
            }

            Assert.False(Regex.IsMatch(code, @"\bCentralBank\b"), "the credit goes through ModCredit, not through the bank");
            Assert.False(Regex.IsMatch(code, @"\bDebtLimit\b"), "the debt limit goes through ModCredit.ThresholdOf");
            Assert.True(code.Contains("sealed class GameWorldView : IWorldView"), "GameWorldView is the IWorldView of the game");
        }

        [Test]
        public void TheLogicOfTheWorld_HasNoClassifierOfItsOwn()
        {
            // D15 / FR-3 item 7: the segment of a CPU comes from the core (SegmentRule / ModSegments); no price budgets or bands live in the feature.
            foreach (string file in new[] { "IWorldView.cs", "MarketShareCache.cs", "RoleResolver.cs" })
            {
                string code = SourceScan.Strip(SourceScan.ReadText(Path.Combine(FeatureDir(), "Logic", file)), true);
                Assert.False(Regex.IsMatch(code, @"MidRangeBudget|LowEndBudget|IndustriesBudget|priceBand", RegexOptions.IgnoreCase), file + " must not classify by price");
            }
        }

        private static string FeatureDir()
        {
            return Path.Combine(SourceScan.SrcDirOrFail(), "Features", "HistoricalEvents");
        }
    }
}
