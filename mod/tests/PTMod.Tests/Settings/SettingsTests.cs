using System;
using System.Collections.Generic;
using System.Linq;
using PTMod.Core;
using PTMod.Core.Logic.Json;
using PTMod.Core.Logic.Save;
using PTMod.Core.Logic.Settings;
using PTMod.Tests.Common;

namespace PTMod.Tests.Settings
{
    /// <summary>
    /// ModSettings logic (FR-6 items 1-4, TSK-014): loading and validating ptmod_settings.json, ParamSpec clamping, the corrupt-file path,
    /// atomic saving. A fake disk, a fixed clock; nothing touches the real file system.
    /// </summary>
    [Covers(typeof(SettingsModel), typeof(SettingsFileStatus), typeof(SettingsLoadReport), typeof(ParamSpec), typeof(ParamType))]
    public class SettingsTests
    {
        private const string SettingsPath = FakeFileSystem.Root + @"\ptmod_settings.json";
        private static readonly DateTime Now = new DateTime(2026, 10, 10, 12, 34, 56, DateTimeKind.Utc);

        private static SettingsModel Make(FakeFileSystem fs, bool debugBuild = false)
        {
            return new SettingsModel(fs, SettingsPath, debugBuild, () => Now);
        }

        private static ParamSpec FloatSpec(string feature, string key, double def, double min, double max)
        {
            return new ParamSpec { FeatureId = feature, Key = key, Type = ParamType.Float, Default = def, Min = min, Max = max, Step = 0.05 };
        }

        private static ParamSpec IntSpec(string feature, string key, double def, double min, double max)
        {
            return new ParamSpec { FeatureId = feature, Key = key, Type = ParamType.Int, Default = def, Min = min, Max = max, Step = 1 };
        }

        private static ParamSpec BoolSpec(string feature, string key, bool def)
        {
            return new ParamSpec { FeatureId = feature, Key = key, Type = ParamType.Bool, Default = def ? 1 : 0 };
        }

        private static JsonObject Parse(string text)
        {
            JsonNode node;
            JsonError error;
            Assert.True(ModJson.TryParse(text, out node, out error), "the file must be valid JSON: " + error);
            var obj = node as JsonObject;
            Assert.NotNull(obj, "the root must be an object");
            return obj;
        }

        private static JsonObject SavedDocument(FakeFileSystem fs)
        {
            return Parse(fs.Text(SettingsPath));
        }

        private static bool AnyContains(IEnumerable<string> lines, string fragment)
        {
            return lines.Any(l => l.IndexOf(fragment, StringComparison.Ordinal) >= 0);
        }

        /// <summary>Structural JSON equality (object member order does not matter); prints both trees when they differ.</summary>
        private static void AssertJson(JsonNode expected, JsonNode actual)
        {
            Assert.True(expected != null && expected.Equals(actual), "expected " + expected + " but got " + actual);
        }

        // ---- creating and reading the file ----

        [Test]
        public void Load_NoFile_CreatesDefaultsFile()
        {
            var fs = new FakeFileSystem();
            SettingsModel model = Make(fs);

            SettingsLoadReport report = model.Load();

            Assert.Equal(SettingsFileStatus.Created, report.Status);
            Assert.NotNull(report.WriteResult);
            Assert.True(report.WriteResult.Success);
            Assert.Equal(0, report.Warnings.Count);
            Assert.False(fs.Exists(SettingsPath + ".tmp"), "no temporary file is left");

            JsonObject doc = SavedDocument(fs);
            Assert.Equal(1, doc.GetInt32("schema"));
            Assert.Equal("ru", doc.GetString("language"));
            Assert.False(doc.GetBool("verboseLog", true));
            Assert.NotNull(doc.GetObject("features"));
            Assert.False(doc.ContainsKey("debug"), "a release build does not write the debug section");

            Assert.Equal("ru", model.Language);
            Assert.False(model.VerboseLog);
            Assert.False(model.IsDirty);
        }

        [Test]
        public void Load_NoFile_DebugBuildWritesDebugFlagsOff()
        {
            var fs = new FakeFileSystem();
            SettingsModel model = Make(fs, true);

            model.Load();

            JsonObject debug = SavedDocument(fs).GetObject("debug");
            Assert.NotNull(debug);
            Assert.Equal(new[] { "failInit", "brokenPatch", "throwOnTick", "stress", "badIds" }, debug.Keys.ToArray());
            foreach (string flag in debug.Keys) Assert.False(debug.GetBool(flag, true), flag);
            Assert.False(model.Debug("debug.failInit"));
        }

