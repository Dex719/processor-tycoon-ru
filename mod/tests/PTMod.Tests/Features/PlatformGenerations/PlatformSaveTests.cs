using System;
using System.Collections.Generic;
using PTMod.Core;
using PTMod.Core.Logic.Json;
using PTMod.Core.Logic.Save;
using PTMod.Features.PlatformGenerations;

namespace PTMod.Tests.Features.PlatformGenerations
{
    /// <summary>
    /// TSK-001, FR-12, NFR-3: the section "platform-generations" of the side save file. The DTOs of design.md ("Секция ModSave") written with
    /// <c>ModJson.From</c> and read with <c>ModJson.Bind</c>, the empty section, the example of the design round trip, no <c>_schema</c> in what
    /// <c>Write</c> returns, version 1, and the holder driven by the real <c>SideFileModel</c> of the core: reset on a new game before the
    /// fast-forward, read, write, a missing section.
    /// </summary>
    [Covers(typeof(PlatformSaveCodec), typeof(PlatformSaveSection), typeof(PlatformGenerationsSave), typeof(GenerationStateDto), typeof(AdopterDto),
        typeof(SocketIntroDto), typeof(ContractPrefDto))]
    public class PlatformSaveTests
    {
        private const string Id = PlatformTestData.FeatureId;

        // The example of the design (the section without the "_schema" member, which the core strips before Read).
        private const string DesignExample =
            "{ 'generations': [" +
            "    { 'id': 'gen-1990-pga', 'announced': '1990-01-01', 'launched': '1990-02-14', 'launchedBy': 'ai:3'," +
            "      'earlyAdopters': [ { 'company': 'ai:3', 'date': '1990-02-14' }, { 'company': 'player', 'date': '1990-11-02' } ] }," +
            "    { 'id': 'gen-1995-pga', 'announced': '1995-01-01', 'launched': '', 'launchedBy': '', 'earlyAdopters': [] } ]," +
            "  'socketIntros': [" +
            "    { 'company': 'player', 'socket': 'pkg:PGA 1990', 'date': '1990-11-02', 'main': 'pkg:PGA 1985', 'mainAgeMonths': 50, 'severity': 0.0 }," +
            "    { 'company': 'ai:5', 'socket': 'pkg:PGA 1995', 'date': '1995-02-01', 'main': 'pkg:PGA 1990', 'mainAgeMonths': 20, 'severity': 0.444 } ]," +
            "  'contractPrefs': [" +
            "    { 'key': 'Apple|1993-1|52000|299|1', 'gen': 'gen-1990-pga' }," +
            "    { 'key': 'Sony|1993-1|30000|199|2', 'gen': '' } ] }";

        private static JsonObject Parse(string text)
        {
            return PlatformTestData.ParseObject(text);
        }

        // ---- version and the empty section (TSK-001 acceptance) ----

        [Test]
        public void TheSection_HasVersionOne_AndTheIdOfTheFeature()
        {
            Assert.Equal(1, PlatformSaveCodec.SchemaVersion);
            var section = new PlatformSaveSection(Id);
            Assert.Equal(1, section.SchemaVersion);
            Assert.Equal("platform-generations", section.SectionId);
        }

        [Test]
        public void AnEmptySection_IsWrittenAsThreeEmptyLists_WithoutSchema()
        {
            JsonObject written = PlatformSaveCodec.Write(new PlatformGenerationsSave());
            Assert.False(written.ContainsKey("_schema"), "the core writes _schema, not the feature");
            Assert.Equal(3, written.Count);
            Assert.Equal(0, written.GetArray("generations").Count);
            Assert.Equal(0, written.GetArray("socketIntros").Count);
            Assert.Equal(0, written.GetArray("contractPrefs").Count);
        }

        [Test]
        public void AnEmptySection_IsReadBack_AsEmptyLists()
        {
            var problems = new List<string>();
            PlatformGenerationsSave save = PlatformSaveCodec.Read(PlatformSaveCodec.Write(new PlatformGenerationsSave()), problems);
            Assert.Equal(0, problems.Count, string.Join("; ", problems));
            Assert.NotNull(save);
            Assert.Equal(0, save.generations.Count);
            Assert.Equal(0, save.socketIntros.Count);
            Assert.Equal(0, save.contractPrefs.Count);
        }

