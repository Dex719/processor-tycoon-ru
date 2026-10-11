using System;
using System.Collections.Generic;
using System.Globalization;
using PTMod.Core.Logic.Json;

namespace PTMod.Features.CpuArchitectures
{
    /// <summary>
    /// Research numbers (FR-2, FR-12): the copy of the game's year penalty and bonus of <c>TechnologyTimeUpdater</c> as fractions per year, and the
    /// factor of the research time of a family whose ISA the company already knows (stage 2).
    /// </summary>
    public sealed class ResearchConfig
    {
        public float penaltyPerYear = 3.5f;
        public float bonusPerYear = 0.15f;
        public float bonusCap = 0.75f;
        public float isaExperienceFactor = 0.6f;
    }

    /// <summary>One level of the software support (FR-10): the level it converges to, how fast, and the price relative to "normal".</summary>
    public sealed class SupportTierDef
    {
        public float target;
        public float rate;
        public float cost;
    }

    /// <summary>The four tiers of the software support, as <c>support.tiers</c> of the file names them.</summary>
    public sealed class SupportTiersConfig
    {
        public SupportTierDef off = new SupportTierDef { target = 0f, rate = 0f, cost = 0f };
        public SupportTierDef low = new SupportTierDef { target = 40f, rate = 0.10f, cost = 0.5f };
        public SupportTierDef normal = new SupportTierDef { target = 70f, rate = 0.10f, cost = 1.0f };
        public SupportTierDef high = new SupportTierDef { target = 95f, rate = 0.10f, cost = 2.0f };
    }

    /// <summary>Software support (stage 2): the four tiers, the start level of a newly researched family and the share inherited from the same ISA.</summary>
    public sealed class SupportConfig
    {
        public SupportTiersConfig tiers = new SupportTiersConfig();
        public float startOnResearch = 30f;
        public float inheritSameIsa = 0.5f;

        /// <summary>The definition of a tier.</summary>
        public SupportTierDef Tier(SupportTier tier)
        {
            switch (tier)
            {
                case SupportTier.Low: return tiers.low;
                case SupportTier.Normal: return tiers.normal;
                case SupportTier.High: return tiers.high;
                default: return tiers.off;
            }
        }
    }

    /// <summary>The ecosystem of an architecture (stage 2): the sales window, the family counted when nothing was sold, the milestones announced to the player.</summary>
    public sealed class EcosystemConfig
    {
        public int windowMonths = 12;
        public string fallbackFamily = ArchCatalog.DefaultId;
        public int[] milestones = { 25, 50, 75 };
    }

    /// <summary>Immature tools of a family new to the company (stage 2): how long and how much more a project costs.</summary>
    public sealed class MigrationConfig
    {
        public int immatureMonths = 12;
        public float immatureDevCost = 0.25f;
    }

    /// <summary>
    /// A gate of a CPU extension (FR-13, stage 2): the extension gives its full rating effect only when the support of the family reaches
    /// <see cref="requiredSupport"/>; below that the multiplier moves from <see cref="penalty"/> up to 1.
    /// </summary>
    public sealed class ExtensionDef
    {
        public const string TriggerCoreCount = "coreCount";
        public const string TriggerSmt = "smt";

        public string id;

        /// <summary><see cref="TriggerCoreCount"/> (the CPU has at least <see cref="minCores"/> cores) or <see cref="TriggerSmt"/> (the CPU has SMT).</summary>
        public string trigger = TriggerCoreCount;

        public int minCores = 1;
        public float requiredSupport = 40f;
        public float penalty = 0.95f;
    }

    /// <summary>The numbers of the AI (FR-7, FR-14): the funding by difficulty, the reserve, the rule of not interrupting a nearly finished technology, the switch rule.</summary>
    public sealed class AiConfig
    {
        public const int DifficultyLevels = 5;

        /// <summary>The <c>ResearchSector.Funding</c> an AI company gets for an architecture, by <c>Player.DifficultyLevel</c> 0…4.</summary>
        public float[] fundingByDifficulty = { 0.6f, 0.8f, 1.0f, 1.3f, 1.6f };

        public int reserveMonths = 6;

        /// <summary>The AI does not interrupt a technology that needs this many days or fewer (FR-7 item 1).</summary>
        public int noInterruptTimeLeftDays = 30;

        public float switchMargin = 1.10f;
        public float switchMinSupport = 35f;
        public int normalTierReserveMonths = 12;
        public float candidateEcoForLowTier = 0.2f;
        public int salesWindowMonths = 3;
    }

