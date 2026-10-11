using System;
using System.Collections.Generic;
using System.Globalization;

namespace PTMod.Features.CpuArchitectures
{
    /// <summary>
    /// The arithmetic of researching a family in the shared lab (FR-2, FR-12; design.md, "Исследование"): the same formulas the game uses for its
    /// technologies, so that an architecture costs time and money the way a technology of the same year does.
    /// <list type="bullet">
    /// <item><b>Year factor</b> - a copy of <c>TechnologyTimeUpdater.UpdateTechnologyTimes</c>: a family of a future year takes
    /// <c>1 + days until 1 January of its year / 365 × penalty</c> longer, a family of a past year
    /// <c>1 − clamp((days since 1 January of its year − 365) / 365 × bonus, 0, cap)</c> as long; the current year is ×1.</item>
    /// <item><b>Days</b> - <c>baseDays × year factor × ISA factor</c>; the ISA factor is 0.6 for a company that already researched a family of
    /// the same ISA (stage 2), 1 otherwise.</item>
    /// <item><b>Progress per day</b> - <c>ResearchMath.ResearchSpeed(F, company) / days</c> (√F × the research speed modifier, 1.05 for an IDM
    /// company), as <c>ResearchSector.Research.AddProgress</c> does.</item>
    /// <item><b>Cost per day</b> - <c>monthlyCost × Modifiers.GetModifier(ResearchCost) / 30 × F</c>, as <c>ResearchSector.Team.GetTotalCost</c>
    /// (×6 for the "unlucky" company of the game).</item>
    /// <item><b>Days left</b> - <c>RoundToInt((1 − progress) / progress per day)</c>, as <c>ResearchSector.Research.TimeLeft</c>; <see cref="InfiniteEta"/>
    /// when the lab does not move (speed 0).</item>
    /// </list>
    /// Pure: no game types, no Unity. The service (<c>ArchResearchService</c>) reads the game's numbers and hands them in.
    /// </summary>
    public static class ArchResearchMath
    {
        /// <summary>The days of a year in the game's formula (<c>/ 365</c>).</summary>
        public const float DaysPerYear = 365f;

        /// <summary>The days of a month in the game's cost formula (<c>/ 30</c>).</summary>
        public const float DaysPerMonth = 30f;

        /// <summary>What <see cref="EtaDays"/> returns when the research does not move: shown as <see cref="InfinitySign"/>.</summary>
        public const int InfiniteEta = -1;

        /// <summary>The text of an endless ETA.</summary>
        public const string InfinitySign = "∞";

        /// <summary>The earliest and latest year the date arithmetic accepts (the range of <c>DateTime</c> with a margin).</summary>
        public const int MinYear = 1;
        public const int MaxYear = 9998;

        // ---- time ----

        /// <summary>
        /// The year factor of a family (a copy of <c>TechnologyTimeUpdater.UpdateTechnologyTimes</c>). <paramref name="penaltyPerYear"/> and
        /// <paramref name="bonusPerYear"/> are fractions per year (the game's serialized 350 and 15 divided by 100: 3.5 and 0.15),
        /// <paramref name="bonusCap"/> is the game's 0.75.
        /// </summary>
        /// <param name="familyYear">The year the family appears.</param>
        /// <param name="daysUntilFamilyYear"><c>DateController.DaysUntilYear(familyYear)</c>: the days from today to 1 January of that year (used when it is in the future).</param>
        /// <param name="daysSinceFamilyYearStart"><c>DateController.DaysSince(1 January of familyYear)</c>: the days since then (used when it is in the past).</param>
        /// <param name="currentYear">Today's year.</param>
        public static float YearFactor(int familyYear, int daysUntilFamilyYear, int daysSinceFamilyYearStart, int currentYear,
                                       float penaltyPerYear, float bonusPerYear, float bonusCap)
        {
            if (familyYear > currentYear)
            {
                float penalty = ((float)daysUntilFamilyYear / DaysPerYear) * penaltyPerYear;
                return Finite(1f + penalty, 1f);
            }

            if (familyYear < currentYear)
            {
                float bonus = Clamp((((float)daysSinceFamilyYearStart - DaysPerYear) / DaysPerYear) * bonusPerYear, 0f, Math.Max(0f, bonusCap));
                return Finite((bonus + 1f) - (bonus * 2f), 1f);   // MathHelper.ReversePercentageScale(bonus) = 1 − bonus, in the game's order
            }

            return 1f;
        }

