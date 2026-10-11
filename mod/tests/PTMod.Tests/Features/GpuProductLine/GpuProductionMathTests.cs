using System;
using System.Linq;
using PTMod.Features.GpuProductLine;

namespace PTMod.Tests.Features.GpuProductLine
{
    /// <summary>
    /// The production of graphics cards (TSK-010, FR-5, FR-7; F17, F18, F28, F28', F29, F31, F32): a day on the lines of the own factory against a hand calculation of
    /// <c>Factory.ProduceWafers</c>, a fabless day, the lines the cards may take (default and "CPU first": AC-5.2, AC-5.3, AC-5.5), the proportional cut by the largest
    /// remainders, the automatic lines (never an endless loop), the cost of storage (AC-7.2) and the liquidation (AC-7.1).
    /// </summary>
    [Covers(typeof(GpuProductionMath))]
    public class GpuProductionMathTests
    {
        private const int Wafers = 50;
        private const float WaferMaterial = 2000f;

        // ---- F17: a day on the own factory ----

        [Test]
        public void Produce_MatchesProduceWafers_OnTheHandExample()
        {
            // 5 lines, 50 wafers a line a month, a 300 mm2 die on a 300 mm wafer: 197 dies at yield 1 and 177 at the node's yield.
            // gross  = floor(5 x 50 x 197 / 30) = floor(1641.67) = 1641
            // passed = floor(5 x 50 x 177 / 30) = floor(1475.0)  = 1475
            // failed = round(1475 x 0.12) = 177, good = 1475 - 177 = 1298
            // cost   = 15 x 1641 + 120 x 1298 + 430000 x 5 / 30 + 2000 x 50 x 5 / 30
            GpuProductionMath.DailyOutput o = GpuProductionMath.Produce(5, Wafers, 197, 177, 0.12f, 15f, 120f, 430000f, WaferMaterial, 1f);
            Assert.Equal(1641, o.Gross);
            Assert.Equal(1475, o.Passed);
            Assert.Equal(177, o.Failed);
            Assert.Equal(1298, o.Good);
            double expected = 15.0 * 1641 + 120.0 * 1298 + 430000.0 * 5 / 30 + 2000.0 * 50 * 5 / 30;
            Assert.Near(268708.33, expected, 0.01);
            Assert.Near(expected, o.Cost, 1.0);
        }

        [Test]
        public void Produce_Ac51_GoodCardsAreTheFloorMinusTheFailures()
        {
            // AC-5.1: floor(5 x 50 x DPW x yield / 30) good dies minus the failures
            GpuProductionMath.DailyOutput o = GpuProductionMath.Produce(5, Wafers, 197, 177, 0.0f, 10f, 100f, 400000f, WaferMaterial, 1f);
            Assert.Equal((5 * 50 * 177) / 30, o.Passed);
            Assert.Equal(o.Passed, o.Good, "no failures");
            Assert.Equal(0, o.Failed);
        }

        [Test]
        public void Produce_FailuresRoundHalvesToEven_LikeTheGame()
        {
            // passed = floor(1 x 50 x 30 / 30) = 50; 50 x 0.25 = 12.5 -> 12 (even), not 13
            GpuProductionMath.DailyOutput o = GpuProductionMath.Produce(1, Wafers, 30, 30, 0.25f, 0f, 0f, 0f, 0f, 1f);
            Assert.Equal(50, o.Passed);
            Assert.Equal(12, o.Failed);
            Assert.Equal(38, o.Good);
            // 50 x 0.75 = 37.5 -> 38 (even)
            o = GpuProductionMath.Produce(1, Wafers, 30, 30, 0.75f, 0f, 0f, 0f, 0f, 1f);
            Assert.Equal(38, o.Failed);
        }