        [Test]
        public void Load_ValidFile_ReadsAllParts()
        {
            var fs = new FakeFileSystem();
            fs.Seed(SettingsPath, "{\"schema\":1,\"language\":\"en\",\"verboseLog\":true,\"features\":{"
                + "\"framework-sample\":{\"enabled\":false,\"params\":{\"boost\":1.25}},\"event-system\":{\"enabled\":true,\"params\":{}}},"
                + "\"debug\":{\"failInit\":true,\"stress\":false}}");
            SettingsModel model = Make(fs, true);
            var warnings = new List<string>();

            SettingsLoadReport report = model.Load();
            Assert.True(model.Declare(FloatSpec("framework-sample", "boost", 1.0, 0.5, 2.0), warnings));

            Assert.Equal(SettingsFileStatus.Loaded, report.Status);
            Assert.Null(report.WriteResult, "an existing file is only read");
            Assert.Equal(0, report.Warnings.Count);
            Assert.Equal(0, warnings.Count);
            Assert.Equal("en", model.Language);
            Assert.True(model.VerboseLog);
            Assert.Equal(false, model.GetFeatureEnabled("framework-sample"));
            Assert.Equal(true, model.GetFeatureEnabled("event-system"));
            Assert.Null(model.GetFeatureEnabled("market-shocks"));
            Assert.Near(1.25, model.GetValue("framework-sample", "boost"));
            Assert.True(model.Debug("debug.failInit"));
            Assert.True(model.Debug("failInit"));
            Assert.False(model.Debug("debug.stress"));
            Assert.False(model.Debug("debug.unknownFlag"));
            Assert.False(model.IsDirty);
        }

        [Test]
        public void IsFeatureEnabled_UsesTheFeaturesOwnDefaultWhenThereIsNoChoice()
        {
            var fs = new FakeFileSystem();
            fs.Seed(SettingsPath, "{\"schema\":1,\"features\":{\"a\":{\"enabled\":false},\"b\":{\"enabled\":true}}}");
            SettingsModel model = Make(fs);
            model.Load();

            Assert.False(model.IsFeatureEnabled("a", true), "an explicit off wins over the default");
            Assert.True(model.IsFeatureEnabled("b", false), "an explicit on wins over the default");
            Assert.True(model.IsFeatureEnabled("c", true));
            Assert.False(model.IsFeatureEnabled("c", false));
            Assert.False(model.IsFeatureEnabled(null, false));
        }

        // AC-6.1
        [Test]
        public void Load_EnabledOfWrongType_UsesDefaultAndWarnsWithPath()
        {
            var fs = new FakeFileSystem();
            fs.Seed(SettingsPath, "{\"schema\":1,\"features\":{\"event-system\":{\"enabled\":\"yes\"}}}");
            SettingsModel model = Make(fs);

            SettingsLoadReport report = model.Load();

            Assert.Equal(SettingsFileStatus.Loaded, report.Status);
            Assert.Null(model.GetFeatureEnabled("event-system"));
            Assert.True(model.IsFeatureEnabled("event-system", true), "the feature's own default decides");
            Assert.Equal(1, report.Warnings.Count);
            Assert.True(report.Warnings[0].StartsWith("features.event-system.enabled:", StringComparison.Ordinal), report.Warnings[0]);
        }

        [Test]
        public void Load_BadTopLevelValues_FallBackToDefaultsWithWarnings()
        {
            var fs = new FakeFileSystem();
            fs.Seed(SettingsPath, "{\"schema\":\"one\",\"language\":\"de\",\"verboseLog\":\"yes\",\"features\":[1,2]}");
            SettingsModel model = Make(fs);

            SettingsLoadReport report = model.Load();

            Assert.Equal(SettingsFileStatus.Loaded, report.Status);
            Assert.Equal("ru", model.Language);
            Assert.False(model.VerboseLog);
            Assert.True(AnyContains(report.Warnings, "schema:"));
            Assert.True(AnyContains(report.Warnings, "language:"));
            Assert.True(AnyContains(report.Warnings, "verboseLog:"));
            Assert.True(AnyContains(report.Warnings, "features:"));
        }

        [Test]
        public void Load_LanguageIsCaseInsensitive()
        {
            var fs = new FakeFileSystem();
            fs.Seed(SettingsPath, "{\"schema\":1,\"language\":\"EN\"}");
            SettingsModel model = Make(fs);

            SettingsLoadReport report = model.Load();

            Assert.Equal("en", model.Language);
            Assert.Equal(0, report.Warnings.Count);
        }

        [Test]
        public void Load_FeatureEntryAndParamsOfWrongShape_AreReported()
        {
            var fs = new FakeFileSystem();
            fs.Seed(SettingsPath, "{\"schema\":1,\"features\":{\"a\":5,\"b\":{\"enabled\":true,\"params\":[1]}}}");
            SettingsModel model = Make(fs);

            SettingsLoadReport report = model.Load();

            Assert.True(AnyContains(report.Warnings, "features.a:"));
            Assert.True(AnyContains(report.Warnings, "features.b.params:"));
            Assert.Equal(true, model.GetFeatureEnabled("b"), "the valid part of an entry is still used");
        }

        [Test]
        public void Load_MissingSchema_Warns()
        {
            var fs = new FakeFileSystem();
            fs.Seed(SettingsPath, "{\"language\":\"en\"}");
            SettingsModel model = Make(fs);

            SettingsLoadReport report = model.Load();

            Assert.Equal("en", model.Language);
            Assert.True(AnyContains(report.Warnings, "schema:"));
        }