        /// <summary>
        /// The year factor of a family on <paramref name="today"/> with the numbers of <c>balance.json</c> ("research"): the days are counted the way
        /// <c>DateController.DaysUntilYear</c> and <c>DaysSince</c> count them (whole days between the dates, never negative).
        /// </summary>
        public static float YearFactor(int familyYear, DateTime today, ResearchConfig config)
        {
            if (config == null) config = new ResearchConfig();
            int year = familyYear < MinYear ? MinYear : (familyYear > MaxYear ? MaxYear : familyYear);
            var yearStart = new DateTime(year, 1, 1);
            int days = DaysBetween(today, yearStart);
            return YearFactor(familyYear, days, days, today.Year, config.penaltyPerYear, config.bonusPerYear, config.bonusCap);
        }

        /// <summary>Whole days between two dates, never negative (<c>DateTimeExtensions.DaysUntil</c> of the game: <c>|(a − b).Days|</c>).</summary>
        public static int DaysBetween(DateTime a, DateTime b)
        {
            return Math.Abs((a - b).Days);
        }

        /// <summary>The ISA factor (FR-12 item 1, stage 2): <paramref name="experienceFactor"/> when the company has researched another family of the same ISA, 1 otherwise and at stage 1.</summary>
        public static float IsaFactor(ArchFamily family, IReadOnlyDictionary<string, FamilyState> states, ArchCatalog catalog, float experienceFactor, bool stageFull)
        {
            if (!stageFull || family == null || states == null || catalog == null) return 1f;

            foreach (KeyValuePair<string, FamilyState> pair in states)
            {
                FamilyState state = pair.Value;
                if (state == null || !state.Researched || pair.Key == family.Id) continue;
                ArchFamily other = catalog.Get(pair.Key);
                if (other != null && other.Isa == family.Isa) return Finite(experienceFactor, 1f);
            }
            return 1f;
        }

        /// <summary>The research days of a family now: <c>baseDays × year factor × ISA factor</c>; 0 for a family researched at once.</summary>
        public static float EffectiveDays(float baseDays, float yearFactor, float isaFactor)
        {
            if (!(baseDays > 0f)) return 0f;
            float days = baseDays * Finite(yearFactor, 1f) * Finite(isaFactor, 1f);
            return days > 0f && !float.IsInfinity(days) ? days : 0f;
        }

        /// <summary>
        /// The progress of one day: <c>researchSpeed / effectiveDays</c> (<c>researchSpeed</c> = <c>ResearchMath.ResearchSpeed(F, company)</c>).
        /// A family with no days completes in one tick (1); a lab that does not move gives 0.
        /// </summary>
        public static float DailyProgress(float researchSpeed, float effectiveDays)
        {
            if (!(researchSpeed > 0f) || float.IsInfinity(researchSpeed)) return 0f;
            if (!(effectiveDays > 0f)) return 1f;
            return Finite(researchSpeed / effectiveDays, 0f);
        }

        /// <summary>The money of one day: <c>monthlyCost × costModifier / 30 × funding</c> (<c>costModifier</c> = <c>Modifiers.GetModifier(ResearchCost, company)</c>); never negative.</summary>
        public static float DailyCost(float monthlyCost, float costModifier, float funding)
        {
            float cost = ((monthlyCost * costModifier) / DaysPerMonth) * funding;
            return cost > 0f && !float.IsInfinity(cost) ? cost : 0f;
        }

