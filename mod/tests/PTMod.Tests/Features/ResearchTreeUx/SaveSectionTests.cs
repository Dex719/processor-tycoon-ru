using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using PTMod.Core;
using PTMod.Core.Logic.Json;
using PTMod.Core.Logic.Save;
using PTMod.Features.ResearchTreeUx;
using PTMod.Tests.Architecture;

namespace PTMod.Tests.Features.ResearchTreeUx
{
    /// <summary>
    /// The section <c>research-tree-ux</c> of the side save file as a whole (NFR-4, TSK-019): the holder <see cref="UxSaveSection"/> on its own
    /// (no section, an empty one, ids outside the technologies, repeats, more than 512, a list that is not a list, <c>ResetToDefaults</c>) and
    /// inside the core's own <see cref="SideFileModel"/> (a save without the section, a section newer than the mod, the feature switched off), so
    /// the contract between the two is tested and not assumed: the holder neither reads nor writes the version field (<c>_schema</c> is the
    /// core's), a newer section is kept by the core as it is, and nothing is written to the save of the game.
    /// </summary>
    [Covers(typeof(UxSaveSection))]
    public class SaveSectionTests
    {
        private const string Id = "research-tree-ux";
        private const int Technologies = 97;

        // ---- helpers ----

        private static JsonObject Parse(string json)
        {
            JsonNode node;
            JsonError error;
            Assert.True(ModJson.TryParse(json, out node, out error), "test JSON is invalid: " + error);
            return (JsonObject)node;
        }

        private static UxSaveSection Section(List<string> warnings, int technologies = Technologies)
        {
            return new UxSaveSection(() => technologies, warnings == null ? (Action<string>)null : warnings.Add);
        }

        private static int[] Seen(UxSaveSection section)
        {
            return section.Tracker.Seen.ToArray();
        }

        private static string SectionText(string list)
        {
            return "{ \"seenAvailable\": " + list + " }";
        }

        private static UxSaveSection Loaded(string sectionJson, List<string> warnings, int technologies = Technologies)
        {
            UxSaveSection section = Section(warnings, technologies);
            section.Read(Parse(sectionJson), 1);
            return section;
        }

        private static string FileText(string featuresJson)
        {
            return "{ \"schema\": 1, \"modVersion\": \"0.1.0\", \"features\": " + featuresJson + " }";
        }

        private static SideFileModel ModelWith(UxSaveSection section, Func<string, bool> isActive = null)
        {
            var model = new SideFileModel(isActive);
            string problem;
            Assert.True(model.TryRegister(section, out problem), "the section is registered: " + problem);
            return model;
        }

        private static JsonObject FeaturesOf(SideFileModel model)
        {
            SideFileBuildReport build = model.Build(new SideFileHeader { modVersion = "0.1.0" });
            JsonObject features = build.Document.GetObject("features");
            Assert.NotNull(features, "the document has a features map");
            return features;
        }

        // ---- the identity of the section ----

        [Test]
        public void TheSection_IsKeyedByTheFeatureId_AtVersionOne()
        {
            UxSaveSection section = Section(null);
            Assert.Equal(Id, section.SectionId);
            Assert.Equal(1, section.SchemaVersion);
            Assert.Equal(Id, UxSaveSection.Id);
        }

        // ---- no section, an empty one ----

        [Test]
        public void ANewHolder_HasNothingSeen_AndWaitsToBeInitialized()
        {
            UxSaveSection section = Section(null);

            Assert.Equal(0, section.Tracker.SeenCount);
            Assert.True(section.NeedsInitialization);
            Assert.False(section.HasStoredSection);
        }

        [Test]
        public void NoSection_ReadOfNull_GivesTheDefaults()
        {
            var warnings = new List<string>();
            UxSaveSection section = Section(warnings);
            section.Tracker.MarkSeen(5);
            section.MarkInitialized();

            section.Read(null, 1);

            Assert.Equal(0, section.Tracker.SeenCount, "the old set is gone");
            Assert.True(section.NeedsInitialization, "FR-3 item 5: every available technology counts as seen when the game is ready");
            Assert.False(section.HasStoredSection);
            Assert.Equal(0, warnings.Count);
        }