        [Test]
        public void Produce_VideoMemoryBoardAndCooler_AreChargedOnlyOnWorkingCards()
        {
            GpuProductionMath.DailyOutput cheapExtras = GpuProductionMath.Produce(5, Wafers, 197, 177, 0.12f, 15f, 0f, 0f, 0f, 1f);
            GpuProductionMath.DailyOutput dearExtras = GpuProductionMath.Produce(5, Wafers, 197, 177, 0.12f, 15f, 100f, 0f, 0f, 1f);
            Assert.Near(100.0 * 1298, dearExtras.Cost - cheapExtras.Cost, 1.0, "100 more on each of the 1298 good cards, none on the failed ones");
            Assert.Near(15.0 * 1641, cheapExtras.Cost, 1.0, "the die material is charged on every die started (gross)");
        }

        [Test]
        public void Produce_WaferMaterial_ComesAsANumber_SoAShockReachesTheCost()
        {
            // AC-5.6: the wafer material cost x1.5 raises the material part x1.5 and leaves the video memory part alone
            GpuProductionMath.DailyOutput normal = GpuProductionMath.Produce(5, Wafers, 197, 177, 0.12f, 15f, 120f, 430000f, 2000f, 1f);
            GpuProductionMath.DailyOutput shock = GpuProductionMath.Produce(5, Wafers, 197, 177, 0.12f, 15f, 120f, 430000f, 3000f, 1f);
            double materialNormal = 2000.0 * 50 * 5 / 30;
            Assert.Near(materialNormal * 0.5, shock.Cost - normal.Cost, 1.0, "+50 % of the material of 250 wafers / 30");
            Assert.Near(277041.67, shock.Cost, 1.0);
            Assert.Equal(normal.Good, shock.Good);
        }

        [Test]
        public void Produce_TheOutputChannel_ScalesTheCardsAddedNotTheCost()
        {
            GpuProductionMath.DailyOutput one = GpuProductionMath.Produce(5, Wafers, 197, 177, 0.12f, 15f, 120f, 430000f, WaferMaterial, 1f);
            GpuProductionMath.DailyOutput more = GpuProductionMath.Produce(5, Wafers, 197, 177, 0.12f, 15f, 120f, 430000f, WaferMaterial, 1.1f);
            Assert.Equal(1428, more.Good, "round(1298 x 1.1)");
            Assert.Equal(one.Failed, more.Failed);
            Assert.Near(one.Cost, more.Cost, 0.01, "the extras are charged for the cards built, not for the bonus");
            GpuProductionMath.DailyOutput none = GpuProductionMath.Produce(5, Wafers, 197, 177, 0.12f, 15f, 120f, 430000f, WaferMaterial, 0f);
            Assert.Equal(0, none.Good, "a channel at 0 stops the output");
            GpuProductionMath.DailyOutput broken = GpuProductionMath.Produce(5, Wafers, 197, 177, 0.12f, 15f, 120f, 430000f, WaferMaterial, float.NaN);
            Assert.Equal(1298, broken.Good, "a broken channel is 1");
            GpuProductionMath.DailyOutput negative = GpuProductionMath.Produce(5, Wafers, 197, 177, 0.12f, 15f, 120f, 430000f, WaferMaterial, -3f);
            Assert.Equal(0, negative.Good);
        }

