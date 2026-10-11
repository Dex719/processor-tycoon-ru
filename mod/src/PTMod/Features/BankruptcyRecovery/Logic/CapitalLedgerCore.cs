using System;
using System.Collections.Generic;
using System.Globalization;

namespace PTMod.Features.BankruptcyRecovery.Logic
{
    /// <summary>
    /// The rules of <c>CapitalLedger</c> (design.md, "CapitalLedger"), without the game: what the backend does behind the static facade. The
    /// facade of the API has already refused a blank feature id and an amount that is not finite and above 0 and counts the failures of the backend; this
    /// class repeats those checks (it is also used on its own) and does the money:
    /// <list type="bullet">
    /// <item><b>Capital operations</b> (<see cref="CapitalIn"/>, <see cref="CapitalOut"/>) move the balance only, through <see cref="IPlayerFinance.AddMoney"/>:
    /// they are not income or expense of the profit and loss report and are not taxed.</item>
    /// <item><b>Report operations</b> (<see cref="Expense"/>, <see cref="Revenue"/>) go through <see cref="IPlayerFinance.SpendMoney"/> and
    /// <see cref="IPlayerFinance.EarnMoney"/> with the item given.</item>
    /// <item><b>Journal.</b> The operations of the feature itself (<c>ownFeatureId</c>) are written to the <see cref="RecoveryJournal"/>; the operations of
    /// every other feature (<c>rival-acquisitions</c>) are only reported to the log callback. Nothing is journaled for a rejected call.</item>
    /// <item><b>Player only.</b> The company is the player's; while the player's company is not there (the menu, a scene change) nothing moves.</item>
    /// </list>
    /// </summary>
    internal sealed class CapitalLedgerCore
    {
        private readonly IPlayerFinance _finance;
        private readonly RecoveryJournal _journal;
        private readonly string _ownFeatureId;
        private readonly Action<string, string, bool> _log;

        /// <param name="finance">The player's finances.</param>
        /// <param name="journal">The journal of the feature.</param>
        /// <param name="ownFeatureId">The id whose operations are journaled (<c>bankruptcy-recovery</c>).</param>
        /// <param name="log">Called as (feature id, text, isOwn) for every applied operation; may be null.</param>
        public CapitalLedgerCore(IPlayerFinance finance, RecoveryJournal journal, string ownFeatureId, Action<string, string, bool> log = null)
        {
            if (finance == null) throw new ArgumentNullException("finance");
            if (journal == null) throw new ArgumentNullException("journal");
            if (string.IsNullOrWhiteSpace(ownFeatureId)) throw new ArgumentException("the own feature id is required", "ownFeatureId");
            _finance = finance;
            _journal = journal;
            _ownFeatureId = ownFeatureId;
            _log = log;
        }

        /// <summary>The journal of the feature, oldest first (a copy).</summary>
        public IReadOnlyList<JournalEntryData> Journal
        {
            get { return _journal.Snapshot(); }
        }

        /// <summary>Capital in: <c>+amount</c> on the balance. True when it was applied.</summary>
        public bool CapitalIn(string featureId, double amount, string op, string details)
        {
            if (!Accept(featureId, amount)) return false;
            _finance.AddMoney(amount);
            Done(featureId, "CapitalIn", op, amount, details);
            return true;
        }

        /// <summary>Capital out: <c>−amount</c> on the balance, which may go below zero. True when it was applied.</summary>
        public bool CapitalOut(string featureId, double amount, string op, string details)
        {
            if (!Accept(featureId, amount)) return false;
            _finance.AddMoney(-amount);
            Done(featureId, "CapitalOut", op, -amount, details);
            return true;
        }

        /// <summary>An expense through the report (<paramref name="expenseOrigin"/> is the number of the game's item). True when it was applied.</summary>
        public bool Expense(string featureId, double amount, int expenseOrigin, string op, string details)
        {
            if (!Accept(featureId, amount)) return false;
            _finance.SpendMoney(amount, expenseOrigin);
            Done(featureId, "Expense", op, -amount, details);
            return true;
        }

        /// <summary>An income through the report (<paramref name="revenueOrigin"/> is the number of the game's item). True when it was applied.</summary>
        public bool Revenue(string featureId, double amount, int revenueOrigin, string op, string details)
        {
            if (!Accept(featureId, amount)) return false;
            _finance.EarnMoney(amount, revenueOrigin);
            Done(featureId, "Revenue", op, amount, details);
            return true;
        }

        private bool Accept(string featureId, double amount)
        {
            if (string.IsNullOrWhiteSpace(featureId)) return false;
            if (double.IsNaN(amount) || double.IsInfinity(amount) || amount <= 0.0) return false;
            return _finance.IsAvailable;
        }

        // The record of an applied operation: the journal for the feature itself, the log line for anybody.
        private void Done(string featureId, string operation, string op, double signedAmount, string details)
        {
            bool own = string.Equals(featureId, _ownFeatureId, StringComparison.Ordinal);
            string code = op ?? string.Empty;
            string note = details ?? string.Empty;
            if (own) _journal.Record(code, signedAmount, note);

            if (_log == null) return;
            string sign = signedAmount >= 0.0 ? "+" : string.Empty;
            string line = "capital: " + operation + " " + code + " " + sign + signedAmount.ToString("0.##", CultureInfo.InvariantCulture)
                          + (note.Length > 0 ? " (" + note + ")" : string.Empty);
            _log(featureId, line, own);
        }
    }
}
