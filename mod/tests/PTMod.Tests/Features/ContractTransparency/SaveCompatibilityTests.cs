using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using PTMod.Core;
using PTMod.Core.Logic.Json;
using PTMod.Core.Logic.Save;
using PTMod.Features.ContractTransparency;
using PTMod.Tests.Architecture;
using PTMod.Tests.Common;
using PTMod.Tests.Save;
using LogEntry = PTMod.Features.ContractTransparency.LogEntry;

namespace PTMod.Tests.Features.ContractTransparency
{
    /// <summary>
    /// TSK-021 (NFR-3, NFR-7): the log against the side save file of the core, with whole files as fixtures (<c>Fixtures/*.ptmod.json</c>): a save with no section of the feature, one with the section
    /// of version 1, one with a section of version 99 (from a later mod), one damaged field by field. The core's <see cref="SideFileModel"/> hands the sections out and writes them back, the participant
    /// here does what <c>ContractLog</c> does (the codec and the model; a source test holds the two together), so what is checked is what a player's save meets: a missing section is an empty log, a newer
    /// section stays in the file untouched, fields nobody knows are ignored, and a write never changes the sections of others.
    /// </summary>
    public class SaveCompatibilityTests
    {
        private const string Section = "contract-transparency";

        /// <summary>The holder of the section as <c>ContractLog</c> is: reset, read and write go to the codec and the model; it counts what the core asks of it.</summary>
        private sealed class LogSection : ISideFileParticipant
        {
            public readonly ContractLogModel Model = new ContractLogModel();
            public readonly List<string> Problems = new List<string>();
            public int Resets, Reads, Writes;

            public string SectionId
            {
                get { return ContractLogCodec.SectionId; }
            }

            public int SchemaVersion
            {
                get { return ContractLogCodec.SchemaVersion; }
            }

            public void ResetToDefaults()
            {
                Resets++;
                Model.Reset();
            }

            public void Read(JsonObject section, int schema)
            {
                Reads++;
                ContractLogCodec.Read(Model, section, schema, Model.Rules, default(DateTime), Problems);
            }

            public JsonObject Write()
            {
                Writes++;
                return ContractLogCodec.Write(Model, Problems);
            }
        }

        private sealed class Loaded
        {
            public SideFileModel File;
            public LogSection Log;
            public Part Events;
            public SideFileReadReport Report;
            public JsonObject Original;
        }

        private static void Register(SideFileModel file, params ISideFileParticipant[] participants)
        {
            foreach (ISideFileParticipant participant in participants)
            {
                string problem;
                Assert.True(file.TryRegister(participant, out problem), problem);
            }
        }

        // ---- the fixtures ----

        private static string FixtureText(string name)
        {
            SourceScan.SrcDirOrFail();
            Assert.NotNull(TestEnv.ModDir, "the folder of the mod");
            string path = Path.Combine(TestEnv.ModDir, "tests", "PTMod.Tests", "Features", "ContractTransparency", "Fixtures", name + ".ptmod.json");
            Assert.True(File.Exists(path), "missing fixture " + path);
            return SourceScan.ReadText(path);
        }

        private static JsonObject Parse(string text)
        {
            return SideFileTestKit.Parse(text);
        }

        /// <summary>Loads a fixture the way the core does, into the section of the feature and, as a bystander, the section of another feature.</summary>
        private static Loaded Load(string text, bool prefill = true, Func<string, bool> isActive = null)
        {
            var loaded = new Loaded { File = new SideFileModel(isActive), Log = new LogSection(), Events = new Part("event-system", 2), Original = Parse(text) };
            Register(loaded.File, loaded.Log, loaded.Events);

            if (prefill)
            {
                // What the player had in the session before the load: it must not survive into the loaded save.
                LogEntry old = LogEntry.Create("1985-01-01", LogTypes.OfferSent, LogSources.Manual);
                old.cpuName = "left over";
                loaded.Log.Model.Add(old);
                loaded.Log.Model.CountOfferSent();
                loaded.Log.Resets = 0;
            }

            loaded.Report = loaded.File.Apply(text);
            return loaded;
        }