        [Test]
        public void Load_NewerSchema_UsesKnownPartsAndKeepsTheRest()
        {
            var fs = new FakeFileSystem();
            fs.Seed(SettingsPath, "{\"schema\":2,\"language\":\"en\",\"futureThing\":{\"a\":[1,2,3]}}");
            SettingsModel model = Make(fs);

            SettingsLoadReport report = model.Load();
            model.SetVerboseLog(true);
            Assert.True(model.Save().Success);

            Assert.True(AnyContains(report.Warnings, "newer"));
            Assert.Equal("en", model.Language);
            JsonObject doc = SavedDocument(fs);
            Assert.Equal(2, doc.GetInt32("schema"), "the file keeps its own format number");
            AssertJson(Parse("{\"a\":[1,2,3]}"), doc.GetObject("futureThing"));
            Assert.True(doc.GetBool("verboseLog"));
        }

        // ---- a broken file ----

        [Test]
        public void Load_TruncatedFile_IsSetAsideAndRecreated()
        {
            var fs = new FakeFileSystem();
            string original = "{\"schema\":1,\"language\":\"en\",\"features\":{\"a\":{\"enabled\":true}}}";
            fs.Seed(SettingsPath, original);
            fs.Truncate(SettingsPath, 30);
            string broken = fs.Text(SettingsPath);
            SettingsModel model = Make(fs);

            SettingsLoadReport report = model.Load();

            Assert.Equal(SettingsFileStatus.Corrupt, report.Status);
            Assert.Equal(SettingsPath + ".corrupt-20261010123456", report.QuarantinedTo);
            Assert.True(fs.Exists(report.QuarantinedTo));
            Assert.Equal(broken, fs.Text(report.QuarantinedTo));
            Assert.True(report.Error.IndexOf("line 1", StringComparison.Ordinal) >= 0, report.Error);

            Assert.True(report.WriteResult.Success);
            JsonObject fresh = SavedDocument(fs);
            Assert.Equal("ru", fresh.GetString("language"), "a new file with defaults");
            Assert.Equal("ru", model.Language);
            Assert.Null(model.GetFeatureEnabled("a"), "nothing of the broken file is used");
        }

        [Test]
        public void Load_RootThatIsNotAnObject_IsCorrupt()
        {
            var fs = new FakeFileSystem();
            fs.Seed(SettingsPath, "[1,2,3]");
            SettingsModel model = Make(fs);

            SettingsLoadReport report = model.Load();

            Assert.Equal(SettingsFileStatus.Corrupt, report.Status);
            Assert.True(fs.Exists(report.QuarantinedTo));
            Assert.Equal("[1,2,3]", fs.Text(report.QuarantinedTo));
        }

        [Test]
        public void Load_UnreadableFile_IsTreatedAsCorrupt()
        {
            var fs = new FakeFileSystem();
            fs.Seed(SettingsPath, "{\"schema\":1}");
            fs.FailOn(FsOp.ReadAllText, "ptmod_settings.json");
            SettingsModel model = Make(fs);

            SettingsLoadReport report = model.Load();

            Assert.Equal(SettingsFileStatus.Corrupt, report.Status);
            Assert.True(report.Error.IndexOf("cannot be read", StringComparison.Ordinal) >= 0, report.Error);
        }

        [Test]
        public void Load_CorruptFileTwiceInTheSameSecond_GetsDistinctNames()
        {
            var fs = new FakeFileSystem();
            SettingsModel model = Make(fs);
            fs.Seed(SettingsPath, "{");
            string first = model.Load().QuarantinedTo;
            fs.Seed(SettingsPath, "{{");
            string second = model.Load().QuarantinedTo;

            Assert.NotEqual(first, second);
            Assert.True(second.StartsWith(SettingsPath + ".corrupt-20261010123456", StringComparison.Ordinal));
            Assert.Equal(2, fs.FilesContaining(".corrupt-").Count);
        }

        // ---- declaring parameters ----

        [Test]
        public void Declare_ParamAbsentFromFile_UsesDefaultWithoutWarning()
        {
            var fs = new FakeFileSystem();
            SettingsModel model = Make(fs);
            model.Load();
            var warnings = new List<string>();

            Assert.True(model.Declare(FloatSpec("fs", "boost", 1.5, 0.5, 2.0), warnings));

            Assert.Equal(0, warnings.Count);
            Assert.Near(1.5, model.GetValue("fs", "boost"));
            Assert.True(model.IsDeclared("fs", "boost"));
            Assert.False(model.IsDeclared("fs", "other"));
        }

        [Test]
        public void Declare_ValueOutsideRange_UsesDefaultAndWarnsWithPath()
        {
            var fs = new FakeFileSystem();
            fs.Seed(SettingsPath, "{\"schema\":1,\"features\":{\"fs\":{\"params\":{\"boost\":9,\"low\":-1,\"count\":50}}}}");
            SettingsModel model = Make(fs);
            model.Load();
            var warnings = new List<string>();

            model.Declare(FloatSpec("fs", "boost", 1.0, 0.5, 2.0), warnings);
            model.Declare(FloatSpec("fs", "low", 0.25, 0.0, 1.0), warnings);
            model.Declare(IntSpec("fs", "count", 3, 0, 10), warnings);

            Assert.Equal(3, warnings.Count);
            Assert.True(warnings[0].StartsWith("features.fs.params.boost:", StringComparison.Ordinal), warnings[0]);
            Assert.True(warnings[1].StartsWith("features.fs.params.low:", StringComparison.Ordinal), warnings[1]);
            Assert.True(warnings[2].StartsWith("features.fs.params.count:", StringComparison.Ordinal), warnings[2]);
            Assert.Near(1.0, model.GetValue("fs", "boost"), 1e-12, "the default, not a clamped 2.0");
            Assert.Near(0.25, model.GetValue("fs", "low"));
            Assert.Equal(3, model.GetInt("fs", "count"));
        }

