using System;
using PTMod.Features.GpuProductLine;

namespace PTMod.Tests.Features.GpuProductLine
{
    /// <summary>
    /// The arithmetic of the GPU laboratory (TSK-008, FR-2; F15, F16): the time of a node in the current year (AC-2.1, AC-2.2), the cap of the discount, the progress
    /// and the cost of a day. The numbers are the ones of <c>TechnologyTimeUpdater.UpdateTechnologyTimes</c> (penalty 3.5 and bonus 0.15 a year, discount at most 75 %).
    /// </summary>
    [Covers(typeof(GpuResearchMath))]
    public class GpuResearchMathTests
    {
        private const float Penalty = 3.5f, Bonus = 0.15f, MaxBonus = 0.75f;

        private static float Days(int baseDays, int nodeYear, DateTime today)
        {
            return GpuResearchMath.EffectiveDays(baseDays, nodeYear, today, Penalty, Bonus, MaxBonus);
        }

        // ---- F15 ----

        [Test]
        public void Ac21_OnTheFirstOfJanuary1996_ANodeOf1995TakesItsBaseTime()
        {
            // 1 Jan 1995 -> 1 Jan 1996 is exactly 365 days: the discount starts after a full year, so it is still 0.
            float teff = Days(200, 1995, new DateTime(1996, 1, 1));
            Assert.Equal(200f, teff, "T_eff = 200");

            // AC-2.1: progress = sqrt(1.0) x 1.05 / T_eff, cost = 2e7 / 30 a day
            float speed = GpuResearchMath.ResearchSpeedPort(1.0f, 1.05f);
            Assert.Near(1.05, speed, 1e-6);
            Assert.Near(1.05 / 200.0, GpuResearchMath.DailyProgress(speed, teff), 1e-7);
            Assert.Near(666667, GpuResearchMath.DailyCost(2e7f, 1f, 1f), 1.0, "about 666 667 a day");
        }

        [Test]
        public void Ac22_AFutureNode_TakesManyTimesLonger()
        {
            // 1 Jan 1990 -> 1 Jan 1995 is 1826 days (one leap day, 1992): 200 x (1 + 3.5 x 1826 / 365)
            float teff = Days(200, 1995, new DateTime(1990, 1, 1));
            Assert.Near(200.0 * (1 + 3.5 * 1826.0 / 365.0), teff, 0.5);
            Assert.Near(3700, teff, 3700 * 0.01, "AC-2.2: about 3700 days");
            Assert.True(teff / 365f > 9.5f && teff / 365f < 10.5f, "the window shows about ten years: " + teff / 365f);
            Assert.Near(teff / 200f, GpuResearchMath.AheadFactor(200, teff), 1e-4);
            Assert.True(GpuResearchMath.AheadFactor(200, teff) > 18f, "'ahead of its time' x18.5");
        }

        [Test]
        public void AFutureNode_PenaltyShrinksDayByDay()
        {
            float far = Days(120, 1985, new DateTime(1981, 1, 1));
            float near = Days(120, 1985, new DateTime(1984, 1, 1));
            float sameYear = Days(120, 1985, new DateTime(1985, 6, 1));
            Assert.True(far > near && near > sameYear, far + " > " + near + " > " + sameYear);
            Assert.Equal(120f, sameYear, "in the year of the node there is no penalty");
            // mid-year: 1 Jul 1984 -> 1 Jan 1985 = 184 days
            Assert.Near(120.0 * (1 + 3.5 * 184.0 / 365.0), Days(120, 1985, new DateTime(1984, 7, 1)), 0.01);
        }

        [Test]
        public void ANodeOfThePast_GetsADiscountAfterAYear_CappedAt75Percent()
        {
            // 1 Jan 1995 -> 1 Jan 1997 = 731 days: (731 - 365) / 365 x 0.15 = 15.04 %
            float teff = Days(200, 1995, new DateTime(1997, 1, 1));
            Assert.Near(200.0 * (1 - (731 - 365) / 365.0 * 0.15), teff, 0.01);
            Assert.True(teff < 200f && teff > 160f, teff.ToString());

            // the first year after the node: still nothing
            Assert.Equal(200f, Days(200, 1995, new DateTime(1996, 1, 1)), "exactly one year");
            Assert.Equal(200f, Days(200, 1995, new DateTime(1995, 12, 31)), "the last day of the year of the node");
            float inFirstYear = Days(200, 1995, new DateTime(1996, 7, 1));
            Assert.Near(200.0 * (1 - (547 - 365) / 365.0 * 0.15), inFirstYear, 0.01, "1 Jul 1996: 547 days");

            // a very old node: capped at 75 % off
            Assert.Near(200.0 * 0.25, Days(200, 1981, new DateTime(2000, 1, 1)), 1e-3, "the cap: 25 % of the base time");
            Assert.Near(50, Days(200, 1971, new DateTime(2035, 5, 5)), 1e-3);
        }