        private static JsonObject FeaturesOf(JsonObject document)
        {
            return document.GetObject("features");
        }

        private static JsonObject BuildFeatures(SideFileModel file)
        {
            SideFileBuildReport build = file.Build(new SideFileHeader { modVersion = "0.1.0", gameVersion = "0.2.3", gameDate = "1987-01-02" });
            return FeaturesOf(build.Document);
        }

        private static void AssertSame(JsonNode expected, JsonNode actual, string what)
        {
            Assert.True(expected != null && expected.Equals(actual), what + " must be as it was in the file:\n  expected " + expected + "\n  actual   " + actual);
        }

        private static string SaveText(LogSection log)
        {
            return ModJson.Write(ModJson.From(log.Model.ToSave()), false);
        }

        // ---- no section ----

        [Test]
        public void ASaveWithoutTheSection_LoadsAnEmptyLog_WithNoComplaint()
        {
            Loaded loaded = Load(FixtureText("no-section"));

            Assert.Equal(SideFileStatus.Loaded, loaded.Report.Status);
            Assert.Equal(0, loaded.Report.Warnings.Count, string.Join("; ", loaded.Report.Warnings));
            Assert.Equal(1, loaded.Log.Resets, "an active participant without a section gets the defaults");
            Assert.Equal(0, loaded.Log.Reads);
            Assert.Equal(0, loaded.Log.Model.Count);
            Assert.Equal(0, loaded.Log.Model.TenderCount);
            Assert.Equal(0, loaded.Log.Model.OffersSent, "the counters of the session before the load are gone");
            Assert.Equal(0, loaded.Log.Model.TendersWon);
            Assert.Equal(0, loaded.Log.Model.Query(LogFilter.All).Count);
        }

        [Test]
        public void ASaveWithoutTheSection_GetsOneOnTheNextSave_AndTheOthersAreUntouched()
        {
            Loaded loaded = Load(FixtureText("no-section"));
            LogEntry entry = LogEntry.Create("1985-03-02", LogTypes.OfferSent, LogSources.Manual);
            entry.cpuName = "first";
            loaded.Log.Model.Add(entry);

            JsonObject features = BuildFeatures(loaded.File);

            JsonObject section = features.GetObject(Section);
            Assert.NotNull(section, "the section appears");
            Assert.Equal(1, section.GetInt32("_schema", 0), "with the version the core writes");
            Assert.Equal(1, section.GetArray("log").Count);
            Assert.True(section.GetArray("tenders").Count == 0);

            JsonObject original = FeaturesOf(loaded.Original);
            AssertSame(original["mod-framework"], features["mod-framework"], "the section of the core");
            AssertSame(original["gpu-product-line"], features["gpu-product-line"], "the section of a feature this build does not have");
        }

        // ---- version 1 ----

