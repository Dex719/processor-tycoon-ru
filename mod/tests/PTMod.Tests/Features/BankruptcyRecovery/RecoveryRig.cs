using System;
using System.Collections.Generic;
using PTMod.Features.BankruptcyRecovery.Logic;

namespace PTMod.Tests.Features.BankruptcyRecovery
{
    /// <summary>
    /// A fake of the player's finances (<see cref="IPlayerFinance"/>): plain fields the tests set, and a record of every change the engines make. Money
    /// operations change <see cref="MoneyValue"/> as the game's company would. The reports are two tables by month, and the sums are looked up with
    /// <see cref="ReportMonths"/> like the real adapter does. Internal, so the runner never discovers it.
    /// </summary>
    internal sealed class FakePlayerFinance : IPlayerFinance
    {
        /// <summary>The numbers of the game's enums the feature uses (<c>ExpenseOrigin</c> and <c>RevenueOrigin</c>), as the core logic passes them.</summary>
        public const int InterestItem = 6;
        public const int RoyaltiesItem = 2;
        public const int SalesItem = 0;

        public bool Available = true;
        public double MoneyValue = 1000000.0;
        public double DebtLimit = 8400000.0;
        public double InflationValue = 1.25;
        public bool InDefault;
        public bool Bankrupt;
        public int Days;
        public int TimeLimit = 120;
        public int DifficultyLevel = 2;
        public DateTime TodayValue = new DateTime(1975, 1, 1);
        public DateTime Foundation = new DateTime(1975, 1, 1);
        public int Capacity = 20;
        public double Multiplier = 1.0;

        /// <summary>Revenue from sales and contracts by month, <c>year × 12 + month</c>.</summary>
        public readonly Dictionary<int, double> Sales = new Dictionary<int, double>();

        /// <summary>Net profit by month, <c>year × 12 + month</c>.</summary>
        public readonly Dictionary<int, double> Profit = new Dictionary<int, double>();

        public int SalesReads;
        public int CancelCalls;
        public Exception CancelThrows;

        public readonly List<double> Added = new List<double>();
        public readonly List<KeyValuePair<double, int>> Spent = new List<KeyValuePair<double, int>>();
        public readonly List<KeyValuePair<double, int>> Earned = new List<KeyValuePair<double, int>>();

        public bool IsAvailable
        {
            get { return Available; }
        }

        public double Money
        {
            get { return MoneyValue; }
        }

        public double PlayerDebtLimit
        {
            get { return DebtLimit; }
        }

        public double Threshold
        {
            get { return -DebtLimit; }
        }

        public double Inflation
        {
            get { return InflationValue; }
        }

        public bool IsInDefault
        {
            get { return InDefault; }
        }

        public bool IsBankrupt
        {
            get { return Bankrupt; }
        }

        public int DaysInDefault
        {
            get { return Days; }
            set { Days = value; }
        }

        public int DefaultTimeLimit
        {
            get { return TimeLimit; }
        }

        public int Difficulty
        {
            get { return DifficultyLevel; }
        }

        public DateTime Today
        {
            get { return TodayValue; }
        }

        public DateTime FoundationDate
        {
            get { return Foundation; }
        }

        public int ProductionCapacity
        {
            get { return Capacity; }
        }

        public double InterestMultiplier
        {
            get { return Multiplier; }
        }

        public double SalesRevenueLastFullMonths(int months)
        {
            SalesReads++;
            double sum = 0.0;
            for (int back = 1; back <= months; back++)
            {
                int year;
                int month;
                ReportMonths.Back(TodayValue, back, out year, out month);
                double value;
                if (Sales.TryGetValue(year * 12 + month, out value)) sum += value;
            }
            return sum;
        }

        public double NetProfitOfMonth(int year, int month)
        {
            double value;
            return Profit.TryGetValue(year * 12 + month, out value) ? value : 0.0;
        }

        public void CancelDefault()
        {
            CancelCalls++;
            if (CancelThrows != null) throw CancelThrows;
            Days = 0;
            InDefault = false;
        }

        public void AddMoney(double amount)
        {
            Added.Add(amount);
            MoneyValue += amount;
        }

