using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using PTMod.Core;
using PTMod.Core.Logic.Content;
using PTMod.Core.Logic.Json;
using PTMod.Features.GpuProductLine;

namespace PTMod.Tests.Features.GpuProductLine
{
    /// <summary>
    /// <see cref="IModContentSource"/> over text: the built-in text of each file (and the player's override, if one is set) merged by
    /// <see cref="ContentMerger"/> - the same merge <c>ModContent</c> does in the game, so the tests see the records, the reports and the override rules
    /// the game sees. Internal: the runner does not look for tests in it.
    /// </summary>
    internal sealed class TextContentSource : IModContentSource
    {
        public const string Feature = "gpu-product-line";

        public readonly Dictionary<string, string> Builtin = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        public readonly Dictionary<string, string> Overrides = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>What happened to each file at its last load (by the name the caller used).</summary>
        public readonly Dictionary<string, ContentReport> Reports = new Dictionary<string, ContentReport>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Set to make a file's load throw, the way a broken source might.</summary>
        public string ThrowOn;

        public TextContentSource Set(string file, string text)
        {
            Builtin[file] = text;
            return this;
        }

        public TextContentSource Override(string file, string text)
        {
            Overrides[file] = text;
            return this;
        }

        /// <summary>Replaces <paramref name="from"/> by <paramref name="to"/> in the built-in text of a file; fails the test when the text is not there.</summary>
        public TextContentSource Edit(string file, string from, string to)
        {
            string text;
            Assert.True(Builtin.TryGetValue(file, out text), "no built-in text for " + file);
            Assert.True(text.Contains(from), file + " does not contain: " + from);
            Builtin[file] = text.Replace(from, to);
            return this;
        }

        public IReadOnlyList<T> LoadRecords<T>(string file, Func<JsonObject, List<string>, T> map, Func<T, List<string>, bool> validate)
            where T : class, IContentRecord
        {
            if (ThrowOn == file) throw new InvalidOperationException("the source of " + file + " is broken");
            ContentLoad<T> load = ContentMerger.MergeRecords(Feature, file, Text(Builtin, file), Text(Overrides, file), map, validate, GpuContentDb.ContentSchema);
            Reports[file] = load.Report;
            return load.Records;
        }

        public JsonObject LoadConfig(string file)
        {
            if (ThrowOn == file) throw new InvalidOperationException("the source of " + file + " is broken");
            ConfigLoad load = ContentMerger.MergeConfig(Feature, file, Text(Builtin, file), Text(Overrides, file), GpuContentDb.ContentSchema);
            Reports[file] = load.Report;
            return (JsonObject)load.Config.DeepClone();
        }

        private static string Text(Dictionary<string, string> texts, string file)
        {
            string text;
            return texts.TryGetValue(file, out text) ? text : null;
        }

        /// <summary>Errors and warnings of every file report, one line each, in file order; empty when all files loaded clean.</summary>
        public List<string> ReportLines()
        {
            var lines = new List<string>();
            foreach (KeyValuePair<string, ContentReport> pair in Reports)
            {
                foreach (string error in pair.Value.Errors) lines.Add("error: " + error);
                foreach (string warning in pair.Value.Warnings) lines.Add("warning: " + warning);
            }
            return lines;
        }

        public ContentReport ReportOf(string file)
        {
            ContentReport report;
            Assert.True(Reports.TryGetValue(file, out report), "the file was not loaded: " + file);
            return report;
        }
    }

    /// <summary>Where the tests find the shipped content, the fixtures and the L10n tables (through <see cref="TestEnv"/>, like the architecture tests).</summary>
    internal static class GpuFiles
    {
        public static readonly string[] ContentFiles =
        {
            "balance", "architectures", "memory", "coolers", "research", "segments", "eras", "vendors", "consoles", "timeline",
        };

        public static string ShippedContentDir
        {
            get
            {
                Assert.NotNull(TestEnv.SrcDir, "source folder not found");
                return Path.Combine(TestEnv.SrcDir, "Content", "gpu-product-line");
            }
        }

        public static string FeatureSourceDir
        {
            get
            {
                Assert.NotNull(TestEnv.SrcDir, "source folder not found");
                return Path.Combine(TestEnv.SrcDir, "Features", "GpuProductLine");
            }
        }

        public static string FixtureDir
        {
            get
            {
                Assert.NotNull(TestEnv.ModDir, "mod folder not found");
                return Path.Combine(TestEnv.ModDir, "tests", "PTMod.Tests", "Features", "GpuProductLine", "fixtures");
            }
        }

        public static string Read(string path)
        {
            Assert.True(File.Exists(path), "no such file: " + path);
            return File.ReadAllText(path, new UTF8Encoding(false));
        }

        public static string Fixture(string name)
        {
            return Read(Path.Combine(FixtureDir, name));
        }

        public static JsonObject FixtureJson(string name)
        {
            return ParseObject(Fixture(name), name);
        }

        public static JsonObject ParseObject(string text, string what)
        {
            JsonNode node;
            JsonError error;
            Assert.True(ModJson.TryParse(text, out node, out error), what + " is not JSON: " + error);
            var obj = node as JsonObject;
            Assert.NotNull(obj, what + " is not a JSON object");
            return obj;
        }

        /// <summary>The shipped content files as built-in texts.</summary>
        public static TextContentSource Shipped()
        {
            var source = new TextContentSource();
            foreach (string file in ContentFiles) source.Set(file, Read(Path.Combine(ShippedContentDir, file + ".json")));
            return source;
        }

        /// <summary>
        /// The shipped content in the market mode of stage 1 (<c>single</c>, the one segment <c>consumer</c>): the files are the shipped ones, only the mode is the old one. The
        /// tests of stage 1 run on it; the tests of the eras and the vendors run on <see cref="Shipped"/>.
        /// </summary>
        public static TextContentSource ShippedSingle()
        {
            return Shipped().Edit("balance", "\"marketMode\": \"eras\"", "\"marketMode\": \"single\"");
        }

        /// <summary>The small content set of the fixtures: two of everything, with references between the files.</summary>
        public static TextContentSource Minimal()
        {
            var source = new TextContentSource();
            foreach (string file in ContentFiles) source.Set(file, Read(Path.Combine(FixtureDir, "content-min", file + ".json")));
            return source;
        }

        public static GpuContentDb Load(IModContentSource source, List<string> problems = null)
        {
            return GpuContentDb.Load(source, problems ?? new List<string>());
        }
    }
}