        [Test]
        public void Produce_EdgeCases_NeverNegativeNeverNaN()
        {
            Assert.Equal(0, GpuProductionMath.Produce(0, Wafers, 197, 177, 0.1f, 15f, 120f, 430000f, WaferMaterial, 1f).Gross);
            Assert.Equal(0f, GpuProductionMath.Produce(0, Wafers, 197, 177, 0.1f, 15f, 120f, 430000f, WaferMaterial, 1f).Cost, "no lines, no cost");
            Assert.Equal(0f, GpuProductionMath.Produce(-3, Wafers, 197, 177, 0.1f, 15f, 120f, 430000f, WaferMaterial, 1f).Cost);
            Assert.Equal(0, GpuProductionMath.Produce(5, 0, 197, 177, 0.1f, 15f, 120f, 430000f, WaferMaterial, 1f).Gross);

            // a die that does not fit: no dies, but the lines still cost their upkeep and material
            GpuProductionMath.DailyOutput none = GpuProductionMath.Produce(5, Wafers, 0, 0, 0.1f, 15f, 120f, 430000f, WaferMaterial, 1f);
            Assert.Equal(0, none.Gross);
            Assert.Equal(0, none.Good);
            Assert.Near(430000.0 * 5 / 30 + 2000.0 * 50 * 5 / 30, none.Cost, 1.0);
            GpuProductionMath.DailyOutput negativeDpw = GpuProductionMath.Produce(5, Wafers, -4, -2, 0.1f, 15f, 120f, 430000f, WaferMaterial, 1f);
            Assert.Equal(0, negativeDpw.Gross, "a negative die count is read as 0");

            // every die fails
            GpuProductionMath.DailyOutput all = GpuProductionMath.Produce(5, Wafers, 197, 177, 1.0f, 15f, 120f, 430000f, WaferMaterial, 1f);
            Assert.Equal(all.Passed, all.Failed);
            Assert.Equal(0, all.Good);
            GpuProductionMath.DailyOutput over = GpuProductionMath.Produce(5, Wafers, 197, 177, 3.5f, 15f, 120f, 430000f, WaferMaterial, 1f);
            Assert.Equal(over.Passed, over.Failed, "a failure rate above 1 is capped");
            GpuProductionMath.DailyOutput nan = GpuProductionMath.Produce(5, Wafers, 197, 177, float.NaN, 15f, 120f, 430000f, WaferMaterial, 1f);
            Assert.Equal(0, nan.Failed, "a failure rate that is not a number is 0");
            GpuProductionMath.DailyOutput negative = GpuProductionMath.Produce(5, Wafers, 197, 177, -0.5f, 15f, 120f, 430000f, WaferMaterial, 1f);
            Assert.Equal(0, negative.Failed);
        }

        [Test]
        public void Produce_ManyLines_DoNotOverflow()
        {
            GpuProductionMath.DailyOutput o = GpuProductionMath.Produce(1000000, 1000000, 100000, 90000, 0.1f, 1f, 1f, 1f, 1f, 1f);
            Assert.True(o.Gross > 0 && o.Gross <= int.MaxValue);
            Assert.True(o.Good >= 0);
            Assert.False(float.IsInfinity(o.Cost) || float.IsNaN(o.Cost));
        }

        // ---- F18: fabless ----

        [Test]
        public void ProduceOutsourced_Ac54_ThousandAUnitDayGivesOneMinusFailureRate()
        {
            // AC-5.4: stock +1000 x (1 - FR); the cost is the unit cost (already with the foundry's markup) x units
            GpuProductionMath.DailyOutput o = GpuProductionMath.ProduceOutsourced(1000, 0.0657f, 170.18f, 1f);
            Assert.Equal(66, o.Failed, "round(65.7)");
            Assert.Equal(934, o.Good, "round(934.3)");
            Assert.Equal(1000, o.Gross);
            Assert.Near(170180, o.Cost, 0.5);

            Assert.Equal(1028, GpuProductionMath.ProduceOutsourced(1000, 0.0657f, 170.18f, 1.1f).Good, "round(934.3 x 1.1) = round(1027.7): the output channel applies");
        }

        [Test]
        public void ProduceOutsourced_EdgeCases()
        {
            Assert.Equal(0, GpuProductionMath.ProduceOutsourced(0, 0.1f, 100f, 1f).Good);
            Assert.Equal(0f, GpuProductionMath.ProduceOutsourced(-5, 0.1f, 100f, 1f).Cost);
            GpuProductionMath.DailyOutput allFail = GpuProductionMath.ProduceOutsourced(1000, 1f, 100f, 1f);
            Assert.Equal(0, allFail.Good);
            Assert.Equal(1000, allFail.Failed);
            Assert.Near(100000, allFail.Cost, 0.5, "you still pay for the dies that failed");
            GpuProductionMath.DailyOutput capped = GpuProductionMath.ProduceOutsourced(1000, 2.5f, 100f, 1f);
            Assert.Equal(0, capped.Good);
            Assert.Equal(1000, capped.Failed);
            Assert.Equal(1000, GpuProductionMath.ProduceOutsourced(1000, float.NaN, 100f, 1f).Good);
            Assert.Equal(0f, GpuProductionMath.ProduceOutsourced(1000, 0.1f, float.PositiveInfinity, 1f).Cost, "no infinite money");
        }

