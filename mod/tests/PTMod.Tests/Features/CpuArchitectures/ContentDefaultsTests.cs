using System;
using System.Collections.Generic;
using System.Linq;
using PTMod.Core.Logic.Content;
using PTMod.Core.Logic.Json;
using PTMod.Features.CpuArchitectures;

namespace PTMod.Tests.Features.CpuArchitectures
{
    /// <summary>
    /// The three built-in files (TSK-002): <c>architectures.json</c>, <c>balance.json</c> and <c>ai-preferences.json</c> are valid, say what the owner
    /// approved (7 families, the AI preferences by <c>uniqueId</c>, the numbers of the balance), and the player's files override them. The balance
    /// config and the AI table are checked rule by rule, as is the neutrality rule of the feature.
    /// </summary>
    [Covers(typeof(BalanceConfig), typeof(ResearchConfig), typeof(RatingConfig), typeof(SupportTierDef), typeof(SupportTiersConfig), typeof(SupportConfig),
            typeof(EcosystemConfig), typeof(MigrationConfig), typeof(ExtensionDef), typeof(AiConfig), typeof(SeedConfig), typeof(X86LicenseConfig),
            typeof(AiPreference), typeof(AiPreferenceTable), typeof(ArchNeutrality))]
    public class ContentDefaultsTests
    {
        private static BalanceConfig BalanceFrom(JsonObject json, out List<string> problems)
        {
            problems = new List<string>();
            return BalanceConfig.FromJson(json, problems);
        }

        private static string Json(BalanceConfig config)
        {
            return ModJson.Write(ModJson.From(config), false);
        }

        private static ContentLoad<AiPreference> LoadPreferences(string overrideText = null)
        {
            return ContentMerger.MergeRecords<AiPreference>(ArchTestKit.FeatureId, "ai-preferences", ArchTestKit.ReadContent("ai-preferences"), overrideText,
                AiPreference.Map, AiPreference.ValidateRecord, 1);
        }

        private static AiPreference Preference(string id, int? uniqueId, params string[] families)
        {
            var preference = new AiPreference { Id = id, LagYears = 1 };
            if (uniqueId.HasValue) preference.UniqueId = uniqueId.Value;
            else preference.IsDefault = true;
            foreach (string family in families) preference.Weights[family] = 1f;
            return preference;
        }

        // ---- architectures.json ----

        [Test]
        public void ArchitecturesJson_IsValid_AndHasTheSevenFamiliesOfTheOwner()
        {
            ContentLoad<ArchFamily> load = ArchTestKit.LoadFamilies();
            ArchCatalog catalog;
            ValidationResult result;
            bool usable = ArchCatalog.TryCreate(load.Records, out catalog, out result);

            Assert.Equal(0, load.Report.Errors.Count, ArchTestKit.Joined(load.Report.Errors));
            Assert.Equal(0, load.Report.Warnings.Count, ArchTestKit.Joined(load.Report.Warnings));
            Assert.True(usable, ArchTestKit.Joined(result.Errors));
            Assert.Equal(0, result.Errors.Count, ArchTestKit.Joined(result.Errors));
            Assert.Equal(0, result.Warnings.Count, ArchTestKit.Joined(result.Warnings));

            Assert.True(ArchTestKit.Ids(catalog.Families).SequenceEqual(new[] { "proprietary", "x86", "risc", "arm", "x86_64", "arm64", "riscv" }),
                string.Join(",", ArchTestKit.Ids(catalog.Families)));
            var years = new Dictionary<string, int>
            {
                { "proprietary", 1971 }, { "x86", 1978 }, { "risc", 1986 }, { "arm", 1987 }, { "x86_64", 2003 }, { "arm64", 2011 }, { "riscv", 2014 },
            };
            foreach (KeyValuePair<string, int> pair in years) Assert.Equal(pair.Value, catalog.Get(pair.Key).Year, pair.Key);
        }

        [Test]
        public void ArchitecturesJson_DependenciesIsaAndFadeAreAsApproved()
        {
            ArchCatalog catalog = ArchTestKit.BuiltInCatalog();

            Assert.True(catalog.Get("x86_64").Requires.SequenceEqual(new[] { "x86" }));
            Assert.True(catalog.Get("arm64").Requires.SequenceEqual(new[] { "arm" }));
            foreach (ArchFamily family in catalog.Families.Where(f => f.Id != "x86_64" && f.Id != "arm64"))
                Assert.Equal(0, family.Requires.Length, family.Id + " requires nothing");

            Assert.Equal("own", catalog.Get("proprietary").Isa);
            Assert.Equal("x86", catalog.Get("x86").Isa);
            Assert.Equal("x86", catalog.Get("x86_64").Isa);
            Assert.Equal("arm", catalog.Get("arm64").Isa);
            Assert.Equal("riscv", catalog.Get("riscv").Isa);

            // Only the own ISA ages (D6): 1985-1995, down to 0.90, in the three consumer segments.
            foreach (ArchFamily family in catalog.Families) Assert.True((family.Fade != null) == (family.Id == "proprietary"), family.Id);
            Assert.Equal(1985, catalog.Get("proprietary").Fade.FromYear);
            Assert.Equal(1995, catalog.Get("proprietary").Fade.ToYear);
            Assert.True(catalog.Get("proprietary").Fade.Segments.SequenceEqual(new[] { 0, 1, 2 }));
        }

