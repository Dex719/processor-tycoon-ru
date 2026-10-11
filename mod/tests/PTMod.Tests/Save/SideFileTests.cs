using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using PTMod.Core;
using PTMod.Core.Logic.Json;
using PTMod.Core.Logic.Save;
using PTMod.Tests.Common;

namespace PTMod.Tests.Save
{
    /// <summary>Shared helpers of the side-file tests.</summary>
    internal static class SideFileTestKit
    {
        public const string SavesDir = FakeFileSystem.Root + @"\Saves";
        public static readonly DateTime Now = new DateTime(2026, 10, 10, 12, 34, 56, DateTimeKind.Utc);

        /// <summary>A section of a feature this build does not know (as if the feature was removed).</summary>
        public const string Gpu = "{\"_schema\":2,\"lines\":[\"a\",{\"b\":[1,2,3]}],\"ratio\":0.5,\"big\":9007199254740993}";

        public static string SidePath(string saveName)
        {
            return SavesDir + "\\" + saveName + ".ptmod.json";
        }

        public static FakeFileSystem NewFs()
        {
            var fs = new FakeFileSystem();
            fs.AddDirectory(SavesDir);
            return fs;
        }

        public static SideFileIo NewIo(FakeFileSystem fs)
        {
            return new SideFileIo(fs, SavesDir, () => Now);
        }

        /// <summary>A side file with the usual header and the given members of <c>features</c>.</summary>
        public static string File1(string features)
        {
            return "{\"schema\":1,\"modVersion\":\"0.1.0\",\"gameVersion\":\"0.2.3\",\"saveName\":\"1985_3_1\",\"gameDate\":\"1985-03-01\","
                   + "\"writtenUtc\":\"2026-10-10T18:42:07Z\",\"features\":{" + features + "}}";
        }

        public static JsonObject Parse(string text)
        {
            return (JsonObject)ParseNode(text);
        }

        public static JsonNode ParseNode(string text)
        {
            JsonNode node;
            JsonError error;
            Assert.True(ModJson.TryParse(text, out node, out error), "must be valid JSON: " + error);
            return node;
        }

        public static void AssertJson(JsonNode expected, JsonNode actual)
        {
            Assert.True(expected != null && expected.Equals(actual), "expected " + expected + " but got " + actual);
        }

        public static bool AnyContains(IEnumerable<string> lines, string fragment)
        {
            return lines.Any(l => l.IndexOf(fragment, StringComparison.Ordinal) >= 0);
        }

        public static JsonObject Features(SideFileBuildReport build)
        {
            return build.Document.GetObject("features");
        }

        public static SideFileModel Model(params Part[] participants)
        {
            var model = new SideFileModel();
            Register(model, participants);
            return model;
        }

        public static void Register(SideFileModel model, params Part[] participants)
        {
            foreach (Part part in participants)
            {
                string problem;
                Assert.True(model.TryRegister(part, out problem), problem);
            }
        }
    }

    /// <summary>A participant that records what the model does to it.</summary>
    internal sealed class Part : ISideFileParticipant
    {
        public Part(string id, int version = 1)
        {
            SectionId = id;
            SchemaVersion = version;
        }

        public string SectionId { get; }

        public int SchemaVersion { get; set; }

        /// <summary>The "state of the feature"; -1 = defaults, -2 = the section had no "v".</summary>
        public int Value = -1;

        public int ResetCount, ReadCount, WriteCount, LastSchema;
        public JsonObject LastRead;
        public bool ThrowOnRead, ThrowOnReset, ThrowOnWrite, ReturnNull, MutateInRead;
        public JsonObject Extra;   // when set, Write() returns exactly this

        public void ResetToDefaults()
        {
            ResetCount++;
            Value = -1;
            if (ThrowOnReset) throw new InvalidOperationException("reset boom");
        }

        public void Read(JsonObject section, int schema)
        {
            ReadCount++;
            LastRead = section;
            LastSchema = schema;
            if (MutateInRead) section.Remove("v");
            if (ThrowOnRead) throw new InvalidOperationException("read boom");
            Value = section.GetInt32("v", -2);
        }

        public JsonObject Write()
        {
            WriteCount++;
            if (ThrowOnWrite) throw new InvalidOperationException("write boom");
            if (ReturnNull) return null;
            if (Extra != null) return Extra;
            return new JsonObject().Set("v", Value);
        }
    }

    /// <summary>
    /// The model of the side save file (FR-5, TSK-016): distribution of sections, passthrough, versions, damage, exceptions of participants.
    /// No disk, no clock: <see cref="SideFileModel"/> takes and gives text and trees.
    /// </summary>
    [Covers(typeof(SideFileModel), typeof(SideFileStatus), typeof(SideFileReadReport), typeof(SideFileBuildReport), typeof(ISideFileParticipant))]
    public class SideFileModelTests
    {
        // ---- distribution ----

        [Test]
        public void Apply_HandsEachSectionToItsParticipantWithTheStoredSchema()
        {
            var a = new Part("fs", 2);
            var b = new Part("other");
            SideFileModel model = SideFileTestKit.Model(a, b);

            SideFileReadReport report = model.Apply(SideFileTestKit.File1("\"fs\":{\"_schema\":2,\"v\":7},\"other\":{\"_schema\":1,\"v\":9}"));

            Assert.Equal(SideFileStatus.Loaded, report.Status);
            Assert.Equal(SideFileStatus.Loaded, model.Status);
            Assert.Equal(0, report.Warnings.Count);
            Assert.Equal(7, a.Value);
            Assert.Equal(2, a.LastSchema);
            Assert.False(a.LastRead.ContainsKey("_schema"), "the participant never sees the version field");
            Assert.Equal(9, b.Value);
            Assert.Equal(1, b.LastSchema);
            Assert.Equal(0, a.ResetCount + b.ResetCount);
        }

        [Test]
        public void Apply_OlderStoredVersion_IsPassedSoTheParticipantCanMigrate()
        {
            var a = new Part("fs", 3);
            SideFileModel model = SideFileTestKit.Model(a);

            SideFileReadReport report = model.Apply(SideFileTestKit.File1("\"fs\":{\"_schema\":1,\"v\":5}"));

            Assert.Equal(SideFileStatus.Loaded, report.Status);
            Assert.Equal(1, a.LastSchema);
            Assert.Equal(5, a.Value);
            Assert.Equal(0, report.NewerSections.Count);

            SideFileBuildReport build = model.Build(null);
            Assert.Equal(3, SideFileTestKit.Features(build).GetObject("fs").GetInt32("_schema"), "written back with the current version");
        }

        [Test]
        public void Apply_NoSectionForAParticipant_GivesDefaults()
        {
            var a = new Part("fs");
            var b = new Part("other");
            a.Value = 5;
            SideFileModel model = SideFileTestKit.Model(a, b);

            SideFileReadReport report = model.Apply(SideFileTestKit.File1("\"other\":{\"_schema\":1,\"v\":2}"));

            Assert.Equal(SideFileStatus.Loaded, report.Status);
            Assert.Equal(1, a.ResetCount);
            Assert.Equal(0, a.ReadCount);
            Assert.Equal(-1, a.Value);
            Assert.Equal(2, b.Value);

            Part c = new Part("third");
            SideFileTestKit.Register(model, c);
            model.Apply("{\"schema\":1}");
            Assert.Equal(1, c.ResetCount, "a file without 'features' at all is still a valid, empty file");
            Assert.Equal(SideFileStatus.Loaded, model.Status);
        }

        // AC-5.3 (model level)
        [Test]
        public void Build_UnknownSection_IsWrittenBackUnchanged()
        {
            var a = new Part("fs");
            SideFileModel model = SideFileTestKit.Model(a);
            model.Apply(SideFileTestKit.File1("\"fs\":{\"_schema\":1,\"v\":3},\"gpu-product-line\":" + SideFileTestKit.Gpu));
            Assert.Equal(new[] { "gpu-product-line" }, model.RetainedSections.ToArray());

            a.Value = 4;
            JsonObject features = SideFileTestKit.Features(model.Build(null));

            SideFileTestKit.AssertJson(SideFileTestKit.Parse(SideFileTestKit.Gpu), features["gpu-product-line"]);
            SideFileTestKit.AssertJson(SideFileTestKit.Parse("{\"_schema\":1,\"v\":4}"), features["fs"]);
            Assert.Equal(new[] { "fs", "gpu-product-line" }, features.Keys.ToArray(), "participants first, then the sections nobody owns");
        }

        [Test]
        public void Build_PassthroughSurvivesManyRoundTrips()
        {
            var a = new Part("fs");
            SideFileModel model = SideFileTestKit.Model(a);
            model.Apply(SideFileTestKit.File1("\"gpu-product-line\":" + SideFileTestKit.Gpu));

            for (int i = 0; i < 3; i++)
            {
                JsonObject document = model.Build(null).Document;
                model.Apply(document.ToString());
            }

            JsonObject features = SideFileTestKit.Features(model.Build(null));
            SideFileTestKit.AssertJson(SideFileTestKit.Parse(SideFileTestKit.Gpu), features["gpu-product-line"]);
            Assert.True(features.ToString().Contains("9007199254740993"), "a long integer is kept exactly");
        }

        // ---- inactive participants ----

        [Test]
        public void InactiveParticipant_IsNotReadNotWritten_AndItsSectionPassesThrough()
        {
            bool active = false;
            var a = new Part("fs");
            var model = new SideFileModel(id => active);
            SideFileTestKit.Register(model, a);

            SideFileReadReport report = model.Apply(SideFileTestKit.File1("\"fs\":{\"_schema\":1,\"v\":3}"));
            JsonObject features = SideFileTestKit.Features(model.Build(null));

            Assert.Equal(SideFileStatus.Loaded, report.Status);
            Assert.Equal(0, a.ReadCount + a.ResetCount + a.WriteCount, "an inactive participant is left alone");
            SideFileTestKit.AssertJson(SideFileTestKit.Parse("{\"_schema\":1,\"v\":3}"), features["fs"]);

            active = true;                                      // the player switches the feature on in the hub
            SideFileReadReport restored = model.RestoreParticipant("fs");
            Assert.Equal(0, restored.Warnings.Count);
            Assert.Equal(3, a.Value);
            a.Value = 8;
            SideFileTestKit.AssertJson(SideFileTestKit.Parse("{\"_schema\":1,\"v\":8}"), SideFileTestKit.Features(model.Build(null))["fs"]);
        }

        [Test]
        public void InactiveWithoutASection_WritesNothing_AndRestoreResetsIt()
        {
            bool active = false;
            var a = new Part("fs");
            var model = new SideFileModel(id => active);
            SideFileTestKit.Register(model, a);
            model.Apply(SideFileTestKit.File1(string.Empty));

            Assert.False(SideFileTestKit.Features(model.Build(null)).ContainsKey("fs"));

            active = true;
            model.RestoreParticipant("fs");
            Assert.Equal(1, a.ResetCount);
            Assert.True(SideFileTestKit.Features(model.Build(null)).ContainsKey("fs"));
        }