        [Test]
        public void AnEmptySectionObject_SaysNothingAboutWhatWasSeen_SoItIsReadAsMissing()
        {
            var warnings = new List<string>();

            UxSaveSection section = Loaded("{}", warnings);

            Assert.Equal(0, section.Tracker.SeenCount);
            Assert.True(section.NeedsInitialization, "an object without the list must not turn every available technology into a new one");
            Assert.False(section.HasStoredSection);
            Assert.Equal(0, warnings.Count, "a section that simply has no list is not damage: " + string.Join("; ", warnings));
        }

        [Test]
        public void AnEmptyList_IsAStoredSection_NothingWasSeen()
        {
            var warnings = new List<string>();

            UxSaveSection section = Loaded(SectionText("[]"), warnings);

            Assert.Equal(0, section.Tracker.SeenCount);
            Assert.False(section.NeedsInitialization);
            Assert.True(section.HasStoredSection);
            Assert.Equal(0, warnings.Count);
        }

        [Test]
        public void TheExampleOfTheSpec_IsRestored()
        {
            var warnings = new List<string>();

            UxSaveSection section = Loaded(SectionText("[12, 31, 44, 58, 71]"), warnings);

            Assert.Equal(new[] { 12, 31, 44, 58, 71 }, Seen(section));
            Assert.False(section.NeedsInitialization);
            Assert.True(section.HasStoredSection);
            Assert.Equal(0, warnings.Count);
        }

        [Test]
        public void TheVersionArgument_IsIgnored_TheDataIsTheSame()
        {
            foreach (int schema in new[] { 0, 1, 2, 99 })
            {
                UxSaveSection section = Section(null);
                section.Read(Parse(SectionText("[3, 9]")), schema);
                Assert.Equal(new[] { 3, 9 }, Seen(section), "schema " + schema);
            }
        }

        [Test]
        public void Read_DoesNotChangeTheObjectItIsGiven()
        {
            JsonObject given = Parse(SectionText("[9, 3, 3, -1, 500]"));
            string before = ModJson.Write(given, false);

            Section(null).Read(given, 1);

            Assert.Equal(before, ModJson.Write(given, false), "the core hands out its stored object: Read must not edit it");
        }

        // ---- damaged values ----

        [Test]
        public void IdsOutsideTheTechnologies_AreDropped_WithOneWarning()
        {
            var warnings = new List<string>();

            // 97 technologies: the valid ids are 0...96.
            UxSaveSection section = Loaded(SectionText("[-3, 0, 96, 97, 5000, 12]"), warnings);

            Assert.Equal(new[] { 0, 12, 96 }, Seen(section));
            Assert.Equal(1, warnings.Count, string.Join("; ", warnings));
            Assert.True(warnings[0].Contains("3"), warnings[0]);
            Assert.False(section.NeedsInitialization, "the section is stored, only its damaged values are gone");
        }

        [Test]
        public void WhenTheNumberOfTechnologiesIsUnknown_OnlyNegativeIdsAreDropped()
        {
            UxSaveSection unknown = new UxSaveSection(() => 0, null);
            unknown.Read(Parse(SectionText("[-1, 5000, 7]")), 1);
            Assert.Equal(new[] { 7, 5000 }, Seen(unknown));

            UxSaveSection noCounter = new UxSaveSection();
            noCounter.Read(Parse(SectionText("[-1, 5000, 7]")), 1);
            Assert.Equal(new[] { 7, 5000 }, Seen(noCounter), "no counter at all is the same as an unknown count");
        }

        [Test]
        public void RepeatedIds_AreMerged_WithOneWarning()
        {
            var warnings = new List<string>();

            UxSaveSection section = Loaded(SectionText("[44, 12, 44, 31, 12, 12]"), warnings);

            Assert.Equal(new[] { 12, 31, 44 }, Seen(section));
            Assert.Equal(1, warnings.Count, string.Join("; ", warnings));
        }

