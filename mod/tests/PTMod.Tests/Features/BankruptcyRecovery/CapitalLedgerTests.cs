using System;
using System.Collections.Generic;
using PTMod.Features.BankruptcyRecovery.Logic;

namespace PTMod.Tests.Features.BankruptcyRecovery
{
    /// <summary>
    /// TSK-007 (FR-6, FR-7, FR-8, FR-10, FR-11): the rules of the capital ledger on a fake of the player's finances. Capital operations move the
    /// balance only; the report operations go through the report with their item; a call that is not valid moves nothing and is not journaled; the journal
    /// belongs to the feature itself and is a ring of 200; the calls of other features reach only the log. Also the months the reports are looked up by,
    /// the revenue of the last three months kept for a month, and the facts the rules read.
    /// </summary>
    [Covers(typeof(CapitalLedgerCore), typeof(RecoveryJournal), typeof(RecoveryEnvironment), typeof(IPlayerFinance), typeof(IRecoveryMoney),
        typeof(PlayerFinanceExtensions), typeof(ReportMonths))]
    public class CapitalLedgerTests
    {
        private const string Own = CoreMoney.Own;

        // ---- capital operations ----

        [Test]
        public void CapitalIn_MovesTheBalanceOnly_NotTheReport()
        {
            var rig = new RecoveryRig();
            rig.Finance.MoneyValue = 100.0;

            bool done = rig.Core.CapitalIn(Own, 8400000.0, "bridge.open", "12m @ 30%");

            Assert.True(done);
            Assert.Near(8400100.0, rig.Finance.MoneyValue, 1e-6);
            Assert.Equal(1, rig.Finance.Added.Count);
            Assert.Near(8400000.0, rig.Finance.Added[0], 0);
            Assert.Equal(0, rig.Finance.Spent.Count);
            Assert.Equal(0, rig.Finance.Earned.Count);
        }

        [Test]
        public void CapitalOut_TakesFromTheBalanceOnly_AndMayGoBelowZero()
        {
            var rig = new RecoveryRig();
            rig.Finance.MoneyValue = 1000.0;

            bool done = rig.Core.CapitalOut(Own, 608892.0, "loan.principal", "bridge-1");

            Assert.True(done);
            Assert.Near(-607892.0, rig.Finance.MoneyValue, 1e-6);
            Assert.Near(-608892.0, rig.Finance.Added[0], 0);
            Assert.Equal(0, rig.Finance.Spent.Count);
            Assert.Equal(0, rig.Finance.Earned.Count);
        }

        // ---- report operations ----

        [Test]
        public void Expense_GoesThroughTheReport_WithTheGivenItem()
        {
            var rig = new RecoveryRig();
            rig.Finance.MoneyValue = 5000000.0;

            Assert.True(rig.Core.Expense(Own, 420000.0, FakePlayerFinance.InterestItem, "bridge.fee", string.Empty));
            Assert.True(rig.Core.Expense(Own, 1200000.0, FakePlayerFinance.RoyaltiesItem, "investor.payment", "1977-02"));

            Assert.Near(3380000.0, rig.Finance.MoneyValue, 1e-6);
            Assert.Equal(0, rig.Finance.Added.Count, "an expense is not a capital operation");
            Assert.Equal(2, rig.Finance.Spent.Count);
            Assert.Near(420000.0, rig.Finance.Spent[0].Key, 0);
            Assert.Equal(FakePlayerFinance.InterestItem, rig.Finance.Spent[0].Value);
            Assert.Near(1200000.0, rig.Finance.Spent[1].Key, 0);
            Assert.Equal(FakePlayerFinance.RoyaltiesItem, rig.Finance.Spent[1].Value);
        }