        [Test]
        public void Royalty_IsARateOfTheRevenue()
        {
            Assert.Near(5000, GpuProductionMath.Royalty(100000f, 0.05f), 0.01);
            Assert.Equal(0f, GpuProductionMath.Royalty(0f, 0.05f));
            Assert.Equal(0f, GpuProductionMath.Royalty(-5f, 0.05f));
            Assert.Equal(0f, GpuProductionMath.Royalty(100000f, -0.05f));
        }

        // ---- F28, F28': the lines of the cards ----

        [Test]
        public void AvailableLines_Default_CapacityMinusClientsMinusWhatIsAlreadyReserved()
        {
            Assert.Equal(20, GpuProductionMath.AvailableLines(20, 0, 0, 18, false), "AC-5.2: the cards are served before the CPUs, so the CPUs' 18 lines do not reduce them");
            Assert.Equal(8, GpuProductionMath.AvailableLines(20, 12, 0, 0, false), "AC-5.3: the foundry clients hold 12 of 20");
            Assert.Equal(16, GpuProductionMath.AvailableLines(20, 0, 4, 0, false), "four lines are already reserved");
            Assert.Equal(4, GpuProductionMath.AvailableLines(20, 12, 4, 0, false));
            Assert.Equal(0, GpuProductionMath.AvailableLines(20, 20, 0, 0, false));
            Assert.Equal(0, GpuProductionMath.AvailableLines(20, 15, 10, 0, false), "more than the factory has: nothing, never negative");
            Assert.Equal(0, GpuProductionMath.AvailableLines(0, 0, 0, 0, false), "no factory");
            Assert.Equal(0, GpuProductionMath.AvailableLines(-5, 0, 0, 0, false));
            Assert.Equal(20, GpuProductionMath.AvailableLines(20, -3, -2, 0, false), "negative sums are read as 0");
        }

        [Test]
        public void AvailableLines_CpuFirst_IsWhatTheCpusLeave()
        {
            Assert.Equal(2, GpuProductionMath.AvailableLines(20, 0, 0, 18, true), "AC-5.5: 20 lines, the CPUs hold 18");
            Assert.Equal(0, GpuProductionMath.AvailableLines(20, 0, 0, 20, true), "zero remainder");
            Assert.Equal(0, GpuProductionMath.AvailableLines(20, 5, 0, 18, true), "a negative remainder is 0");
            Assert.Equal(0, GpuProductionMath.AvailableLines(20, 0, 0, 25, true));
            Assert.Equal(20, GpuProductionMath.AvailableLines(20, 0, 0, 0, true), "no CPU lines: all of them");
            Assert.Equal(7, GpuProductionMath.AvailableLines(20, 5, 0, 8, true), "clients 5 and CPUs 8 hold 13");
        }

        [Test]
        public void AvailableLines_CpuFirst_NeverExceedsTheDefault()
        {
            Assert.Equal(10, GpuProductionMath.AvailableLines(20, 0, 4, 10, true), "the reserved lines count too: min(16, 10)");
            Assert.Equal(16, GpuProductionMath.AvailableLines(20, 0, 4, 0, true), "min(16, 20)");
            Assert.Equal(0, GpuProductionMath.AvailableLines(0, 0, 0, 5, true));
            for (int capacity = 0; capacity <= 25; capacity += 5)
            {
                for (int clients = 0; clients <= capacity; clients += 5)
                {
                    for (int allocated = 0; allocated <= 8; allocated += 4)
                    {
                        for (int cpu = 0; cpu <= 30; cpu += 6)
                        {
                            int normal = GpuProductionMath.AvailableLines(capacity, clients, allocated, cpu, false);
                            int first = GpuProductionMath.AvailableLines(capacity, clients, allocated, cpu, true);
                            Assert.True(first <= normal && first >= 0 && normal >= 0, "capacity " + capacity + ", clients " + clients + ", allocated " + allocated + ", cpu " + cpu);
                            Assert.True(first + cpu + clients <= Math.Max(capacity, cpu + clients), "the CPUs keep their lines");
                        }
                    }
                }
            }
        }

