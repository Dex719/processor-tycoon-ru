using System;
using System.Collections.Generic;
using System.Globalization;

namespace PTMod.Features.RivalAcquisitions
{
    /// <summary>
    /// The verdict of <see cref="StakeMath.Rescue"/>: whether the rescue order can be carried out now, how much money goes into the target and which
    /// share of it the player gets for that money.
    /// </summary>
    public struct RescueDecision
    {
        public bool Ok;

        /// <summary>J: the money the player puts into the target's account (when refused for the limit, the money that would have been needed). 0 when it was not worked out.</summary>
        public double Injection;

        /// <summary>σ_J: the share of the target the injection buys (what the player owns after = owned + this). 0 when it was not worked out.</summary>
        public double ShareBought;

        /// <summary>The balance the target ends up with after the injection (strictly above its default threshold). Set whenever the injection was worked out.</summary>
        public double TargetBalanceAfter;

        /// <summary>A key of <see cref="AcqKeys"/> (<c>acq.rescue.reason.*</c>) when the rescue is refused; null when it can go ahead.</summary>
        public string ReasonKey;

        /// <summary>The argument of the reason text ({0}) as a whole number of dollars; null when the reason has none.</summary>
        public string ReasonArg;
    }

    /// <summary>
    /// <c>StakeMath</c> (FR-6, design.md "Доля и спасение"): the price of a minority stake, the dividend and the arithmetic of a rescue order. Pure.
    /// <list type="bullet">
    /// <item><b>Price</b> <c>P_σ = σ × max(E_min, E + C) × m_s × Ch</c> with <c>C</c> signed (a debt makes the stake cheaper, the floor is <c>E_min</c>) and <c>m_s</c>
    /// 1.0 for a healthy company, 0.6 for one in default. The money goes into the target's account, not to a seller, so there is no arbitrage.</item>
    /// <item><b>Dividend</b> <c>d = σ_p × payoutRatio × max(0, profit of last month)</c>, paid only if the target still has <c>C - d ≥ 0</c>.</item>
    /// <item><b>Rescue</b>: bring the target's balance to <c>B_r = rescueTargetDebtLimitShare × D</c> (strictly above the threshold <c>-D</c>), <c>J = B_r - C</c>,
    /// for the share <c>σ_J = J / (max(E_min, E + C) × 0.6 × Ch)</c>; carried out if <c>J ≤ limit</c>, the player keeps <c>minCashAfter</c> and the stake stays within <c>maxShare</c>.</item>
    /// </list>
    /// </summary>
    public static class StakeMath
    {
        /// <summary>Shares are compared with this tolerance so that 0.25 + 0.24 is not refused as 0.49000000000000005 against 0.49.</summary>
        private const double ShareTolerance = 1e-9;

        /// <summary>
        /// The price of a stake of <paramref name="share"/> in a target worth <paramref name="v"/> with the balance <paramref name="money"/> (C, signed).
        /// <paramref name="channel"/> is <c>acquisition.price</c>. A <paramref name="roundTo"/> above 1 rounds the price UP to a multiple of it (the
        /// deals round to <c>valuation.roundTo</c>); 0 leaves it exact. 0 for a share that is not positive.
        /// </summary>
        public static double StakePrice(double share, Valuation v, double money, bool inDefault, StakeConfig c, double channel, double roundTo = 0.0)
        {
            if (!IsPositive(share)) return 0.0;
            if (c == null) c = new StakeConfig();

            double basis = Math.Max(Clean(v.EMin), Clean(v.E + Finite(money)));
            double multiplier = inDefault ? c.DistressedMultiplier : c.HealthyMultiplier;
            double price = share * basis * multiplier * UsableChannel(channel);
            return DealRules.RoundUp(price, roundTo);
        }

        /// <summary>
        /// The dividend for last month: <c>ownedShare × payoutRatio × profit</c>, nothing for a loss, and nothing when the target could not pay it and
        /// stay at 0 or more (it is all or nothing, not a part).
        /// </summary>
        public static double Dividend(double share, double prevMonthProfit, double targetMoney, StakeConfig c)
        {
            if (!IsPositive(share) || !(prevMonthProfit > 0.0) || double.IsInfinity(prevMonthProfit)) return 0.0;
            if (c == null) c = new StakeConfig();

            double dividend = share * c.PayoutRatio * prevMonthProfit;
            if (!(dividend > 0.0) || double.IsInfinity(dividend)) return 0.0;
            return Finite(targetMoney) - dividend >= 0.0 ? dividend : 0.0;
        }