        [Test]
        public void MoreThan512Ids_KeepTheFirst512_WithOneWarning()
        {
            var many = new List<string>();
            for (int i = 0; i < 600; i++) many.Add(i.ToString());
            var warnings = new List<string>();

            // The count of technologies is not known yet (the research data is not loaded when a save is read at the menu): only the length is checked.
            UxSaveSection section = Loaded(SectionText("[" + string.Join(",", many) + "]"), warnings, 0);

            Assert.Equal(512, section.Tracker.SeenCount);
            Assert.True(section.Tracker.IsSeen(0) && section.Tracker.IsSeen(511) && !section.Tracker.IsSeen(512), "the smallest ids stay");
            Assert.Equal(1, warnings.Count, string.Join("; ", warnings));
            Assert.True(warnings[0].Contains("512"), warnings[0]);
        }

        [Test]
        public void MoreThan512Ids_WithAKnownCount_AreCutDownToTheTechnologiesFirst()
        {
            var many = new List<string>();
            for (int i = 0; i < 600; i++) many.Add(i.ToString());

            UxSaveSection section = Loaded(SectionText("[" + string.Join(",", many) + "]"), null);

            Assert.Equal(Technologies, section.Tracker.SeenCount, "ids 97 and over are no technologies, so they go before the length limit is looked at");
            Assert.True(section.Tracker.IsSeen(96) && !section.Tracker.IsSeen(97));
        }

        [Test]
        public void EntriesThatAreNotWholeNumbers_AreDropped_TheRestStands()
        {
            var warnings = new List<string>();

            UxSaveSection section = Loaded(SectionText("[1, 2.5, \"7\", null, true, 3, [4], {\"a\": 5}, 1e20]"), warnings);

            Assert.Equal(new[] { 1, 3 }, Seen(section));
            Assert.Equal(1, warnings.Count, string.Join("; ", warnings));
            Assert.True(warnings[0].Contains("7"), "seven entries are dropped: " + warnings[0]);
        }

        [Test]
        public void AListThatIsNotAList_IsReadAsMissing_WithAWarning()
        {
            foreach (string wrong in new[] { "\"12\"", "12", "null", "true", "{ \"0\": 1 }" })
            {
                var warnings = new List<string>();

                UxSaveSection section = Loaded(SectionText(wrong), warnings);

                Assert.Equal(0, section.Tracker.SeenCount, wrong);
                Assert.True(section.NeedsInitialization, wrong + ": no list, nothing is known as seen");
                Assert.False(section.HasStoredSection, wrong);
                Assert.Equal(1, warnings.Count, wrong + ": " + string.Join("; ", warnings));
            }
        }

        [Test]
        public void UnknownMembers_AreIgnored()
        {
            UxSaveSection section = Loaded("{ \"seenAvailable\": [4, 8], \"future\": { \"x\": 1 }, \"_schema\": 1 }", null);

            Assert.Equal(new[] { 4, 8 }, Seen(section));
        }

        [Test]
        public void ALoggerThatThrows_AndACounterThatThrows_CostNothing()
        {
            var section = new UxSaveSection(() => { throw new InvalidOperationException("no research data"); }, line => { throw new InvalidOperationException("no log"); });

            section.Read(Parse(SectionText("[-1, 4, 4, 2]")), 1);

            Assert.Equal(new[] { 2, 4 }, Seen(section));
            Assert.True(section.HasStoredSection);
        }

        // ---- ResetToDefaults ----

        [Test]
        public void ResetToDefaults_ForgetsWhatWasRead_AndAsksForTheInitialization()
        {
            UxSaveSection section = Loaded(SectionText("[1, 2, 3]"), null);
            NewTechTracker old = section.Tracker;

            section.ResetToDefaults();

            Assert.Equal(0, section.Tracker.SeenCount);
            Assert.True(section.NeedsInitialization);
            Assert.False(section.HasStoredSection);
            Assert.False(ReferenceEquals(old, section.Tracker), "a new tracker: the old one is not changed under a holder of it");
            Assert.Equal(3, old.SeenCount);
        }