        [Test]
        public void ASaveWithVersionOne_LoadsTheWholeLog_NumbersTextsAndTenders()
        {
            Loaded loaded = Load(FixtureText("schema-1"));
            ContractLogModel model = loaded.Log.Model;

            Assert.Equal(SideFileStatus.Loaded, loaded.Report.Status);
            Assert.Equal(0, loaded.Report.NewerSections.Count);
            Assert.Equal(0, loaded.Report.Warnings.Count, string.Join("; ", loaded.Report.Warnings));
            Assert.Equal(1, loaded.Log.Reads);
            Assert.Equal(0, loaded.Log.Problems.Count, string.Join("; ", loaded.Log.Problems));

            Assert.Equal(12, model.Count);
            Assert.Equal(2, model.TenderCount);
            Assert.Equal(7, model.OffersSent);
            Assert.Equal(1, model.TendersWon);
            Assert.Equal(0, model.PendingTenderCount, "results already in the save have had their letter");

            LogEntry sent = model.Query(LogFilter.Manual).First(e => e.type == LogTypes.OfferSent);
            Assert.Equal("Alpha 286", sent.cpuName);
            Assert.Equal(41, sent.cpuId);
            Assert.Equal("Apple|1986-1|52000|299|1", sent.contractKey);
            Assert.Equal("Apple — Computer, 52 000 шт./мес.", sent.contractLabel);
            Assert.Equal("ct.reason.sentManual", sent.reasonKey);
            Assert.Equal("0.2105", sent.Get(LogDataKeys.Actual));
            Assert.Equal(40, sent.GetInt(LogDataKeys.Chance, -1));

            LogEntry deal = model.Query(LogFilter.Business).Single();
            Assert.Equal("Intel", deal.Get(LogDataKeys.Provider));
            Assert.Equal("Zilog", deal.Get(LogDataKeys.Client));
            Assert.Equal(5, deal.GetInt(LogDataKeys.Years, 0));
            Assert.Equal(20000.0, deal.GetDouble(LogDataKeys.ClientFines, 0));

            LogEntry rejected = model.Query(LogFilter.AutoReject).Single();
            Assert.Equal(5, rejected.GetInt(LogDataKeys.Required, 0));

            Assert.Equal(4, model.Query(LogFilter.Automation).Count(e => e.type == LogTypes.AutomationDecision || e.type == LogTypes.AutomationSummary));

            TenderResultDto lost = model.FindTender("Apple|1986-1|52000|299|1", "1987-01-01");
            Assert.NotNull(lost);
            Assert.Equal(TenderOutcomeClassifier.OutcomeLost, lost.outcome);
            Assert.Equal(40, lost.playerGameChance);
            Assert.Near(0.2105, lost.playerActual, 1e-12);
            Assert.Equal("AMD", lost.winnerCompany);
            Assert.Equal(2, lost.offers.Count);
            Assert.True(lost.offers[1].player);
            Assert.Near(34000.5, lost.offers[1].ips, 1e-9);
            Assert.Equal(1, lost.pruned.Count);
            Assert.Equal("Cyrix", lost.pruned[0].company);
            Assert.Equal(5, lost.pruned[0].required);

            TenderResultDto won = model.FindTender("Sega|1986-2|10000|50|2", "1987-01-01");
            Assert.NotNull(won);
            Assert.Equal(TenderOutcomeClassifier.OutcomeWon, won.outcome);
            Assert.Near(1.0, won.playerActual);
        }

        [Test]
        public void WhatIsLoaded_IsWrittenBackWhole_AndReadsBackTheSame()
        {
            Loaded loaded = Load(FixtureText("schema-1"));
            string before = SaveText(loaded.Log);

            JsonObject features = BuildFeatures(loaded.File);
            JsonObject section = features.GetObject(Section);
            Assert.Equal(1, section.GetInt32("_schema", 0));
            Assert.Equal(12, section.GetArray("log").Count);
            Assert.Equal(2, section.GetArray("tenders").Count);
            Assert.Equal(7, section.GetInt32("offersSent", -1));
            Assert.Equal(1, section.GetInt32("tendersWon", -1));

            // The file as the core writes it, read by a second run of the mod.
            var again = new SideFileModel();
            var second = new LogSection();
            Register(again, second);
            string text = ModJson.Write(new JsonObject().Set("schema", 1).Set("features", features), true);
            SideFileReadReport report = again.Apply(text);

            Assert.Equal(SideFileStatus.Loaded, report.Status);
            Assert.Equal(before, SaveText(second));
            Assert.Equal(0, second.Problems.Count, string.Join("; ", second.Problems));
        }

        // ---- fields nobody knows ----