        [Test]
        public void Declare_ValueOfWrongType_UsesDefaultAndWarns()
        {
            var fs = new FakeFileSystem();
            fs.Seed(SettingsPath, "{\"schema\":1,\"features\":{\"fs\":{\"params\":{\"f\":\"fast\",\"i\":1.5,\"b\":1,\"n\":null}}}}");
            SettingsModel model = Make(fs);
            model.Load();
            var warnings = new List<string>();

            model.Declare(FloatSpec("fs", "f", 1.0, 0.0, 2.0), warnings);
            model.Declare(IntSpec("fs", "i", 4, 0, 10), warnings);
            model.Declare(BoolSpec("fs", "b", false), warnings);
            model.Declare(BoolSpec("fs", "n", true), warnings);

            Assert.Equal(4, warnings.Count);
            Assert.True(AnyContains(warnings, "features.fs.params.f: expected a number, got a string"));
            Assert.True(AnyContains(warnings, "features.fs.params.i: expected a whole number"));
            Assert.True(AnyContains(warnings, "features.fs.params.b: expected true or false, got a number"));
            Assert.True(AnyContains(warnings, "features.fs.params.n:"));
            Assert.Near(1.0, model.GetValue("fs", "f"));
            Assert.Equal(4, model.GetInt("fs", "i"));
            Assert.False(model.GetBool("fs", "b"));
            Assert.True(model.GetBool("fs", "n"));
        }

        [Test]
        public void Declare_ValueInRange_IsUsedAsIs()
        {
            var fs = new FakeFileSystem();
            fs.Seed(SettingsPath, "{\"schema\":1,\"features\":{\"fs\":{\"params\":{\"f\":0.5,\"i\":10,\"b\":true}}}}");
            SettingsModel model = Make(fs);
            model.Load();
            var warnings = new List<string>();

            model.Declare(FloatSpec("fs", "f", 1.0, 0.5, 2.0), warnings);
            model.Declare(IntSpec("fs", "i", 3, 0, 10), warnings);
            model.Declare(BoolSpec("fs", "b", false), warnings);

            Assert.Equal(0, warnings.Count);
            Assert.Near(0.5, model.GetValue("fs", "f"), 1e-12, "the lower end is legal");
            Assert.Equal(10, model.GetInt("fs", "i"), "the upper end is legal");
            Assert.True(model.GetBool("fs", "b"));
        }

        [Test]
        public void Declare_BeforeLoad_IsEvaluatedAgainstTheFileAtLoad()
        {
            var fs = new FakeFileSystem();
            fs.Seed(SettingsPath, "{\"schema\":1,\"features\":{\"fs\":{\"params\":{\"boost\":1.5,\"bad\":99}}}}");
            SettingsModel model = Make(fs);
            model.Declare(FloatSpec("fs", "boost", 1.0, 0.5, 2.0), null);
            model.Declare(FloatSpec("fs", "bad", 1.0, 0.5, 2.0), null);
            Assert.Near(1.0, model.GetValue("fs", "boost"), 1e-12, "before the file is read the default is in force");

            SettingsLoadReport report = model.Load();

            Assert.Near(1.5, model.GetValue("fs", "boost"));
            Assert.Near(1.0, model.GetValue("fs", "bad"));
            Assert.True(AnyContains(report.Warnings, "features.fs.params.bad:"));
        }

        [Test]
        public void Declare_UnusableSpec_IsRejectedWithReason()
        {
            var model = Make(new FakeFileSystem());
            var warnings = new List<string>();

            Assert.False(model.Declare(null, warnings));
            Assert.False(model.Declare(FloatSpec("", "k", 1, 0, 2), warnings));
            Assert.False(model.Declare(FloatSpec("fs", "", 1, 0, 2), warnings));
            Assert.False(model.Declare(FloatSpec("fs", "k", 1, 2, 0), warnings), "Min above Max");
            Assert.False(model.Declare(FloatSpec("fs", "k", double.NaN, 0, 2), warnings), "NaN default");
            Assert.False(model.Declare(FloatSpec("fs", "k", 1, 0, double.PositiveInfinity), warnings), "infinite Max");
            Assert.False(model.Declare(IntSpec("fs", "k", 1, 0.2, 0.8), warnings), "an Int range with no whole number in it");
            Assert.False(model.Declare(new ParamSpec { FeatureId = "fs", Key = "k", Type = (ParamType)42 }, warnings));

            Assert.Equal(8, warnings.Count);
            Assert.False(model.IsDeclared("fs", "k"));
            Assert.False(model.Declare(null, null), "a null list of warnings is fine");
        }

