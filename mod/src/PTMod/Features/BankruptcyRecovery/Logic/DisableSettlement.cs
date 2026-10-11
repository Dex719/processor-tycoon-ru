using System;
using System.Collections.Generic;

namespace PTMod.Features.BankruptcyRecovery.Logic
{
    /// <summary>What turns into ordinary debt when the feature is switched off, by source (<see cref="DisableSettlement.Breakdown"/>).</summary>
    public struct DisableBreakdown
    {
        /// <summary>The outstanding principal of all loans (bridge, term, rescue).</summary>
        public double Loans;

        /// <summary>The drawn part of the startup credit line.</summary>
        public double LineDrawn;

        /// <summary>What the investor may still take: the ceiling minus the paid part, for a running contract.</summary>
        public double InvestorRemainder;

        /// <summary>The sum of the three parts.</summary>
        public double Total;
    }

    /// <summary>
    /// The arithmetic of the safe switch-off (FR-12 items 3-5; design.md, "Выключение фичи"): which sum becomes ordinary debt of the player's
    /// balance when the feature goes off, and what the balance would be after that (the confirmation in the hub shows both and warns about a
    /// default). Pure functions on the state records; no game, no side effects.
    /// </summary>
    public static class DisableSettlement
    {
        /// <summary>
        /// The sum that becomes ordinary debt: the outstanding amounts of the loans, plus the drawn part of the credit line, plus
        /// <c>max(0, cap − paid)</c> of a running investor share contract. A null list or contract counts as nothing; a part that is not a
        /// positive finite number counts as 0. The empty state gives 0 (the hub asks no question then).
        /// </summary>
        public static double Total(IReadOnlyList<LoanData> loans, double lineDrawn, InvestorShareData investor)
        {
            return Breakdown(loans, lineDrawn, investor).Total;
        }

        /// <summary>The same sum split by source; <see cref="DisableBreakdown.Total"/> is the sum of the three parts.</summary>
        public static DisableBreakdown Breakdown(IReadOnlyList<LoanData> loans, double lineDrawn, InvestorShareData investor)
        {
            var result = new DisableBreakdown();

            if (loans != null)
            {
                for (int i = 0; i < loans.Count; i++)
                {
                    LoanData loan = loans[i];
                    if (loan != null) result.Loans += Positive(loan.outstanding);
                }
            }

            result.LineDrawn = Positive(lineDrawn);

            if (investor != null && investor.active) result.InvestorRemainder = Positive(Positive(investor.cap) - Positive(investor.paid));

            result.Total = result.Loans + result.LineDrawn + result.InvestorRemainder;
            return result;
        }

        /// <summary>The balance after the sum is taken from it: <c>money − total</c> (what the confirmation shows as "balance after").</summary>
        public static double BalanceAfter(double money, double total)
        {
            return money - Positive(total);
        }

        /// <summary>
        /// The balance after the switch-off is below the default threshold (<c>money − total &lt; −debtLimit</c>), so the confirmation adds the
        /// warning about the default. False when the threshold is not a positive finite number or the balance is not a number.
        /// </summary>
        public static bool WouldDefault(double money, double total, double debtLimit)
        {
            if (double.IsNaN(debtLimit) || double.IsInfinity(debtLimit) || !(debtLimit > 0.0)) return false;
            double after = BalanceAfter(money, total);
            return !double.IsNaN(after) && after < -debtLimit;
        }

        private static double Positive(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value) || value < 0.0) return 0.0;
            return value;
        }
    }
}