        [Test]
        public void ParticipantSwitchedOffAfterWriting_KeepsItsLastState()
        {
            bool active = true;
            var a = new Part("fs");
            var model = new SideFileModel(id => active);
            SideFileTestKit.Register(model, a);
            model.Apply(SideFileTestKit.File1(string.Empty));

            a.Value = 5;
            SideFileTestKit.AssertJson(SideFileTestKit.Parse("{\"_schema\":1,\"v\":5}"), SideFileTestKit.Features(model.Build(null))["fs"]);

            active = false;
            a.Value = 99;
            SideFileTestKit.AssertJson(SideFileTestKit.Parse("{\"_schema\":1,\"v\":5}"), SideFileTestKit.Features(model.Build(null))["fs"]);
            Assert.Equal(1, a.WriteCount, "an inactive participant is not asked again");
        }

        [Test]
        public void ParticipantSwitchedOffBeforeAnySave_KeepsTheStateItHeldWhenItWasSwitchedOff()
        {
            // The gap the capture closes: loaded from the file, changed, switched off, and only then saved. Without the capture the model had
            // dropped the stored copy at Read and never asked the participant, so the next file lost the section.
            bool active = true;
            var a = new Part("fs");
            var model = new SideFileModel(id => active);
            SideFileTestKit.Register(model, a);
            model.Apply(SideFileTestKit.File1("\"fs\":{\"_schema\":1,\"v\":3}"));
            a.Value = 7;                                         // played on after the load

            active = false;                                      // the player switches the feature off; the host captures the state
            SideFileBuildReport captured = model.CaptureParticipant("fs");
            Assert.Equal(0, captured.Warnings.Count);
            a.Value = 99;                                        // whatever happens to the dead feature later is not stored

            SideFileTestKit.AssertJson(SideFileTestKit.Parse("{\"_schema\":1,\"v\":7}"), SideFileTestKit.Features(model.Build(null))["fs"]);
            Assert.Equal(1, a.WriteCount, "asked once, when it was switched off; an inactive participant is not asked by Build");

            active = true;                                       // switched on again: it gets the state of the moment it was switched off
            SideFileReadReport restored = model.RestoreParticipant("fs");
            Assert.Equal(0, restored.Warnings.Count);
            Assert.Equal(7, a.Value);
        }

        [Test]
        public void Capture_RefreshesTheCopyOfAnEarlierSave_SoReEnablingDoesNotGetTheOldState()
        {
            bool active = true;
            var a = new Part("fs");
            var model = new SideFileModel(id => active);
            SideFileTestKit.Register(model, a);
            model.Apply(SideFileTestKit.File1(string.Empty));

            a.Value = 1;
            model.Build(null);                                   // a save: the copy holds 1
            a.Value = 2;                                         // changed after the save

            active = false;
            model.CaptureParticipant("fs");
            active = true;
            model.RestoreParticipant("fs");

            Assert.Equal(2, a.Value, "the state at switch-off, not the state of the last save");
        }

        [Test]
        public void Capture_LeavesAlone_ASectionKeptAsRead_AParticipantThatNeverGotData_AndUnknownIds()
        {
            bool active = true;
            var newer = new Part("newer", 1);
            var late = new Part("late");
            var model = new SideFileModel(id => active);
            SideFileTestKit.Register(model, newer);
            model.Apply(SideFileTestKit.File1("\"newer\":{\"_schema\":5,\"v\":3},\"late\":{\"_schema\":1,\"v\":4}"));
            SideFileTestKit.Register(model, late);               // registered after the load: it was never given its data

            Assert.Equal(0, model.CaptureParticipant("newer").Warnings.Count);
            Assert.Equal(0, model.CaptureParticipant("late").Warnings.Count);
            Assert.Equal(0, model.CaptureParticipant("nobody").Warnings.Count);
            Assert.Equal(0, model.CaptureParticipant(null).Warnings.Count);
            Assert.Equal(0, newer.WriteCount + late.WriteCount, "neither is asked: their stored sections are the truth");

            active = false;
            JsonObject features = SideFileTestKit.Features(model.Build(null));
            SideFileTestKit.AssertJson(SideFileTestKit.Parse("{\"_schema\":5,\"v\":3}"), features["newer"]);
            SideFileTestKit.AssertJson(SideFileTestKit.Parse("{\"_schema\":1,\"v\":4}"), features["late"]);
        }

        [Test]
        public void Capture_WriteThatThrows_KeepsThePreviousSection_WriteThatReturnsNull_LeavesItOut()
        {
            var a = new Part("fs");
            var b = new Part("gs");
            var model = new SideFileModel(id => false);
            SideFileTestKit.Register(model, a, b);
            model.Apply(SideFileTestKit.File1(string.Empty));    // inactive from the start: the sections were reset, nothing is held
            model.RestoreParticipant("fs");
            model.RestoreParticipant("gs");
            a.Value = 5;
            model.CaptureParticipant("fs");                      // holds v = 5

            a.ThrowOnWrite = true;
            SideFileBuildReport thrown = model.CaptureParticipant("fs");
            Assert.True(SideFileTestKit.AnyContains(thrown.Warnings, "Write threw"), "the exception becomes a warning");
            SideFileTestKit.AssertJson(SideFileTestKit.Parse("{\"_schema\":1,\"v\":5}"), SideFileTestKit.Features(model.Build(null))["fs"]);

            b.Value = 8;
            model.CaptureParticipant("gs");
            Assert.True(SideFileTestKit.Features(model.Build(null)).ContainsKey("gs"));
            b.ReturnNull = true;
            model.CaptureParticipant("gs");
            Assert.False(SideFileTestKit.Features(model.Build(null)).ContainsKey("gs"), "no data: the section is left out");
        }

        [Test]
        public void IsActiveThatThrows_MeansInactive_SoNoDataIsLost()
        {
            var a = new Part("fs");
            var model = new SideFileModel(id => { throw new InvalidOperationException("boom"); });
            SideFileTestKit.Register(model, a);

            SideFileReadReport report = model.Apply(SideFileTestKit.File1("\"fs\":{\"_schema\":1,\"v\":3}"));
            JsonObject features = SideFileTestKit.Features(model.Build(null));

            Assert.Equal(SideFileStatus.Loaded, report.Status);
            Assert.Equal(0, a.ReadCount + a.WriteCount);
            SideFileTestKit.AssertJson(SideFileTestKit.Parse("{\"_schema\":1,\"v\":3}"), features["fs"]);
        }

        [Test]
        public void ParticipantRegisteredAfterTheLoad_DoesNotOverwriteItsSectionWithDefaults()
        {
            var model = new SideFileModel();
            model.Apply(SideFileTestKit.File1("\"late\":{\"_schema\":1,\"v\":3}"));
            var late = new Part("late");
            SideFileTestKit.Register(model, late);

            JsonObject features = SideFileTestKit.Features(model.Build(null));

            Assert.Equal(0, late.WriteCount, "it never got its data, so its (default) state must not replace the stored one");
            SideFileTestKit.AssertJson(SideFileTestKit.Parse("{\"_schema\":1,\"v\":3}"), features["late"]);

            model.RestoreParticipant("late");
            Assert.Equal(3, late.Value);
            late.Value = 4;
            SideFileTestKit.AssertJson(SideFileTestKit.Parse("{\"_schema\":1,\"v\":4}"), SideFileTestKit.Features(model.Build(null))["late"]);
        }

        [Test]
        public void ParticipantRegisteredAfterTheLoadWithNothingStored_IsSimplyWritten()
        {
            var model = new SideFileModel();
            model.Apply(SideFileTestKit.File1(string.Empty));
            var late = new Part("late");
            SideFileTestKit.Register(model, late);

            JsonObject features = SideFileTestKit.Features(model.Build(null));

            Assert.Equal(1, late.WriteCount);
            Assert.True(features.ContainsKey("late"));
        }

        [Test]
        public void RestoreParticipant_UnknownId_IsReported()
        {
            var model = SideFileTestKit.Model(new Part("fs"));

            SideFileReadReport report = model.RestoreParticipant("nobody");
            SideFileReadReport nullReport = model.RestoreParticipant(null);

            Assert.True(SideFileTestKit.AnyContains(report.Warnings, "features.nobody"));
            Assert.Equal(1, nullReport.Warnings.Count);
        }

        // ---- newer sections and files (AC-5.6) ----

        [Test]
        public void Apply_SectionNewerThanTheParticipant_GivesDefaultsAndKeepsTheSection()
        {
            var a = new Part("event-system", 2);
            SideFileModel model = SideFileTestKit.Model(a);
            string stored = "{\"_schema\":3,\"v\":7,\"extra\":[1,{\"x\":null}]}";

            SideFileReadReport report = model.Apply(SideFileTestKit.File1("\"event-system\":" + stored));

            Assert.Equal(SideFileStatus.Newer, report.Status);
            Assert.Equal(SideFileStatus.Newer, model.Status);
            Assert.Equal(new[] { "event-system" }, report.NewerSections.ToArray());
            Assert.Equal(1, a.ResetCount);
            Assert.Equal(0, a.ReadCount);
            Assert.Equal(-1, a.Value);
            Assert.True(SideFileTestKit.AnyContains(report.Warnings, "features.event-system._schema: version 3 is newer"));

            a.Value = 4;                                  // the player goes on playing with defaults
            SideFileBuildReport build = model.Build(null);

            SideFileTestKit.AssertJson(SideFileTestKit.Parse(stored), SideFileTestKit.Features(build)["event-system"]);
            Assert.Equal(0, a.WriteCount, "the section is not rewritten with data the feature could not read");

            // and it stays that way across another save/load cycle
            SideFileReadReport again = model.Apply(build.Document.ToString());
            Assert.Equal(SideFileStatus.Newer, again.Status);
            SideFileTestKit.AssertJson(SideFileTestKit.Parse(stored), SideFileTestKit.Features(model.Build(null))["event-system"]);
        }

        [Test]
        public void Apply_NewerSectionOfOneParticipant_DoesNotAffectTheOthers()
        {
            var old = new Part("old-feature", 1);
            var fine = new Part("fine");
            SideFileModel model = SideFileTestKit.Model(old, fine);

            SideFileReadReport report = model.Apply(SideFileTestKit.File1("\"old-feature\":{\"_schema\":4,\"v\":1},\"fine\":{\"_schema\":1,\"v\":6}"));

            Assert.Equal(SideFileStatus.Newer, report.Status);
            Assert.Equal(6, fine.Value);
            Assert.Equal(1, fine.ReadCount);
        }

        [Test]
        public void Apply_FileWithANewerFormat_IsNotInterpretedAndIsWrittenBackAsItWas()
        {
            var a = new Part("fs");
            SideFileModel model = SideFileTestKit.Model(a);
            string text = "{\"schema\":2,\"modVersion\":\"9.9.9\",\"weird\":true,\"features\":{\"fs\":{\"v\":1},\"new-thing\":[1,2]}}";

            SideFileReadReport report = model.Apply(text);
            SideFileBuildReport build = model.Build(new SideFileHeader { modVersion = "0.1.0" });

            Assert.Equal(SideFileStatus.Newer, report.Status);
            Assert.True(report.FileIsNewer);
            Assert.Equal(1, a.ResetCount);
            Assert.Equal(0, a.ReadCount);
            Assert.True(SideFileTestKit.AnyContains(report.Warnings, "schema:"));
            SideFileTestKit.AssertJson(SideFileTestKit.Parse(text), build.Document);
            Assert.Equal(0, a.WriteCount);
            Assert.Equal(2, model.Header.schema);
        }

