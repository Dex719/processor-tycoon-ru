using System;
using PTMod.Core.Logic.Rng;
using PTMod.Features.DesignFlawRecalls;

namespace PTMod.Tests.Features.DesignFlawRecalls
{
    /// <summary>
    /// TSK-007 (FR-4): the discovery of a hidden flaw. The nine values of the table <c>h</c> of design.md (formula 3), the forced discovery by
    /// <c>maxHiddenMonths</c>, the expiry by <c>expireAfterRetiredMonths</c> and <c>expireNoSalesMonths</c>, the source, and the leak rates.
    /// </summary>
    [Covers(typeof(DiscoveryModel))]
    public class DiscoveryModelTests
    {
        private static FlawBalance Balance()
        {
            return FlawBalance.Defaults();
        }

        // ---- h ----

        [Test]
        public void MonthlyHazard_Table_NineValues()
        {
            // Rows: severity; columns: U = 100 thousand, 1 million, 10 million.
            double[,] expected =
            {
                { 0.026, 0.041, 0.060 },   // Minor
                { 0.052, 0.082, 0.120 },   // Major
                { 0.091, 0.143, 0.210 },   // Critical
            };
            long[] units = { 100000L, 1000000L, 10000000L };

            for (int sev = 0; sev < 3; sev++)
            {
                for (int column = 0; column < 3; column++)
                {
                    Assert.Near(expected[sev, column], DiscoveryModel.MonthlyHazard((FlawSeverity)sev, units[column], Balance()), 0.001,
                        (FlawSeverity)sev + " at " + units[column] + " units");
                }
            }
        }

        [Test]
        public void MonthlyHazard_AC41_MajorAtOneMillion_IsAbout082()
        {
            // h = 0.04 * (1 + log10(1 + 10)) = 0.0817
            Assert.Near(0.04 * (1 + Math.Log10(11)), DiscoveryModel.MonthlyHazard(FlawSeverity.Major, 1000000L, Balance()), 1e-6);
        }

        [Test]
        public void MonthlyHazard_NoSales_IsTheBase_AndNegativeUnitsCountAsNone()
        {
            FlawBalance b = Balance();

            Assert.Near(0.02, DiscoveryModel.MonthlyHazard(FlawSeverity.Minor, 0L, b), 1e-9);
            Assert.Near(0.04, DiscoveryModel.MonthlyHazard(FlawSeverity.Major, 0L, b), 1e-9);
            Assert.Near(0.07, DiscoveryModel.MonthlyHazard(FlawSeverity.Critical, -5L, b), 1e-9);
        }

        [Test]
        public void MonthlyHazard_IsLimitedByHMax_AndGrowsWithSalesAndSeverity()
        {
            FlawBalance b = Balance();

            Assert.Near(0.35, DiscoveryModel.MonthlyHazard(FlawSeverity.Critical, 1000000000000L, b), 1e-6);
            b.Discovery.HMax = 0.1f;
            Assert.Near(0.1, DiscoveryModel.MonthlyHazard(FlawSeverity.Critical, 1000000L, b), 1e-6);

            FlawBalance fresh = Balance();
            Assert.True(DiscoveryModel.MonthlyHazard(FlawSeverity.Major, 10000L, fresh) < DiscoveryModel.MonthlyHazard(FlawSeverity.Major, 1000000L, fresh));
            Assert.True(DiscoveryModel.MonthlyHazard(FlawSeverity.Minor, 1000000L, fresh) < DiscoveryModel.MonthlyHazard(FlawSeverity.Critical, 1000000L, fresh));
        }

        // ---- forced ----

        [Test]
        public void IsForced_AC42_Critical_24MonthsAndSomethingSold()
        {
            FlawBalance b = Balance();

            Assert.True(DiscoveryModel.IsForced(FlawSeverity.Critical, 24, 5000L, b));
            Assert.True(DiscoveryModel.IsForced(FlawSeverity.Critical, 30, 1L, b));
            Assert.False(DiscoveryModel.IsForced(FlawSeverity.Critical, 23, 5000L, b));
        }

        [Test]
        public void IsForced_LimitsBySeverity_48_36_24()
        {
            FlawBalance b = Balance();

            Assert.False(DiscoveryModel.IsForced(FlawSeverity.Minor, 47, 10L, b));
            Assert.True(DiscoveryModel.IsForced(FlawSeverity.Minor, 48, 10L, b));
            Assert.False(DiscoveryModel.IsForced(FlawSeverity.Major, 35, 10L, b));
            Assert.True(DiscoveryModel.IsForced(FlawSeverity.Major, 36, 10L, b));
        }

        [Test]
        public void IsForced_NothingSold_NeverForces()
        {
            Assert.False(DiscoveryModel.IsForced(FlawSeverity.Critical, 120, 0L, Balance()));
        }

        // ---- expired ----

