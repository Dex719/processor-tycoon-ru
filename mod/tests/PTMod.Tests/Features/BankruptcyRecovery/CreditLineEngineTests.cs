using System;
using System.Collections.Generic;
using PTMod.Features.BankruptcyRecovery.Logic;

namespace PTMod.Tests.Features.BankruptcyRecovery
{
    /// <summary>
    /// TSK-009 (FR-7, FR-12): the startup credit line. It opens for the first years of the company (on a new game, on an old save for the rest of the term),
    /// the limit follows the difficulty and the sales, the auto draw lends before the game's bankruptcy check, the interest is one line of the report, a part of a
    /// positive balance pays the debt back, and at the end of the term what is owed becomes a term loan. A line that is closed never opens again, and a part
    /// that is switched off stops new draws but not the service of what was drawn.
    /// </summary>
    [Covers(typeof(CreditLineEngine))]
    public class CreditLineEngineTests
    {
        private sealed class Setup
        {
            public readonly RecoveryRig Rig = new RecoveryRig();
            public readonly LoanBook Loans;
            public readonly CreditLineEngine Line;
            public readonly List<double> AutoDraws = new List<double>();
            public readonly List<LoanData> Converted = new List<LoanData>();

            public Setup()
            {
                Loans = new LoanBook(Rig.Env);
                Line = new CreditLineEngine(Rig.Env, Loans);
                Line.AutoDrawn += AutoDraws.Add;
                Line.Converted += Converted.Add;
            }

            public FakePlayerFinance Finance
            {
                get { return Rig.Finance; }
            }

            public CreditLineData Data
            {
                get { return Rig.State.creditLine; }
            }

            // The 1975 player of the examples: a new game on the first of January, D = 8.4 million, normal difficulty, the line open.
            public static Setup Opened()
            {
                var s = new Setup();
                Assert.True(s.Line.EnsureOpened());
                return s;
            }
        }

        // ---- opening ----

        [Test]
        public void EnsureOpened_OnANewGame_AC_7_1()
        {
            var s = new Setup();

            Assert.True(s.Line.EnsureOpened());

            Assert.Equal(CreditLineData.StateOpen, s.Data.state);
            Assert.Equal("1975-01-01", s.Data.opensOn);
            Assert.Equal("1978-01-01", s.Data.closesOn, "three years from the foundation of the company");
            Assert.Near(0.0, s.Data.drawn, 0);
            Assert.True(s.Line.IsOpen);
            Assert.Near(8400000.0, s.Line.Limit(), 1e-6, "D = 8 400 000, normal difficulty, no sales");
            Assert.Near(8400000.0, s.Line.Available(), 1e-6);
            Assert.Near(0.0, s.Line.Drawn, 0);

            List<JournalEntryData> opened = s.Rig.Entries("line.open");
            Assert.Equal(1, opened.Count);
            Assert.Equal("until 1978-01-01", opened[0].details);
            Assert.Near(0.0, opened[0].amount, 0);
        }

        [Test]
        public void EnsureOpened_TwiceOpensOnce()
        {
            var s = Setup.Opened();
            s.Data.drawn = 1000.0;

            Assert.False(s.Line.EnsureOpened(), "an open line is left alone");

            Assert.Near(1000.0, s.Data.drawn, 0);
            Assert.Equal(1, s.Rig.Entries("line.open").Count);
        }

        [Test]
        public void EnsureOpened_AnOldSave_OpensForTheRestOfTheTerm()
        {
            var s = new Setup();
            s.Finance.TodayValue = new DateTime(1976, 5, 1);

            Assert.True(s.Line.EnsureOpened());

            Assert.Equal("1976-05-01", s.Data.opensOn);
            Assert.Equal("1978-01-01", s.Data.closesOn, "FR-7 item 8: the term still ends three years after the foundation");
        }

