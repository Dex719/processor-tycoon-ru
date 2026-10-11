using System;
using System.Collections.Generic;

namespace PTMod.Features.CpuArchitectures
{
    /// <summary>
    /// The arithmetic of the software support (FR-10, stage 2; design.md Component 10, "Поддержка"). Pure: the family's <see cref="SupportDef"/> and the
    /// four tiers of <c>balance.json</c> (<see cref="SupportConfig"/>) come in as arguments, nothing of the game is read.
    /// <code>
    /// S′     = S + (min(T_tier, targetCap) − S) × r_tier         r_off = decayRate of the family, T_off = 0
    /// upkeep = upkeepPerMonth × c_tier × eraCost(Y) × inflation
    /// </code>
    /// With the built-in numbers: "normal" (T 70, r 0.10, c 1) takes 30 to 58.7 in 12 months (AC-10.1); "off" takes the 60 of ARM (decay 0.04) to 36.8
    /// (AC-10.2); <c>proprietary</c> never rises above its <c>targetCap</c> of 60 whatever the tier. The support level is held to 0..100 after every step.
    /// </summary>
    public static class SupportMath
    {
        /// <summary>The top of the scale of the support.</summary>
        public const float MaxSupport = 100f;

        /// <summary>The horizon of the forecast of the panel (FR-10 item 7): twelve months.</summary>
        public const int ForecastMonths = 12;

        private const int MaxForecastMonths = 1200;

        /// <summary>A support level held to 0..100; an unusable number is 0.</summary>
        public static float Clamp(float support)
        {
            if (float.IsNaN(support) || float.IsInfinity(support) || support < 0f) return 0f;
            return support > MaxSupport ? MaxSupport : support;
        }

        /// <summary>The level the tier leads to: 0 for "off", otherwise the target of the tier held to the <c>targetCap</c> of the family.</summary>
        public static float Target(SupportDef def, SupportTier tier, SupportConfig config)
        {
            if (config == null) config = new SupportConfig();
            if (def == null) def = new SupportDef();

            float target = config.Tier(tier).target;
            if (tier == SupportTier.Off) return Clamp(target);

            float cap = float.IsNaN(def.TargetCap) ? MaxSupport : Clamp(def.TargetCap);
            return Clamp(Math.Min(target, cap));
        }

        /// <summary>The share of the distance to the target covered in a month: <c>decayRate</c> of the family for "off", the rate of the tier otherwise.</summary>
        public static float Rate(SupportDef def, SupportTier tier, SupportConfig config)
        {
            if (config == null) config = new SupportConfig();
            if (def == null) def = new SupportDef();

            float rate = tier == SupportTier.Off ? def.DecayRate : config.Tier(tier).rate;
            if (float.IsNaN(rate) || rate < 0f) return 0f;
            return rate > 1f ? 1f : rate;
        }

        /// <summary>The support after one month (FR-10 item 2): <c>S + (T − S) × r</c>, held to 0..100.</summary>
        public static float Next(float support, SupportTier tier, SupportDef def, SupportConfig config)
        {
            float s = Clamp(support);
            float target = Target(def, tier, config);
            float rate = Rate(def, tier, config);
            return Clamp(s + (target - s) * rate);
        }

        /// <summary>The support after <paramref name="months"/> months at the same tier (the forecast of the panel); the level itself for no months.</summary>
        public static float Forecast(float support, SupportTier tier, SupportDef def, SupportConfig config, int months)
        {
            float s = Clamp(support);
            int count = months > MaxForecastMonths ? MaxForecastMonths : months;
            for (int i = 0; i < count; i++) s = Next(s, tier, def, config);
            return s;
        }

