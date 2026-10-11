namespace PTMod.Features.BankruptcyRecovery
{
    /// <summary>
    /// The state of the player's finances as the rescue menu summary shows it (<see cref="RecoveryApi.GetSummary"/>). A snapshot: it does not
    /// change after it was returned. Money is in dollars. <c>default(RecoverySummary)</c> (all zero, <see cref="InDefault"/> false, no date) is
    /// what the API answers while the feature is not active.
    /// </summary>
    public struct RecoverySummary
    {
        /// <summary>The player's balance (<c>Company.MoneyAmount</c>).</summary>
        public double Money;

        /// <summary>The default threshold without a sign: the default starts when the balance is below <c>-DebtLimit</c> (<c>ModCredit.PlayerDebtLimit()</c>).</summary>
        public double DebtLimit;

        /// <summary>How far the balance is below the default threshold, that is how much it has to grow to leave the default; 0 when it is not below.</summary>
        public double DeficitToThreshold;

        /// <summary>The sum of what the player still owes on the loans and the drawn part of the credit line of the feature.</summary>
        public double LoansOutstanding;

        /// <summary>The amount of the next monthly payment (all loans of the feature together); 0 when there is none.</summary>
        public double NextPayment;

        /// <summary>The player's company is in default now.</summary>
        public bool InDefault;

        /// <summary>Days left before the bankruptcy (<c>defaultTimeLimit - DaysInDefault</c>); meaningful only when <see cref="InDefault"/> is true.</summary>
        public int DaysToBankruptcy;

        /// <summary>The date of the next payment, <c>yyyy-MM-dd</c>; null or empty when there is no payment.</summary>
        public string NextPaymentDate;
    }

    /// <summary>One loan of the feature as <see cref="RecoveryApi.GetLoans"/> and the loan events show it. A snapshot.</summary>
    public struct LoanView
    {
        /// <summary>The loan id (<c>bridge-1</c>), unique in a save.</summary>
        public string Id;

        public LoanKind Kind;

        /// <summary>What the player still owes on the principal.</summary>
        public double Outstanding;

        /// <summary>The annual interest rate of the loan as a fraction (0.30 is 30 percent).</summary>
        public double AnnualRate;

        /// <summary>The monthly payment (interest and principal).</summary>
        public double Payment;

        /// <summary>How many monthly payments are left.</summary>
        public int MonthsLeft;
    }

    /// <summary>
    /// The startup credit line (FR-7) as <see cref="RecoveryApi.GetCreditLine"/> shows it. A snapshot. <c>default(CreditLineView)</c>
    /// (<see cref="Open"/> false) is what the API answers while the feature is not active or the line is not open.
    /// </summary>
    public struct CreditLineView
    {
        /// <summary>The line is open (not "none" and not "closed").</summary>
        public bool Open;

        /// <summary>The current limit of the line.</summary>
        public double Limit;

        /// <summary>The drawn part, the debt on the line.</summary>
        public double Drawn;

        /// <summary>What can still be drawn: the limit minus the drawn part, not below 0.</summary>
        public double Available;

        /// <summary>The date the line closes, <c>yyyy-MM-dd</c>; null or empty when it is not open.</summary>
        public string ClosesOn;

        /// <summary>The line draws money by itself when the balance gets close to the threshold.</summary>
        public bool AutoDraw;
    }
}
