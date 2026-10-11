using System.Collections.Generic;
using System.IO;
using PTMod.Core;
using PTMod.Core.Logic.Content;
using PTMod.Core.Logic.Json;
using PTMod.Core.Logic.L10n;
using PTMod.Features.PressReviews;
using PTMod.Tests.Architecture;

namespace PTMod.Tests.PressReviews
{
    /// <summary>
    /// The built-in content and string tables of press-reviews read from the source tree, for the tests of this feature. Internal, so the runner
    /// never discovers it (it has no tests of its own).
    /// </summary>
    internal static class PressContentFixture
    {
        public const string FeatureId = "press-reviews";

        public static string ContentText(string file)
        {
            string path = Path.Combine(SourceScan.SrcDirOrFail(), "Content", FeatureId, file);
            string text = SourceScan.ReadText(path);
            Assert.True(text.Length > 0, "the built-in content file is missing or empty: " + path);
            return text;
        }

        public static JsonObject ContentJson(string file)
        {
            JsonNode node;
            JsonError error;
            Assert.True(ModJson.TryParse(ContentText(file), out node, out error), file + " is not valid JSON: " + error);
            Assert.True(node is JsonObject, file + " must be a JSON object");
            return (JsonObject)node;
        }

        public static ContentLoad<Outlet> LoadOutlets()
        {
            return ContentMerger.MergeRecords<Outlet>(FeatureId, "outlets.json", ContentText("outlets.json"), null,
                PressContentValidator.ParseOutlet, null, 1);
        }

        public static ContentLoad<NoteTemplate> LoadNotes()
        {
            return ContentMerger.MergeRecords<NoteTemplate>(FeatureId, "notes.json", ContentText("notes.json"), null,
                PressContentValidator.ParseNote, null, 1);
        }

        public static List<Outlet> Outlets()
        {
            return new List<Outlet>(LoadOutlets().Records);
        }

        /// <summary>The config the built-in <c>review-config.json</c> gives, with the messages of the parser in <paramref name="report"/> (may be null).</summary>
        public static ReviewConfig Config(ValidationReport report = null)
        {
            ConfigLoad load = ContentMerger.MergeConfig(FeatureId, "review-config.json", ContentText("review-config.json"), null, 1);
            return PressContentValidator.ParseConfig(load.Config, report);
        }

        public static Outlet OutletById(IEnumerable<Outlet> outlets, string id)
        {
            foreach (Outlet outlet in outlets)
            {
                if (outlet.Id == id) return outlet;
            }
            Assert.Fail("no outlet '" + id + "' in the built-in outlets.json");
            return null;
        }

        /// <summary>The strings of the feature file <c>L10n/&lt;language&gt;/press-reviews.json</c> (the shared language files are other features' business).</summary>
        public static L10nTables Tables(string language)
        {
            string root = Path.Combine(SourceScan.SrcDirOrFail(), "L10n");
            var tables = new L10nTables();
            var problems = new List<string>();

            string own = Path.Combine(root, language, FeatureId + ".json");
            Assert.True(File.Exists(own), "missing " + own);
            tables.AddJson(language, "l10n/" + language + "/" + FeatureId + ".json", SourceScan.ReadText(own), problems);

            Assert.True(problems.Count == 0, "the string tables of press-reviews have problems:\n  " + string.Join("\n  ", problems));
            return tables;
        }
    }
}