        [Test]
        public void ReadTwice_ReplacesTheSet_NeverMergesIt()
        {
            UxSaveSection section = Loaded(SectionText("[1, 2, 3]"), null);

            section.Read(Parse(SectionText("[9]")), 1);

            Assert.Equal(new[] { 9 }, Seen(section));
        }

        [Test]
        public void MarkInitialized_EndsTheWaiting_ResetStartsItAgain()
        {
            UxSaveSection section = Section(null);

            section.MarkInitialized();
            Assert.False(section.NeedsInitialization);
            Assert.False(section.HasStoredSection, "initialized in memory is not a stored section");

            section.ResetToDefaults();
            Assert.True(section.NeedsInitialization);
        }

        // ---- writing ----

        [Test]
        public void Write_HasNoVersionField_JustTheList()
        {
            UxSaveSection section = Section(null);
            section.Tracker.MarkSeen(new[] { 9, 3, 3, 40 });
            section.MarkInitialized();

            JsonObject written = section.Write();

            Assert.NotNull(written);
            Assert.Equal(1, written.Count, "the list and nothing else: no _schema, no schema, no version (D20)");
            Assert.False(written.ContainsKey("_schema") || written.ContainsKey("schema") || written.ContainsKey("version"));
            Assert.Equal("{\"seenAvailable\":[3,9,40]}", ModJson.Write(written, false));
        }

        [Test]
        public void Write_BeforeTheInitialization_LeavesTheSectionOut()
        {
            UxSaveSection section = Section(null);

            Assert.Null(section.Write(), "nothing is known as seen: an empty list would turn every available technology into a new one on the next load");

            section.MarkInitialized();
            Assert.Equal("{\"seenAvailable\":[]}", ModJson.Write(section.Write(), false), "initialized with nothing seen is a fact worth storing");
        }

        [Test]
        public void WriteThenRead_GivesTheSameSet()
        {
            UxSaveSection first = Section(null);
            first.Tracker.MarkSeen(new[] { 0, 7, 33, 96 });
            first.MarkInitialized();
            string text = ModJson.Write(first.Write(), false);

            UxSaveSection second = Section(null);
            second.Read(Parse(text), 1);

            Assert.Equal(Seen(first), Seen(second));
            Assert.False(second.NeedsInitialization);
        }

        [Test]
        public void Write_NeverMoreThan512Ids()
        {
            UxSaveSection section = Section(null);
            for (int i = 0; i < 700; i++) section.Tracker.MarkSeen(i);
            section.MarkInitialized();

            JsonArray list = section.Write().GetArray("seenAvailable");

            Assert.Equal(512, list.Count);
        }

        // ---- inside the side file model of the core ----

        [Test]
        public void ASaveWithoutTheSection_GetsTheDefaults_AndTheInitializationIsAsked()
        {
            var section = Section(null);
            SideFileModel model = ModelWith(section);
            section.Tracker.MarkSeen(1);

            SideFileReadReport report = model.Apply(FileText("{}"));

            Assert.Equal(SideFileStatus.Loaded, report.Status);
            Assert.Equal(0, section.Tracker.SeenCount);
            Assert.True(section.NeedsInitialization, "FR-3 item 5: an old save has no section");
            Assert.False(section.HasStoredSection);
        }

        [Test]
        public void AVanillaSave_NoSideFile_GetsTheDefaults()
        {
            var section = Section(null);
            SideFileModel model = ModelWith(section);
            section.Tracker.MarkSeen(1);
            section.MarkInitialized();

            SideFileReadReport report = model.ApplyMissing();

            Assert.Equal(SideFileStatus.Missing, report.Status);
            Assert.Equal(0, section.Tracker.SeenCount);
            Assert.True(section.NeedsInitialization);
        }

