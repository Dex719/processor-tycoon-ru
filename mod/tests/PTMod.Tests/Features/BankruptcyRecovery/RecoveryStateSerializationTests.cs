using System;
using System.Collections.Generic;
using System.IO;
using PTMod.Core;
using PTMod.Core.Logic.Json;
using PTMod.Core.Logic.Save;
using PTMod.Features.BankruptcyRecovery.Logic;
using PTMod.Tests.Architecture;

namespace PTMod.Tests.Features.BankruptcyRecovery
{
    /// <summary>
    /// TSK-017 (NFR-3, NFR-7, FR-12): the section of the side save file as a whole. The state goes to a <c>JsonObject</c> and back without loss and without a field
    /// of version (the core owns <c>_schema</c>); a save without the section gives the state of a new game; the three states of the credit line are read and
    /// written; a broken date or sum drops its record and says so in one line for <c>ModLog</c>; the example side file of the design (<c>Data/sample.ptmod.json</c>)
    /// is read; the section of another feature in the same file comes out as it went in (through the core's own <c>SideFileModel</c>, which <c>ModSave</c> is made of);
    /// and a state a settlement has left is the same eight members and passes every check the next read applies. The cases of <c>_schema</c> (no field, newer
    /// than the mod) are the core's (mod-framework TSK-016) and are not repeated here beyond the one that shows the section is left alone.
    /// </summary>
    [Covers(typeof(RecoverySaveSection), typeof(RecoveryStateCodec), typeof(RecoveryState), typeof(CreditLineData), typeof(LoanData), typeof(InvestorShareData),
        typeof(EpisodeData), typeof(JournalEntryData))]
    public class RecoveryStateSerializationTests
    {
        private const string Id = RecoveryTestData.FeatureId;
        private const string Other = "rival-acquisitions";

        // The section of another feature: keeps what it is given and gives it back.
        private sealed class ForeignParticipant : ISideFileParticipant
        {
            public JsonObject Data;
            public int Resets;
            public int Reads;

            public string SectionId
            {
                get { return Other; }
            }

            public int SchemaVersion
            {
                get { return 3; }
            }

            public void ResetToDefaults()
            {
                Data = null;
                Resets++;
            }

            public void Read(JsonObject section, int schema)
            {
                Data = (JsonObject)section.DeepClone();
                Reads++;
            }

            public JsonObject Write()
            {
                return Data == null ? null : (JsonObject)Data.DeepClone();
            }
        }

        private static string SampleText()
        {
            string path = Path.Combine(TestEnv.ModDir, "tests", "PTMod.Tests", "Features", "BankruptcyRecovery", "Data", "sample.ptmod.json");
            Assert.True(File.Exists(path), "the sample side file is missing: " + path);
            string text = SourceScan.ReadText(path);
            Assert.True(text.Length > 500, "the sample side file is empty");
            return text;
        }

        private static JsonObject SampleDocument()
        {
            return RecoveryTestData.ParseObject(SampleText());
        }

        private static SideFileModel Model(RecoverySaveSection section, ISideFileParticipant other = null)
        {
            var model = new SideFileModel();
            string problem;
            Assert.True(model.TryRegister(section, out problem), problem);
            if (other != null) Assert.True(model.TryRegister(other, out problem), problem);
            return model;
        }

        private static JsonObject SectionOf(JsonObject document, string id)
        {
            JsonObject section = document.GetObject("features").GetObject(id);
            Assert.NotNull(section, "no section " + id);
            return section;
        }

        private static JsonObject Item(JsonArray array, int index)
        {
            return (JsonObject)array[index];
        }

        private static string StateJson(RecoveryState state)
        {
            return ModJson.Write(ModJson.From(state), false);
        }

        // The members of the section and nothing else: the version is the core's, not a member.
        private static readonly string[] Members =
            { "creditLine", "loans", "bailoutsUsed", "investorShare", "episode", "lastBridgeClosedOn", "nextLoanSeq", "journal" };

        // ---- the example of the design ----

