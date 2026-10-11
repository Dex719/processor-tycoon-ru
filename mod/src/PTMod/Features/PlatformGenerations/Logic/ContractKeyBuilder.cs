using System;
using System.Globalization;
using System.Text;

namespace PTMod.Features.PlatformGenerations
{
    /// <summary>
    /// The composite key of a contract (the game has no id for a contract): <c>Company|AcceptanceYear-AcceptanceMonth|Units|round(PaymentPerUnit)|ContractType</c>,
    /// for example <c>Apple|1993-1|52000|299|1</c> (the Glossary of contract-transparency, "ContractKey"). The feature keeps its own copy of the
    /// formula so as not to depend on that feature. The name of the client is escaped, so a <c>|</c> in it cannot move the other parts: <c>\</c> becomes
    /// <c>\\</c> and <c>|</c> becomes <c>\|</c>. The key is stable: the same contract gives the same text in every session.
    /// </summary>
    public static class ContractKeyBuilder
    {
        /// <summary>The separator between the parts of the key.</summary>
        public const char Separator = '|';

        public static string Build(string company, int acceptanceYear, int acceptanceMonth, int units, float paymentPerUnit, int contractType)
        {
            var sb = new StringBuilder(48);
            Escape(sb, company);
            sb.Append(Separator);
            sb.Append(acceptanceYear.ToString(CultureInfo.InvariantCulture)).Append('-').Append(acceptanceMonth.ToString(CultureInfo.InvariantCulture));
            sb.Append(Separator);
            sb.Append(units.ToString(CultureInfo.InvariantCulture));
            sb.Append(Separator);
            sb.Append(RoundPayment(paymentPerUnit).ToString(CultureInfo.InvariantCulture));
            sb.Append(Separator);
            sb.Append(contractType.ToString(CultureInfo.InvariantCulture));
            return sb.ToString();
        }

        /// <summary>The payment as a whole number (halves go away from zero); 0 for NaN and infinities.</summary>
        private static long RoundPayment(float payment)
        {
            if (float.IsNaN(payment) || float.IsInfinity(payment)) return 0;
            return (long)Math.Round((double)payment, MidpointRounding.AwayFromZero);
        }

        private static void Escape(StringBuilder sb, string text)
        {
            if (string.IsNullOrEmpty(text)) return;

            foreach (char c in text)
            {
                if (c == '\\' || c == Separator) sb.Append('\\');
                sb.Append(c);
            }
        }
    }
}