        [Test]
        public void ArchitecturesJson_Numbers_MatchTheDesignTable()
        {
            ArchCatalog catalog = ArchTestKit.BuiltInCatalog();

            // id, baseDays, monthlyCost, ipc, power, devCost, affinity HighEnd/MidRange/LowEnd/Industries
            AssertFamily(catalog, "proprietary", 0, 0, 1.00f, 1.00f, 1.00f, 1.00f, 1.00f, 1.00f, 1.00f);
            AssertFamily(catalog, "x86", 240, 5000000, 1.05f, 1.10f, 1.15f, 1.05f, 1.08f, 1.05f, 0.95f);
            AssertFamily(catalog, "risc", 300, 15000000, 1.12f, 0.90f, 1.05f, 1.10f, 0.95f, 0.92f, 1.05f);
            AssertFamily(catalog, "arm", 240, 8000000, 0.95f, 0.65f, 0.85f, 0.90f, 0.95f, 1.05f, 1.10f);
            AssertFamily(catalog, "x86_64", 360, 150000000, 1.10f, 1.05f, 1.10f, 1.10f, 1.08f, 1.05f, 0.95f);
            AssertFamily(catalog, "arm64", 300, 120000000, 1.08f, 0.70f, 0.95f, 0.95f, 1.02f, 1.08f, 1.10f);
            AssertFamily(catalog, "riscv", 180, 50000000, 1.03f, 0.75f, 0.80f, 0.90f, 0.92f, 1.00f, 1.10f);
        }

        private static void AssertFamily(ArchCatalog catalog, string id, float baseDays, float monthlyCost, float ipc, float power, float devCost,
            float high, float mid, float low, float industries)
        {
            ArchFamily f = catalog.Get(id);
            Assert.NotNull(f, id);
            Assert.Near(baseDays, f.Research.BaseDays, 1e-3, id + " baseDays");
            Assert.Near(monthlyCost, f.Research.MonthlyCost, 1.0, id + " monthlyCost");
            Assert.Near(ipc, f.Design.Ipc, 1e-6, id + " ipc");
            Assert.Near(power, f.Design.Power, 1e-6, id + " power");
            Assert.Near(devCost, f.Design.DevCost, 1e-6, id + " devCost");
            Assert.Equal(4, f.Affinity.Length, id + " affinity");
            Assert.Near(high, f.Affinity[0], 1e-6, id + " HighEnd");
            Assert.Near(mid, f.Affinity[1], 1e-6, id + " MidRange");
            Assert.Near(low, f.Affinity[2], 1e-6, id + " LowEnd");
            Assert.Near(industries, f.Affinity[3], 1e-6, id + " Industries");
        }

        [Test]
        public void ArchitecturesJson_TheOwnIsaIsFreeAndInstant_TheOthersCostMoney()
        {
            ArchCatalog catalog = ArchTestKit.BuiltInCatalog();

            Assert.Near(0, catalog.Default.Research.BaseDays, 0, "proprietary is researched at once (baseDays 0)");
            Assert.Near(0, catalog.Default.Research.MonthlyCost, 0);
            foreach (ArchFamily family in catalog.Families.Where(f => f.Id != "proprietary"))
            {
                Assert.True(family.Research.BaseDays > 0 && family.Research.MonthlyCost > 0, family.Id + " takes time and money");
            }
        }

        // ---- balance.json ----

        [Test]
        public void BalanceJson_BindsWithoutProblems_AndEqualsTheDefaultsOfTheCode()
        {
            List<string> problems;
            BalanceConfig fromFile = BalanceFrom(ArchTestKit.ParseObject(ArchTestKit.ReadContent("balance")), out problems);

            Assert.Equal(0, problems.Count, ArchTestKit.Joined(problems));
            Assert.Equal(Json(BalanceConfig.Defaults()), Json(fromFile), "the code defaults and balance.json must say the same");
        }

        [Test]
        public void BalanceJson_AndTheConfigClasses_HaveTheSameMembers()
        {
            // A member of the file that the config class does not have would be ignored silently by the binder (the "tiers" block once was);
            // a member of the class that the file does not have would hide a number from the content (NFR-4).
            JsonObject file = ArchTestKit.ParseObject(ArchTestKit.ReadContent("balance"));
            file.Remove("schema");
            JsonNode dto = ModJson.From(BalanceConfig.Defaults());

            var fromFile = new SortedSet<string>(StringComparer.Ordinal);
            var fromClass = new SortedSet<string>(StringComparer.Ordinal);
            CollectPaths(file, "", fromFile);
            CollectPaths(dto, "", fromClass);

            string onlyInFile = string.Join(", ", fromFile.Except(fromClass));
            string onlyInClass = string.Join(", ", fromClass.Except(fromFile));
            Assert.Equal("", onlyInFile, "members of balance.json that BalanceConfig ignores");
            Assert.Equal("", onlyInClass, "members of BalanceConfig that balance.json does not have");
            Assert.True(fromFile.Contains("support.tiers.high.target") && fromFile.Contains("x86License.holderIds") && fromFile.Contains("extensions[].penalty"),
                "the walk reaches the nested members: " + fromFile.Count);
        }

