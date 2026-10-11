using System;
using System.Collections.Generic;

namespace PTMod.Features.CpuArchitectures
{
    /// <summary>
    /// What a seed gives one company (FR-8; design.md Component 8): the families it has researched (catalog order), the family it designs on, and the
    /// starting support level and tier of every researched family. Plain data made by <see cref="ArchSeedPolicy"/>; <see cref="ApplyTo"/> writes it
    /// into the company's <see cref="CompanyArchState"/>.
    /// </summary>
    public sealed class SeedResult
    {
        /// <summary>The researched families in the order of the catalog; always holds the default family.</summary>
        public readonly List<string> Researched = new List<string>();

        /// <summary>The active family (AI) or the family the designer opens on (player).</summary>
        public string Active;

        /// <summary>The starting support level 0..100 of every researched family (stage 2 uses it; stage 1 only stores it).</summary>
        public readonly Dictionary<string, float> Support = new Dictionary<string, float>(StringComparer.Ordinal);

        /// <summary>The starting support tier of every researched family.</summary>
        public readonly Dictionary<string, SupportTier> Tiers = new Dictionary<string, SupportTier>(StringComparer.Ordinal);

        /// <summary>
        /// Writes the seed into <paramref name="state"/>: every family of the result becomes researched (progress 1, the support and the tier of the
        /// seed), unless the company already has it researched - then the company's own support and tier stay. <c>Active</c> is set only when the
        /// state has none, so sowing twice, or sowing a company that already chose, changes nothing. Returns how many families became researched.
        /// </summary>
        public int ApplyTo(CompanyArchState state)
        {
            if (state == null) throw new ArgumentNullException("state");

            int added = 0;
            foreach (string id in Researched)
            {
                FamilyState family = state.GetOrAddFamily(id);
                if (family.Researched) continue;

                family.Researched = true;
                family.Progress = 1f;
                float support;
                family.Support = Support.TryGetValue(id, out support) ? support : 0f;
                SupportTier tier;
                family.Tier = Tiers.TryGetValue(id, out tier) ? tier : SupportTier.Off;
                added++;
            }
            if (state.Active == null && Active != null) state.Active = Active;
            return added;
        }
    }

    /// <summary>
    /// The choices of the seed (FR-8; design.md, "Посев"): which families a company has researched when the game starts or the mod meets an old save, and
    /// which family a CPU without a binding belongs to. Pure and deterministic: the services read the game (the unlock year U of the player, the year of
    /// the date, the <c>UniqueID</c> of an AI company) and hand the numbers in.
    /// <list type="bullet">
    /// <item><b>Player</b> - <c>proprietary</c> and the families of <c>seed.playerPath</c> whose year is at most U (the latest year of a technology he has
    /// researched), with the families they require; the newest one is the starting choice with support 50 (tier "normal"), the others 30 ("off").</item>
    /// <item><b>AI company</b> - <c>proprietary</c> and every family with a weight above 0 whose year plus the lag of the company is at most the year,
    /// with the families they require; the active one has the largest weight (a tie goes to the newer family) and support 60 ("normal"), the others 35 ("off").</item>
    /// <item><b>A CPU without a binding</b> - for an AI company the family with the largest weight among those that existed (with the lag) in the year of
    /// the CPU, for the player the newest family of the path that existed then; otherwise <c>proprietary</c>.</item>
    /// </list>
    /// </summary>
    public static class ArchSeedPolicy
    {
        /// <summary>The seed of an AI company in <paramref name="year"/> (its preference record, the lag included).</summary>
        public static SeedResult SeedAi(ArchCatalog cat, AiPreference pref, int year, SeedConfig cfg)
        {
            if (cat == null) throw new ArgumentNullException("cat");
            if (pref == null) pref = AiPreferenceTable.Fallback();
            if (cfg == null) cfg = new SeedConfig();

            var wanted = new List<string>();
            foreach (ArchFamily family in cat.Families)
            {
                if (pref.WeightOf(family.Id) > 0f && ExistsBy(family, pref.LagYears, year)) wanted.Add(family.Id);
            }

            HashSet<string> researched = Closure(cat, wanted);
            AddDefault(cat, researched);

            var result = new SeedResult();
            FillOrdered(cat, researched, result);
            result.Active = ArchAiPolicy.PickActive(cat, pref, researched);

            foreach (string id in result.Researched)
            {
                bool active = string.Equals(id, result.Active, StringComparison.Ordinal);
                result.Support[id] = active ? cfg.aiActiveSupport : cfg.aiOtherSupport;
                result.Tiers[id] = active ? SupportTier.Normal : SupportTier.Off;
            }
            return result;
        }