        [Test]
        public void EnsureOpened_TheTermWindowIsHalfOpen()
        {
            var s = new Setup();
            s.Finance.TodayValue = new DateTime(1977, 12, 31);
            Assert.True(s.Line.EnsureOpened(), "the last day of the window");

            var late = new Setup();
            late.Finance.TodayValue = new DateTime(1978, 1, 1);
            Assert.False(late.Line.EnsureOpened(), "foundation + 3 years: too late");
            Assert.Equal(CreditLineData.StateNone, late.Data.state);

            var far = new Setup();
            far.Finance.TodayValue = new DateTime(1990, 6, 1);
            Assert.False(far.Line.EnsureOpened());
        }

        [Test]
        public void EnsureOpened_NeverOpensAClosedLine_AC_12_6()
        {
            var s = new Setup();
            s.Data.state = CreditLineData.StateClosed;
            s.Data.closedReason = CreditLineData.ReasonDisabled;

            Assert.False(s.Line.EnsureOpened());

            Assert.Equal(CreditLineData.StateClosed, s.Data.state);
            Assert.False(s.Line.IsOpen);
        }

        [Test]
        public void EnsureOpened_NeedsThePartTheCompanyAndAKnownFoundation()
        {
            var off = new Setup();
            off.Rig.Off.Add(SubFeature.StartupCredit);
            Assert.False(off.Line.EnsureOpened(), "the part is off: no new line");
            Assert.Equal(CreditLineData.StateNone, off.Data.state);

            var broke = new Setup();
            broke.Finance.Bankrupt = true;
            Assert.False(broke.Line.EnsureOpened());

            var away = new Setup();
            away.Finance.Available = false;
            Assert.False(away.Line.EnsureOpened());

            var unknown = new Setup();
            unknown.Finance.Foundation = default(DateTime);
            Assert.False(unknown.Line.EnsureOpened(), "a foundation date that is unknown is not guessed");
        }

        [Test]
        public void EnsureOpened_TurningThePartOnLaterOpensIt()
        {
            var s = new Setup();
            s.Rig.Off.Add(SubFeature.StartupCredit);
            Assert.False(s.Line.EnsureOpened());

            s.Rig.Off.Remove(SubFeature.StartupCredit);
            Assert.True(s.Line.EnsureOpened());
        }

        // ---- the limit ----

        [Test]
        public void Limit_GrowsWithTheSalesOfTheLastThreeMonths_AC_7_2()
        {
            var s = Setup.Opened();
            s.Finance.TodayValue = new DateTime(1975, 7, 10);
            s.Finance.SetSales(1975, 4, 1500000.0);
            s.Finance.SetSales(1975, 5, 1500000.0);
            s.Finance.SetSales(1975, 6, 1500000.0);

            Assert.Near(10650000.0, s.Line.Limit(), 1e-6, "8 400 000 + 0.5 x 4 500 000");
            Assert.Near(10650000.0, s.Line.Available(), 1e-6);
        }

        [Test]
        public void Limit_FollowsTheDifficulty()
        {
            double[] shares = { 1.5, 1.25, 1.0, 0.75, 0.5, 0.25 };
            for (int level = 0; level < shares.Length; level++)
            {
                var s = Setup.Opened();
                s.Finance.DifficultyLevel = level;
                Assert.Near(shares[level] * 8400000.0, s.Line.Limit(), 1e-6, "difficulty " + level);
            }
        }

        [Test]
        public void Limit_IsHeldToThreeTimesTheBase()
        {
            var s = Setup.Opened();
            s.Finance.TodayValue = new DateTime(1975, 7, 10);
            s.Finance.SetSales(1975, 6, 1.0e10);

            Assert.Near(3.0 * 8400000.0, s.Line.Limit(), 1e-6);
        }

