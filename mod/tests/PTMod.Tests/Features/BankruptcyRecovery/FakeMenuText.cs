using System;
using System.Globalization;
using PTMod.Features.BankruptcyRecovery.Logic;

namespace PTMod.Tests.Features.BankruptcyRecovery
{
    /// <summary>
    /// <see cref="IMenuText"/> for the tests of the windows' models: every text is its key with the arguments, so a test sees which key the model chose and which
    /// number it put in (<c>rec.summary.daysLeft(90)</c>). Money is <c>$7000000</c> (short) and <c>$7000000.00</c> (full), a rate is the number of percent, a date is
    /// <c>d:1975-01-01</c>. Internal, so the runner never discovers it.
    /// </summary>
    internal sealed class FakeMenuText : IMenuText
    {
        public string L(string key, params string[] args)
        {
            if (args == null || args.Length == 0) return key;
            return key + "(" + string.Join("|", args) + ")";
        }

        public string Money(double value)
        {
            return (value < 0.0 ? "-" : string.Empty) + "$" + Math.Abs(value).ToString("0.##", CultureInfo.InvariantCulture);
        }

        public string MoneyExact(double value)
        {
            return (value < 0.0 ? "-" : string.Empty) + "$" + Math.Abs(value).ToString("0.00", CultureInfo.InvariantCulture);
        }

        public string Percent(double fraction)
        {
            return (fraction * 100.0).ToString("0.##", CultureInfo.InvariantCulture);
        }

        public string Date(DateTime date)
        {
            return "d:" + date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }
    }
}