        /// <summary>
        /// The seed of the player, <paramref name="unlockYear"/> being the latest <c>Technology.Year</c> among the technologies he has researched (U;
        /// 0 or less when he has none: only the default family is given).
        /// </summary>
        public static SeedResult SeedPlayer(ArchCatalog cat, int unlockYear, SeedConfig cfg)
        {
            if (cat == null) throw new ArgumentNullException("cat");
            if (cfg == null) cfg = new SeedConfig();

            var wanted = new List<string>();
            foreach (string id in PathOf(cat, cfg))
            {
                ArchFamily family = cat.Get(id);
                if (family != null && unlockYear > 0 && family.Year <= unlockYear) wanted.Add(id);
            }

            HashSet<string> researched = Closure(cat, wanted);
            AddDefault(cat, researched);

            var result = new SeedResult();
            FillOrdered(cat, researched, result);
            result.Active = Newest(cat, result.Researched);

            foreach (string id in result.Researched)
            {
                bool newest = string.Equals(id, result.Active, StringComparison.Ordinal);
                result.Support[id] = newest ? cfg.playerNewestSupport : cfg.playerOtherSupport;
                result.Tiers[id] = newest ? SupportTier.Normal : SupportTier.Off;
            }
            return result;
        }

        /// <summary>
        /// The family a CPU without a binding belongs to (FR-5 item 3, FR-8 item 5). An AI company (<paramref name="pref"/> its record; null gives the
        /// default family): the family with the largest weight among those that existed in <paramref name="cpuYear"/> with the lag of the company, a
        /// tie going to the newer. The player: the newest family of <c>seed.playerPath</c> with a year up to <paramref name="cpuYear"/>. Without a match
        /// the default family. Never null.
        /// </summary>
        public static string DefaultForCpu(ArchCatalog cat, AiPreference prefOrNull, bool isPlayer, int cpuYear, SeedConfig cfg)
        {
            if (cat == null) throw new ArgumentNullException("cat");
            if (cfg == null) cfg = new SeedConfig();

            string best = null;
            if (isPlayer)
            {
                var existing = new List<string>();
                foreach (string id in PathOf(cat, cfg))
                {
                    ArchFamily family = cat.Get(id);
                    if (family != null && family.Year <= cpuYear) existing.Add(id);
                }
                best = Newest(cat, existing);
            }
            else if (prefOrNull != null)
            {
                float bestWeight = 0f;
                foreach (ArchFamily family in cat.Families)
                {
                    float weight = prefOrNull.WeightOf(family.Id);
                    if (!(weight > 0f) || !ExistsBy(family, prefOrNull.LagYears, cpuYear)) continue;
                    if (best == null || ArchAiPolicy.IsBetterActive(weight, family, bestWeight, cat.Get(best)))
                    {
                        best = family.Id;
                        bestWeight = weight;
                    }
                }
            }
            return best ?? ArchCatalog.DefaultId;
        }

        /// <summary>
        /// The families with their requirements, transitively (a catalog is acyclic; a cycle would end at the set anyway). Ids the catalog does not
        /// know are dropped.
        /// </summary>
        public static HashSet<string> Closure(ArchCatalog cat, IEnumerable<string> ids)
        {
            var set = new HashSet<string>(StringComparer.Ordinal);
            if (cat == null || ids == null) return set;

            var stack = new Stack<string>();
            foreach (string id in ids) stack.Push(id);
            while (stack.Count > 0)
            {
                string id = stack.Pop();
                ArchFamily family = cat.Get(id);
                if (family == null || !set.Add(family.Id)) continue;
                if (family.Requires == null) continue;
                foreach (string required in family.Requires) stack.Push(required);
            }
            return set;
        }

        /// <summary>The families of <c>seed.playerPath</c> the catalog knows, in the order of the path, the default family first.</summary>
        public static List<string> PathOf(ArchCatalog cat, SeedConfig cfg)
        {
            var path = new List<string>();
            if (cat != null && cat.Default != null) path.Add(cat.Default.Id);
            if (cat == null || cfg == null || cfg.playerPath == null) return path;

            foreach (string id in cfg.playerPath)
            {
                if (id != null && cat.Contains(id) && !path.Contains(id)) path.Add(id);
            }
            return path;
        }

        /// <summary>The newest family of the list: the largest year, of two of the same year the later one in the list. Null for an empty list.</summary>
        private static string Newest(ArchCatalog cat, IList<string> ids)
        {
            string best = null;
            int bestYear = int.MinValue;
            foreach (string id in ids)
            {
                ArchFamily family = cat.Get(id);
                if (family == null || family.Year < bestYear) continue;
                best = id;
                bestYear = family.Year;
            }
            return best;
        }

        /// <summary>A family exists for a company with this lag in this year (FR-7 item 1: year + lag ≤ the year).</summary>
        internal static bool ExistsBy(ArchFamily family, int lagYears, int year)
        {
            return family != null && (long)family.Year + lagYears <= year;
        }

        private static void AddDefault(ArchCatalog cat, ISet<string> set)
        {
            if (cat.Default != null) set.Add(cat.Default.Id);
        }

        /// <summary>The ids of the set in the order of the catalog.</summary>
        private static void FillOrdered(ArchCatalog cat, ISet<string> set, SeedResult result)
        {
            foreach (ArchFamily family in cat.Families)
            {
                if (set.Contains(family.Id)) result.Researched.Add(family.Id);
            }
        }
    }
}