        // ---- the version field (AC-5.5) ----

        // AC-5.5
        [Test]
        public void Apply_SectionWithoutSchema_IsVersion1WithOneWarningPerSession_AndGainsTheFieldOnWrite()
        {
            var a = new Part("event-system");
            SideFileModel model = SideFileTestKit.Model(a);
            string text = SideFileTestKit.File1("\"event-system\":{\"v\":6}");

            SideFileReadReport first = model.Apply(text);

            Assert.Equal(SideFileStatus.Loaded, first.Status);
            Assert.Equal(1, a.LastSchema, "no field means version 1");
            Assert.Equal(6, a.Value);
            Assert.Equal(1, first.Warnings.Count);
            Assert.True(first.Warnings[0].StartsWith("features.event-system._schema:", StringComparison.Ordinal), first.Warnings[0]);

            SideFileReadReport second = model.Apply(text);       // loading the same save again in the same session
            Assert.Equal(0, second.Warnings.Count, "the warning is given once per session");
            Assert.Equal(1, a.LastSchema);

            JsonObject section = SideFileTestKit.Features(model.Build(null)).GetObject("event-system");
            Assert.Equal(new[] { "_schema", "v" }, section.Keys.ToArray());
            Assert.Equal(1, section.GetInt32("_schema"));
        }

        [Test]
        public void Apply_SchemaThatIsNotAVersion_KeepsTheSectionAndGivesDefaults()
        {
            var a = new Part("fs");
            SideFileModel model = SideFileTestKit.Model(a);
            string stored = "{\"_schema\":\"two\",\"v\":1}";

            SideFileReadReport report = model.Apply(SideFileTestKit.File1("\"fs\":" + stored));

            Assert.Equal(1, a.ResetCount);
            Assert.Equal(0, a.ReadCount);
            Assert.True(SideFileTestKit.AnyContains(report.Warnings, "features.fs._schema"));
            SideFileTestKit.AssertJson(SideFileTestKit.Parse(stored), SideFileTestKit.Features(model.Build(null))["fs"]);

            foreach (string bad in new[] { "0", "-1", "1.5", "null", "true", "[1]" })
            {
                var p = new Part("fs");
                SideFileModel m = SideFileTestKit.Model(p);
                m.Apply(SideFileTestKit.File1("\"fs\":{\"_schema\":" + bad + ",\"v\":1}"));
                Assert.Equal(0, p.ReadCount, "_schema = " + bad);
                Assert.Equal(1, p.ResetCount, "_schema = " + bad);
            }
        }

        [Test]
        public void Build_WritesTheSchemaVersionOfEveryParticipantFirst()
        {
            var a = new Part("a", 1);
            var b = new Part("b", 2);
            var c = new Part("c", 5);
            SideFileModel model = SideFileTestKit.Model(a, b, c);
            model.Apply(SideFileTestKit.File1(string.Empty));

            JsonObject features = SideFileTestKit.Features(model.Build(null));

            Assert.Equal(1, features.GetObject("a").GetInt32("_schema"));
            Assert.Equal(2, features.GetObject("b").GetInt32("_schema"));
            Assert.Equal(5, features.GetObject("c").GetInt32("_schema"));
            Assert.Equal("_schema", features.GetObject("c").Keys.First());
        }

        [Test]
        public void Build_WriteThatReturnsSchema_IsOverwrittenWithAWarning()
        {
            var a = new Part("fs", 2);
            a.Extra = new JsonObject().Set("_schema", 99).Set("v", 1);
            SideFileModel model = SideFileTestKit.Model(a);
            model.Apply(SideFileTestKit.File1(string.Empty));

            SideFileBuildReport build = model.Build(null);
            JsonObject section = SideFileTestKit.Features(build).GetObject("fs");

            Assert.Equal(2, section.GetInt32("_schema"), "the core owns the version");
            Assert.Equal(1, section.GetInt32("v"));
            Assert.Equal(new[] { "_schema", "v" }, section.Keys.ToArray());
            Assert.Equal(1, build.Warnings.Count);
            Assert.True(build.Warnings[0].StartsWith("features.fs._schema:", StringComparison.Ordinal), build.Warnings[0]);
            Assert.Equal(99, a.Extra.GetInt32("_schema"), "the participant's own object is not modified");
        }

        // ---- damage (AC-5.4 on the model level) ----

        [Test]
        public void Apply_InvalidFiles_AreCorruptAndEverybodyGetsDefaults()
        {
            string[] bad =
            {
                string.Empty,
                "{",
                "{\"schema\":1,\"features\":{\"fs\":",
                "[]",
                "null",
                "\"text\"",
                "{\"features\":{}}",
                "{\"schema\":0,\"features\":{}}",
                "{\"schema\":-3}",
                "{\"schema\":\"1\"}",
                "{\"schema\":1.5}",
                "{\"schema\":1,\"features\":5}",
                "{\"schema\":1,\"features\":[]}",
                "{\"schema\":1,\"schema\":1}",
            };

            foreach (string text in bad)
            {
                var a = new Part("fs");
                a.Value = 5;
                SideFileModel model = SideFileTestKit.Model(a);

                SideFileReadReport report = model.Apply(text);

                Assert.Equal(SideFileStatus.Corrupt, report.Status, text);
                Assert.Equal(SideFileStatus.Corrupt, model.Status, text);
                Assert.False(string.IsNullOrEmpty(report.Error), text);
                Assert.Equal(1, a.ResetCount, text);
                Assert.Equal(-1, a.Value, text);
                Assert.Null(model.Header, text);
            }
        }

        [Test]
        public void Apply_BrokenJson_ReportsLineAndColumn()
        {
            SideFileModel model = SideFileTestKit.Model(new Part("fs"));

            SideFileReadReport report = model.Apply("{\n  \"schema\": 1,\n  \"features\": {\n");

            Assert.Equal(SideFileStatus.Corrupt, report.Status);
            Assert.True(report.Error.Contains("line 4"), report.Error);
        }

        [Test]
        public void Apply_AfterACorruptFile_NothingOfTheOldLoadIsWrittenBack()
        {
            SideFileModel model = SideFileTestKit.Model(new Part("fs"));
            model.Apply(SideFileTestKit.File1("\"gpu-product-line\":" + SideFileTestKit.Gpu));

            model.Apply("{");
            JsonObject features = SideFileTestKit.Features(model.Build(null));

            Assert.False(features.ContainsKey("gpu-product-line"), "the passthrough sections belong to the file that was loaded last, and it was rejected");
        }

        // ---- missing file, new game ----

        [Test]
        public void ApplyMissing_ResetsEverybody()
        {
            var a = new Part("fs");
            a.Value = 5;
            SideFileModel model = SideFileTestKit.Model(a);

            SideFileReadReport report = model.ApplyMissing();

            Assert.Equal(SideFileStatus.Missing, report.Status);
            Assert.Equal(SideFileStatus.Missing, model.Status);
            Assert.Equal(1, a.ResetCount);
            Assert.Equal(-1, a.Value);
            Assert.Null(model.Header);
            Assert.Equal(0, report.Warnings.Count);
        }

        [Test]
        public void StartNewGame_ResetsEverybodyAndForgetsTheOldFile()
        {
            var a = new Part("fs");
            var b = new Part("off");
            bool offActive = false;
            var model = new SideFileModel(id => id != "off" || offActive);
            SideFileTestKit.Register(model, a, b);
            model.Apply(SideFileTestKit.File1("\"fs\":{\"_schema\":1,\"v\":3},\"off\":{\"_schema\":1,\"v\":4},\"gpu-product-line\":" + SideFileTestKit.Gpu));
            a.ResetCount = 0;

            SideFileReadReport report = model.StartNewGame();

            Assert.Equal(SideFileStatus.NewGame, report.Status);
            Assert.Equal(SideFileStatus.NewGame, model.Status);
            Assert.Equal(1, a.ResetCount);
            Assert.Equal(1, b.ResetCount, "everybody is reset, a switched-off feature too");
            Assert.Equal(0, model.RetainedSections.Count);
            Assert.Null(model.Header);

            JsonObject features = SideFileTestKit.Features(model.Build(null));
            Assert.False(features.ContainsKey("gpu-product-line"), "a new save starts clean");
            Assert.False(features.ContainsKey("off"));
            Assert.True(features.ContainsKey("fs"));
        }

        [Test]
        public void Status_StartsAsNone()
        {
            Assert.Equal(SideFileStatus.None, new SideFileModel().Status);
        }

        // ---- misbehaving participants ----

        [Test]
        public void ReadThatThrows_GivesDefaultsAWarning_AndKeepsTheSectionAsRead()
        {
            var a = new Part("fs");
            a.ThrowOnRead = true;
            SideFileModel model = SideFileTestKit.Model(a);
            string stored = "{\"_schema\":1,\"v\":3,\"keep\":[1,2]}";

            SideFileReadReport report = model.Apply(SideFileTestKit.File1("\"fs\":" + stored));
            a.ThrowOnRead = false;
            SideFileBuildReport build = model.Build(null);

            Assert.Equal(SideFileStatus.Loaded, report.Status);
            Assert.True(SideFileTestKit.AnyContains(report.Warnings, "features.fs: Read threw InvalidOperationException: read boom"));
            Assert.Equal(1, a.ResetCount);
            SideFileTestKit.AssertJson(SideFileTestKit.Parse(stored), SideFileTestKit.Features(build)["fs"]);
            Assert.Equal(0, a.WriteCount, "the unreadable section is not replaced by defaults");
        }

        [Test]
        public void ResetToDefaultsThatThrows_IsOnlyAWarning()
        {
            var a = new Part("fs");
            a.ThrowOnReset = true;
            SideFileModel model = SideFileTestKit.Model(a);

            SideFileReadReport report = model.Apply(SideFileTestKit.File1(string.Empty));
            SideFileReadReport newGame = model.StartNewGame();
            SideFileReadReport missing = model.ApplyMissing();
            SideFileReadReport corrupt = model.ApplyCorrupt("because");

            Assert.True(SideFileTestKit.AnyContains(report.Warnings, "ResetToDefaults threw"));
            Assert.True(SideFileTestKit.AnyContains(newGame.Warnings, "ResetToDefaults threw"));
            Assert.True(SideFileTestKit.AnyContains(missing.Warnings, "ResetToDefaults threw"));
            Assert.True(SideFileTestKit.AnyContains(corrupt.Warnings, "ResetToDefaults threw"));
            Assert.Equal("because", corrupt.Error);
        }