        [Test]
        public void AvailableLines_HugeNumbers_DoNotOverflow()
        {
            Assert.Equal(0, GpuProductionMath.AvailableLines(int.MinValue, int.MaxValue, int.MaxValue, int.MaxValue, true));
            Assert.Equal(int.MaxValue, GpuProductionMath.AvailableLines(int.MaxValue, 0, 0, 0, false));
            Assert.Equal(0, GpuProductionMath.AvailableLines(int.MaxValue, int.MaxValue, 0, 0, false));
        }

        // ---- F28: the cut by the largest remainders ----

        [Test]
        public void ScaleLines_CutsInProportion_ByTheLargestRemainders()
        {
            // 22 requested, 11 available: exact 2.5, 5.0, 3.5; floors 2, 5, 3 = 10; one line left; the equal remainders (0.5) go to the larger request
            Assert.Equal(new[] { 2, 5, 4 }, GpuProductionMath.ScaleLines(new[] { 5, 10, 7 }, 11));
            // 3 x 1 requested, 2 available: equal remainders, equal requests - the earlier ones first
            Assert.Equal(new[] { 1, 1, 0 }, GpuProductionMath.ScaleLines(new[] { 1, 1, 1 }, 2));
            // the exact fit
            Assert.Equal(new[] { 5, 10, 5 }, GpuProductionMath.ScaleLines(new[] { 10, 20, 10 }, 20));
            // 10 + 6 + 1 requested = 17, 9 available: 5.29, 3.18, 0.53 -> 5, 3, 0; one line left, the largest remainder (0.53) is the third
            Assert.Equal(new[] { 5, 3, 1 }, GpuProductionMath.ScaleLines(new[] { 10, 6, 1 }, 9));
            // the same with the remainders the other way round: 9 x 5 / 17 = 2.65, 9 x 6 / 17 = 3.18, 9 x 6 / 17 = 3.18 -> 2, 3, 3 and one line goes to the largest remainder (0.65)
            Assert.Equal(new[] { 3, 3, 3 }, GpuProductionMath.ScaleLines(new[] { 5, 6, 6 }, 9));
        }

        [Test]
        public void ScaleLines_TheSumIsTheAvailable_OrTheRequestsThemselves()
        {
            int[] requested = { 5, 10, 7 };
            int[] same = GpuProductionMath.ScaleLines(requested, 22);
            Assert.Equal(requested, same, "they fit: nothing is cut");
            Assert.False(ReferenceEquals(requested, same), "a copy");
            Assert.Equal(new[] { 5, 10, 7 }, GpuProductionMath.ScaleLines(requested, 100));
            Assert.Equal(new[] { 0, 0, 0 }, GpuProductionMath.ScaleLines(requested, 0));
            Assert.Equal(new[] { 0, 0, 0 }, GpuProductionMath.ScaleLines(requested, -4));
            Assert.Equal(new[] { 2, 0, 0 }, GpuProductionMath.ScaleLines(new[] { 3, 0, 0 }, 2), "a single request is cut to the available");
            Assert.Equal(new int[0], GpuProductionMath.ScaleLines(new int[0], 5));
            Assert.Equal(new int[0], GpuProductionMath.ScaleLines(null, 5));
            Assert.Equal(new[] { 0, 3, 0 }, GpuProductionMath.ScaleLines(new[] { -4, 3, 0 }, 7), "a negative request is 0");
            Assert.Equal(new[] { 0, 2 }, GpuProductionMath.ScaleLines(new[] { -4, 3 }, 2));
        }

