using System;
using System.Collections.Generic;
using System.Globalization;

namespace PTMod.Features.BankruptcyRecovery.Logic
{
    /// <summary>What <see cref="LoanBook.Prepay"/> did.</summary>
    internal enum PrepayResult
    {
        /// <summary>Paid back (all or a part); the money left the balance.</summary>
        Done,

        /// <summary>There is no loan with that id.</summary>
        NotFound,

        /// <summary>The amount is not a number above 0.</summary>
        InvalidAmount,

        /// <summary>The balance is lower than the amount to pay back (FR-10 item 3: the balance must cover it).</summary>
        NotEnoughMoney,

        /// <summary>The ledger or the player's company is not there.</summary>
        Unavailable,
    }

    /// <summary>
    /// The loans of the feature (design.md, <c>LoanService</c> without the game): the annuity loans of the bridge loan, the term loan the credit line turns
    /// into and the rescue loan. It registers a loan, takes the monthly payment of each loan on the first of the month, lets the player pay a loan back
    /// early, and says what is due next. The money of a payment goes through <see cref="IRecoveryMoney"/>: the interest as an expense of the report
    /// (item "Interest"), the principal as a capital operation, both even when the balance falls below zero (FR-6 item 3).
    /// <para>
    /// Opening a loan moves no money: the caller moves it (the bridge loan credits the amount, the rescue loan too, the term loan is a debt that
    /// already exists on the credit line). The state is read through the environment each time.
    /// </para>
    /// </summary>
    internal sealed class LoanBook
    {
        /// <summary>A loan was registered (after it is in the state).</summary>
        public event Action<LoanData> Opened;

        /// <summary>A loan was paid off, in its last payment or early (after it left the state).</summary>
        public event Action<LoanData> Closed;

        private const double PaidOffSlack = 0.005;   // half a cent: the remainder of a float calculation is not a debt

        private readonly RecoveryEnvironment _env;

        public LoanBook(RecoveryEnvironment env)
        {
            if (env == null) throw new ArgumentNullException("env");
            _env = env;
        }

        /// <summary>The loans, oldest first (the live list of the state; do not change it).</summary>
        public IReadOnlyList<LoanData> Loans
        {
            get { return _env.State.loans; }
        }

        /// <summary>What the player still owes on all loans.</summary>
        public double Outstanding
        {
            get
            {
                double total = 0.0;
                List<LoanData> loans = _env.State.loans;
                for (int i = 0; i < loans.Count; i++) total += loans[i].outstanding;
                return total;
            }
        }

        public LoanData Find(string id)
        {
            if (id == null) return null;
            List<LoanData> loans = _env.State.loans;
            for (int i = 0; i < loans.Count; i++)
            {
                if (string.Equals(loans[i].id, id, StringComparison.Ordinal)) return loans[i];
            }
            return null;
        }

        /// <summary>A loan of that kind (<see cref="LoanData.KindBridge"/> and the others) is running.</summary>
        public bool HasActive(string kind)
        {
            List<LoanData> loans = _env.State.loans;
            for (int i = 0; i < loans.Count; i++)
            {
                if (string.Equals(loans[i].kind, kind, StringComparison.Ordinal)) return true;
            }
            return false;
        }

        /// <summary>
        /// Registers an annuity loan: <paramref name="principal"/> over <paramref name="months"/> at <paramref name="annualRate"/> (before the multiplier of
        /// the channel <c>bank.interest</c>, which the monthly step applies and the payment of the schedule already contains at the moment of the opening).
        /// Returns the loan, or null when the numbers or the kind are not valid. The first payment falls due on the first day of the next month.
        /// </summary>
        public LoanData Open(string kind, double principal, double annualRate, int months)
        {
            string prefix = PrefixOf(kind);
            if (prefix == null) return null;
            if (double.IsNaN(principal) || double.IsInfinity(principal) || !(principal > 0.0)) return null;
            if (double.IsNaN(annualRate) || double.IsInfinity(annualRate) || annualRate < 0.0 || annualRate > RecoveryStateCodec.MaxAnnualRate) return null;
            if (months < 1 || months > RecoveryStateCodec.MaxLoanMonths) return null;

            RecoveryState state = _env.State;
            double effective = LoanMath.EffectiveRate(annualRate, _env.Finance.InterestMultiplier);

            var loan = new LoanData
            {
                id = NextId(state, prefix),
                kind = kind,
                principal = principal,
                outstanding = principal,
                annualRate = annualRate,
                monthsTotal = months,
                monthsLeft = months,
                payment = LoanMath.AnnuityPayment(principal, effective, months),
                openedOn = RecoveryDates.ToText(_env.Finance.Today),
            };
            state.loans.Add(loan);

            Action<LoanData> handler = Opened;
            if (handler != null) handler(loan);
            return loan;
        }