        [Test]
        public void Revenue_GoesThroughTheReport_WithTheGivenItem()
        {
            var rig = new RecoveryRig();
            rig.Finance.MoneyValue = 0.0;

            Assert.True(rig.Core.Revenue(Own, 560000.0, FakePlayerFinance.SalesItem, "lines.sell", "5 x 76 mm"));

            Assert.Near(560000.0, rig.Finance.MoneyValue, 1e-6);
            Assert.Equal(0, rig.Finance.Added.Count);
            Assert.Equal(1, rig.Finance.Earned.Count);
            Assert.Equal(FakePlayerFinance.SalesItem, rig.Finance.Earned[0].Value);
        }

        // ---- calls that are not valid ----

        [Test]
        public void BadAmounts_AreRejected_NothingMovesNothingIsJournaled()
        {
            var rig = new RecoveryRig();
            double[] bad = { 0.0, -1.0, -0.0001, double.NaN, double.PositiveInfinity, double.NegativeInfinity };
            foreach (double amount in bad)
            {
                Assert.False(rig.Core.CapitalIn(Own, amount, "op", string.Empty), "CapitalIn " + amount);
                Assert.False(rig.Core.CapitalOut(Own, amount, "op", string.Empty), "CapitalOut " + amount);
                Assert.False(rig.Core.Expense(Own, amount, FakePlayerFinance.InterestItem, "op", string.Empty), "Expense " + amount);
                Assert.False(rig.Core.Revenue(Own, amount, FakePlayerFinance.SalesItem, "op", string.Empty), "Revenue " + amount);
            }

            Assert.Near(1000000.0, rig.Finance.MoneyValue, 0);
            Assert.Equal(0, rig.Finance.Added.Count + rig.Finance.Spent.Count + rig.Finance.Earned.Count);
            Assert.Equal(0, rig.State.journal.Count);
        }

        [Test]
        public void ABlankFeatureId_IsRejected()
        {
            var rig = new RecoveryRig();
            Assert.False(rig.Core.CapitalIn(null, 100.0, "op", string.Empty));
            Assert.False(rig.Core.CapitalOut(string.Empty, 100.0, "op", string.Empty));
            Assert.False(rig.Core.Expense("   ", 100.0, FakePlayerFinance.InterestItem, "op", string.Empty));
            Assert.Near(1000000.0, rig.Finance.MoneyValue, 0);
            Assert.Equal(0, rig.State.journal.Count);
        }

        [Test]
        public void WithoutThePlayersCompany_NothingMoves()
        {
            // FR-11: only the player's company; while it is not there (the menu, a scene change) the ledger does nothing.
            var rig = new RecoveryRig();
            rig.Finance.Available = false;

            Assert.False(rig.Core.CapitalIn(Own, 100.0, "op", string.Empty));
            Assert.False(rig.Core.Revenue(Own, 100.0, FakePlayerFinance.SalesItem, "op", string.Empty));
            Assert.Near(1000000.0, rig.Finance.MoneyValue, 0);
            Assert.Equal(0, rig.State.journal.Count);
        }

        // ---- the journal ----

        [Test]
        public void TheOwnFeature_IsJournaled_WithSignDateOperationAndNote()
        {
            var rig = new RecoveryRig();
            rig.Finance.TodayValue = new DateTime(1976, 3, 1);

            rig.Core.CapitalIn(Own, 8400000.0, "bridge.open", "12m @ 30%");
            rig.Core.Expense(Own, 420000.0, FakePlayerFinance.InterestItem, "bridge.fee", string.Empty);
            rig.Finance.TodayValue = new DateTime(1976, 4, 1);
            rig.Core.CapitalOut(Own, 818892.0, "loan.payment", "bridge-1");
            rig.Core.Revenue(Own, 100.0, FakePlayerFinance.SalesItem, "stock.sell", "X-100");

            IReadOnlyList<JournalEntryData> journal = rig.Core.Journal;
            Assert.Equal(4, journal.Count);

            Assert.Equal("1976-03-01", journal[0].date);
            Assert.Equal("bridge.open", journal[0].op);
            Assert.Near(8400000.0, journal[0].amount, 0);
            Assert.Equal("12m @ 30%", journal[0].details);

            Assert.Near(-420000.0, journal[1].amount, 0, "an expense is negative, as in the example of the design");
            Assert.Equal(string.Empty, journal[1].details);

            Assert.Equal("1976-04-01", journal[2].date);
            Assert.Near(-818892.0, journal[2].amount, 0);
            Assert.Equal("bridge-1", journal[2].details);

            Assert.Near(100.0, journal[3].amount, 0, "an income is positive");
        }