        [Test]
        public void Limit_OnTheFloorOfTheBase_AC_7_6()
        {
            var s = Setup.Opened();
            s.Finance.DebtLimit = 1000000.0;   // the bank's floor
            s.Finance.InflationValue = 2.0;    // 1985

            Assert.Near(4000000.0, s.Line.Limit(), 1e-6, "D_L = max(1 000 000, 2 000 000 x 2.0)");
            Assert.Near(4000000.0, s.Line.Available(), 1e-6);

            s.Finance.MoneyValue = -950000.0;   // below -0.9 x 1 000 000
            s.Line.OnDay();
            Assert.Near(350000.0, s.Data.drawn, 1e-6, "the auto draw works from -0.9 x D: it lifts the balance to -0.6 x D");
        }

        [Test]
        public void Limit_IsZeroForALineThatIsNotOpen_OrWithoutTheCompany()
        {
            var s = new Setup();
            Assert.Near(0.0, s.Line.Limit(), 0);
            Assert.Near(0.0, s.Line.Available(), 0);

            s.Line.EnsureOpened();
            s.Finance.Available = false;
            Assert.Near(0.0, s.Line.Limit(), 0);
        }

        // ---- the auto draw ----

        [Test]
        public void OnDay_AutoDraw_AC_7_3()
        {
            var s = Setup.Opened();
            s.Data.drawn = 3400000.0;           // 5 000 000 of the limit are free
            s.Finance.MoneyValue = -7700000.0;  // below -7 560 000

            s.Line.OnDay();

            Assert.Near(2660000.0, s.Data.drawn - 3400000.0, 1e-6, "2 660 000 are drawn");
            Assert.Near(-5040000.0, s.Finance.MoneyValue, 1e-6, "the balance is -0.6 x D");
            Assert.Equal(1, s.Finance.Added.Count, "a capital operation, not income");
            Assert.Equal(1, s.AutoDraws.Count);
            Assert.Near(2660000.0, s.AutoDraws[0], 1e-6);
            Assert.Equal(1, s.Rig.Entries("line.autodraw").Count);
            Assert.Near(2660000.0, s.Rig.Entries("line.autodraw")[0].amount, 1e-6);
        }

        [Test]
        public void OnDay_IsCheapWhileTheBalanceIsFarFromTheThreshold()
        {
            var s = Setup.Opened();
            s.Finance.MoneyValue = -7500000.0;   // above -7 560 000
            s.Finance.SalesReads = 0;

            s.Line.OnDay();

            Assert.Near(0.0, s.Data.drawn, 0);
            Assert.Equal(0, s.Finance.SalesReads, "the limit is not even computed");

            s.Finance.MoneyValue = -7560000.0;   // exactly at the trigger: not below
            s.Line.OnDay();
            Assert.Near(0.0, s.Data.drawn, 0);
        }

        [Test]
        public void OnDay_NeverDrawsMoreThanIsFree()
        {
            var s = Setup.Opened();
            s.Data.drawn = 8000000.0;           // 400 000 are free
            s.Finance.MoneyValue = -8000000.0;  // would need 3 160 000

            s.Line.OnDay();

            Assert.Near(8400000.0, s.Data.drawn, 1e-6);
            Assert.Near(-7600000.0, s.Finance.MoneyValue, 1e-6);

            s.Finance.MoneyValue = -8000000.0;
            s.Line.OnDay();
            Assert.Near(8400000.0, s.Data.drawn, 1e-6, "nothing is free any more");
        }

        [Test]
        public void OnDay_DoesNothing_WhenItMustNot()
        {
            // the switch of the menu, the part, an expired line, a bankrupt company, no company, no ledger
            var s = Setup.Opened();
            s.Finance.MoneyValue = -8000000.0;

            s.Line.SetAutoDraw(false);
            Assert.False(s.Line.AutoDraw);
            s.Line.OnDay();
            s.Line.SetAutoDraw(true);

            s.Rig.Off.Add(SubFeature.StartupCredit);
            s.Line.OnDay();
            s.Rig.Off.Clear();

            s.Finance.Bankrupt = true;
            s.Line.OnDay();
            s.Finance.Bankrupt = false;

            s.Finance.Available = false;
            s.Line.OnDay();
            s.Finance.Available = true;

            s.Rig.Money.Ledger = false;
            s.Line.OnDay();
            s.Rig.Money.Ledger = true;

            s.Finance.TodayValue = new DateTime(1978, 1, 1);   // the term is over
            s.Line.OnDay();

            Assert.Near(0.0, s.Data.drawn, 0);
            Assert.Near(-8000000.0, s.Finance.MoneyValue, 0);
            Assert.Equal(0, s.AutoDraws.Count);

            s.Finance.TodayValue = new DateTime(1975, 6, 1);
            s.Line.OnDay();
            Assert.True(s.Data.drawn > 0.0, "and with everything in order it draws");
        }