        /// <summary>
        /// The rescue order at the moment the target is about to go bankrupt: <paramref name="targetMoney"/> is C (at or below the threshold),
        /// <paramref name="targetDebtLimit"/> is D (positive), <paramref name="ownedShare"/> is σ_p, <paramref name="v"/> the valuation of the target,
        /// <paramref name="maxAmount"/> the limit of the order. <paramref name="channel"/> is <c>acquisition.price</c>; <paramref name="minCashAfter"/> is the
        /// money the player must keep. A refusal carries the key and the argument of the reason.
        /// </summary>
        public static RescueDecision Rescue(double targetMoney, double targetDebtLimit, double ownedShare, Valuation v, double playerMoney, double maxAmount,
            StakeConfig c, double channel = 1.0, double minCashAfter = 0.0)
        {
            var d = new RescueDecision();
            if (c == null) c = new StakeConfig();

            double money = Finite(targetMoney);
            double limit = Finite(targetDebtLimit);
            if (!(limit > 0.0))
            {
                d.ReasonKey = AcqKeys.RescueNoLimit;
                return d;
            }

            double targetBalance = c.RescueTargetDebtLimitShare * limit;   // B_r, below 0 and above -D
            d.TargetBalanceAfter = targetBalance;
            double injection = targetBalance - money;
            if (!(injection > 0.0))
            {
                d.ReasonKey = AcqKeys.RescueNotNeeded;
                return d;
            }

            double basis = Math.Max(Clean(v.EMin), Clean(v.E + money)) * c.DistressedMultiplier * UsableChannel(channel);
            if (!(basis > 0.0) || double.IsInfinity(basis))
            {
                d.ReasonKey = AcqKeys.RescueNoValue;
                return d;
            }

            double bought = injection / basis;
            d.Injection = injection;
            d.ShareBought = bought;

            if (injection > Finite(maxAmount))
            {
                return Refuse(d, AcqKeys.RescueOverLimit, Math.Ceiling(injection));
            }

            double after = Finite(playerMoney) - injection;
            if (after < minCashAfter)
            {
                return Refuse(d, AcqKeys.RescueNoCash, Math.Ceiling(minCashAfter - after));
            }

            if (!FitsShare(ownedShare, bought, c))
            {
                return Refuse(d, AcqKeys.RescueOverShare, 0.0, false);
            }

            d.Ok = true;
            return d;
        }

        /// <summary>The stake <paramref name="share"/> on top of <paramref name="ownedShare"/> stays within <c>maxShare</c> (and so below one half).</summary>
        public static bool FitsShare(double ownedShare, double share, StakeConfig c)
        {
            if (c == null) c = new StakeConfig();
            if (!IsPositive(share) || c.MaxShare <= 0.0) return false;
            return Finite(ownedShare) + share <= c.MaxShare + ShareTolerance;
        }

        /// <summary>The shares of <c>stake.options</c> the player can still buy on top of <paramref name="ownedShare"/>, smallest first.</summary>
        public static List<double> FittingOptions(double ownedShare, StakeConfig c)
        {
            var result = new List<double>();
            if (c == null) c = new StakeConfig();
            if (!c.StakesEnabled) return result;

            for (int i = 0; i < c.Options.Length; i++)
            {
                if (FitsShare(ownedShare, c.Options[i], c)) result.Add(c.Options[i]);
            }
            result.Sort();
            return result;
        }

        /// <summary>The smallest share the player can still buy, or 0 when none fits (the stake is at its maximum or stakes are switched off).</summary>
        public static double SmallestFittingOption(double ownedShare, StakeConfig c)
        {
            List<double> options = FittingOptions(ownedShare, c);
            return options.Count > 0 ? options[0] : 0.0;
        }

        // ---- helpers ----

        private static RescueDecision Refuse(RescueDecision d, string key, double argValue, bool withArg = true)
        {
            d.Ok = false;
            d.ReasonKey = key;
            d.ReasonArg = withArg ? argValue.ToString("F0", CultureInfo.InvariantCulture) : null;
            return d;
        }

        private static bool IsPositive(double value)
        {
            return value > 0.0 && !double.IsInfinity(value);
        }

        private static double Finite(double value)
        {
            return double.IsNaN(value) || double.IsInfinity(value) ? 0.0 : value;
        }

        private static double Clean(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value) && value > 0.0 ? value : 0.0;
        }

        private static double UsableChannel(double channel)
        {
            return !double.IsNaN(channel) && !double.IsInfinity(channel) && channel > 0.0 ? channel : 1.0;
        }
    }
}