    /// <summary>What a new game and a migrated save start from (FR-8): the path of the player and the starting support levels.</summary>
    public sealed class SeedConfig
    {
        public string[] playerPath = { ArchCatalog.DefaultId, "x86", "x86_64" };
        public float aiActiveSupport = 60f;
        public float aiOtherSupport = 35f;
        public float playerNewestSupport = 50f;
        public float playerOtherSupport = 30f;
    }

    /// <summary>The royalty for x86 without a licence (FR-16, stage 2): the ISA, the <c>AICompany.UniqueID</c> of the holders and the share of the revenue.</summary>
    public sealed class X86LicenseConfig
    {
        public string isa = "x86";
        public int[] holderIds = { 0, 1 };
        public float royaltyRate = 0.03f;
    }

    /// <summary>
    /// All numbers of <c>balance.json</c> (NFR-4: every balance number lives in the content): fields mirror the JSON, the defaults are the values of the
    /// built-in file, so the feature works with a damaged or partly overridden file. <see cref="FromJson"/> binds the merged file and
    /// <see cref="Normalize"/> replaces every unusable value by its default with a note.
    /// </summary>
    public sealed class BalanceConfig
    {
        public const int Stage1 = 1;
        public const int Stage2 = 2;
        public const int MaxExtensions = 8;

        /// <summary>1 - the MVP (support, ecosystem and extensions give ×1, no royalty); 2 - the full version.</summary>
        public int stage = Stage1;

        public ResearchConfig research = new ResearchConfig();
        public RatingConfig rating = new RatingConfig();
        public SupportConfig support = new SupportConfig();
        public EcosystemConfig ecosystem = new EcosystemConfig();
        public MigrationConfig migration = new MigrationConfig();
        public List<ExtensionDef> extensions = DefaultExtensions();
        public AiConfig ai = new AiConfig();
        public SeedConfig seed = new SeedConfig();
        public X86LicenseConfig x86License = new X86LicenseConfig();

        /// <summary>The stage of the feature is 2 (the formulas of the full version are on).</summary>
        public bool IsFullStage
        {
            get { return stage >= Stage2; }
        }

        /// <summary>The values of the built-in file.</summary>
        public static BalanceConfig Defaults()
        {
            return new BalanceConfig();
        }

        /// <summary>
        /// Binds the config object that <c>ModContent.LoadConfig</c> returned (null gives the defaults), then <see cref="Normalize"/>s it. Every
        /// problem (a member of the wrong kind, a value out of range) is added to <paramref name="problems"/> and the default stays in its place.
        /// </summary>
        public static BalanceConfig FromJson(JsonObject json, List<string> problems)
        {
            if (problems == null) problems = new List<string>();
            if (json == null) return Defaults();

            var errors = new List<string>();
            BalanceConfig config = ModJson.Bind<BalanceConfig>(json, errors);
            foreach (string error in errors) problems.Add("balance" + error.Substring(error.StartsWith("$", StringComparison.Ordinal) ? 1 : 0));
            if (config == null) config = Defaults();

            config.Normalize(problems);
            return config;
        }

        private static List<ExtensionDef> DefaultExtensions()
        {
            return new List<ExtensionDef>
            {
                new ExtensionDef { id = "multicore", trigger = ExtensionDef.TriggerCoreCount, minCores = 2, requiredSupport = 40f, penalty = 0.92f },
                new ExtensionDef { id = "smt", trigger = ExtensionDef.TriggerSmt, minCores = 1, requiredSupport = 50f, penalty = 0.95f },
            };
        }

        // ---- normalization ----

