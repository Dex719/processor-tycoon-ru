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
    /// ModProfile logic (FR-6 items 5-9, TSK-014): sections with a version owned by the core, passthrough of unknown and newer sections,
    /// the throttled and immediate atomic write with a .bak, recovery from a broken file. Fake disk, fake time.
    /// </summary>
    [Covers(typeof(ProfileModel), typeof(ProfileStatus), typeof(ProfileLoadReport))]
    public class ProfileTests
    {
        private const string ProfilePath = FakeFileSystem.Root + @"\ptmod_profile.json";
        private const string BackupPath = ProfilePath + ".bak";
        private static readonly DateTime Now = new DateTime(2026, 10, 10, 12, 34, 56, DateTimeKind.Utc);

        private const string Good1 = "{\"schema\":1,\"features\":{\"scenarios-achievements\":{\"_schema\":1,\"unlocked\":[\"first-profit\"]}}}";
        private const string Good2 = "{\"schema\":1,\"features\":{\"scenarios-achievements\":{\"_schema\":1,\"unlocked\":[\"first-profit\",\"big-win\"]}}}";

        private readonly List<string> _lazyWarnings = new List<string>();

        private ProfileModel Make(FakeFileSystem fs, double throttle = 2.0)
        {
            return new ProfileModel(fs, ProfilePath, () => Now, throttle, _lazyWarnings.Add);
        }

        private static JsonObject Parse(string text)
        {
            JsonNode node;
            JsonError error;
            Assert.True(ModJson.TryParse(text, out node, out error), "must be valid JSON: " + error);
            return (JsonObject)node;
        }

        private static JsonObject Saved(FakeFileSystem fs)
        {
            return Parse(fs.Text(ProfilePath));
        }

        private static JsonObject SavedSection(FakeFileSystem fs, string id)
        {
            return Saved(fs).GetObject("features").GetObject(id);
        }

        private static void AssertJson(JsonNode expected, JsonNode actual)
        {
            Assert.True(expected != null && expected.Equals(actual), "expected " + expected + " but got " + actual);
        }

        private static bool AnyContains(IEnumerable<string> lines, string fragment)
        {
            return lines.Any(l => l.IndexOf(fragment, StringComparison.Ordinal) >= 0);
        }

        private static string[] Unlocked(JsonObject section)
        {
            return section.GetArray("unlocked").Select(n => ((JsonValue)n).StringValue).ToArray();
        }

        // ---- loading ----

        [Test]
        public void Load_NoFile_IsAnEmptyNewProfile()
        {
            var fs = new FakeFileSystem();
            ProfileModel model = Make(fs);

            ProfileLoadReport report = model.Load(0);

            Assert.Equal(ProfileStatus.New, report.Status);
            Assert.Equal(ProfileStatus.New, model.Status);
            Assert.Equal(0, report.Warnings.Count);
            Assert.False(model.IsDirty);
            Assert.Equal(0, model.Section("any").Count);
            Assert.False(fs.Exists(ProfilePath), "loading never creates the file");
            Assert.Equal(0, model.SuccessfulWrites);
        }

        [Test]
        public void Load_ValidFile_ExposesSectionsWithoutTheSchemaField()
        {
            var fs = new FakeFileSystem();
            fs.Seed(ProfilePath, Good1);
            ProfileModel model = Make(fs);

            ProfileLoadReport report = model.Load(0);
            JsonObject section = model.Section("scenarios-achievements");

            Assert.Equal(ProfileStatus.Loaded, report.Status);
            Assert.Equal(0, report.Warnings.Count);
            Assert.Equal(new[] { "first-profit" }, Unlocked(section));
            Assert.False(section.ContainsKey("_schema"), "the version belongs to the core");
            Assert.Equal(1, model.StoredSchema("scenarios-achievements"));
            Assert.Equal(0, model.StoredSchema("missing"));
            Assert.Equal(0, model.StoredSchema(null));
            Assert.False(model.IsDirty);
        }

        [Test]
        public void Section_ReturnsTheSameLiveObjectOnEveryCall()
        {
            var fs = new FakeFileSystem();
            ProfileModel model = Make(fs);
            model.Load(0);

            JsonObject first = model.Section("fs", 1);
            first.Set("n", 5);
            JsonObject second = model.Section("fs", 1);

            Assert.True(ReferenceEquals(first, second));
            Assert.Equal(5, second.GetInt32("n"));
            Assert.Equal(0, _lazyWarnings.Count);
        }

        [Test]
        public void Section_RequestedAgainWithAnotherVersion_KeepsTheFirstAndWarns()
        {
            var fs = new FakeFileSystem();
            ProfileModel model = Make(fs);
            model.Load(0);

            model.Section("fs", 2);
            model.Section("fs", 3);
            model.Save(0);

            Assert.Equal(1, _lazyWarnings.Count);
            Assert.True(_lazyWarnings[0].Contains("features.fs"));
            Assert.Equal(2, SavedSection(fs, "fs").GetInt32("_schema"));
        }

        [Test]
        public void Section_BadArguments_GiveADetachedObject()
        {
            var fs = new FakeFileSystem();
            ProfileModel model = Make(fs);
            model.Load(0);

            JsonObject nothing = model.Section(null);
            nothing.Set("x", 1);
            JsonObject also = model.Section("");
            model.Save(0);

            Assert.NotNull(nothing);
            Assert.False(ReferenceEquals(nothing, also));
            Assert.Equal(2, _lazyWarnings.Count);
            Assert.Equal(0, Saved(fs).GetObject("features").Count, "a detached object is never written");

            model.Section("fs", 0);
            model.Save(0);
            Assert.Equal(1, SavedSection(fs, "fs").GetInt32("_schema"), "a version below 1 is read as 1");
        }

        // ---- sections and _schema ----

        [Test]
        public void Save_WritesTheRequestedVersionFirstInEverySection()
        {
            var fs = new FakeFileSystem();
            ProfileModel model = Make(fs);
            model.Load(0);

            model.Section("fs", 3).Set("n", 5);
            model.Section("other").Set("flag", true);
            Assert.True(model.Save(1).Success);

            JsonObject doc = Saved(fs);
            Assert.Equal(1, doc.GetInt32("schema"));
            JsonObject fsSection = doc.GetObject("features").GetObject("fs");
            Assert.Equal(new[] { "_schema", "n" }, fsSection.Keys.ToArray());
            Assert.Equal(3, fsSection.GetInt32("_schema"));
            Assert.Equal(1, doc.GetObject("features").GetObject("other").GetInt32("_schema"));
        }

        [Test]
        public void Load_SectionWithoutSchema_WarnsOnceWithPathAndGainsTheFieldOnWrite()
        {
            var fs = new FakeFileSystem();
            fs.Seed(ProfilePath, "{\"schema\":1,\"features\":{\"fs\":{\"count\":4}}}");
            ProfileModel model = Make(fs);

            ProfileLoadReport report = model.Load(0);
            JsonObject section = model.Section("fs", 1);
            model.Save(0);

            Assert.Equal(1, report.Warnings.Count);
            Assert.True(report.Warnings[0].StartsWith("features.fs._schema:", StringComparison.Ordinal), report.Warnings[0]);
            Assert.Equal(4, section.GetInt32("count"));
            Assert.Equal(1, model.StoredSchema("fs"));
            Assert.Equal(1, SavedSection(fs, "fs").GetInt32("_schema"));
            Assert.Equal(4, SavedSection(fs, "fs").GetInt32("count"));
        }

        [Test]
        public void Section_OlderStoredVersion_IsHandedOutForTheFeatureToMigrate()
        {
            var fs = new FakeFileSystem();
            fs.Seed(ProfilePath, "{\"schema\":1,\"features\":{\"fs\":{\"_schema\":1,\"old\":true}}}");
            ProfileModel model = Make(fs);
            model.Load(0);

            JsonObject section = model.Section("fs", 2);

            Assert.True(section.GetBool("old"));
            Assert.Equal(1, model.StoredSchema("fs"), "the feature can ask which version it got");
            section.Remove("old");
            section.Set("renamed", true);
            model.Save(0);
            Assert.Equal(2, SavedSection(fs, "fs").GetInt32("_schema"));
            Assert.True(SavedSection(fs, "fs").GetBool("renamed"));
        }

        [Test]
        public void Section_NewerThanRequested_IsEmptyAndTheStoredOneSurvives()
        {
            var fs = new FakeFileSystem();
            string stored = "{\"_schema\":3,\"x\":1,\"nested\":{\"list\":[1,2,3]}}";
            fs.Seed(ProfilePath, "{\"schema\":1,\"features\":{\"fs\":" + stored + "}}");
            ProfileModel model = Make(fs);
            model.Load(0);

            JsonObject section = model.Section("fs", 2);
            Assert.Equal(0, section.Count, "the feature gets an empty object");
            section.Set("y", 2);          // whatever the feature does with the empty object is dropped
            model.MarkDirty("fs", 0);
            model.Save(0);

            Assert.Equal(3, model.StoredSchema("fs"));
            AssertJson(Parse(stored), SavedSection(fs, "fs"));
        }

        [Test]
        public void Section_SchemaThatIsNotAVersion_IsKeptAsItIs()
        {
            var fs = new FakeFileSystem();
            string stored = "{\"_schema\":\"two\",\"x\":1}";
            fs.Seed(ProfilePath, "{\"schema\":1,\"features\":{\"fs\":" + stored + "}}");
            ProfileModel model = Make(fs);

            ProfileLoadReport report = model.Load(0);
            JsonObject section = model.Section("fs", 1);
            section.Set("y", 2);
            model.Save(0);

            Assert.True(AnyContains(report.Warnings, "features.fs._schema:"));
            Assert.Equal(0, model.StoredSchema("fs"));
            AssertJson(Parse(stored), SavedSection(fs, "fs"));
        }

        [Test]
        public void Section_StoredValueThatIsNotAnObject_IsReplacedWhenTheFeatureUsesIt()
        {
            var fs = new FakeFileSystem();
            fs.Seed(ProfilePath, "{\"schema\":1,\"features\":{\"fs\":5,\"other\":6}}");
            ProfileModel model = Make(fs);

            ProfileLoadReport report = model.Load(0);
            model.Section("fs", 1).Set("ok", true);
            model.Save(0);

            Assert.True(AnyContains(report.Warnings, "features.fs:"));
            Assert.True(AnyContains(report.Warnings, "features.other:"));
            Assert.True(SavedSection(fs, "fs").GetBool("ok"));
            Assert.Equal(6, Saved(fs).GetObject("features").GetInt32("other"), "an untouched oddity is not destroyed");
        }

        [Test]
        public void Save_AFeatureThatPutsSchemaInItsData_DoesNotOverrideTheCore()
        {
            var fs = new FakeFileSystem();
            ProfileModel model = Make(fs);
            model.Load(0);

            JsonObject section = model.Section("fs", 2);
            section.Set("_schema", 99);
            section.Set("n", 1);
            model.Save(0);

            Assert.Equal(2, SavedSection(fs, "fs").GetInt32("_schema"));
            Assert.Equal(1, SavedSection(fs, "fs").GetInt32("n"));
            Assert.Equal(1, _lazyWarnings.Count);
        }

        // AC-6.4
        [Test]
        public void Save_UnknownAndUntouchedSections_AreWrittenBackUnchanged()
        {
            var fs = new FakeFileSystem();
            string unknown = "{\"_schema\":7,\"lines\":[{\"id\":1,\"tags\":[\"a\",\"b\"]},{\"id\":2}],\"ratio\":0.125,\"big\":9007199254740993}";
            string noSchema = "{\"free\":\"form\"}";
            fs.Seed(ProfilePath, "{\"schema\":1,\"features\":{\"scenarios-achievements\":{\"_schema\":1,\"unlocked\":[\"first-profit\"]},"
                + "\"unknown-feature\":" + unknown + ",\"no-schema-one\":" + noSchema + "}}");
            ProfileModel model = Make(fs);
            model.Load(0);

            JsonObject mine = model.Section("scenarios-achievements", 1);
            mine.GetArray("unlocked").Add("big-win");
            model.MarkDirty("scenarios-achievements", 0);
            Assert.True(model.Save(0).Success);

            JsonObject features = Saved(fs).GetObject("features");
            AssertJson(Parse(unknown), features["unknown-feature"]);
            AssertJson(Parse(noSchema), features["no-schema-one"]);
            Assert.Equal(new[] { "first-profit", "big-win" }, Unlocked(features.GetObject("scenarios-achievements")));
            Assert.Equal(new[] { "scenarios-achievements", "unknown-feature", "no-schema-one" }, features.Keys.ToArray(), "file order is kept");
            Assert.True(fs.Text(ProfilePath).Contains("9007199254740993"), "a 16-digit integer survives exactly");
        }

        [Test]
        public void Save_NewSectionsGoAfterTheExistingOnes()
        {
            var fs = new FakeFileSystem();
            fs.Seed(ProfilePath, Good1);
            ProfileModel model = Make(fs);
            model.Load(0);

            model.Section("zzz").Set("a", 1);
            model.Section("aaa").Set("a", 1);
            model.Save(0);

            Assert.Equal(new[] { "scenarios-achievements", "zzz", "aaa" }, Saved(fs).GetObject("features").Keys.ToArray());
        }

        [Test]
        public void Save_UnknownTopLevelMembersAreKept_AndNewerFileSchemaIsNotDowngraded()
        {
            var fs = new FakeFileSystem();
            fs.Seed(ProfilePath, "{\"schema\":2,\"futureMeta\":{\"a\":1},\"features\":{\"fs\":{\"_schema\":1,\"n\":1}}}");
            ProfileModel model = Make(fs);

            ProfileLoadReport report = model.Load(0);
            model.Section("fs").Set("n", 2);
            model.Save(0);

            Assert.True(AnyContains(report.Warnings, "newer"));
            JsonObject doc = Saved(fs);
            Assert.Equal(2, doc.GetInt32("schema"));
            AssertJson(Parse("{\"a\":1}"), doc["futureMeta"]);
            Assert.Equal(2, SavedSection(fs, "fs").GetInt32("n"));
        }

        [Test]
        public void Load_SchemaMissingOrBad_IsHandledByTheRules()
        {
            var fs = new FakeFileSystem();
            fs.Seed(ProfilePath, "{\"features\":{}}");
            ProfileModel model = Make(fs);
            ProfileLoadReport missing = model.Load(0);
            Assert.Equal(ProfileStatus.Loaded, missing.Status);
            Assert.True(AnyContains(missing.Warnings, "schema:"));

            fs.Seed(ProfilePath, "{\"schema\":\"x\",\"features\":{}}");
            ProfileLoadReport bad = model.Load(0);
            Assert.Equal(ProfileStatus.Corrupt, bad.Status, "a file whose format number is not a number is broken");
        }

        // ---- writing: throttle, immediate, .bak ----

        // AC-6.2
        [Test]
        public void MarkDirtyTenTimesInOneSecond_GivesOneWriteTwoSecondsAfterTheFirst()
        {
            var fs = new FakeFileSystem();
            ProfileModel model = Make(fs);
            model.Load(0);
            JsonObject section = model.Section("fs");

            for (int i = 0; i < 10; i++)
            {
                section.Set("n", i);
                model.MarkDirty("fs", i * 0.1);   // 0.0 ... 0.9 s
            }

            Assert.True(model.IsDirty);
            Assert.Equal(2.0, model.DueAt);
            Assert.Null(model.Tick(1.0), "nothing is written inside the window");
            Assert.Null(model.Tick(1.99));
            Assert.Equal(0, fs.FinalWrites(ProfilePath));

            FileWriteResult written = model.Tick(2.0);

            Assert.NotNull(written);
            Assert.True(written.Success);
            Assert.Equal(1, fs.FinalWrites(ProfilePath), "exactly one write");
            Assert.Equal(1, model.SuccessfulWrites);
            Assert.Equal(9, SavedSection(fs, "fs").GetInt32("n"), "the latest value is in the file");
            Assert.False(model.IsDirty);
            Assert.Null(model.Tick(60), "clean: nothing more to write");
            Assert.Equal(1, fs.FinalWrites(ProfilePath));
        }

        [Test]
        public void Save_WritesAtOnce_AndTheThrottleThenHasNothingLeft()
        {
            var fs = new FakeFileSystem();
            ProfileModel model = Make(fs);
            model.Load(0);
            model.Section("fs").Set("n", 1);
            model.MarkDirty("fs", 0.5);

            FileWriteResult written = model.Save(0.6);

            Assert.True(written.Success);
            Assert.Equal(1, fs.FinalWrites(ProfilePath));
            Assert.False(model.IsDirty);
            Assert.Null(model.Tick(5.0));
            Assert.Equal(1, fs.FinalWrites(ProfilePath), "the throttled write is not repeated");
        }

        [Test]
        public void Save_AlwaysWrites_EvenWhenNothingWasMarked()
        {
            var fs = new FakeFileSystem();
            ProfileModel model = Make(fs);
            model.Load(0);

            Assert.True(model.Save(0).Success);
            Assert.True(model.Save(0).Success);

            Assert.Equal(2, fs.FinalWrites(ProfilePath));
        }

        [Test]
        public void Throttle_NeverWritesMoreThanOncePerWindow()
        {
            // Times are multiples of 0.25 so that every sum below is exact in binary floating point.
            var fs = new FakeFileSystem();
            ProfileModel model = Make(fs);
            model.Load(0);
            model.Section("fs");

            model.MarkDirty("fs", 0.0);
            Assert.NotNull(model.Tick(2.0));               // first write at 2.0
            model.MarkDirty("fs", 2.25);                   // a change right after the write
            Assert.Equal(4.25, model.DueAt);               // a full window after the change
            Assert.Null(model.Tick(4.0));
            Assert.NotNull(model.Tick(4.25));
            Assert.Equal(2, fs.FinalWrites(ProfilePath));

            model.MarkDirty("fs", 4.5);                    // a quarter second after the write: still a full window after the CHANGE
            Assert.Equal(6.5, model.DueAt);
            Assert.Null(model.Tick(6.25));
            Assert.NotNull(model.Tick(6.5));
            Assert.Equal(3, fs.FinalWrites(ProfilePath));
        }

        [Test]
        public void Throttle_WindowComesFromTheSetting()
        {
            var fs = new FakeFileSystem();
            ProfileModel model = Make(fs, 5.0);
            model.Load(0);
            model.MarkDirty("fs", 10);

            Assert.Equal(5.0, model.ThrottleSeconds);
            Assert.Equal(15.0, model.DueAt);
            Assert.Null(model.Tick(14.9));
            model.ThrottleSeconds = 1.0;                    // profile.saveThrottleSeconds arrives later from core.json
            Assert.Equal(11.0, model.DueAt);
            Assert.NotNull(model.Tick(11.0));
        }

        [Test]
        public void FlushIfDirty_WritesOnlyWhenThereIsSomethingToWrite()
        {
            var fs = new FakeFileSystem();
            ProfileModel model = Make(fs);
            model.Load(0);

            Assert.Null(model.FlushIfDirty(1), "clean: no write");
            Assert.False(fs.Exists(ProfilePath));

            model.Section("fs").Set("n", 1);
            model.MarkDirty("fs", 1.0);
            FileWriteResult written = model.FlushIfDirty(1.2);   // quit, or back to the main menu: no waiting for the window

            Assert.True(written.Success);
            Assert.Equal(1, SavedSection(fs, "fs").GetInt32("n"));
            Assert.Null(model.FlushIfDirty(1.3));
        }

        [Test]
        public void Save_FirstWriteIsAMove_ThenEveryWriteKeepsThePreviousVersionAsBak()
        {
            var fs = new FakeFileSystem();
            ProfileModel model = Make(fs);
            model.Load(0);
            JsonObject section = model.Section("fs");

            section.Set("n", 1);
            model.Save(0);
            string first = fs.Text(ProfilePath);
            Assert.False(fs.Exists(BackupPath), "nothing to back up on the first write");
            Assert.True(fs.Log.Contains("Move " + ProfilePath + ".tmp -> " + ProfilePath));

            section.Set("n", 2);
            model.Save(1);
            string second = fs.Text(ProfilePath);
            Assert.Equal(first, fs.Text(BackupPath), ".bak holds the version before the write");
            Assert.True(fs.Log.Contains("Replace " + ProfilePath + ".tmp -> " + ProfilePath + " (backup " + BackupPath + ")"));

            section.Set("n", 3);
            model.Save(2);
            Assert.Equal(second, fs.Text(BackupPath), "and again on the next write");
            Assert.NotEqual(second, fs.Text(ProfilePath));
            Assert.False(fs.Exists(ProfilePath + ".tmp"));
        }

        // fake disk: Replace fails
        [Test]
        public void Save_ReplaceFailure_LeavesTheOldFileAndBakAndRetriesAfterAWindow()
        {
            var fs = new FakeFileSystem();
            fs.Seed(ProfilePath, Good2);
            fs.Seed(BackupPath, Good1);
            ProfileModel model = Make(fs);
            model.Load(0);
            model.Section("scenarios-achievements").GetArray("unlocked").Add("third");
            model.MarkDirty("scenarios-achievements", 0);
            fs.FailOn(FsOp.Replace, "ptmod_profile.json");

            FileWriteResult failed = model.Tick(2.0);

            Assert.False(failed.Success);
            Assert.Equal(Good2, fs.Text(ProfilePath), "the main file is exactly as before");
            Assert.Equal(Good1, fs.Text(BackupPath), "and so is the .bak");
            Assert.False(fs.Exists(ProfilePath + ".tmp"), "the temporary file is removed");
            Assert.Equal(0, fs.FinalWrites(ProfilePath));
            Assert.True(model.IsDirty, "the change is still to be written");
            Assert.Equal(4.0, model.DueAt, "the next try is one window later, not every frame");
            Assert.Null(model.Tick(3.9));

            FileWriteResult retried = model.Tick(4.0);

            Assert.True(retried.Success);
            Assert.Equal(3, Unlocked(SavedSection(fs, "scenarios-achievements")).Length);
            Assert.Equal(Good2, fs.Text(BackupPath), ".bak is now the version that was in place");
            Assert.False(model.IsDirty);
        }

        [Test]
        public void Save_ExplicitSaveThatFails_BecomesDirtyAndIsRetried()
        {
            var fs = new FakeFileSystem();
            ProfileModel model = Make(fs);
            model.Load(0);
            model.Section("fs").Set("n", 1);
            fs.FailOn(FsOp.WriteAllText, "ptmod_profile.json");

            FileWriteResult failed = model.Save(10);

            Assert.False(failed.Success);
            Assert.True(model.IsDirty, "the data never reached the disk");
            Assert.Equal(12.0, model.DueAt);
            Assert.NotNull(model.Tick(12.0));
            Assert.Equal(1, SavedSection(fs, "fs").GetInt32("n"));
        }

        [Test]
        public void Save_FileLargerThanTheJsonLimit_IsRefusedAndTheOldFileStays()
        {
            var fs = new FakeFileSystem();
            fs.Seed(ProfilePath, Good1);
            ProfileModel model = Make(fs);
            model.Load(0);
            model.Section("fs").Set("blob", new string('x', ModJson.MaxBytes + 10));

            FileWriteResult result = model.Save(0);

            Assert.False(result.Success);
            Assert.True(result.Error.Contains("larger than"), result.Error);
            Assert.Equal(Good1, fs.Text(ProfilePath), "the file that can still be read is not replaced by one that cannot");
            Assert.False(fs.Exists(ProfilePath + ".tmp"));
        }

        [Test]
        public void Save_DocumentThatFitsOnlyCompactly_IsWrittenCompact()
        {
            var fs = new FakeFileSystem();
            ProfileModel model = Make(fs);
            model.Load(0);
            var numbers = new JsonArray();
            // 900 000 one-digit numbers: indented, each costs a whole line (about 11 bytes, ~10 MB > 8 MiB); compact, 2 bytes (~1.8 MB).
            for (int i = 0; i < 900000; i++) numbers.Add(i % 10);
            model.Section("fs").Set("numbers", numbers);

            FileWriteResult result = model.Save(0);

            Assert.True(result.Success, result.Error);
            string text = fs.Text(ProfilePath);
            Assert.True(text.Length <= ModJson.MaxBytes);
            Assert.False(text.Contains("\n"), "the compact form was chosen because the indented one would not be readable back");
        }

        // ---- recovery ----

        // AC-6.3
        [Test]
        public void Load_TruncatedMainFileWithValidBak_RestoresFromBackup()
        {
            var fs = new FakeFileSystem();
            fs.Seed(ProfilePath, Good2);
            fs.Truncate(ProfilePath, 60);
            string broken = fs.Text(ProfilePath);
            fs.Seed(BackupPath, Good1);
            ProfileModel model = Make(fs);

            ProfileLoadReport report = model.Load(0);

            Assert.Equal(ProfileStatus.RestoredFromBackup, report.Status);
            Assert.Equal(ProfileStatus.RestoredFromBackup, model.Status);
            Assert.Equal(ProfilePath + ".corrupt-20261010123456", report.QuarantinedTo);
            Assert.Equal(broken, fs.Text(report.QuarantinedTo), "the broken file is kept for the owner");
            Assert.True(report.Error.Contains("line 1"), report.Error);
            Assert.Equal(new[] { "first-profit" }, Unlocked(model.Section("scenarios-achievements")), "the data of the .bak");

            Assert.True(report.RestoreWrite.Success, "the main file is put back at once");
            AssertJson(Parse(Good1), Saved(fs));
            Assert.Equal(Good1, fs.Text(BackupPath), "the .bak is not touched by the restore");
            Assert.Equal(1, model.SuccessfulWrites);
            Assert.False(model.IsDirty);
        }

        [Test]
        public void Load_MainAndBakBothBroken_GivesAnEmptyCorruptProfile()
        {
            var fs = new FakeFileSystem();
            fs.Seed(ProfilePath, Good2);
            fs.Truncate(ProfilePath, 40);
            fs.Seed(BackupPath, Good1);
            fs.Truncate(BackupPath, 20);
            ProfileModel model = Make(fs);

            ProfileLoadReport report = model.Load(0);

            Assert.Equal(ProfileStatus.Corrupt, report.Status);
            Assert.Equal(ProfileStatus.Corrupt, model.Status);
            Assert.True(fs.Exists(report.QuarantinedTo));
            Assert.False(fs.Exists(ProfilePath), "nothing is written until the profile changes");
            Assert.Equal(0, model.Section("scenarios-achievements").Count, "an empty profile");
            Assert.True(AnyContains(report.Warnings, "no usable backup"));

            model.Section("fs").Set("n", 1);
            Assert.True(model.Save(0).Success, "a new file starts from the empty profile");
            Assert.Equal(1, SavedSection(fs, "fs").GetInt32("n"));
        }

        [Test]
        public void Load_BrokenMainAndNoBak_GivesAnEmptyCorruptProfile()
        {
            var fs = new FakeFileSystem();
            fs.Seed(ProfilePath, "{\"schema\":1,\"features\":");
            ProfileModel model = Make(fs);

            ProfileLoadReport report = model.Load(0);

            Assert.Equal(ProfileStatus.Corrupt, report.Status);
            Assert.True(AnyContains(report.Warnings, "there is no .bak file"));
            Assert.Equal(1, fs.FilesContaining(".corrupt-").Count);
        }

        [Test]
        public void Load_UnreadableMainFile_IsTreatedLikeABrokenOne()
        {
            var fs = new FakeFileSystem();
            fs.Seed(ProfilePath, Good2);
            fs.Seed(BackupPath, Good1);
            fs.FailOn(FsOp.ReadAllText, "ptmod_profile.json", 1);
            ProfileModel model = Make(fs);

            ProfileLoadReport report = model.Load(0);

            Assert.Equal(ProfileStatus.RestoredFromBackup, report.Status);
            Assert.True(report.Error.Contains("cannot be read"), report.Error);
        }

        [Test]
        public void Load_BrokenMainAndUnreadableBak_GivesAnEmptyCorruptProfile()
        {
            var fs = new FakeFileSystem();
            fs.Seed(ProfilePath, "{");
            fs.Seed(BackupPath, Good1);
            fs.FailOn(FsOp.ReadAllText, ".bak");
            ProfileModel model = Make(fs);

            ProfileLoadReport report = model.Load(0);

            Assert.Equal(ProfileStatus.Corrupt, report.Status);
            Assert.True(AnyContains(report.Warnings, "the backup cannot be read"));
        }

        [Test]
        public void Load_BrokenMainThatCannotBeRenamed_StillRestoresAndKeepsTheBak()
        {
            var fs = new FakeFileSystem();
            fs.Seed(ProfilePath, "{ broken");
            fs.Seed(BackupPath, Good1);
            fs.FailOn(FsOp.Move, ".corrupt-");
            ProfileModel model = Make(fs);

            ProfileLoadReport report = model.Load(0);

            Assert.Equal(ProfileStatus.RestoredFromBackup, report.Status);
            Assert.Null(report.QuarantinedTo);
            Assert.True(AnyContains(report.Warnings, "could not be set aside"));
            Assert.True(report.RestoreWrite.Success);
            AssertJson(Parse(Good1), Saved(fs));
            Assert.Equal(Good1, fs.Text(BackupPath), "a copy of the broken file never replaces the good .bak");
        }

        [Test]
        public void Load_RestoreWriteFails_TheDataStayDirtyAndAreWrittenLater()
        {
            var fs = new FakeFileSystem();
            fs.Seed(ProfilePath, "{ broken");
            fs.Seed(BackupPath, Good1);
            fs.FailOn(FsOp.Move, "ptmod_profile.json.tmp");
            ProfileModel model = Make(fs);

            ProfileLoadReport report = model.Load(5);

            Assert.Equal(ProfileStatus.RestoredFromBackup, report.Status);
            Assert.False(report.RestoreWrite.Success);
            Assert.True(AnyContains(report.Warnings, "could not be written back"));
            Assert.True(model.IsDirty);
            Assert.Equal(7.0, model.DueAt);
            Assert.NotNull(model.Tick(7.0));
            AssertJson(Parse(Good1), Saved(fs));
        }

        [Test]
        public void Load_MissingMainFileWithBak_IsNewAndLeavesTheBakAlone()
        {
            var fs = new FakeFileSystem();
            fs.Seed(BackupPath, Good1);
            ProfileModel model = Make(fs);

            ProfileLoadReport report = model.Load(0);

            Assert.Equal(ProfileStatus.New, report.Status, "deleting the profile is a reset, not damage");
            Assert.Equal(0, model.Section("scenarios-achievements").Count);
            Assert.Equal(Good1, fs.Text(BackupPath));
        }

        [Test]
        public void Load_Twice_StartsFromTheFileAgain()
        {
            var fs = new FakeFileSystem();
            fs.Seed(ProfilePath, Good1);
            ProfileModel model = Make(fs);
            model.Load(0);
            model.Section("scenarios-achievements").GetArray("unlocked").Add("unsaved");
            model.MarkDirty("scenarios-achievements", 0);

            model.Load(1);

            Assert.False(model.IsDirty);
            Assert.Equal(new[] { "first-profit" }, Unlocked(model.Section("scenarios-achievements")));
        }

        [Test]
        public void Constructor_RejectsMissingArguments()
        {
            var fs = new FakeFileSystem();
            Assert.Throws<ArgumentNullException>(() => new ProfileModel(null, ProfilePath));
            Assert.Throws<ArgumentException>(() => new ProfileModel(fs, ""));
            Assert.Equal(ProfilePath + ".bak", new ProfileModel(fs, ProfilePath).BackupPath);
            Assert.Equal(ProfilePath, new ProfileModel(fs, ProfilePath).FilePath);
        }
    }

    /// <summary>The window logic behind ProfileModel's throttle, on fake time.</summary>
    [Covers(typeof(WriteThrottle))]
    public class WriteThrottleTests
    {
        [Test]
        public void Clean_HasNoDueTimeAndIsNeverDue()
        {
            var throttle = new WriteThrottle(2.0);

            Assert.False(throttle.IsDirty);
            Assert.True(double.IsNaN(throttle.DueAt));
            Assert.False(throttle.IsDue(1e9));
        }

        [Test]
        public void FirstMarkOpensTheWindow_LaterMarksDoNotMoveIt()
        {
            var throttle = new WriteThrottle(2.0);

            for (int i = 0; i < 10; i++) throttle.MarkDirty(i * 0.1);

            Assert.True(throttle.IsDirty);
            Assert.Equal(2.0, throttle.DueAt);
            Assert.False(throttle.IsDue(1.99));
            Assert.True(throttle.IsDue(2.0));
            Assert.True(throttle.IsDue(50));
        }

        [Test]
        public void Saved_CleansAndTheNextWindowCountsFromTheNextChange()
        {
            var throttle = new WriteThrottle(2.0);
            throttle.MarkDirty(0);
            throttle.Saved(2.0);

            Assert.False(throttle.IsDirty);
            Assert.False(throttle.IsDue(100));

            throttle.MarkDirty(2.5);
            Assert.Equal(4.5, throttle.DueAt);
            throttle.Saved(4.5);
            throttle.MarkDirty(4.75);
            Assert.Equal(6.75, throttle.DueAt);
        }

        [Test]
        public void AWriteJustBeforeAMark_PushesThePermissionToAFullWindowAfterTheMark()
        {
            var throttle = new WriteThrottle(2.0);
            throttle.Saved(10.0);          // an explicit Save()

            throttle.MarkDirty(10.5);

            Assert.Equal(12.5, throttle.DueAt);
        }

        [Test]
        public void Failed_KeepsDirtyAndRetriesOneWindowLater()
        {
            var throttle = new WriteThrottle(2.0);
            throttle.MarkDirty(0);

            throttle.Failed(2.0);

            Assert.True(throttle.IsDirty);
            Assert.Equal(4.0, throttle.DueAt);
            Assert.False(throttle.IsDue(3.99));
            Assert.True(throttle.IsDue(4.0));

            throttle.Failed(4.0);
            Assert.Equal(6.0, throttle.DueAt);
        }

        [Test]
        public void ABadWindow_BecomesZero_SoSavingCannotStop()
        {
            var throttle = new WriteThrottle(-5);
            Assert.Equal(0.0, throttle.WindowSeconds);

            throttle.WindowSeconds = double.NaN;
            Assert.Equal(0.0, throttle.WindowSeconds);
            throttle.WindowSeconds = double.PositiveInfinity;
            Assert.Equal(0.0, throttle.WindowSeconds);

            throttle.MarkDirty(7);
            Assert.True(throttle.IsDue(7), "a zero window means the next poll");

            Assert.Equal(WriteThrottle.DefaultWindowSeconds, new WriteThrottle().WindowSeconds);
        }

        [Test]
        public void Reset_ForgetsEverything()
        {
            var throttle = new WriteThrottle(2.0);
            throttle.MarkDirty(5);
            throttle.Failed(6);

            throttle.Reset();

            Assert.False(throttle.IsDirty);
            Assert.True(double.IsNaN(throttle.DueAt));
            throttle.MarkDirty(0);
            Assert.Equal(2.0, throttle.DueAt, "the failed attempt of before the reset does not count");
        }
    }
}
