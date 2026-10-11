#if PTMOD_DEBUG
using System;
using System.Globalization;

namespace PTMod.Features.RivalAcquisitions
{
    /// <summary>
    /// The arithmetic of the debug actions of the feature (TSK-021; design.md "Ручные сценарии в игре"), kept apart from the game so the numbers are tested: how many
    /// dollars "N million" is, what balance "k × D" is for a company, which count of days in default the game accepts, and the day before today. The whole file is the debug
    /// build's (<c>PTMOD_DEBUG</c>): a release build has neither this class nor the actions that use it. The actions themselves are in
    /// <c>Debug/AcquisitionsDebugActions.cs</c>.
    /// </summary>
    public static class AcqDebugMath
    {
        /// <summary>The smallest, the largest and the step of the amount "give the player N million", and the amount the slider starts at (scenario M1: 2 000 million).</summary>
        public const int MinMillions = 100, MaxMillions = 5000, MillionsStep = 100, DefaultMillions = 2000;

        /// <summary>The smallest, the largest and the step of the factor k of "balance = k × D", and the factor the slider starts at (scenario M2: -1.2).</summary>
        public const double MinRatio = -2.0, MaxRatio = 2.0, RatioStep = 0.1, DefaultRatio = -1.2;

        /// <summary>
        /// The factor of "bankrupt" (scenario M3): the balance -100 × D with the last day of the grace period, so the game's own check at the end of the term finds the company
        /// bankrupt. Not the -1.2 of the first sketch: on the last day the game lets an AI company with a factory sell its production lines one by one until its balance
        /// is back to -0.75 × D (<c>AIBehaviourController.SellProductionLinesToAvoidBankruptcy</c>), which takes it out of default; a deficit that the whole factory cannot cover
        /// leaves the game no way out. Not limited to the range of the slider of <see cref="BalanceFor"/>.
        /// </summary>
        public const double BankruptRatio = -100.0;

        /// <summary>
        /// The last count of days in default that does not end the term at once: the game declares bankruptcy when the count reaches <see cref="DistressRules.DaysToBankruptcy"/>
        /// at the next day, so 119 means "bankrupt tomorrow".
        /// </summary>
        public static int LastDayBeforeBankruptcy
        {
            get { return DistressRules.DaysToBankruptcy - 1; }
        }

        /// <summary>"N million" in dollars. 0 for a count that is not a positive finite number.</summary>
        public static double Dollars(double millions)
        {
            if (double.IsNaN(millions) || double.IsInfinity(millions) || millions <= 0.0) return 0.0;
            return millions * 1000000.0;
        }

        /// <summary>
        /// The balance "k × D" for a company whose debt limit is <paramref name="debtLimit"/> (positive, <c>ModCredit.DebtLimitOf</c>): k is held to
        /// [<see cref="MinRatio"/>; <see cref="MaxRatio"/>]. 0 when the limit is not a positive finite number (a company without a bank has no D to count in).
        /// </summary>
        public static double BalanceFor(double ratio, double debtLimit)
        {
            if (double.IsNaN(ratio) || double.IsNaN(debtLimit) || double.IsInfinity(debtLimit) || debtLimit <= 0.0) return 0.0;
            double k = Math.Max(MinRatio, Math.Min(MaxRatio, ratio));
            return k * debtLimit;
        }

        /// <summary>The balance of "bankrupt" for the debt limit <paramref name="debtLimit"/>: <see cref="BankruptRatio"/> × D. 0 when the limit is not a positive finite number.</summary>
        public static double BankruptBalance(double debtLimit)
        {
            if (double.IsNaN(debtLimit) || double.IsInfinity(debtLimit) || debtLimit <= 0.0) return 0.0;
            return BankruptRatio * debtLimit;
        }

        /// <summary>The count of days in default for a requested number: rounded, held to [0; <see cref="LastDayBeforeBankruptcy"/>] (0 ends the default).</summary>
        public static int ClampDays(double days)
        {
            if (double.IsNaN(days) || double.IsInfinity(days)) return 0;
            double rounded = Math.Round(days, MidpointRounding.AwayFromZero);
            if (rounded < 0.0) return 0;
            if (rounded > LastDayBeforeBankruptcy) return LastDayBeforeBankruptcy;
            return (int)rounded;
        }

        /// <summary>
        /// The day before <paramref name="today"/> as the books of the feature write a date (<c>yyyy-MM-dd</c>): the "last day" an auction is given to make it expired
        /// (the auction is open through its last day). Empty when there is no clock.
        /// </summary>
        public static string YesterdayText(DateTime today)
        {
            if (today == default(DateTime)) return string.Empty;
            return today.Date.AddDays(-1).ToString(AcquisitionsState.DateFormat, CultureInfo.InvariantCulture);
        }
    }
}
#endif