        [Test]
        public void AnotherFeature_ReachesTheLogOnly_NotTheJournal()
        {
            var rig = new RecoveryRig();

            Assert.True(rig.Core.CapitalOut("rival-acquisitions", 12500000.0, "acq.price", "Solar Foundries"));

            Assert.Near(1000000.0 - 12500000.0, rig.Finance.MoneyValue, 1e-6, "the money moves all the same");
            Assert.Equal(0, rig.State.journal.Count, "the journal of the feature holds its own operations only");
            Assert.Equal(1, rig.OtherFeatureLines.Count);
            Assert.True(rig.OtherFeatureLines[0].StartsWith("rival-acquisitions: capital: CapitalOut acq.price -12500000", StringComparison.Ordinal),
                rig.OtherFeatureLines[0]);
            Assert.True(rig.OtherFeatureLines[0].Contains("Solar Foundries"));
        }

        [Test]
        public void TheJournal_IsARingOf200_OldestFirst()
        {
            var rig = new RecoveryRig();
            Assert.Equal(200, RecoveryJournal.Capacity);

            for (int i = 1; i <= 205; i++) rig.Core.CapitalIn(Own, i, "op" + i, string.Empty);

            IReadOnlyList<JournalEntryData> journal = rig.Core.Journal;
            Assert.Equal(200, journal.Count);
            Assert.Equal("op6", journal[0].op, "the five oldest records are gone");
            Assert.Equal("op205", journal[199].op);
            Assert.Equal(200, rig.Journal.Count);
        }

        [Test]
        public void TheJournal_SurvivesTheCodecAtItsFullSize()
        {
            // The records the ledger writes must come back from the save: a blank code or a number that is not finite would be dropped on read.
            var rig = new RecoveryRig();
            for (int i = 0; i < 230; i++) rig.Journal.Record(i % 2 == 0 ? "op" : string.Empty, i % 3 == 0 ? double.NaN : i, null);

            var problems = new List<string>();
            RecoveryState back = RecoveryStateCodec.Read(RecoveryStateCodec.Write(rig.State), problems);

            Assert.Equal(0, problems.Count, string.Join("; ", problems));
            Assert.Equal(200, back.journal.Count);
            foreach (JournalEntryData entry in back.journal)
            {
                Assert.True(entry.op.Length > 0);
                Assert.False(double.IsNaN(entry.amount));
            }
        }

        [Test]
        public void TheSnapshot_IsACopy()
        {
            var rig = new RecoveryRig();
            rig.Core.CapitalIn(Own, 1.0, "a", string.Empty);
            IReadOnlyList<JournalEntryData> snapshot = rig.Core.Journal;

            rig.Core.CapitalIn(Own, 2.0, "b", string.Empty);

            Assert.Equal(1, snapshot.Count);
            Assert.Equal(2, rig.Core.Journal.Count);
        }

        [Test]
        public void TheJournal_FollowsTheStateThatIsReplacedByALoad()
        {
            var rig = new RecoveryRig();
            rig.Core.CapitalIn(Own, 1.0, "old", string.Empty);

            rig.ReplaceState();
            rig.Core.CapitalIn(Own, 2.0, "new", string.Empty);

            Assert.Equal(1, rig.State.journal.Count);
            Assert.Equal("new", rig.State.journal[0].op);
        }

        [Test]
        public void ARecordWithoutAnOperationOrAnAmount_GetsSafeValues()
        {
            var rig = new RecoveryRig();
            rig.State.journal = null;   // a state that came without a list

            rig.Journal.Record("   ", double.PositiveInfinity, null);

            Assert.Equal(1, rig.State.journal.Count);
            Assert.Equal("op", rig.State.journal[0].op);
            Assert.Near(0.0, rig.State.journal[0].amount, 0);
            Assert.Equal(string.Empty, rig.State.journal[0].details);
        }

