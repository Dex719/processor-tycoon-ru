using System;

namespace PTMod.Features.BankruptcyRecovery.Logic
{
    /// <summary>
    /// The arithmetic of the "last chance" offer (FR-8; design.md, "Последний шанс"): how much the investor gives, how many offers a difficulty
    /// level allows, and what the investor takes each month. Pure functions in <c>double</c>; the numbers come from <see cref="BailoutConfig"/>.
    /// Input that is not a finite number is read as 0 (a damaged value gives no payment instead of an exception).
    /// </summary>
    public static class BailoutMath
    {
        /// <summary>
        /// The amount of the offer, <c>B = targetDebtLimitShare × D − money</c>: it lifts the balance to <c>+0.25 × D</c>, above the default
        /// threshold. 36 075 000 for D = 24 300 000 and a balance of −30 000 000. 0 when the balance is already at or above that level, when the
        /// threshold is not a positive number (no offer without a threshold) or the input is not a finite number.
        /// </summary>
        public static double Amount(BailoutConfig c, double money, double debtLimit)
        {
            if (c == null) c = new BailoutConfig();
            if (double.IsNaN(money) || double.IsInfinity(money)) return 0.0;
            double d = NonNegative(debtLimit);
            if (!(d > 0.0)) return 0.0;
            double amount = NonNegative(c.targetDebtLimitShare) * d - money;
            return amount > 0.0 ? amount : 0.0;
        }

        /// <summary>
        /// How many offers a game allows at this difficulty level: <c>perGameByDifficulty[level]</c>, <c>[2, 1, 1, 1, 1, 0]</c> by default (nothing
        /// on "impossible"). A level outside 0..5 is read as the nearest level; a missing or wrong table gives the defaults.
        /// </summary>
        public static int Allowed(BailoutConfig c, int difficulty)
        {
            int[] table = c != null && c.perGameByDifficulty != null && c.perGameByDifficulty.Length == RecoveryBalance.DifficultyLevels
                ? c.perGameByDifficulty
                : new BailoutConfig().perGameByDifficulty;
            int level = difficulty < 0 ? 0 : (difficulty >= table.Length ? table.Length - 1 : difficulty);
            return table[level] < 0 ? 0 : table[level];
        }

        /// <summary>The ceiling of everything the investor may take for an offer of <paramref name="amount"/>: <c>capMultiple × amount</c>.</summary>
        public static double InvestorCap(InvestorConfig c, double amount)
        {
            if (c == null) c = new InvestorConfig();
            return NonNegative(c.capMultiple) * NonNegative(amount);
        }

        /// <summary>
        /// This month's payment to the investor: <c>min(cap − paid, profitShare × max(0, monthProfit))</c>. A month with a loss pays 0 (the term still
        /// runs), and so does a contract that has reached its ceiling.
        /// </summary>
        public static double InvestorPayment(InvestorConfig c, double monthProfit, double paid, double cap)
        {
            if (c == null) c = new InvestorConfig();
            double profit = monthProfit > 0.0 && !double.IsInfinity(monthProfit) ? monthProfit : 0.0;
            double room = NonNegative(cap) - NonNegative(paid);
            if (!(room > 0.0) || !(profit > 0.0)) return 0.0;
            return Math.Min(room, NonNegative(c.profitShare) * profit);
        }

        private static double NonNegative(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value) || value < 0.0) return 0.0;
            return value;
        }
    }
}
