using System;
using System.Collections.Generic;
using PTMod.Features.BankruptcyRecovery.Logic;

namespace PTMod.Tests.Features.BankruptcyRecovery
{
    /// <summary>
    /// TSK-008 (FR-6, FR-12): the bridge loan. Who may take it (the balance below -0.75 D or a default, no loan running, the cooldown over, the part on, the
    /// company not bankrupt), how much (<c>β × D_L + γ × R_3</c>), and what taking it does: the amount is credited outside the report, the fee is an expense of the
    /// report, an annuity loan of 12 months is registered. The numbers are the examples of the requirements (AC-6.1).
    /// </summary>
    [Covers(typeof(BridgeLoanEngine), typeof(BridgeTakeResult))]
    public class BridgeLoanEngineTests
    {
        private sealed class Setup
        {
            public readonly RecoveryRig Rig = new RecoveryRig();
            public readonly LoanBook Loans;
            public readonly BridgeLoanEngine Bridge;

            public Setup()
            {
                Loans = new LoanBook(Rig.Env);
                Bridge = new BridgeLoanEngine(Rig.Env, Loans);
                Rig.Finance.TodayValue = new DateTime(1976, 3, 1);
                Rig.Finance.MoneyValue = -7000000.0;   // below -0.75 x 8 400 000 = -6 300 000
            }

            public FakePlayerFinance Finance
            {
                get { return Rig.Finance; }
            }
        }

        // ---- taking the loan ----

        [Test]
        public void Take_TheWholeLimit_AC_6_1()
        {
            var s = new Setup();
            Assert.Near(8400000.0, s.Bridge.MaxAmount(), 1e-6);

            LoanData loan;
            BridgeTakeResult result = s.Bridge.Take(8400000.0, out loan);

            Assert.Equal(BridgeTakeResult.Done, result);
            Assert.Near(-7000000.0 + 8400000.0 - 420000.0, s.Finance.MoneyValue, 1e-6, "AC-6.1: +980 000");
            Assert.NotNull(loan);
            Assert.Equal("bridge-1", loan.id);
            Assert.Equal(LoanData.KindBridge, loan.kind);
            Assert.Near(8400000.0, loan.principal, 0);
            Assert.Equal(12, loan.monthsTotal);
            Assert.Near(0.30, loan.annualRate, 0);
            Assert.Near(818892.0, loan.payment, 1.0);
            RecoveryAssert.Same(loan, s.Loans.Find("bridge-1"));

            // the amount is capital, the fee is an expense of the report in the item "Interest"
            Assert.Equal(1, s.Finance.Added.Count);
            Assert.Near(8400000.0, s.Finance.Added[0], 0);
            Assert.Equal(1, s.Finance.Spent.Count);
            Assert.Near(420000.0, s.Finance.Spent[0].Key, 1e-6);
            Assert.Equal(FakePlayerFinance.InterestItem, s.Finance.Spent[0].Value);

            Assert.Equal(2, s.Rig.State.journal.Count);
            Assert.Equal("bridge.open", s.Rig.State.journal[0].op);
            Assert.Near(8400000.0, s.Rig.State.journal[0].amount, 0);
            Assert.Equal("12m @ 30%", s.Rig.State.journal[0].details);
            Assert.Equal("bridge.fee", s.Rig.State.journal[1].op);
            Assert.Near(-420000.0, s.Rig.State.journal[1].amount, 1e-6);
        }

        [Test]
        public void Take_APart_PaysTheFeeOnThatPart()
        {
            var s = new Setup();
            LoanData loan;

            Assert.Equal(BridgeTakeResult.Done, s.Bridge.Take(0.25 * s.Bridge.MaxAmount(), out loan));

            Assert.Near(2100000.0, loan.principal, 1e-6);
            Assert.Near(105000.0, s.Finance.Spent[0].Key, 1e-6);
            Assert.Near(-7000000.0 + 2100000.0 - 105000.0, s.Finance.MoneyValue, 1e-6);
        }