        [Test]
        public void ANewGame_ResetsTheSection()
        {
            var section = Section(null);
            SideFileModel model = ModelWith(section);
            model.Apply(FileText("{ \"" + Id + "\": { \"_schema\": 1, \"seenAvailable\": [1, 2] } }"));
            Assert.Equal(2, section.Tracker.SeenCount);

            model.StartNewGame();

            Assert.Equal(0, section.Tracker.SeenCount);
            Assert.True(section.NeedsInitialization);
        }

        [Test]
        public void ASectionOfThisVersion_IsDistributedWithoutItsVersionField()
        {
            var section = Section(null);
            SideFileModel model = ModelWith(section);

            SideFileReadReport report = model.Apply(FileText("{ \"" + Id + "\": { \"_schema\": 1, \"seenAvailable\": [12, 31, 44] } }"));

            Assert.Equal(SideFileStatus.Loaded, report.Status);
            Assert.Equal(0, report.Warnings.Count, string.Join("; ", report.Warnings));
            Assert.Equal(new[] { 12, 31, 44 }, Seen(section));
            Assert.False(section.NeedsInitialization);
        }

        [Test]
        public void ASectionWithoutAVersion_IsReadAsVersionOne_AndTheCoreAddsTheField()
        {
            var section = Section(null);
            SideFileModel model = ModelWith(section);

            model.Apply(FileText("{ \"" + Id + "\": { \"seenAvailable\": [5] } }"));
            Assert.Equal(new[] { 5 }, Seen(section));

            JsonObject written = FeaturesOf(model).GetObject(Id);
            Assert.NotNull(written);
            Assert.Equal(1, written.GetInt32("_schema", -1), "the version is the core's: it appears on the first write");
            Assert.Equal(2, written.Count);
        }

        [Test]
        public void TheWrittenSection_CarriesOnlyTheVersionOfTheCoreAndTheList()
        {
            var section = Section(null);
            SideFileModel model = ModelWith(section);
            model.StartNewGame();
            section.Tracker.MarkSeen(new[] { 8, 2 });
            section.MarkInitialized();

            SideFileBuildReport build = model.Build(new SideFileHeader());

            Assert.Equal(0, build.Warnings.Count, "no 'Write() returned the version' warning: " + string.Join("; ", build.Warnings));
            Assert.Equal("{\"_schema\":1,\"seenAvailable\":[2,8]}", ModJson.Write(build.Document.GetObject("features").GetObject(Id), false));
        }

        [Test]
        public void ASaveBeforeTheInitialization_LeavesTheSectionOut()
        {
            var section = Section(null);
            SideFileModel model = ModelWith(section);
            model.Apply(FileText("{}"));

            JsonObject features = FeaturesOf(model);

            Assert.False(features.ContainsKey(Id), "an old save that is written before the game is ready does not get an empty list");
        }

        [Test]
        public void ASectionNewerThanTheMod_GivesDefaults_AndIsKeptByTheCoreAsItWas()
        {
            var section = Section(null);
            SideFileModel model = ModelWith(section);
            const string newer = "{ \"_schema\": 2, \"seenAvailable\": [1, 2, 3], \"pinned\": [\"memory\"], \"layout\": { \"w\": 1.4 } }";

            SideFileReadReport report = model.Apply(FileText("{ \"" + Id + "\": " + newer + " }"));

            Assert.Equal(SideFileStatus.Newer, report.Status);
            Assert.Equal(new[] { Id }, report.NewerSections.ToArray());
            Assert.Equal(0, section.Tracker.SeenCount, "the older mod uses the defaults (ResetToDefaults)");
            Assert.True(section.NeedsInitialization);

            // The player goes on, the marks are counted again, and the game is saved: the newer section must come back untouched.
            section.Tracker.MarkSeen(new[] { 50, 60 });
            section.MarkInitialized();

            JsonObject written = FeaturesOf(model).GetObject(Id);
            Assert.Equal(ModJson.Write(Parse(newer), false), ModJson.Write(written, false), "written back exactly as read, nothing of the newer mod is lost");
        }