        [Test]
        public void OnDay_ALineThatIsNotOpen_DoesNothing()
        {
            var s = new Setup();
            s.Finance.MoneyValue = -8000000.0;
            s.Line.OnDay();
            Assert.Near(-8000000.0, s.Finance.MoneyValue, 0);
        }

        // ---- the month ----

        [Test]
        public void OnMonth_InterestIsOneLineOfTheReport_AC_7_5()
        {
            var s = Setup.Opened();
            s.Data.drawn = 2660000.0;
            s.Finance.MoneyValue = 0.0;

            s.Line.OnMonth();

            Assert.Equal(1, s.Finance.Spent.Count);
            Assert.Near(22166.67, s.Finance.Spent[0].Key, 0.01);
            Assert.Equal(FakePlayerFinance.InterestItem, s.Finance.Spent[0].Value);
            Assert.Near(2660000.0, s.Data.drawn, 0, "no positive balance, nothing is repaid");
            List<JournalEntryData> interest = s.Rig.Entries("line.interest");
            Assert.Equal(1, interest.Count);
            Assert.Near(-22166.67, interest[0].amount, 0.01);
        }

        [Test]
        public void OnMonth_TheInterestFollowsTheRateMultiplier()
        {
            var s = Setup.Opened();
            s.Data.drawn = 2660000.0;
            s.Finance.Multiplier = 2.0;
            s.Finance.MoneyValue = 0.0;

            s.Line.OnMonth();

            Assert.Near(44333.33, s.Finance.Spent[0].Key, 0.01);
            Assert.Near(44333.33, s.Line.NextInterest(), 0.01);
        }

        [Test]
        public void OnMonth_AQuarterOfAPositiveBalanceRepaysTheDebt_AfterTheInterest()
        {
            var s = Setup.Opened();
            s.Data.drawn = 2660000.0;
            s.Finance.MoneyValue = 4000000.0;

            s.Line.OnMonth();

            double afterInterest = 4000000.0 - 22166.6667;
            double sweep = 0.25 * afterInterest;
            Assert.Near(2660000.0 - sweep, s.Data.drawn, 0.01);
            Assert.Near(afterInterest - sweep, s.Finance.MoneyValue, 0.01);
            Assert.Equal(1, s.Rig.Entries("line.sweep").Count);
            Assert.Near(-sweep, s.Rig.Entries("line.sweep")[0].amount, 0.01);
        }

        [Test]
        public void OnMonth_TheRepaymentCannotExceedTheDebt()
        {
            var s = Setup.Opened();
            s.Data.drawn = 1000000.0;
            s.Finance.MoneyValue = 100000000.0;

            s.Line.OnMonth();

            Assert.Near(0.0, s.Data.drawn, 1e-6);
            Assert.True(s.Line.IsOpen, "an open line with no debt stays open until its term ends");
        }