        [Test]
        public void ANullOrEmptyObjectSection_IsReadAsEmpty()
        {
            var problems = new List<string>();
            PlatformGenerationsSave fromNull = PlatformSaveCodec.Read(null, problems);
            PlatformGenerationsSave fromEmpty = PlatformSaveCodec.Read(new JsonObject(), problems);
            Assert.Equal(0, problems.Count);
            Assert.Equal(0, fromNull.generations.Count);
            Assert.Equal(0, fromEmpty.generations.Count);
            Assert.Equal(0, fromEmpty.socketIntros.Count);
            Assert.Equal(0, fromEmpty.contractPrefs.Count);
        }

        [Test]
        public void WritingNull_GivesAnEmptySection()
        {
            JsonObject written = PlatformSaveCodec.Write(null);
            Assert.Equal(3, written.Count);
            Assert.Equal(0, written.GetArray("generations").Count);
        }

        [Test]
        public void Write_GivesAFreshObjectEveryTime()
        {
            var save = new PlatformGenerationsSave();
            JsonObject first = PlatformSaveCodec.Write(save);
            first.Set("generations", new JsonArray());
            first.Set("extra", 1);
            JsonObject second = PlatformSaveCodec.Write(save);
            Assert.False(second.ContainsKey("extra"));
            Assert.Equal(3, second.Count);
        }

        // ---- the example of the design ----

        [Test]
        public void TheDesignExample_IsReadIntoTheDtos()
        {
            var problems = new List<string>();
            PlatformGenerationsSave save = PlatformSaveCodec.Read(Parse(DesignExample), problems);
            Assert.Equal(0, problems.Count, string.Join("; ", problems));

            Assert.Equal(2, save.generations.Count);
            GenerationStateDto g = save.generations[0];
            Assert.Equal("gen-1990-pga", g.id);
            Assert.Equal("1990-01-01", g.announced);
            Assert.Equal("1990-02-14", g.launched);
            Assert.Equal("ai:3", g.launchedBy);
            Assert.Equal(2, g.earlyAdopters.Count);
            Assert.Equal("player", g.earlyAdopters[1].company);
            Assert.Equal("1990-11-02", g.earlyAdopters[1].date);
            Assert.Equal(string.Empty, save.generations[1].launched);
            Assert.Equal(0, save.generations[1].earlyAdopters.Count);

            Assert.Equal(2, save.socketIntros.Count);
            Assert.Equal("ai:5", save.socketIntros[1].company);
            Assert.Equal("pkg:PGA 1995", save.socketIntros[1].socket);
            Assert.Equal("pkg:PGA 1990", save.socketIntros[1].main);
            Assert.Equal(20, save.socketIntros[1].mainAgeMonths);
            Assert.Near(0.444, save.socketIntros[1].severity, 1e-6);

            Assert.Equal(2, save.contractPrefs.Count);
            Assert.Equal("Apple|1993-1|52000|299|1", save.contractPrefs[0].key);
            Assert.Equal("gen-1990-pga", save.contractPrefs[0].gen);
            Assert.Equal(string.Empty, save.contractPrefs[1].gen);
        }

        [Test]
        public void TheDesignExample_WrittenBack_IsTheSameJson()
        {
            JsonObject original = Parse(DesignExample);
            PlatformGenerationsSave save = PlatformSaveCodec.Read(original, null);
            JsonObject written = PlatformSaveCodec.Write(save);
            Assert.True(original.Equals(written), "expected " + ModJson.Write(original, false) + "\nbut got " + ModJson.Write(written, false));
        }

        [Test]
        public void TheDesignExample_SurvivesTextAndAgain()
        {
            PlatformGenerationsSave save = PlatformSaveCodec.Read(Parse(DesignExample), null);
            string text = ModJson.Write(PlatformSaveCodec.Write(save), true);
            PlatformGenerationsSave again = PlatformSaveCodec.Read(Parse(text), null);
            Assert.True(PlatformSaveCodec.Write(again).Equals(PlatformSaveCodec.Write(save)));
        }