        [Test]
        public void WriteThatThrows_KeepsThePreviousSectionOrLeavesItOut()
        {
            var a = new Part("fs");
            SideFileModel model = SideFileTestKit.Model(a);
            model.Apply(SideFileTestKit.File1("\"fs\":{\"_schema\":1,\"v\":3}"));

            a.ThrowOnWrite = true;
            SideFileBuildReport first = model.Build(null);
            Assert.False(SideFileTestKit.Features(first).ContainsKey("fs"), "read fine, so nothing is retained; there is nothing to fall back to");
            Assert.True(SideFileTestKit.AnyContains(first.Warnings, "features.fs: Write threw"));

            a.ThrowOnWrite = false;
            a.Value = 6;
            model.Build(null);                       // a good write: now there is a last known state
            a.ThrowOnWrite = true;
            SideFileBuildReport second = model.Build(null);

            SideFileTestKit.AssertJson(SideFileTestKit.Parse("{\"_schema\":1,\"v\":6}"), SideFileTestKit.Features(second)["fs"]);
            Assert.Equal(1, second.Warnings.Count);
        }

        [Test]
        public void WriteThatReturnsNull_LeavesTheSectionOutAndForgetsTheOldOne()
        {
            var a = new Part("fs");
            SideFileModel model = SideFileTestKit.Model(a);
            model.Apply(SideFileTestKit.File1(string.Empty));
            model.Build(null);

            a.ReturnNull = true;
            JsonObject features = SideFileTestKit.Features(model.Build(null));

            Assert.False(features.ContainsKey("fs"));
            Assert.False(SideFileTestKit.Features(model.Build(null)).ContainsKey("fs"), "and it does not come back from the retained copy");
        }

        // ---- registration ----

        [Test]
        public void TryRegister_RejectsNullEmptyAndDuplicateIds()
        {
            var model = new SideFileModel();
            string problem;

            Assert.False(model.TryRegister(null, out problem));
            Assert.True(problem.Contains("null"));
            Assert.False(model.TryRegister(new Part(""), out problem));
            Assert.True(problem.Contains("empty"));
            Assert.False(model.TryRegister(new Part(null), out problem));

            Assert.True(model.TryRegister(new Part("fs"), out problem));
            Assert.Null(problem);
            Assert.False(model.TryRegister(new Part("fs"), out problem));
            Assert.True(problem.Contains("already registered"));
            Assert.Equal(1, model.ParticipantCount);
        }

        // ---- header ----

        [Test]
        public void Header_IsReadFromTheFile_AndNullBeforeAndAfterReset()
        {
            SideFileModel model = SideFileTestKit.Model(new Part("fs"));
            Assert.Null(model.Header);

            model.Apply(SideFileTestKit.File1(string.Empty));

            Assert.NotNull(model.Header);
            Assert.Equal(1, model.Header.schema);
            Assert.Equal("0.1.0", model.Header.modVersion);
            Assert.Equal("0.2.3", model.Header.gameVersion);
            Assert.Equal("1985_3_1", model.Header.saveName);
            Assert.Equal("1985-03-01", model.Header.gameDate);
            Assert.Equal("2026-10-10T18:42:07Z", model.Header.writtenUtc);

            model.ApplyMissing();
            Assert.Null(model.Header);
        }

        [Test]
        public void Header_OfAFileWithoutTheOptionalFields_IsStillFine()
        {
            SideFileModel model = SideFileTestKit.Model(new Part("fs"));

            SideFileReadReport report = model.Apply("{\"schema\":1,\"features\":{}}");

            Assert.Equal(SideFileStatus.Loaded, report.Status);
            Assert.Null(model.Header.modVersion);
            Assert.Null(model.Header.saveName);
        }

        [Test]
        public void Build_Header_SchemaIsAlwaysTheFormatVersion_AndNullFieldsAreLeftOut()
        {
            SideFileModel model = SideFileTestKit.Model(new Part("fs"));
            model.Apply(SideFileTestKit.File1(string.Empty));

            JsonObject document = model.Build(new SideFileHeader
            {
                schema = 77,
                modVersion = "0.1.0",
                gameVersion = null,
                saveName = "x",
                gameDate = "1985-03-01",
                writtenUtc = "2026-10-10T12:34:56Z",
            }).Document;

            Assert.Equal(new[] { "schema", "modVersion", "saveName", "gameDate", "writtenUtc", "features" }, document.Keys.ToArray());
            Assert.Equal(1, document.GetInt32("schema"));

            JsonObject bare = model.Build(null).Document;
            Assert.Equal(new[] { "schema", "features" }, bare.Keys.ToArray());
        }

        // ---- shapes of sections ----

        [Test]
        public void Apply_SectionThatIsNotAnObject_GivesDefaultsAndIsReplacedByTheParticipantsData()
        {
            var a = new Part("fs");
            SideFileModel model = SideFileTestKit.Model(a);

            SideFileReadReport report = model.Apply(SideFileTestKit.File1("\"fs\":5"));
            a.Value = 2;
            JsonObject features = SideFileTestKit.Features(model.Build(null));

            Assert.Equal(SideFileStatus.Loaded, report.Status);
            Assert.True(SideFileTestKit.AnyContains(report.Warnings, "features.fs: the section is not an object"));
            Assert.Equal(1, a.ResetCount);
            Assert.Equal(2, features.GetObject("fs").GetInt32("v"));
        }

        [Test]
        public void Apply_UnknownSectionThatIsNotAnObject_IsKeptToo()
        {
            SideFileModel model = SideFileTestKit.Model(new Part("fs"));
            model.Apply(SideFileTestKit.File1("\"mystery\":[1,2,3]"));

            JsonObject features = SideFileTestKit.Features(model.Build(null));

            SideFileTestKit.AssertJson(SideFileTestKit.ParseNode("[1,2,3]"), features["mystery"]);
        }

        [Test]
        public void ReadThatDamagesItsObjectBeforeThrowing_DoesNotDamageTheKeptSection()
        {
            // Read() receives a copy of the member list: removing members from it must not change the section that is kept for the next save.
            var a = new Part("fs");
            a.MutateInRead = true;
            a.ThrowOnRead = true;
            SideFileModel model = SideFileTestKit.Model(a);
            string stored = "{\"_schema\":1,\"v\":1,\"deep\":{\"x\":[1,2]}}";

            model.Apply(SideFileTestKit.File1("\"fs\":" + stored));

            SideFileTestKit.AssertJson(SideFileTestKit.Parse(stored), SideFileTestKit.Features(model.Build(null))["fs"]);
        }
    }

    /// <summary>
    /// The disk side of the side save file (FR-5, TSK-016) on a fake disk: path rules (nothing outside Saves/), the atomic write and its
    /// failures, the corrupt-file rename, the end-to-end acceptance scenarios AC-5.1 to AC-5.6 as far as they are decided by SideFileIo.
    /// One test runs on the real disk (a temporary folder) to check that the fake behaves like System.IO.
    /// </summary>
    [Covers(typeof(SideFileIo), typeof(SideFileWriteReport), typeof(IFileSystem), typeof(SystemFileSystem), typeof(AtomicFile), typeof(FileWriteResult))]
    public class SideFileIoTests
    {
        private static readonly SideFileHeader Header = new SideFileHeader { modVersion = "0.1.0", gameVersion = "0.2.3", gameDate = "1985-03-01" };

        private static string Side(string name)
        {
            return SideFileTestKit.SidePath(name);
        }

        // ---- writing ----

        // AC-5.1
        [Test]
        public void Write_CreatesTheSideFileNextToTheSaveAndLeavesTheSaveAlone()
        {
            FakeFileSystem fs = SideFileTestKit.NewFs();
            string save = SideFileTestKit.SavesDir + @"\1985_3_1.txt";
            fs.Seed(save, "{\"vanilla\":true}");
            var part = new Part("framework-sample");
            SideFileModel model = SideFileTestKit.Model(part);
            model.StartNewGame();
            part.Value = 3;

            SideFileWriteReport report = SideFileTestKit.NewIo(fs).Write(model, "1985_3_1", Header);

            Assert.True(report.Success, report.Error);
            Assert.Null(report.Error);
            Assert.Equal(Side("1985_3_1"), report.Path);
            JsonObject doc = SideFileTestKit.Parse(fs.Text(Side("1985_3_1")));
            Assert.Equal(1, doc.GetInt32("schema"));
            Assert.Equal("0.1.0", doc.GetString("modVersion"));
            Assert.Equal("0.2.3", doc.GetString("gameVersion"));
            Assert.Equal("1985_3_1", doc.GetString("saveName"));
            Assert.Equal("1985-03-01", doc.GetString("gameDate"));
            Assert.Equal("2026-10-10T12:34:56Z", doc.GetString("writtenUtc"));
            SideFileTestKit.AssertJson(SideFileTestKit.Parse("{\"_schema\":1,\"v\":3}"), doc.GetObject("features")["framework-sample"]);

            Assert.Equal("{\"vanilla\":true}", fs.Text(save), "the game's own save is not touched");
            Assert.False(fs.Log.Any(line => line.Contains(".txt")));
            Assert.False(fs.Exists(Side("1985_3_1") + ".tmp"));
        }

        [Test]
        public void Write_WithoutAHeader_StillWritesSchemaSaveNameAndTime_AndKeepsTheCallersHeaderIntact()
        {
            FakeFileSystem fs = SideFileTestKit.NewFs();
            SideFileModel model = SideFileTestKit.Model(new Part("fs"));
            model.StartNewGame();
            var header = new SideFileHeader { modVersion = "0.1.0", schema = 9, saveName = "other" };

            Assert.True(SideFileTestKit.NewIo(fs).Write(model, "autosave", null).Success);
            Assert.True(SideFileTestKit.NewIo(fs).Write(model, "named", header).Success);

            JsonObject auto = SideFileTestKit.Parse(fs.Text(Side("autosave")));
            Assert.Equal(new[] { "schema", "saveName", "writtenUtc", "features" }, auto.Keys.ToArray());
            Assert.Equal("autosave", auto.GetString("saveName"));
            JsonObject named = SideFileTestKit.Parse(fs.Text(Side("named")));
            Assert.Equal("named", named.GetString("saveName"), "the name of the file wins over the header");
            Assert.Equal(1, named.GetInt32("schema"));
            Assert.Equal(9, header.schema);
            Assert.Equal("other", header.saveName);
        }

        [Test]
        public void Write_IsIndentedForHumans()
        {
            FakeFileSystem fs = SideFileTestKit.NewFs();
            SideFileModel model = SideFileTestKit.Model(new Part("fs"));
            model.StartNewGame();

            SideFileTestKit.NewIo(fs).Write(model, "x", null);

            Assert.True(fs.Text(Side("x")).Contains("\n  \"schema\": 1"));
        }

        [Test]
        public void Write_FirstWriteIsAMove_LaterWritesReplaceWithoutBackup()
        {
            FakeFileSystem fs = SideFileTestKit.NewFs();
            SideFileModel model = SideFileTestKit.Model(new Part("fs"));
            model.StartNewGame();
            SideFileIo io = SideFileTestKit.NewIo(fs);

            io.Write(model, "x", null);
            Assert.True(fs.Log.Contains("Move " + Side("x") + ".tmp -> " + Side("x")));
            io.Write(model, "x", null);

            Assert.True(fs.Log.Contains("Replace " + Side("x") + ".tmp -> " + Side("x")), "no backup argument: the side file keeps no .bak");
            Assert.Equal(1, fs.CountLog("Replace"));
            Assert.Equal(0, fs.FilesContaining(".bak").Count);
        }