        [Test]
        public void Declare_Normalizes_BoolIntAndDefault()
        {
            var model = Make(new FakeFileSystem());

            model.Declare(BoolSpec("fs", "b", true), null);
            model.Declare(IntSpec("fs", "i", 100, 0.2, 9.8), null);
            model.Declare(FloatSpec("fs", "f", 5, 0, 1), null);

            ParamSpec b = model.GetSpec("fs", "b");
            Assert.Equal(0.0, b.Min);
            Assert.Equal(1.0, b.Max);
            Assert.Equal(1.0, b.Default);

            ParamSpec i = model.GetSpec("fs", "i");
            Assert.Equal(1.0, i.Min, "rounded inward");
            Assert.Equal(9.0, i.Max, "rounded inward");
            Assert.Equal(9.0, i.Default, "a default outside the range is brought into it");

            Assert.Equal(1.0, model.GetSpec("fs", "f").Default);
            Assert.Equal(9, model.GetInt("fs", "i"));
        }

        [Test]
        public void Declare_SameKeyTwice_IdenticalIsFineDifferentKeepsTheFirst()
        {
            var model = Make(new FakeFileSystem());
            var warnings = new List<string>();

            Assert.True(model.Declare(FloatSpec("fs", "k", 1, 0, 2), warnings));
            Assert.True(model.Declare(FloatSpec("fs", "k", 1, 0, 2), warnings), "the same definition again");
            Assert.Equal(0, warnings.Count);

            Assert.False(model.Declare(FloatSpec("fs", "k", 1, 0, 5), warnings));
            Assert.Equal(1, warnings.Count);
            Assert.Equal(2.0, model.GetSpec("fs", "k").Max, "the first declaration stays");
            Assert.Equal(1, model.SpecsOf("fs").Count);
        }

        [Test]
        public void GetSpec_ReturnsCopies_AndSpecsOfKeepsDeclarationOrder()
        {
            var model = Make(new FakeFileSystem());
            var original = FloatSpec("fs", "a", 1, 0, 2);
            model.Declare(original, null);
            model.Declare(IntSpec("other", "x", 1, 0, 2), null);
            model.Declare(BoolSpec("fs", "b", true), null);

            original.Max = 100;                       // changing the caller's object after Declare does not matter
            model.GetSpec("fs", "a").Max = 100;       // nor does changing a returned copy

            Assert.Equal(2.0, model.GetSpec("fs", "a").Max);
            Assert.Equal(new[] { "a", "b" }, model.SpecsOf("fs").Select(s => s.Key).ToArray());
            Assert.Equal(0, model.SpecsOf("none").Count);
            Assert.Null(model.GetSpec("fs", "missing"));
            Assert.Null(model.GetSpec(null, "a"));
        }

        // ---- changing values ----

        [Test]
        public void Set_Float_ClampsToTheRange()
        {
            var model = Make(new FakeFileSystem());
            model.Declare(FloatSpec("fs", "boost", 1.0, 0.5, 2.0), null);
            double stored;

            Assert.True(model.Set("fs", "boost", 5.0, out stored));
            Assert.Equal(2.0, stored);
            Assert.Near(2.0, model.GetValue("fs", "boost"));

            Assert.True(model.Set("fs", "boost", -3.0, out stored));
            Assert.Equal(0.5, stored);

            Assert.True(model.Set("fs", "boost", 1.25, out stored));
            Assert.Equal(1.25, stored);

            Assert.True(model.Set("fs", "boost", double.PositiveInfinity, out stored));
            Assert.Equal(2.0, stored);
            Assert.True(model.Set("fs", "boost", double.NegativeInfinity, out stored));
            Assert.Equal(0.5, stored);
        }

        [Test]
        public void Set_Int_RoundsHalvesAwayFromZeroThenClamps()
        {
            var model = Make(new FakeFileSystem());
            model.Declare(IntSpec("fs", "n", 0, -5, 10), null);
            double stored;

            model.Set("fs", "n", 2.5, out stored);
            Assert.Equal(3.0, stored);
            model.Set("fs", "n", -2.5, out stored);
            Assert.Equal(-3.0, stored);
            model.Set("fs", "n", 2.4, out stored);
            Assert.Equal(2.0, stored);
            model.Set("fs", "n", 99, out stored);
            Assert.Equal(10.0, stored);
            Assert.Equal(10, model.GetInt("fs", "n"));
        }

        [Test]
        public void Set_Bool_AnyNonZeroIsTrue()
        {
            var model = Make(new FakeFileSystem());
            model.Declare(BoolSpec("fs", "on", false), null);
            double stored;

            Assert.True(model.Set("fs", "on", 0.3, out stored));
            Assert.Equal(1.0, stored);
            Assert.True(model.GetBool("fs", "on"));
            Assert.True(model.Set("fs", "on", 0, out stored));
            Assert.False(model.GetBool("fs", "on"));
        }

        [Test]
        public void Set_ReportsChangeOnlyWhenTheValueChanges()
        {
            var model = Make(new FakeFileSystem());
            model.Declare(FloatSpec("fs", "boost", 1.0, 0.5, 2.0), null);
            double stored;

            Assert.False(model.Set("fs", "boost", 1.0, out stored), "the same as the default");
            Assert.True(model.Set("fs", "boost", 1.5, out stored));
            Assert.False(model.Set("fs", "boost", 1.5, out stored), "the same as now");
            Assert.True(model.Set("fs", "boost", 7.0, out stored), "7 clamps to 2, which differs from 1.5");
            Assert.Equal(2.0, stored);
            Assert.False(model.Set("fs", "boost", 7.0, out stored), "7 clamps to 2 again: nothing changed");
            Assert.Equal(2.0, stored);
        }

