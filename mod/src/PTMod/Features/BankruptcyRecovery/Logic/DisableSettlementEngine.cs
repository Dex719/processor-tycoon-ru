using System;
using System.Collections.Generic;
using System.Globalization;

namespace PTMod.Features.BankruptcyRecovery.Logic
{
    /// <summary>
    /// What the question before the switch-off shows (FR-12 item 3): the sum that turns into ordinary debt, the balance after it, and whether that balance is below
    /// the default threshold. Taken from the live numbers at the moment the player flips the switch.
    /// </summary>
    internal sealed class DisablePlan
    {
        public DisablePlan(DisableBreakdown breakdown, double money, double balanceAfter, bool wouldDefault, int loanCount)
        {
            Breakdown = breakdown;
            Money = money;
            BalanceAfter = balanceAfter;
            WouldDefault = wouldDefault;
            LoanCount = loanCount;
        }

        /// <summary>The sum split by source.</summary>
        public DisableBreakdown Breakdown { get; private set; }

        /// <summary>The sum that turns into ordinary debt (always above 0 in a plan).</summary>
        public double Total
        {
            get { return Breakdown.Total; }
        }

        /// <summary>The balance now.</summary>
        public double Money { get; private set; }

        /// <summary>The balance once the sum is taken from it.</summary>
        public double BalanceAfter { get; private set; }

        /// <summary>The balance after is below the default threshold: the game then declares the default (the confirmation warns about it).</summary>
        public bool WouldDefault { get; private set; }

        /// <summary>How many loans of the feature there are.</summary>
        public int LoanCount { get; private set; }
    }

    /// <summary>How a settlement ended.</summary>
    internal enum SettleStatus
    {
        /// <summary>Obligations were turned into ordinary debt (the sum may be 0 for a contract with nothing left to pay).</summary>
        Done,

        /// <summary>There was nothing to settle; only the marks were refreshed.</summary>
        NothingToSettle,

        /// <summary>The player's company or the ledger is not there: nothing was changed, the obligations stay as they are.</summary>
        Unavailable,
    }

    /// <summary>The outcome of <see cref="DisableSettlementEngine.Settle"/>.</summary>
    internal sealed class SettleResult
    {
        public SettleStatus Status;

        /// <summary>The sum taken from the balance as ordinary debt.</summary>
        public double Converted;

        /// <summary>How many loans were closed.</summary>
        public int LoansClosed;

        /// <summary>The credit line had a debt and was closed.</summary>
        public bool LineClosed;

        /// <summary>An investor contract was running and was closed.</summary>
        public bool InvestorClosed;

        /// <summary>A bridge loan was among the closed loans (its pause started).</summary>
        public bool BridgeClosed;
    }

    /// <summary>
    /// The safe switch-off without the game (FR-12, design.md D9 and "DisableSettlementService"): <see cref="Quote"/> says what the player is asked before the
    /// feature goes off, <see cref="Settle"/> turns what is owed into ordinary debt of the balance. One operation <c>CapitalOut(total)</c> with the code
    /// <c>disable.convert</c> (the ledger writes the journal record), then the loans, the credit line and the investor contract are closed, the pause of the bridge loan
    /// starts if a bridge loan was among them, and the marks of <see cref="SettlementMarks"/> are written. The counter of the rescues and the date of the last bridge
    /// loan are kept, and the credit line that was closed never opens again: switching off and on gives no new money (D9).
    /// <para>
    /// <see cref="Settle"/> repeats safely: after it nothing is owed and the second call does nothing. It does not look at whether the feature is active (it runs
    /// from <c>OnDisabled</c>, when it already is not) and asks for no confirmation (a switch from code never asks). Player only: it works on the player's finances.
    /// </para>
    /// </summary>
    internal sealed class DisableSettlementEngine
    {
        /// <summary>The code of the journal record of the conversion.</summary>
        public const string Operation = "disable.convert";

        private readonly RecoveryEnvironment _env;
        private readonly ISettlementFlags _marks;

        public DisableSettlementEngine(RecoveryEnvironment env, ISettlementFlags marks)
        {
            if (env == null) throw new ArgumentNullException("env");
            if (marks == null) throw new ArgumentNullException("marks");
            _env = env;
            _marks = marks;
        }

