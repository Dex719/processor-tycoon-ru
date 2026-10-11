using System;

namespace PTMod.Features.GpuProductLine
{
    /// <summary>
    /// The arithmetic of the GPU laboratory (design.md, F15-F16; FR-2): how long a node takes in the current year, how far research goes in a day and
    /// what a day costs. It repeats the research of the game: <c>TechnologyTimeUpdater.UpdateTechnologyTimes</c> (a node of a future year takes many times
    /// longer, a node of a past year up to 75 % less), <c>ResearchSector.Research.AddProgress</c> (progress per day is the research speed over the time of the
    /// node) and <c>ResearchSector.Team.GetTotalCost</c> (the monthly cost over 30, times the research cost modifier and the funding).
    /// The research speed and the cost modifier arrive as numbers - <c>ResearchMath.ResearchSpeed</c> and <c>Modifiers.GetModifier</c> are asked by the caller.
    /// </summary>
    public static class GpuResearchMath
    {
        // The days a node waits after its year before the discount starts (a full year, as in the game).
        private const float DiscountFreeDays = 365f;
        private const float DaysPerYear = 365f;
        private const float DaysPerMonth = 30f;

        /// <summary>
        /// F15: the time of a node in days on <paramref name="today"/>. A node of a later year: <c>base x (1 + penaltyPerYear x daysUntil(1 Jan nodeYear) / 365)</c>.
        /// A node of an earlier year: <c>base x (1 - clamp((daysSince(1 Jan nodeYear) - 365) / 365 x bonusPerYear, 0, maxBonus))</c>. The same year: <c>base</c>.
        /// A node with no base time takes no time (it is done at once, as in the game).
        /// </summary>
        public static float EffectiveDays(int baseDays, int nodeYear, DateTime today, float penaltyPerYear, float bonusPerYear, float maxBonus)
        {
            if (baseDays <= 0) return 0f;
            int year = today.Year;
            if (nodeYear > year)
            {
                float until = DaysBetween(today, FirstOfJanuary(nodeYear)) / DaysPerYear * Math.Max(0f, penaltyPerYear);
                return baseDays + baseDays * until;
            }
            if (nodeYear < year)
            {
                float since = DaysBetween(FirstOfJanuary(nodeYear), today);
                float bonus = (since - DiscountFreeDays) / DaysPerYear * Math.Max(0f, bonusPerYear);
                bonus = Math.Min(Math.Min(1f, Math.Max(0f, maxBonus)), Math.Max(0f, bonus));
                return baseDays * (1f - bonus);
            }
            return baseDays;
        }

        /// <summary>How many times longer than the base time a node takes (<c>effective / base</c>), or 1 when it is not slower: the "ahead of its time" mark of the research tab.</summary>
        public static float AheadFactor(int baseDays, float effectiveDays)
        {
            if (baseDays <= 0 || !(effectiveDays > baseDays)) return 1f;
            return effectiveDays / baseDays;
        }

        /// <summary><c>ResearchMath.ResearchSpeed</c>: <c>sqrt(funding) x modifier</c>. Only for tests and previews; the feature asks the game (<c>IGpuGame.ResearchSpeed</c>).</summary>
        public static float ResearchSpeedPort(float funding, float modifier)
        {
            return (float)Math.Sqrt(Math.Max(0f, funding)) * modifier;
        }

        /// <summary>F16: progress per day (a fraction of the node), <c>researchSpeed / effectiveDays</c>. A node of no time is done in a day (1).</summary>
        public static float DailyProgress(float researchSpeed, float effectiveDays)
        {
            if (!(effectiveDays > 0f)) return 1f;
            float progress = researchSpeed / effectiveDays;
            return float.IsNaN(progress) || progress < 0f ? 0f : progress;
        }

        /// <summary>F16: money per day, <c>monthlyCost x researchCostMod / 30 x funding</c>.</summary>
        public static float DailyCost(float monthlyCost, float researchCostMod, float funding)
        {
            float cost = monthlyCost * researchCostMod / DaysPerMonth * funding;
            return float.IsNaN(cost) || cost < 0f ? 0f : cost;
        }

        /// <summary>Days left at the current pace, <c>round((1 - progress) / dailyProgress)</c> (<c>Research.TimeLeft</c>); -1 when the research does not move.</summary>
        public static int DaysLeft(float progress, float dailyProgress)
        {
            if (!(dailyProgress > 0f)) return -1;
            float left = (1f - Math.Min(1f, Math.Max(0f, progress))) / dailyProgress;
            return GpuDesignMath.RoundToInt(left);
        }

        private static DateTime FirstOfJanuary(int year)
        {
            return new DateTime(Math.Min(9999, Math.Max(1, year)), 1, 1);
        }

        // The game counts whole days between two dates, without the sign (DateTimeExtensions.DaysUntil).
        private static float DaysBetween(DateTime a, DateTime b)
        {
            return Math.Abs((a - b).Days);
        }
    }
}