        [Test]
        public void Write_ReplaceFailure_KeepsThePreviousFileRemovesTheTempAndLeavesTheSaveAlone()
        {
            FakeFileSystem fs = SideFileTestKit.NewFs();
            var part = new Part("fs");
            SideFileModel model = SideFileTestKit.Model(part);
            model.StartNewGame();
            SideFileIo io = SideFileTestKit.NewIo(fs);
            string save = SideFileTestKit.SavesDir + @"\x.txt";
            fs.Seed(save, "vanilla save");
            part.Value = 1;
            Assert.True(io.Write(model, "x", Header).Success);
            string before = fs.Text(Side("x"));

            part.Value = 2;
            fs.FailOn(FsOp.Replace, "x.ptmod.json");
            SideFileWriteReport failed = io.Write(model, "x", Header);

            Assert.False(failed.Success);
            Assert.False(failed.PathRejected);
            Assert.True(failed.Error.Contains("injected failure"), failed.Error);
            Assert.Equal(before, fs.Text(Side("x")), "the previous side file is exactly as it was");
            Assert.False(fs.Exists(Side("x") + ".tmp"), "the temporary file is removed");
            Assert.Equal("vanilla save", fs.Text(save));

            Assert.True(io.Write(model, "x", Header).Success, "the next save works");
            Assert.Equal(2, SideFileTestKit.Parse(fs.Text(Side("x"))).GetObject("features").GetObject("fs").GetInt32("v"));
        }

        [Test]
        public void Write_TemporaryFileFailure_AndFirstMoveFailure_LeaveNothingBehind()
        {
            FakeFileSystem fs = SideFileTestKit.NewFs();
            SideFileModel model = SideFileTestKit.Model(new Part("fs"));
            model.StartNewGame();
            SideFileIo io = SideFileTestKit.NewIo(fs);

            fs.FailOn(FsOp.WriteAllText, ".tmp");
            Assert.False(io.Write(model, "x", null).Success);
            Assert.Equal(0, fs.FilesContaining("x.ptmod.json").Count);

            fs.FailOn(FsOp.Move, ".tmp");
            SideFileWriteReport moveFailed = io.Write(model, "x", null);
            Assert.False(moveFailed.Success);
            Assert.False(fs.Exists(Side("x")), "no half-written side file");
            Assert.False(fs.Exists(Side("x") + ".tmp"));
        }

        [Test]
        public void Write_DocumentTooBigToBeReadBack_IsRefused()
        {
            FakeFileSystem fs = SideFileTestKit.NewFs();
            var part = new Part("fs");
            SideFileModel model = SideFileTestKit.Model(part);
            model.StartNewGame();
            SideFileIo io = SideFileTestKit.NewIo(fs);
            Assert.True(io.Write(model, "x", null).Success);
            string before = fs.Text(Side("x"));

            part.Extra = new JsonObject().Set("blob", new string('x', ModJson.MaxBytes + 10));
            SideFileWriteReport report = io.Write(model, "x", null);

            Assert.False(report.Success);
            Assert.True(report.Error.Contains("larger than"), report.Error);
            Assert.Equal(before, fs.Text(Side("x")));
        }

        [Test]
        public void Write_CreatesAMissingSavesFolder()
        {
            var fs = new FakeFileSystem();
            SideFileModel model = SideFileTestKit.Model(new Part("fs"));
            model.StartNewGame();

            SideFileWriteReport report = SideFileTestKit.NewIo(fs).Write(model, "x", null);

            Assert.True(report.Success, report.Error);
            Assert.True(fs.Exists(Side("x")));
        }

        [Test]
        public void Write_ReportsTheWarningsOfTheModel()
        {
            FakeFileSystem fs = SideFileTestKit.NewFs();
            var part = new Part("fs");
            part.Extra = new JsonObject().Set("_schema", 5).Set("v", 1);
            SideFileModel model = SideFileTestKit.Model(part);
            model.StartNewGame();

            SideFileWriteReport report = SideFileTestKit.NewIo(fs).Write(model, "x", null);

            Assert.True(report.Success);
            Assert.Equal(1, report.Warnings.Count);
            Assert.True(report.Warnings[0].StartsWith("features.fs._schema:", StringComparison.Ordinal), report.Warnings[0]);
        }

        [Test]
        public void ReadAndWrite_RequireAModel()
        {
            SideFileIo io = SideFileTestKit.NewIo(SideFileTestKit.NewFs());

            Assert.Throws<ArgumentNullException>(() => io.Read(null, "x"));
            Assert.Throws<ArgumentNullException>(() => io.Write(null, "x", null));
            Assert.Throws<ArgumentNullException>(() => new SideFileIo(null, SideFileTestKit.SavesDir));
        }

        [Test]
        public void WriteAndRead_AMegabyteOfState_TakeWellUnderASecond()
        {
            // The budget in the design is 50 ms for 1 MB; the bound here is generous so that a slow machine does not fail it,
            // it only guards against accidental quadratic work.
            FakeFileSystem fs = SideFileTestKit.NewFs();
            var big = new JsonObject();
            for (int i = 0; i < 100; i++) big.Set("key" + i, new string((char)('a' + i % 26), 10000));
            var part = new Part("fs") { Extra = big };
            SideFileModel model = SideFileTestKit.Model(part);
            model.StartNewGame();
            SideFileIo io = SideFileTestKit.NewIo(fs);

            var watch = System.Diagnostics.Stopwatch.StartNew();
            SideFileWriteReport written = io.Write(model, "x", Header);
            long writeMs = watch.ElapsedMilliseconds;
            watch.Restart();
            SideFileReadReport read = io.Read(SideFileTestKit.Model(new Part("fs")), "x");
            long readMs = watch.ElapsedMilliseconds;

            Assert.True(written.Success);
            Assert.True(fs.Text(Side("x")).Length > 1000000);
            Assert.Equal(SideFileStatus.Loaded, read.Status);
            Assert.True(writeMs < 1000, "write took " + writeMs + " ms");
            Assert.True(readMs < 1000, "read took " + readMs + " ms");
        }

        // ---- the path rules (design: Security) ----

        [Test]
        public void TryGetPath_AcceptsTheNamesTheGameCanProduce()
        {
            SideFileIo io = SideFileTestKit.NewIo(SideFileTestKit.NewFs());
            string[] names = { "1985_3_1", "autosave", "Мой сейв", "name with spaces", "x.y", "sub/x", "sub\\x", "a/../b" };

            foreach (string name in names)
            {
                string path;
                string reason;
                Assert.True(io.TryGetPath(name, out path, out reason), name + ": " + reason);
                Assert.True(path.StartsWith(SideFileTestKit.SavesDir + "\\", StringComparison.OrdinalIgnoreCase), name);
                Assert.True(path.EndsWith(".ptmod.json", StringComparison.Ordinal), name);
                Assert.Null(reason);
            }

            string normalized;
            string unused;
            io.TryGetPath("a/../b", out normalized, out unused);
            Assert.Equal(Side("b"), normalized);
        }

        [Test]
        public void TryGetPath_RejectsEverythingThatLeavesSavesOrIsNotAFileName()
        {
            SideFileIo io = SideFileTestKit.NewIo(SideFileTestKit.NewFs());
            string[] names =
            {
                "../evil", "..\\evil", "a/../../evil", "C:\\evil", "D:evil", "bad|name", "q?x", "a*b", "a\"b", "a<b", "a>b",
                "x\0y", "tab\there", "", " ", "   ", null,
            };

            foreach (string name in names)
            {
                string path;
                string reason;
                Assert.False(io.TryGetPath(name, out path, out reason), "'" + name + "' must be rejected");
                Assert.Null(path);
                Assert.False(string.IsNullOrEmpty(reason), name);
            }
        }

        [Test]
        public void TryGetPath_NamesThatEscapeSavesAreToldApartFromBadCharacters()
        {
            SideFileIo io = SideFileTestKit.NewIo(SideFileTestKit.NewFs());
            string path;
            string reason;

            Assert.False(io.TryGetPath("../evil", out path, out reason));
            Assert.True(reason.Contains("leaves the Saves folder"), reason);
            Assert.False(io.TryGetPath("a|b", out path, out reason));
            Assert.True(reason.Contains("characters"), reason);
            Assert.False(io.TryGetPath("", out path, out reason));
            Assert.True(reason.Contains("empty"), reason);
        }

        [Test]
        public void Write_RejectedNames_WriteNothingAtAll()
        {
            FakeFileSystem fs = SideFileTestKit.NewFs();
            SideFileModel model = SideFileTestKit.Model(new Part("fs"));
            model.StartNewGame();
            SideFileIo io = SideFileTestKit.NewIo(fs);

            foreach (string name in new[] { "../evil", "..\\evil", "a/../../evil", "C:\\evil", "", null })
            {
                SideFileWriteReport report = io.Write(model, name, null);

                Assert.False(report.Success, name);
                Assert.True(report.PathRejected, name);
                Assert.Null(report.Path);
                Assert.True(report.Error.Contains("was not written"), report.Error);
            }

            Assert.Equal(0, fs.Log.Count, "not even a temporary file");
            Assert.Equal(0, fs.Files.Count());
        }

        [Test]
        public void Read_RejectedName_IsTreatedAsNoFileWithAWarning()
        {
            FakeFileSystem fs = SideFileTestKit.NewFs();
            var part = new Part("fs");
            part.Value = 5;
            SideFileModel model = SideFileTestKit.Model(part);
            fs.Seed(FakeFileSystem.Root + @"\evil.ptmod.json", SideFileTestKit.File1("\"fs\":{\"_schema\":1,\"v\":9}"));

            SideFileReadReport report = SideFileTestKit.NewIo(fs).Read(model, "../evil");

            Assert.Equal(SideFileStatus.Missing, report.Status);
            Assert.True(report.PathRejected);
            Assert.Null(report.Path);
            Assert.True(SideFileTestKit.AnyContains(report.Warnings, "leaves the Saves folder"));
            Assert.Equal(-1, part.Value, "the file outside Saves is not read");
        }

        [Test]
        public void SavesDirectory_IsNormalized_AndAnInvalidOneRejectsEveryName()
        {
            FakeFileSystem fs = SideFileTestKit.NewFs();

            var withSlash = new SideFileIo(fs, SideFileTestKit.SavesDir + "\\");
            Assert.Equal(SideFileTestKit.SavesDir, withSlash.SavesDirectory);
            string path;
            string reason;
            Assert.True(withSlash.TryGetPath("x", out path, out reason));
            Assert.Equal(Side("x"), path);

            var invalid = new SideFileIo(fs, string.Empty);
            Assert.Null(invalid.SavesDirectory);
            Assert.False(invalid.TryGetPath("x", out path, out reason));
            Assert.True(reason.Contains("Saves folder"), reason);
        }

        // ---- reading ----