        [Test]
        public void Set_NaNUndeclaredAndNullKeys_ChangeNothing()
        {
            var model = Make(new FakeFileSystem());
            model.Declare(FloatSpec("fs", "boost", 1.0, 0.5, 2.0), null);
            double stored;

            Assert.False(model.Set("fs", "boost", double.NaN, out stored));
            Assert.Equal(1.0, stored, "the value in force is reported");
            Assert.False(model.Set("fs", "nope", 1.0, out stored));
            Assert.False(model.Set("nope", "boost", 1.0, out stored));
            Assert.False(model.Set(null, "boost", 1.0, out stored));
            Assert.False(model.Set("fs", null, 1.0, out stored));
            Assert.False(model.IsDirty);
            Assert.Near(1.0, model.GetValue("fs", "boost"));
        }

        [Test]
        public void Set_ThenSave_RoundTripsThroughTheFile()
        {
            var fs = new FakeFileSystem();
            SettingsModel model = Make(fs);
            model.Load();
            model.Declare(FloatSpec("fs", "boost", 1.0, 0.5, 2.0), null);
            model.Declare(IntSpec("fs", "n", 3, 0, 10), null);
            model.Declare(BoolSpec("fs", "on", false), null);
            double stored;

            model.Set("fs", "boost", 1.25, out stored);
            model.Set("fs", "n", 7, out stored);
            model.Set("fs", "on", 1, out stored);
            Assert.True(model.IsDirty);
            Assert.True(model.Save().Success);
            Assert.False(model.IsDirty);

            JsonObject parameters = SavedDocument(fs).GetObject("features").GetObject("fs").GetObject("params");
            Assert.Equal(1.25, parameters.GetDouble("boost"));
            Assert.Equal(7, parameters.GetInt32("n"));
            Assert.True(parameters.GetBool("on"));
            Assert.True(parameters["n"].ToString() == "7", "an Int is written without a fraction");

            SettingsModel again = Make(fs);
            again.Load();
            var warnings = new List<string>();
            again.Declare(FloatSpec("fs", "boost", 1.0, 0.5, 2.0), warnings);
            again.Declare(IntSpec("fs", "n", 3, 0, 10), warnings);
            again.Declare(BoolSpec("fs", "on", false), warnings);
            Assert.Equal(0, warnings.Count);
            Assert.Near(1.25, again.GetValue("fs", "boost"));
            Assert.Equal(7, again.GetInt("fs", "n"));
            Assert.True(again.GetBool("fs", "on"));
        }

        [Test]
        public void Set_ReplacesABadValueInTheFileEvenWhenTheEffectiveValueStays()
        {
            var fs = new FakeFileSystem();
            fs.Seed(SettingsPath, "{\"schema\":1,\"features\":{\"fs\":{\"params\":{\"on\":\"yes\"}}}}");
            SettingsModel model = Make(fs);
            model.Load();
            model.Declare(BoolSpec("fs", "on", true), null);
            double stored;

            bool changed = model.Set("fs", "on", 1, out stored);

            Assert.False(changed, "the effective value was already true (the default)");
            Assert.True(model.IsDirty, "but the file still says \"yes\"");
            Assert.True(model.Save().Success);
            Assert.True(SavedDocument(fs).GetObject("features").GetObject("fs").GetObject("params").GetBool("on", false));
        }

        // ---- features, language, verbose ----

        [Test]
        public void SetFeatureEnabled_WritesEntryWithEmptyParams_AndReportsChange()
        {
            var fs = new FakeFileSystem();
            SettingsModel model = Make(fs);
            model.Load();

            Assert.True(model.SetFeatureEnabled("event-system", false), "from \"no choice\" to a choice");
            Assert.False(model.SetFeatureEnabled("event-system", false), "same choice");
            Assert.True(model.SetFeatureEnabled("event-system", true));
            Assert.True(model.IsDirty);
            Assert.True(model.Save().Success);

            JsonObject entry = SavedDocument(fs).GetObject("features").GetObject("event-system");
            Assert.True(entry.GetBool("enabled"));
            Assert.NotNull(entry.GetObject("params"));
            Assert.Equal(true, model.GetFeatureEnabled("event-system"));
            Assert.False(model.SetFeatureEnabled("", true));
            Assert.False(model.SetFeatureEnabled(null, true));
        }

        [Test]
        public void SetFeatureEnabled_KeepsTheParamsOfTheEntry()
        {
            var fs = new FakeFileSystem();
            fs.Seed(SettingsPath, "{\"schema\":1,\"features\":{\"fs\":{\"enabled\":false,\"params\":{\"boost\":1.25,\"unknown\":[1]}}}}");
            SettingsModel model = Make(fs);
            model.Load();

            model.SetFeatureEnabled("fs", true);
            model.Save();

            JsonObject entry = SavedDocument(fs).GetObject("features").GetObject("fs");
            Assert.True(entry.GetBool("enabled"));
            Assert.Equal(1.25, entry.GetObject("params").GetDouble("boost"));
            AssertJson(Parse("{\"x\":[1]}")["x"], entry.GetObject("params")["unknown"]);
        }