        private static void CollectPaths(JsonNode node, string prefix, SortedSet<string> into)
        {
            var obj = node as JsonObject;
            if (obj != null)
            {
                foreach (KeyValuePair<string, JsonNode> member in obj)
                {
                    string path = prefix.Length == 0 ? member.Key : prefix + "." + member.Key;
                    into.Add(path);
                    CollectPaths(member.Value, path, into);
                }
                return;
            }

            var array = node as JsonArray;
            if (array != null)
            {
                foreach (JsonNode item in array) CollectPaths(item, prefix + "[]", into);
            }
        }

        [Test]
        public void BalanceJson_HasTheNumbersOfTheDesign()
        {
            List<string> problems;
            BalanceConfig c = BalanceFrom(ArchTestKit.ParseObject(ArchTestKit.ReadContent("balance")), out problems);

            Assert.Equal(1, c.stage);
            Assert.False(c.IsFullStage);
            Assert.Near(3.5, c.research.penaltyPerYear, 1e-6);
            Assert.Near(0.15, c.research.bonusPerYear, 1e-6);
            Assert.Near(0.75, c.research.bonusCap, 1e-6);
            Assert.Near(0.6, c.research.isaExperienceFactor, 1e-6);
            Assert.Near(0.80, c.rating.clampMin, 1e-6);
            Assert.Near(1.20, c.rating.clampMax, 1e-6);
            Assert.Near(0.90, c.rating.supportBase, 1e-6);
            Assert.Near(0.20, c.rating.supportSpan, 1e-6);
            Assert.Near(0.95, c.rating.ecoBase, 1e-6);
            Assert.Near(0.10, c.rating.ecoSpan, 1e-6);
            Assert.Near(50, c.rating.mvpSupport);

            Assert.Near(0, c.support.Tier(SupportTier.Off).target);
            Assert.Near(40, c.support.Tier(SupportTier.Low).target);
            Assert.Near(70, c.support.Tier(SupportTier.Normal).target);
            Assert.Near(95, c.support.Tier(SupportTier.High).target);
            Assert.Near(0.5, c.support.Tier(SupportTier.Low).cost, 1e-6);
            Assert.Near(1.0, c.support.Tier(SupportTier.Normal).cost, 1e-6);
            Assert.Near(2.0, c.support.Tier(SupportTier.High).cost, 1e-6);
            Assert.Near(30, c.support.startOnResearch);
            Assert.Near(0.5, c.support.inheritSameIsa, 1e-6);

            Assert.Equal(12, c.ecosystem.windowMonths);
            Assert.Equal("proprietary", c.ecosystem.fallbackFamily);
            Assert.True(c.ecosystem.milestones.SequenceEqual(new[] { 25, 50, 75 }));
            Assert.Equal(12, c.migration.immatureMonths);
            Assert.Near(0.25, c.migration.immatureDevCost, 1e-6);

            Assert.Equal(2, c.extensions.Count);
            Assert.Equal("multicore", c.extensions[0].id);
            Assert.Equal(ExtensionDef.TriggerCoreCount, c.extensions[0].trigger);
            Assert.Equal(2, c.extensions[0].minCores);
            Assert.Near(40, c.extensions[0].requiredSupport);
            Assert.Near(0.92, c.extensions[0].penalty, 1e-6);
            Assert.Equal("smt", c.extensions[1].id);
            Assert.Equal(ExtensionDef.TriggerSmt, c.extensions[1].trigger);
            Assert.Near(50, c.extensions[1].requiredSupport);
            Assert.Near(0.95, c.extensions[1].penalty, 1e-6);

            Assert.True(c.ai.fundingByDifficulty.SequenceEqual(new[] { 0.6f, 0.8f, 1.0f, 1.3f, 1.6f }));
            Assert.Equal(6, c.ai.reserveMonths);
            Assert.Equal(30, c.ai.noInterruptTimeLeftDays);
            Assert.Near(1.10, c.ai.switchMargin, 1e-6);
            Assert.Near(35, c.ai.switchMinSupport);
            Assert.Equal(12, c.ai.normalTierReserveMonths);
            Assert.Near(0.2, c.ai.candidateEcoForLowTier, 1e-6);
            Assert.Equal(3, c.ai.salesWindowMonths);

            Assert.True(c.seed.playerPath.SequenceEqual(new[] { "proprietary", "x86", "x86_64" }));
            Assert.Near(60, c.seed.aiActiveSupport);
            Assert.Near(35, c.seed.aiOtherSupport);
            Assert.Near(50, c.seed.playerNewestSupport);
            Assert.Near(30, c.seed.playerOtherSupport);

            Assert.Equal("x86", c.x86License.isa);
            Assert.True(c.x86License.holderIds.SequenceEqual(new[] { 0, 1 }));
            Assert.Near(0.03, c.x86License.royaltyRate, 1e-6);
        }

        [Test]
        public void BalanceJson_ReferencesOnlyFamiliesOfTheCatalog()
        {
            List<string> problems;
            BalanceConfig c = BalanceFrom(ArchTestKit.ParseObject(ArchTestKit.ReadContent("balance")), out problems);
            c.ValidateAgainst(ArchTestKit.BuiltInCatalog(), problems);

            Assert.Equal(0, problems.Count, ArchTestKit.Joined(problems));
            Assert.Equal(3, c.seed.playerPath.Length);
        }