        // ---- being tolerant ----

        [Test]
        public void Write_NeverReturnsSchema_EvenWhenTheStoredSectionHadOne()
        {
            JsonObject stored = Parse("{ '_schema': 7, 'generations': [] }");
            PlatformGenerationsSave save = PlatformSaveCodec.Read(stored, null);
            Assert.False(PlatformSaveCodec.Write(save).ContainsKey("_schema"));
        }

        [Test]
        public void UnknownMembers_AreIgnoredWhenReading()
        {
            var problems = new List<string>();
            PlatformGenerationsSave save = PlatformSaveCodec.Read(
                Parse("{ 'future': [1, 2], 'generations': [ { 'id': 'g', 'nextVersionField': true } ] }"), problems);
            Assert.Equal(1, save.generations.Count);
            Assert.Equal("g", save.generations[0].id);
            Assert.Equal(0, problems.Count, string.Join("; ", problems));
        }

        [Test]
        public void ARecordOfAnUnknownGeneration_IsKeptAsItIs_FR12Item6()
        {
            PlatformGenerationsSave save = PlatformSaveCodec.Read(
                Parse("{ 'generations': [ { 'id': 'gen-from-a-removed-override', 'announced': '2001-01-01', 'launched': '2001-05-05', 'launchedBy': 'ai:9'," +
                      " 'earlyAdopters': [ { 'company': 'ai:9', 'date': '2001-05-05' } ] } ] }"), null);
            JsonObject written = PlatformSaveCodec.Write(save);
            GenerationStateDto kept = PlatformSaveCodec.Read(written, null).generations[0];
            Assert.Equal("gen-from-a-removed-override", kept.id);
            Assert.Equal("2001-05-05", kept.launched);
            Assert.Equal("ai:9", kept.earlyAdopters[0].company);
        }

        [Test]
        public void NullEntriesOfTheLists_AreDropped()
        {
            PlatformGenerationsSave save = PlatformSaveCodec.Read(
                Parse("{ 'generations': [ null, { 'id': 'g', 'earlyAdopters': [ null, { 'company': 'player', 'date': '2000-01-01' } ] } ]," +
                      " 'socketIntros': [ null ], 'contractPrefs': [ null, { 'key': 'k', 'gen': '' } ] }"), null);
            Assert.Equal(1, save.generations.Count);
            Assert.Equal(1, save.generations[0].earlyAdopters.Count);
            Assert.Equal(0, save.socketIntros.Count);
            Assert.Equal(1, save.contractPrefs.Count);
        }

        [Test]
        public void ANullList_IsReadAsAnEmptyList()
        {
            PlatformGenerationsSave save = PlatformSaveCodec.Read(Parse("{ 'generations': null, 'socketIntros': null, 'contractPrefs': null }"), null);
            Assert.NotNull(save.generations);
            Assert.NotNull(save.socketIntros);
            Assert.NotNull(save.contractPrefs);
            Assert.Equal(0, save.generations.Count);
        }

        [Test]
        public void AMemberOfTheWrongType_IsReported_AndTheRestIsRead()
        {
            var problems = new List<string>();
            PlatformGenerationsSave save = PlatformSaveCodec.Read(
                Parse("{ 'generations': 'many', 'socketIntros': [ { 'company': 'player', 'socket': 'pkg:PGA 1990', 'mainAgeMonths': 'old', 'severity': 0.5 } ]," +
                      " 'contractPrefs': [ { 'key': 'k', 'gen': 'g' } ] }"), problems);
            Assert.True(problems.Count >= 2, string.Join("; ", problems));
            Assert.NotNull(save.generations);
            Assert.Equal(0, save.generations.Count);
            Assert.Equal(1, save.socketIntros.Count);
            Assert.Equal("player", save.socketIntros[0].company);
            Assert.Equal(0, save.socketIntros[0].mainAgeMonths, "the bad member keeps its default");
            Assert.Near(0.5, save.socketIntros[0].severity, 1e-6);
            Assert.Equal(1, save.contractPrefs.Count);
        }

