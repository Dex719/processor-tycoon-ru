using System.Collections.Generic;
using PTMod.Core;
using PTMod.Core.Logic.Json;
using PTMod.Core.Logic.Save;
using PTMod.Features.BankruptcyRecovery.Logic;

namespace PTMod.Tests.Features.BankruptcyRecovery
{
    /// <summary>
    /// TSK-001 (FR-12, NFR-3): the section "bankruptcy-recovery" of the side save file. An empty section is written and read back, a state with
    /// everything in it survives the trip through the core's own <c>SideFileModel</c>, a save without the section gives the state of a new game,
    /// the example of design.md is read, the section of another feature is not lost. The version field <c>_schema</c> belongs to the core: the DTO has
    /// none and the section never returns one.
    /// </summary>
    [Covers(typeof(RecoverySaveSection), typeof(RecoveryState), typeof(CreditLineData), typeof(LoanData), typeof(InvestorShareData), typeof(EpisodeData),
        typeof(JournalEntryData))]
    public class RecoverySaveSectionTests
    {
        private const string Id = RecoveryTestData.FeatureId;

        // The example of design.md ("Пример секции бокового файла"), as the core writes the file (the core adds _schema; the example has none).
        private const string DesignExample = @"{
  ""schema"": 1,
  ""modVersion"": ""0.1.0"",
  ""features"": {
    ""bankruptcy-recovery"": {
      ""creditLine"": { ""state"": ""open"", ""opensOn"": ""1975-01-01"", ""closesOn"": ""1978-01-01"",
                      ""drawn"": 2660000.0, ""autoDraw"": true, ""closedReason"": """" },
      ""loans"": [
        { ""id"": ""bridge-1"", ""kind"": ""Bridge"", ""principal"": 8400000.0, ""outstanding"": 6527277.0,
          ""annualRate"": 0.30, ""monthsTotal"": 12, ""monthsLeft"": 9, ""payment"": 818892.0,
          ""openedOn"": ""1976-03-01"" }
      ],
      ""bailoutsUsed"": 0,
      ""investorShare"": { ""active"": false, ""amount"": 0.0, ""share"": 0.0, ""monthsLeft"": 0, ""paid"": 0.0, ""cap"": 0.0 },
      ""episode"": { ""preWarningSent"": true, ""defaultMailSent"": true, ""recoveryStreak"": 0, ""bailoutDeclined"": false },
      ""lastBridgeClosedOn"": """",
      ""nextLoanSeq"": 2,
      ""journal"": [
        { ""date"": ""1976-03-01"", ""op"": ""bridge.open"", ""amount"": 8400000.0, ""details"": ""12m @ 30%"" },
        { ""date"": ""1976-03-01"", ""op"": ""bridge.fee"", ""amount"": -420000.0, ""details"": """" },
        { ""date"": ""1976-04-01"", ""op"": ""loan.payment"", ""amount"": -818892.0, ""details"": ""bridge-1"" }
      ]
    }
  }
}";

        private static void AssertSameState(RecoveryState expected, RecoveryState actual, string message = null)
        {
            JsonNode a = ModJson.From(expected);
            JsonNode b = ModJson.From(actual);
            Assert.True(a.Equals(b), (message ?? "the states differ") + "\nexpected " + a + "\nactual   " + b);
        }

        private static SideFileModel NewModel(RecoverySaveSection section)
        {
            var model = new SideFileModel();
            string problem;
            Assert.True(model.TryRegister(section, out problem), "the section must register: " + problem);
            return model;
        }

        private static string TextOf(SideFileModel model)
        {
            SideFileBuildReport report = model.Build(new SideFileHeader { modVersion = "0.1.0", gameDate = "1976-04-01" });
            Assert.Equal(0, report.Warnings.Count, "building the file must not warn: " + string.Join("; ", report.Warnings));
            return ModJson.Write(report.Document, true);
        }

        // ---- the holder ----

        [Test]
        public void ANewSection_HoldsTheStateOfANewGame()
        {
            var section = new RecoverySaveSection(Id);
            Assert.Equal(Id, section.SectionId);
            Assert.Equal(1, section.SchemaVersion);
            AssertSameState(new RecoveryState(), section.State);
            Assert.Equal(CreditLineData.StateNone, section.State.creditLine.state);
            Assert.Equal(0, section.State.loans.Count);
            Assert.Equal(1, section.State.nextLoanSeq);
        }

        [Test]
        public void ResetToDefaults_ReplacesWhatWasRead()
        {
            var section = new RecoverySaveSection(Id);
            section.Read(RecoveryStateCodec.Write(RecoveryTestData.PopulatedState()), 1);
            Assert.Equal(2, section.State.loans.Count);

            RecoveryState before = section.State;
            section.ResetToDefaults();

            AssertSameState(new RecoveryState(), section.State);
            Assert.Equal(2, before.loans.Count, "the old object is not cleared under the one who still holds it");
        }

        // ---- an empty section ----

        [Test]
        public void AnEmptySection_IsWrittenWithoutAVersionField_AndReadBack()
        {
            var section = new RecoverySaveSection(Id);

            JsonObject written = section.Write();

            Assert.NotNull(written, "a section with the defaults is written too");
            Assert.False(written.ContainsKey(SideFileModel.SchemaKey), "the version of the section belongs to the core");
            foreach (string member in new[] { "creditLine", "loans", "bailoutsUsed", "investorShare", "episode", "lastBridgeClosedOn", "nextLoanSeq", "journal" })
                Assert.True(written.ContainsKey(member), "the section lacks " + member);
            Assert.Equal(8, written.Count, "the section has these members and no others");

            var other = new RecoverySaveSection(Id);
            other.Read(written, 1);
            AssertSameState(new RecoveryState(), other.State);
        }

        [Test]
        public void EverySectionWrite_IsAFreshObject()
        {
            var section = new RecoverySaveSection(Id);
            JsonObject first = section.Write();
            JsonObject second = section.Write();

            Assert.False(ReferenceEquals(first, second));
            Assert.True(first.Equals(second));
            first.Set("bailoutsUsed", 99);
            Assert.Equal(0, section.Write().GetInt32("bailoutsUsed"), "changing the written object must not change the state");
        }

        // ---- a full state ----

        [Test]
        public void AFullState_SurvivesTheTrip()
        {
            RecoveryState original = RecoveryTestData.PopulatedState();
            var writer = new RecoverySaveSection(Id);
            writer.Read(RecoveryStateCodec.Write(original), 1);

            JsonObject written = writer.Write();
            var reader = new RecoverySaveSection(Id);
            reader.Read(written, 1);

            AssertSameState(original, reader.State);
            RecoveryState s = reader.State;
            Assert.Equal(CreditLineData.StateOpen, s.creditLine.state);
            Assert.Equal(2660000.0, s.creditLine.drawn);
            Assert.False(s.creditLine.autoDraw);
            Assert.Equal(2, s.loans.Count);
            Assert.Equal("rescue-2", s.loans[1].id);
            Assert.Equal(36075000.0, s.loans[1].principal);
            Assert.Equal(1, s.bailoutsUsed);
            Assert.True(s.investorShare.active);
            Assert.Equal(58, s.investorShare.monthsLeft);
            Assert.True(s.episode.preWarningSent);
            Assert.Equal(3, s.episode.recoveryStreak);
            Assert.Equal("1974-02-01", s.lastBridgeClosedOn);
            Assert.Equal(3, s.nextLoanSeq);
            Assert.Equal(3, s.journal.Count);
            Assert.Equal(-420000.0, s.journal[1].amount);
        }

        [Test]
        public void TheStateGoesThroughTheCoresSideFile_AndComesBack()
        {
            RecoveryState original = RecoveryTestData.PopulatedState();
            var section = new RecoverySaveSection(Id);
            section.Read(RecoveryStateCodec.Write(original), 1);

            string text = TextOf(NewModel(section));

            JsonObject document = RecoveryTestData.ParseObject(text);
            JsonObject features = document.GetObject("features");
            JsonObject stored = features.GetObject(Id);
            Assert.NotNull(stored, "the file must hold the section");
            Assert.Equal(1, stored.GetInt32(SideFileModel.SchemaKey, -1), "the core adds the version of the section");

            var lines = new List<string>();
            var restored = new RecoverySaveSection(Id, lines.Add);
            SideFileReadReport report = NewModel(restored).Apply(text);

            Assert.Equal(SideFileStatus.Loaded, report.Status);
            Assert.Equal(0, lines.Count, "reading a file the section wrote must not warn: " + string.Join("; ", lines));
            AssertSameState(original, restored.State);
        }

        [Test]
        public void ASaveWithoutTheSection_GivesTheStateOfANewGame()
        {
            var section = new RecoverySaveSection(Id);
            section.Read(RecoveryStateCodec.Write(RecoveryTestData.PopulatedState()), 1);
            SideFileModel model = NewModel(section);

            model.ApplyMissing();
            AssertSameState(new RecoveryState(), section.State, "no side file");

            section.Read(RecoveryStateCodec.Write(RecoveryTestData.PopulatedState()), 1);
            model.Apply(@"{ ""schema"": 1, ""features"": { } }");
            AssertSameState(new RecoveryState(), section.State, "a file without the section");

            section.Read(RecoveryStateCodec.Write(RecoveryTestData.PopulatedState()), 1);
            model.StartNewGame();
            AssertSameState(new RecoveryState(), section.State, "a new game");
        }

        [Test]
        public void ASectionNewerThanTheMod_IsKept_AndTheStateIsTheDefault()
        {
            // the core's rule (NFR-3), seen from this section: the newer data is neither read nor lost
            string newer = @"{ ""schema"": 1, ""features"": { """ + Id + @""": { ""_schema"": 2, ""creditLine"": { ""state"": ""open"" }, ""future"": [1, 2] } } }";
            var section = new RecoverySaveSection(Id);
            section.Read(RecoveryStateCodec.Write(RecoveryTestData.PopulatedState()), 1);
            SideFileModel model = NewModel(section);

            SideFileReadReport report = model.Apply(newer);

            Assert.Equal(SideFileStatus.Newer, report.Status);
            AssertSameState(new RecoveryState(), section.State);
            JsonObject kept = model.Build(new SideFileHeader()).Document.GetObject("features").GetObject(Id);
            Assert.Equal(2, kept.GetInt32(SideFileModel.SchemaKey, -1), "the newer section is written back as it was");
            Assert.True(kept.ContainsKey("future"));
        }

        // ---- the example of the design ----

        [Test]
        public void TheExampleOfTheDesign_IsRead()
        {
            var lines = new List<string>();
            var section = new RecoverySaveSection(Id, lines.Add);

            SideFileReadReport report = NewModel(section).Apply(DesignExample);

            Assert.Equal(0, lines.Count, "the example must read without problems: " + string.Join("; ", lines));
            Assert.Equal(SideFileStatus.Loaded, report.Status);

            RecoveryState s = section.State;
            Assert.Equal(CreditLineData.StateOpen, s.creditLine.state);
            Assert.Equal("1975-01-01", s.creditLine.opensOn);
            Assert.Equal("1978-01-01", s.creditLine.closesOn);
            Assert.Equal(2660000.0, s.creditLine.drawn);
            Assert.True(s.creditLine.autoDraw);

            Assert.Equal(1, s.loans.Count);
            LoanData loan = s.loans[0];
            Assert.Equal("bridge-1", loan.id);
            Assert.Equal(LoanData.KindBridge, loan.kind);
            Assert.Equal(8400000.0, loan.principal);
            Assert.Equal(6527277.0, loan.outstanding);
            Assert.Equal(0.30, loan.annualRate);
            Assert.Equal(12, loan.monthsTotal);
            Assert.Equal(9, loan.monthsLeft);
            Assert.Equal(818892.0, loan.payment);
            Assert.Equal("1976-03-01", loan.openedOn);

            Assert.Equal(0, s.bailoutsUsed);
            Assert.False(s.investorShare.active);
            Assert.True(s.episode.preWarningSent);
            Assert.True(s.episode.defaultMailSent);
            Assert.Equal("", s.lastBridgeClosedOn);
            Assert.Equal(2, s.nextLoanSeq);

            Assert.Equal(3, s.journal.Count);
            Assert.Equal("bridge.open", s.journal[0].op);
            Assert.Equal(8400000.0, s.journal[0].amount);
            Assert.Equal("12m @ 30%", s.journal[0].details);
            Assert.Equal(-420000.0, s.journal[1].amount);
            Assert.Equal("bridge-1", s.journal[2].details);
        }

        // ---- other sections ----

        [Test]
        public void TheSectionOfAnotherFeature_IsNotLost()
        {
            string text = @"{ ""schema"": 1, ""features"": {
                ""gpu-product-line"": { ""_schema"": 3, ""lines"": [""a"", { ""b"": [1, 2, 3] }], ""ratio"": 0.5 },
                """ + Id + @""": { ""bailoutsUsed"": 1, ""nextLoanSeq"": 4 } } }";
            var section = new RecoverySaveSection(Id);
            SideFileModel model = NewModel(section);

            model.Apply(text);
            Assert.Equal(1, section.State.bailoutsUsed);
            Assert.Equal(4, section.State.nextLoanSeq);

            JsonObject features = model.Build(new SideFileHeader()).Document.GetObject("features");
            JsonObject gpu = features.GetObject("gpu-product-line");
            Assert.NotNull(gpu, "the section of a feature this build does not hold is written back");
            Assert.Equal(3, gpu.GetInt32(SideFileModel.SchemaKey, -1));
            Assert.Equal(0.5, gpu.GetDouble("ratio"));
            Assert.Equal(1, features.GetObject(Id).GetInt32("bailoutsUsed"));
        }

        // ---- a section with damage ----

        [Test]
        public void ADamagedLoan_IsDroppedAndTheRestOfTheSectionIsKept_WithAWarning()
        {
            var lines = new List<string>();
            var section = new RecoverySaveSection(Id, lines.Add);
            JsonObject stored = RecoveryStateCodec.Write(RecoveryTestData.PopulatedState());
            stored.GetArray("loans").Add(RecoveryTestData.ParseObject(
                @"{ ""id"": ""term-3"", ""kind"": ""Term"", ""principal"": 1000.0, ""outstanding"": -5.0, ""annualRate"": 0.1, ""monthsTotal"": 6, ""monthsLeft"": 6, ""payment"": 10.0, ""openedOn"": ""1977-01-01"" }"));

            section.Read(stored, 1);

            Assert.Equal(2, section.State.loans.Count, "the damaged third loan is dropped, the two good ones stay");
            Assert.Equal(1, lines.Count, string.Join("; ", lines));
            Assert.True(lines[0].StartsWith("loans[2] (term-3): outstanding", System.StringComparison.Ordinal), lines[0]);
            Assert.Equal(1, section.State.bailoutsUsed, "the rest of the section is read");
            Assert.Equal(2660000.0, section.State.creditLine.drawn);
        }
    }
}