        // AC-5.2
        [Test]
        public void Read_NoSideFile_IsMissingAndEverybodyHasDefaults()
        {
            FakeFileSystem fs = SideFileTestKit.NewFs();
            fs.Seed(SideFileTestKit.SavesDir + @"\old.txt", "{}");
            var part = new Part("fs");
            part.Value = 5;
            SideFileModel model = SideFileTestKit.Model(part);

            SideFileReadReport report = SideFileTestKit.NewIo(fs).Read(model, "old");

            Assert.Equal(SideFileStatus.Missing, report.Status);
            Assert.Equal(SideFileStatus.Missing, model.Status);
            Assert.Equal(Side("old"), report.Path);
            Assert.Null(report.QuarantinedTo);
            Assert.Equal(1, part.ResetCount);
            Assert.Equal(-1, part.Value);
            Assert.Equal(0, report.Warnings.Count);
            Assert.Equal(0, fs.Log.Count, "reading never writes");
        }

        [Test]
        public void Read_ValidFile_DistributesTheSections()
        {
            FakeFileSystem fs = SideFileTestKit.NewFs();
            fs.Seed(Side("x"), SideFileTestKit.File1("\"fs\":{\"_schema\":1,\"v\":7}"));
            var part = new Part("fs");
            SideFileModel model = SideFileTestKit.Model(part);

            SideFileReadReport report = SideFileTestKit.NewIo(fs).Read(model, "x");

            Assert.Equal(SideFileStatus.Loaded, report.Status);
            Assert.Equal(Side("x"), report.Path);
            Assert.False(report.PathRejected);
            Assert.Equal(7, part.Value);
        }

        [Test]
        public void Read_FileWithABom_IsAccepted()
        {
            FakeFileSystem fs = SideFileTestKit.NewFs();
            fs.Seed(Side("x"), "\uFEFF" + SideFileTestKit.File1("\"fs\":{\"_schema\":1,\"v\":7}"));
            var part = new Part("fs");

            SideFileReadReport report = SideFileTestKit.NewIo(fs).Read(SideFileTestKit.Model(part), "x");

            Assert.Equal(SideFileStatus.Loaded, report.Status);
            Assert.Equal(7, part.Value);
        }

        // AC-5.4
        [Test]
        public void Read_TruncatedFile_IsSetAsideWithATimestampAndEverybodyGetsDefaults()
        {
            FakeFileSystem fs = SideFileTestKit.NewFs();
            var part = new Part("fs");
            SideFileModel model = SideFileTestKit.Model(part);
            model.StartNewGame();
            part.Value = 4;
            SideFileIo io = SideFileTestKit.NewIo(fs);
            io.Write(model, "x", Header);
            fs.Truncate(Side("x"), 60);
            string broken = fs.Text(Side("x"));
            part.Value = 99;

            SideFileReadReport report = io.Read(model, "x");

            Assert.Equal(SideFileStatus.Corrupt, report.Status);
            Assert.Equal(SideFileStatus.Corrupt, model.Status);
            Assert.Equal(Side("x") + ".corrupt-20261010123456", report.QuarantinedTo);
            Assert.False(fs.Exists(Side("x")), "the broken file is out of the way");
            Assert.Equal(broken, fs.Text(report.QuarantinedTo), "and kept for the owner");
            Assert.True(report.Error.Contains("not valid JSON") && report.Error.Contains("line"), report.Error);
            Assert.Equal(-1, part.Value, "defaults");

            Assert.True(io.Write(model, "x", Header).Success, "the next save starts a fresh file");
            Assert.True(fs.Exists(Side("x")));
        }

        [Test]
        public void Read_SecondCorruptFileInTheSameSecond_GetsADistinctName()
        {
            FakeFileSystem fs = SideFileTestKit.NewFs();
            SideFileIo io = SideFileTestKit.NewIo(fs);
            SideFileModel model = SideFileTestKit.Model(new Part("fs"));
            fs.Seed(Side("x"), "{");
            string first = io.Read(model, "x").QuarantinedTo;
            fs.Seed(Side("x"), "{{");

            string second = io.Read(model, "x").QuarantinedTo;

            Assert.Equal(Side("x") + ".corrupt-20261010123456", first);
            Assert.Equal(Side("x") + ".corrupt-20261010123456-2", second);
            Assert.Equal(2, fs.FilesContaining(".corrupt-").Count);
        }

        [Test]
        public void Read_UnreadableFile_IsTreatedAsCorrupt()
        {
            FakeFileSystem fs = SideFileTestKit.NewFs();
            fs.Seed(Side("x"), SideFileTestKit.File1(string.Empty));
            fs.FailOn(FsOp.ReadAllText, "x.ptmod.json");
            var part = new Part("fs");

            SideFileReadReport report = SideFileTestKit.NewIo(fs).Read(SideFileTestKit.Model(part), "x");

            Assert.Equal(SideFileStatus.Corrupt, report.Status);
            Assert.True(report.Error.Contains("cannot be read"), report.Error);
            Assert.NotNull(report.QuarantinedTo);
            Assert.Equal(1, part.ResetCount);
        }

        [Test]
        public void Read_BrokenFileThatCannotBeSetAside_StillGivesDefaultsAndAWarning()
        {
            FakeFileSystem fs = SideFileTestKit.NewFs();
            fs.Seed(Side("x"), "{");
            fs.FailOn(FsOp.Move, ".corrupt-");
            var part = new Part("fs");
            part.Value = 5;

            SideFileReadReport report = SideFileTestKit.NewIo(fs).Read(SideFileTestKit.Model(part), "x");

            Assert.Equal(SideFileStatus.Corrupt, report.Status);
            Assert.Null(report.QuarantinedTo);
            Assert.True(SideFileTestKit.AnyContains(report.Warnings, "could not be set aside"));
            Assert.Equal(-1, part.Value);
        }

        // ---- the acceptance scenarios end to end ----

        // AC-5.3
        [Test]
        public void LoadAndResave_SectionOfARemovedFeature_IsIdenticalToTheOriginal()
        {
            FakeFileSystem fs = SideFileTestKit.NewFs();
            fs.Seed(Side("1985_3_1"), SideFileTestKit.File1("\"fs\":{\"_schema\":1,\"v\":3},\"gpu-product-line\":" + SideFileTestKit.Gpu));
            var part = new Part("fs");
            SideFileModel model = SideFileTestKit.Model(part);
            SideFileIo io = SideFileTestKit.NewIo(fs);

            io.Read(model, "1985_3_1");
            part.Value = 10;
            SideFileWriteReport saved = io.Write(model, "1985_3_1", Header);

            Assert.True(saved.Success);
            JsonObject features = SideFileTestKit.Parse(fs.Text(Side("1985_3_1"))).GetObject("features");
            SideFileTestKit.AssertJson(SideFileTestKit.Parse(SideFileTestKit.Gpu), features["gpu-product-line"]);
            Assert.Equal(10, features.GetObject("fs").GetInt32("v"));
            Assert.True(fs.Text(Side("1985_3_1")).Contains("9007199254740993"));

            // saved under another name (Save As), still carried along
            io.Write(model, "copy", Header);
            SideFileTestKit.AssertJson(SideFileTestKit.Parse(SideFileTestKit.Gpu),
                SideFileTestKit.Parse(fs.Text(Side("copy"))).GetObject("features")["gpu-product-line"]);
        }

        // AC-5.5
        [Test]
        public void LoadAndResave_SectionWithoutSchema_ReadsAsVersion1WithOneWarningAndGainsTheField()
        {
            FakeFileSystem fs = SideFileTestKit.NewFs();
            fs.Seed(Side("x"), SideFileTestKit.File1("\"event-system\":{\"v\":6}"));
            var part = new Part("event-system");
            SideFileModel model = SideFileTestKit.Model(part);
            SideFileIo io = SideFileTestKit.NewIo(fs);

            SideFileReadReport first = io.Read(model, "x");
            SideFileReadReport second = io.Read(model, "x");
            io.Write(model, "x", Header);

            Assert.Equal(1, part.LastSchema);
            Assert.Equal(1, first.Warnings.Count);
            Assert.Equal(0, second.Warnings.Count);
            Assert.Equal(1, SideFileTestKit.Parse(fs.Text(Side("x"))).GetObject("features").GetObject("event-system").GetInt32("_schema"));
        }

        // AC-5.6
        [Test]
        public void LoadAndResave_SectionNewerThanTheParticipant_IsKeptWhole()
        {
            FakeFileSystem fs = SideFileTestKit.NewFs();
            string stored = "{\"_schema\":3,\"v\":7,\"extra\":[1,2,3]}";
            fs.Seed(Side("x"), SideFileTestKit.File1("\"event-system\":" + stored));
            var part = new Part("event-system", 2);
            SideFileModel model = SideFileTestKit.Model(part);
            SideFileIo io = SideFileTestKit.NewIo(fs);

            SideFileReadReport report = io.Read(model, "x");
            part.Value = 1;
            io.Write(model, "x", Header);

            Assert.Equal(SideFileStatus.Newer, report.Status);
            Assert.Equal(1, part.ResetCount);
            Assert.Equal(0, part.ReadCount);
            SideFileTestKit.AssertJson(SideFileTestKit.Parse(stored), SideFileTestKit.Parse(fs.Text(Side("x"))).GetObject("features")["event-system"]);
        }

        [Test]
        public void SaveLoadSave_RoundTripKeepsEveryParticipantsData()
        {
            FakeFileSystem fs = SideFileTestKit.NewFs();
            var a = new Part("a", 2);
            var b = new Part("b");
            SideFileModel model = SideFileTestKit.Model(a, b);
            SideFileIo io = SideFileTestKit.NewIo(fs);
            model.StartNewGame();
            a.Value = 11;
            b.Value = 22;
            io.Write(model, "x", Header);

            var a2 = new Part("a", 2);
            var b2 = new Part("b");
            SideFileModel loaded = SideFileTestKit.Model(a2, b2);
            SideFileReadReport report = io.Read(loaded, "x");

            Assert.Equal(SideFileStatus.Loaded, report.Status);
            Assert.Equal(0, report.Warnings.Count);
            Assert.Equal(11, a2.Value);
            Assert.Equal(22, b2.Value);
            Assert.Equal(2, a2.LastSchema);
            Assert.Equal("0.1.0", loaded.Header.modVersion);

            string firstText = fs.Text(Side("x"));
            io.Write(loaded, "y", Header);
            JsonObject again = SideFileTestKit.Parse(fs.Text(Side("y")));
            SideFileTestKit.AssertJson(SideFileTestKit.Parse(firstText).GetObject("features"), again.GetObject("features"));
        }

        // ---- deleting ----

        [Test]
        public void Delete_RemovesTheSideFileAndATempLeftover_ButNotTheSaveOrTheCorruptCopies()
        {
            FakeFileSystem fs = SideFileTestKit.NewFs();
            fs.Seed(Side("x"), "{}");
            fs.Seed(Side("x") + ".tmp", "partial");
            fs.Seed(Side("x") + ".corrupt-20260101000000", "old damage");
            fs.Seed(SideFileTestKit.SavesDir + @"\x.txt", "save");
            fs.Seed(Side("other"), "{}");
            string error;

            bool deleted = SideFileTestKit.NewIo(fs).Delete("x", out error);

            Assert.True(deleted);
            Assert.Null(error);
            Assert.False(fs.Exists(Side("x")));
            Assert.False(fs.Exists(Side("x") + ".tmp"));
            Assert.True(fs.Exists(Side("x") + ".corrupt-20260101000000"));
            Assert.True(fs.Exists(SideFileTestKit.SavesDir + @"\x.txt"), "deleting the save itself is the game's job");
            Assert.True(fs.Exists(Side("other")));
        }