        [Test]
        public void FieldsNobodyKnows_AreIgnored_AtEveryLevel()
        {
            string text = FixtureText("schema-1");
            Loaded withExtras = Load(text);

            // The same file without the extra fields: the fixture has one in the section (two), in an entry, in a tender, in a row of offers and in a row of withdrawn offers.
            JsonObject root = Parse(text);
            JsonObject section = FeaturesOf(root).GetObject(Section);
            Assert.True(section.Remove("futureCounter") && section.Remove("preferences"), "the fixture holds fields of a later version in the section");
            Assert.True(First(section.GetArray("log")).Remove("colour"), "in a log entry");
            JsonObject tender = First(section.GetArray("tenders"));
            Assert.True(tender.Remove("note"), "in a tender result");
            Assert.True(First(tender.GetArray("offers")).Remove("rank"), "in a row of offers");
            Assert.True(First(tender.GetArray("pruned")).Remove("reason"), "in a row of withdrawn offers");
            Loaded without = Load(ModJson.Write(root, true));

            Assert.Equal(0, withExtras.Log.Problems.Count, "ignored means ignored, not reported: " + string.Join("; ", withExtras.Log.Problems));
            Assert.Equal(0, withExtras.Report.Warnings.Count, string.Join("; ", withExtras.Report.Warnings));
            Assert.Equal(SaveText(without.Log), SaveText(withExtras.Log));
            Assert.Equal(12, withExtras.Log.Model.Count);
        }

        private static JsonObject First(JsonArray array)
        {
            JsonNode node;
            Assert.True(array.TryGetValue(0, out node) && node is JsonObject, "the first element is an object");
            return (JsonObject)node;
        }

        // ---- a section from a later mod ----

        [Test]
        public void ASectionOfVersion99_GivesAnEmptyLog_AndStaysInTheFileUntouched()
        {
            Loaded loaded = Load(FixtureText("schema-99"));

            Assert.Equal(SideFileStatus.Newer, loaded.Report.Status);
            Assert.Equal(new[] { Section }, loaded.Report.NewerSections.ToArray());
            Assert.True(loaded.Report.Warnings.Any(w => w.Contains("newer than supported")), string.Join("; ", loaded.Report.Warnings));
            Assert.Equal(0, loaded.Log.Reads, "the mod does not try to understand a section from the future");
            Assert.Equal(1, loaded.Log.Resets);
            Assert.Equal(0, loaded.Log.Model.Count);
            Assert.Equal(0, loaded.Log.Model.TenderCount);
            Assert.Equal(0, loaded.Log.Model.OffersSent);

            // The player plays on with an empty log; the next save does not overwrite the section of the later mod.
            LogEntry entry = LogEntry.Create("1990-06-02", LogTypes.OfferSent, LogSources.Manual);
            entry.cpuName = "new";
            loaded.Log.Model.Add(entry);

            JsonObject features = BuildFeatures(loaded.File);
            JsonObject original = FeaturesOf(loaded.Original);
            AssertSame(original[Section], features[Section], "the section of version 99");
            Assert.Equal(99, features.GetObject(Section).GetInt32("_schema", 0));
            Assert.Equal(0, loaded.Log.Writes, "the core does not even ask for the data of a locked section");
            AssertSame(original["mod-framework"], features["mod-framework"], "the section of the core");
            AssertSame(original["gpu-product-line"], features["gpu-product-line"], "the section of another feature");
        }

        [Test]
        public void ASectionOfVersion99_SurvivesSeveralSaves_AndALoadOfTheSavedFile()
        {
            Loaded loaded = Load(FixtureText("schema-99"));
            JsonObject original = FeaturesOf(loaded.Original);

            // Save, save again, and load what was saved: the section of the later mod is still the one that was in the file.
            BuildFeatures(loaded.File);
            JsonObject second = BuildFeatures(loaded.File);
            AssertSame(original[Section], second[Section], "after two saves");

            string written = ModJson.Write(new JsonObject().Set("schema", 1).Set("features", second), true);
            Loaded again = Load(written, false);
            Assert.Equal(SideFileStatus.Newer, again.Report.Status);
            AssertSame(original[Section], FeaturesOf(Parse(written))[Section], "in the file text");
            Assert.Equal(0, again.Log.Model.Count);
        }

