using System;
using System.Collections.Generic;
using System.Globalization;

namespace PTMod.Features.RivalAcquisitions
{
    /// <summary>
    /// <c>StakeBook</c> (FR-6, design.md "StakeService"): the records of the player's stakes and rescue orders in <see cref="AcquisitionsState"/>,
    /// and the rules of the rescue prefix (P3), independent of the game. The money and the game objects are the business of <c>StakeService</c>.
    /// <list type="bullet">
    /// <item>One stake record and one order per <c>UniqueID</c>. A record of an older company with the same <c>UniqueID</c> (another <c>SaveID</c>)
    /// is stale: it is ignored by the reads and replaced by a new purchase.</item>
    /// <item>A share never reaches one half: <see cref="AddShare"/> refuses what would.</item>
    /// <item>The rescue is tried only when the order is active, the threshold is below 0, the company is in default and its balance is at or below
    /// the threshold (otherwise the game's method returns by itself and nothing has to be saved).</item>
    /// </list>
    /// </summary>
    public static class StakeBook
    {
        // ---- stakes ----

        /// <summary>The stake record of this very company (the pair of ids), or null.</summary>
        public static StakeData Find(AcquisitionsState s, int uniqueId, int saveId)
        {
            if (s == null || s.stakes == null) return null;
            foreach (StakeData stake in s.stakes)
            {
                if (stake != null && stake.targetUniqueId == uniqueId && stake.targetSaveId == saveId) return stake;
            }
            return null;
        }

        /// <summary>
        /// Adds <paramref name="share"/> bought for <paramref name="invested"/> to the player's stake in the company (a new record on the first
        /// purchase, dated <paramref name="today"/>; a stale record of the same <c>UniqueID</c> is replaced). Returns the share the player owns
        /// after it. A share that is not positive, or one that would reach <see cref="AcquisitionsState.ShareLimit"/>, changes nothing.
        /// </summary>
        public static double AddShare(AcquisitionsState s, int uniqueId, int saveId, double share, double invested, DateTime today)
        {
            if (s == null) throw new ArgumentNullException("s");
            if (s.stakes == null) s.stakes = new List<StakeData>();

            StakeData stake = Find(s, uniqueId, saveId);
            double owned = stake != null && AcquisitionsState.IsValidShare(stake.share) ? stake.share : 0.0;
            if (!(share > 0.0) || double.IsInfinity(share) || !(owned + share < AcquisitionsState.ShareLimit)) return owned;

            if (stake == null)
            {
                s.stakes.RemoveAll(x => x == null || x.targetUniqueId == uniqueId);
                stake = new StakeData
                {
                    targetUniqueId = uniqueId,
                    targetSaveId = saveId,
                    since = today == default(DateTime) ? "" : today.ToString(AcquisitionsState.DateFormat, CultureInfo.InvariantCulture),
                };
                s.stakes.Add(stake);
            }

            stake.share = owned + share;
            if (invested > 0.0 && !double.IsInfinity(invested)) stake.invested += invested;
            return stake.share;
        }

        /// <summary>Removes the stake of the company (any record of the <c>UniqueID</c>). Returns the share it had (0 when there was none).</summary>
        public static double RemoveStake(AcquisitionsState s, int uniqueId)
        {
            if (s == null || s.stakes == null) return 0.0;
            double had = 0.0;
            foreach (StakeData stake in s.stakes)
            {
                if (stake != null && stake.targetUniqueId == uniqueId && AcquisitionsState.IsValidShare(stake.share)) had = stake.share;
            }
            s.stakes.RemoveAll(x => x == null || x.targetUniqueId == uniqueId);
            return had;
        }

        /// <summary>Books a dividend that was paid: it is added to the total and is the last one.</summary>
        public static void RecordDividend(StakeData stake, double amount)
        {
            if (stake == null || !(amount > 0.0) || double.IsInfinity(amount)) return;
            stake.dividendsPaid += amount;
            stake.lastDividend = amount;
        }

        // ---- rescue orders ----

        /// <summary>The rescue order of this very company (the pair of ids), active or not; null for none.</summary>
        public static RescueOrderData Order(AcquisitionsState s, int uniqueId, int saveId)
        {
            if (s == null || s.rescueOrders == null) return null;
            foreach (RescueOrderData order in s.rescueOrders)
            {
                if (order != null && order.targetUniqueId == uniqueId && order.targetSaveId == saveId) return order;
            }
            return null;
        }

        /// <summary>
        /// Sets the rescue order of the company: its limit (below 0 counts as 0) and whether it is on. A stale order of the same <c>UniqueID</c> is
        /// replaced. Returns the record.
        /// </summary>
        public static RescueOrderData SetOrder(AcquisitionsState s, int uniqueId, int saveId, double maxAmount, bool active)
        {
            if (s == null) throw new ArgumentNullException("s");
            if (s.rescueOrders == null) s.rescueOrders = new List<RescueOrderData>();

            RescueOrderData order = Order(s, uniqueId, saveId);
            if (order == null)
            {
                s.rescueOrders.RemoveAll(x => x == null || x.targetUniqueId == uniqueId);
                order = new RescueOrderData { targetUniqueId = uniqueId, targetSaveId = saveId };
                s.rescueOrders.Add(order);
            }
            order.maxAmount = maxAmount > 0.0 && !double.IsInfinity(maxAmount) ? maxAmount : 0.0;
            order.active = active;
            return order;
        }

        /// <summary>Removes the rescue order of the company (any record of the <c>UniqueID</c>); true when there was one.</summary>
        public static bool RemoveOrder(AcquisitionsState s, int uniqueId)
        {
            if (s == null || s.rescueOrders == null) return false;
            return s.rescueOrders.RemoveAll(x => x == null || x.targetUniqueId == uniqueId) > 0;
        }

        /// <summary>
        /// P3: the rescue is worth trying now. <paramref name="negativeBalanceLimit"/> is the argument of <c>AICompany.BankruptcyWarning</c> (the
        /// company's threshold); 0 or more is the game's debug call (<c>DebugTriggerFabless</c>) and is ignored. Above the threshold the game's
        /// method returns by itself, so nothing needs saving.
        /// </summary>
        public static bool ShouldTryRescue(bool orderActive, double negativeBalanceLimit, bool isInDefault, double money)
        {
            return orderActive && negativeBalanceLimit < 0.0 && isInDefault && money <= negativeBalanceLimit;
        }

        // ---- dividends ----

        /// <summary>The month whose report the dividend of <paramref name="today"/> is paid from: the month before.</summary>
        public static void PreviousMonth(DateTime today, out int year, out int month)
        {
            DateTime previous = new DateTime(today.Year, today.Month, 1).AddMonths(-1);
            year = previous.Year;
            month = previous.Month;
        }

        // ---- records ----

        /// <summary>The record of a stake purchase (<see cref="DealType.Stake"/>) or of a rescue (<see cref="DealType.RescueOrder"/>), without an id.</summary>
        public static DealRecord Record(DealType type, int uniqueId, int saveId, string name, DateTime today, double price, double share)
        {
            return new DealRecord
            {
                type = type.ToString(),
                targetUniqueId = uniqueId,
                targetSaveId = saveId,
                targetName = name ?? "",
                date = today.ToString(AcquisitionsState.DateFormat, CultureInfo.InvariantCulture),
                price = price > 0.0 && !double.IsInfinity(price) ? price : 0.0,
                share = AcquisitionsState.IsValidShare(share) ? share : 0.0,
            };
        }
    }
}