        /// <summary>
        /// The first of the month: the payment of every loan. The interest of the month is taken on what is owed, the principal part follows the schedule,
        /// the last payment takes all that is left. A loan opened today pays from the next month on. A loan whose debt is gone leaves the book (and raises
        /// <see cref="Closed"/>). Nothing happens while the player's company or the ledger is not there.
        /// </summary>
        public void OnMonth()
        {
            RecoveryState state = _env.State;
            if (state.loans.Count == 0) return;
            if (!_env.Finance.IsAvailable || !_env.Money.IsAvailable) return;

            double multiplier = _env.Finance.InterestMultiplier;
            DateTime today = _env.Finance.Today;
            List<LoanData> closed = null;

            for (int i = 0; i < state.loans.Count; i++)
            {
                LoanData loan = state.loans[i];
                if (OpenedOn(loan, today)) continue;

                LoanStep step = LoanMath.MonthStep(loan.outstanding, loan.payment, LoanMath.EffectiveRate(loan.annualRate, multiplier), loan.monthsLeft);
                if (step.Interest > 0.0) _env.Money.Interest(step.Interest, "loan.interest", loan.id);
                if (step.Principal > 0.0) _env.Money.CapitalOut(step.Principal, "loan.principal", loan.id);

                loan.outstanding = step.OutstandingAfter;
                loan.monthsLeft = loan.monthsLeft > 0 ? loan.monthsLeft - 1 : 0;

                if (loan.outstanding <= PaidOffSlack || loan.monthsLeft <= 0)
                {
                    loan.outstanding = 0.0;
                    if (closed == null) closed = new List<LoanData>();
                    closed.Add(loan);
                    state.loans.RemoveAt(i);
                    i--;
                }
            }

            if (closed == null) return;
            for (int i = 0; i < closed.Count; i++) Finish(closed[i]);
        }

        /// <summary>
        /// Pays a loan back early, all of it or a part, without a penalty (FR-10 item 3). The amount is cut to what is owed; the balance has to cover it.
        /// A loan whose debt is gone leaves the book.
        /// </summary>
        public PrepayResult Prepay(string loanId, double amount)
        {
            LoanData loan = Find(loanId);
            if (loan == null) return PrepayResult.NotFound;
            if (double.IsNaN(amount) || double.IsInfinity(amount) || !(amount > 0.0)) return PrepayResult.InvalidAmount;
            if (!_env.Finance.IsAvailable || !_env.Money.IsAvailable) return PrepayResult.Unavailable;

            double pay = Math.Min(amount, loan.outstanding);
            if (!(pay > 0.0)) return PrepayResult.InvalidAmount;
            if (_env.Finance.Money < pay) return PrepayResult.NotEnoughMoney;

            _env.Money.CapitalOut(pay, "loan.prepay", loan.id);
            loan.outstanding -= pay;

            if (loan.outstanding <= PaidOffSlack)
            {
                loan.outstanding = 0.0;
                _env.State.loans.Remove(loan);
                Finish(loan);
            }
            return PrepayResult.Done;
        }

        /// <summary>What the next first of the month takes for all loans together (interest and principal), at today's rate multiplier.</summary>
        public double NextPayment()
        {
            List<LoanData> loans = _env.State.loans;
            if (loans.Count == 0) return 0.0;

            double multiplier = _env.Finance.InterestMultiplier;
            double total = 0.0;
            for (int i = 0; i < loans.Count; i++)
            {
                LoanData loan = loans[i];
                total += LoanMath.MonthStep(loan.outstanding, loan.payment, LoanMath.EffectiveRate(loan.annualRate, multiplier), loan.monthsLeft).Total;
            }
            return total;
        }

        /// <summary>What the next first of the month takes for one loan; 0 for an unknown id.</summary>
        public double NextPaymentOf(string loanId)
        {
            LoanData loan = Find(loanId);
            if (loan == null) return 0.0;
            double multiplier = _env.Finance.InterestMultiplier;
            return LoanMath.MonthStep(loan.outstanding, loan.payment, LoanMath.EffectiveRate(loan.annualRate, multiplier), loan.monthsLeft).Total;
        }

        // ---- internals ----

        // A loan that left the book: the cooldown of the bridge loan starts, then the subscribers hear of it.
        private void Finish(LoanData loan)
        {
            if (string.Equals(loan.kind, LoanData.KindBridge, StringComparison.Ordinal)) _env.State.lastBridgeClosedOn = RecoveryDates.ToText(_env.Finance.Today);

            Action<LoanData> handler = Closed;
            if (handler != null) handler(loan);
        }

        private static bool OpenedOn(LoanData loan, DateTime today)
        {
            DateTime opened;
            return RecoveryDates.TryParse(loan.openedOn, out opened) && opened.Date == today.Date;
        }

        private static string PrefixOf(string kind)
        {
            if (string.Equals(kind, LoanData.KindBridge, StringComparison.Ordinal)) return "bridge";
            if (string.Equals(kind, LoanData.KindTerm, StringComparison.Ordinal)) return "term";
            if (string.Equals(kind, LoanData.KindRescueLoan, StringComparison.Ordinal)) return "rescue";
            return null;
        }

        // bridge-1, term-2, rescue-3: one sequence for all kinds; a number that a loan of the list already carries is skipped, so a sequence that fell
        // behind the list (a damaged save) cannot hand out an id twice or two ids with the same number.
        private static string NextId(RecoveryState state, string prefix)
        {
            int seq = state.nextLoanSeq < 1 ? 1 : state.nextLoanSeq;
            while (NumberTaken(state, seq)) seq++;
            state.nextLoanSeq = seq + 1;
            return prefix + "-" + seq.ToString(CultureInfo.InvariantCulture);
        }

        private static bool NumberTaken(RecoveryState state, int number)
        {
            string suffix = "-" + number.ToString(CultureInfo.InvariantCulture);
            for (int i = 0; i < state.loans.Count; i++)
            {
                string id = state.loans[i].id;
                if (id != null && id.EndsWith(suffix, StringComparison.Ordinal)) return true;
            }
            return false;
        }
    }
}