        // ---- the sections of others ----

        [Test]
        public void ASave_NeverChangesTheSectionsOfOthers_NeitherTheOnesNobodyOwnsNorTheOnesAnotherFeatureWrites()
        {
            Loaded loaded = Load(FixtureText("schema-1"));
            Assert.Equal(5, loaded.Events.Value, "the section of the other feature was read");

            // The player acts a lot: the log changes.
            for (int i = 0; i < 40; i++)
            {
                LogEntry entry = LogEntry.Create("1987-01-0" + (1 + i % 9), LogTypes.OfferSent, LogSources.Manual);
                entry.cpuName = "n" + i;
                loaded.Log.Model.Add(entry);
                loaded.Log.Model.CountOfferSent();
            }

            JsonObject features = BuildFeatures(loaded.File);
            JsonObject original = FeaturesOf(loaded.Original);

            AssertSame(original["mod-framework"], features["mod-framework"], "the section of the core");
            AssertSame(original["gpu-product-line"], features["gpu-product-line"], "a section nobody here owns (big numbers included)");
            AssertSame(original["event-system"], features["event-system"], "the section of the other feature");
            Assert.NotEqual(original[Section].ToString(), features[Section].ToString());
            Assert.Equal(47, features.GetObject(Section).GetInt32("offersSent", -1));
            Assert.Equal(52, features.GetObject(Section).GetArray("log").Count);
        }

        [Test]
        public void WhenTheFeatureIsSwitchedOff_ItsSectionStaysInTheFileAsItWas_UnknownFieldsToo()
        {
            Loaded loaded = Load(FixtureText("schema-1"), true, id => id != Section);

            Assert.Equal(0, loaded.Log.Reads, "an inactive participant is not read");
            JsonObject features = BuildFeatures(loaded.File);
            AssertSame(FeaturesOf(loaded.Original)[Section], features[Section], "the section of the switched-off feature");
            Assert.Equal(0, loaded.Log.Writes);
        }

        [Test]
        public void WhenTheFeatureIsSwitchedOnLater_ItGetsItsSectionFromTheFile()
        {
            Loaded loaded = Load(FixtureText("schema-1"), false, id => id != Section);
            Assert.Equal(0, loaded.Log.Model.Count);

            SideFileReadReport report = loaded.File.RestoreParticipant(Section);

            Assert.Equal(0, report.Warnings.Count, string.Join("; ", report.Warnings));
            Assert.Equal(12, loaded.Log.Model.Count);
            Assert.Equal(7, loaded.Log.Model.OffersSent);
        }

        // ---- damage ----

        [Test]
        public void ADamagedSection_LoadsAsFarAsItGoes_AndTheNextSaveWritesItWhole()
        {
            Loaded loaded = Load(FixtureText("damaged-section"));
            ContractLogModel model = loaded.Log.Model;

            Assert.Equal(SideFileStatus.Loaded, loaded.Report.Status);
            Assert.Equal(2, model.Count, "the two usable lines");
            Assert.Equal(1, model.TenderCount);
            Assert.Equal(0, model.OffersSent, "a negative counter is zero");
            Assert.Equal(0, model.TendersWon, "a counter of the wrong kind keeps its default");
            Assert.True(loaded.Log.Problems.Count >= 5, "every refusal has a line for the log: " + string.Join("; ", loaded.Log.Problems));
            Assert.Equal(5, loaded.Events.Value, "the section of the neighbour is read as usual");

            JsonObject features = BuildFeatures(loaded.File);
            JsonObject section = features.GetObject(Section);
            Assert.Equal(1, section.GetInt32("_schema", 0));
            Assert.Equal(2, section.GetArray("log").Count);
            Assert.False(section.ContainsKey("unknownField"), "what is rewritten is the model, not the damage");
            AssertSame(FeaturesOf(loaded.Original)["mod-framework"], features["mod-framework"], "the section of the core");
        }