        [Test]
        public void Balance_NullJson_GivesTheDefaults()
        {
            var problems = new List<string>();
            BalanceConfig config = BalanceConfig.FromJson(null, problems);

            Assert.Equal(0, problems.Count);
            Assert.Equal(Json(BalanceConfig.Defaults()), Json(config));
        }

        [Test]
        public void Balance_ValuesOutOfRange_AreReplacedByTheDefaults_WithANote()
        {
            JsonObject json = ArchTestKit.ParseObject(ArchTestKit.ReadContent("balance"));
            json.Set("stage", 3);
            json.GetObject("research").Set("bonusCap", 5);
            json.GetObject("rating").Set("clampMax", 3.5);
            json.GetObject("rating").Set("mvpSupport", -1);
            json.GetObject("migration").Set("immatureMonths", 1000);
            json.GetObject("ai").Set("noInterruptTimeLeftDays", -1);
            json.GetObject("ai").Set("fundingByDifficulty", new JsonArray().Add(1).Add(1).Add(1));
            json.GetObject("ai").Set("switchMargin", 0.5);
            json.GetObject("x86License").Set("royaltyRate", 0.9);
            json.GetObject("x86License").Set("holderIds", new JsonArray().Add(0).Add(-4));
            json.GetObject("seed").Set("playerPath", new JsonArray());
            json.GetObject("ecosystem").Set("milestones", new JsonArray().Add(50).Add(25));
            json.GetObject("support").GetObject("tiers").GetObject("high").Set("target", 150);

            List<string> problems;
            BalanceConfig c = BalanceFrom(json, out problems);
            BalanceConfig d = BalanceConfig.Defaults();

            Assert.Equal(d.stage, c.stage);
            Assert.Near(d.research.bonusCap, c.research.bonusCap, 1e-6);
            Assert.Near(d.rating.clampMax, c.rating.clampMax, 1e-6);
            Assert.Near(d.rating.mvpSupport, c.rating.mvpSupport, 1e-6);
            Assert.Equal(d.migration.immatureMonths, c.migration.immatureMonths);
            Assert.Equal(d.ai.noInterruptTimeLeftDays, c.ai.noInterruptTimeLeftDays);
            Assert.True(c.ai.fundingByDifficulty.SequenceEqual(d.ai.fundingByDifficulty));
            Assert.Near(d.ai.switchMargin, c.ai.switchMargin, 1e-6);
            Assert.Near(d.x86License.royaltyRate, c.x86License.royaltyRate, 1e-6);
            Assert.True(c.x86License.holderIds.SequenceEqual(d.x86License.holderIds));
            Assert.True(c.seed.playerPath.SequenceEqual(d.seed.playerPath));
            Assert.True(c.ecosystem.milestones.SequenceEqual(d.ecosystem.milestones));
            Assert.Near(d.support.tiers.high.target, c.support.tiers.high.target, 1e-6);

            foreach (string field in new[] { "stage", "research.bonusCap", "rating.clampMax", "rating.mvpSupport", "migration.immatureMonths",
                                             "ai.noInterruptTimeLeftDays", "ai.fundingByDifficulty", "ai.switchMargin", "x86License.royaltyRate",
                                             "x86License.holderIds", "seed.playerPath", "ecosystem.milestones", "support.tiers.high.target" })
            {
                Assert.True(ArchTestKit.AnyContains(problems, field), field + " must be reported: " + ArchTestKit.Joined(problems));
            }
        }

        [Test]
        public void Balance_AMemberOfTheWrongKind_KeepsTheDefault_WithANote()
        {
            JsonObject json = ArchTestKit.ParseObject(ArchTestKit.ReadContent("balance"));
            json.Set("stage", "two");
            json.GetObject("ai").Set("reserveMonths", "six");
            json.Set("research", "slow");

            List<string> problems;
            BalanceConfig c = BalanceFrom(json, out problems);

            Assert.Equal(1, c.stage);
            Assert.Equal(6, c.ai.reserveMonths);
            Assert.Near(3.5, c.research.penaltyPerYear, 1e-6, "a research block of the wrong kind keeps the defaults");
            Assert.True(ArchTestKit.AnyContains(problems, "stage"), ArchTestKit.Joined(problems));
            Assert.True(ArchTestKit.AnyContains(problems, "reserveMonths"), ArchTestKit.Joined(problems));
            Assert.True(ArchTestKit.AnyContains(problems, "research"), ArchTestKit.Joined(problems));
        }

        [Test]
        public void Balance_ANullBlock_IsReplacedByTheDefaults()
        {
            JsonObject json = ArchTestKit.ParseObject(ArchTestKit.ReadContent("balance"));
            json.Set("ai", JsonValue.Null);
            json.Set("extensions", JsonValue.Null);
            json.Set("x86License", JsonValue.Null);

            List<string> problems;
            BalanceConfig c = BalanceFrom(json, out problems);

            Assert.Equal(30, c.ai.noInterruptTimeLeftDays);
            Assert.Equal(2, c.extensions.Count);
            Assert.True(c.x86License.holderIds.SequenceEqual(new[] { 0, 1 }));
            Assert.True(ArchTestKit.AnyContains(problems, "ai"), ArchTestKit.Joined(problems));
        }

