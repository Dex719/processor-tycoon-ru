using System;

namespace PTMod.Features.CpuArchitectures
{
    /// <summary>
    /// Immature tools of a young architecture (FR-12 items 3-4, stage 2): for twelve months after a company has researched a family, a project of a CPU on it costs
    /// more.
    /// <code>
    /// surcharge(months) = immatureDevCost × max(0, 1 − months / immatureMonths)          0.25 × (1 − months / 12)
    /// project cost     = the game's cost × design.devCost × (1 + surcharge)               the provider of development.cost.design
    /// </code>
    /// The months are whole months between the month the family was researched and today's. A family given at the start (no date of research) has none. Pure:
    /// the dates come in as numbers; the provider and the line of the designer ask the same function, so the price and the words about it never disagree.
    /// </summary>
    public static class ImmatureTools
    {
        /// <summary>Whole months from the month of research to today's month; -1 when the family has no date of research (given at the start, or not researched).</summary>
        public static int MonthsSince(int researchedYear, int researchedMonth, DateTime today)
        {
            if (researchedYear <= 0 || researchedMonth < 1 || researchedMonth > 12) return -1;
            int months = (today.Year - researchedYear) * 12 + (today.Month - researchedMonth);
            return months < 0 ? 0 : months;
        }

        /// <summary>The surcharge of the project cost (0..<c>immatureDevCost</c>) for a family researched <paramref name="months"/> months ago; 0 for an unknown date and after the period.</summary>
        public static float Surcharge(int months, MigrationConfig config)
        {
            if (config == null) config = new MigrationConfig();
            if (months < 0 || config.immatureMonths <= 0 || months >= config.immatureMonths) return 0f;

            float left = 1f - (float)months / config.immatureMonths;
            float surcharge = config.immatureDevCost * left;
            return float.IsNaN(surcharge) || float.IsInfinity(surcharge) || surcharge < 0f ? 0f : surcharge;
        }

        /// <summary>The surcharge for the family state of a company today (<see cref="MonthsSince"/> and <see cref="Surcharge(int, MigrationConfig)"/>); 0 for a missing state or one that is not researched.</summary>
        public static float Surcharge(FamilyState state, DateTime today, MigrationConfig config)
        {
            if (state == null || !state.Researched || today == default(DateTime)) return 0f;
            return Surcharge(MonthsSince(state.ResearchedYear, state.ResearchedMonth, today), config);
        }

        /// <summary>The months of immaturity that are left: <c>immatureMonths − months</c>; 0 when the period is over or unknown.</summary>
        public static int MonthsLeft(int months, MigrationConfig config)
        {
            if (config == null) config = new MigrationConfig();
            if (months < 0 || months >= config.immatureMonths) return 0;
            return config.immatureMonths - months;
        }

        /// <summary>The surcharge as a whole percent for the text ("+19 %": 0.1875 is 19); 0 for none.</summary>
        public static int Percent(float surcharge)
        {
            if (float.IsNaN(surcharge) || float.IsInfinity(surcharge) || surcharge <= 0f) return 0;
            return (int)Math.Round(surcharge * 100.0, MidpointRounding.AwayFromZero);
        }
    }
}
