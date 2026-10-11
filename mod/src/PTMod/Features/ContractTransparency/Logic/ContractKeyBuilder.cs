using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace PTMod.Features.ContractTransparency
{
    /// <summary>The five parts a contract key is made of (<see cref="ContractKeyBuilder.TryParse"/>).</summary>
    public struct ContractKeyParts
    {
        /// <summary>The client (<c>Contract.Company</c>), cut to <see cref="ContractKeyBuilder.MaxClientLength"/> characters.</summary>
        public string Client;

        /// <summary>The year of the review date (<c>Contract.AcceptanceDate.Year</c>).</summary>
        public int Year;

        /// <summary>The month of the review date, 1..12.</summary>
        public int Month;

        /// <summary><c>Contract.Units</c>.</summary>
        public int Units;

        /// <summary><c>Contract.PaymentPerUnit</c>.</summary>
        public double Payment;

        /// <summary><c>(int)Contract.ContractType</c>.</summary>
        public int ContractType;
    }

    /// <summary>
    /// The key that ties log entries to a contract (design.md, D7). A contract of the game has no id of its own: <c>ContractID</c> is the index of a template in
    /// the list of a decade and moves with the order of the pool, and a reference to the object does not survive a load. So the key is made of what is
    /// stable and what tells two contracts apart: <c>client|review year-month|units|payment|type</c>, for example <c>Apple|1986-1|52000|299|1</c>.
    /// A <c>|</c> or a backslash in the name of the client is written with a backslash before it, so the key can always be taken apart again.
    /// The key is at most about 130 characters, the log's own limit is 128: the client is cut to <see cref="MaxClientLength"/> so the parts after it are never lost.
    /// </summary>
    public static class ContractKeyBuilder
    {
        /// <summary>The separator of the parts.</summary>
        public const char Separator = '|';

        /// <summary>The character that escapes <see cref="Separator"/> and itself.</summary>
        public const char EscapeChar = '\\';

        /// <summary>The client is cut to this many characters (before escaping), so the whole key fits the limit of the log's strings.</summary>
        public const int MaxClientLength = 40;

        private const int PartCount = 5;

        /// <summary>
        /// The key of a contract. <paramref name="client"/> may be null (an empty part); the payment is written with at most two decimals and never in
        /// exponent form; a payment that is not a finite number is written as 0.
        /// </summary>
        public static string Build(string client, int reviewYear, int reviewMonth, int units, float paymentPerUnit, int contractType)
        {
            string name = TextSafe.Clean(client, MaxClientLength);
            double payment = float.IsNaN(paymentPerUnit) || float.IsInfinity(paymentPerUnit) ? 0.0 : paymentPerUnit;

            var sb = new StringBuilder(name.Length + 32);
            AppendEscaped(sb, name);
            sb.Append(Separator);
            sb.Append(reviewYear.ToString(CultureInfo.InvariantCulture)).Append('-').Append(reviewMonth.ToString(CultureInfo.InvariantCulture));
            sb.Append(Separator);
            sb.Append(units.ToString(CultureInfo.InvariantCulture));
            sb.Append(Separator);
            sb.Append(payment.ToString("0.##", CultureInfo.InvariantCulture));
            sb.Append(Separator);
            sb.Append(contractType.ToString(CultureInfo.InvariantCulture));
            return sb.ToString();
        }

        /// <summary>The text with <c>|</c> and the backslash escaped, as the client is written into a key.</summary>
        public static string Escape(string text)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;
            var sb = new StringBuilder(text.Length + 2);
            AppendEscaped(sb, text);
            return sb.ToString();
        }

        /// <summary>
        /// Takes a key apart. False for text that is not a key made by <see cref="Build"/> (a wrong number of parts, a part that is not a number, a month
        /// outside 1..12, an escape at the very end). Never throws.
        /// </summary>
        public static bool TryParse(string key, out ContractKeyParts parts)
        {
            parts = default(ContractKeyParts);
            if (string.IsNullOrEmpty(key)) return false;

            List<string> fields = Split(key);
            if (fields == null || fields.Count != PartCount) return false;

            string[] review = fields[1].Split('-');
            int year, month, units, type;
            double payment;
            if (review.Length != 2) return false;
            if (!int.TryParse(review[0], NumberStyles.None, CultureInfo.InvariantCulture, out year)) return false;
            if (!int.TryParse(review[1], NumberStyles.None, CultureInfo.InvariantCulture, out month) || month < 1 || month > 12) return false;
            if (!int.TryParse(fields[2], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out units)) return false;
            if (!double.TryParse(fields[3], NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out payment)) return false;
            if (!int.TryParse(fields[4], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out type)) return false;

            parts = new ContractKeyParts { Client = fields[0], Year = year, Month = month, Units = units, Payment = payment, ContractType = type };
            return true;
        }

        private static void AppendEscaped(StringBuilder sb, string text)
        {
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (c == Separator || c == EscapeChar) sb.Append(EscapeChar);
                sb.Append(c);
            }
        }

        /// <summary>The fields of a key between the unescaped separators, with the escapes removed; null when an escape ends the text.</summary>
        private static List<string> Split(string key)
        {
            var fields = new List<string>(PartCount);
            var current = new StringBuilder();
            for (int i = 0; i < key.Length; i++)
            {
                char c = key[i];
                if (c == EscapeChar)
                {
                    if (i + 1 >= key.Length) return null;
                    current.Append(key[++i]);
                }
                else if (c == Separator)
                {
                    fields.Add(current.ToString());
                    current.Length = 0;
                }
                else
                {
                    current.Append(c);
                }
            }
            fields.Add(current.ToString());
            return fields;
        }
    }
}