        [Test]
        public void OnMonth_TheEndOfTheTerm_TurnsTheDebtIntoATermLoan_AC_7_4()
        {
            var s = Setup.Opened();
            s.Data.drawn = 3000000.0;
            s.Finance.TodayValue = new DateTime(1978, 1, 1);   // today = the closing date
            s.Finance.MoneyValue = -1000000.0;                 // no positive balance: nothing is repaid before the end

            s.Line.OnMonth();

            Assert.Equal(CreditLineData.StateClosed, s.Data.state);
            Assert.Equal(CreditLineData.ReasonExpired, s.Data.closedReason);
            Assert.Near(0.0, s.Data.drawn, 0);
            Assert.False(s.Line.IsOpen);

            Assert.Equal(1, s.Loans.Loans.Count);
            LoanData term = s.Loans.Loans[0];
            Assert.Equal("term-1", term.id);
            Assert.Equal(LoanData.KindTerm, term.kind);
            Assert.Near(3000000.0, term.principal, 1e-6, "the whole debt");
            Assert.Near(0.15, term.annualRate, 0);
            Assert.Equal(24, term.monthsTotal);
            Assert.Equal(24, term.monthsLeft);
            Assert.Near(145459.94, term.payment, 0.01);

            Assert.Equal(1, s.Converted.Count);
            RecoveryAssert.Same(term, s.Converted[0]);
            Assert.Near(-1000000.0 - 25000.0, s.Finance.MoneyValue, 1e-6, "only the interest of the month was taken: 3 000 000 x 0.10 / 12; no new money");
            Assert.Equal(0, s.Finance.Added.Count);
            Assert.Equal(1, s.Rig.Entries("line.convert").Count);
            Assert.Equal("term-1", s.Rig.Entries("line.convert")[0].details);
        }

        [Test]
        public void OnMonth_TheEndOfTheTerm_TheRepaymentOfThatMonthComesFirst_ThenTheRestBecomesTheLoan()
        {
            // the order of the task: the interest, the repayment out of a positive balance, then what is left is converted
            var s = Setup.Opened();
            s.Data.drawn = 3000000.0;
            s.Finance.TodayValue = new DateTime(1978, 1, 1);
            s.Finance.MoneyValue = 5000000.0;

            s.Line.OnMonth();

            double sweep = 0.25 * (5000000.0 - 25000.0);
            Assert.Equal(1, s.Loans.Loans.Count);
            Assert.Near(3000000.0 - sweep, s.Loans.Loans[0].principal, 1e-6);
            Assert.Near(5000000.0 - 25000.0 - sweep, s.Finance.MoneyValue, 1e-6);
            Assert.Equal(CreditLineData.StateClosed, s.Data.state);
            Assert.Equal(1, s.Rig.Entries("line.sweep").Count);
        }

        [Test]
        public void OnMonth_TheEndOfTheTerm_ARepaymentThatClearsTheDebtLeavesNoLoan()
        {
            var s = Setup.Opened();
            s.Data.drawn = 1000000.0;
            s.Finance.TodayValue = new DateTime(1978, 1, 1);
            s.Finance.MoneyValue = 100000000.0;

            s.Line.OnMonth();

            Assert.Equal(CreditLineData.StateClosed, s.Data.state);
            Assert.Equal(0, s.Loans.Loans.Count);
            Assert.Equal(0, s.Converted.Count);
            Assert.Near(0.0, s.Data.drawn, 0);
        }

        [Test]
        public void OnMonth_TheEndOfTheTerm_WithoutDebt_ClosesTheLine()
        {
            var s = Setup.Opened();
            s.Finance.TodayValue = new DateTime(1978, 1, 1);

            s.Line.OnMonth();

            Assert.Equal(CreditLineData.StateClosed, s.Data.state);
            Assert.Equal(0, s.Loans.Loans.Count);
            Assert.Equal(0, s.Converted.Count);
        }

        [Test]
        public void OnMonth_BeforeTheEnd_TheLineStaysOpen()
        {
            var s = Setup.Opened();
            s.Data.drawn = 1000000.0;
            s.Finance.TodayValue = new DateTime(1977, 12, 1);

            s.Line.OnMonth();

            Assert.Equal(CreditLineData.StateOpen, s.Data.state);
            Assert.Equal(0, s.Loans.Loans.Count);
        }