        [Test]
        public void TheCap_IsABalanceValue_AndStaysInZeroToOne()
        {
            Assert.Near(100, GpuResearchMath.EffectiveDays(200, 1981, new DateTime(2000, 1, 1), Penalty, Bonus, 0.5f), 1e-3, "at most 50 % off");
            Assert.Near(200, GpuResearchMath.EffectiveDays(200, 1981, new DateTime(2000, 1, 1), Penalty, Bonus, 0f), 1e-3, "no discount at all");
            Assert.True(GpuResearchMath.EffectiveDays(200, 1981, new DateTime(2000, 1, 1), Penalty, Bonus, 5f) >= 0f, "a cap above 1 cannot make the time negative");
            Assert.Near(0, GpuResearchMath.EffectiveDays(200, 1981, new DateTime(2000, 1, 1), Penalty, Bonus, 5f), 1e-3, "it is read as 1: the node takes no time");
            Assert.Near(200, GpuResearchMath.EffectiveDays(200, 1981, new DateTime(2000, 1, 1), Penalty, -3f, MaxBonus), 1e-3, "a negative rate is no discount");
            Assert.Near(200, GpuResearchMath.EffectiveDays(200, 2005, new DateTime(2000, 1, 1), -3f, Bonus, MaxBonus), 1e-3, "a negative penalty is no penalty");
        }

        [Test]
        public void ANodeWithoutBaseTime_TakesNoTime_AndNeverDivides()
        {
            Assert.Equal(0f, Days(0, 2000, new DateTime(1995, 1, 1)));
            Assert.Equal(0f, Days(-5, 2000, new DateTime(1995, 1, 1)));
            Assert.Equal(1f, GpuResearchMath.DailyProgress(1.05f, 0f), "done at once");
            Assert.Equal(1f, GpuResearchMath.DailyProgress(1.05f, -3f));
            Assert.Equal(1f, GpuResearchMath.AheadFactor(0, 100f));
        }

        [Test]
        public void YearsOutsideTheCalendar_DoNotThrow()
        {
            Assert.True(Days(200, 0, new DateTime(2000, 1, 1)) >= 0f);
            Assert.True(Days(200, int.MaxValue, new DateTime(2000, 1, 1)) > 0f);
            Assert.True(Days(200, -5, new DateTime(2000, 1, 1)) >= 0f);
        }

        // ---- F16 ----

        [Test]
        public void DailyProgress_IsSpeedOverTime()
        {
            Assert.Near(0.005, GpuResearchMath.DailyProgress(1f, 200f), 1e-7);
            Assert.Near(0.00525, GpuResearchMath.DailyProgress(1.05f, 200f), 1e-7);
            Assert.Near(0, GpuResearchMath.DailyProgress(-1f, 200f), 0, "negative speed does not undo research");
            Assert.Near(0, GpuResearchMath.DailyProgress(float.NaN, 200f), 0);
        }

        [Test]
        public void FundingSlowsResearchBySquareRoot_AndCostsLinearly()
        {
            float full = GpuResearchMath.DailyProgress(GpuResearchMath.ResearchSpeedPort(1.0f, 1.05f), 200f);
            float half = GpuResearchMath.DailyProgress(GpuResearchMath.ResearchSpeedPort(0.5f, 1.05f), 200f);
            Assert.Near(1.0 / Math.Sqrt(0.5), full / half, 1e-4, "50 % funding: the node takes about 1.41 times longer");

            Assert.Near(2e7 / 30 * 0.5, GpuResearchMath.DailyCost(2e7f, 1f, 0.5f), 1.0, "and costs half");
            Assert.Near(2e7 / 30 * 0.67, GpuResearchMath.DailyCost(2e7f, 0.67f, 1f), 1.0, "the research cost modifier");
            Assert.Near(2e7 / 30 * 1.5 * 0.1, GpuResearchMath.DailyCost(2e7f, 1.5f, 0.1f), 1.0);
            Assert.Equal(0f, GpuResearchMath.DailyCost(2e7f, 1f, 0f), "no funding, no cost");
            Assert.Equal(0f, GpuResearchMath.DailyCost(2e7f, 1f, -1f), "a negative funding is no cost, not an income");
            Assert.Equal(0f, GpuResearchMath.DailyCost(float.NaN, 1f, 1f));
        }

        [Test]
        public void ResearchSpeedPort_IsSqrtOfFundingTimesTheModifier()
        {
            Assert.Near(1.05, GpuResearchMath.ResearchSpeedPort(1.0f, 1.05f), 1e-6, "an IDM company has 1.05");
            Assert.Near(0.5, GpuResearchMath.ResearchSpeedPort(0.25f, 1.0f), 1e-6);
            Assert.Equal(0f, GpuResearchMath.ResearchSpeedPort(-1f, 1.0f));
        }

        [Test]
        public void DaysLeft_IsTheRemainingShareOverTheDailyProgress()
        {
            Assert.Equal(200, GpuResearchMath.DaysLeft(0f, 0.005f));
            Assert.Equal(100, GpuResearchMath.DaysLeft(0.5f, 0.005f));
            Assert.Equal(0, GpuResearchMath.DaysLeft(1f, 0.005f));
            Assert.Equal(0, GpuResearchMath.DaysLeft(1.5f, 0.005f), "progress past the end");
            Assert.Equal(-1, GpuResearchMath.DaysLeft(0.3f, 0f), "it does not move");
            Assert.Equal(-1, GpuResearchMath.DaysLeft(0.3f, float.NaN));
        }

        [Test]
        public void AheadFactor_IsOneUnlessTheNodeIsSlower()
        {
            Assert.Equal(1f, GpuResearchMath.AheadFactor(200, 200f));
            Assert.Equal(1f, GpuResearchMath.AheadFactor(200, 150f), "a discount is not 'ahead of time'");
            Assert.Near(2.5, GpuResearchMath.AheadFactor(200, 500f), 1e-6);
        }
    }
}