        /// <summary>
        /// The factor of the era on the upkeep: the points <c>[year, factor]</c> are joined by straight lines, before the first and after the last the
        /// factor of that end holds (ARM: 1.3 in 1987, 1.0 in 2000, 0.6 from 2010). 1 for a family without points. Years of the points ascend (the
        /// validator of the catalog sees to it); a pair that is not a pair is skipped.
        /// </summary>
        public static float EraCost(float[][] eraCost, int year)
        {
            if (eraCost == null || eraCost.Length == 0) return 1f;

            float[] previous = null;
            for (int i = 0; i < eraCost.Length; i++)
            {
                float[] point = eraCost[i];
                if (point == null || point.Length != 2 || float.IsNaN(point[0]) || float.IsNaN(point[1])) continue;

                if (previous == null)
                {
                    if (year <= point[0]) return point[1];
                }
                else if (year <= point[0])
                {
                    float span = point[0] - previous[0];
                    if (span <= 0f) return point[1];
                    float t = (year - previous[0]) / span;
                    return previous[1] + (point[1] - previous[1]) * t;
                }
                previous = point;
            }
            return previous == null ? 1f : previous[1];
        }

        /// <summary>
        /// The upkeep of one month at the tier (FR-10 item 3): <c>upkeepPerMonth × c(tier) × eraCost(year) × inflation</c>; 0 for "off". An unusable
        /// inflation counts as 1. Never negative.
        /// </summary>
        public static float MonthlyUpkeep(SupportDef def, SupportTier tier, int year, float inflation, SupportConfig config)
        {
            if (config == null) config = new SupportConfig();
            if (def == null) return 0f;

            float cost = config.Tier(tier).cost;
            if (float.IsNaN(cost) || cost <= 0f || !(def.UpkeepPerMonth > 0f)) return 0f;

            float upkeep = def.UpkeepPerMonth * cost * EraCost(def.EraCost, year) * SafeInflation(inflation);
            return float.IsNaN(upkeep) || float.IsInfinity(upkeep) || upkeep < 0f ? 0f : upkeep;
        }

        /// <summary>The price level the upkeep is multiplied by: the inflation of the game, 1 when it is not known (zero, negative, not a number).</summary>
        public static float SafeInflation(float inflation)
        {
            return float.IsNaN(inflation) || float.IsInfinity(inflation) || inflation <= 0f ? 1f : inflation;
        }

        /// <summary>
        /// The support a company starts a newly researched family with (FR-12 item 2): <c>max(startOnResearch, inheritSameIsa × the highest support among its other
        /// researched families of the same ISA)</c> - the tools of x86 are the tools of x86-64 up to a point (x86 at 80 gives x86-64 a start of 40). A family with no
        /// relative, an unknown family and a company with no states start from <c>startOnResearch</c>.
        /// </summary>
        public static float StartSupport(ArchFamily family, IReadOnlyDictionary<string, FamilyState> states, ArchCatalog catalog, SupportConfig config)
        {
            if (config == null) config = new SupportConfig();
            float start = Clamp(config.startOnResearch);
            if (family == null || states == null || catalog == null) return start;

            float best = 0f;
            foreach (KeyValuePair<string, FamilyState> pair in states)
            {
                FamilyState state = pair.Value;
                if (state == null || !state.Researched || pair.Key == family.Id) continue;
                ArchFamily other = catalog.Get(pair.Key);
                if (other == null || other.Isa != family.Isa) continue;
                best = Math.Max(best, Clamp(state.Support));
            }

            float inherited = Clamp(config.inheritSameIsa) * best;
            return Math.Max(start, Clamp(inherited));
        }

        /// <summary>The support level as the whole number the panel writes (87.5 is "88"); the scale is 0..100.</summary>
        public static int Score(float support)
        {
            return (int)Math.Round(Clamp(support), MidpointRounding.AwayFromZero);
        }

        /// <summary>A share per month as a whole percent for the texts (0.06 is 6).</summary>
        public static int PercentOf(float share)
        {
            if (float.IsNaN(share) || float.IsInfinity(share) || share <= 0f) return 0;
            return (int)Math.Round(share * 100f, MidpointRounding.AwayFromZero);
        }
    }
}
