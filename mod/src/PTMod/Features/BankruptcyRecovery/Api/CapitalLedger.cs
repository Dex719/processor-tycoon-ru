using System;
using System.Collections.Generic;
using System.Globalization;
using PTMod.Core;
using PTMod.Features.BankruptcyRecovery.Logic;
using ExpenseOrigin = ProcessorTycoon.ExpenseOrigin;
using RevenueOrigin = ProcessorTycoon.RevenueOrigin;

namespace PTMod.Features.BankruptcyRecovery
{
    /// <summary>
    /// The one place for the mod's money operations on the player's company (design: "CapitalLedger", shared with <c>rival-acquisitions</c>).
    /// No patches. It works even when <c>bankruptcy-recovery</c> is switched off: it is a service, not a part of the feature's behaviour, and a
    /// feature that uses it has no hard dependency on <c>bankruptcy-recovery</c>.
    /// <list type="bullet">
    /// <item><b>Capital operations</b> (<see cref="CapitalIn"/>, <see cref="CapitalOut"/>) move only the balance (<c>Company.AddMoney</c>): they
    /// are not income or expense of the profit and loss report and are not taxed.</item>
    /// <item><b>Report operations</b> (<see cref="Expense"/>, <see cref="Revenue"/>) go through <c>Company.SpendMoney</c> and
    /// <c>Company.EarnMoney</c> with the given origin, like the game's own operations.</item>
    /// <item><b>Player only.</b> The company is <c>Player.Instance.Company</c>. AI companies are changed by their own features
    /// (<c>ICompany.set_MoneyAmount</c>) and are not written to any journal.</item>
    /// <item><b>Checks.</b> A blank <c>featureId</c> and an amount that is zero, negative, NaN or infinite are rejected with a warning in
    /// <c>ModLog</c>; nothing moves. Amounts are <c>double</c>, the game gets a <c>float</c>.</item>
    /// <item><b>Journal.</b> The records of <c>featureId == "bankruptcy-recovery"</c> only, the last <see cref="JournalCapacity"/>; the calls of
    /// other features go to <c>ModLog</c> only (<c>[PTMod] [featureId] INFO capital: ...</c>).</item>
    /// <item><b>Fault isolation.</b> The methods never throw: an exception of the backend is counted against <c>bankruptcy-recovery</c>.</item>
    /// </list>
    /// A caller whose own step must not go on without the money checks <see cref="IsAvailable"/> before it starts; without a registered backend
    /// (an isolated build) the operations only log a warning. Main thread only.
    /// </summary>
    public static class CapitalLedger
    {
        /// <summary>The size of the ring buffer of the journal (FR-10).</summary>
        public const int JournalCapacity = 200;

        private static ICapitalLedgerBackend _backend = NullCapitalLedger.Instance;

        // ---- public API ----

        /// <summary>
        /// Capital in (+balance), outside the profit and loss report and the tax: <c>Company.AddMoney</c>.
        /// </summary>
        /// <param name="featureId">The calling feature (<c>rival-acquisitions</c>); decides whether the operation is journaled.</param>
        /// <param name="amount">A positive finite amount in dollars.</param>
        /// <param name="op">The operation code for the journal and the log (<c>bridge.open</c>, <c>acq.price</c>).</param>
        /// <param name="details">A short note; may be null.</param>
        public static void CapitalIn(string featureId, double amount, string op, string details)
        {
            if (!Accept("CapitalIn", featureId, amount, ref op, ref details)) return;
            try
            {
                _backend.CapitalIn(featureId, amount, op, details);
            }
            catch (Exception e)
            {
                Failed("CapitalIn", e);
            }
        }

        /// <summary>
        /// Capital out (-balance), outside the profit and loss report: <c>Company.AddMoney</c> with the negative amount. The balance may go below zero.
        /// </summary>
        public static void CapitalOut(string featureId, double amount, string op, string details)
        {
            if (!Accept("CapitalOut", featureId, amount, ref op, ref details)) return;
            try
            {
                _backend.CapitalOut(featureId, amount, op, details);
            }
            catch (Exception e)
            {
                Failed("CapitalOut", e);
            }
        }

        /// <summary>
        /// An expense through the report: <c>Company.SpendMoney(amount, origin)</c> (interest, fees, the investor's share). Lowers the taxable profit.
        /// </summary>
        public static void Expense(string featureId, double amount, ExpenseOrigin origin, string op, string details)
        {
            if (!Accept("Expense", featureId, amount, ref op, ref details)) return;
            try
            {
                _backend.Expense(featureId, amount, origin, op, details);
            }
            catch (Exception e)
            {
                Failed("Expense", e);
            }
        }

        /// <summary>
        /// An income through the report: <c>Company.EarnMoney(amount, origin)</c> (for example the sale of production lines with
        /// <c>RevenueOrigin.Sales</c>).
        /// </summary>
        public static void Revenue(string featureId, double amount, RevenueOrigin origin, string op, string details)
        {
            if (!Accept("Revenue", featureId, amount, ref op, ref details)) return;
            try
            {
                _backend.Revenue(featureId, amount, origin, op, details);
            }
            catch (Exception e)
            {
                Failed("Revenue", e);
            }
        }

        /// <summary>
        /// The journal of <c>bankruptcy-recovery</c> (the records with <c>featureId == "bankruptcy-recovery"</c>), the last
        /// <see cref="JournalCapacity"/>, oldest first. Never null; empty without a backend.
        /// </summary>
        public static IReadOnlyList<JournalEntryData> Journal
        {
            get
            {
                try
                {
                    return _backend.Journal ?? Array.Empty<JournalEntryData>();
                }
                catch (Exception e)
                {
                    Failed("Journal", e);
                    return Array.Empty<JournalEntryData>();
                }
            }
        }

        /// <summary>
        /// The feature has registered its backend, so the operations really move money. False in an isolated build and before
        /// <c>BankruptcyRecoveryFeature.Register</c> ran.
        /// </summary>
        public static bool IsAvailable
        {
            get { return !ReferenceEquals(_backend, NullCapitalLedger.Instance); }
        }

        // ---- for the feature ----

        /// <summary>
        /// Registers the implementation (once, in <c>BankruptcyRecoveryFeature.Register</c>); null puts the null object back. Not for other features.
        /// </summary>
        internal static void SetBackend(ICapitalLedgerBackend backend)
        {
            _backend = backend ?? NullCapitalLedger.Instance;
        }

        /// <summary>The registered backend; the null object when there is none. For the feature and its tests.</summary>
        internal static ICapitalLedgerBackend Backend
        {
            get { return _backend; }
        }

        // ---- checks ----

        // The common check of the four operations; normalizes the texts. False: rejected, already logged.
        private static bool Accept(string operation, string featureId, double amount, ref string op, ref string details)
        {
            if (string.IsNullOrWhiteSpace(featureId))
            {
                ModLog.Warn(RecoveryApi.FeatureId, "capital: " + operation + " '" + op + "' without a feature id is rejected");
                return false;
            }
            if (double.IsNaN(amount) || double.IsInfinity(amount) || amount <= 0)
            {
                ModLog.Warn(featureId, "capital: " + operation + " '" + op + "' with the amount "
                                       + amount.ToString("R", CultureInfo.InvariantCulture) + " is rejected (it must be finite and above zero)");
                return false;
            }
            if (op == null) op = string.Empty;
            if (details == null) details = string.Empty;
            return true;
        }

        private static void Failed(string operation, Exception e)
        {
            ModHooks.ReportFault(RecoveryApi.FeatureId, "CapitalLedger." + operation, e);
        }
    }
}
