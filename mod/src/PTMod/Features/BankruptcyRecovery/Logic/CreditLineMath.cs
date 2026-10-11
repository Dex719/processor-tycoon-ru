using System;

namespace PTMod.Features.BankruptcyRecovery.Logic
{
    /// <summary>
    /// The arithmetic of the startup credit line (FR-7; design.md, "Стартовая кредитная линия") and of the base the limits of the feature are
    /// counted from. Pure functions in <c>double</c>; the numbers come from <see cref="StartupCreditConfig"/>. Notation of the design:
    /// <c>D</c> is the default threshold without a sign (<c>ModCredit.PlayerDebtLimit()</c>), <c>I</c> the inflation, <c>D_L</c> the base of the
    /// limits (<see cref="LimitBase"/>), <c>R_3</c> the revenue of the last three full months. Input that is not a finite number is read as 0
    /// (a damaged value gives no credit instead of an exception); a limit or an amount is never negative.
    /// </summary>
    public static class CreditLineMath
    {
        /// <summary>
        /// The base of the limits of the mod, <c>D_L = max(D, floorBase × I)</c>: the early-game threshold of the bank is small (even the floor of
        /// the bank, 1 million), so the floor keeps the limits worth having (D = 1 million, I = 2.0, floorBase = 2 million gives 4 million).
        /// </summary>
        public static double LimitBase(double debtLimit, double inflation, double floorBase)
        {
            return Math.Max(NonNegative(debtLimit), NonNegative(floorBase) * NonNegative(inflation));
        }

        /// <summary>
        /// The limit of the line for the difficulty level: <c>L = clamp(a_d·D_L + revenueShare·R_3, 0, capDebtLimitShare·D_L)</c>. A level outside
        /// 0..5 is read as the nearest level. <paramref name="r3"/> below 0 counts as 0.
        /// </summary>
        public static double Limit(StartupCreditConfig c, int difficulty, double limitBase, double r3)
        {
            if (c == null) c = new StartupCreditConfig();
            double dl = NonNegative(limitBase);
            double raw = Share(c.baseDebtLimitShareByDifficulty, difficulty) * dl + NonNegative(c.revenueShare) * NonNegative(r3);
            double cap = NonNegative(c.capDebtLimitShare) * dl;
            return NonNegative(Math.Min(raw, cap));
        }

        /// <summary>What can still be drawn: the limit minus the drawn part, never below 0.</summary>
        public static double Available(double limit, double drawn)
        {
            double free = NonNegative(limit) - NonNegative(drawn);
            return free > 0.0 ? free : 0.0;
        }

        /// <summary>
        /// The auto draw: when the balance is below <c>−autoDrawTriggerShare × D</c> the line gives <c>min(available, −autoDrawTargetShare × D −
        /// money)</c>, which lifts the balance to <c>−autoDrawTargetShare × D</c> if the free limit allows. 0 when the balance is not below the
        /// trigger, nothing is free, or <paramref name="debtLimit"/> is not a positive number. Example: D = 8.4 million, balance −7.7 million
        /// (trigger −7.56 million) gives 2.66 million.
        /// </summary>
        public static double AutoDraw(StartupCreditConfig c, double money, double debtLimit, double available)
        {
            if (c == null) c = new StartupCreditConfig();
            double d = NonNegative(debtLimit);
            if (!(d > 0.0) || double.IsNaN(money) || double.IsInfinity(money)) return 0.0;
            if (!(money < -NonNegative(c.autoDrawTriggerShare) * d)) return 0.0;

            double free = NonNegative(available);
            if (!(free > 0.0)) return 0.0;

            double need = -NonNegative(c.autoDrawTargetShare) * d - money;
            if (!(need > 0.0)) return 0.0;
            return Math.Min(free, need);
        }

        /// <summary>
        /// The monthly repayment from a positive balance: <c>min(drawn, sweepShare × money)</c>. 0 when the balance is not above 0 or nothing is drawn.
        /// </summary>
        public static double Sweep(StartupCreditConfig c, double money, double drawn)
        {
            if (c == null) c = new StartupCreditConfig();
            if (!(money > 0.0) || double.IsInfinity(money)) return 0.0;
            double owed = NonNegative(drawn);
            if (!(owed > 0.0)) return 0.0;
            return Math.Min(owed, NonNegative(c.sweepShare) * money);
        }

        /// <summary>
        /// The day the line closes: the foundation date plus <c>years</c> years (the time of day is dropped). The window is
        /// <c>foundation ≤ day &lt; ClosesOn</c>.
        /// </summary>
        public static DateTime ClosesOn(StartupCreditConfig c, DateTime foundation)
        {
            int years = c == null ? new StartupCreditConfig().years : c.years;
            DateTime start = foundation.Date;
            try
            {
                return start.AddYears(years);
            }
            catch (ArgumentOutOfRangeException)
            {
                return DateTime.MaxValue.Date;
            }
        }

        /// <summary>
        /// The company may still have the line: <c>foundation ≤ today &lt; foundation + years</c> (the day of the foundation counts, the day three
        /// years later does not). Compared by date.
        /// </summary>
        public static bool IsWindowOpen(StartupCreditConfig c, DateTime foundation, DateTime today)
        {
            DateTime day = today.Date;
            return foundation.Date <= day && day < ClosesOn(c, foundation);
        }

        /// <summary>The line has run out of time: <paramref name="today"/> is the closing day or later (new draws are closed from then on).</summary>
        public static bool HasExpired(DateTime closesOn, DateTime today)
        {
            return today.Date >= closesOn.Date;
        }

        // The share a_d of the level; the array is checked by RecoveryBalance, but a missing one gives the defaults of the table.
        private static double Share(double[] byDifficulty, int difficulty)
        {
            double[] table = byDifficulty != null && byDifficulty.Length == RecoveryBalance.DifficultyLevels
                ? byDifficulty
                : new StartupCreditConfig().baseDebtLimitShareByDifficulty;
            int level = difficulty < 0 ? 0 : (difficulty >= table.Length ? table.Length - 1 : difficulty);
            return NonNegative(table[level]);
        }

        private static double NonNegative(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value) || value < 0.0) return 0.0;
            return value;
        }
    }
}
