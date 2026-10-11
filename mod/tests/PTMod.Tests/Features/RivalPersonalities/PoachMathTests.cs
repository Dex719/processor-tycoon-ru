using System;
using PTMod.Features.RivalPersonalities;

namespace PTMod.Tests.Features.RivalPersonalities
{
    /// <summary>
    /// The price of keeping the engineers (TSK-015; design.md formula 9; FR-7 item 6): <c>C = max(250 000 × Inflation, 2 × ExpenseByResearch of the last
    /// full month)</c>, and just the first term while the company has fewer than two monthly reports.
    /// </summary>
    [Covers(typeof(PoachMath))]
    public class PoachMathTests
    {
        [Test]
        public void FewerThanTwoReports_GiveTheMinimum_WhateverTheReportSays()
        {
            Assert.Near(250000.0, PoachMath.CounterOfferCost(1f, 0, 9e9f, 250000, 2), 1e-3);
            Assert.Near(250000.0, PoachMath.CounterOfferCost(1f, 1, 9e9f, 250000, 2), 1e-3, "one report: the month in progress, not a full month");
            Assert.Near(375000.0, PoachMath.CounterOfferCost(1.5f, 1, 9e9f, 250000, 2), 1e-3, "the minimum grows with the inflation");
        }

        [Test]
        public void WithTwoOrMoreReports_ItIsTheLargerOfTheTwoTerms()
        {
            // inflation 1.5: the minimum is 375 000
            Assert.Near(800000.0, PoachMath.CounterOfferCost(1.5f, 5, 400000f, 250000, 2), 1e-3, "2 x 400 000 is more");
            Assert.Near(375000.0, PoachMath.CounterOfferCost(1.5f, 5, 50000f, 250000, 2), 1e-3, "2 x 50 000 is less");
            Assert.Near(375000.0, PoachMath.CounterOfferCost(1.5f, 2, 187500f, 250000, 2), 1e-3, "equal: the same number");
            Assert.Near(1000000.0, PoachMath.CounterOfferCost(1f, 2, 500000f, 250000, 2), 1e-3, "two reports are enough");
        }

        [Test]
        public void TheInflation_ScalesOnlyTheMinimum()
        {
            Assert.Near(800000.0, PoachMath.CounterOfferCost(3f, 4, 400000f, 250000, 2), 1e-3, "the spending of the company is already in today's money");
            Assert.Near(750000.0, PoachMath.CounterOfferCost(3f, 4, 100000f, 250000, 2), 1e-3);
        }

        [Test]
        public void TheLastFullMonth_IsTheReportBeforeTheLast()
        {
            Assert.Equal(-1, PoachMath.LastFullMonthIndex(0));
            Assert.Equal(-1, PoachMath.LastFullMonthIndex(1));
            Assert.Equal(0, PoachMath.LastFullMonthIndex(2));
            Assert.Equal(8, PoachMath.LastFullMonthIndex(10));
            Assert.Equal(2, PoachMath.MinReports);
        }

        [Test]
        public void TheNumbersOfTheConfig_AreUsed()
        {
            RivalConfig config = RivalConfig.Defaults();
            Assert.Near(250000.0, PoachMath.CounterOfferCost(1f, 0, 0f, config), 1e-3);
            Assert.Near(400000.0, PoachMath.CounterOfferCost(1f, 3, 200000f, config), 1e-3, "researchMonths 2 by default");

            config.reactions.Poach.minCost = 100000;
            config.reactions.Poach.researchMonths = 3;
            Assert.Near(600000.0, PoachMath.CounterOfferCost(1f, 3, 200000f, config), 1e-3, "3 x 200 000");
            Assert.Near(100000.0, PoachMath.CounterOfferCost(1f, 1, 200000f, config), 1e-3);

            Assert.Near(250000.0, PoachMath.CounterOfferCost(1f, 0, 0f, (RivalConfig)null), 1e-3, "no config: the defaults");
        }

        [Test]
        public void DamagedNumbers_NeverGiveANonsenseCost()
        {
            Assert.Near(250000.0, PoachMath.CounterOfferCost(float.NaN, 0, 0f, 250000, 2), 1e-3, "an unknown inflation counts as 1");
            Assert.Near(250000.0, PoachMath.CounterOfferCost(-2f, 0, 0f, 250000, 2), 1e-3);
            Assert.Near(250000.0, PoachMath.CounterOfferCost(float.PositiveInfinity, 0, 0f, 250000, 2), 1e-3);
            Assert.Near(250000.0, PoachMath.CounterOfferCost(1f, 5, float.NaN, 250000, 2), 1e-3, "an unreadable report counts as no spending");
            Assert.Near(250000.0, PoachMath.CounterOfferCost(1f, 5, -1e6f, 250000, 2), 1e-3);
            Assert.Near(0.0, PoachMath.CounterOfferCost(1f, 0, 0f, -5, 2), 1e-3, "a negative minimum is 0");
            Assert.Near(250000.0, PoachMath.CounterOfferCost(1f, 5, 1e6f, 250000, 0), 1e-3, "no months: only the minimum");
            Assert.True(PoachMath.CounterOfferCost(1f, 5, float.MaxValue, 250000, 2) <= float.MaxValue);
        }
    }
}