        [Test]
        public void TheValuesAreNotJudgedByTheCodec_DatesAndKeysPassAsTheyAre()
        {
            // The checks of dates, companies and ranges belong to the chronology that is built from the section (design.md, "Validation Rules").
            PlatformGenerationsSave save = PlatformSaveCodec.Read(
                Parse("{ 'socketIntros': [ { 'company': 'nobody', 'socket': 'x', 'date': '2999-99-99', 'main': '', 'mainAgeMonths': -5, 'severity': 7.5 } ] }"), null);
            SocketIntroDto intro = save.socketIntros[0];
            Assert.Equal("nobody", intro.company);
            Assert.Equal("2999-99-99", intro.date);
            Assert.Equal(-5, intro.mainAgeMonths);
            Assert.Near(7.5, intro.severity, 1e-6);
        }

        // ---- the holder with the real save model of the core ----

        private static SideFileModel ModelWith(PlatformSaveSection section)
        {
            var model = new SideFileModel();
            string problem;
            Assert.True(model.TryRegister(section, out problem), problem);
            return model;
        }

        private static string FileWith(string features)
        {
            return "{\"schema\":1,\"modVersion\":\"0.1.0\",\"features\":{" + features + "}}";
        }

        [Test]
        public void AFreshHolder_IsEmpty_AndNeedsReconstruction()
        {
            var section = new PlatformSaveSection(Id);
            Assert.NotNull(section.Data);
            Assert.Equal(0, section.Data.generations.Count);
            Assert.True(section.NeedsReconstruction);
        }

        [Test]
        public void ANewGame_ResetsTheSection_BeforeTheFastForward()
        {
            var section = new PlatformSaveSection(Id);
            SideFileModel model = ModelWith(section);

            // A game in play: the section holds data and does not need reconstruction.
            section.Data.generations.Add(new GenerationStateDto { id = "gen-2000", announced = "2000-01-01" });
            section.NeedsReconstruction = false;

            SideFileReadReport report = model.StartNewGame();   // ModSave.PrepareNewGame, from the prefix of GameManager.StartNewGame
            Assert.Equal(SideFileStatus.NewGame, report.Status);
            Assert.Equal(0, section.Data.generations.Count, "the state of the old game is gone");
            Assert.Equal(0, section.Data.socketIntros.Count);
            Assert.Equal(0, section.Data.contractPrefs.Count);
            Assert.True(section.NeedsReconstruction, "the chronology is rebuilt when the fast-forward ends");
        }

        [Test]
        public void ReadingTheFile_HandsTheSectionToTheHolder_WithoutSchema()
        {
            var section = new PlatformSaveSection(Id);
            SideFileModel model = ModelWith(section);

            string text = FileWith("\"platform-generations\":{\"_schema\":1," + DesignExample.Substring(1).Replace('\'', '"'));
            SideFileReadReport report = model.Apply(text);

            Assert.Equal(SideFileStatus.Loaded, report.Status);
            Assert.Equal(0, report.Warnings.Count, string.Join("; ", report.Warnings));
            Assert.Equal(2, section.Data.generations.Count);
            Assert.Equal("gen-1990-pga", section.Data.generations[0].id);
            Assert.Equal(2, section.Data.contractPrefs.Count);
            Assert.False(section.NeedsReconstruction, "a section was read: nothing to reconstruct");
        }

        [Test]
        public void ASaveWithoutTheSection_ResetsTheHolder_AndAsksForReconstruction()
        {
            var section = new PlatformSaveSection(Id);
            SideFileModel model = ModelWith(section);
            section.Data.generations.Add(new GenerationStateDto { id = "stale" });
            section.NeedsReconstruction = false;

            SideFileReadReport report = model.Apply(FileWith("\"some-other-feature\":{\"_schema\":1}"));   // FR-12 item 4: a save from before the mod's feature
            Assert.Equal(SideFileStatus.Loaded, report.Status);
            Assert.Equal(0, section.Data.generations.Count);
            Assert.True(section.NeedsReconstruction);

            SideFileReadReport missing = model.ApplyMissing();   // no side file at all (a vanilla or old save)
            Assert.Equal(SideFileStatus.Missing, missing.Status);
            Assert.True(section.NeedsReconstruction);
        }

