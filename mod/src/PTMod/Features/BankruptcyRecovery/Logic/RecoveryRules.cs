using System;

namespace PTMod.Features.BankruptcyRecovery.Logic
{
    /// <summary>
    /// The decisions of the default cycle that need no game: when the last-chance offer holds the bankruptcy counter (FR-8), when the warning
    /// letter is sent and forgotten (FR-1), how the streak of the early exit counts (FR-9) and whether a bridge loan may be offered (FR-6).
    /// Pure functions of a <see cref="BankruptcyView"/> (a snapshot the caller takes) and the numbers of the balance. Every rule says no for a
    /// company that is not the player's (FR-11), for a bankrupt company and, as far as it compares the balance with the threshold, when the view has
    /// no usable threshold (<see cref="BankruptcyView.HasThreshold"/>): when in doubt the feature stays out of the game's way. The sub-feature switches are not here: the caller asks only for the rules of the
    /// parts that are on.
    /// </summary>
    public static class RecoveryRules
    {
        /// <summary>Reason key: the player is bankrupt, no new loans.</summary>
        public const string ReasonBankrupt = "rec.bridge.unavailable.bankrupt";

        /// <summary>Reason key: a bridge loan is already running.</summary>
        public const string ReasonActive = "rec.bridge.unavailable.active";

        /// <summary>Reason key: the pause after the previous bridge loan has not passed (the days left go in <see cref="Eligibility.CooldownDaysLeft"/>).</summary>
        public const string ReasonCooldown = "rec.bridge.unavailable.cooldown";

        /// <summary>Reason key: the balance is not close enough to the threshold and the company is not in default.</summary>
        public const string ReasonNotNeeded = "rec.bridge.unavailable.notNeeded";

        /// <summary>
        /// The bankruptcy counter of the player has to be held back today (P1): the player is in default, tomorrow's step would reach
        /// <c>DaysInDefault + 1 ≥ DefaultTimeLimit</c>, the balance is still below the threshold, fewer offers than allowed were accepted and the
        /// player did not decline in this episode. Always false for another company, a bankrupt one, for <paramref name="bailoutsAllowed"/> of 0
        /// (the "impossible" level) and without a positive <see cref="BankruptcyView.DefaultTimeLimit"/>.
        /// </summary>
        public static bool MustHoldForBailout(in BankruptcyView v, int bailoutsUsed, int bailoutsAllowed, bool declined)
        {
            if (!v.IsPlayer || v.IsBankrupt || !v.InDefault || declined) return false;
            if (bailoutsUsed >= bailoutsAllowed) return false;
            if (v.DefaultTimeLimit < 1) return false;
            if ((long)v.DaysInDefault + 1L < v.DefaultTimeLimit) return false;
            return v.IsBelowThreshold;
        }

        /// <summary>
        /// The warning rule of one day. <see cref="PreWarningDecision.Send"/>: not in default, the balance below <c>−thresholdDebtLimitShare × D</c>
        /// and no letter sent in this episode. <see cref="PreWarningDecision.Reset"/>: a letter was sent and the balance is back at or above
        /// <c>−resetDebtLimitShare × D</c>. Otherwise <see cref="PreWarningDecision.None"/> (no repeat while the episode lasts).
        /// </summary>
        public static PreWarningDecision PreWarning(in BankruptcyView v, bool alreadySent, PreWarningConfig c)
        {
            if (!v.IsPlayer || v.IsBankrupt) return PreWarningDecision.None;
            if (c == null) c = new PreWarningConfig();

            if (alreadySent)
                return v.IsAtOrAboveShareOfLimit(c.resetDebtLimitShare) ? PreWarningDecision.Reset : PreWarningDecision.None;
            if (v.InDefault) return PreWarningDecision.None;
            return v.IsBelowShareOfLimit(c.thresholdDebtLimitShare) ? PreWarningDecision.Send : PreWarningDecision.None;
        }

        /// <summary>
        /// The streak of days in a row that a company in default spends at or above the threshold (P2): <c>streak + 1</c> for such a day, 0 for any
        /// other (below the threshold, not in default, not the player). A negative <paramref name="streak"/> counts as 0.
        /// </summary>
        public static int NextRecoveryStreak(in BankruptcyView v, int streak)
        {
            if (!v.IsPlayer || v.IsBankrupt || !v.InDefault || !v.IsAtOrAboveShareOfLimit(1.0)) return 0;
            if (streak < 0) streak = 0;
            return streak == int.MaxValue ? streak : streak + 1;
        }

        /// <summary>The streak is long enough for the default to be cancelled: <c>streak ≥ streakDays</c> (at least one day).</summary>
        public static bool IsStreakComplete(int streak, EarlyExitConfig c)
        {
            int days = c == null ? new EarlyExitConfig().streakDays : (c.streakDays < 1 ? 1 : c.streakDays);
            return streak >= days;
        }

        /// <summary>
        /// May a bridge loan be offered now: the player is not bankrupt, has no bridge loan running, the pause <c>cooldownDays</c> since the last
        /// one was closed (<paramref name="lastClosed"/>, null for none) has passed, and the company is in default or its balance is below
        /// <c>−eligibleDebtLimitShare × D</c>. The first reason that fails is returned, in that order.
        /// </summary>
        public static Eligibility CanTakeBridge(in BankruptcyView v, bool hasActiveBridge, DateTime? lastClosed, BridgeConfig c)
        {
            if (!v.IsPlayer) return Eligibility.No(null);
            if (c == null) c = new BridgeConfig();
            if (v.IsBankrupt) return Eligibility.No(ReasonBankrupt);
            if (hasActiveBridge) return Eligibility.No(ReasonActive);

            if (lastClosed.HasValue)
            {
                int passed = (v.Today.Date - lastClosed.Value.Date).Days;
                if (passed < c.cooldownDays)
                {
                    Eligibility wait = Eligibility.No(ReasonCooldown);
                    wait.CooldownDaysLeft = c.cooldownDays - passed;
                    return wait;
                }
            }

            bool needed = v.InDefault || v.IsBelowShareOfLimit(c.eligibleDebtLimitShare);
            return needed ? Eligibility.Yes() : Eligibility.No(ReasonNotNeeded);
        }
    }
}