        [Test]
        public void Take_RefusesWhatItMust_AndMovesNothing()
        {
            var s = new Setup();
            LoanData loan;
            double before = s.Finance.MoneyValue;

            Assert.Equal(BridgeTakeResult.InvalidAmount, s.Bridge.Take(0.0, out loan));
            Assert.Equal(BridgeTakeResult.InvalidAmount, s.Bridge.Take(-5.0, out loan));
            Assert.Equal(BridgeTakeResult.InvalidAmount, s.Bridge.Take(double.NaN, out loan));
            Assert.Equal(BridgeTakeResult.InvalidAmount, s.Bridge.Take(double.PositiveInfinity, out loan));
            Assert.Equal(BridgeTakeResult.TooMuch, s.Bridge.Take(8400001.0, out loan));
            Assert.Null(loan);

            s.Rig.Money.Ledger = false;
            Assert.Equal(BridgeTakeResult.Unavailable, s.Bridge.Take(1000000.0, out loan));
            s.Rig.Money.Ledger = true;
            s.Finance.Available = false;
            Assert.Equal(BridgeTakeResult.Unavailable, s.Bridge.Take(1000000.0, out loan));

            Assert.Near(before, s.Finance.MoneyValue, 0);
            Assert.Equal(0, s.Loans.Loans.Count);
            Assert.Equal(0, s.Rig.State.journal.Count);
            Assert.Equal(1, s.Rig.State.nextLoanSeq);
        }

        [Test]
        public void Take_ASecondLoanIsRefusedWhileOneIsRunning_AC_6_2()
        {
            var s = new Setup();
            LoanData loan;
            Assert.Equal(BridgeTakeResult.Done, s.Bridge.Take(1000000.0, out loan));

            s.Finance.MoneyValue = -7000000.0;
            Eligibility check = s.Bridge.Check();
            Assert.False(check.Ok);
            Assert.Equal(RecoveryRules.ReasonActive, check.ReasonKey);
            Assert.Equal(BridgeTakeResult.NotEligible, s.Bridge.Take(1000000.0, out loan));
            Assert.Equal(1, s.Loans.Loans.Count);
        }

        // ---- the amount ----

        [Test]
        public void MaxAmount_AddsAQuarterOfTheRevenueOfTheLastThreeMonths()
        {
            var s = new Setup();
            s.Finance.SetSales(1976, 2, 2000000.0);
            s.Finance.SetSales(1976, 1, 1000000.0);
            s.Finance.SetSales(1975, 12, 1000000.0);

            Assert.Near(8400000.0 + 0.25 * 4000000.0, s.Bridge.MaxAmount(), 1e-6);
        }

        [Test]
        public void MaxAmount_OnTheFloorOfTheBase()
        {
            var s = new Setup();
            s.Finance.DebtLimit = 1000000.0;
            s.Finance.InflationValue = 2.0;

            Assert.Near(4000000.0, s.Bridge.MaxAmount(), 1e-6, "D_L = max(1 000 000, 2 000 000 x 2.0)");
        }

        [Test]
        public void MaxAmount_IsZeroWithoutTheCompany()
        {
            var s = new Setup();
            s.Finance.Available = false;
            Assert.Near(0.0, s.Bridge.MaxAmount(), 0);
        }

        [Test]
        public void TheTerms_FeePaymentAndRateFollowTheBalanceFile_AndTheMultiplier()
        {
            var s = new Setup();
            Assert.Near(420000.0, s.Bridge.Fee(8400000.0), 1e-6);
            Assert.Near(0.0, s.Bridge.Fee(-1.0), 0);
            Assert.Near(818892.0, s.Bridge.Payment(8400000.0), 1.0);
            Assert.Near(0.30, s.Bridge.EffectiveRate(), 1e-12);

            s.Finance.Multiplier = 1.5;
            Assert.Near(0.45, s.Bridge.EffectiveRate(), 1e-12);
            Assert.Near(882103.0, s.Bridge.Payment(8400000.0), 1.0);

            s.Rig.Balance.bridgeLoan.fee = 0.1;
            Assert.Near(840000.0, s.Bridge.Fee(8400000.0), 1e-6);
        }

        // ---- who may take it ----

        [Test]
        public void Check_AYesNeedsTheBalanceBelowThreeQuartersOfTheThreshold_OrADefault()
        {
            var s = new Setup();
            Assert.True(s.Bridge.Check().Ok);

            s.Finance.MoneyValue = -6300000.0;   // exactly -0.75 x D: not below
            Eligibility far = s.Bridge.Check();
            Assert.False(far.Ok);
            Assert.Equal(RecoveryRules.ReasonNotNeeded, far.ReasonKey);

            s.Finance.MoneyValue = -1000000.0;
            s.Finance.InDefault = true;   // a default is enough, whatever the balance
            Assert.True(s.Bridge.Check().Ok);
        }

