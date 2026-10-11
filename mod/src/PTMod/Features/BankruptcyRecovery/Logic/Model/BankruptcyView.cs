using System;

namespace PTMod.Features.BankruptcyRecovery.Logic
{
    // The small value types the rules of RecoveryRules read and return (design.md, "Чистая логика"). Plain data, no references to the game: the
    // adapter of the player's finances fills a BankruptcyView once per call, the rules never touch the game.

    /// <summary>
    /// The facts of one company's finances that the rules of the default cycle need (a snapshot taken by the caller). <see cref="DebtLimit"/> is
    /// the default threshold without a sign (<c>ModCredit.PlayerDebtLimit()</c>, the live value the game applies); the default starts when the
    /// balance is below <c>-DebtLimit</c>. A view whose limit is not a positive finite number has no usable threshold (<see cref="HasThreshold"/>
    /// is false), and every rule then stays out of the way: the feature never guesses a threshold.
    /// </summary>
    public readonly struct BankruptcyView
    {
        /// <summary>The balance of the company (<c>Company.MoneyAmount</c>).</summary>
        public readonly double Money;

        /// <summary>The default threshold without a sign, <c>D</c>: positive, in dollars.</summary>
        public readonly double DebtLimit;

        /// <summary>The company is in default now (<c>ICompany.IsInDefault</c>).</summary>
        public readonly bool InDefault;

        /// <summary>The company is bankrupt.</summary>
        public readonly bool IsBankrupt;

        /// <summary>The company is the player's. The rules of the feature work for the player only (FR-11): for any other company they say no.</summary>
        public readonly bool IsPlayer;

        /// <summary>Days in default so far (<c>ICompany.DaysInDefault</c>).</summary>
        public readonly int DaysInDefault;

        /// <summary>Days of default after which the game declares the bankruptcy (<c>BankruptcyHandler.defaultTimeLimit</c>, 120).</summary>
        public readonly int DefaultTimeLimit;

        /// <summary>The game date.</summary>
        public readonly DateTime Today;

        public BankruptcyView(double money, double debtLimit, bool inDefault, bool isBankrupt, int daysInDefault, int defaultTimeLimit,
            DateTime today, bool isPlayer)
        {
            Money = money;
            DebtLimit = debtLimit;
            InDefault = inDefault;
            IsBankrupt = isBankrupt;
            DaysInDefault = daysInDefault;
            DefaultTimeLimit = defaultTimeLimit;
            Today = today;
            IsPlayer = isPlayer;
        }

        /// <summary>The default threshold with its sign, a negative number (<c>-DebtLimit</c>), the way the balance is compared with it.</summary>
        public double Threshold
        {
            get { return -DebtLimit; }
        }

        /// <summary>The limit is a finite number above 0, so a threshold exists.</summary>
        public bool HasThreshold
        {
            get { return !double.IsNaN(DebtLimit) && !double.IsInfinity(DebtLimit) && DebtLimit > 0.0; }
        }

        /// <summary>The balance is a real number below the threshold (the game's condition of a default). False without a usable threshold.</summary>
        public bool IsBelowThreshold
        {
            get { return IsBelowShareOfLimit(1.0); }
        }

        /// <summary>
        /// The balance is a real number below <c>−share × D</c> (the warning line is 0.75 of the limit, the bridge loan line the same). False
        /// without a usable threshold. The rules read the limit only through this and the next method, so the number is compared the same way
        /// everywhere.
        /// </summary>
        public bool IsBelowShareOfLimit(double share)
        {
            return HasThreshold && !double.IsNaN(Money) && Money < -share * DebtLimit;
        }

        /// <summary>The balance is a real number at or above <c>−share × D</c>. False without a usable threshold.</summary>
        public bool IsAtOrAboveShareOfLimit(double share)
        {
            return HasThreshold && !double.IsNaN(Money) && Money >= -share * DebtLimit;
        }
    }

    /// <summary>What the pre-warning rule (FR-1) tells the monitor to do today.</summary>
    public enum PreWarningDecision
    {
        /// <summary>Nothing.</summary>
        None,

        /// <summary>Send the "default risk" letter and remember that it was sent in this episode.</summary>
        Send,

        /// <summary>The balance is back high enough: forget the episode, so the next fall sends a new letter.</summary>
        Reset,
    }

    /// <summary>
    /// The answer of <see cref="RecoveryRules.CanTakeBridge"/>: a bridge loan may be offered now, or not and why. <see cref="ReasonKey"/> is a key
    /// of the string tables (<c>rec.bridge.unavailable.*</c>) and is null when the answer is yes (and for a company the rules do not serve).
    /// </summary>
    public struct Eligibility
    {
        /// <summary>A bridge loan may be offered.</summary>
        public bool Ok;

        /// <summary>The key of the reason for a "no" (shown on the Loans tab); null for a "yes".</summary>
        public string ReasonKey;

        /// <summary>For the reason "cooldown": days left until the next bridge loan may be taken (the key takes it as <c>{0}</c>); otherwise 0.</summary>
        public int CooldownDaysLeft;

        /// <summary>A yes.</summary>
        public static Eligibility Yes()
        {
            return new Eligibility { Ok = true };
        }

        /// <summary>A no with the key of the reason.</summary>
        public static Eligibility No(string reasonKey)
        {
            return new Eligibility { Ok = false, ReasonKey = reasonKey };
        }
    }
}