        [Test]
        public void OnMonth_AClosedLine_NeverComesBack()
        {
            var s = Setup.Opened();
            s.Finance.TodayValue = new DateTime(1978, 1, 1);
            s.Line.OnMonth();
            Assert.Equal(CreditLineData.StateClosed, s.Data.state);

            s.Finance.TodayValue = new DateTime(1978, 2, 1);
            s.Line.OnMonth();
            s.Line.OnDay();
            Assert.False(s.Line.EnsureOpened());
            Assert.Equal(CreditLineData.StateClosed, s.Data.state);
        }

        [Test]
        public void OnMonth_ServesTheLineWhileThePartIsOff_AC_12_4()
        {
            var s = Setup.Opened();
            s.Data.drawn = 2660000.0;
            s.Finance.MoneyValue = 4000000.0;
            s.Rig.Off.Add(SubFeature.StartupCredit);

            s.Line.OnMonth();

            Assert.Equal(1, s.Finance.Spent.Count, "the interest is still charged");
            Assert.True(s.Data.drawn < 2660000.0, "and the repayment still happens");
        }

        [Test]
        public void OnMonth_DoesNothingWithoutTheCompanyTheLedgerOrALine()
        {
            var s = Setup.Opened();
            s.Data.drawn = 2660000.0;

            s.Finance.Available = false;
            s.Line.OnMonth();
            s.Finance.Available = true;
            s.Rig.Money.Ledger = false;
            s.Line.OnMonth();
            s.Rig.Money.Ledger = true;

            Assert.Equal(0, s.Finance.Spent.Count);

            var none = new Setup();
            none.Line.OnMonth();
            Assert.Equal(0, none.Finance.Spent.Count);
        }

        // ---- the player's own moves ----

        [Test]
        public void Draw_ByHand_IsACapitalOperation_CutToWhatIsFree()
        {
            var s = Setup.Opened();
            s.Finance.MoneyValue = 0.0;

            Assert.Near(3000000.0, s.Line.Draw(3000000.0), 1e-6);
            Assert.Near(3000000.0, s.Data.drawn, 1e-6);
            Assert.Near(3000000.0, s.Finance.MoneyValue, 1e-6);
            Assert.Equal(0, s.Finance.Earned.Count, "no income in the report");
            Assert.Equal(1, s.Rig.Entries("line.draw").Count);

            Assert.Near(5400000.0, s.Line.Draw(99000000.0), 1e-6, "cut to the free part: 8 400 000 - 3 000 000");
            Assert.Near(8400000.0, s.Data.drawn, 1e-6);
            Assert.Near(0.0, s.Line.Draw(1.0), 0, "nothing is free any more");
            Assert.Equal(CreditLineEngine.ReasonFull, s.Line.DrawBlock());
        }

        [Test]
        public void Draw_RefusesBadAmounts()
        {
            var s = Setup.Opened();
            Assert.Near(0.0, s.Line.Draw(0.0), 0);
            Assert.Near(0.0, s.Line.Draw(-5.0), 0);
            Assert.Near(0.0, s.Line.Draw(double.NaN), 0);
            Assert.Near(0.0, s.Line.Draw(double.PositiveInfinity), 0);
            Assert.Near(0.0, s.Data.drawn, 0);
        }

        [Test]
        public void DrawBlock_NamesTheReason()
        {
            var s = new Setup();
            Assert.Equal(CreditLineEngine.ReasonNoLine, s.Line.DrawBlock());

            s.Line.EnsureOpened();
            Assert.Null(s.Line.DrawBlock());

            s.Rig.Off.Add(SubFeature.StartupCredit);
            Assert.Equal(CreditLineEngine.ReasonOff, s.Line.DrawBlock());
            Assert.Near(0.0, s.Line.Draw(1000.0), 0, "the part is off: no new draws");
            s.Rig.Off.Clear();

            s.Finance.Bankrupt = true;
            Assert.Equal(CreditLineEngine.ReasonBankrupt, s.Line.DrawBlock());
            s.Finance.Bankrupt = false;

            s.Finance.TodayValue = new DateTime(1978, 1, 1);
            Assert.Equal(CreditLineEngine.ReasonExpired, s.Line.DrawBlock());
            Assert.True(s.Line.IsExpired);
            Assert.Near(0.0, s.Line.Available(), 0);
        }

