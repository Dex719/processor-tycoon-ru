using PTMod.Features.BankruptcyRecovery.Logic;

namespace PTMod.Tests.Features.BankruptcyRecovery
{
    /// <summary>
    /// TSK-014 (FR-6 item 1, FR-7 item 3, FR-10 item 3, design.md "UI": a slider of 25 to 100 percent for the bridge loan): the sums behind the sliders of the "Loans" tab. A slider
    /// picks a share of a limit in steps of five; the whole limit is exact, a part is a whole number of dollars that never exceeds the limit. The early repayment of a loan
    /// (all of it or a part, FR-10 item 3) is the same arithmetic over the debt.
    /// </summary>
    [Covers(typeof(AmountSteps))]
    public class AmountStepsTests
    {
        [Test]
        public void TheWholeLimit_IsExact_AtOneHundredPercent()
        {
            Assert.Near(8400000.0, AmountSteps.OfMax(8400000.0, 100), 0);
            Assert.Near(1234567.89, AmountSteps.OfMax(1234567.89, 100), 0, "a limit with cents is taken whole, so that the top of the slider is the most the service allows");
            Assert.Near(8400000.0, AmountSteps.OfMax(8400000.0, 250), 0, "a share above 100 is the whole limit");
        }

        [Test]
        public void APart_IsAShareOfTheLimit_InWholeDollars()
        {
            Assert.Near(2100000.0, AmountSteps.OfMax(8400000.0, 25), 0, "the lowest share of the bridge loan");
            Assert.Near(6300000.0, AmountSteps.OfMax(8400000.0, 75), 0);
            Assert.Near(329.0, AmountSteps.OfMax(999.0, 33), 0, "329.67 dollars, rounded down to whole dollars");
            Assert.Near(0.0, AmountSteps.OfMax(10.0, 5), 0, "half a dollar is rounded down to 0: nothing to take");
        }

        [Test]
        public void APart_NeverExceedsTheLimit()
        {
            foreach (double max in new[] { 1.0, 17.0, 999999.99, 8400000.0, 123456789.0 })
            {
                for (int percent = 0; percent <= 120; percent += 5)
                {
                    double sum = AmountSteps.OfMax(max, percent);
                    Assert.True(sum >= 0.0 && sum <= max, "limit " + max + ", " + percent + "%: " + sum);
                }
            }
        }

        [Test]
        public void NothingIsGiven_ForABadLimit_OrAShareOfZero()
        {
            Assert.Near(0.0, AmountSteps.OfMax(0.0, 50), 0);
            Assert.Near(0.0, AmountSteps.OfMax(-5.0, 50), 0);
            Assert.Near(0.0, AmountSteps.OfMax(double.NaN, 50), 0);
            Assert.Near(0.0, AmountSteps.OfMax(double.PositiveInfinity, 50), 0);
            Assert.Near(0.0, AmountSteps.OfMax(8400000.0, 0), 0);
            Assert.Near(0.0, AmountSteps.OfMax(8400000.0, -10), 0);
        }

        [Test]
        public void TheSteps_AreFivePercent_AndTheBridgeSliderStartsAtTwentyFive()
        {
            Assert.Equal(5, AmountSteps.PercentStep);
            Assert.Equal(25, AmountSteps.BridgeMinPercent);
            Assert.Equal(100, AmountSteps.MaxPercent);
            Assert.True(AmountSteps.LineMinPercent >= AmountSteps.PercentStep && AmountSteps.LineMinPercent < AmountSteps.MaxPercent);
            Assert.Equal(0, (AmountSteps.MaxPercent - AmountSteps.BridgeMinPercent) % AmountSteps.PercentStep, "the top of the slider is on a step");
            Assert.Equal(0, (AmountSteps.MaxPercent - AmountSteps.LineMinPercent) % AmountSteps.PercentStep);
        }

        [Test]
        public void PrepayAmount_IsAShareOfTheDebt_AndTheWholeDebtAtOneHundredPercent_FR_10_3()
        {
            Assert.Near(5000000.0, AmountSteps.PrepayAmount(5000000.0, 100), 0, "the top of the slider closes the loan, to the cent");
            Assert.Near(4999999.17, AmountSteps.PrepayAmount(4999999.17, 100), 0, "a debt with cents is paid whole, so that nothing is left behind");
            Assert.Near(2500000.0, AmountSteps.PrepayAmount(5000000.0, 50), 0);
            Assert.Near(250000.0, AmountSteps.PrepayAmount(5000000.0, 5), 0, "the lowest share of the slider");
            Assert.Near(0.0, AmountSteps.PrepayAmount(10.0, 5), 0, "half a dollar is rounded down to 0: the button says the share is too small");
            Assert.Near(0.0, AmountSteps.PrepayAmount(0.0, 100), 0);
            Assert.Near(0.0, AmountSteps.PrepayAmount(double.NaN, 100), 0);
            Assert.Near(0.0, AmountSteps.PrepayAmount(-5.0, 100), 0);
        }

        [Test]
        public void PrepayAmount_NeverExceedsTheDebt_AndStepsOnToTheWhole()
        {
            double previous = 0.0;
            for (int percent = AmountSteps.PrepayMinPercent; percent <= AmountSteps.MaxPercent; percent += AmountSteps.PercentStep)
            {
                double sum = AmountSteps.PrepayAmount(6527277.0, percent);
                Assert.True(sum >= previous && sum <= 6527277.0, percent + "%: " + sum);
                previous = sum;
            }
            Assert.Near(6527277.0, previous, 0);
            Assert.Equal(0, (AmountSteps.MaxPercent - AmountSteps.PrepayMinPercent) % AmountSteps.PercentStep, "the top of the slider is on a step");
            Assert.True(AmountSteps.PrepayMinPercent >= 1 && AmountSteps.PrepayMinPercent < AmountSteps.MaxPercent);
        }

        [Test]
        public void ClampPercent_HoldsAShareBetweenTheLowestAndTheWhole()
        {
            Assert.Equal(25, AmountSteps.ClampPercent(10, 25));
            Assert.Equal(60, AmountSteps.ClampPercent(60, 25));
            Assert.Equal(100, AmountSteps.ClampPercent(180, 25));
            Assert.Equal(0, AmountSteps.ClampPercent(-3, 0));
            Assert.Equal(100, AmountSteps.ClampPercent(40, 140), "a lowest share above the whole is the whole");
        }

        [Test]
        public void RepayLimit_IsWhatIsOwed_ButNotMoreThanTheBalanceHolds()
        {
            Assert.Near(2660000.0, AmountSteps.RepayLimit(2660000.0, 10000000.0), 0);
            Assert.Near(1500000.0, AmountSteps.RepayLimit(2660000.0, 1500000.0), 0, "only what the balance holds");
            Assert.Near(0.0, AmountSteps.RepayLimit(2660000.0, 0.0), 0);
            Assert.Near(0.0, AmountSteps.RepayLimit(2660000.0, -500000.0), 0, "a balance below zero holds nothing");
            Assert.Near(0.0, AmountSteps.RepayLimit(0.0, 10000000.0), 0, "nothing is owed");
            Assert.Near(0.0, AmountSteps.RepayLimit(double.NaN, 10000000.0), 0);
            Assert.Near(0.0, AmountSteps.RepayLimit(100.0, double.NaN), 0);
        }
    }
}
