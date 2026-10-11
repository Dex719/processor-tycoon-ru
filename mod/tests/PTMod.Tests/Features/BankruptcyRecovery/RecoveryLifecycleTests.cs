using System;
using System.Collections.Generic;
using PTMod.Features.BankruptcyRecovery.Logic;

namespace PTMod.Tests.Features.BankruptcyRecovery
{
    /// <summary>
    /// TSK-008, TSK-009, TSK-011 (NFR-3, FR-12): the engines over a whole life, month after month, in the order the feature subscribes them (the loans, the credit line,
    /// the investor). The debts add up, nothing is left behind when everything is paid, the journal stays a ring, a save written in the middle of the life and read back
    /// gives the same life, and everything the engines write passes the checks the save applies when it is read.
    /// </summary>
    [Covers(typeof(LoanBook), typeof(CreditLineEngine), typeof(BridgeLoanEngine), typeof(BailoutEngine))]
    public class RecoveryLifecycleTests
    {
        private sealed class World
        {
            public readonly RecoveryRig Rig = new RecoveryRig();
            public readonly LoanBook Loans;
            public readonly CreditLineEngine Line;
            public readonly BridgeLoanEngine Bridge;
            public readonly BailoutEngine Bailout;

            public World()
            {
                Loans = new LoanBook(Rig.Env);
                Line = new CreditLineEngine(Rig.Env, Loans);
                Bridge = new BridgeLoanEngine(Rig.Env, Loans);
                Bailout = new BailoutEngine(Rig.Env, Loans);
            }

            public FakePlayerFinance Finance
            {
                get { return Rig.Finance; }
            }

            // The first of the next month; the month's operating result lands in the balance first, then the handlers of LateMonthPassed run in the order of the feature.
            public void NextMonth(double operatingResult)
            {
                DateTime today = Finance.TodayValue;
                Finance.TodayValue = new DateTime(today.Year, today.Month, 1).AddMonths(1);
                Finance.MoneyValue += operatingResult;
                Loans.OnMonth();
                Line.OnMonth();
                Bailout.OnMonth();
            }

            // The sum of the journal amounts of some operation codes.
            public double Sum(params string[] ops)
            {
                double sum = 0.0;
                foreach (string op in ops)
                {
                    foreach (JournalEntryData entry in Rig.Entries(op)) sum += entry.amount;
                }
                return sum;
            }
        }

        // ---- the credit line, from the opening to the last payment of the term loan ----

        [Test]
        public void TheCreditLine_LivesItsThreeYears_ThenATermLoanIsPaidOff_AndTheDebtAddsUp()
        {
            var w = new World();
            Assert.True(w.Line.EnsureOpened());
            w.Finance.MoneyValue = 0.0;
            Assert.Near(2000000.0, w.Line.Draw(2000000.0), 0);

            for (int month = 1; month <= 36; month++) w.NextMonth(100000.0);   // the line expires on 1978-01-01, the 36th first of the month

            Assert.Equal(CreditLineData.StateClosed, w.Rig.State.creditLine.state);
            Assert.Near(0.0, w.Rig.State.creditLine.drawn, 0);
            for (int month = 37; month <= 96; month++) w.NextMonth(100000.0);

            Assert.Equal(0, w.Loans.Loans.Count, "the term loan is paid off");
            Assert.Near(0.0, w.Loans.Outstanding, 0);

            // what was drawn came back to the cent: the draw, the repayments out of the balance and the principal parts of the term loan
            double drawn = w.Sum("line.draw", "line.autodraw");
            double paidBack = -w.Sum("line.sweep", "line.repay", "loan.principal");
            Assert.Near(2000000.0, drawn, 0);
            Assert.Near(drawn, paidBack, 0.01, "the debt of the line was paid back in full");
            Assert.True(w.Sum("line.interest", "loan.interest") < 0.0, "and it cost interest");
            Assert.True(w.Rig.State.journal.Count <= RecoveryJournal.Capacity);
        }

        [Test]
        public void AnAutoDrawKeepsAShortOfMoneyCompanyAboveTheThreshold_ForYears()
        {
            var w = new World();
            w.Finance.MoneyValue = 0.0;
            w.Line.EnsureOpened();

            // every month costs 600 000 more than it brings; the game would start a default on the day the balance passes -8 400 000
            double lowest = 0.0;
            for (int month = 1; month <= 24; month++)
            {
                for (int day = 0; day < 30; day++)
                {
                    w.Finance.MoneyValue -= 600000.0 / 30.0;
                    w.Line.OnDay();   // LateTick comes before the game's check on DayPassed
                    lowest = Math.Min(lowest, w.Finance.MoneyValue);
                    Assert.True(w.Finance.MoneyValue >= -w.Finance.DebtLimit, "month " + month + " day " + day + ": the balance fell through the threshold");
                }
                w.NextMonth(0.0);
            }

            Assert.True(lowest < 0.0);
            Assert.True(w.Rig.State.creditLine.drawn > 0.0, "the line carried the company");
            Assert.True(w.Rig.State.creditLine.drawn <= w.Line.Limit() + 0.01, "within its limit");
        }

        // ---- a save written in the middle of the life ----