        /// <summary>
        /// Replaces every member that is null, not finite or out of its range by the default and adds a note ("research.bonusCap: 5 is outside
        /// 0..0.95, 0.75 is used"). Lists and arrays are checked as a whole: a wrong length or an unusable entry restores the default list (or drops the entry).
        /// </summary>
        public void Normalize(List<string> problems)
        {
            if (problems == null) problems = new List<string>();
            var d = new BalanceConfig();

            if (stage != Stage1 && stage != Stage2)
            {
                problems.Add("stage: " + Num(stage) + " is not 1 or 2, 1 is used");
                stage = Stage1;
            }

            if (research == null) { problems.Add("research: is missing, the defaults are used"); research = d.research; }
            research.penaltyPerYear = Fix(research.penaltyPerYear, 0f, 20f, d.research.penaltyPerYear, "research.penaltyPerYear", problems);
            research.bonusPerYear = Fix(research.bonusPerYear, 0f, 1f, d.research.bonusPerYear, "research.bonusPerYear", problems);
            research.bonusCap = Fix(research.bonusCap, 0f, 0.95f, d.research.bonusCap, "research.bonusCap", problems);
            research.isaExperienceFactor = Fix(research.isaExperienceFactor, 0.1f, 1f, d.research.isaExperienceFactor, "research.isaExperienceFactor", problems);

            if (rating == null) { problems.Add("rating: is missing, the defaults are used"); rating = d.rating; }
            rating.clampMin = Fix(rating.clampMin, 0.5f, 1f, d.rating.clampMin, "rating.clampMin", problems);
            rating.clampMax = Fix(rating.clampMax, 1f, 2f, d.rating.clampMax, "rating.clampMax", problems);
            rating.supportBase = Fix(rating.supportBase, 0.5f, 1.5f, d.rating.supportBase, "rating.supportBase", problems);
            rating.supportSpan = Fix(rating.supportSpan, 0f, 1f, d.rating.supportSpan, "rating.supportSpan", problems);
            rating.ecoBase = Fix(rating.ecoBase, 0.5f, 1.5f, d.rating.ecoBase, "rating.ecoBase", problems);
            rating.ecoSpan = Fix(rating.ecoSpan, 0f, 1f, d.rating.ecoSpan, "rating.ecoSpan", problems);
            rating.mvpSupport = Fix(rating.mvpSupport, 0f, 100f, d.rating.mvpSupport, "rating.mvpSupport", problems);

            NormalizeSupport(d.support, problems);
            NormalizeEcosystem(d.ecosystem, problems);

            if (migration == null) { problems.Add("migration: is missing, the defaults are used"); migration = d.migration; }
            migration.immatureMonths = FixInt(migration.immatureMonths, 0, 60, d.migration.immatureMonths, "migration.immatureMonths", problems);
            migration.immatureDevCost = Fix(migration.immatureDevCost, 0f, 1f, d.migration.immatureDevCost, "migration.immatureDevCost", problems);

            NormalizeExtensions(problems);
            NormalizeAi(d.ai, problems);
            NormalizeSeed(d.seed, problems);
            NormalizeLicense(d.x86License, problems);
        }

        /// <summary>
        /// Checks the references of the config to the catalog: a family that does not exist is dropped from <see cref="SeedConfig.playerPath"/>, the
        /// ecosystem fallback becomes the default family. Notes go to <paramref name="problems"/>.
        /// </summary>
        public void ValidateAgainst(ArchCatalog catalog, List<string> problems)
        {
            if (catalog == null) return;
            if (problems == null) problems = new List<string>();

            var path = new List<string>();
            foreach (string id in seed.playerPath)
            {
                if (catalog.Contains(id)) path.Add(id);
                else problems.Add("seed.playerPath: the family '" + id + "' does not exist, it is skipped");
            }
            seed.playerPath = path.ToArray();

            if (!catalog.Contains(ecosystem.fallbackFamily))
            {
                problems.Add("ecosystem.fallbackFamily: the family '" + ecosystem.fallbackFamily + "' does not exist, '" + ArchCatalog.DefaultId + "' is used");
                ecosystem.fallbackFamily = ArchCatalog.DefaultId;
            }
        }

        private void NormalizeSupport(SupportConfig d, List<string> problems)
        {
            if (support == null) { problems.Add("support: is missing, the defaults are used"); support = d; }
            if (support.tiers == null) { problems.Add("support.tiers: is missing, the defaults are used"); support.tiers = d.tiers; }

            SupportTiersConfig tiers = support.tiers;
            if (tiers.off == null) { problems.Add("support.tiers.off: is missing, the default is used"); tiers.off = d.tiers.off; }
            if (tiers.low == null) { problems.Add("support.tiers.low: is missing, the default is used"); tiers.low = d.tiers.low; }
            if (tiers.normal == null) { problems.Add("support.tiers.normal: is missing, the default is used"); tiers.normal = d.tiers.normal; }
            if (tiers.high == null) { problems.Add("support.tiers.high: is missing, the default is used"); tiers.high = d.tiers.high; }

            NormalizeTier(tiers.off, d.tiers.off, "off", problems);
            NormalizeTier(tiers.low, d.tiers.low, "low", problems);
            NormalizeTier(tiers.normal, d.tiers.normal, "normal", problems);
            NormalizeTier(tiers.high, d.tiers.high, "high", problems);
            support.startOnResearch = Fix(support.startOnResearch, 0f, 100f, d.startOnResearch, "support.startOnResearch", problems);
            support.inheritSameIsa = Fix(support.inheritSameIsa, 0f, 1f, d.inheritSameIsa, "support.inheritSameIsa", problems);
        }