        [Test]
        public void IsExpired_IsTrueFromTheClosingDay()
        {
            var s = Setup.Opened();
            s.Finance.TodayValue = new DateTime(1977, 12, 31);
            Assert.False(s.Line.IsExpired);
            s.Finance.TodayValue = new DateTime(1978, 1, 1);
            Assert.True(s.Line.IsExpired);
            s.Finance.TodayValue = new DateTime(1980, 1, 1);
            Assert.True(s.Line.IsExpired);
        }

        [Test]
        public void Repay_ByHand_NeedsTheMoney_AndWorksWhileThePartIsOff()
        {
            var s = Setup.Opened();
            s.Data.drawn = 3000000.0;
            s.Finance.MoneyValue = 5000000.0;

            Assert.Near(1000000.0, s.Line.Repay(1000000.0), 1e-6);
            Assert.Near(2000000.0, s.Data.drawn, 1e-6);
            Assert.Near(4000000.0, s.Finance.MoneyValue, 1e-6);
            Assert.Equal(1, s.Rig.Entries("line.repay").Count);
            Assert.Equal(0, s.Finance.Spent.Count, "no expense in the report");

            s.Rig.Off.Add(SubFeature.StartupCredit);
            Assert.Near(2000000.0, s.Line.Repay(99000000.0), 1e-6, "cut to the debt; the part being off does not matter");
            Assert.Near(0.0, s.Data.drawn, 1e-6);

            s.Data.drawn = 3000000.0;
            s.Finance.MoneyValue = 2999999.0;
            Assert.Near(0.0, s.Line.Repay(3000000.0), 0, "the balance must cover the sum");
            Assert.Near(3000000.0, s.Data.drawn, 0);
        }

        [Test]
        public void Repay_RefusesWhatCannotBePaid()
        {
            var none = new Setup();
            Assert.Near(0.0, none.Line.Repay(100.0), 0, "no line");

            var s = Setup.Opened();
            s.Finance.MoneyValue = 5000000.0;
            Assert.Near(0.0, s.Line.Repay(100.0), 0, "nothing is owed");
            s.Data.drawn = 1000.0;
            Assert.Near(0.0, s.Line.Repay(0.0), 0);
            Assert.Near(0.0, s.Line.Repay(-1.0), 0);
            Assert.Near(0.0, s.Line.Repay(double.NaN), 0);
            s.Rig.Money.Ledger = false;
            Assert.Near(0.0, s.Line.Repay(100.0), 0);
        }

        // ---- the state ----

        [Test]
        public void TheEngine_FollowsTheStateThatALoadReplaces()
        {
            var s = Setup.Opened();
            s.Data.drawn = 1000000.0;
            Assert.True(s.Line.IsOpen);

            s.Rig.ReplaceState();
            Assert.False(s.Line.IsOpen);
            Assert.Near(0.0, s.Line.Drawn, 0);

            Assert.True(s.Line.EnsureOpened());
            Assert.True(s.Line.IsOpen);
            Assert.False(s.Line.IsExpired, "the closing date of the new state is read, not the old one");
        }

        [Test]
        public void NextInterest_IsZeroWithoutADebtOrALine()
        {
            var s = new Setup();
            Assert.Near(0.0, s.Line.NextInterest(), 0);
            s.Line.EnsureOpened();
            Assert.Near(0.0, s.Line.NextInterest(), 0);
            s.Data.drawn = 2660000.0;
            Assert.Near(22166.67, s.Line.NextInterest(), 0.01);
        }
    }
}