        [Test]
        public void Check_TheCooldownCountsFromThePayoff_180Days()
        {
            var s = new Setup();
            s.Rig.State.lastBridgeClosedOn = "1976-03-01";

            s.Finance.TodayValue = new DateTime(1976, 6, 9);   // 100 days later
            Eligibility wait = s.Bridge.Check();
            Assert.False(wait.Ok);
            Assert.Equal(RecoveryRules.ReasonCooldown, wait.ReasonKey);
            Assert.Equal(80, wait.CooldownDaysLeft);

            s.Finance.TodayValue = new DateTime(1976, 8, 27);  // 179 days
            Assert.False(s.Bridge.Check().Ok);

            s.Finance.TodayValue = new DateTime(1976, 8, 28);  // 180 days
            Assert.True(s.Bridge.Check().Ok);
        }

        [Test]
        public void Check_ADamagedDateOfThePayoffIsNoCooldown()
        {
            var s = new Setup();
            s.Rig.State.lastBridgeClosedOn = "not a date";
            Assert.True(s.Bridge.Check().Ok);
        }

        [Test]
        public void Check_ThePartBeingOff_RefusesNewLoans_ButTheOldOnesKeepPaying_AC_12_4()
        {
            var s = new Setup();
            LoanData loan;
            Assert.Equal(BridgeTakeResult.Done, s.Bridge.Take(8400000.0, out loan));
            s.Rig.Off.Add(SubFeature.BridgeLoan);
            s.Finance.MoneyValue = 5000000.0;

            Eligibility off = s.Bridge.Check();
            Assert.False(off.Ok);
            Assert.Equal(BridgeLoanEngine.ReasonOff, off.ReasonKey);

            s.Finance.TodayValue = new DateTime(1976, 4, 1);
            s.Loans.OnMonth();   // the first of the month: the payment goes by the schedule
            Assert.Near(5000000.0 - 818892.0, s.Finance.MoneyValue, 1.0);
            Assert.Equal(11, loan.monthsLeft);

            Assert.Equal(BridgeTakeResult.NotEligible, s.Bridge.Take(1000.0, out loan));
        }

        [Test]
        public void Check_ABankruptCompanyGetsNoLoan()
        {
            var s = new Setup();
            s.Finance.Bankrupt = true;
            Eligibility check = s.Bridge.Check();
            Assert.False(check.Ok);
            Assert.Equal(RecoveryRules.ReasonBankrupt, check.ReasonKey);

            LoanData loan;
            Assert.Equal(BridgeTakeResult.NotEligible, s.Bridge.Take(1000000.0, out loan));
        }

        [Test]
        public void Check_WithoutTheCompany_IsNo()
        {
            var s = new Setup();
            s.Finance.Available = false;
            Assert.False(s.Bridge.Check().Ok);
        }

        // ---- the whole life of a loan ----

        [Test]
        public void ALoanPaidOff_StartsTheCooldown_ThenAnotherCanBeTaken()
        {
            var s = new Setup();
            LoanData loan;
            Assert.Equal(BridgeTakeResult.Done, s.Bridge.Take(2000000.0, out loan));
            s.Finance.MoneyValue = 100000000.0;

            for (int month = 0; month < 12; month++)
            {
                s.Finance.TodayValue = s.Finance.TodayValue.AddMonths(1);
                s.Loans.OnMonth();
            }
            Assert.Equal(0, s.Loans.Loans.Count);
            Assert.Equal("1977-03-01", s.Rig.State.lastBridgeClosedOn);

            s.Finance.MoneyValue = -7000000.0;   // in need again
            s.Finance.TodayValue = new DateTime(1977, 6, 1);   // 92 days after the payoff
            Assert.Equal(RecoveryRules.ReasonCooldown, s.Bridge.Check().ReasonKey);

            s.Finance.TodayValue = new DateTime(1977, 8, 28);  // 180 days
            Assert.True(s.Bridge.Check().Ok);
            Assert.Equal(BridgeTakeResult.Done, s.Bridge.Take(1000000.0, out loan));
            Assert.Equal("bridge-2", loan.id);
        }
    }
}