        [Test]
        public void Balance_BadExtensions_AreDropped_TheGoodOnesStay()
        {
            JsonObject json = ArchTestKit.ParseObject(ArchTestKit.ReadContent("balance"));
            var list = new JsonArray();
            list.Add(Extension("multicore", "coreCount", 2, 40, 0.92));
            list.Add(Extension("Bad Id", "smt", 1, 50, 0.95));
            list.Add(Extension("avx", "wide", 1, 50, 0.95));
            list.Add(Extension("multicore", "coreCount", 4, 40, 0.9));
            list.Add(Extension("deep", "coreCount", 4, 140, 0.9));
            json.Set("extensions", list);

            List<string> problems;
            BalanceConfig c = BalanceFrom(json, out problems);

            Assert.Equal(2, c.extensions.Count, ArchTestKit.Joined(problems));
            Assert.Equal("multicore", c.extensions[0].id);
            Assert.Equal(2, c.extensions[0].minCores, "the first multicore stays");
            Assert.Equal("deep", c.extensions[1].id);
            Assert.Near(40, c.extensions[1].requiredSupport, 1e-6, "an unusable requiredSupport becomes the default");
            Assert.True(ArchTestKit.AnyContains(problems, "extensions[1]"), ArchTestKit.Joined(problems));
            Assert.True(ArchTestKit.AnyContains(problems, "extensions[2]", "wide"), ArchTestKit.Joined(problems));
            Assert.True(ArchTestKit.AnyContains(problems, "extensions[3]", "twice"), ArchTestKit.Joined(problems));
            Assert.True(ArchTestKit.AnyContains(problems, "extensions[4].requiredSupport"), ArchTestKit.Joined(problems));
        }

        private static JsonObject Extension(string id, string trigger, int minCores, double requiredSupport, double penalty)
        {
            var x = new JsonObject();
            x.Set("id", id);
            x.Set("trigger", trigger);
            x.Set("minCores", minCores);
            x.Set("requiredSupport", requiredSupport);
            x.Set("penalty", penalty);
            return x;
        }

        [Test]
        public void Balance_ValidateAgainst_DropsUnknownFamilies()
        {
            var config = BalanceConfig.Defaults();
            config.seed.playerPath = new[] { "proprietary", "x86", "ghost", "x86_64" };
            config.ecosystem.fallbackFamily = "ghost";
            var problems = new List<string>();

            config.ValidateAgainst(ArchTestKit.BuiltInCatalog(), problems);

            Assert.True(config.seed.playerPath.SequenceEqual(new[] { "proprietary", "x86", "x86_64" }));
            Assert.Equal("proprietary", config.ecosystem.fallbackFamily);
            Assert.True(ArchTestKit.AnyContains(problems, "playerPath", "ghost"), ArchTestKit.Joined(problems));
            Assert.True(ArchTestKit.AnyContains(problems, "fallbackFamily", "ghost"), ArchTestKit.Joined(problems));

            config.ValidateAgainst(null, problems);   // no catalog: nothing to check, nothing thrown
        }

        [Test]
        public void Balance_PlayersFileOverridesTheBuiltInOne_FieldByField()
        {
            string over = "{ \"schema\": 1, \"stage\": 2, \"ai\": { \"noInterruptTimeLeftDays\": 10 }, \"x86License\": { \"royaltyRate\": 0.05 }, " +
                          "\"support\": { \"tiers\": { \"low\": { \"target\": 45 } } } }";
            ConfigLoad load = ContentMerger.MergeConfig(ArchTestKit.FeatureId, "balance", ArchTestKit.ReadContent("balance"), over, 1);
            List<string> problems;
            BalanceConfig c = BalanceFrom(load.Config, out problems);

            Assert.Equal(0, load.Report.Errors.Count, ArchTestKit.Joined(load.Report.Errors));
            Assert.True(load.Report.OverrideApplied);
            Assert.Equal(0, problems.Count, ArchTestKit.Joined(problems));
            Assert.Equal(2, c.stage);
            Assert.True(c.IsFullStage);
            Assert.Equal(10, c.ai.noInterruptTimeLeftDays);
            Assert.Near(0.05, c.x86License.royaltyRate, 1e-6);
            Assert.Near(45, c.support.Tier(SupportTier.Low).target, 1e-6, "a tier changed through the nested block");
            Assert.Near(0.10, c.support.Tier(SupportTier.Low).rate, 1e-6, "the rest of the tier stays");
            Assert.Near(70, c.support.Tier(SupportTier.Normal).target, 1e-6);
            Assert.Equal(6, c.ai.reserveMonths, "the rest of the ai block stays");
            Assert.True(c.x86License.holderIds.SequenceEqual(new[] { 0, 1 }), "the rest of the licence block stays");
            Assert.True(c.ai.fundingByDifficulty.SequenceEqual(new[] { 0.6f, 0.8f, 1.0f, 1.3f, 1.6f }));
        }

        [Test]
        public void Balance_AnOverrideWithAnotherSchema_IsIgnoredWhole()
        {
            ConfigLoad load = ContentMerger.MergeConfig(ArchTestKit.FeatureId, "balance", ArchTestKit.ReadContent("balance"), "{ \"schema\": 7, \"stage\": 2 }", 1);
            List<string> problems;
            BalanceConfig c = BalanceFrom(load.Config, out problems);

            Assert.True(load.Report.OverrideIgnored);
            Assert.Equal(1, c.stage);
        }