        // ---- the months of the reports ----

        [Test]
        public void ReportMonths_Back_CountsFullMonthsBeforeTheMonthOfTheDate()
        {
            int year;
            int month;

            ReportMonths.Back(new DateTime(1975, 1, 15), 1, out year, out month);
            Assert.Equal(1974, year);
            Assert.Equal(12, month);

            ReportMonths.Back(new DateTime(1975, 1, 15), 3, out year, out month);
            Assert.Equal(1974, year);
            Assert.Equal(10, month);

            ReportMonths.Back(new DateTime(1975, 7, 1), 0, out year, out month);
            Assert.Equal(1975, year);
            Assert.Equal(7, month);

            ReportMonths.Back(new DateTime(1976, 3, 31), 14, out year, out month);
            Assert.Equal(1975, year);
            Assert.Equal(1, month);

            ReportMonths.Back(new DateTime(1975, 7, 1), -5, out year, out month);
            Assert.Equal(1975, year);
            Assert.Equal(7, month, "a negative count is the month itself");
        }

        [Test]
        public void ReportMonths_Back_BeforeTheFirstMonthOfTheCalendar_GivesAYearNoReportHas()
        {
            int year;
            int month;
            ReportMonths.Back(new DateTime(1, 2, 1), 5, out year, out month);
            Assert.Equal(0, year);
            Assert.Equal(1, month);
        }

        [Test]
        public void ReportMonths_KeyAndNextMonth()
        {
            Assert.Equal(1975 * 12 + 7, ReportMonths.KeyOf(new DateTime(1975, 7, 31)));
            Assert.NotEqual(ReportMonths.KeyOf(new DateTime(1975, 7, 31)), ReportMonths.KeyOf(new DateTime(1975, 8, 1)));

            Assert.Equal(new DateTime(1975, 8, 1), ReportMonths.NextMonthStart(new DateTime(1975, 7, 20)));
            Assert.Equal(new DateTime(1976, 1, 1), ReportMonths.NextMonthStart(new DateTime(1975, 12, 31)));
            Assert.Equal(new DateTime(9999, 12, 1), ReportMonths.NextMonthStart(new DateTime(9999, 12, 31)), "the last month of the calendar does not overflow");
        }

        // ---- R_3 kept for a month ----

        [Test]
        public void Revenue3_SumsTheLastThreeFullMonths_AndIsKeptForTheMonth()
        {
            var rig = new RecoveryRig();
            rig.Finance.TodayValue = new DateTime(1975, 7, 10);
            rig.Finance.SetSales(1975, 6, 1500000.0);
            rig.Finance.SetSales(1975, 5, 1500000.0);
            rig.Finance.SetSales(1975, 4, 1500000.0);
            rig.Finance.SetSales(1975, 3, 99000000.0);   // too old
            rig.Finance.SetSales(1975, 7, 99000000.0);   // the month in progress is not a full month

            Assert.Near(4500000.0, rig.Env.RevenueLast3Months(), 0, "AC-7.2: 4 500 000");
            Assert.Equal(1, rig.Finance.SalesReads);

            rig.Finance.TodayValue = new DateTime(1975, 7, 25);
            rig.Finance.SetSales(1975, 6, 2500000.0);
            Assert.Near(4500000.0, rig.Env.RevenueLast3Months(), 0, "the same month: the value is kept");
            Assert.Equal(1, rig.Finance.SalesReads);

            rig.Finance.TodayValue = new DateTime(1975, 8, 1);
            Assert.Near(99000000.0 + 2500000.0 + 1500000.0, rig.Env.RevenueLast3Months(), 0,
                "a new month: July (now a full month), June and May are summed, the value is read again");
            Assert.Equal(2, rig.Finance.SalesReads);
        }