        [Test]
        public void Delete_NoSuchFileRejectedNameAndFailure_AreReported()
        {
            FakeFileSystem fs = SideFileTestKit.NewFs();
            SideFileIo io = SideFileTestKit.NewIo(fs);
            string error;

            Assert.False(io.Delete("none", out error));
            Assert.Null(error, "nothing to delete is not an error");

            Assert.False(io.Delete("../evil", out error));
            Assert.True(error.Contains("leaves the Saves folder"), error);

            fs.Seed(Side("x"), "{}");
            fs.FailOn(FsOp.Delete, "x.ptmod.json");
            Assert.False(io.Delete("x", out error));
            Assert.True(error.Contains("injected failure"), error);
            Assert.True(fs.Exists(Side("x")));
        }

        // ---- AtomicFile ----

        [Test]
        public void AtomicFile_WriteText_KeepsTheOldContentAsBackupWhenAsked()
        {
            FakeFileSystem fs = SideFileTestKit.NewFs();
            string path = SideFileTestKit.SavesDir + @"\f.json";
            fs.Seed(path, "old");

            FileWriteResult result = AtomicFile.WriteText(fs, path, "new", path + ".bak");

            Assert.True(result.Success);
            Assert.Null(result.Error);
            Assert.Equal(path, result.Path);
            Assert.Equal("new", fs.Text(path));
            Assert.Equal("old", fs.Text(path + ".bak"));
            Assert.False(fs.Exists(path + ".tmp"));
        }

        [Test]
        public void AtomicFile_BadArguments_FailWithoutThrowing()
        {
            FakeFileSystem fs = SideFileTestKit.NewFs();

            Assert.False(AtomicFile.WriteText(null, "x", "t", null).Success);
            Assert.False(AtomicFile.WriteText(fs, "", "t", null).Success);
            Assert.False(AtomicFile.WriteText(fs, null, "t", null).Success);
            Assert.False(AtomicFile.WriteText(fs, SideFileTestKit.SavesDir + @"\f.json", null, null).Success);
            Assert.False(AtomicFile.WriteJson(fs, SideFileTestKit.SavesDir + @"\g.json", MakeTooDeep(), null).Success, "a tree deeper than the JSON limit");
            Assert.Equal(0, fs.Files.Count());
        }

        private static JsonNode MakeTooDeep()
        {
            JsonObject root = new JsonObject();
            JsonObject current = root;
            for (int i = 0; i < ModJson.MaxDepth + 5; i++)
            {
                var child = new JsonObject();
                current.Set("c", child);
                current = child;
            }
            return root;
        }

        [Test]
        public void AtomicFile_Quarantine_NumbersCollisionsAndReportsAMissingFile()
        {
            FakeFileSystem fs = SideFileTestKit.NewFs();
            string path = SideFileTestKit.SavesDir + @"\f.json";
            DateTime stamp = new DateTime(2026, 1, 2, 3, 4, 5);
            string error;

            fs.Seed(path, "1");
            Assert.Equal(path + ".corrupt-20260102030405", AtomicFile.Quarantine(fs, path, stamp, out error));
            fs.Seed(path, "2");
            Assert.Equal(path + ".corrupt-20260102030405-2", AtomicFile.Quarantine(fs, path, stamp, out error));
            fs.Seed(path, "3");
            Assert.Equal(path + ".corrupt-20260102030405-3", AtomicFile.Quarantine(fs, path, stamp, out error));
            Assert.Null(error);

            Assert.Null(AtomicFile.Quarantine(fs, path, stamp, out error), "there is nothing left to move");
            Assert.True(error.Contains("FileNotFoundException"), error);
        }

        [Test]
        public void AtomicFile_TryDelete_ReportsFailure_AndFileWriteResultDescribesItself()
        {
            FakeFileSystem fs = SideFileTestKit.NewFs();
            fs.Seed(SideFileTestKit.SavesDir + @"\f", "x");

            fs.FailOn(FsOp.Delete);
            Assert.False(AtomicFile.TryDelete(fs, SideFileTestKit.SavesDir + @"\f"));
            Assert.True(AtomicFile.TryDelete(fs, SideFileTestKit.SavesDir + @"\f"));
            Assert.True(AtomicFile.TryDelete(fs, SideFileTestKit.SavesDir + @"\missing"));

            Assert.True(FileWriteResult.Ok("p").ToString().Contains("written p"));
            FileWriteResult failed = FileWriteResult.Fail("p", null);
            Assert.Equal("unknown error", failed.Error);
            Assert.True(failed.ToString().Contains("not written p"));
            Assert.Equal("IOException: m", AtomicFile.Describe(new IOException("m")));
        }

        // ---- the real disk ----

        [Test]
        public void RealDisk_SystemFileSystemAndSideFileIoWorkLikeTheFake()
        {
            string root = Path.Combine(Path.GetTempPath(), "ptmod-tests-" + Guid.NewGuid().ToString("N"));
            string saves = Path.Combine(root, "Saves");
            try
            {
                var disk = new SystemFileSystem();
                var io = new SideFileIo(disk, saves, () => SideFileTestKit.Now);
                var part = new Part("fs");
                part.Extra = new JsonObject().Set("text", "рецессия, 价格, \U0001F600").Set("v", 1);
                SideFileModel model = SideFileTestKit.Model(part);
                model.StartNewGame();

                Assert.True(io.Write(model, "x", Header).Success, "first write: the Saves folder is created, then Move");
                part.Extra.Set("v", 2);
                Assert.True(io.Write(model, "x", Header).Success, "second write: Replace");

                string path = Path.Combine(saves, "x.ptmod.json");
                Assert.True(File.Exists(path));
                Assert.False(File.Exists(path + ".tmp"));
                Assert.Equal(0, Directory.GetFiles(saves, "*.bak").Length);
                byte[] bytes = File.ReadAllBytes(path);
                Assert.False(bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF, "no byte order mark is written");

                var readPart = new Part("fs");
                SideFileModel readModel = SideFileTestKit.Model(readPart);
                SideFileReadReport report = io.Read(readModel, "x");
                Assert.Equal(SideFileStatus.Loaded, report.Status);
                Assert.Equal(2, readPart.Value);
                Assert.Equal("рецессия, 价格, \U0001F600", readPart.LastRead.GetString("text"));

                File.WriteAllText(path, File.ReadAllText(path), new System.Text.UTF8Encoding(true));   // rewritten with a byte order mark
                Assert.Equal(SideFileStatus.Loaded, io.Read(readModel, "x").Status);

                File.WriteAllText(path, "{\"schema\":");
                SideFileReadReport corrupt = io.Read(readModel, "x");
                Assert.Equal(SideFileStatus.Corrupt, corrupt.Status);
                Assert.True(File.Exists(corrupt.QuarantinedTo));
                Assert.True(corrupt.QuarantinedTo.EndsWith(".ptmod.json.corrupt-20261010123456", StringComparison.Ordinal), corrupt.QuarantinedTo);
                Assert.False(File.Exists(path));

                io.Write(readModel, "x", Header);
                string error;
                Assert.True(io.Delete("x", out error), error);
                Assert.False(File.Exists(path));
                Assert.True(File.Exists(corrupt.QuarantinedTo), "the corrupt copy is kept");

                string a = Path.Combine(root, "a.txt");
                string b = Path.Combine(root, "b.txt");
                string bak = Path.Combine(root, "b.bak");
                File.WriteAllText(a, "new");
                File.WriteAllText(b, "old");
                disk.Replace(a, b, bak);
                Assert.Equal("new", disk.ReadAllText(b));
                Assert.Equal("old", disk.ReadAllText(bak));
                Assert.False(disk.FileExists(a));
                File.WriteAllText(a, "newer");
                disk.Replace(a, b, null);
                Assert.Equal("newer", disk.ReadAllText(b));
                Assert.Equal("old", disk.ReadAllText(bak), "no backup argument: the .bak is left alone");
                disk.Delete(Path.Combine(root, "no-such-file"));
                Assert.True(disk.DirectoryExists(root));
                Assert.False(disk.DirectoryExists(Path.Combine(root, "nope")));

                string resolved;
                string reason;
                Assert.False(io.TryGetPath("..\\evil", out resolved, out reason));
                Assert.True(io.TryGetPath("ok", out resolved, out reason));
                Assert.Equal(Path.GetFullPath(Path.Combine(saves, "ok.ptmod.json")), resolved);
                Assert.Equal(Path.GetFullPath(saves), io.SavesDirectory);
            }
            finally
            {
                try
                {
                    if (Directory.Exists(root)) Directory.Delete(root, true);
                }
                catch (IOException)
                {
                    // a leftover temporary folder is not worth failing the run
                }
            }
        }
    }

    /// <summary>The shapes of the side file (design.md, "DTO бокового файла"): the sample file of the design binds, round-trips and passes through the model.</summary>
    [Covers(typeof(SideFileHeader), typeof(CoreSectionDto), typeof(RandomDto), typeof(ModifierEntryDto), typeof(ReputationDto), typeof(ReputationChangeDto),
        typeof(MailStoreDto), typeof(MailRecordDto), typeof(MailChoiceDto), typeof(MailActionDto), typeof(LocTextDto))]
    public class SideFileDtoTests
    {
        // The example file of design.md, verbatim.
        private const string Sample = @"{
  ""schema"": 1,
  ""modVersion"": ""0.1.0"",
  ""gameVersion"": ""0.2.3"",
  ""saveName"": ""1985_3_1"",
  ""gameDate"": ""1985-03-01"",
  ""writtenUtc"": ""2026-10-10T18:42:07Z"",
  ""features"": {
    ""mod-framework"": {
      ""_schema"": 1,
      ""startDate"": ""1975-01-01"",
      ""random"": { ""worldSeed"": 2730914561, ""streams"": { ""framework-sample"": [123456789, 362436069, 521288629, 88675123] } },
      ""flags"": { ""framework-sample"": { ""welcomeSent"": 1 } },
      ""newGameOptions"": { ""first-year-onboarding.tutorial"": true },
      ""modifiers"": [
        { ""id"": ""m-000017"", ""feature"": ""framework-sample"", ""channel"": ""research.speed"", ""scope"": ""Player"", ""mult"": 1.25, ""expires"": ""1985-06-01"", ""reason"": ""smp.mod.boost"" },
        { ""id"": ""m-000018"", ""feature"": ""market-shocks"", ""channel"": ""market.demand.lowend"", ""scope"": ""Global"", ""mult"": 0.7, ""expires"": ""1985-04-01"", ""reason"": ""ms.event.recession"", ""reasonArgs"": [""рецессия""] }
      ],
      ""reputation"": {
        ""player"": { ""value"": 57.5, ""history"": [ { ""date"": ""1985-02-14"", ""delta"": 7.5, ""value"": 57.5, ""reason"": ""smp.test"" } ] },
        ""ai:0"":   { ""value"": 49.1, ""history"": [] }
      },
      ""mail"": {
        ""nextId"": 100004,
        ""mails"": [
          { ""id"": 100001, ""feature"": ""framework-sample"", ""tag"": ""smp:welcome"", ""priority"": ""Critical"", ""titleKey"": ""smp.mail.title"", ""bodyKey"": ""smp.mail.body"", ""args"": [""JAT""], ""sent"": ""1985-01-02"", ""delivered"": ""1985-01-02"", ""read"": true, ""choices"": [ { ""id"": ""yes"", ""labelKey"": ""core.common.yes"" }, { ""id"": ""no"", ""labelKey"": ""core.common.no"" } ], ""defaultChoice"": ""no"", ""deadline"": ""1985-01-12"", ""choice"": ""yes"", ""resolution"": ""Player"" },
          { ""id"": 100002, ""feature"": ""framework-sample"", ""tag"": ""smp:help"", ""priority"": ""Normal"", ""titleKey"": ""smp.mail.helpTitle"", ""bodyKey"": ""smp.mail.helpBody"", ""sent"": ""1985-02-10"", ""delivered"": ""1985-02-11"", ""read"": false, ""status"": ""Осталось 3 дня"", ""resolution"": ""Pending"",
            ""actions"": [ { ""id"": ""hint"", ""labelKey"": ""smp.mail.hint"", ""repeatable"": true }, { ""id"": ""skip"", ""labelKey"": ""smp.mail.skip"", ""repeatable"": false, ""used"": true, ""enabled"": false } ] }
        ],
        ""queue"": [
          { ""id"": 100003, ""feature"": ""framework-sample"", ""tag"": ""smp:digest"", ""priority"": ""Low"", ""titleKey"": ""smp.mail.digestTitle"", ""bodyKey"": ""smp.mail.digestBody"", ""sent"": ""1985-02-28"", ""resolution"": ""Pending"" }
        ]
      }
    },
    ""framework-sample"": { ""_schema"": 1, ""clicks"": 3 },
    ""gpu-product-line"": { ""_schema"": 2, ""lines"": [ ""…секция неизвестной фичи сохраняется как есть…"" ] }
  }
}";