        [Test]
        public void TheSampleSideFile_IsRead_WithoutAProblem()
        {
            var lines = new List<string>();
            var section = new RecoverySaveSection(Id, lines.Add);
            var other = new ForeignParticipant();
            SideFileModel model = Model(section, other);

            SideFileReadReport report = model.Apply(SampleText());

            Assert.Equal(SideFileStatus.Loaded, report.Status);
            Assert.Equal(0, report.Warnings.Count, "the core found nothing to say: " + string.Join("; ", report.Warnings));
            Assert.Equal(0, lines.Count, "the section found nothing to say: " + string.Join("; ", lines));
            Assert.Equal("0.1.0", model.Header.modVersion);
            Assert.Equal("1976_4_1", model.Header.saveName);

            RecoveryState s = section.State;
            Assert.Equal(CreditLineData.StateOpen, s.creditLine.state);
            Assert.Near(2660000.0, s.creditLine.drawn, 0);
            Assert.Equal("1978-01-01", s.creditLine.closesOn);
            Assert.Equal(1, s.loans.Count);
            Assert.Equal("bridge-1", s.loans[0].id);
            Assert.Near(6527277.0, s.loans[0].outstanding, 0);
            Assert.Equal(9, s.loans[0].monthsLeft);
            Assert.True(s.episode.preWarningSent && s.episode.defaultMailSent);
            Assert.Equal(2, s.nextLoanSeq);
            Assert.Equal(3, s.journal.Count);
            Assert.Equal("loan.payment", s.journal[2].op);
        }

        [Test]
        public void TheSampleSideFile_GoesBackOut_AsItCameIn()
        {
            JsonObject original = SampleDocument();
            var section = new RecoverySaveSection(Id);
            var other = new ForeignParticipant();
            SideFileModel model = Model(section, other);
            model.Apply(SampleText());

            JsonObject written = model.Build(new SideFileHeader { modVersion = "0.1.0" }).Document;

            Assert.True(SectionOf(original, Id).Equals(SectionOf(written, Id)), "the section is written as it was read:\n" + SectionOf(written, Id));
            Assert.True(SectionOf(original, Other).Equals(SectionOf(written, Other)), "the section of the other feature is untouched");
            Assert.Equal(1, SectionOf(written, Id).GetInt32(SideFileModel.SchemaKey, -1), "the version of the section is written by the core");
        }

        [Test]
        public void TheSampleSideFile_IsHeldWholeWhenNobodyOwnsTheOtherSection()
        {
            // the other feature is switched off or not installed: its section is a stranger to this build and is passed through
            JsonObject original = SampleDocument();
            var section = new RecoverySaveSection(Id);
            SideFileModel model = Model(section);

            model.Apply(SampleText());
            JsonObject written = model.Build(new SideFileHeader { modVersion = "0.1.0" }).Document;

            Assert.True(SectionOf(original, Other).Equals(SectionOf(written, Other)), "a section nobody holds is written back as it was");
            Assert.True(SectionOf(original, Id).Equals(SectionOf(written, Id)));
        }

        // ---- the round trip ----

        [Test]
        public void ADtoGoesToAJsonObjectAndBack_WithoutLoss_AndWithoutAVersionField()
        {
            RecoveryState original = RecoveryTestData.PopulatedState();
            original.creditLine.drawn = 1.0 / 3.0;
            original.loans[0].outstanding = 6527277.123456789;
            original.loans[0].annualRate = 0.1 + 0.2;     // not exactly 0.3 in binary
            original.journal[0].amount = 1e-7;
            original.journal[1].amount = -123456789012.345;

            JsonObject written = RecoveryStateCodec.Write(original);
            var problems = new List<string>();
            RecoveryState restored = RecoveryStateCodec.Read(written, problems);

            Assert.Equal(0, problems.Count, string.Join("; ", problems));
            Assert.False(written.ContainsKey(SideFileModel.SchemaKey), "no field of version in the section the feature writes");
            Assert.Equal(Members.Length, written.Count, "these members and no others");
            foreach (string member in Members) Assert.True(written.ContainsKey(member), "the section lacks " + member);
            Assert.Equal(StateJson(original), StateJson(restored), "nothing is lost in the trip");
            Assert.Equal(original.creditLine.drawn, restored.creditLine.drawn, "to the last digit");
            Assert.Equal(original.loans[0].annualRate, restored.loans[0].annualRate);
        }