        [Test]
        public void SupportConfig_TierLookup_CoversEveryTier()
        {
            var support = new SupportConfig();

            Assert.True(ReferenceEquals(support.tiers.off, support.Tier(SupportTier.Off)));
            Assert.True(ReferenceEquals(support.tiers.low, support.Tier(SupportTier.Low)));
            Assert.True(ReferenceEquals(support.tiers.normal, support.Tier(SupportTier.Normal)));
            Assert.True(ReferenceEquals(support.tiers.high, support.Tier(SupportTier.High)));
            Assert.True(ReferenceEquals(support.tiers.off, support.Tier((SupportTier)99)), "an unknown tier is off");
        }

        // ---- ai-preferences.json ----

        [Test]
        public void AiPreferencesJson_HasOneDefault_UniqueIds_AndOnlyKnownFamilies()
        {
            ContentLoad<AiPreference> load = LoadPreferences();
            AiPreferenceTable table = AiPreferenceTable.Build(load.Records, ArchTestKit.BuiltInCatalog());

            Assert.Equal(0, load.Report.Errors.Count, ArchTestKit.Joined(load.Report.Errors));
            Assert.Equal(0, load.Report.Warnings.Count, ArchTestKit.Joined(load.Report.Warnings));
            Assert.Equal(0, table.Errors.Count, ArchTestKit.Joined(table.Errors));
            Assert.Equal(0, table.Warnings.Count, ArchTestKit.Joined(table.Warnings));
            Assert.True(table.HadDefault);
            Assert.Equal("default", table.Default.Id);
            Assert.True(table.Default.IsDefault);
            Assert.Equal(11, table.Preferences.Count);
            Assert.Equal(11, table.Preferences.Select(p => p.UniqueId).Distinct().Count(), "unique ids");
            Assert.Equal(1, load.Records.Count(p => p.IsDefault), "exactly one default record");
        }

        [Test]
        public void AiPreferencesJson_MapsTheHistoricalCompaniesByUniqueId()
        {
            AiPreferenceTable table = AiPreferenceTable.Build(LoadPreferences().Records, ArchTestKit.BuiltInCatalog());
            var expected = new Dictionary<int, string>
            {
                { 0, "intel" }, { 1, "amd" }, { 2, "zilog" }, { 3, "texas-instruments" }, { 4, "ibm" }, { 5, "fairchild" },
                { 6, "toshiba" }, { 7, "motorola" }, { 10, "cyrix" }, { 11, "via" }, { 12, "digital-devices" },
            };

            foreach (KeyValuePair<int, string> pair in expected) Assert.Equal(pair.Value, table.ByUniqueId(pair.Key).Id, "uniqueId " + pair.Key);

            // GlobalFoundries (8) and TSMC (9) are foundries: they have no record and fall back to the default; so does a company that is not listed.
            Assert.Null(table.ByUniqueId(8));
            Assert.Null(table.ByUniqueId(9));
            Assert.Equal("default", table.Match(8).Id);
            Assert.Equal("default", table.Match(57).Id);
            Assert.Equal("intel", table.Match(0).Id);
        }

        [Test]
        public void AiPreferencesJson_Weights_AndLags_AreAsApproved()
        {
            AiPreferenceTable table = AiPreferenceTable.Build(LoadPreferences().Records, ArchTestKit.BuiltInCatalog());

            AiPreference intel = table.ByUniqueId(0);
            Assert.Equal(0, intel.LagYears);
            Assert.Near(1.0, intel.WeightOf("x86"), 1e-6);
            Assert.Near(1.0, intel.WeightOf("x86_64"), 1e-6);
            Assert.Near(0.2, intel.WeightOf("proprietary"), 1e-6);
            Assert.Near(0, intel.WeightOf("riscv"), 1e-6, "a family without an entry weighs 0");

            AiPreference amd = table.ByUniqueId(1);
            Assert.Equal(1, amd.LagYears);
            Assert.Near(1.2, amd.WeightOf("x86_64"), 1e-6);

            AiPreference ti = table.ByUniqueId(3);
            Assert.Equal(2, ti.LagYears);
            Assert.Near(1.0, ti.WeightOf("arm"), 1e-6);
            Assert.Near(0.5, ti.WeightOf("riscv"), 1e-6);

            Assert.Equal(2, table.Default.LagYears);
            Assert.Near(0.6, table.Default.WeightOf("x86"), 1e-6);
            Assert.Near(0.4, table.Default.WeightOf("riscv"), 1e-6);
            Assert.Near(0, table.Default.WeightOf(null), 1e-6);
        }