        [Test]
        public void ASectionThatIsNotAnObject_IsDefaults_NotACrash()
        {
            string text = FixtureText("schema-1").Replace("\"contract-transparency\": {", "\"contract-transparency\": [], \"ignored-by-the-test\": {");
            Loaded loaded = Load(text);

            Assert.Equal(0, loaded.Log.Model.Count);
            Assert.True(loaded.Report.Warnings.Any(w => w.Contains("not an object")), string.Join("; ", loaded.Report.Warnings));
        }

        [Test]
        public void AFileThatIsNotTheMods_IsDefaultsForTheLog()
        {
            var file = new SideFileModel();
            var log = new LogSection();
            Register(file, log);
            LogEntry old = LogEntry.Create("1985-01-01", LogTypes.OfferSent, LogSources.Manual);
            log.Model.Add(old);

            SideFileReadReport report = file.Apply("this is not json");

            Assert.Equal(SideFileStatus.Corrupt, report.Status);
            Assert.Equal(0, log.Model.Count);
        }

        // ---- through the real writer of the files ----

        [Test]
        public void TheLog_GoesToTheDiskAndBack_ThroughTheFileWriterOfTheCore()
        {
            FakeFileSystem fs = SideFileTestKit.NewFs();
            SideFileIo io = SideFileTestKit.NewIo(fs);

            Loaded loaded = Load(FixtureText("schema-1"));
            SideFileWriteReport written = io.Write(loaded.File, "1987_1_2", new SideFileHeader { modVersion = "0.1.0", gameVersion = "0.2.3", gameDate = "1987-01-02" });
            Assert.True(written.Success, written.Error);

            var model = new SideFileModel();
            var second = new LogSection();
            Register(model, second);
            SideFileReadReport read = io.Read(model, "1987_1_2");

            Assert.Equal(SideFileStatus.Loaded, read.Status);
            Assert.Equal(SaveText(loaded.Log), SaveText(second));
            Assert.Equal(12, second.Model.Count);

            // The sections nobody here owns are in the file as they were.
            JsonObject onDisk = Parse(fs.Text(SideFileTestKit.SidePath("1987_1_2")));
            AssertSame(FeaturesOf(loaded.Original)["gpu-product-line"], FeaturesOf(onDisk)["gpu-product-line"], "a section nobody owns, on the disk");
            AssertSame(FeaturesOf(loaded.Original)["mod-framework"], FeaturesOf(onDisk)["mod-framework"], "the section of the core, on the disk");
        }

