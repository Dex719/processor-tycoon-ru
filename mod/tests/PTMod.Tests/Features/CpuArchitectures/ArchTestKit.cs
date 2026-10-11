using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using PTMod.Core;
using PTMod.Core.Logic.Content;
using PTMod.Core.Logic.Json;
using PTMod.Features.CpuArchitectures;

namespace PTMod.Tests.Features.CpuArchitectures
{
    /// <summary>
    /// Helpers shared by the tests of the cpu-architectures logic: the built-in content files read from the source tree, a valid family to break
    /// one rule at a time, a catalog from the built-in file. Internal, so the runner never discovers it.
    /// </summary>
    internal static class ArchTestKit
    {
        public const string FeatureId = "cpu-architectures";

        public static string ReadContent(string file)
        {
            Assert.NotNull(TestEnv.SrcDir, "source folder not found; run the tests through python mod/build_feature.py cpu-architectures --tests");
            string path = Path.Combine(TestEnv.SrcDir, "Content", FeatureId, file + ".json");
            Assert.True(File.Exists(path), "the built-in content file is missing: " + path);
            return File.ReadAllText(path, new UTF8Encoding(false));
        }

        public static JsonObject ParseObject(string text)
        {
            JsonNode node;
            JsonError error;
            Assert.True(ModJson.TryParse(text, out node, out error), "must be valid JSON: " + error);
            var obj = node as JsonObject;
            Assert.NotNull(obj, "the root must be an object");
            return obj;
        }

        /// <summary>The families of the built-in file, with the override text (or null) merged on top, like ModContent does.</summary>
        public static ContentLoad<ArchFamily> LoadFamilies(string overrideText = null)
        {
            return ContentMerger.MergeRecords<ArchFamily>(FeatureId, "architectures", ReadContent("architectures"), overrideText,
                ArchFamily.Map, ArchCatalogValidator.ValidateRecord, 1);
        }

        public static ArchCatalog BuiltInCatalog()
        {
            ContentLoad<ArchFamily> load = LoadFamilies();
            ArchCatalog catalog;
            ValidationResult result;
            Assert.True(ArchCatalog.TryCreate(load.Records, out catalog, out result), "the built-in catalog must be usable: " + string.Join("; ", result.Errors));
            return catalog;
        }

        /// <summary>The preferences of the AI companies from the built-in <c>ai-preferences.json</c>, checked against the catalog like the feature does.</summary>
        public static AiPreferenceTable BuiltInPreferences(ArchCatalog catalog)
        {
            ContentLoad<AiPreference> load = ContentMerger.MergeRecords<AiPreference>(FeatureId, "ai-preferences", ReadContent("ai-preferences"), null,
                AiPreference.Map, AiPreference.ValidateRecord, 1);
            return AiPreferenceTable.Build(load.Records, catalog);
        }

        /// <summary>The numbers of the built-in <c>balance.json</c>.</summary>
        public static BalanceConfig BuiltInBalance()
        {
            var problems = new List<string>();
            BalanceConfig balance = BalanceConfig.FromJson(ParseObject(ReadContent("balance")), problems);
            Assert.Equal(0, problems.Count, "balance.json must be read without problems: " + string.Join("; ", problems));
            return balance;
        }

        /// <summary>A registry made the way the feature makes it: the built-in catalog, the AI preferences and the numbers of the seed.</summary>
        public static ArchRegistry BuiltInRegistry()
        {
            ArchCatalog catalog = BuiltInCatalog();
            return new ArchRegistry(catalog, BuiltInPreferences(catalog), BuiltInBalance().seed);
        }

        /// <summary>The JSON record of a family of the built-in file (a copy the test may change).</summary>
        public static JsonObject RecordJson(string id)
        {
            JsonObject root = ParseObject(ReadContent("architectures"));
            foreach (JsonNode node in root.GetArray("records"))
            {
                var record = (JsonObject)node;
                if (record.GetString("id") == id) return (JsonObject)record.DeepClone();
            }
            Assert.Fail("no record '" + id + "' in architectures.json");
            return null;
        }

        /// <summary>An override file with the given records, in the shape of a record file.</summary>
        public static string OverrideFile(params JsonObject[] records)
        {
            var array = new JsonArray();
            foreach (JsonObject record in records) array.Add(record);
            var root = new JsonObject();
            root.Set("schema", 1);
            root.Set("records", array);
            return ModJson.Write(root, false);
        }

        /// <summary>A valid family that breaks no rule; tests change one field to break one.</summary>
        public static ArchFamily Family(string id, int year = 1980, params string[] requires)
        {
            return new ArchFamily
            {
                Id = id,
                Isa = "own",
                NameKey = "arch.family." + id + ".name",
                DescKey = "arch.family." + id + ".desc",
                Year = year,
                Requires = requires ?? new string[0],
                Research = new ResearchDef { BaseDays = 100f, MonthlyCost = 1000000f },
                Design = new DesignDef(),
                Affinity = new[] { 1f, 1f, 1f, 1f },
                Support = new SupportDef(),
            };
        }

        public static ArchFamily Proprietary()
        {
            ArchFamily family = Family(ArchCatalog.DefaultId, 1971);
            family.Research.BaseDays = 0f;
            family.Research.MonthlyCost = 0f;
            return family;
        }

        public static string[] Ids(IEnumerable<ArchFamily> families)
        {
            return families.Select(f => f.Id).ToArray();
        }

        public static bool AnyContains(IEnumerable<string> lines, params string[] fragments)
        {
            return lines.Any(line => fragments.All(fragment => line.IndexOf(fragment, StringComparison.Ordinal) >= 0));
        }

        public static string Joined(IEnumerable<string> lines)
        {
            return string.Join("\n  ", lines);
        }
    }
}