        [Test]
        public void SetLanguage_AcceptsRuAndEnOnly()
        {
            var fs = new FakeFileSystem();
            SettingsModel model = Make(fs);
            model.Load();

            Assert.True(model.SetLanguage("EN"));
            Assert.Equal("en", model.Language);
            Assert.False(model.SetLanguage("en"), "no change");
            Assert.False(model.SetLanguage("de"));
            Assert.False(model.SetLanguage(null));
            Assert.False(model.SetLanguage(""));
            Assert.Equal("en", model.Language);

            model.Save();
            Assert.Equal("en", SavedDocument(fs).GetString("language"));
        }

        [Test]
        public void SetVerboseLog_WritesTheFlag()
        {
            var fs = new FakeFileSystem();
            SettingsModel model = Make(fs);
            model.Load();

            Assert.True(model.SetVerboseLog(true));
            Assert.False(model.SetVerboseLog(true));
            Assert.True(model.VerboseLog);
            model.Save();

            Assert.True(SavedDocument(fs).GetBool("verboseLog"));
        }

        // ---- debug flags ----

        [Test]
        public void Debug_ReleaseBuildIgnoresTheSectionCompletely()
        {
            var fs = new FakeFileSystem();
            fs.Seed(SettingsPath, "{\"schema\":1,\"debug\":{\"failInit\":true,\"stress\":\"maybe\"}}");
            SettingsModel model = Make(fs, false);

            SettingsLoadReport report = model.Load();

            Assert.False(model.Debug("debug.failInit"));
            Assert.False(model.Debug("failInit"));
            Assert.Equal(0, report.Warnings.Count, "not even a warning about the bad flag");
            Assert.False(model.DebugBuild);
        }

        [Test]
        public void Debug_DebugBuildReadsFlagsAndReportsBadOnes()
        {
            var fs = new FakeFileSystem();
            fs.Seed(SettingsPath, "{\"schema\":1,\"debug\":{\"failInit\":true,\"stress\":\"maybe\",\"events\":true}}");
            SettingsModel model = Make(fs, true);

            SettingsLoadReport report = model.Load();

            Assert.True(model.Debug("debug.failInit"));
            Assert.True(model.Debug("debug.events"), "any flag name is allowed");
            Assert.False(model.Debug("debug.stress"));
            Assert.True(AnyContains(report.Warnings, "debug.stress:"));
            Assert.False(model.Debug(null));
            Assert.False(model.Debug(""));
        }

        // ---- saving ----

        [Test]
        public void Save_KeepsUnknownMembersAndTheOrderOfTheFile()
        {
            var fs = new FakeFileSystem();
            string text = "{\"schema\":1,\"note\":\"hand written\",\"language\":\"ru\",\"features\":{\"removed-feature\":{\"enabled\":true,\"params\":{\"x\":1}}},\"zzz\":[1,2]}";
            fs.Seed(SettingsPath, text);
            SettingsModel model = Make(fs);
            model.Load();

            model.SetLanguage("en");
            model.SetFeatureEnabled("new-feature", true);
            Assert.True(model.Save().Success);

            JsonObject doc = SavedDocument(fs);
            Assert.Equal(new[] { "schema", "note", "language", "features", "zzz" }, doc.Keys.ToArray());
            Assert.Equal("hand written", doc.GetString("note"));
            AssertJson(Parse("{\"enabled\":true,\"params\":{\"x\":1}}"), doc.GetObject("features")["removed-feature"]);
            Assert.Equal(new[] { "removed-feature", "new-feature" }, doc.GetObject("features").Keys.ToArray());
        }

        [Test]
        public void Save_Atomic_UsesTempFileThenReplace()
        {
            var fs = new FakeFileSystem();
            SettingsModel model = Make(fs);
            model.Load();                       // creates the file (Move)
            model.SetVerboseLog(true);
            fs.Seed(SettingsPath + ".bak", "old");   // a stale file the settings must not touch

            Assert.True(model.Save().Success);

            Assert.True(fs.Log.Contains("WriteAllText " + SettingsPath + ".tmp"));
            Assert.True(fs.Log.Any(l => l.StartsWith("Replace " + SettingsPath + ".tmp -> " + SettingsPath, StringComparison.Ordinal)));
            Assert.False(fs.Exists(SettingsPath + ".tmp"));
            Assert.Equal("old", fs.Text(SettingsPath + ".bak"), "settings keep no backup of their own");
        }

        [Test]
        public void Save_ReplaceFailure_KeepsTheOldFileRemovesTheTempAndStaysDirty()
        {
            var fs = new FakeFileSystem();
            string original = "{\"schema\":1,\"language\":\"ru\"}";
            fs.Seed(SettingsPath, original);
            SettingsModel model = Make(fs);
            model.Load();
            model.SetLanguage("en");
            fs.FailOn(FsOp.Replace, "ptmod_settings.json");

            FileWriteResult failed = model.Save();

            Assert.False(failed.Success);
            Assert.True(failed.Error.IndexOf("injected failure", StringComparison.Ordinal) >= 0, failed.Error);
            Assert.Equal(original, fs.Text(SettingsPath), "the previous file is untouched");
            Assert.False(fs.Exists(SettingsPath + ".tmp"), "the temporary file is removed");
            Assert.True(model.IsDirty);

            Assert.True(model.Save().Success, "the next try works");
            Assert.False(model.IsDirty);
            Assert.Equal("en", SavedDocument(fs).GetString("language"));
        }