        private static void NormalizeTier(SupportTierDef tier, SupportTierDef d, string name, List<string> problems)
        {
            string path = "support.tiers." + name;
            tier.target = Fix(tier.target, 0f, 100f, d.target, path + ".target", problems);
            tier.rate = Fix(tier.rate, 0f, 1f, d.rate, path + ".rate", problems);
            tier.cost = Fix(tier.cost, 0f, 10f, d.cost, path + ".cost", problems);
        }

        private void NormalizeEcosystem(EcosystemConfig d, List<string> problems)
        {
            if (ecosystem == null) { problems.Add("ecosystem: is missing, the defaults are used"); ecosystem = d; }
            ecosystem.windowMonths = FixInt(ecosystem.windowMonths, 1, 60, d.windowMonths, "ecosystem.windowMonths", problems);
            if (!ArchIds.IsFamilyId(ecosystem.fallbackFamily))
            {
                problems.Add("ecosystem.fallbackFamily: '" + ecosystem.fallbackFamily + "' is not a family id, '" + d.fallbackFamily + "' is used");
                ecosystem.fallbackFamily = d.fallbackFamily;
            }

            int[] milestones = ecosystem.milestones;
            bool ok = milestones != null && milestones.Length <= 8;
            if (ok)
            {
                for (int i = 0; i < milestones.Length; i++)
                {
                    if (milestones[i] < 1 || milestones[i] > 100 || (i > 0 && milestones[i] <= milestones[i - 1])) ok = false;
                }
            }
            if (!ok)
            {
                problems.Add("ecosystem.milestones: must be at most 8 ascending numbers in 1..100, the defaults are used");
                ecosystem.milestones = (int[])d.milestones.Clone();
            }
        }

        private void NormalizeExtensions(List<string> problems)
        {
            if (extensions == null)
            {
                problems.Add("extensions: is missing, the defaults are used");
                extensions = DefaultExtensions();
                return;
            }

            var kept = new List<ExtensionDef>();
            var ids = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < extensions.Count; i++)
            {
                ExtensionDef x = extensions[i];
                string path = "extensions[" + Num(i) + "]";
                if (x == null || !ArchIds.IsFamilyId(x.id))
                {
                    problems.Add(path + ": the id is missing or not [a-z0-9_]{1,32}, the entry is dropped");
                    continue;
                }
                if (x.trigger != ExtensionDef.TriggerCoreCount && x.trigger != ExtensionDef.TriggerSmt)
                {
                    problems.Add(path + ": the trigger '" + x.trigger + "' is not coreCount or smt, the entry is dropped");
                    continue;
                }
                if (!ids.Add(x.id))
                {
                    problems.Add(path + ": the id '" + x.id + "' is used twice, the entry is dropped");
                    continue;
                }
                if (kept.Count >= MaxExtensions)
                {
                    problems.Add(path + ": more than " + Num(MaxExtensions) + " extensions, the entry is dropped");
                    continue;
                }

                x.minCores = FixInt(x.minCores, 1, 64, 1, path + ".minCores", problems);
                x.requiredSupport = Fix(x.requiredSupport, 0f, 100f, 40f, path + ".requiredSupport", problems);
                x.penalty = Fix(x.penalty, 0.5f, 1f, 0.95f, path + ".penalty", problems);
                kept.Add(x);
            }
            extensions = kept;
        }