        [Test]
        public void ScaleLines_NeverGivesMoreThanRequested_AndIsDeterministic_OnManyCases()
        {
            var random = new Random(77);
            for (int round = 0; round < 2000; round++)
            {
                int count = 1 + random.Next(7);
                var requested = new int[count];
                for (int i = 0; i < count; i++) requested[i] = random.Next(5) == 0 ? 0 : random.Next(60);
                int available = random.Next(150);
                int total = requested.Sum();

                int[] a = GpuProductionMath.ScaleLines(requested, available);
                int[] b = GpuProductionMath.ScaleLines((int[])requested.Clone(), available);
                Assert.Equal(a, b, "the same input, the same answer");
                Assert.Equal(Math.Min(total, available), a.Sum(), "round " + round + ": the sum is the available (or all requests)");
                for (int i = 0; i < count; i++)
                {
                    Assert.True(a[i] >= 0 && a[i] <= requested[i], "round " + round + ": line " + i + " got " + a[i] + " of " + requested[i]);
                    if (total > available)
                    {
                        double exact = requested[i] * (double)available / total;
                        Assert.True(a[i] == (int)Math.Floor(exact) || a[i] == (int)Math.Ceiling(exact), "round " + round + ": within one line of the exact share");
                    }
                }
            }
        }

        [Test]
        public void ScaleLines_BigNumbers_DoNotOverflow()
        {
            int[] big = GpuProductionMath.ScaleLines(new[] { int.MaxValue, int.MaxValue }, 1000);
            Assert.Equal(1000, big.Sum());
            Assert.Equal(500, big[0]);
        }

        // ---- F29: automatic lines ----

        [Test]
        public void AutoLines_AreTheDemandOverWhatALineMakes_CappedByTheShareOfTheFactory()
        {
            // 2000 a day: ceil(1.1 x 2000 x 30 / (50 x 100 x 0.9)) = ceil(14.67) = 15
            Assert.Equal(15, GpuProductionMath.AutoLines(2000f, 50, 100, 0.1f, 40, 1.1f, 0.5f));
            // the cap: floor(0.5 x 20) = 10 lines
            Assert.Equal(10, GpuProductionMath.AutoLines(2000f, 50, 100, 0.1f, 20, 1.1f, 0.5f));
            Assert.Equal(15, GpuProductionMath.AutoLines(2000f, 50, 100, 0.1f, 30, 1.1f, 0.5f), "floor(0.5 x 30) = 15");
            Assert.Equal(14, GpuProductionMath.AutoLines(2000f, 50, 100, 0.1f, 29, 1.1f, 0.5f), "floor(0.5 x 29) = 14");
            Assert.Equal(8, GpuProductionMath.AutoLines(10f, 50, 0, 0.1f, 40, 1.1f, 0.5f) , "a die that does not fit counts as one die per wafer: ceil(1.1 x 10 x 30 / (50 x 1 x 0.9)) = 8");
        }