        public void SpendMoney(double amount, int expenseOrigin)
        {
            Spent.Add(new KeyValuePair<double, int>(amount, expenseOrigin));
            MoneyValue -= amount;
        }

        public void EarnMoney(double amount, int revenueOrigin)
        {
            Earned.Add(new KeyValuePair<double, int>(amount, revenueOrigin));
            MoneyValue += amount;
        }

        /// <summary>Sets the sales of one month.</summary>
        public void SetSales(int year, int month, double value)
        {
            Sales[year * 12 + month] = value;
        }

        /// <summary>Sets the net profit of one month.</summary>
        public void SetProfit(int year, int month, double value)
        {
            Profit[year * 12 + month] = value;
        }
    }

    /// <summary>The one assertion the mini runner lacks: the same object.</summary>
    internal static class RecoveryAssert
    {
        public static void Same(object expected, object actual, string message = null)
        {
            Assert.True(ReferenceEquals(expected, actual), message ?? "expected the same object");
        }
    }

    /// <summary>
    /// <see cref="IRecoveryMoney"/> on top of the real <see cref="CapitalLedgerCore"/>: the engines under test go through the same rules, the journal and
    /// the fake finances as in the game. The expense items are the numbers of the game's enums.
    /// </summary>
    internal sealed class CoreMoney : IRecoveryMoney
    {
        public const string Own = "bankruptcy-recovery";

        private readonly CapitalLedgerCore _core;
        private readonly Func<int> _investorItem;

        public bool Ledger = true;

        public CoreMoney(CapitalLedgerCore core, Func<int> investorItem)
        {
            _core = core;
            _investorItem = investorItem;
        }

        public bool IsAvailable
        {
            get { return Ledger; }
        }

        public void CapitalIn(double amount, string op, string details)
        {
            _core.CapitalIn(Own, amount, op, details);
        }

        public void CapitalOut(double amount, string op, string details)
        {
            _core.CapitalOut(Own, amount, op, details);
        }

        public void Interest(double amount, string op, string details)
        {
            _core.Expense(Own, amount, FakePlayerFinance.InterestItem, op, details);
        }

        public void InvestorPayment(double amount, string op, string details)
        {
            _core.Expense(Own, amount, _investorItem(), op, details);
        }
    }

    /// <summary>
    /// Everything an engine needs, wired the way the feature wires it, around the fake finances: the state, the balance, the journal, the ledger rules, the
    /// money, the environment and the switches of the five parts (all on). The state can be replaced like a load does. Internal.
    /// </summary>
    internal sealed class RecoveryRig
    {
        public readonly FakePlayerFinance Finance = new FakePlayerFinance();
        public RecoveryState State = new RecoveryState();
        public RecoveryBalance Balance = RecoveryBalance.Defaults();
        public readonly HashSet<SubFeature> Off = new HashSet<SubFeature>();
        public readonly List<string> Warnings = new List<string>();
        public readonly List<string> OtherFeatureLines = new List<string>();
        public readonly RecoveryJournal Journal;
        public readonly CapitalLedgerCore Core;
        public readonly CoreMoney Money;
        public readonly RecoveryEnvironment Env;

        public RecoveryRig()
        {
            Journal = new RecoveryJournal(() => State, () => Finance.TodayValue);
            Core = new CapitalLedgerCore(Finance, Journal, CoreMoney.Own, (feature, line, own) =>
            {
                if (!own) OtherFeatureLines.Add(feature + ": " + line);
            });
            Money = new CoreMoney(Core, () => Balance.bailout.investor.expenseOrigin == "Interest" ? FakePlayerFinance.InterestItem : FakePlayerFinance.RoyaltiesItem);
            Env = new RecoveryEnvironment(Finance, Money, Journal, () => State, () => Balance, part => !Off.Contains(part), Warnings.Add);
        }

        /// <summary>A load or a new game: the state is a new object.</summary>
        public void ReplaceState()
        {
            State = new RecoveryState();
        }

        /// <summary>The journal entries with this operation code, oldest first.</summary>
        public List<JournalEntryData> Entries(string op)
        {
            var found = new List<JournalEntryData>();
            foreach (JournalEntryData entry in State.journal)
            {
                if (entry.op == op) found.Add(entry);
            }
            return found;
        }
    }
}