        private void NormalizeAi(AiConfig d, List<string> problems)
        {
            if (ai == null) { problems.Add("ai: is missing, the defaults are used"); ai = d; }

            bool fundingOk = ai.fundingByDifficulty != null && ai.fundingByDifficulty.Length == AiConfig.DifficultyLevels;
            if (fundingOk)
            {
                foreach (float f in ai.fundingByDifficulty)
                {
                    if (float.IsNaN(f) || float.IsInfinity(f) || f < 0.1f || f > 5f) fundingOk = false;
                }
            }
            if (!fundingOk)
            {
                problems.Add("ai.fundingByDifficulty: must be " + Num(AiConfig.DifficultyLevels) + " numbers in 0.1..5, the defaults are used");
                ai.fundingByDifficulty = (float[])d.fundingByDifficulty.Clone();
            }

            ai.reserveMonths = FixInt(ai.reserveMonths, 0, 36, d.reserveMonths, "ai.reserveMonths", problems);
            ai.noInterruptTimeLeftDays = FixInt(ai.noInterruptTimeLeftDays, 0, 3650, d.noInterruptTimeLeftDays, "ai.noInterruptTimeLeftDays", problems);
            ai.switchMargin = Fix(ai.switchMargin, 1f, 3f, d.switchMargin, "ai.switchMargin", problems);
            ai.switchMinSupport = Fix(ai.switchMinSupport, 0f, 100f, d.switchMinSupport, "ai.switchMinSupport", problems);
            ai.normalTierReserveMonths = FixInt(ai.normalTierReserveMonths, 0, 60, d.normalTierReserveMonths, "ai.normalTierReserveMonths", problems);
            ai.candidateEcoForLowTier = Fix(ai.candidateEcoForLowTier, 0f, 1f, d.candidateEcoForLowTier, "ai.candidateEcoForLowTier", problems);
            ai.salesWindowMonths = FixInt(ai.salesWindowMonths, 1, 24, d.salesWindowMonths, "ai.salesWindowMonths", problems);
        }

        private void NormalizeSeed(SeedConfig d, List<string> problems)
        {
            if (seed == null) { problems.Add("seed: is missing, the defaults are used"); seed = d; }

            bool pathOk = seed.playerPath != null && seed.playerPath.Length >= 1 && seed.playerPath.Length <= 8;
            if (pathOk)
            {
                foreach (string id in seed.playerPath)
                {
                    if (!ArchIds.IsFamilyId(id)) pathOk = false;
                }
            }
            if (!pathOk)
            {
                problems.Add("seed.playerPath: must be 1-8 family ids, the defaults are used");
                seed.playerPath = (string[])d.playerPath.Clone();
            }

            seed.aiActiveSupport = Fix(seed.aiActiveSupport, 0f, 100f, d.aiActiveSupport, "seed.aiActiveSupport", problems);
            seed.aiOtherSupport = Fix(seed.aiOtherSupport, 0f, 100f, d.aiOtherSupport, "seed.aiOtherSupport", problems);
            seed.playerNewestSupport = Fix(seed.playerNewestSupport, 0f, 100f, d.playerNewestSupport, "seed.playerNewestSupport", problems);
            seed.playerOtherSupport = Fix(seed.playerOtherSupport, 0f, 100f, d.playerOtherSupport, "seed.playerOtherSupport", problems);
        }

        private void NormalizeLicense(X86LicenseConfig d, List<string> problems)
        {
            if (x86License == null) { problems.Add("x86License: is missing, the defaults are used"); x86License = d; }

            if (!ArchIds.IsIsaId(x86License.isa))
            {
                problems.Add("x86License.isa: '" + x86License.isa + "' is not an ISA id, '" + d.isa + "' is used");
                x86License.isa = d.isa;
            }

            bool holdersOk = x86License.holderIds != null && x86License.holderIds.Length <= 8;
            if (holdersOk)
            {
                foreach (int id in x86License.holderIds)
                {
                    if (id < 0 || id > 99) holdersOk = false;
                }
            }
            if (!holdersOk)
            {
                problems.Add("x86License.holderIds: must be at most 8 company ids in 0..99, the defaults are used");
                x86License.holderIds = (int[])d.holderIds.Clone();
            }

            x86License.royaltyRate = Fix(x86License.royaltyRate, 0f, 0.3f, d.royaltyRate, "x86License.royaltyRate", problems);
        }

        // ---- helpers ----

        private static float Fix(float value, float min, float max, float fallback, string name, List<string> problems)
        {
            if (!float.IsNaN(value) && !float.IsInfinity(value) && value >= min && value <= max) return value;
            problems.Add(name + ": " + value.ToString("R", CultureInfo.InvariantCulture) + " is outside " + min.ToString("R", CultureInfo.InvariantCulture)
                         + ".." + max.ToString("R", CultureInfo.InvariantCulture) + ", " + fallback.ToString("R", CultureInfo.InvariantCulture) + " is used");
            return fallback;
        }

        private static int FixInt(int value, int min, int max, int fallback, string name, List<string> problems)
        {
            if (value >= min && value <= max) return value;
            problems.Add(name + ": " + Num(value) + " is outside " + Num(min) + ".." + Num(max) + ", " + Num(fallback) + " is used");
            return fallback;
        }

        private static string Num(int value)
        {
            return value.ToString(CultureInfo.InvariantCulture);
        }
    }
}
