using System;
using System.Collections.Generic;
using System.IO;
using PTMod.Core;
using PTMod.Core.Logic.Content;
using PTMod.Core.Logic.Json;
using PTMod.Features.PlatformGenerations;
using PTMod.Tests.Architecture;

namespace PTMod.Tests.Features.PlatformGenerations
{
    /// <summary>
    /// Shared by the tests of platform-generations: the shipped content read from the source tree (the tests follow the files, so a change of the
    /// balance needs no change of a test), the packages and memories of the game, and small builders. Internal, so the runner never discovers it.
    /// </summary>
    internal static class PlatformTestData
    {
        public const string FeatureId = "platform-generations";

        /// <summary>The 16 <c>Package</c> objects of the game (<c>ProcessorTycoon.Hardware.Package.json</c> of the code map).</summary>
        public static readonly string[] GamePackages =
        {
            "DIP 1970", "DIP 1975", "PLCC 1980", "PGA 1985", "PGA 1990", "PGA 1995", "LGA 2000", "PGA 2000",
            "LGA 2010", "PGA 2010", "LGA 2015", "PGA 2015", "LGA 2020 A", "LGA 2020 B", "LGA 2030 A", "LGA 2030 B",
        };

        /// <summary>The 9 <c>Memory</c> objects of the game.</summary>
        public static readonly string[] GameMemories =
        {
            "DIP SRAM", "DRAM", "SDR", "DDR", "DDR2", "DDR3", "DDR4", "DDR5", "DDR6",
        };

        public static bool PackageExists(string name)
        {
            return Array.IndexOf(GamePackages, name) >= 0;
        }

        public static bool MemoryExists(string name)
        {
            return Array.IndexOf(GameMemories, name) >= 0;
        }

        /// <summary>The text of a shipped file <c>Content/platform-generations/&lt;file&gt;</c>.</summary>
        public static string ReadContent(string file)
        {
            string path = Path.Combine(SourceScan.SrcDirOrFail(), "Content", FeatureId, file);
            Assert.True(File.Exists(path), "the content file is missing: " + path);
            return SourceScan.ReadText(path);
        }

        /// <summary>The text of a shipped string table <c>L10n/&lt;lang&gt;/platform-generations.json</c>.</summary>
        public static string ReadStrings(string language)
        {
            string path = Path.Combine(SourceScan.SrcDirOrFail(), "L10n", language, FeatureId + ".json");
            Assert.True(File.Exists(path), "the string table is missing: " + path);
            return SourceScan.ReadText(path);
        }

        public static JsonObject ParseObject(string text)
        {
            JsonNode node;
            JsonError error;
            Assert.True(ModJson.TryParse(text.Replace('\'', '"'), out node, out error), "test JSON is invalid: " + error);
            Assert.True(node is JsonObject, "test JSON must be an object");
            return (JsonObject)node;
        }

        /// <summary>
        /// <c>generations.json</c> loaded the way <c>ModContent.LoadRecords</c> does it: the mapper, the validation against the game's objects, and
        /// the optional override text on top. The report says what happened.
        /// </summary>
        public static ContentLoad<GenerationDef> LoadGenerations(string overrideText)
        {
            return ContentMerger.MergeRecords<GenerationDef>(FeatureId, "generations.json", ReadContent("generations.json"), overrideText,
                GenerationDef.FromJson,
                (record, errors) => GenerationCatalog.Validate(record, PackageExists, MemoryExists, errors),
                1);
        }

        /// <summary>The shipped catalog, built like the feature builds it; <paramref name="problems"/> receives every line of the report and of the build.</summary>
        public static GenerationCatalog BuiltInCatalog(List<string> problems)
        {
            ContentLoad<GenerationDef> load = LoadGenerations(null);
            if (problems != null)
            {
                problems.AddRange(load.Report.Errors);
                problems.AddRange(load.Report.Warnings);
            }
            return GenerationCatalog.Build(load.Records, PackageExists, MemoryExists, problems);
        }

        /// <summary>The shipped <c>balance.json</c> as the feature reads it; <paramref name="problems"/> receives the report and the parser's lines.</summary>
        public static PlatformBalance BuiltInBalance(List<string> problems)
        {
            ConfigLoad load = ContentMerger.MergeConfig(FeatureId, "balance.json", ReadContent("balance.json"), null, 1);
            if (problems != null)
            {
                problems.AddRange(load.Report.Errors);
                problems.AddRange(load.Report.Warnings);
            }
            return PlatformBalance.FromJson(load.Config, problems);
        }

        /// <summary>A record that passes the validation, on the given sockets (names of the game's packages).</summary>
        public static GenerationDef Gen(string id, int order, params string[] sockets)
        {
            return new GenerationDef
            {
                id = id,
                order = order,
                name = LocText.Of("Имя " + id, "Name " + id),
                description = LocText.Of("Описание", "Description"),
                announceYear = 1990,
                obsoleteYear = 2000,
                sockets = new List<string>(sockets),
                memories = new List<string> { "SDR" },
                earlyWindowMonths = 24,
                oem = new OemCurveDef { delayMonths = 6, rampMonths = 36, maxAdoption = 0.9f, ibAccel = 0.5f },
                compatibleSockets = new List<SocketPair>(),
            };
        }

        public static bool AnyContains(List<string> lines, string text)
        {
            return lines.Exists(line => line.IndexOf(text, StringComparison.Ordinal) >= 0);
        }
    }
}