        [Test]
        public void TheTripThroughTheTextOfAFile_LosesNothing()
        {
            RecoveryState original = RecoveryTestData.PopulatedState();
            original.loans[0].outstanding = 6527277.987654321;
            var firstSection = new RecoverySaveSection(Id);
            firstSection.Read(RecoveryStateCodec.Write(original), 1);

            string text = ModJson.Write(Model(firstSection).Build(new SideFileHeader()).Document, true);
            var secondSection = new RecoverySaveSection(Id);
            var lines = new List<string>();
            RecoverySaveSection checkedSection = new RecoverySaveSection(Id, lines.Add);
            Model(checkedSection).Apply(text);
            Model(secondSection).Apply(text);

            Assert.Equal(0, lines.Count, string.Join("; ", lines));
            Assert.Equal(StateJson(original), StateJson(checkedSection.State));
            Assert.Equal(StateJson(original), StateJson(secondSection.State));
        }

        // ---- no section ----

        [Test]
        public void ASaveWithoutTheSection_GivesTheValuesOfANewGame()
        {
            var section = new RecoverySaveSection(Id);
            section.Read(RecoveryStateCodec.Write(RecoveryTestData.PopulatedState()), 1);
            SideFileModel model = Model(section);

            model.Apply(@"{ ""schema"": 1, ""features"": { ""other-feature"": { ""_schema"": 1 } } }");

            Assert.Equal(StateJson(new RecoveryState()), StateJson(section.State), "the old state of another game is gone: defaults");
            Assert.Equal(CreditLineData.StateNone, section.State.creditLine.state);
            Assert.Equal(1, section.State.nextLoanSeq);
        }