        private static string Describe(World w)
        {
            var text = new System.Text.StringBuilder();
            text.Append(w.Finance.MoneyValue.ToString("R", System.Globalization.CultureInfo.InvariantCulture)).Append('|');
            foreach (LoanData loan in w.Loans.Loans)
                text.Append(loan.id).Append(':').Append(loan.outstanding.ToString("R", System.Globalization.CultureInfo.InvariantCulture)).Append(':').Append(loan.monthsLeft).Append(';');
            text.Append('|').Append(w.Rig.State.creditLine.state).Append(':').Append(w.Rig.State.creditLine.drawn.ToString("R", System.Globalization.CultureInfo.InvariantCulture));
            text.Append('|').Append(w.Rig.State.investorShare.paid.ToString("R", System.Globalization.CultureInfo.InvariantCulture)).Append(':').Append(w.Rig.State.investorShare.monthsLeft);
            text.Append('|').Append(w.Rig.State.journal.Count).Append('|').Append(w.Rig.State.lastBridgeClosedOn);
            return text.ToString();
        }

        private static string Live(bool saveAndLoadInTheMiddle)
        {
            var w = new World();
            w.Finance.TodayValue = new DateTime(1976, 3, 1);
            w.Finance.Foundation = new DateTime(1975, 1, 1);
            w.Finance.MoneyValue = -7000000.0;
            w.Line.EnsureOpened();
            w.Line.Draw(1500000.0);
            LoanData bridge;
            w.Bridge.Take(3000000.0, out bridge);

            // the rescue with the investor share in the same life
            w.Finance.DebtLimit = 24300000.0;
            w.Finance.MoneyValue = -30000000.0;
            w.Finance.InDefault = true;
            w.Finance.Days = 119;
            Assert.True(w.Bailout.Accept(BailoutChoice.InvestorShare));
            w.Finance.MoneyValue = 2000000.0;
            w.Finance.DebtLimit = 8400000.0;

            for (int month = 1; month <= 40; month++)
            {
                w.Finance.SetProfit(w.Finance.TodayValue.Year, w.Finance.TodayValue.Month, month % 4 == 0 ? -500000.0 : 1500000.0);
                w.NextMonth(month % 3 == 0 ? 400000.0 : 250000.0);

                if (saveAndLoadInTheMiddle && month == 17)
                {
                    var problems = new List<string>();
                    RecoveryState reread = RecoveryStateCodec.Read(RecoveryStateCodec.Write(w.Rig.State), problems);
                    Assert.Equal(0, problems.Count, string.Join("; ", problems));
                    w.Rig.State = reread;   // a load: the state is a new object
                    w.Rig.Env.ResetCache();
                }
            }
            Assert.True(w.Rig.State.investorShare.paid > 0.0, "the investor was paid in the profitable months");
            Assert.True(w.Rig.State.journal.Count > 20, "a life with many records");
            return Describe(w);
        }

        [Test]
        public void ASaveInTheMiddleOfTheLife_GivesTheSameLife()
        {
            string straight = Live(false);
            string withASave = Live(true);

            Assert.Equal(straight, withASave);
        }

        // ---- what the engines write passes the checks of the save ----

        [Test]
        public void EverythingTheEnginesWrite_PassesTheChecksOfTheSave()
        {
            var w = new World();
            w.Finance.TodayValue = new DateTime(1976, 3, 1);
            w.Finance.MoneyValue = -7000000.0;
            w.Line.EnsureOpened();
            LoanData bridge;
            Assert.Equal(BridgeTakeResult.Done, w.Bridge.Take(8400000.0, out bridge));
            w.Line.Draw(2660000.0);
            w.Loans.Open(LoanData.KindTerm, 1000000.0, 0.15, 24);

            w.Finance.DebtLimit = 24300000.0;
            w.Finance.MoneyValue = -30000000.0;
            w.Finance.InDefault = true;
            Assert.True(w.Bailout.Accept(BailoutChoice.InvestorShare));
            w.Finance.InDefault = true;
            w.Finance.MoneyValue = -30000000.0;   // in need again
            Assert.True(w.Bailout.Accept(BailoutChoice.RescueLoan));
            for (int month = 1; month <= 5; month++) w.NextMonth(200000.0);
            w.Finance.MoneyValue = 50000000.0;
            Assert.Equal(PrepayResult.Done, w.Loans.Prepay("term-2", 100000.0));

            var problems = new List<string>();
            RecoveryState copy = RecoveryStateCodec.Read(RecoveryStateCodec.Write(w.Rig.State), problems);

            Assert.Equal(0, problems.Count, string.Join("; ", problems));
            Assert.Equal(w.Rig.State.loans.Count, copy.loans.Count);
            Assert.Equal(w.Rig.State.journal.Count, copy.journal.Count);
            Assert.True(copy.investorShare.active);
            Assert.Equal(CreditLineData.StateOpen, copy.creditLine.state);
            Assert.Equal(2, copy.bailoutsUsed);
            Assert.True(copy.nextLoanSeq > copy.loans.Count);
        }

        [Test]
        public void TheJournal_NeverGrowsPastTheRing_InALongLife()
        {
            var w = new World();
            w.Finance.TodayValue = new DateTime(1976, 3, 1);
            w.Finance.MoneyValue = 500000000.0;
            for (int loan = 0; loan < 8; loan++) w.Loans.Open(LoanData.KindTerm, 1000000.0, 0.15, 120);

            for (int month = 1; month <= 60; month++) w.NextMonth(0.0);   // 8 loans x 2 records x 60 months

            Assert.Equal(200, w.Rig.State.journal.Count);
            Assert.Equal(200, w.Rig.Core.Journal.Count);
        }
    }
}
