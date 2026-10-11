using System;

namespace PTMod.Features.RivalPersonalities
{
    /// <summary>
    /// The arithmetic of the prices of the AI (design.md, formulas 3 and 4; FR-3 item 3, FR-7 item 3, FR-13 item 6): the launch price of a new CPU
    /// of a rival and the price under a price cut. Pure; <c>PriceBook</c> and <c>MarketPatches</c> apply the numbers to the game.
    /// </summary>
    public static class PriceCutMath
    {
        /// <summary>The largest share of the price a cut takes off, and the largest margin of the floor above the unit cost (the clip of the API).</summary>
        public const float MaxShare = 0.5f;

        /// <summary>The longest id of a price cut (<c>r-&lt;n&gt;</c> or the caller's <c>&lt;prefix&gt;:&lt;name&gt;</c>).</summary>
        public const int MaxReactionIdLength = 64;

        /// <summary>The prefix of the ids of the feature's own price cuts (<c>r-&lt;n&gt;</c>); every other id belongs to an external caller.</summary>
        public const string InternalPrefix = "r-";

        /// <summary>
        /// The price under a cut (formula 4): <c>max(price × (1 − depth), realUnitCost × (1 + floorMargin))</c>, never above <paramref name="price"/>.
        /// <paramref name="depth"/> and <paramref name="floorMargin"/> are clipped to 0..<see cref="MaxShare"/> (NaN is 0). A price that is not a
        /// positive finite number is returned as it is; a unit cost that is not finite or not positive gives no floor.
        /// </summary>
        /// <example><c>CutPrice(239, 120, 0.15, 0.08)</c> = max(203.15, 129.6) = 203.15.</example>
        public static float CutPrice(float price, float realUnitCost, float depth, float floorMargin)
        {
            if (!IsUsablePrice(price)) return price;

            float cut = price * (1f - ClipShare(depth));
            if (IsUsablePrice(realUnitCost))
            {
                float floor = realUnitCost * (1f + ClipShare(floorMargin));
                if (floor > cut) cut = floor;
            }
            return cut < price ? cut : price;
        }

        /// <summary>
        /// The launch price of a new CPU of a rival (formula 3, FR-3 item 3): <c>max(targetPrice × multiplier, realUnitCost × (1 + floorMargin))</c>,
        /// the same number for <c>TargetPrice</c> and <c>Price</c>. A multiplier of 1 (the vanilla price), a multiplier that is not a positive finite
        /// number and a target price that is not a positive finite number leave <paramref name="targetPrice"/> as it is.
        /// </summary>
        /// <example><c>LaunchPrice(249, 0.92, 100, 0.05)</c> = max(229.08, 105) = 229.08.</example>
        public static float LaunchPrice(float targetPrice, float multiplier, float realUnitCost, float floorMargin)
        {
            if (!IsUsablePrice(targetPrice)) return targetPrice;
            if (!IsUsablePrice(multiplier) || multiplier == 1f) return targetPrice;

            float price = targetPrice * multiplier;
            if (IsUsablePrice(realUnitCost))
            {
                float margin = float.IsNaN(floorMargin) || floorMargin < 0f ? 0f : floorMargin;
                float floor = realUnitCost * (1f + margin);
                if (floor > price) price = floor;
            }
            return price;
        }

        /// <summary>The target price held under a cut: <c>min(targetPrice, cut)</c>. A cut that is not a positive finite number changes nothing.</summary>
        public static float ClampTarget(float targetPrice, float cut)
        {
            if (!IsUsablePrice(cut)) return targetPrice;
            return targetPrice > cut ? cut : targetPrice;
        }

        /// <summary>A share for <see cref="CutPrice"/>: clipped to 0..<see cref="MaxShare"/>, NaN is 0.</summary>
        public static float ClipShare(float share)
        {
            if (float.IsNaN(share) || share <= 0f) return 0f;
            return share > MaxShare ? MaxShare : share;
        }

        /// <summary>A positive finite number (a price that the AI has set).</summary>
        public static bool IsUsablePrice(float value)
        {
            return value > 0f && !float.IsInfinity(value) && !float.IsNaN(value);
        }

        /// <summary>The id of a price cut is usable: not empty and at most <see cref="MaxReactionIdLength"/> characters.</summary>
        public static bool IsValidReactionId(string reactionId)
        {
            return !string.IsNullOrEmpty(reactionId) && reactionId.Length <= MaxReactionIdLength;
        }

        /// <summary>The id is one of the feature's own reactions: <c>r-</c> followed by one or more ASCII digits.</summary>
        public static bool IsInternalId(string reactionId)
        {
            if (reactionId == null || reactionId.Length <= InternalPrefix.Length) return false;
            if (!reactionId.StartsWith(InternalPrefix, StringComparison.Ordinal)) return false;
            for (int i = InternalPrefix.Length; i < reactionId.Length; i++)
            {
                char c = reactionId[i];
                if (c < '0' || c > '9') return false;
            }
            return true;
        }

        /// <summary>
        /// A cut of an external caller has run its course (FR-13 item 6): <paramref name="maxDays"/> or more days passed since it started. An internal
        /// id never expires here (its end is the event system's), and neither does a cut with an unknown start.
        /// </summary>
        public static bool ExternalExpired(string reactionId, DateTime startedOn, DateTime today, int maxDays)
        {
            if (IsInternalId(reactionId)) return false;
            if (startedOn == default(DateTime) || today == default(DateTime)) return false;
            return RivalDates.DaysBetween(startedOn, today) >= maxDays;
        }
    }
}
