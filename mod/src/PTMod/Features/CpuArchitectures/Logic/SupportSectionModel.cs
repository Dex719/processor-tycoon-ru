using System.Collections.Generic;

namespace PTMod.Features.CpuArchitectures
{
    /// <summary>
    /// What the section "Программная поддержка" of the panel shows about one researched family (FR-10 item 7, NFR-6): the support level and its tier, where
    /// the tier leads and how fast, what it costs a month and what the level will be in a year, and the multiplier it gives the rating. Plain numbers made by
    /// <see cref="SupportSectionModel"/>; <c>SupportRow</c> writes them as text and the tooltip shows the formula with them.
    /// </summary>
    public sealed class SupportRowInfo
    {
        public string FamilyId;

        /// <summary>The support level now, 0..100.</summary>
        public float Support;

        public SupportTier Tier;

        /// <summary>The level the tier leads to (held to the <c>targetCap</c> of the family); 0 for "off".</summary>
        public float Target;

        /// <summary>The share of the distance to the target covered in a month (the decay rate of the family for "off").</summary>
        public float Rate;

        /// <summary><c>targetCap</c> of the family, 100 when it has none.</summary>
        public float Cap;

        /// <summary>The terms of the upkeep: <c>upkeepPerMonth</c>, the factor of the tier, the factor of the era and the inflation.</summary>
        public float BaseUpkeep;
        public float TierFactor;
        public float EraFactor;
        public float Inflation;

        /// <summary>The upkeep of one month at the tier, in game money (the product of the four terms above).</summary>
        public float Upkeep;

        /// <summary>The support level after <see cref="SupportMath.ForecastMonths"/> months at the tier.</summary>
        public float Forecast;

        /// <summary><c>f_sup</c>: the multiplier the support gives the rating of the company's CPUs on this family.</summary>
        public float RatingFactor;

        /// <summary>The tier may be changed now (the section works and the company is not in default).</summary>
        public bool Editable;

        /// <summary>The family cannot be supported above its <c>targetCap</c> whatever the tier (the proprietary ISA).</summary>
        public bool Capped
        {
            get { return Cap < SupportMath.MaxSupport; }
        }
    }

    /// <summary>
    /// The section "Программная поддержка" of the panel "Архитектуры" without the game (FR-10 item 7; design.md Component 13, TSK-023): which families it lists
    /// (the researched ones, in the order of the catalog) and the numbers of each row. The cost follows the tier at once: <see cref="RowOf"/> is called again
    /// with the new tier and gives the new upkeep and forecast (M9).
    /// </summary>
    public static class SupportSectionModel
    {
        /// <summary>The section is shown only at stage 2 of <c>balance.json</c> and while the feature works; at stage 1 support gives ×1 and costs nothing.</summary>
        public static bool IsShown(BalanceConfig balance, bool featureWorks)
        {
            return featureWorks && balance != null && balance.IsFullStage;
        }

        /// <summary>The <c>ModL10n</c> key of the name of a tier ("Выкл", "Низкая", "Обычная", "Высокая"): the options of the list of the row.</summary>
        public static string TierKey(SupportTier tier)
        {
            switch (tier)
            {
                case SupportTier.Low: return "arch.support.tier.low";
                case SupportTier.Normal: return "arch.support.tier.normal";
                case SupportTier.High: return "arch.support.tier.high";
                default: return "arch.support.tier.off";
            }
        }

        /// <summary>The numbers of one row, for the state of the family the company has (null: nothing to show, the result is null).</summary>
        /// <param name="family">The family of the catalog.</param>
        /// <param name="own">What the company has with the family; its level and tier.</param>
        /// <param name="year">The year of the era factor of the upkeep (today's).</param>
        /// <param name="inflation"><c>InflationHandler.Inflation</c> now.</param>
        /// <param name="editable">The tier may be changed.</param>
        public static SupportRowInfo RowOf(ArchFamily family, FamilyState own, BalanceConfig balance, int year, float inflation, bool editable)
        {
            if (family == null || own == null || balance == null || !own.Researched) return null;

            SupportConfig config = balance.support;
            SupportTier tier = SupportCycle.IsTier(own.Tier) ? own.Tier : SupportTier.Off;
            float support = SupportMath.Clamp(own.Support);
            SupportDef def = family.Support ?? new SupportDef();

            var row = new SupportRowInfo
            {
                FamilyId = family.Id,
                Support = support,
                Tier = tier,
                Target = SupportMath.Target(def, tier, config),
                Rate = SupportMath.Rate(def, tier, config),
                Cap = SupportMath.Clamp(def.TargetCap),
                BaseUpkeep = def.UpkeepPerMonth,
                TierFactor = config.Tier(tier).cost,
                EraFactor = SupportMath.EraCost(def.EraCost, year),
                Inflation = SupportMath.SafeInflation(inflation),
                Editable = editable,
            };
            row.Upkeep = SupportMath.MonthlyUpkeep(def, tier, year, inflation, config);
            row.Forecast = SupportMath.Forecast(support, tier, def, config, SupportMath.ForecastMonths);
            row.RatingFactor = ArchRatingMath.SupportFactor(support, balance.rating);
            return row;
        }

        /// <summary>The rows of the section: every family the company has researched, in the order of the catalog; none for a null state.</summary>
        public static List<SupportRowInfo> Rows(ArchCatalog catalog, CompanyArchState state, BalanceConfig balance, int year, float inflation, bool editable)
        {
            var rows = new List<SupportRowInfo>();
            if (catalog == null || state == null || balance == null) return rows;

            foreach (ArchFamily family in catalog.Families)
            {
                SupportRowInfo row = RowOf(family, state.GetFamily(family.Id), balance, year, inflation, editable);
                if (row != null) rows.Add(row);
            }
            return rows;
        }

        /// <summary>The upkeep of all the rows together, a month (the line under the section).</summary>
        public static float TotalUpkeep(IEnumerable<SupportRowInfo> rows)
        {
            float total = 0f;
            if (rows == null) return total;
            foreach (SupportRowInfo row in rows) total += row.Upkeep;
            return total;
        }
    }
}