        [Test]
        public void ASectionNewerThanTheBuild_GivesDefaults_AndTheHolderAsksForReconstruction()
        {
            var section = new PlatformSaveSection(Id);
            SideFileModel model = ModelWith(section);

            SideFileReadReport report = model.Apply(FileWith("\"platform-generations\":{\"_schema\":2,\"generations\":[{\"id\":\"x\"}]}"));
            Assert.Equal(SideFileStatus.Newer, report.Status);
            Assert.Equal(0, section.Data.generations.Count, "the newer section is not read");
            Assert.True(section.NeedsReconstruction);
        }

        [Test]
        public void TheCoreWritesTheSchema_AndTheHolderGetsItBackWithoutOne()
        {
            var section = new PlatformSaveSection(Id);
            SideFileModel model = ModelWith(section);
            section.Data.generations.Add(new GenerationStateDto { id = "gen-2000", announced = "2000-01-01", launched = "2000-04-01", launchedBy = "player" });
            section.Data.generations[0].earlyAdopters.Add(new AdopterDto { company = "player", date = "2000-04-01" });
            section.Data.socketIntros.Add(new SocketIntroDto { company = "player", socket = "pkg:LGA 2000", date = "2000-04-01", main = "pkg:PGA 1995", mainAgeMonths = 30, severity = 0.1667f });
            section.Data.contractPrefs.Add(new ContractPrefDto { key = "Dell|2002-1|1000|100|1", gen = "gen-2000" });

            SideFileBuildReport build = model.Build(null);
            Assert.Equal(0, build.Warnings.Count, "no warning about a _schema the feature returned: " + string.Join("; ", build.Warnings));
            JsonObject features = build.Document.GetObject("features");
            JsonObject stored = features.GetObject(Id);
            Assert.NotNull(stored, "the section is in the file");
            Assert.Equal(1, stored.GetInt32("_schema", -1), "the core wrote _schema 1");
            Assert.Equal(1, stored.GetArray("generations").Count);

            // Load it into a second holder through the text of the file, as the game would after a restart.
            var second = new PlatformSaveSection(Id);
            SideFileModel secondModel = ModelWith(second);
            SideFileReadReport read = secondModel.Apply(ModJson.Write(build.Document, false));
            Assert.Equal(SideFileStatus.Loaded, read.Status);
            Assert.Equal(0, read.Warnings.Count, string.Join("; ", read.Warnings));
            Assert.False(second.NeedsReconstruction);
            Assert.True(PlatformSaveCodec.Write(second.Data).Equals(PlatformSaveCodec.Write(section.Data)), "the same section came back");
            Assert.Equal("Dell|2002-1|1000|100|1", second.Data.contractPrefs[0].key);
            Assert.Near(0.1667, second.Data.socketIntros[0].severity, 1e-6);
        }

        [Test]
        public void TheWarnings_OfAReadAreHandedToTheOwner()
        {
            var lines = new List<string>();
            var section = new PlatformSaveSection(Id, lines.Add);
            section.Read(Parse("{ 'generations': 'many' }"), 1);
            Assert.True(lines.Count >= 1, "a line about 'generations'");
            Assert.True(PlatformTestData.AnyContains(lines, "generations"), string.Join("; ", lines));
            Assert.Equal(0, section.Data.generations.Count);
            Assert.False(section.NeedsReconstruction);
        }

        [Test]
        public void ResetToDefaults_ReplacesTheDataObject()
        {
            var section = new PlatformSaveSection(Id);
            PlatformGenerationsSave before = section.Data;
            section.Data.generations.Add(new GenerationStateDto { id = "g" });
            section.ResetToDefaults();
            Assert.False(ReferenceEquals(before, section.Data), "whoever kept the old DTO does not see the new game's state");
            Assert.Equal(0, section.Data.generations.Count);
        }
    }
}