        [Test]
        public void IsExpired_AC43_RetiredMoreThan36MonthsAgo()
        {
            FlawBalance b = Balance();

            Assert.True(DiscoveryModel.IsExpired(true, 37, 50, 100L, b));
            Assert.False(DiscoveryModel.IsExpired(true, 36, 50, 100L, b), "exactly 36 months is not yet more than 36");
            Assert.False(DiscoveryModel.IsExpired(true, 5, 50, 100L, b));
        }

        [Test]
        public void IsExpired_NotRetired_IgnoresTheMonthsSinceRetired()
        {
            Assert.False(DiscoveryModel.IsExpired(false, 99, 50, 100L, Balance()));
        }

        [Test]
        public void IsExpired_NoSalesFor24Months()
        {
            FlawBalance b = Balance();

            Assert.True(DiscoveryModel.IsExpired(false, 0, 24, 0L, b));
            Assert.True(DiscoveryModel.IsExpired(false, 0, 40, 0L, b));
            Assert.False(DiscoveryModel.IsExpired(false, 0, 23, 0L, b), "a CPU that is on the market for less than the window is not judged yet");
            Assert.False(DiscoveryModel.IsExpired(false, 0, 40, 1L, b), "one unit in the window keeps it alive");
        }

        [Test]
        public void IsExpired_FollowsTheContent()
        {
            FlawBalance b = Balance();
            b.Discovery.ExpireAfterRetiredMonths = 12;
            b.Discovery.ExpireNoSalesMonths = 6;

            Assert.True(DiscoveryModel.IsExpired(true, 13, 20, 50L, b));
            Assert.True(DiscoveryModel.IsExpired(false, 0, 6, 0L, b));
            Assert.False(DiscoveryModel.IsExpired(false, 0, 5, 0L, b));
        }

        // ---- the source ----

        [Test]
        public void PickSource_ScriptedRng_InternalBelowQ_ExternalAbove()
        {
            FlawBalance b = Balance();   // qInternal = [0.65, 0.55, 0.40]

            Assert.Equal(DiscoverySource.Internal, DiscoveryModel.PickSource(FlawSeverity.Minor, new FakeRng(0.64f), b));
            Assert.Equal(DiscoverySource.External, DiscoveryModel.PickSource(FlawSeverity.Minor, new FakeRng(0.66f), b));
            Assert.Equal(DiscoverySource.Internal, DiscoveryModel.PickSource(FlawSeverity.Critical, new FakeRng(0.39f), b));
            Assert.Equal(DiscoverySource.External, DiscoveryModel.PickSource(FlawSeverity.Critical, new FakeRng(0.41f), b));
        }

        [Test]
        public void PickSource_Share_FollowsQInternal_AndDrawsOneNumber()
        {
            FlawBalance b = Balance();
            var rng = new Xoshiro128(3u);
            int internalCount = 0;
            const int draws = 50000;
            for (int i = 0; i < draws; i++)
            {
                if (DiscoveryModel.PickSource(FlawSeverity.Major, rng, b) == DiscoverySource.Internal) internalCount++;
            }
            Assert.Near(0.55, internalCount / (double)draws, 0.01);

            var counted = new FakeRng(0.5f);
            DiscoveryModel.PickSource(FlawSeverity.Major, counted, b);
            Assert.Equal(1, counted.Draws);
        }

        // ---- the leak ----

        [Test]
        public void LeakRate_Ignore_QuietStepping_AndTail()
        {
            FlawBalance b = Balance();

            Assert.Near(0.06, DiscoveryModel.LeakRate(FlawResponse.Ignore, false, b), 1e-6);
            Assert.Near(0.06, DiscoveryModel.LeakRate(FlawResponse.Ignore, true, b), 1e-6, "the flag matters for a revision only");
            Assert.Near(0.03, DiscoveryModel.LeakRate(FlawResponse.Stepping, false, b), 1e-6);
            Assert.Near(0.015, DiscoveryModel.LeakRate(FlawResponse.Stepping, true, b), 1e-6);
        }

        [Test]
        public void LeakRate_OtherResponses_AreZero()
        {
            FlawBalance b = Balance();

            Assert.Near(0.0, DiscoveryModel.LeakRate(FlawResponse.None, false, b), 1e-9);
            Assert.Near(0.0, DiscoveryModel.LeakRate(FlawResponse.Recall, false, b), 1e-9);
            Assert.Near(0.0, DiscoveryModel.LeakRate(FlawResponse.Patch, true, b), 1e-9);
        }

        [Test]
        public void LeakRate_FollowsTheContent()
        {
            FlawBalance b = Balance();
            b.Discovery.LeakRate.Ignore = 0.5f;
            b.Discovery.LeakRate.QuietStepping = 0.25f;
            b.Discovery.LeakRate.Tail = 0.125f;

            Assert.Near(0.5, DiscoveryModel.LeakRate(FlawResponse.Ignore, false, b), 1e-9);
            Assert.Near(0.25, DiscoveryModel.LeakRate(FlawResponse.Stepping, false, b), 1e-9);
            Assert.Near(0.125, DiscoveryModel.LeakRate(FlawResponse.Stepping, true, b), 1e-9);
        }
    }
}
