using PTMod.Features.ResearchTreeUx;

namespace PTMod.Tests.Features.ResearchTreeUx
{
    /// <summary>The numbers of the tooltip (FR-4): groups of three digits set apart, whole numbers, percentages, infinity. TSK-007.</summary>
    [Covers(typeof(TipNumberFormat))]
    public class TipNumberFormatTests
    {
        [Test]
        public void Group_SetsThousandsApart()
        {
            Assert.Equal("0", TipNumberFormat.Group(0));
            Assert.Equal("7", TipNumberFormat.Group(7));
            Assert.Equal("999", TipNumberFormat.Group(999));
            Assert.Equal("1 000", TipNumberFormat.Group(1000));
            Assert.Equal("13 300", TipNumberFormat.Group(13300));
            Assert.Equal("133 000", TipNumberFormat.Group(133000));
            Assert.Equal("1 234 567", TipNumberFormat.Group(1234567));
            Assert.Equal("9 223 372 036 854 775 807", TipNumberFormat.Group(long.MaxValue));
        }

        [Test]
        public void Group_KeepsTheSignOfANegativeNumber()
        {
            Assert.Equal("-5", TipNumberFormat.Group(-5));
            Assert.Equal("-999", TipNumberFormat.Group(-999));
            Assert.Equal("-1 000", TipNumberFormat.Group(-1000));
            Assert.Equal("-133 000", TipNumberFormat.Group(-133000));
            Assert.Equal("-1 500", TipNumberFormat.Group(-1500));
            Assert.Equal("-9 223 372 036 854 775 808", TipNumberFormat.Group(long.MinValue));
        }

        [Test]
        public void Round_RoundsHalfAwayFromZero()
        {
            Assert.Equal("3", TipNumberFormat.Round(2.5));
            Assert.Equal("2", TipNumberFormat.Round(2.4));
            Assert.Equal("-3", TipNumberFormat.Round(-2.5));
            Assert.Equal("0", TipNumberFormat.Round(0.4));
            Assert.Equal("32 160", TipNumberFormat.Round(32160.0));
            Assert.Equal("61", TipNumberFormat.Round(60.5));
        }

        [Test]
        public void Round_ANumberThatIsNone_IsInfinity()
        {
            Assert.Equal("∞", TipNumberFormat.Round(double.NaN));
            Assert.Equal("∞", TipNumberFormat.Round(double.PositiveInfinity));
            Assert.Equal("∞", TipNumberFormat.Round(double.NegativeInfinity));
            Assert.Equal("∞", TipNumberFormat.Infinity);
        }

        [Test]
        public void RoundToLong_StaysInsideTheRangeOfLong()
        {
            Assert.Equal(long.MaxValue, TipNumberFormat.RoundToLong(1e30));
            Assert.Equal(long.MinValue, TipNumberFormat.RoundToLong(-1e30));
            Assert.Equal(0L, TipNumberFormat.RoundToLong(double.NaN));
            Assert.Equal(13300L, TipNumberFormat.RoundToLong(13299.6));
        }

        [Test]
        public void Percent_IsAFractionTimesAHundred()
        {
            Assert.Equal("50 %", TipNumberFormat.Percent(0.5));
            Assert.Equal("100 %", TipNumberFormat.Percent(1.0));
            Assert.Equal("200 %", TipNumberFormat.Percent(2.0));
            Assert.Equal("500 %", TipNumberFormat.Percent(5.0));
            Assert.Equal("0 %", TipNumberFormat.Percent(0.0));
            Assert.Equal("1 %", TipNumberFormat.Percent(0.01));
            Assert.Equal("1 300 %", TipNumberFormat.Percent(13.0));
        }

        [Test]
        public void PercentValue_IsTheNumberAlone_ForATextWithTheSignOfItsOwn()
        {
            Assert.Equal("50", TipNumberFormat.PercentValue(0.5));
            Assert.Equal("45", TipNumberFormat.PercentValue(0.452));
            Assert.Equal("500", TipNumberFormat.PercentValue(5.0));
            Assert.Equal("∞", TipNumberFormat.PercentValue(double.NaN));
        }
    }
}