        [Test]
        public void ASectionWhenTheFeatureIsOff_PassesThroughUnchanged_AndIsNotGivenToTheHolder()
        {
            var section = Section(null);
            bool on = false;
            SideFileModel model = ModelWith(section, id => on);
            const string stored = "{ \"_schema\": 1, \"seenAvailable\": [4, 5, 6] }";

            model.Apply(FileText("{ \"" + Id + "\": " + stored + " }"));

            Assert.Equal(0, section.Tracker.SeenCount, "the feature is off: nothing is read into it");
            Assert.Equal(ModJson.Write(Parse(stored), false), ModJson.Write(FeaturesOf(model).GetObject(Id), false), "written back as it was");

            // The player switches the feature on in the hub: the held section is given to the holder.
            on = true;
            SideFileReadReport restored = model.RestoreParticipant(Id);
            Assert.Equal(0, restored.Warnings.Count, string.Join("; ", restored.Warnings));
            Assert.Equal(new[] { 4, 5, 6 }, Seen(section));
            Assert.False(section.NeedsInitialization);
        }

        [Test]
        public void ANewerSectionWhenTheFeatureIsOff_IsKeptByTheCoreToo()
        {
            var section = Section(null);
            SideFileModel model = ModelWith(section, id => false);
            const string newer = "{ \"_schema\": 3, \"seenAvailable\": [9], \"extra\": true }";

            model.Apply(FileText("{ \"" + Id + "\": " + newer + " }"));
            section.Tracker.MarkSeen(77);
            section.MarkInitialized();

            Assert.Equal(ModJson.Write(Parse(newer), false), ModJson.Write(FeaturesOf(model).GetObject(Id), false));
        }

        [Test]
        public void AFeatureSwitchedOffAfterASave_KeepsItsLastSection()
        {
            var section = Section(null);
            bool on = true;
            SideFileModel model = ModelWith(section, id => on);
            model.Apply(FileText("{ \"" + Id + "\": { \"_schema\": 1, \"seenAvailable\": [1] } }"));
            section.Tracker.MarkSeen(2);

            string whileOn = ModJson.Write(FeaturesOf(model).GetObject(Id), false);   // a save while the feature is on
            on = false;                                                                  // the player switches it off in the hub
            section.Tracker.MarkSeen(3);                                                 // (must not matter: nobody reads it any more)
            string whileOff = ModJson.Write(FeaturesOf(model).GetObject(Id), false);

            Assert.Equal("{\"_schema\":1,\"seenAvailable\":[1,2]}", whileOn);
            Assert.Equal(whileOn, whileOff, "a feature that is off holds the section it wrote last (NFR-4)");
        }

        [Test]
        public void ASectionThatIsNotAnObject_GivesTheDefaults_WithAWarningOfTheCore()
        {
            var warnings = new List<string>();
            var section = Section(warnings);
            SideFileModel model = ModelWith(section);

            // The section is an array instead of an object: the core reports it and resets the holder; the next save writes a proper section in its place.
            SideFileReadReport report = model.Apply(FileText("{ \"" + Id + "\": [1, 2, 3] }"));

            Assert.True(report.Warnings.Count >= 1, "the core reports it");
            Assert.True(section.NeedsInitialization);
            Assert.Equal(0, section.Tracker.SeenCount);
        }

        [Test]
        public void ADamagedList_InsideTheFile_IsCleanedAndWrittenBackClean()
        {
            var warnings = new List<string>();
            var section = Section(warnings);
            SideFileModel model = ModelWith(section);
            model.Apply(FileText("{ \"" + Id + "\": { \"_schema\": 1, \"seenAvailable\": [5, 5, -2, 200, \"x\", 3] } }"));

            Assert.Equal(new[] { 3, 5 }, Seen(section));
            Assert.True(warnings.Count >= 2, "every kind of damage is reported: " + string.Join("; ", warnings));

            Assert.Equal("{\"_schema\":1,\"seenAvailable\":[3,5]}", ModJson.Write(FeaturesOf(model).GetObject(Id), false), "the next save writes what is left");
        }