        private static CoreSectionDto BindCore(JsonObject document, List<string> errors)
        {
            return ModJson.Bind<CoreSectionDto>(document.GetObject("features").GetObject("mod-framework"), errors);
        }

        [Test]
        public void SampleFile_BindsTheHeader()
        {
            var errors = new List<string>();

            SideFileHeader header = ModJson.Bind<SideFileHeader>(SideFileTestKit.Parse(Sample), errors);

            Assert.Equal(0, errors.Count);
            Assert.Equal(1, header.schema);
            Assert.Equal("0.1.0", header.modVersion);
            Assert.Equal("0.2.3", header.gameVersion);
            Assert.Equal("1985_3_1", header.saveName);
            Assert.Equal("1985-03-01", header.gameDate);
            Assert.Equal("2026-10-10T18:42:07Z", header.writtenUtc);
        }

        [Test]
        public void SampleFile_BindsTheCoreSection()
        {
            var errors = new List<string>();

            CoreSectionDto core = BindCore(SideFileTestKit.Parse(Sample), errors);

            Assert.Equal(0, errors.Count);
            Assert.Equal(1, core._schema);
            Assert.Equal("1975-01-01", core.startDate);
            Assert.Equal(2730914561u, core.random.worldSeed);
            Assert.Equal(new uint[] { 123456789, 362436069, 521288629, 88675123 }, core.random.streams["framework-sample"]);
            Assert.Equal(1, core.flags["framework-sample"]["welcomeSent"]);
            Assert.True(core.newGameOptions["first-year-onboarding.tutorial"].Equals(JsonValue.True));

            Assert.Equal(2, core.modifiers.Count);
            ModifierEntryDto boost = core.modifiers[0];
            Assert.Equal("m-000017", boost.id);
            Assert.Equal("framework-sample", boost.feature);
            Assert.Equal("research.speed", boost.channel);
            Assert.Equal("Player", boost.scope);
            Assert.Equal(1.25f, boost.mult);
            Assert.Equal("1985-06-01", boost.expires);
            Assert.Equal("smp.mod.boost", boost.reason);
            Assert.Equal(-1, boost.cpu, "missing: the default of the DTO");
            Assert.Equal(-1, boost.segment);
            Assert.Null(boost.company);
            Assert.Null(boost.reasonArgs);
            Assert.Equal(new[] { "рецессия" }, core.modifiers[1].reasonArgs);
            Assert.Equal(0.7f, core.modifiers[1].mult);

            Assert.Equal(57.5f, core.reputation["player"].value);
            Assert.Equal(1, core.reputation["player"].history.Count);
            ReputationChangeDto change = core.reputation["player"].history[0];
            Assert.Equal("1985-02-14", change.date);
            Assert.Equal(7.5f, change.delta);
            Assert.Equal(57.5f, change.value);
            Assert.Equal("smp.test", change.reason);
            Assert.Equal(49.1f, core.reputation["ai:0"].value);
            Assert.Equal(0, core.reputation["ai:0"].history.Count);
        }

        [Test]
        public void SampleFile_BindsTheMailStore()
        {
            var errors = new List<string>();

            MailStoreDto mail = BindCore(SideFileTestKit.Parse(Sample), errors).mail;

            Assert.Equal(0, errors.Count);
            Assert.Equal(100004, mail.nextId);
            Assert.Equal(2, mail.mails.Count);

            MailRecordDto welcome = mail.mails[0];
            Assert.Equal(100001, welcome.id);
            Assert.Equal("framework-sample", welcome.feature);
            Assert.Equal("smp:welcome", welcome.tag);
            Assert.Equal("Critical", welcome.priority);
            Assert.Equal("smp.mail.title", welcome.titleKey);
            Assert.Equal(new[] { "JAT" }, welcome.args);
            Assert.Equal("1985-01-02", welcome.sent);
            Assert.Equal("1985-01-02", welcome.delivered);
            Assert.True(welcome.read);
            Assert.Equal("no", welcome.defaultChoice);
            Assert.Equal("1985-01-12", welcome.deadline);
            Assert.Equal("yes", welcome.choice);
            Assert.Equal("Player", welcome.resolution);
            Assert.Equal(2, welcome.choices.Count);
            Assert.Equal("yes", welcome.choices[0].id);
            Assert.Equal("core.common.yes", welcome.choices[0].labelKey);
            Assert.True(welcome.choices[0].enabled, "missing: the default of the DTO");
            Assert.Null(welcome.title);
            Assert.Null(welcome.actions);

            MailRecordDto help = mail.mails[1];
            Assert.False(help.read);
            Assert.Equal("Осталось 3 дня", help.status);
            Assert.Equal("Pending", help.resolution);
            Assert.Equal(2, help.actions.Count);
            Assert.True(help.actions[0].repeatable);
            Assert.False(help.actions[0].used);
            Assert.True(help.actions[0].enabled);
            Assert.False(help.actions[1].repeatable);
            Assert.True(help.actions[1].used);
            Assert.False(help.actions[1].enabled);

            Assert.Equal(1, mail.queue.Count);
            Assert.Equal("smp:digest", mail.queue[0].tag);
            Assert.Equal("Low", mail.queue[0].priority);
            Assert.Null(mail.queue[0].delivered, "queued mail has no delivery date");
        }

        [Test]
        public void CoreSection_RoundTripsThroughTheTree()
        {
            var errors = new List<string>();
            CoreSectionDto core = BindCore(SideFileTestKit.Parse(Sample), errors);

            JsonNode written = ModJson.From(core, errors);
            CoreSectionDto again = ModJson.Bind<CoreSectionDto>(written, errors);
            JsonNode writtenAgain = ModJson.From(again, errors);

            Assert.Equal(0, errors.Count);
            SideFileTestKit.AssertJson(written, writtenAgain);
            Assert.Equal(2730914561u, again.random.worldSeed);
            Assert.Equal(2, again.mail.mails.Count);
            Assert.Equal("core.common.no", again.mail.mails[0].choices[1].labelKey);
            Assert.Equal(1.25f, again.modifiers[0].mult);
            Assert.True(written.ToString().Contains("2730914561"), "an unsigned 32-bit seed is written as is");
        }

        [Test]
        public void CoreSection_PassesThroughTheModelLikeAnyOtherSection()
        {
            var core = new Part("mod-framework");
            var sample = new Part("framework-sample");
            SideFileModel model = SideFileTestKit.Model(core, sample);

            SideFileReadReport report = model.Apply(Sample);
            JsonObject features = SideFileTestKit.Features(model.Build(null));

            Assert.Equal(SideFileStatus.Loaded, report.Status);
            Assert.Equal(0, report.Warnings.Count, "every section of the sample has a _schema");
            Assert.Equal(1, sample.LastRead.Count, "the section of framework-sample without its _schema");
            Assert.Equal(3, sample.LastRead.GetInt32("clicks"));
            Assert.Equal(1, core.LastSchema);
            Assert.Equal(new[] { "mod-framework", "framework-sample", "gpu-product-line" }, features.Keys.ToArray());
            Assert.Equal(2, features.GetObject("gpu-product-line").GetInt32("_schema"), "a section of an unknown feature is carried along");
        }

        [Test]
        public void Defaults_OfTheDtos_MatchTheDesign()
        {
            Assert.Equal(1, new CoreSectionDto()._schema);
            Assert.Equal(-1, new ModifierEntryDto().cpu);
            Assert.Equal(-1, new ModifierEntryDto().segment);
            Assert.True(new MailChoiceDto().enabled);
            Assert.True(new MailActionDto().enabled);
            Assert.True(new MailActionDto().repeatable);
            Assert.False(new MailActionDto().used);
            Assert.False(new MailRecordDto().read);
            Assert.Null(new CoreSectionDto().mail);
        }

        [Test]
        public void LocTextDto_HoldsKeyAndBothLanguages()
        {
            var errors = new List<string>();
            JsonObject json = SideFileTestKit.Parse("{\"id\":7,\"title\":{\"key\":\"a.b\",\"ru\":\"Привет\",\"en\":\"Hello\"},\"body\":{\"ru\":\"Только ru\"}}");

            MailRecordDto mail = ModJson.Bind<MailRecordDto>(json, errors);

            Assert.Equal(0, errors.Count);
            Assert.Equal("a.b", mail.title.key);
            Assert.Equal("Привет", mail.title.ru);
            Assert.Equal("Hello", mail.title.en);
            Assert.Null(mail.body.en);
            Assert.Equal("Только ru", mail.body.ru);
        }

        [Test]
        public void Dtos_IgnoreUnknownFieldsOfANewerMod_AndReportWrongTypesWithAPath()
        {
            var errors = new List<string>();

            CoreSectionDto core = ModJson.Bind<CoreSectionDto>(
                SideFileTestKit.Parse("{\"_schema\":1,\"futureField\":[1,2],\"startDate\":\"1980-01-01\",\"modifiers\":[{\"id\":\"m1\",\"mult\":\"fast\"}]}"), errors);

            Assert.Equal("1980-01-01", core.startDate);
            Assert.Equal(1, core.modifiers.Count);
            Assert.Equal(1, errors.Count);
            Assert.True(errors[0].StartsWith("$.modifiers[0].mult", StringComparison.Ordinal), errors[0]);
        }
    }
}