        /// <summary>The money of a month at this funding (the "per month" line of the panel): <see cref="DailyCost"/> × 30.</summary>
        public static float MonthlyCost(float monthlyCost, float costModifier, float funding)
        {
            return DailyCost(monthlyCost, costModifier, funding) * DaysPerMonth;
        }

        /// <summary>
        /// The days left, rounded like <c>Mathf.RoundToInt</c> (halves to even), as <c>ResearchSector.Research.TimeLeft</c> does; 0 for a finished
        /// research and <see cref="InfiniteEta"/> when the progress per day is 0 (the lab stands still).
        /// </summary>
        public static int EtaDays(float progress, float dailyProgress)
        {
            if (progress >= 1f) return 0;
            if (!(dailyProgress > 0f) || float.IsInfinity(dailyProgress)) return InfiniteEta;

            float left = Clamp(1f - (float.IsNaN(progress) ? 0f : progress), 0f, 1f);
            double days = Math.Round(left / dailyProgress, MidpointRounding.ToEven);
            return days >= int.MaxValue ? InfiniteEta : (int)days;
        }

        /// <summary>The ETA as text: <see cref="InfinitySign"/> for <see cref="InfiniteEta"/>, otherwise <paramref name="formatDays"/> of the days (the plain number when null).</summary>
        public static string FormatEta(int etaDays, Func<int, string> formatDays = null)
        {
            if (etaDays < 0) return InfinitySign;
            return formatDays == null ? etaDays.ToString(CultureInfo.InvariantCulture) : formatDays(etaDays);
        }

        // ---- availability ----

        /// <summary>A family that is researched at once (<c>baseDays</c> 0, FR-2 item 9), like a technology with <c>BaseTime</c> 0.</summary>
        public static bool IsInstant(ResearchDef research)
        {
            return research == null || !(research.BaseDays > 0f);
        }

        /// <summary>The family is researched in <paramref name="states"/>.</summary>
        public static bool IsResearched(string familyId, IReadOnlyDictionary<string, FamilyState> states)
        {
            FamilyState state;
            return familyId != null && states != null && states.TryGetValue(familyId, out state) && state != null && state.Researched;
        }

        /// <summary>Every family the family requires is researched (a family without requirements always is).</summary>
        public static bool RequirementsMet(ArchFamily family, IReadOnlyDictionary<string, FamilyState> states)
        {
            if (family == null) return false;
            if (family.Requires == null) return true;
            foreach (string id in family.Requires)
            {
                if (!IsResearched(id, states)) return false;
            }
            return true;
        }

        /// <summary>The family can be started now (FR-2 item 1): its requirements are researched and it is not researched itself.</summary>
        public static bool IsAvailable(ArchFamily family, IReadOnlyDictionary<string, FamilyState> states)
        {
            return family != null && !IsResearched(family.Id, states) && RequirementsMet(family, states);
        }

        /// <summary>The family is researched, or it is researched at once and available (FR-2 item 9: <c>proprietary</c> for every company).</summary>
        public static bool CountsAsResearched(ArchFamily family, IReadOnlyDictionary<string, FamilyState> states)
        {
            if (family == null) return false;
            return IsResearched(family.Id, states) || (IsInstant(family.Research) && RequirementsMet(family, states));
        }

        /// <summary>The required families that are not researched yet, in the order of the record ("Требует: {0}"); empty when none is missing.</summary>
        public static List<string> MissingRequirements(ArchFamily family, IReadOnlyDictionary<string, FamilyState> states)
        {
            var missing = new List<string>();
            if (family == null || family.Requires == null) return missing;
            foreach (string id in family.Requires)
            {
                if (!IsResearched(id, states)) missing.Add(id);
            }
            return missing;
        }

        // ---- helpers ----

        private static float Clamp(float value, float min, float max)
        {
            if (float.IsNaN(value)) return min;
            return value < min ? min : (value > max ? max : value);
        }

        private static float Finite(float value, float fallback)
        {
            return float.IsNaN(value) || float.IsInfinity(value) ? fallback : value;
        }
    }
}