        [Test]
        public void AiPreferences_PlayersFileReplacesAndAddsRecords()
        {
            string over = "{ \"schema\": 1, \"records\": [ " +
                          "{ \"id\": \"intel\", \"uniqueId\": 0, \"lagYears\": 4, \"weights\": { \"riscv\": 2.0 } }, " +
                          "{ \"id\": \"newcomer\", \"uniqueId\": 20, \"lagYears\": 0, \"weights\": { \"arm\": 1.0 } }, " +
                          "{ \"id\": \"zilog\", \"disabled\": true } ] }";
            ContentLoad<AiPreference> load = LoadPreferences(over);
            AiPreferenceTable table = AiPreferenceTable.Build(load.Records, ArchTestKit.BuiltInCatalog());

            Assert.Equal(0, load.Report.Errors.Count, ArchTestKit.Joined(load.Report.Errors));
            Assert.Equal(4, table.ByUniqueId(0).LagYears);
            Assert.Near(2.0, table.ByUniqueId(0).WeightOf("riscv"), 1e-6);
            Assert.Near(0, table.ByUniqueId(0).WeightOf("x86"), 1e-6, "a replaced record is replaced whole");
            Assert.Equal("newcomer", table.ByUniqueId(20).Id);
            Assert.Null(table.ByUniqueId(2), "zilog was removed");
            Assert.Equal(11, table.Preferences.Count);
        }

        // ---- ai-preferences: one record ----

        [Test]
        public void AiPreference_Map_ReadsADefaultAndAHistoricalRecord()
        {
            var errors = new List<string>();
            AiPreference historical = AiPreference.Map(ArchTestKit.ParseObject("{ \"id\": \"intel\", \"uniqueId\": 0, \"lagYears\": 2, \"weights\": { \"x86\": 1.5 } }"), errors);
            AiPreference fallback = AiPreference.Map(ArchTestKit.ParseObject("{ \"id\": \"default\", \"default\": true, \"lagYears\": 3, \"weights\": { } }"), errors);

            Assert.NotNull(historical, ArchTestKit.Joined(errors));
            Assert.NotNull(fallback, ArchTestKit.Joined(errors));
            Assert.Equal("intel", historical.Id);
            Assert.Equal("intel", historical.Key);
            Assert.Equal(0, historical.UniqueId);
            Assert.False(historical.IsDefault);
            Assert.Equal(2, historical.LagYears);
            Assert.Near(1.5, historical.WeightOf("x86"), 1e-6);
            Assert.True(fallback.IsDefault);
            Assert.Equal(-1, fallback.UniqueId);
            Assert.Equal(0, fallback.Weights.Count);
        }

        [Test]
        public void AiPreference_Map_RejectsTheDefaultWithAUniqueId_AndAHistoricalRecordWithout()
        {
            var errors = new List<string>();

            Assert.Null(AiPreference.Map(ArchTestKit.ParseObject("{ \"id\": \"d\", \"default\": true, \"uniqueId\": 3, \"weights\": { } }"), errors));
            Assert.True(ArchTestKit.AnyContains(errors, "uniqueId", "default"), ArchTestKit.Joined(errors));

            errors.Clear();
            Assert.Null(AiPreference.Map(ArchTestKit.ParseObject("{ \"id\": \"x\", \"weights\": { } }"), errors));
            Assert.True(ArchTestKit.AnyContains(errors, "uniqueId", "required"), ArchTestKit.Joined(errors));
        }

        [Test]
        public void AiPreference_Map_RejectsWrongKinds()
        {
            foreach (string text in new[]
            {
                "{ \"uniqueId\": 1, \"weights\": { } }",                                   // no id
                "{ \"id\": \"x\", \"uniqueId\": \"one\", \"weights\": { } }",               // uniqueId of the wrong kind
                "{ \"id\": \"x\", \"uniqueId\": 1.5, \"weights\": { } }",                   // not a whole number
                "{ \"id\": \"x\", \"uniqueId\": 1 }",                                       // no weights
                "{ \"id\": \"x\", \"uniqueId\": 1, \"weights\": [ 1 ] }",                   // weights is not an object
                "{ \"id\": \"x\", \"uniqueId\": 1, \"weights\": { \"x86\": \"high\" } }",   // a weight of the wrong kind
                "{ \"id\": \"x\", \"uniqueId\": 1, \"lagYears\": \"2\", \"weights\": { } }",
            })
            {
                var errors = new List<string>();
                Assert.Null(AiPreference.Map(ArchTestKit.ParseObject(text), errors), text);
                Assert.True(errors.Count > 0, text);
            }
        }

        [Test]
        public void AiPreference_ValidateRecord_HoldsTheRanges()
        {
            Assert.True(Valid(Preference("a", 0, "x86")));
            Assert.True(Valid(Preference("a", 99, "x86")));
            Assert.True(Valid(Preference("texas-instruments", 3, "x86")));
            Assert.True(Valid(Preference("default", null, "x86")));

            Assert.False(Valid(Preference("a", 100, "x86")), "uniqueId 100");
            Assert.False(Valid(Preference("a", -3, "x86")), "uniqueId -3");
            Assert.False(Valid(Preference("A B", 1, "x86")), "slug with a space");
            Assert.False(Valid(Preference("", 1, "x86")), "empty slug");

            AiPreference lag = Preference("a", 1, "x86");
            lag.LagYears = 11;
            Assert.False(Valid(lag), "lag 11");
            lag.LagYears = -1;
            Assert.False(Valid(lag), "lag -1");
            lag.LagYears = 10;
            Assert.True(Valid(lag), "lag 10");

            AiPreference weight = Preference("a", 1);
            weight.Weights["x86"] = 3f;
            Assert.True(Valid(weight), "weight 3");
            weight.Weights["x86"] = 3.01f;
            Assert.False(Valid(weight), "weight 3.01");
            weight.Weights["x86"] = -0.1f;
            Assert.False(Valid(weight), "weight -0.1");
            weight.Weights["x86"] = float.NaN;
            Assert.False(Valid(weight), "weight NaN");
            weight.Weights["x86"] = 1f;
            weight.Weights["X86"] = 1f;
            Assert.False(Valid(weight), "weight of a bad family id");

            Assert.False(AiPreference.ValidateRecord(null, new List<string>()), "null");
        }

