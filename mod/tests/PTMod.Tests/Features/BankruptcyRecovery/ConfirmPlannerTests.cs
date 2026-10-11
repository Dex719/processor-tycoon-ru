using System;
using PTMod.Features.BankruptcyRecovery.Logic;

namespace PTMod.Tests.Features.BankruptcyRecovery
{
    /// <summary>
    /// TSK-013, TSK-014 (FR-2 item 4, FR-3, FR-4, FR-6, FR-7, FR-10): the second step of every operation of the rescue menu, the question of the core's <c>ModUI.Confirm</c>.
    /// It names the operation, its sum and its side effects and ends with the balance after it; the balance after is the number of the examples of the requirements
    /// (AC-6.1, AC-10.2). There is no dialog of the feature's own: the model only writes the text.
    /// </summary>
    [Covers(typeof(ConfirmPlanner), typeof(Confirmation))]
    public class ConfirmPlannerTests
    {
        private static readonly IMenuText Text = new FakeMenuText();

        private static ConfirmPlanner Planner(double balance, double debtLimit = 8400000.0)
        {
            return new ConfirmPlanner(Text, balance, debtLimit);
        }

        private static string[] Lines(Confirmation c)
        {
            return c.Message.Split('\n');
        }

        [Test]
        public void EveryQuestion_EndsWithTheBalanceAfter_AndHasTheCaption()
        {
            Confirmation c = Planner(1000000.0).SellStock("X-100", 10000, 290000.0);

            string[] lines = Lines(c);
            Assert.Equal("rec.confirm.title", c.Title);
            Assert.Equal("rec.confirm.after($1290000.00)", lines[lines.Length - 1], "the last line is the balance after, in full");
            Assert.Near(1290000.0, c.BalanceAfter, 1e-9);
            Assert.False(c.BelowThreshold);
        }

        [Test]
        public void SellLines_ShowsTheSum_TheCapacityBeforeAndAfter_AC_3_1()
        {
            // 20 lines on 76 mm, five of them sold
            Confirmation c = Planner(-3000000.0).SellLines("76 mm", 5, 4250000.0, 20, 15, false, 0);

            string[] lines = Lines(c);
            Assert.Equal("rec.lines.sell(5|76 mm|$4250000)", lines[0]);
            Assert.Equal("rec.confirm.capacity(20|15)", lines[1]);
            Assert.Equal("rec.confirm.after($1250000.00)", lines[2], "-3 000 000 + 4 250 000");
            Assert.Equal(3, lines.Length, "no warning without a risk for the contracts");
            Assert.Near(1250000.0, c.BalanceAfter, 1e-9);
        }

        [Test]
        public void SellLines_WarnsWhenTheContractsNeedMoreLinesThanAreLeft_AC_3_3()
        {
            // 18 lines are under contracts, 20 lines, five sold
            Confirmation c = Planner(5000000.0).SellLines("76 mm", 5, 4250000.0, 20, 15, true, 18);

            string[] lines = Lines(c);
            Assert.Equal("rec.lines.contractsWarning", lines[2]);
            Assert.Equal("rec.confirm.contracts(18)", lines[3]);
            Assert.Equal("rec.confirm.after($9250000.00)", lines[4]);
        }

        [Test]
        public void SellStock_TheExample_AC_4_1()
        {
            // 10 000 units at 29 dollars
            Confirmation c = Planner(0.0).SellStock("X-100", 10000, 290000.0);

            Assert.Equal("rec.confirm.stock(X-100|10000|$290000)", Lines(c)[0]);
            Assert.Near(290000.0, c.BalanceAfter, 1e-9);
        }

        [Test]
        public void Retire_ShowsTheFineAndTheIncomeOfTheStock_AC_4_2()
        {
            // a contract with a fine of 1.2 million and a stock worth 290 thousand
            Confirmation c = Planner(2000000.0).Retire("X-200", 1200000.0, 290000.0);

            string[] lines = Lines(c);
            Assert.Equal("rec.confirm.retire(X-200)", lines[0]);
            Assert.Equal("rec.confirm.retire.fine($1200000)", lines[1]);
            Assert.Equal("rec.confirm.retire.stock($290000)", lines[2]);
            Assert.Equal("rec.confirm.retire.note", lines[3]);
            Assert.Near(2000000.0 + 290000.0 - 1200000.0, c.BalanceAfter, 1e-9, "the income of the stock minus the fine");
        }

