using System;
using System.Globalization;

namespace PTMod.Features.RivalAcquisitions
{
    /// <summary>The tone of a line of the interface: the window paints it with the colours of the theme.</summary>
    public enum ViewTone
    {
        Neutral = 0,
        Positive = 1,
        Negative = 2,
        Warning = 3,
    }

    /// <summary>
    /// How the pure models of the windows turn keys and numbers into text (design.md "UI"; NFR-4). The window hands in <c>ModL10n.L</c> and the
    /// game's money formatter; the tests hand in fakes. A member left null falls back to a plain invariant text, so a model can always be built.
    /// No sentence is composed anywhere else than in the tables (<c>L10n/*/rival-acquisitions.json</c>).
    /// </summary>
    public sealed class ViewFormat
    {
        private static readonly string[] NoArgs = new string[0];

        /// <summary>The text of a key with the arguments filled in (<c>ModL10n.L</c>).</summary>
        public Func<string, string[], string> Text;

        /// <summary>An amount of dollars as the game writes money (<c>StringFormatter.FloatToMoney</c>).</summary>
        public Func<double, string> Money;

        /// <summary>A share, 0 to 1, as a number of percent with the sign ("25 %").</summary>
        public Func<double, string> Percent;

        /// <summary>A plain number such as a multiplier ("1.5").</summary>
        public Func<double, string> Number;

        public string T(string key, params string[] args)
        {
            if (Text != null) return Text(key, args ?? NoArgs) ?? key;
            return args == null || args.Length == 0 ? key : key + "(" + string.Join(", ", args) + ")";
        }

        public string M(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value)) value = 0.0;
            if (Money != null) return Money(value);
            return value.ToString("F0", CultureInfo.InvariantCulture);
        }

        public string P(double share)
        {
            if (double.IsNaN(share) || double.IsInfinity(share)) share = 0.0;
            if (Percent != null) return Percent(share);
            return (share * 100.0).ToString("0.#", CultureInfo.InvariantCulture) + " %";
        }

        public string N(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value)) value = 0.0;
            if (Number != null) return Number(value);
            return value.ToString("0.##", CultureInfo.InvariantCulture);
        }

        public string I(int value)
        {
            return value.ToString(CultureInfo.InvariantCulture);
        }
    }

    /// <summary>
    /// The text of a reason key of the logic (<see cref="AcqKeys"/>) with its argument: the keys whose argument is an amount of dollars (a shortfall, a
    /// new price, a needed injection) get it as money in the game's format, the others (a date) get it as it is. One place for the windows and the mails.
    /// </summary>
    public static class ReasonText
    {
        public static string Of(ViewFormat f, string key, string arg)
        {
            if (f == null) f = new ViewFormat();
            if (string.IsNullOrEmpty(key)) key = AcqKeys.ReasonUnsupported;
            if (arg == null) return f.T(key);

            if (IsMoneyArgument(key))
            {
                double amount;
                if (double.TryParse(arg, NumberStyles.Float, CultureInfo.InvariantCulture, out amount)) return f.T(key, f.M(amount));
            }
            return f.T(key, arg);
        }

        /// <summary>The argument of this key is a whole number of dollars.</summary>
        public static bool IsMoneyArgument(string key)
        {
            return key == AcqKeys.ReasonNoCash || key == AcqKeys.ReasonPriceChanged || key == AcqKeys.RescueOverLimit || key == AcqKeys.RescueNoCash;
        }
    }
}