        [Test]
        public void ASectionWithNothingInIt_IsTheDefaults_WithoutAProblem()
        {
            var lines = new List<string>();
            var section = new RecoverySaveSection(Id, lines.Add);

            Model(section).Apply(@"{ ""schema"": 1, ""features"": { """ + Id + @""": { ""_schema"": 1 } } }");

            Assert.Equal(0, lines.Count, string.Join("; ", lines));
            Assert.Equal(StateJson(new RecoveryState()), StateJson(section.State));
        }

        // ---- the three states of the line ----

        [Test]
        public void TheThreeStatesOfTheCreditLine_AreWrittenAndRead()
        {
            var states = new Dictionary<string, CreditLineData>
            {
                { CreditLineData.StateNone, new CreditLineData() },
                { CreditLineData.StateOpen, new CreditLineData { state = CreditLineData.StateOpen, opensOn = "1975-01-01", closesOn = "1978-01-01", drawn = 2660000.0, autoDraw = false } },
                { CreditLineData.StateClosed, new CreditLineData { state = CreditLineData.StateClosed, opensOn = "1975-01-01", closesOn = "1978-01-01", closedReason = CreditLineData.ReasonDisabled } },
            };

            foreach (KeyValuePair<string, CreditLineData> pair in states)
            {
                var state = new RecoveryState { creditLine = pair.Value };
                JsonObject written = RecoveryStateCodec.Write(state);
                Assert.Equal(pair.Key, written.GetObject("creditLine").GetString("state"), pair.Key + " is written under that name");

                var problems = new List<string>();
                RecoveryState restored = RecoveryStateCodec.Read(written, problems);
                Assert.Equal(0, problems.Count, pair.Key + ": " + string.Join("; ", problems));
                Assert.Equal(StateJson(state), StateJson(restored), pair.Key);
            }
        }

        // ---- damage ----

        // The sample's section with one change, read through the section: the state, and the lines it said for ModLog.
        private static RecoveryState ReadChanged(Action<JsonObject> change, List<string> lines)
        {
            JsonObject document = SampleDocument();
            change(SectionOf(document, Id));
            var section = new RecoverySaveSection(Id, lines.Add);
            Model(section).Apply(ModJson.Write(document, false));
            return section.State;
        }

        [Test]
        public void ABrokenDateOrSum_DropsItsRecord_AndTheRestIsKept_WithOneLineForTheLog()
        {
            // name of the case, what to break, a fragment the line must name
            var cases = new List<KeyValuePair<string, Action<JsonObject>>>
            {
                new KeyValuePair<string, Action<JsonObject>>("openedOn", s => Item(s.GetArray("loans"), 0).Set("openedOn", "03/01/1976")),
                new KeyValuePair<string, Action<JsonObject>>("outstanding", s => Item(s.GetArray("loans"), 0).Set("outstanding", -5.0)),
                new KeyValuePair<string, Action<JsonObject>>("payment", s => Item(s.GetArray("loans"), 0).Set("payment", -1.0)),
                new KeyValuePair<string, Action<JsonObject>>("annualRate", s => Item(s.GetArray("loans"), 0).Set("annualRate", 7.5)),
            };

            foreach (KeyValuePair<string, Action<JsonObject>> c in cases)
            {
                var lines = new List<string>();
                RecoveryState state = ReadChanged(c.Value, lines);

                Assert.Equal(0, state.loans.Count, c.Key + ": the loan is dropped");
                Assert.Equal(1, lines.Count, c.Key + ": one line: " + string.Join("; ", lines));
                Assert.True(lines[0].Contains(c.Key) && lines[0].Contains("bridge-1"), c.Key + ": the line names the field and the loan: " + lines[0]);
                Assert.Equal(CreditLineData.StateOpen, state.creditLine.state, c.Key + ": the line of credit is kept");
                Assert.Equal(3, state.journal.Count, c.Key + ": the journal is kept");
                Assert.Equal(2, state.nextLoanSeq, c.Key + ": the next number is kept");
            }
        }

        [Test]
        public void ABrokenSumOnTheLine_DropsTheLine_ButNotTheLoans()
        {
            var lines = new List<string>();
            RecoveryState state = ReadChanged(s => s.GetObject("creditLine").Set("drawn", -2660000.0), lines);

            Assert.Equal(CreditLineData.StateNone, state.creditLine.state, "a line with a negative debt is no line");
            Assert.Equal(1, state.loans.Count);
            Assert.Equal(1, lines.Count);
            Assert.True(lines[0].Contains("creditLine.drawn"), lines[0]);
        }

        [Test]
        public void ABrokenDateOfTheJournalOrTheLastBridgeLoan_IsDropped_WithALine()
        {
            var lines = new List<string>();
            RecoveryState state = ReadChanged(s =>
            {
                Item(s.GetArray("journal"), 1).Set("date", "tomorrow");
                s.Set("lastBridgeClosedOn", "soon");
            }, lines);

            Assert.Equal(2, state.journal.Count, "only the record with the bad date is dropped");
            Assert.Equal(string.Empty, state.lastBridgeClosedOn, "a date that is no date is no date");
            Assert.Equal(2, lines.Count, string.Join("; ", lines));
            Assert.True(RecoveryTestData.AnyContains(lines, "journal[1]"));
            Assert.True(RecoveryTestData.AnyContains(lines, "lastBridgeClosedOn"));
        }

        [Test]
        public void ABrokenContractOfTheInvestor_IsDropped_WithALine()
        {
            var lines = new List<string>();
            RecoveryState state = ReadChanged(s => s.GetObject("investorShare").Set("share", 1.5).Set("active", true), lines);

            Assert.False(state.investorShare.active, "a share above 1 makes no contract");
            Assert.Equal(1, lines.Count);
            Assert.True(lines[0].Contains("investorShare"), lines[0]);
        }

        [Test]
        public void AValueOfTheWrongType_KeepsTheDefault_AndSaysSo()
        {
            var lines = new List<string>();
            RecoveryState state = ReadChanged(s => s.Set("bailoutsUsed", "lots"), lines);

            Assert.Equal(0, state.bailoutsUsed);
            Assert.True(lines.Count >= 1 && RecoveryTestData.AnyContains(lines, "bailoutsUsed"), string.Join("; ", lines));
            Assert.Equal(1, state.loans.Count, "the rest is read");
        }

        // ---- other sections ----

        [Test]
        public void TheSectionOfAnotherFeature_ComesOutAsItWentIn_WhateverHappensToOurs()
        {
            var section = new RecoverySaveSection(Id);
            var other = new ForeignParticipant();
            SideFileModel model = Model(section, other);
            model.Apply(SampleText());
            JsonObject expected = (JsonObject)SectionOf(SampleDocument(), Other).DeepClone();

            section.State.loans.Clear();
            section.State.creditLine.state = CreditLineData.StateClosed;
            JsonObject whileOurs = model.Build(new SideFileHeader()).Document;
            section.ResetToDefaults();
            JsonObject afterReset = model.Build(new SideFileHeader()).Document;

            Assert.True(SectionOf(whileOurs, Other).Equals(expected), "their section is not touched by ours");
            Assert.True(SectionOf(afterReset, Other).Equals(expected));
            Assert.Equal(0, other.Resets, "nobody reset the other feature");
            Assert.Equal(1, other.Reads);
        }

        [Test]
        public void ANewGame_ResetsBoth_ButTheRestOfTheFileIsNotOursToKeep()
        {
            var section = new RecoverySaveSection(Id);
            var other = new ForeignParticipant();
            SideFileModel model = Model(section, other);
            model.Apply(SampleText());

            model.StartNewGame();

            Assert.Equal(StateJson(new RecoveryState()), StateJson(section.State));
            Assert.Equal(1, other.Resets);
            Assert.Null(other.Data);
        }

        // ---- after a settlement ----

        [Test]
        public void TheStateASettlementLeaves_IsTheSameSection_AndPassesEveryCheckOfTheNextRead()
        {
            RecoveryRig rig = SettlementKit.PopulatedRig();
            new DisableSettlementEngine(rig.Env, new FakeSettlementFlags()).Settle();

            JsonObject written = RecoveryStateCodec.Write(rig.State);
            var problems = new List<string>();
            RecoveryState restored = RecoveryStateCodec.Read(written, problems);

            Assert.Equal(0, problems.Count, "the next read finds nothing wrong: " + string.Join("; ", problems));
            Assert.Equal(Members.Length, written.Count, "the settlement adds no member to the section: the marks live in the flags of the save");
            Assert.False(written.ContainsKey(SideFileModel.SchemaKey));
            Assert.Equal(StateJson(rig.State), StateJson(restored));
            Assert.Equal(CreditLineData.StateClosed, restored.creditLine.state);
            Assert.Equal(CreditLineData.ReasonDisabled, restored.creditLine.closedReason);
            Assert.Equal(0, restored.loans.Count);
            Assert.False(restored.investorShare.active);
            Assert.Equal(1, restored.bailoutsUsed);
            Assert.Equal("1977-06-01", restored.lastBridgeClosedOn);
            Assert.Equal("disable.convert", restored.journal[restored.journal.Count - 1].op, "the record of the conversion is the last one");
        }

        [Test]
        public void TheSampleFile_HasNoMarksOfASettlement()
        {
            // a save made before any switch-off carries no flags of the feature, so reading it applies nothing
            var flags = new FakeSettlementFlags();
            var section = new RecoverySaveSection(Id);
            section.UseMarks(flags, () => true);
            Model(section).Apply(SampleText());
            string before = StateJson(section.State);

            bool changed = section.Reconcile();

            Assert.False(changed);
            Assert.Equal(before, StateJson(section.State));
            Assert.Equal(1, section.State.loans.Count);
        }
    }
}