        [Test]
        public void Retire_WithoutAFineOrStock_NamesNeitherLine()
        {
            Confirmation c = Planner(2000000.0).Retire("X-200", 0.0, 0.0);

            string text = c.Message;
            Assert.False(text.Contains("rec.confirm.retire.fine"));
            Assert.False(text.Contains("rec.confirm.retire.stock"));
            Assert.Near(2000000.0, c.BalanceAfter, 1e-9);
        }

        [Test]
        public void Bridge_TheBalanceAfterHasTheAmountAndTheFee_AC_6_1()
        {
            // D = 8 400 000, the whole limit, the balance -7 000 000: -7 000 000 + 8 400 000 - 420 000 = 980 000
            Confirmation c = Planner(-7000000.0).Bridge(8400000.0, 420000.0, 12, 0.30, 818892.0);

            string[] lines = Lines(c);
            Assert.Equal("rec.bridge.terms($8400000|12|30|$818892)", lines[0]);
            Assert.Equal("rec.confirm.fee($420000)", lines[1]);
            Assert.Equal("rec.confirm.after($980000.00)", lines[2]);
            Assert.Near(980000.0, c.BalanceAfter, 1e-9);
            Assert.False(c.BelowThreshold);
        }

        [Test]
        public void Draw_AddsTheAmount_AndNamesTheRate()
        {
            Confirmation c = Planner(-7700000.0).Draw(2660000.0, 0.10);

            string[] lines = Lines(c);
            Assert.Equal("rec.confirm.draw($2660000)", lines[0]);
            Assert.Equal("rec.confirm.draw.rate(10)", lines[1]);
            Assert.Near(-5040000.0, c.BalanceAfter, 1e-9, "AC-7.3: the balance after the draw");
        }

        [Test]
        public void RepayLine_TakesTheAmountFromTheBalance()
        {
            Confirmation c = Planner(4000000.0).RepayLine(1500000.0);

            Assert.Equal("rec.confirm.repay($1500000)", Lines(c)[0]);
            Assert.Near(2500000.0, c.BalanceAfter, 1e-9);
        }

        [Test]
        public void Prepay_TheExample_AC_10_2()
        {
            // the balance 20 million, the bridge loan owes 5 million: 15 million is left
            Confirmation c = Planner(20000000.0).Prepay("Bridge loan", 5000000.0, 0.0);

            string[] lines = Lines(c);
            Assert.Equal("rec.confirm.prepay(Bridge loan|$5000000)", lines[0]);
            Assert.Equal("rec.confirm.prepay.left($0)", lines[1]);
            Assert.Near(15000000.0, c.BalanceAfter, 1e-9);
        }

        [Test]
        public void APartialPayment_ShowsWhatIsStillOwed()
        {
            Confirmation c = Planner(20000000.0).Prepay("Term loan", 2000000.0, 3000000.0);
            Assert.Equal("rec.confirm.prepay.left($3000000)", Lines(c)[1]);

            Confirmation negative = Planner(20000000.0).Prepay("Term loan", 2000000.0, -5.0);
            Assert.Equal("rec.confirm.prepay.left($0)", Lines(negative)[1], "a debt below zero is shown as nothing owed");
        }

        [Test]
        public void ABalanceBelowTheThreshold_IsSaidSoInTheQuestion()
        {
            // the balance 1 000 000, the threshold -8 400 000: paying back 10 million leaves -9 000 000
            Confirmation c = Planner(1000000.0, 8400000.0).RepayLine(10000000.0);

            string[] lines = Lines(c);
            Assert.True(c.BelowThreshold);
            Assert.Near(-9000000.0, c.BalanceAfter, 1e-9);
            Assert.Equal("rec.confirm.belowThreshold", lines[lines.Length - 1]);
        }

        [Test]
        public void TheWarning_IsLeftOut_WhenTheThresholdIsUnknown_OrNotCrossed()
        {
            Confirmation unknown = Planner(1000000.0, 0.0).RepayLine(10000000.0);
            Assert.False(unknown.BelowThreshold, "no threshold, no warning");
            Assert.False(unknown.Message.Contains("rec.confirm.belowThreshold"));

            Confirmation fine = Planner(20000000.0, 8400000.0).RepayLine(10000000.0);
            Assert.False(fine.BelowThreshold);

            Confirmation onTheLine = Planner(0.0, 8400000.0).RepayLine(8400000.0);
            Assert.False(onTheLine.BelowThreshold, "exactly at the threshold is not below it (the game's default needs a balance below)");
        }
    }
}
