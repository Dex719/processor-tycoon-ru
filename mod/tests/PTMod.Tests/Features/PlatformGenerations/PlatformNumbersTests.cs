using System.Globalization;
using System.Threading;
using PTMod.Features.PlatformGenerations;

namespace PTMod.Tests.Features.PlatformGenerations
{
    /// <summary>
    /// TSK-013, TSK-016: how the feature writes numbers into the texts - at most one decimal for a percent, no trailing zeros, a dot and a plain space whatever
    /// the culture of the machine says, the sign of a component, the multipliers of a column, a base with the thousands apart.
    /// </summary>
    [Covers(typeof(PlatformNumbers))]
    public class PlatformNumbersTests
    {
        [Test]
        public void Percent_HasAtMostOneDecimal_AndNoTrailingZero()
        {
            Assert.Equal("6.7", PlatformNumbers.Percent(0.0667));
            Assert.Equal("6.7", PlatformNumbers.Percent(0.0666667));
            Assert.Equal("6", PlatformNumbers.Percent(0.06));
            Assert.Equal("10", PlatformNumbers.Percent(0.10));
            Assert.Equal("40", PlatformNumbers.Percent(0.4));
            Assert.Equal("12.5", PlatformNumbers.Percent(0.125));
            Assert.Equal("47.5", PlatformNumbers.Percent(0.475));
            Assert.Equal("0.5", PlatformNumbers.Percent(0.005));
            Assert.Equal("0", PlatformNumbers.Percent(0));
            Assert.Equal("100", PlatformNumbers.Percent(1));
        }

        [Test]
        public void Percent_OfANegativeOrBrokenNumber_IsItsSizeOrZero()
        {
            Assert.Equal("5", PlatformNumbers.Percent(-0.05), "the text carries the sign, not the number");
            Assert.Equal("0", PlatformNumbers.Percent(double.NaN));
            Assert.Equal("0", PlatformNumbers.Percent(double.PositiveInfinity));
        }

        [Test]
        public void SignedPercent_CarriesThePlusOrTheMinus_AndNothingForZero()
        {
            Assert.Equal("+7.5", PlatformNumbers.SignedPercent(0.075));
            Assert.Equal("+4.8", PlatformNumbers.SignedPercent(0.048));
            Assert.Equal(PlatformNumbers.Minus + "6.7", PlatformNumbers.SignedPercent(-0.0667));
            Assert.Equal("0", PlatformNumbers.SignedPercent(0));
            Assert.Equal("0", PlatformNumbers.SignedPercent(0.0004), "below a tenth of a percent is nothing");
            Assert.Equal("0", PlatformNumbers.SignedPercent(-0.0004), "and never a minus zero");
            Assert.Equal("0", PlatformNumbers.SignedPercent(double.NaN));
        }

        [Test]
        public void Mult_HasAtLeastTwoDecimals_AndUpToThree()
        {
            Assert.Equal("1.25", PlatformNumbers.Mult(1.25));
            Assert.Equal("1.00", PlatformNumbers.Mult(1.0));
            Assert.Equal("0.50", PlatformNumbers.Mult(0.5));
            Assert.Equal("1.075", PlatformNumbers.Mult(1.075));
            Assert.Equal("0.80", PlatformNumbers.Mult(0.8f));
            Assert.Equal("0.00", PlatformNumbers.Mult(double.NaN), "a broken number counts as 0");
        }

        [Test]
        public void Count_SeparatesTheThousandsWithASpace()
        {
            Assert.Equal("83 120", PlatformNumbers.Count(83120));
            Assert.Equal("0", PlatformNumbers.Count(0));
            Assert.Equal("999", PlatformNumbers.Count(999.4));
            Assert.Equal("1 000", PlatformNumbers.Count(999.6));
            Assert.Equal("1 234 567", PlatformNumbers.Count(1234567));
            Assert.Equal("0", PlatformNumbers.Count(double.NaN));
        }

        [Test]
        public void Join_LeavesOutEmptyNames_AndHandlesNull()
        {
            Assert.Equal("PGA 1990, PGA 1995", PlatformNumbers.Join(new[] { "PGA 1990", null, string.Empty, "PGA 1995" }));
            Assert.Equal(string.Empty, PlatformNumbers.Join(null));
            Assert.Equal(string.Empty, PlatformNumbers.Join(new string[0]));
        }

        [Test]
        public void Cells_RoundsAndStaysInTheRange()
        {
            Assert.Equal(0, PlatformNumbers.Cells(0, 10));
            Assert.Equal(5, PlatformNumbers.Cells(0.475, 10), "47.5 % of ten cells");
            Assert.Equal(10, PlatformNumbers.Cells(1, 10));
            Assert.Equal(10, PlatformNumbers.Cells(7, 10));
            Assert.Equal(0, PlatformNumbers.Cells(-1, 10));
            Assert.Equal(0, PlatformNumbers.Cells(double.NaN, 10));
            Assert.Equal(0, PlatformNumbers.Cells(0.5, 0));
        }

        [Test]
        public void TheSpelling_DoesNotDependOnTheCultureOfTheMachine()
        {
            CultureInfo saved = Thread.CurrentThread.CurrentCulture;
            try
            {
                Thread.CurrentThread.CurrentCulture = new CultureInfo("ru-RU");   // a comma for the decimal mark, a no-break space for the thousands
                Assert.Equal("6.7", PlatformNumbers.Percent(0.0667));
                Assert.Equal("+7.5", PlatformNumbers.SignedPercent(0.075));
                Assert.Equal("1.075", PlatformNumbers.Mult(1.075));
                Assert.Equal("83 120", PlatformNumbers.Count(83120));
                Assert.Equal("1234", PlatformNumbers.Int(1234));
            }
            finally
            {
                Thread.CurrentThread.CurrentCulture = saved;
            }
        }
    }
}
