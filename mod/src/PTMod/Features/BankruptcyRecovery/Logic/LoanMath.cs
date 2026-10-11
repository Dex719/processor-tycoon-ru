using System;

namespace PTMod.Features.BankruptcyRecovery.Logic
{
    /// <summary>One month of a loan: what the payment splits into and what is left (<see cref="LoanMath.MonthStep"/>).</summary>
    public struct LoanStep
    {
        /// <summary>The interest of the month; goes to the report as the expense item "Interest".</summary>
        public double Interest;

        /// <summary>The part of the payment that repays the principal; a capital operation, not an expense.</summary>
        public double Principal;

        /// <summary>What is still owed on the principal after this month; 0 when the loan is repaid.</summary>
        public double OutstandingAfter;

        /// <summary>The whole payment of the month: interest plus principal.</summary>
        public double Total
        {
            get { return Interest + Principal; }
        }
    }

    /// <summary>
    /// The arithmetic of the annuity loans of the feature (FR-6, FR-7, FR-8; design.md, "Аннуитет"): the monthly payment, the interest of a month
    /// and the split of a payment. Pure functions in <c>double</c> (NFR-2: only the result goes to the game as a <c>float</c>); no rounding to
    /// cents, so a schedule repays the principal to within a fraction of a cent. The rate is a yearly fraction (0.30 is 30 percent) that the
    /// caller has already multiplied by the <c>bank.interest</c> channel (<see cref="EffectiveRate"/>). Input that makes no sense (a principal
    /// that is not a positive finite number) gives 0 instead of an exception: the services check the payment before they open a loan.
    /// </summary>
    public static class LoanMath
    {
        // Below this monthly rate the closed formula loses its digits (1 - (1 + i)^-n gets tiny); the plain division is exact enough.
        private const double NegligibleMonthlyRate = 1e-12;

        /// <summary>
        /// The yearly rate after the <c>bank.interest</c> multiplier: <c>annualRate × multiplier</c>. A rate or a multiplier that is not a finite
        /// number of 0 or more gives 0 (a broken channel never produces a negative interest or a bonus).
        /// </summary>
        public static double EffectiveRate(double annualRate, double multiplier)
        {
            if (!IsNonNegative(annualRate) || !IsNonNegative(multiplier)) return 0.0;
            double rate = annualRate * multiplier;
            return IsNonNegative(rate) ? rate : 0.0;
        }

        /// <summary>
        /// The fixed monthly payment of an annuity: <c>P = A·i / (1 − (1 + i)^−n)</c> with <c>i = annualRate / 12</c>; <c>A / n</c> when the rate is 0.
        /// 818 892 for 8 400 000 at 30 percent over 12 months. A term below 1 month is read as 1 (one payment repays everything with one month of
        /// interest). A principal that is not a positive finite number gives 0; a rate that is not a finite number of 0 or more is read as 0.
        /// </summary>
        public static double AnnuityPayment(double principal, double annualRate, int months)
        {
            if (!(principal > 0.0) || double.IsInfinity(principal)) return 0.0;
            int n = months < 1 ? 1 : months;
            double i = IsNonNegative(annualRate) ? annualRate / 12.0 : 0.0;
            if (i <= NegligibleMonthlyRate) return principal / n;

            double payment = principal * i / (1.0 - Math.Pow(1.0 + i, -n));
            return IsNonNegative(payment) ? payment : 0.0;
        }

        /// <summary>
        /// The interest of one month on <paramref name="outstanding"/>: <c>outstanding × annualRate / 12</c> (22 166.67 for 2 660 000 at 10 percent).
        /// Nothing owed, or a rate that is not a finite number of 0 or more, gives 0.
        /// </summary>
        public static double MonthlyInterest(double outstanding, double annualRate)
        {
            if (!(outstanding > 0.0) || double.IsInfinity(outstanding) || !IsNonNegative(annualRate)) return 0.0;
            return outstanding * annualRate / 12.0;
        }

        /// <summary>
        /// One monthly step of a loan: the interest on the outstanding amount (the current <paramref name="annualRate"/>, already multiplied by the
        /// channel) and the principal that the rest of <paramref name="payment"/> repays, never more than what is owed. The last payment
        /// (<paramref name="monthsLeft"/> of 1 or less) repays the whole remainder, whatever the payment says. When the interest alone is more
        /// than the payment (the rate went up after the loan was opened) the principal part is 0 for that month - the debt never grows.
        /// </summary>
        public static LoanStep MonthStep(double outstanding, double payment, double annualRate, int monthsLeft)
        {
            if (!(outstanding > 0.0) || double.IsInfinity(outstanding))
                return new LoanStep { Interest = 0.0, Principal = 0.0, OutstandingAfter = 0.0 };

            double interest = MonthlyInterest(outstanding, annualRate);
            double principal;
            if (monthsLeft <= 1)
            {
                principal = outstanding;
            }
            else
            {
                double room = IsNonNegative(payment) ? payment - interest : 0.0;
                principal = room > 0.0 ? Math.Min(outstanding, room) : 0.0;
            }

            return new LoanStep
            {
                Interest = interest,
                Principal = principal,
                OutstandingAfter = principal >= outstanding ? 0.0 : outstanding - principal,
            };
        }

        private static bool IsNonNegative(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value) && value >= 0.0;
        }
    }
}