        // ---- the feature does not touch the save of the game, and has no version field of its own ----

        [Test]
        public void TheDto_HasTheListOnly()
        {
            var fields = typeof(ResearchUxSave).GetFields().Select(f => f.Name).OrderBy(n => n, StringComparer.Ordinal).ToArray();

            Assert.Equal(new[] { "MaxSeen", "seenAvailable" }, fields, "the DTO is the list (and the limit): no version member (D20)");
        }

        [Test]
        public void TheSources_NeverNameTheVersionField()
        {
            // The version of the section is the core's (SideFileModel.SchemaKey): neither the holder, nor the DTO, nor the participant reads or writes it.
            string src = SourceScan.SrcDirOrFail();
            string[] files =
            {
                Path.Combine(src, "Features", "ResearchTreeUx", "Logic", "UxSaveSection.cs"),
                Path.Combine(src, "Features", "ResearchTreeUx", "Logic", "ResearchUxSave.cs"),
                Path.Combine(src, "Features", "ResearchTreeUx", "ResearchUxSaveParticipant.cs"),
            };

            foreach (string path in files)
            {
                Assert.True(File.Exists(path), "missing " + path);
                string code = SourceScan.Strip(SourceScan.ReadText(path), false);
                Assert.False(code.Contains("_schema"), Path.GetFileName(path) + " names the version field");
                Assert.False(code.Contains("SchemaKey"), Path.GetFileName(path) + " names the version key of the core");
                Assert.False(Regex.IsMatch(code, "\"(?:schema|version|modVersion)\""), Path.GetFileName(path) + " names a version member");
            }
        }

        [Test]
        public void TheFeature_DoesNotWriteToTheSaveOfTheGame_NorAddFieldsToItsClasses()
        {
            // NFR-4: the only file the feature's state goes to is the side file of the core, through the participant.
            var violations = new List<string>();
            string root = Path.Combine(SourceScan.SrcDirOrFail(), "Features", "ResearchTreeUx");
            var forbidden = new Regex(
                @"\busing\s+System\.IO\b|\b(?:File|Directory|StreamWriter|FileStream|BinaryWriter)\s*\.|\bSaveHandler\b|\bPlayerPrefs\b|\bES3\b|\bConditionalWeakTable\b|\bTraverse\b",
                RegexOptions.Compiled);
            int files = 0;
            foreach (string path in SourceScan.Files(root, ".cs"))
            {
                files++;
                string code = SourceScan.Strip(SourceScan.ReadText(path), true);
                foreach (Match match in forbidden.Matches(code))
                    violations.Add(SourceScan.Relative(root, path) + ":" + SourceScan.LineOf(code, match.Index) + ": " + match.Value);
            }

            Assert.True(files > 20, "too few files: " + files);
            Assert.True(violations.Count == 0, "NFR-4:\n  " + string.Join("\n  ", violations));
        }

        [Test]
        public void TheParticipant_IsRegisteredWithTheCore_ThroughTheContext()
        {
            string feature = SourceScan.Strip(SourceScan.ReadText(Path.Combine(SourceScan.SrcDirOrFail(), "Features", "ResearchTreeUx", "ResearchTreeUxFeature.cs")), false);
            string participant = SourceScan.Strip(SourceScan.ReadText(Path.Combine(SourceScan.SrcDirOrFail(), "Features", "ResearchTreeUx", "ResearchUxSaveParticipant.cs")), false);

            Assert.True(Regex.IsMatch(feature, @"ctx\.SaveParticipant\(\s*_save\s*\)"), "ctx.SaveParticipant is the way in");
            Assert.False(feature.Contains("ModSave.Register("), "and the only way");
            Assert.True(Regex.IsMatch(participant, @"class\s+ResearchUxSaveParticipant\s*:\s*UxSaveSection\s*,\s*IModSaveParticipant"), "the game-side class is the holder plus the interface of the core");
        }
    }
}