        [Test]
        public void AFullLog_StaysUnderTheLimit_InTheFileThatIsWritten()
        {
            FakeFileSystem fs = SideFileTestKit.NewFs();
            SideFileIo io = SideFileTestKit.NewIo(fs);

            var file = new SideFileModel();
            var log = new LogSection();
            Register(file, log);
            for (int i = 0; i < 520; i++)
            {
                LogEntry entry = LogEntry.Create("1986-12-31", LogTypes.AutomationDecision, LogSources.Automation);
                entry.contractKey = "Apple|1986-1|52000|299|" + (i % 3);
                entry.contractLabel = "Apple — Computer, 52 000 шт./мес.";
                entry.cpuId = 4000 + i;
                entry.cpuName = "Alpha 286 mk " + i;
                entry.reasonKey = "ct.reason.margin";
                entry.Put(LogDataKeys.Margin, 0.35).Put(LogDataKeys.Threshold, 0.4).Put(LogDataKeys.Payment, 194.4).Put(LogDataKeys.UnitCost, 126.36).Put(LogDataKeys.Units, 52000);
                log.Model.Add(entry);
            }

            SideFileWriteReport written = io.Write(file, "1987_1_2", new SideFileHeader { modVersion = "0.1.0", gameVersion = "0.2.3", gameDate = "1987-01-02" });
            Assert.True(written.Success, written.Error);

            JsonObject onDisk = Parse(fs.Text(SideFileTestKit.SidePath("1987_1_2")));
            JsonObject section = FeaturesOf(onDisk).GetObject(Section);
            int bytes = Encoding.UTF8.GetByteCount(fs.Text(SideFileTestKit.SidePath("1987_1_2")));

            int kept = section.GetArray("log").Count;
            Assert.True(kept <= 500, "the ring holds at most 500: " + kept);
            Assert.True(kept >= 200, "a log of 520 entries of this size loses at most the oldest ones to the limit: " + kept);
            Assert.True(bytes <= ContractLogCodec.MaxSectionBytes, "the whole file is " + bytes + " bytes, the section alone may take " + ContractLogCodec.MaxSectionBytes);
            Assert.True(log.Problems.Count == 0 || log.Problems.Any(p => p.Contains(ContractLogCodec.SizeLimitMarker)), "the only thing the writer may say is that the log was cut: " + string.Join("; ", log.Problems));
        }

        // ---- the participant of the test is the participant of the game ----

        [Test]
        public void ContractLog_IsTheThinAdapterThisTestStandsFor()
        {
            string path = Path.Combine(SourceScan.SrcDirOrFail(), "Features", "ContractTransparency", "ContractLog.cs");
            string code = SourceScan.Strip(SourceScan.ReadText(path), false);

            Assert.True(Regex.IsMatch(code, @"SectionId\s*\{\s*get\s*\{\s*return\s+ContractLogCodec\.SectionId\s*;"), "the section id is the codec's");
            Assert.True(Regex.IsMatch(code, @"SchemaVersion\s*\{\s*get\s*\{\s*return\s+ContractLogCodec\.SchemaVersion\s*;"), "and so is the version");
            Assert.True(Regex.IsMatch(code, @"void\s+ResetToDefaults\(\)\s*\{\s*_model\.Reset\(\)\s*;"), "reset empties the model");
            Assert.True(Regex.IsMatch(code, @"ContractLogCodec\.Read\(\s*_model\s*,\s*section\s*,\s*schema\s*,\s*_model\.Rules\s*,"), "read goes to the codec with the schema the core read");
            Assert.True(Regex.IsMatch(code, @"ContractLogCodec\.Write\(\s*_model\s*,"), "write goes to the codec");
            Assert.False(code.Contains("_schema") || code.Contains("SchemaKey"), "the version field is the core's business");
            Assert.False(Regex.IsMatch(code, @"\.(Set|Remove)\(\s*""_schema"""), "and the adapter never writes it");

            string feature = SourceScan.Strip(SourceScan.ReadText(Path.Combine(SourceScan.SrcDirOrFail(), "Features", "ContractTransparency", "ContractTransparencyFeature.cs")), false);
            Assert.True(Regex.IsMatch(feature, @"ctx\.SaveParticipant\(\s*_log\s*\)"), "the feature hands the holder to the core");
        }

        [Test]
        public void TheSectionOfTheFixtures_IsTheSectionOfTheFeature_AndTheVersionIsOne()
        {
            Assert.Equal("contract-transparency", ContractLogCodec.SectionId);
            Assert.Equal(1, ContractLogCodec.SchemaVersion);
            foreach (string name in new[] { "schema-1", "schema-99", "damaged-section" })
            {
                Assert.True(FeaturesOf(Parse(FixtureText(name))).ContainsKey(ContractLogCodec.SectionId), name);
            }
            Assert.False(FeaturesOf(Parse(FixtureText("no-section"))).ContainsKey(ContractLogCodec.SectionId));
        }
    }
}