        [Test]
        public void Save_CreatesTheMissingDirectory()
        {
            var fs = new FakeFileSystem();
            var model = new SettingsModel(fs, FakeFileSystem.Root + @"\deep\er\ptmod_settings.json", false, () => Now);

            SettingsLoadReport report = model.Load();

            Assert.True(report.WriteResult.Success);
            Assert.True(fs.Exists(FakeFileSystem.Root + @"\deep\er\ptmod_settings.json"));
        }

        [Test]
        public void Load_CreateFailure_IsReportedAndTheModelStaysUsable()
        {
            var fs = new FakeFileSystem();
            fs.FailOn(FsOp.WriteAllText, "ptmod_settings.json");
            SettingsModel model = Make(fs);

            SettingsLoadReport report = model.Load();

            Assert.Equal(SettingsFileStatus.Created, report.Status);
            Assert.False(report.WriteResult.Success);
            Assert.Equal("ru", model.Language);
            model.SetLanguage("en");
            Assert.True(model.Save().Success, "a later save creates the file");
        }

        // ---- typed getters ----

        [Test]
        public void Getters_ConvertTheStoredNumber()
        {
            var model = Make(new FakeFileSystem());
            model.Declare(FloatSpec("fs", "f", 0.1, 0, 1), null);
            model.Declare(FloatSpec("fs", "big", 3e9, 0, 1e10), null);

            Assert.Equal(0.1f, model.GetFloat("fs", "f"));
            Assert.Equal(0, model.GetInt("fs", "f"), "0.1 rounds to 0");
            Assert.True(model.GetBool("fs", "f"), "non-zero is true");
            Assert.Equal(int.MaxValue, model.GetInt("fs", "big"), "beyond Int32 saturates instead of wrapping");
            Assert.Equal(0, model.GetInt("fs", "undeclared"));
            Assert.False(model.GetBool("fs", "undeclared"));
            Assert.Equal(0f, model.GetFloat(null, null));
        }

        // ---- ParamSpec ----

        [Test]
        public void ParamSpec_ClampAndInRange()
        {
            var f = FloatSpec("a", "b", 1, 0.5, 2);
            string problem;
            Assert.True(f.Normalize(out problem));
            Assert.Equal(0.5, f.Clamp(0.1));
            Assert.Equal(2.0, f.Clamp(3));
            Assert.Equal(1.0, f.Clamp(double.NaN), "NaN gives the default");
            Assert.True(f.InRange(0.5));
            Assert.True(f.InRange(2));
            Assert.False(f.InRange(2.0001));
            Assert.False(f.InRange(double.NaN));

            var i = IntSpec("a", "b", 1, 0, 5);
            Assert.True(i.Normalize(out problem));
            Assert.True(i.InRange(3));
            Assert.False(i.InRange(3.5), "not a whole number");
            Assert.Equal(4.0, i.Clamp(3.5));

            var b = BoolSpec("a", "b", true);
            Assert.True(b.Normalize(out problem));
            Assert.True(b.InRange(0));
            Assert.True(b.InRange(1));
            Assert.False(b.InRange(2));
            Assert.Equal(1.0, b.Clamp(-7));
            Assert.Equal(0.0, b.Clamp(0));
        }

        [Test]
        public void ParamSpec_CloneAndSameDefinition()
        {
            var a = new ParamSpec { FeatureId = "f", Key = "k", LabelKey = "x.l", TooltipKey = "x.t", Type = ParamType.Float, Default = 1, Min = 0, Max = 2, Step = 0.1, RequiresReload = true };
            ParamSpec copy = a.Clone();

            Assert.True(a.SameDefinition(copy));
            Assert.Equal("x.l", copy.LabelKey);
            copy.LabelKey = "other";
            Assert.True(a.SameDefinition(copy), "labels do not count");
            copy.RequiresReload = false;
            Assert.False(a.SameDefinition(copy));
            Assert.False(a.SameDefinition(null));
            Assert.Equal("x.l", a.LabelKey, "the clone is independent");
        }

        [Test]
        public void ParamSpec_NormalizeFixesABadStep()
        {
            var f = new ParamSpec { FeatureId = "a", Key = "b", Type = ParamType.Float, Default = 1, Min = 0, Max = 2, Step = -1 };
            var i = new ParamSpec { FeatureId = "a", Key = "b", Type = ParamType.Int, Default = 1, Min = 0, Max = 5, Step = 0.2 };
            string problem;

            Assert.True(f.Normalize(out problem));
            Assert.True(i.Normalize(out problem));
            Assert.Equal(0.0, f.Step, "a Float with a bad step is continuous");
            Assert.Equal(1.0, i.Step, "an Int step is at least 1");
        }

        [Test]
        public void Constructor_RejectsMissingArguments()
        {
            var fs = new FakeFileSystem();
            Assert.Throws<ArgumentNullException>(() => new SettingsModel(null, SettingsPath));
            Assert.Throws<ArgumentException>(() => new SettingsModel(fs, ""));
        }
    }
}