        [Test]
        public void Revenue3_AMonthWithoutAReportCountsZero_AndGarbageIsZero()
        {
            var rig = new RecoveryRig();
            rig.Finance.TodayValue = new DateTime(1975, 4, 1);
            rig.Finance.SetSales(1975, 3, 700.0);

            Assert.Near(700.0, rig.Env.RevenueLast3Months(), 0, "January and February have no report: they count 0");

            rig.Env.ResetCache();
            rig.Finance.SetSales(1975, 3, double.NaN);
            Assert.Near(0.0, rig.Env.RevenueLast3Months(), 0);

            rig.Env.ResetCache();
            rig.Finance.SetSales(1975, 3, -5.0);
            Assert.Near(0.0, rig.Env.RevenueLast3Months(), 0, "a negative sum is not revenue");
        }

        [Test]
        public void Revenue3_ResetCache_ReadsAgain()
        {
            var rig = new RecoveryRig();
            rig.Finance.TodayValue = new DateTime(1975, 7, 10);
            rig.Finance.SetSales(1975, 6, 100.0);
            Assert.Near(100.0, rig.Env.RevenueLast3Months(), 0);

            rig.Finance.SetSales(1975, 6, 300.0);
            rig.Env.ResetCache();
            Assert.Near(300.0, rig.Env.RevenueLast3Months(), 0);
            Assert.Equal(2, rig.Finance.SalesReads);
        }

        // ---- the facts the rules read ----

        [Test]
        public void View_TakesTheFactsOfThePlayer_AndIsThePlayers()
        {
            var rig = new RecoveryRig();
            rig.Finance.MoneyValue = -30000000.0;
            rig.Finance.DebtLimit = 24300000.0;
            rig.Finance.InDefault = true;
            rig.Finance.Days = 119;
            rig.Finance.TodayValue = new DateTime(1977, 5, 1);

            BankruptcyView view = rig.Env.View();

            Assert.Near(-30000000.0, view.Money, 0);
            Assert.Near(24300000.0, view.DebtLimit, 0);
            Assert.True(view.InDefault);
            Assert.False(view.IsBankrupt);
            Assert.True(view.IsPlayer);
            Assert.Equal(119, view.DaysInDefault);
            Assert.Equal(120, view.DefaultTimeLimit);
            Assert.Equal(new DateTime(1977, 5, 1), view.Today);
        }

        [Test]
        public void LimitBase_IsTheLargerOfTheThresholdAndTheFloor()
        {
            var rig = new RecoveryRig();
            rig.Finance.DebtLimit = 1000000.0;
            rig.Finance.InflationValue = 2.0;
            Assert.Near(4000000.0, rig.Env.LimitBase(), 0, "AC-7.6: max(1 000 000, 2 000 000 x 2.0)");

            rig.Finance.DebtLimit = 8400000.0;
            rig.Finance.InflationValue = 1.25;
            Assert.Near(8400000.0, rig.Env.LimitBase(), 0);
        }

        [Test]
        public void TheEnvironment_ReportsWarningsWithoutThrowing_AndRefusesMissingParts()
        {
            var rig = new RecoveryRig();
            rig.Env.Warn("one");
            Assert.Equal(1, rig.Warnings.Count);
            Assert.Equal("one", rig.Warnings[0]);

            var quiet = new RecoveryEnvironment(rig.Finance, rig.Money, rig.Journal, () => rig.State, () => rig.Balance, null, message => { throw new InvalidOperationException(); });
            quiet.Warn("must not throw");
            Assert.True(quiet.IsOn(SubFeature.BridgeLoan), "without a function every part is on");

            Assert.Throws<ArgumentNullException>(() => new RecoveryEnvironment(null, rig.Money, rig.Journal, () => rig.State, () => rig.Balance));
            Assert.Throws<ArgumentNullException>(() => new RecoveryJournal(null, () => DateTime.Now));
            Assert.Throws<ArgumentNullException>(() => new CapitalLedgerCore(rig.Finance, null, Own));
            Assert.Throws<ArgumentException>(() => new CapitalLedgerCore(rig.Finance, rig.Journal, " "));
        }
    }
}
