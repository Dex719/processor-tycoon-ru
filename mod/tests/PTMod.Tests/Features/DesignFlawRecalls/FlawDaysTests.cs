using System;
using PTMod.Features.DesignFlawRecalls;

namespace PTMod.Tests.Features.DesignFlawRecalls
{
    /// <summary>
    /// The day arithmetic the monthly and daily cycle stands on (TSK-008, D12): calendar months added to a day, whole months between two days, the
    /// key of a month for the "once a month" caches, and the small numeric helpers of the formulas.
    /// </summary>
    [Covers(typeof(FlawDays))]
    public class FlawDaysTests
    {
        private static int Day(int year, int month, int day)
        {
            return FlawDays.FromDate(new DateTime(year, month, day));
        }

        [Test]
        public void AddMonths_KeepsTheDayOfTheMonth_AndTheLastDayOfAShorterMonth()
        {
            Assert.Equal(Day(1991, 7, 15), FlawDays.AddMonths(Day(1990, 1, 15), 18));
            Assert.Equal(Day(1991, 2, 28), FlawDays.AddMonths(Day(1990, 11, 30), 3));
            Assert.Equal(Day(1992, 2, 29), FlawDays.AddMonths(Day(1991, 11, 30), 3), "a leap year");
            Assert.Equal(Day(1991, 1, 1), FlawDays.AddMonths(Day(1990, 1, 1), 12));
        }

        [Test]
        public void AddMonths_ZeroOrNegative_DoesNotMoveBack()
        {
            Assert.Equal(Day(1990, 5, 5), FlawDays.AddMonths(Day(1990, 5, 5), 0));
            Assert.Equal(Day(1990, 5, 5), FlawDays.AddMonths(Day(1990, 5, 5), -4));
        }

        [Test]
        public void FullMonthsBetween_CountsWholeCalendarMonths()
        {
            int release = Day(1990, 1, 15);

            Assert.Equal(0, FlawDays.FullMonthsBetween(release, Day(1990, 2, 14)));
            Assert.Equal(1, FlawDays.FullMonthsBetween(release, Day(1990, 2, 15)));
            Assert.Equal(1, FlawDays.FullMonthsBetween(release, Day(1990, 3, 1)));
            Assert.Equal(23, FlawDays.FullMonthsBetween(release, Day(1992, 1, 1)));
            Assert.Equal(24, FlawDays.FullMonthsBetween(release, Day(1992, 1, 15)));
            Assert.Equal(24, FlawDays.FullMonthsBetween(release, Day(1992, 2, 1)));
        }

        [Test]
        public void FullMonthsBetween_FromTheFirstOfAMonth_TheTickOfTheFirstCountsTheMonth()
        {
            int release = Day(1990, 1, 1);

            Assert.Equal(24, FlawDays.FullMonthsBetween(release, Day(1992, 1, 1)));
            Assert.Equal(23, FlawDays.FullMonthsBetween(release, Day(1991, 12, 1)));
        }

        [Test]
        public void FullMonthsBetween_NotAfter_IsZero()
        {
            Assert.Equal(0, FlawDays.FullMonthsBetween(Day(1990, 5, 5), Day(1990, 5, 5)));
            Assert.Equal(0, FlawDays.FullMonthsBetween(Day(1990, 5, 5), Day(1989, 1, 1)));
        }

        [Test]
        public void MonthKey_IsTheSameInAMonth_AndGrowsByOne()
        {
            Assert.Equal(FlawDays.MonthKey(Day(1990, 3, 1)), FlawDays.MonthKey(Day(1990, 3, 31)));
            Assert.Equal(FlawDays.MonthKey(Day(1990, 3, 31)) + 1, FlawDays.MonthKey(Day(1990, 4, 1)));
            Assert.Equal(FlawDays.MonthKey(Day(1990, 12, 31)) + 1, FlawDays.MonthKey(Day(1991, 1, 1)));
        }

        [Test]
        public void YearOf_IsTheCalendarYear()
        {
            Assert.Equal(1994, FlawDays.YearOf(Day(1994, 6, 30)));
            Assert.Equal(1971, FlawDays.YearOf(0));
        }

        // ---- the helpers of the formulas ----

        [Test]
        public void Exact_GivesTheDecimalNumberTheCoefficientWasWrittenAs()
        {
            Assert.Equal(0.15, FlawMath.Exact(0.15f));
            Assert.Equal(0.3, FlawMath.Exact(0.30f));
            Assert.Equal(2000000.0, FlawMath.Exact(2000000f));
            Assert.Equal(0.0, FlawMath.Exact(0f));
            Assert.True(double.IsNaN(FlawMath.Exact(float.NaN)), "a broken number stays broken for the caller to handle");
            Assert.Equal((double)1e30f, FlawMath.Exact(1e30f), "a number outside the range of decimal is returned as it is");
        }

        [Test]
        public void Clamp_And_Finite_HandleBrokenNumbers()
        {
            Assert.Equal(0.0, FlawMath.Clamp(double.NaN, 0.0, 1.0));
            Assert.Equal(1.0, FlawMath.Clamp(double.PositiveInfinity, 0.0, 1.0));
            Assert.Equal(0.0, FlawMath.Clamp(-3.0, 0.0, 1.0));
            Assert.Equal(0.25, FlawMath.Clamp(0.25, 0.0, 1.0));
            Assert.Equal(0.0, FlawMath.Finite(double.NaN));
            Assert.Equal(5.0, FlawMath.Finite(5.0));
            Assert.False(FlawMath.IsFinite(double.NegativeInfinity));
        }

        [Test]
        public void At_ReadsAnArray_OrTheFallback()
        {
            Assert.Equal(2f, FlawMath.At(new[] { 1f, 2f }, 1, 9f));
            Assert.Equal(9f, FlawMath.At(new[] { 1f, 2f }, 2, 9f));
            Assert.Equal(9f, FlawMath.At((float[])null, 0, 9f));
            Assert.Equal(7, FlawMath.At(new[] { 5, 6 }, -1, 7));
        }

        [Test]
        public void SeverityIndex_LimitsToTheThreeSeverities()
        {
            Assert.Equal(0, FlawMath.SeverityIndex(FlawSeverity.Minor));
            Assert.Equal(2, FlawMath.SeverityIndex(FlawSeverity.Critical));
            Assert.Equal(2, FlawMath.SeverityIndex((FlawSeverity)9));
            Assert.Equal(0, FlawMath.SeverityIndex((FlawSeverity)(-2)));
        }
    }
}
