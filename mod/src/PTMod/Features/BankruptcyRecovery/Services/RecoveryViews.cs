using System;
using System.Collections.Generic;
using System.Globalization;
using PTMod.Core;
using PTMod.Features.BankruptcyRecovery.Logic;
using GameFormatter = ProcessorTycoon.StringFormatter;

namespace PTMod.Features.BankruptcyRecovery
{
    /// <summary>
    /// The small translations between the saved data of the feature and what the API and the windows show: the views of <see cref="RecoveryApi"/>, the
    /// names of the loan kinds, and the text of money and of a rate. Static, no state.
    /// </summary>
    internal static class RecoveryViews
    {
        /// <summary>The kind of a saved loan (the save stores the name of the member); false for a name the API does not know.</summary>
        public static bool TryKind(string name, out LoanKind kind)
        {
            return Enum.TryParse(name, false, out kind) && Enum.IsDefined(typeof(LoanKind), kind);
        }

        /// <summary>A saved loan as the API shows it. A kind that is not known is shown as a bridge loan (the codec drops such a loan when the save is read).</summary>
        public static LoanView ToView(LoanData loan)
        {
            LoanKind kind;
            if (!TryKind(loan.kind, out kind)) kind = LoanKind.Bridge;

            var view = new LoanView();
            view.Id = loan.id;
            view.Kind = kind;
            view.Outstanding = loan.outstanding;
            view.AnnualRate = loan.annualRate;
            view.Payment = loan.payment;
            view.MonthsLeft = loan.monthsLeft;
            return view;
        }

        /// <summary>The loans of the book as views, oldest first (a new list).</summary>
        public static List<LoanView> ToViews(IReadOnlyList<LoanData> loans)
        {
            var views = new List<LoanView>(loans.Count);
            for (int i = 0; i < loans.Count; i++)
            {
                LoanKind kind;
                if (!TryKind(loans[i].kind, out kind)) continue;
                views.Add(ToView(loans[i]));
            }
            return views;
        }

        /// <summary>The player's name of a kind of loan in the current language (<c>Bridge loan</c>).</summary>
        public static string KindName(string kindName)
        {
            switch (kindName)
            {
                case LoanData.KindBridge: return ModL10n.L("rec.loan.kind.bridge");
                case LoanData.KindTerm: return ModL10n.L("rec.loan.kind.term");
                case LoanData.KindRescueLoan: return ModL10n.L("rec.loan.kind.rescue");
                default: return kindName ?? string.Empty;
            }
        }

        /// <summary>A sum of money as the game writes it (<c>StringFormatter.FloatToMoney</c>, abbreviated, with the currency).</summary>
        public static string Money(double value)
        {
            return GameFormatter.FloatToMoney((float)value, string.Empty, true, true, true);
        }

        /// <summary>A sum of money in full, not abbreviated (the question before an operation and the balance after it).</summary>
        public static string MoneyExact(double value)
        {
            return GameFormatter.FloatToMoney((float)value, string.Empty, false, true, true);
        }

        /// <summary>A count (units in stock) as the game writes whole numbers: grouped, not abbreviated.</summary>
        public static string Count(int value)
        {
            try
            {
                return GameFormatter.FloatToIntString(value, string.Empty, false);
            }
            catch (Exception)
            {
                return value.ToString(CultureInfo.InvariantCulture);
            }
        }

        /// <summary>A game date as the game writes dates (<c>StringFormatter.DateToString</c>); the ISO text when the formatter is not ready.</summary>
        public static string GameDate(DateTime date)
        {
            try
            {
                return GameFormatter.DateToString(date);
            }
            catch (Exception)
            {
                return RecoveryDates.ToText(date);
            }
        }

        /// <summary>A rate as the text of a percent without the sign (<c>0.3</c> gives <c>30</c>, <c>0.125</c> gives <c>12.5</c>).</summary>
        public static string Percent(double fraction)
        {
            return (fraction * 100.0).ToString("0.##", CultureInfo.InvariantCulture);
        }

        /// <summary>A date for the API: <c>yyyy-MM-dd</c>.</summary>
        public static string Date(DateTime date)
        {
            return RecoveryDates.ToText(date);
        }
    }
}
