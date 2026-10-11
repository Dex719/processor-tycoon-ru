using System.Collections.Generic;
using System.IO;
using PTMod.Core;
using PTMod.Core.Logic.Content;
using PTMod.Core.Logic.Json;
using PTMod.Features.RivalPersonalities;
using PTMod.Tests.Architecture;

namespace PTMod.Tests.Features.RivalPersonalities
{
    /// <summary>
    /// Shared helpers of the tests of rival-personalities: the shipped content files and string tables are read from the source tree and run through
    /// the same pure code as the game does (<c>ContentMerger</c>, <c>ArchetypeCatalog</c>). Internal, so the runner never discovers it.
    /// </summary>
    internal static class RivalTestKit
    {
        public const string Feature = "rival-personalities";

        /// <summary>Turns a JSON written with single quotes into JSON text.</summary>
        public static string J(string text)
        {
            return text.Replace('\'', '"');
        }

        public static JsonObject Obj(string singleQuotedJson)
        {
            return ParseObject(J(singleQuotedJson));
        }

        public static JsonObject ParseObject(string text)
        {
            JsonNode node;
            JsonError error;
            Assert.True(ModJson.TryParse(text, out node, out error), "not valid JSON: " + error);
            var obj = node as JsonObject;
            Assert.NotNull(obj, "not a JSON object");
            return obj;
        }

        public static string ContentPath(string file)
        {
            return Path.Combine(SourceScan.SrcDirOrFail(), "Content", Feature, file);
        }

        /// <summary>The text of a shipped content file (<c>archetypes.json</c>...).</summary>
        public static string ReadContent(string file)
        {
            string path = ContentPath(file);
            Assert.True(File.Exists(path), "missing content file " + path);
            return SourceScan.ReadText(path);
        }

        /// <summary>The string table of a language as shipped: <c>L10n/&lt;lang&gt;/rival-personalities.json</c>.</summary>
        public static JsonObject ReadL10n(string language)
        {
            string path = Path.Combine(SourceScan.SrcDirOrFail(), "L10n", language, Feature + ".json");
            Assert.True(File.Exists(path), "missing string table " + path);
            return ParseObject(SourceScan.ReadText(path));
        }

        public static JsonObject CorePrefixes()
        {
            JsonObject prefixes = CoreContentTests.LoadCore().GetObject("prefixes");
            Assert.NotNull(prefixes, "core.json lacks 'prefixes'");
            return prefixes;
        }

        /// <summary><c>ModContent.LoadRecords</c> on texts: the merge of the core and the mapper and validator of the feature.</summary>
        public static ContentLoad<Archetype> LoadArchetypes(string builtin, string over = null)
        {
            return ContentMerger.MergeRecords<Archetype>(Feature, "archetypes.json", builtin, over,
                ArchetypeCatalog.MapArchetype, ArchetypeCatalog.ValidateArchetype, 1);
        }

        public static ContentLoad<AssignmentRecord> LoadAssignments(string builtin, string over = null)
        {
            return ContentMerger.MergeRecords<AssignmentRecord>(Feature, "assignments.json", builtin, over,
                ArchetypeCatalog.MapAssignment, ArchetypeCatalog.ValidateAssignment, 1);
        }

        /// <summary>The shipped <c>config.json</c> read the way the game reads it; the messages go to <paramref name="messages"/>.</summary>
        public static RivalConfig LoadConfig(List<string> messages)
        {
            ConfigLoad load = ContentMerger.MergeConfig(Feature, "config.json", ReadContent("config.json"), null, 1);
            Assert.Equal(0, load.Report.Errors.Count, "config.json report: " + string.Join("; ", load.Report.Errors));
            return RivalConfig.FromJson(load.Config, messages);
        }

        /// <summary>The whole shipped catalog, built the way <c>RivalContent.Load</c> builds it; every problem fails the test.</summary>
        public static ArchetypeCatalog LoadShippedCatalog()
        {
            ContentLoad<Archetype> archetypes = LoadArchetypes(ReadContent("archetypes.json"));
            ContentLoad<AssignmentRecord> assignments = LoadAssignments(ReadContent("assignments.json"));
            var messages = new List<string>();
            RivalConfig config = LoadConfig(messages);

            Assert.Equal(0, archetypes.Report.Errors.Count, "archetypes.json: " + string.Join("; ", archetypes.Report.Errors));
            Assert.Equal(0, assignments.Report.Errors.Count, "assignments.json: " + string.Join("; ", assignments.Report.Errors));
            Assert.Equal(0, messages.Count, "config.json: " + string.Join("; ", messages));

            var report = new ValidationReport();
            ArchetypeCatalog catalog = ArchetypeCatalog.Build(archetypes.Records, assignments.Records, config, report);
            Assert.Equal(0, report.Errors.Count, string.Join("; ", report.Errors));
            Assert.Equal(0, report.Warnings.Count, string.Join("; ", report.Warnings));
            return catalog;
        }
    }
}