        private static bool Valid(AiPreference preference)
        {
            return AiPreference.ValidateRecord(preference, new List<string>());
        }

        // ---- ai-preferences: across records ----

        [Test]
        public void AiTable_WithoutADefault_UsesTheBuiltInFallback_AndSaysSo()
        {
            AiPreferenceTable table = AiPreferenceTable.Build(new[] { Preference("intel", 0, "x86") }, ArchTestKit.BuiltInCatalog());

            Assert.False(table.HadDefault);
            Assert.NotNull(table.Default);
            Assert.True(table.Default.IsDefault);
            Assert.Near(1, table.Default.WeightOf("proprietary"), 1e-6);
            Assert.Equal(2, table.Default.LagYears);
            Assert.True(ArchTestKit.AnyContains(table.Errors, "default"), ArchTestKit.Joined(table.Errors));
            Assert.Equal(1, table.Preferences.Count);
        }

        [Test]
        public void AiTable_TwoDefaults_KeepTheFirst()
        {
            AiPreference first = Preference("first", null, "x86");
            AiPreference second = Preference("second", null, "arm");
            AiPreferenceTable table = AiPreferenceTable.Build(new[] { first, second }, ArchTestKit.BuiltInCatalog());

            Assert.True(table.HadDefault);
            Assert.Equal("first", table.Default.Id);
            Assert.True(ArchTestKit.AnyContains(table.Errors, "second", "default"), ArchTestKit.Joined(table.Errors));
        }

        [Test]
        public void AiTable_ARepeatedUniqueId_KeepsTheFirstRecord()
        {
            AiPreferenceTable table = AiPreferenceTable.Build(
                new[] { Preference("default", null, "x86"), Preference("a", 5, "x86"), Preference("b", 5, "arm"), Preference("c", 6, "arm") },
                ArchTestKit.BuiltInCatalog());

            Assert.Equal("a", table.ByUniqueId(5).Id);
            Assert.Equal("c", table.ByUniqueId(6).Id);
            Assert.Equal(2, table.Preferences.Count);
            Assert.True(ArchTestKit.AnyContains(table.Errors, "b", "uniqueId 5", "'a'"), ArchTestKit.Joined(table.Errors));
        }

        [Test]
        public void AiTable_ARepeatedId_KeepsTheFirstRecord()
        {
            AiPreferenceTable table = AiPreferenceTable.Build(
                new[] { Preference("default", null, "x86"), Preference("a", 5, "x86"), Preference("a", 6, "arm") }, ArchTestKit.BuiltInCatalog());

            Assert.Equal(1, table.Preferences.Count);
            Assert.Null(table.ByUniqueId(6));
            Assert.True(ArchTestKit.AnyContains(table.Errors, "a", "duplicate"), ArchTestKit.Joined(table.Errors));
        }

        [Test]
        public void AiTable_AWeightForAnUnknownFamily_IsSkippedWithAWarning()
        {
            AiPreference withGhost = Preference("a", 5, "x86", "ghost");
            AiPreferenceTable table = AiPreferenceTable.Build(new[] { Preference("default", null, "x86"), withGhost }, ArchTestKit.BuiltInCatalog());

            Assert.Equal(1, table.ByUniqueId(5).Weights.Count);
            Assert.Near(1, table.ByUniqueId(5).WeightOf("x86"), 1e-6);
            Assert.Equal(2, withGhost.Weights.Count, "the source record is not changed");
            Assert.True(ArchTestKit.AnyContains(table.Warnings, "ghost"), ArchTestKit.Joined(table.Warnings));

            AiPreferenceTable unchecked_ = AiPreferenceTable.Build(new[] { Preference("default", null, "x86"), withGhost }, null);
            Assert.Equal(2, unchecked_.ByUniqueId(5).Weights.Count, "without a catalog nothing is pruned");
            Assert.Equal(0, unchecked_.Warnings.Count);
        }

        [Test]
        public void AiTable_AnEmptyOrNullList_GivesOnlyTheFallback()
        {
            AiPreferenceTable none = AiPreferenceTable.Build(null, null);

            Assert.Equal(0, none.Preferences.Count);
            Assert.False(none.HadDefault);
            Assert.Equal("default", none.Default.Id);
            Assert.Equal("default", none.Match(3).Id);
            Assert.Equal("proprietary", AiPreferenceTable.Fallback().Weights.Keys.Single());
        }

        // ---- neutrality (FR-9) ----

        [Test]
        public void Neutrality_IsOnWhenTheFeatureIsOffOrTheNewGameFastForwards()
        {
            Assert.False(ArchNeutrality.IsNeutral(true, false), "enabled, normal play");
            Assert.True(ArchNeutrality.IsNeutral(true, true), "fast-forward (D8)");
            Assert.True(ArchNeutrality.IsNeutral(false, false), "switched off (FR-9 item 2)");
            Assert.True(ArchNeutrality.IsNeutral(false, true));
        }
    }
}
