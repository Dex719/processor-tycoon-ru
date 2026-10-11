using System;
using System.Collections.Generic;

namespace PTMod.Features.RivalPersonalities
{
    /// <summary>
    /// The rules that turn the facts about the player's moves into triggers (FR-5, design.md formula 12): the thresholds come from
    /// <c>config.json</c> (<see cref="RivalConfig.TriggerThresholdSettings"/>), the facts from the game side. Pure and total: a number that is not
    /// finite never makes a trigger.
    /// </summary>
    public static class TriggerRules
    {
        /// <summary>The slack for comparing shares that were computed as fractions of whole units (the shares are 0..1).</summary>
        private const double ShareEpsilon = 1e-6;

        /// <summary>
        /// A move of the player counts as a trigger only in the running game (FR-5 item 1, AC-5.2, AC-5.5): not while a save loads (the game then
        /// calls <c>AddCpu</c> and <c>AddTechnology</c> for everything the player owns), not during the fast-forward of a new game, not for a CPU that
        /// was released in it (<paramref name="duringFastForward"/> of <c>ModEvents.CpuReleased</c>), and not before the game is ready.
        /// </summary>
        public static bool IsBlocked(bool loading, bool fastForwarding, bool duringFastForward, bool gameReady)
        {
            return loading || fastForwarding || duringFastForward || !gameReady;
        }

        /// <summary>
        /// <c>StrongLaunch</c>: the new CPU is at least <paramref name="ratio"/> (1.15) times as fast as the best CPU that the rival sells in the
        /// segment. A rival that is active in the segment but sells no CPU there (<paramref name="rivalSellsCpu"/> false) is beaten by any CPU
        /// that has a performance. A new CPU without a usable performance (not a number, 0 or less) is never strong.
        /// </summary>
        public static bool IsStrongLaunch(float newIps, bool rivalSellsCpu, float bestRivalIps, float ratio)
        {
            if (!IsUsable(newIps) || newIps <= 0f) return false;
            if (!rivalSellsCpu) return true;
            if (!IsUsable(bestRivalIps) || bestRivalIps <= 0f) return true;
            return (double)newIps >= (double)ratio * bestRivalIps;
        }

        /// <summary>
        /// <c>PriceUndercut</c>: the player's CPU is at least <paramref name="ipsRatio"/> (0.9) as fast as the rival's best CPU of the segment and costs
        /// at most <paramref name="priceRatio"/> (0.85) of its price.
        /// </summary>
        public static bool IsPriceUndercut(float playerIps, float playerPrice, float rivalIps, float rivalPrice, float ipsRatio, float priceRatio)
        {
            if (!IsUsable(playerIps) || !IsUsable(playerPrice) || !IsUsable(rivalIps) || !IsUsable(rivalPrice)) return false;
            if (playerIps <= 0f || rivalIps <= 0f || playerPrice <= 0f || rivalPrice <= 0f) return false;
            return (double)playerIps >= (double)ipsRatio * rivalIps && (double)playerPrice <= (double)priceRatio * rivalPrice;
        }

        /// <summary>
        /// <c>ShareGain</c>: the share of the player in a segment grew by at least <paramref name="thresholdPp"/> percentage points (3.0) against the
        /// month before, and the share of the rival fell. The shares are fractions (0.14 is 14 %).
        /// </summary>
        public static bool IsShareGain(float playerBefore, float playerNow, float rivalBefore, float rivalNow, float thresholdPp)
        {
            if (!PlayerShareGrew(playerBefore, playerNow, thresholdPp)) return false;
            if (!IsUsable(rivalBefore) || !IsUsable(rivalNow)) return false;
            return (double)rivalNow < (double)rivalBefore - ShareEpsilon;
        }

        /// <summary>The share of the player grew by at least <paramref name="thresholdPp"/> percentage points (the first half of <see cref="IsShareGain"/>).</summary>
        public static bool PlayerShareGrew(float playerBefore, float playerNow, float thresholdPp)
        {
            if (!IsUsable(playerBefore) || !IsUsable(playerNow)) return false;
            double growth = (double)playerNow - playerBefore;
            return growth + ShareEpsilon >= thresholdPp / 100.0;
        }

        /// <summary>
        /// <c>TechLead</c>: the technology of the player is of a type that the rivals care about (<c>triggerThresholds.techLeadTypes</c>:
        /// Lithography, Frequency, Multicore). The name of the game's <c>TechnologyType</c> is matched exactly.
        /// </summary>
        public static bool IsTechLeadType(string technologyType, IList<string> allowedTypes)
        {
            if (string.IsNullOrEmpty(technologyType) || allowedTypes == null) return false;
            for (int i = 0; i < allowedTypes.Count; i++)
            {
                if (string.Equals(allowedTypes[i], technologyType, StringComparison.Ordinal)) return true;
            }
            return false;
        }

        private static bool IsUsable(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}