        [Test]
        public void AutoLines_DoNotLoopOrExplode_OnDegenerateInput()
        {
            Assert.True(GpuProductionMath.AutoLines(500f, 50, 0, 0.1f, 40, 1.1f, 1.0f) >= 0, "dpw 0");
            Assert.True(GpuProductionMath.AutoLines(500f, 50, -7, 0.1f, 40, 1.1f, 1.0f) >= 0, "dpw below 0");
            Assert.Equal(40, GpuProductionMath.AutoLines(1e12f, 50, 100, 0.1f, 40, 1.1f, 1.0f), "capped, however big the demand");
            Assert.Equal(40, GpuProductionMath.AutoLines(500f, 50, 100, 1.0f, 40, 1.1f, 1.0f), "every die fails: 0.01 is the floor of the good share");
            Assert.Equal(40, GpuProductionMath.AutoLines(500f, 50, 100, 7f, 40, 1.1f, 1.0f), "a failure rate above 1 is capped at 1");
            Assert.Equal(0, GpuProductionMath.AutoLines(0f, 50, 100, 0.1f, 40, 1.1f, 0.5f), "no demand");
            Assert.Equal(0, GpuProductionMath.AutoLines(-4f, 50, 100, 0.1f, 40, 1.1f, 0.5f));
            Assert.Equal(0, GpuProductionMath.AutoLines(float.NaN, 50, 100, 0.1f, 40, 1.1f, 0.5f));
            Assert.Equal(0, GpuProductionMath.AutoLines(2000f, 50, 100, 0.1f, 0, 1.1f, 0.5f), "no factory");
            Assert.Equal(0, GpuProductionMath.AutoLines(2000f, 50, 100, 0.1f, 40, 1.1f, 0f), "a share of 0");
            Assert.Equal(0, GpuProductionMath.AutoLines(2000f, 0, 100, 0.1f, 40, 1.1f, 0.5f), "no wafers per line");
            Assert.Equal(0, GpuProductionMath.AutoLines(2000f, 50, 100, 0.1f, 40, 0f, 0.5f), "no factor");
            Assert.Equal(1, GpuProductionMath.AutoLines(1f, 50, 100000, 0f, 40, 1f, 1f), "a trickle of demand still wants one line");
        }

        [Test]
        public void AutoLines_ExactDivisions_DoNotAddALineFromSinglePrecisionNoise()
        {
            // 300 a day x 30 / (50 x 2) = 90 exactly
            Assert.Equal(90, GpuProductionMath.AutoLines(300f, 50, 2, 0f, 200, 1f, 1f));
            // 1.1f is not 1.1: 1.1 x 1000 x 30 / (50 x 110 x 1) = 6 exactly in real numbers
            Assert.Equal(6, GpuProductionMath.AutoLines(1000f, 50, 110, 0f, 200, 1.1f, 1f));
        }

        [Test]
        public void AutoLines_NeverMoreThanTheCap_OnManyCases()
        {
            var random = new Random(5);
            for (int round = 0; round < 1000; round++)
            {
                int capacity = random.Next(60);
                float share = (float)random.NextDouble();
                int lines = GpuProductionMath.AutoLines((float)random.NextDouble() * 5000f, 1 + random.Next(100), random.Next(400) - 20, (float)random.NextDouble() * 1.2f, capacity,
                    0.5f + (float)random.NextDouble() * 2f, share);
                Assert.True(lines >= 0 && lines <= (int)Math.Floor(share * capacity + 1e-6), "round " + round + ": " + lines + " lines, capacity " + capacity + ", share " + share);
            }
        }

        // ---- F31, F32: stock ----

        [Test]
        public void Liquidation_Ac71_IsStockTimesPriceTimesTheShare()
        {
            Assert.Near(400000, GpuProductionMath.LiquidationValue(20000, 200f, 0.1f), 0.5, "AC-7.1: 20 000 x 200 x 0.1");
            Assert.Equal(0f, GpuProductionMath.LiquidationValue(0, 200f, 0.1f));
            Assert.Equal(0f, GpuProductionMath.LiquidationValue(-5, 200f, 0.1f));
            Assert.Equal(0f, GpuProductionMath.LiquidationValue(20000, -200f, 0.1f), "a negative price pays nothing");
            Assert.Equal(0f, GpuProductionMath.LiquidationValue(20000, 200f, -0.1f));
        }

        [Test]
        public void Storage_Ac72_AMonthOfAHundredThousandCostsAboutFiftyThousand()
        {
            float perDay = GpuProductionMath.StorageCostPerDay(100000, 0.5f);
            Assert.Near(100000 * 0.5 / 30, perDay, 0.01);
            Assert.Near(50000, perDay * 30, 0.5, "AC-7.2: about 50 000 for a month");
            Assert.Equal(0f, GpuProductionMath.StorageCostPerDay(0, 0.5f));
            Assert.Equal(0f, GpuProductionMath.StorageCostPerDay(-10, 0.5f));
            Assert.Equal(0f, GpuProductionMath.StorageCostPerDay(100000, -0.5f));
        }
    }
}