        /// <summary>
        /// What the question shows now, or null when there is nothing to ask: the player's company is not there, or nothing is owed (no loans, nothing drawn on the
        /// line, no running investor contract with something left to pay). The hub then switches the feature off without a question.
        /// </summary>
        public DisablePlan Quote()
        {
            IPlayerFinance finance = _env.Finance;
            if (!finance.IsAvailable) return null;

            RecoveryState state = _env.State;
            DisableBreakdown parts = DisableSettlement.Breakdown(state.loans, DrawnOf(state.creditLine), state.investorShare);
            if (!(parts.Total > 0.0)) return null;

            double money = finance.Money;
            double limit = finance.PlayerDebtLimit;
            return new DisablePlan(parts, money, DisableSettlement.BalanceAfter(money, parts.Total), DisableSettlement.WouldDefault(money, parts.Total, limit), (state.loans ?? new List<LoanData>()).Count);
        }

        /// <summary>
        /// Turns the obligations into ordinary debt and closes them (see the class). Never throws for the state it is given; a failing ledger is the facade's
        /// to isolate.
        /// </summary>
        public SettleResult Settle()
        {
            var result = new SettleResult();
            RecoveryState state = _env.State;
            if (state.loans == null) state.loans = new List<LoanData>();
            if (state.creditLine == null) state.creditLine = new CreditLineData();
            if (state.investorShare == null) state.investorShare = new InvestorShareData();
            CreditLineData line = state.creditLine;
            bool lineOwes = DrawnOf(line) > 0.0;
            bool investorRuns = state.investorShare.active;
            bool owes = state.loans.Count > 0 || lineOwes || investorRuns;

            if (!owes)
            {
                SettlementMarks.Record(_marks, state, ClosedByDisable(line));
                result.Status = SettleStatus.NothingToSettle;
                return result;
            }

            DisableBreakdown parts = DisableSettlement.Breakdown(state.loans, DrawnOf(line), state.investorShare);
            if (parts.Total > 0.0 && (!_env.Finance.IsAvailable || !_env.Money.IsAvailable))
            {
                result.Status = SettleStatus.Unavailable;
                return result;
            }

            if (parts.Total > 0.0) _env.Money.CapitalOut(parts.Total, Operation, DetailsOf(state.loans.Count, lineOwes, parts.InvestorRemainder > 0.0));

            bool bridge = false;
            foreach (LoanData loan in state.loans)
            {
                if (string.Equals(loan.kind, LoanData.KindBridge, StringComparison.Ordinal)) bridge = true;
            }
            result.LoansClosed = state.loans.Count;
            result.BridgeClosed = bridge;
            state.loans.Clear();
            if (bridge) state.lastBridgeClosedOn = RecoveryDates.ToText(_env.Finance.Today);

            if (lineOwes)
            {
                line.state = CreditLineData.StateClosed;
                line.closedReason = CreditLineData.ReasonDisabled;
                line.drawn = 0.0;
                result.LineClosed = true;
            }

            if (investorRuns)
            {
                state.investorShare.active = false;
                result.InvestorClosed = true;
            }

            SettlementMarks.Record(_marks, state, ClosedByDisable(line));
            result.Converted = parts.Total;
            result.Status = SettleStatus.Done;
            return result;
        }

        // ---- internals ----

        // The line is closed and the reason is a switch-off (this settlement or an earlier one): it never opens again.
        private static bool ClosedByDisable(CreditLineData line)
        {
            return line != null && line.state == CreditLineData.StateClosed && line.closedReason == CreditLineData.ReasonDisabled;
        }

        // Only a line that is open owes anything; a line that is none or closed has no debt, whatever a damaged file says.
        private static double DrawnOf(CreditLineData line)
        {
            return line != null && line.state == CreditLineData.StateOpen ? line.drawn : 0.0;
        }

        // "3 loans, line, investor": what the sum is made of, for the journal.
        private static string DetailsOf(int loans, bool line, bool investor)
        {
            string text = loans.ToString(CultureInfo.InvariantCulture) + (loans == 1 ? " loan" : " loans");
            if (line) text += ", line";
            if (investor) text += ", investor";
            return text;
        }
    }
}
