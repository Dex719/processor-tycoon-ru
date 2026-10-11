using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using PTMod.Core;
using PTMod.Core.Logic.Json;
using PTMod.Core.Logic.Save;
using PTMod.Features.DesignFlawRecalls;

namespace PTMod.Tests.Features.DesignFlawRecalls
{
    /// <summary>
    /// TSK-003 (FR-16, NFR-3, NFR-7): the section "design-flaw-recalls" of the side save file. The data has no version field (the core owns
    /// <c>_schema</c>), survives a round trip through <c>ModJson</c> without loss, starts empty with every existing CPU entered as rolled when the
    /// section is missing, and closes a record whose enum, days or sums are not valid.
    /// </summary>
    [Covers(typeof(FlawSaveSection), typeof(FlawSaveCodec), typeof(FlawSaveData), typeof(FlawRecordDto), typeof(FlawQuoteDto), typeof(RushProjectDto))]
    public class FlawSaveSectionTests
    {
        private const int Closed = (int)FlawState.Closed;

        // ---- identity and shape ----

        [Test]
        public void Section_IsTheFeatureIdAtVersionOne()
        {
            var section = NewSection();

            Assert.Equal("design-flaw-recalls", section.SectionId);
            Assert.Equal(FlawTestKit.Feature, FlawSaveSection.Id);
            Assert.Equal(1, section.SchemaVersion);
            Assert.Equal(1, FlawSaveCodec.SchemaVersion);
            Assert.True(section is ISideFileParticipant, "the logic-side interface of the participant");
        }

        [Test]
        public void SaveData_HasNoVersionField_AndNoAiCautionBlock()
        {
            foreach (Type type in new[] { typeof(FlawSaveData), typeof(FlawRecordDto), typeof(RushProjectDto), typeof(FlawQuoteDto) })
            {
                foreach (MemberInfo member in type.GetMembers(BindingFlags.Public | BindingFlags.Instance).Where(m => m is FieldInfo || m is PropertyInfo))
                {
                    string name = member.Name.ToLowerInvariant();
                    Assert.False(name.Contains("schema") || name.Contains("version"), type.Name + "." + member.Name + ": the core owns the version of the section (D20)");
                    Assert.False(name.Contains("aicaution"), type.Name + "." + member.Name + ": the caution of the AI is led by rival-personalities");
                }
            }
        }

        [Test]
        public void SaveData_HasTheFieldsOfTheDesign()
        {
            var names = new HashSet<string>(typeof(FlawSaveData).GetFields().Select(f => f.Name));
            foreach (string name in new[] { "nextRecordId", "lastActiveDay", "rolledCpuIds", "rushCpuIds", "rushProjects", "records", "pendingDecisionQueue" })
            {
                Assert.True(names.Contains(name), "FlawSaveData." + name);
            }

            var record = new HashSet<string>(typeof(FlawRecordDto).GetFields().Select(f => f.Name));
            foreach (string name in new[]
            {
                "id", "companyKey", "baseCpuId", "cpuIds", "typeId", "severity", "state", "source", "response", "coverUp", "automaticChoice", "suitFiled",
                "quietStepping", "riskAtRelease", "inflationAtRelease", "releaseDay", "discoveredDay", "publicSinceDay", "unitsAtDiscovery", "quote",
                "steppingEndDay", "lastChargedDay", "steppingDailyCost", "mitigationEndDay", "ipsLoss", "ignoreDrainUntilDay", "leakTailUntilDay",
                "modifierEntryIds", "spentTotal",
            })
            {
                Assert.True(record.Contains(name), "FlawRecordDto." + name);
            }

            var project = new HashSet<string>(typeof(RushProjectDto).GetFields().Select(f => f.Name));
            Assert.Equal(3, project.Count);
            Assert.True(project.Contains("companyKey") && project.Contains("cpuName") && project.Contains("plannedEndDay"));
        }

        [Test]
        public void NewData_IsTheEmptyState()
        {
            var data = new FlawSaveData();

            Assert.Equal(1, data.nextRecordId);
            Assert.Equal(0, data.lastActiveDay);
            Assert.Equal(0, data.rolledCpuIds.Count + data.rushCpuIds.Count + data.rushProjects.Count + data.records.Count + data.pendingDecisionQueue.Count);
        }

        // ---- the round trip (design.md example) ----

        [Test]
        public void DesignExample_RoundTripsThroughModJson_WithoutLoss()
        {
            JsonObject example = FlawTestKit.ParseObject(FlawTestKit.DesignSaveExample());
            var errors = new List<string>();
            FlawSaveData data = ModJson.Bind<FlawSaveData>(example, errors);

            Assert.Equal(0, errors.Count, string.Join("; ", errors));
            JsonNode back = ModJson.From(data);
            Assert.True(example.Equals(back), "the section changed on the way:\n" + ModJson.Write(example, true) + "\n" + ModJson.Write(back, true));
        }

        [Test]
        public void DesignExample_RoundTripsThroughTheSection_AndThroughText()
        {
            JsonObject example = FlawTestKit.ParseObject(FlawTestKit.DesignSaveExample());
            var warnings = new List<string>();
            FlawSaveSection section = NewSection(warnings);

            section.Read(example, 1);
            JsonObject written = section.Write();

            Assert.Equal(0, warnings.Count, string.Join("; ", warnings));
            Assert.True(section.WasRead);
            Assert.True(example.Equals(written), "Write() must give back what Read() was given");
            Assert.True(FlawTestKit.ParseNode(ModJson.Write(written, false)).Equals(example), "and through text");
        }

        [Test]
        public void DesignExample_Values_AreRead()
        {
            FlawSaveSection section = NewSection();
            section.Read(FlawTestKit.ParseObject(FlawTestKit.DesignSaveExample()), 1);
            FlawSaveData data = section.Data;

            Assert.Equal(4, data.nextRecordId);
            Assert.Equal(17010, data.lastActiveDay);
            AssertList(new[] { 1012, 1013, 2044, 2051 }, data.rolledCpuIds);
            AssertList(new[] { 2051 }, data.rushCpuIds);
            Assert.Equal(1, data.rushProjects.Count);
            Assert.Equal("Nova 3", data.rushProjects[0].cpuName);
            Assert.Equal(17240, data.rushProjects[0].plannedEndDay);
            Assert.Equal(1, data.records.Count);

            FlawRecordDto r = data.records[0];
            Assert.Equal(3, r.id);
            AssertList(new[] { 2044, 2059 }, r.cpuIds);
            Assert.Equal((int)FlawState.Mitigated, r.state);
            Assert.Equal((int)FlawResponse.Patch, r.response);
            Assert.Equal(17167, r.publicSinceDay);
            Assert.Equal(3200000L, r.unitsAtDiscovery);
            Assert.Equal(17707, r.mitigationEndDay);
            AssertList(new[] { "m-000017" }, r.modifierEntryIds);
            Assert.NotNull(r.quote);
            Assert.Equal(254600000f, r.quote.RecallCost);
            Assert.Equal(18, r.quote.PatchMonths);
            Assert.Equal(-8, r.quote.ReputationIgnore);
            Assert.Equal(3200000L, r.quote.AffectedUnits);
            Assert.True(r.quote.PatchAvailable);
        }

        [Test]
        public void Write_NeverContainsTheSchemaMember()
        {
            FlawSaveSection section = NewSection();
            JsonObject input = FlawTestKit.ParseObject("{ \"_schema\": 1, \"nextRecordId\": 2, \"records\": [] }");

            section.Read(input, 1);

            Assert.False(section.Write().ContainsKey("_schema"), "the core writes _schema, the feature never does");
            Assert.False(FlawSaveCodec.Write(new FlawSaveData()).ContainsKey("_schema"));
            Assert.False(FlawSaveCodec.Write(null).ContainsKey("_schema"));
            Assert.Equal(2, section.Write().GetInt32("nextRecordId"));
        }

        [Test]
        public void Read_DoesNotModifyTheInput()
        {
            JsonObject input = FlawTestKit.ParseObject(FlawTestKit.DesignSaveExample());
            JsonObject copy = (JsonObject)input.DeepClone();

            NewSection().Read(input, 1);

            Assert.True(copy.Equals(input), "the object handed over by the core stays as it was");
        }

        [Test]
        public void Write_GivesAFreshObjectEachTime()
        {
            FlawSaveSection section = NewSection();
            JsonObject first = section.Write();
            first.Set("nextRecordId", 99);

            Assert.Equal(1, section.Write().GetInt32("nextRecordId"));
            Assert.Equal(1, section.Data.nextRecordId);
        }

        // ---- empty, unknown, odd sections ----

        [Test]
        public void EmptySection_IsTheEmptyState()
        {
            var warnings = new List<string>();
            FlawSaveSection section = NewSection(warnings, 5, 6);

            section.Read(new JsonObject(), 1);

            Assert.Equal(0, warnings.Count);
            Assert.True(section.WasRead);
            Assert.Equal(1, section.Data.nextRecordId);
            Assert.Equal(0, section.Data.records.Count);
            Assert.Equal(0, section.Data.rolledCpuIds.Count, "a section that exists is not a reason to enter the CPUs: only a missing one is");
        }

        [Test]
        public void NullSection_IsTheEmptyState()
        {
            FlawSaveData data = FlawSaveCodec.Read(null, null);

            Assert.NotNull(data);
            Assert.Equal(1, data.nextRecordId);
            Assert.Equal(0, data.records.Count);
        }

        [Test]
        public void UnknownFields_AreIgnored()
        {
            var warnings = new List<string>();
            FlawSaveSection section = NewSection(warnings);
            JsonObject input = FlawTestKit.ParseObject(
                "{ \"nextRecordId\": 3, \"futureThing\": { \"a\": [1, 2] }, \"records\": [ " + RecordJson(2, "newerField=true") + " ] }");

            section.Read(input, 1);

            Assert.Equal(0, warnings.Count, string.Join("; ", warnings));
            Assert.Equal(3, section.Data.nextRecordId);
            Assert.Equal(1, section.Data.records.Count);
            Assert.Equal((int)FlawState.Public, section.Data.records[0].state);
            Assert.False(section.Write().ContainsKey("futureThing"));
        }

        [Test]
        public void ListsThatAreNull_BecomeEmpty()
        {
            FlawSaveSection section = NewSection();
            section.Read(FlawTestKit.ParseObject(
                "{ \"rolledCpuIds\": null, \"rushCpuIds\": null, \"rushProjects\": null, \"records\": [ " + RecordJson(1, "cpuIds=null", "modifierEntryIds=null") + " ], \"pendingDecisionQueue\": null }"), 1);

            Assert.Equal(0, section.Data.rolledCpuIds.Count);
            Assert.Equal(0, section.Data.rushProjects.Count);
            Assert.Equal(0, section.Data.pendingDecisionQueue.Count);
            FlawRecordDto r = section.Data.records[0];
            Assert.Equal((int)FlawState.Public, r.state, "a null list is not a damaged record");
            AssertList(new[] { 10 }, r.cpuIds, "the base CPU is always among the CPUs of the record");
            Assert.Equal(0, r.modifierEntryIds.Count);
        }

        // ---- a section that is missing: the empty state and the CPUs that exist ----

        [Test]
        public void Reset_StartsEmpty_AndEntersTheExistingCpusAsRolled()
        {
            FlawSaveSection section = NewSection(null, 40, 7, 7, 1012, -1, 9);

            section.ResetToDefaults();

            AssertList(new[] { 7, 9, 40, 1012 }, section.Data.rolledCpuIds);
            Assert.Equal(0, section.Data.records.Count);
            Assert.Equal(1, section.Data.nextRecordId);
            Assert.Equal(0, section.Data.lastActiveDay);
            Assert.False(section.WasRead);
            Assert.False(section.IsLocked);
        }

        [Test]
        public void Reset_ForgetsTheOldData()
        {
            FlawSaveSection section = NewSection(null, 3);
            section.Read(FlawTestKit.ParseObject(FlawTestKit.DesignSaveExample()), 1);
            Assert.Equal(1, section.Data.records.Count);

            section.ResetToDefaults();

            Assert.Equal(0, section.Data.records.Count);
            AssertList(new[] { 3 }, section.Data.rolledCpuIds);
            Assert.Equal(0, section.Data.rushProjects.Count);
            Assert.False(section.WasRead);
        }

        [Test]
        public void Reset_WithoutAProvider_OrWithAFailingOne_StartsEmpty()
        {
            var warnings = new List<string>();

            var none = new FlawSaveSection(null, warnings.Add);
            none.ResetToDefaults();
            Assert.Equal(0, none.Data.rolledCpuIds.Count);

            var broken = new FlawSaveSection(() => { throw new InvalidOperationException("the scene is gone"); }, warnings.Add);
            broken.ResetToDefaults();
            Assert.Equal(0, broken.Data.rolledCpuIds.Count);
            Assert.Equal(1, warnings.Count);
            Assert.True(warnings[0].Contains("the scene is gone"), warnings[0]);

            var nothing = new FlawSaveSection(() => null, warnings.Add);
            nothing.ResetToDefaults();
            Assert.Equal(0, nothing.Data.rolledCpuIds.Count);
            Assert.Equal(1, warnings.Count, "a null list is just nothing");
        }

        [Test]
        public void Reset_ProviderThatFailsHalfWay_KeepsNothing()
        {
            var section = new FlawSaveSection(() => FailingList(), null);

            section.ResetToDefaults();

            Assert.True(section.Data.rolledCpuIds.Count <= 1, "whatever was read before the failure is at most the part that came out");
            Assert.NotNull(section.Data);
        }

        [Test]
        public void Read_DoesNotAskTheProviderForTheCpus()
        {
            int asked = 0;
            var section = new FlawSaveSection(() => { asked++; return new[] { 1 }; }, null);

            section.Read(FlawTestKit.ParseObject("{}"), 1);

            Assert.Equal(0, asked);
            Assert.Equal(0, section.Data.rolledCpuIds.Count);
        }

        [Test]
        public void SideFile_WithoutOurSection_ResetsTheSection_AndEntersTheCpus()
        {
            var model = new SideFileModel();
            FlawSaveSection section = NewSection(null, 11, 12);
            string problem;
            Assert.True(model.TryRegister(section, out problem), problem);

            SideFileReadReport report = model.Apply(SideFile("{}"));

            Assert.NotEqual(SideFileStatus.Newer, report.Status);
            Assert.False(section.WasRead);
            AssertList(new[] { 11, 12 }, section.Data.rolledCpuIds);
            Assert.Equal(0, section.Data.records.Count);
        }

        [Test]
        public void SideFile_NewGame_ResetsTheSection()
        {
            var model = new SideFileModel();
            FlawSaveSection section = NewSection(null);
            string problem;
            Assert.True(model.TryRegister(section, out problem), problem);
            model.Apply(SideFile("{\"design-flaw-recalls\":{\"_schema\":1,\"nextRecordId\":9}}"));
            Assert.Equal(9, section.Data.nextRecordId);

            model.StartNewGame();

            Assert.Equal(1, section.Data.nextRecordId);
            Assert.False(section.WasRead);
        }

        // ---- the version belongs to the core ----

        [Test]
        public void SideFile_RoundTrip_CoreAddsTheSchema_AndTheDataComesBackEqual()
        {
            var model = new SideFileModel();
            FlawSaveSection section = NewSection();
            string problem;
            Assert.True(model.TryRegister(section, out problem), problem);
            model.Apply(SideFile("{\"design-flaw-recalls\":" + FlawTestKit.DesignSaveExample() + "}"));
            Assert.True(section.WasRead);

            SideFileBuildReport build = model.Build(new SideFileHeader { modVersion = "0.1.0", gameVersion = "0.2.3", saveName = "x", gameDate = "1985-03-01" });
            JsonObject stored = build.Document.GetObject("features").GetObject("design-flaw-recalls");

            Assert.NotNull(stored);
            Assert.Equal(1, stored.GetInt32("_schema"), "the core writes the version of the section");

            var model2 = new SideFileModel();
            FlawSaveSection section2 = NewSection();
            Assert.True(model2.TryRegister(section2, out problem), problem);
            model2.Apply(ModJson.Write(build.Document, false));

            Assert.True(section2.WasRead);
            Assert.True(FlawTestKit.Diff(section.Data, section2.Data).Count == 0, string.Join("\n", FlawTestKit.Diff(section.Data, section2.Data)));
        }

        [Test]
        public void SideFile_SectionWithoutSchema_IsReadAsVersionOne()
        {
            var model = new SideFileModel();
            FlawSaveSection section = NewSection();
            string problem;
            Assert.True(model.TryRegister(section, out problem), problem);

            model.Apply(SideFile("{\"design-flaw-recalls\":{\"nextRecordId\":5}}"));

            Assert.True(section.WasRead);
            Assert.Equal(5, section.Data.nextRecordId);
        }

        [Test]
        public void SideFile_NewerSection_IsNotRead_AndTheOriginalIsKept()
        {
            var model = new SideFileModel();
            FlawSaveSection section = NewSection(null, 21, 22);
            string problem;
            Assert.True(model.TryRegister(section, out problem), problem);
            const string newer = "{\"_schema\":2,\"nextRecordId\":77,\"futureField\":[1,2,3],\"records\":[]}";

            SideFileReadReport report = model.Apply(SideFile("{\"design-flaw-recalls\":" + newer + "}"));

            Assert.Equal(SideFileStatus.Newer, report.Status);
            Assert.Equal(1, report.NewerSections.Count);
            Assert.Equal("design-flaw-recalls", report.NewerSections[0]);
            Assert.False(section.WasRead, "a newer section is not read: the feature gets the defaults");
            Assert.Equal(1, section.Data.nextRecordId);
            AssertList(new[] { 21, 22 }, section.Data.rolledCpuIds);

            JsonObject stored = model.Build(null).Document.GetObject("features").GetObject("design-flaw-recalls");
            Assert.True(FlawTestKit.ParseNode(newer).Equals(stored), "the original section is written back unchanged on every save");
        }

        [Test]
        public void NewerSection_LockIsSetByTheFeature_AndLiftedByTheNextReset()
        {
            FlawSaveSection section = NewSection();
            Assert.False(section.IsLocked);

            section.LockForNewerSchema();
            Assert.True(section.IsLocked, "the feature is off for this save");

            section.ResetToDefaults();
            Assert.False(section.IsLocked, "a new game or another save starts clean");

            section.LockForNewerSchema();
            section.Read(FlawTestKit.ParseObject("{}"), 1);
            Assert.False(section.IsLocked, "a section that was read is not held by anybody");
        }

        [Test]
        public void Read_WithANewerVersion_IsRefused_ByTheSectionItself()
        {
            // The core never does this (it keeps such a section and calls ResetToDefaults); the section is safe if it ever happens.
            var warnings = new List<string>();
            FlawSaveSection section = NewSection(warnings, 8);

            section.Read(FlawTestKit.ParseObject("{ \"nextRecordId\": 50 }"), 2);

            Assert.Equal(1, section.Data.nextRecordId);
            Assert.True(section.IsLocked);
            Assert.False(section.WasRead);
            AssertList(new[] { 8 }, section.Data.rolledCpuIds);
            Assert.True(warnings.Any(w => w.Contains("newer than 1")), string.Join("; ", warnings));
        }

        [Test]
        public void Read_WithAVersionBelowOne_IsReadAsOne()
        {
            FlawSaveSection section = NewSection();

            section.Read(FlawTestKit.ParseObject("{ \"nextRecordId\": 6 }"), 0);

            Assert.True(section.WasRead);
            Assert.Equal(6, section.Data.nextRecordId);
        }

        [Test]
        public void Migrate_FromTheCurrentVersion_ChangesNothing()
        {
            JsonObject input = FlawTestKit.ParseObject(FlawTestKit.DesignSaveExample());
            var problems = new List<string>();

            JsonObject same = FlawSaveCodec.Migrate(input, 1, FlawSaveCodec.SchemaVersion, problems);

            Assert.True(ReferenceEquals(input, same), "no step, no copy");
            Assert.Equal(0, problems.Count);
            Assert.True(ReferenceEquals(input, FlawSaveCodec.Migrate(input, 1, 1, null)));
            Assert.True(ReferenceEquals(input, FlawSaveCodec.Migrate(input, 0, 1, null)));
            Assert.True(ReferenceEquals(input, FlawSaveCodec.Migrate(input, 3, 1, null)), "never downwards");
        }

        [Test]
        public void Migrate_ToAVersionThatHasNoStep_SaysSo()
        {
            // The first schema change adds its step to FlawSaveCodec.Migrate and replaces this test with one for the step.
            JsonObject input = FlawTestKit.ParseObject("{}");
            var problems = new List<string>();

            JsonObject result = FlawSaveCodec.Migrate(input, 1, 2, problems);

            Assert.True(ReferenceEquals(input, result));
            Assert.Equal(1, problems.Count);
            Assert.True(problems[0].Contains("no migration from schema 1 to 2"), problems[0]);
        }

        // ---- NFR-7: invalid values close the record ----

        [Test]
        public void ValidRecord_IsLeftAsItIs()
        {
            FlawSaveData data = DataWith(Record(1), Record(2));
            FlawSaveData expected = (FlawSaveData)Clone(data);
            var problems = new List<string>();

            FlawSaveCodec.Sanitize(data, problems);

            Assert.Equal(0, problems.Count, string.Join("; ", problems));
            Assert.True(FlawTestKit.Diff(expected, data).Count == 0, string.Join("\n", FlawTestKit.Diff(expected, data)));
        }

        [Test]
        public void InvalidEnum_ClosesTheRecord_AndResetsTheValue()
        {
            AssertClosed(r => r.state = 99, r => Assert.Equal(Closed, r.state));
            AssertClosed(r => r.state = -1, r => Assert.Equal(Closed, r.state));
            AssertClosed(r => r.severity = 3, r => Assert.Equal((int)FlawSeverity.Minor, r.severity));
            AssertClosed(r => r.severity = -2, r => Assert.Equal((int)FlawSeverity.Minor, r.severity));
            AssertClosed(r => r.source = 6, r => Assert.Equal((int)DiscoverySource.None, r.source));
            AssertClosed(r => r.response = 5, r => Assert.Equal((int)FlawResponse.None, r.response));
        }

        [Test]
        public void InvalidEnum_FromJson_ClosesTheRecord()
        {
            var warnings = new List<string>();
            FlawSaveSection section = NewSection(warnings);
            section.Read(FlawTestKit.ParseObject("{ \"nextRecordId\": 4, \"records\": [ " + RecordJson(1, "state=42") + ", " + RecordJson(2, "severity=9") + ", " + RecordJson(3, "state=2") + " ] }"), 1);

            Assert.Equal(3, section.Data.records.Count);
            Assert.Equal(Closed, section.Data.records[0].state);
            Assert.Equal(Closed, section.Data.records[1].state);
            Assert.Equal((int)FlawState.Public, section.Data.records[2].state, "the neighbours are not touched");
            Assert.Equal(2, warnings.Count);
            Assert.True(warnings[0].Contains("records[0] (id 1) is closed") && warnings[0].Contains("state 42 is not a FlawState"), warnings[0]);
        }

        [Test]
        public void NotANumber_ClosesTheRecord()
        {
            foreach (string field in FloatFields())
            {
                foreach (float bad in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity })
                {
                    FlawSaveData data = DataWith(Record(1), Record(2));
                    SetFloat(data.records[0], field, bad);
                    var problems = new List<string>();

                    FlawSaveCodec.Sanitize(data, problems);

                    Assert.Equal(Closed, data.records[0].state, field + " = " + bad);
                    Assert.Equal(0f, GetFloat(data.records[0], field), field + " is reset");
                    Assert.Equal((int)FlawState.Public, data.records[1].state, "the other record stays");
                    Assert.Equal(1, problems.Count, field);
                    Assert.True(problems[0].Contains(field), problems[0]);
                }
            }
        }

        [Test]
        public void NotANumber_InTheQuote_ClosesTheRecord()
        {
            foreach (string field in new[] { "RecallCost", "SteppingCost", "SteppingDailyCost", "ResidualClaimsCost", "PatchCost", "PatchIpsLoss", "ClassActionCost", "OemCancelChance", "IgnoreSignChanceMultiplier" })
            {
                FlawSaveData data = DataWith(Record(1));
                data.records[0].quote = new FlawQuoteDto { RecallCost = 5f };
                typeof(FlawQuoteDto).GetField(field).SetValue(data.records[0].quote, float.NaN);

                FlawSaveCodec.Sanitize(data, null);

                Assert.Equal(Closed, data.records[0].state, field);
                Assert.Equal(0f, (float)typeof(FlawQuoteDto).GetField(field).GetValue(data.records[0].quote), field);
            }
        }

        [Test]
        public void NegativeDays_CloseTheRecord()
        {
            foreach (string field in DayFields())
            {
                FlawSaveData data = DataWith(Record(1));
                typeof(FlawRecordDto).GetField(field).SetValue(data.records[0], -3);

                FlawSaveCodec.Sanitize(data, null);

                Assert.Equal(Closed, data.records[0].state, field);
                Assert.Equal(0, (int)typeof(FlawRecordDto).GetField(field).GetValue(data.records[0]), field + " is reset");
            }
        }

        [Test]
        public void NegativeUnitsAndTerms_CloseTheRecord()
        {
            AssertClosed(r => r.unitsAtDiscovery = -5, r => Assert.Equal(0L, r.unitsAtDiscovery));
            AssertClosed(r => r.quote = new FlawQuoteDto { SteppingDays = -1 }, r => Assert.Equal(0, r.quote.SteppingDays));
            AssertClosed(r => r.quote = new FlawQuoteDto { PatchMonths = -1 }, r => Assert.Equal(0, r.quote.PatchMonths));
            AssertClosed(r => r.quote = new FlawQuoteDto { AffectedUnits = -1 }, r => Assert.Equal(0L, r.quote.AffectedUnits));
        }

        [Test]
        public void BadCompanyKey_ClosesTheRecord()
        {
            AssertClosed(r => r.companyKey = "rival", null);
            AssertClosed(r => r.companyKey = "ai:", null);
            AssertClosed(r => r.companyKey = "ai:x", null);
            AssertClosed(r => r.companyKey = null, null);
            AssertClosed(r => r.companyKey = "", null);

            FlawSaveData good = DataWith(Record(1), Record(2));
            good.records[1].companyKey = "ai:12";
            FlawSaveCodec.Sanitize(good, null);
            Assert.Equal((int)FlawState.Public, good.records[0].state);
            Assert.Equal((int)FlawState.Public, good.records[1].state, "ai:N is a valid key");
        }

        [Test]
        public void EmptyTypeId_AndNegativeBaseCpu_CloseTheRecord()
        {
            AssertClosed(r => r.typeId = "", null);
            AssertClosed(r => r.typeId = null, null);
            AssertClosed(r => r.baseCpuId = -4, r => Assert.True(r.cpuIds.Contains(0)));
        }

        [Test]
        public void ClosedRecord_KeepsItsIdentityAndTheEntriesOfTheChannels()
        {
            FlawSaveData data = DataWith(Record(7));
            data.records[0].spentTotal = 123f;
            data.records[0].modifierEntryIds = new List<string> { "m-000001", "", null, "m-000002" };
            data.records[0].state = 77;

            FlawSaveCodec.Sanitize(data, null);

            FlawRecordDto r = data.records[0];
            Assert.Equal(7, r.id);
            Assert.Equal(123f, r.spentTotal);
            AssertList(new[] { "m-000001", "m-000002" }, r.modifierEntryIds, "the entries stay: FlawChannels removes them when it sees the closed record");
            Assert.Equal(Closed, r.state);
        }

        [Test]
        public void Write_AfterClosing_StoresTheClosedState()
        {
            FlawSaveSection section = NewSection();
            section.Read(FlawTestKit.ParseObject("{ \"nextRecordId\": 2, \"records\": [ " + RecordJson(1, "state=50") + " ] }"), 1);

            JsonObject written = section.Write();

            Assert.Equal(Closed, ((JsonObject)((JsonArray)written["records"]).First()).GetInt32("state"));
        }

        [Test]
        public void WrongJsonType_InsideARecord_ClosesThatRecordOnly()
        {
            var warnings = new List<string>();
            FlawSaveSection section = NewSection(warnings);
            section.Read(FlawTestKit.ParseObject(
                "{ \"nextRecordId\": 4, \"records\": [ " + RecordJson(1) + ", " + RecordJson(2, "spentTotal=\"a lot\"") + ", " + RecordJson(3, "state=\"Public\"") + " ] }"), 1);

            Assert.Equal(3, section.Data.records.Count);
            Assert.Equal((int)FlawState.Public, section.Data.records[0].state);
            Assert.Equal(Closed, section.Data.records[1].state);
            Assert.Equal(Closed, section.Data.records[2].state);
            Assert.True(warnings.Any(w => w.Contains("records[1].spentTotal")), string.Join("; ", warnings));
            Assert.True(warnings.Any(w => w.Contains("records[1] (id 2) is closed") && w.Contains("wrong type")), string.Join("; ", warnings));
        }

        [Test]
        public void WrongJsonType_AtTheTop_KeepsTheDefault_AndSaysSo()
        {
            var warnings = new List<string>();
            FlawSaveSection section = NewSection(warnings);

            section.Read(FlawTestKit.ParseObject("{ \"rolledCpuIds\": \"all\", \"nextRecordId\": \"x\", \"records\": 5, \"lastActiveDay\": true }"), 1);

            Assert.Equal(0, section.Data.rolledCpuIds.Count);
            Assert.Equal(1, section.Data.nextRecordId);
            Assert.Equal(0, section.Data.records.Count);
            Assert.Equal(0, section.Data.lastActiveDay);
            Assert.True(warnings.Count >= 4, string.Join("; ", warnings));
        }

        [Test]
        public void RecordThatIsNotAnObject_DoesNotBreakTheOthers()
        {
            FlawSaveSection section = NewSection();

            section.Read(FlawTestKit.ParseObject("{ \"nextRecordId\": 3, \"records\": [ 5, null, " + RecordJson(2) + " ] }"), 1);

            Assert.True(section.Data.records.Any(r => r.id == 2 && r.state == (int)FlawState.Public), "the valid record survives");
            Assert.True(section.Data.records.All(r => r.id >= 1), "a record without a valid id never stays");
        }

        // ---- ids, queues, projects ----

        [Test]
        public void RecordIds_MustBeUniqueAndPositive()
        {
            FlawSaveData data = DataWith(Record(1), Record(1), Record(0), Record(-4), Record(5));
            var problems = new List<string>();

            FlawSaveCodec.Sanitize(data, problems);

            Assert.Equal(new[] { 1, 5 }, data.records.Select(r => r.id).ToArray());
            Assert.Equal(3, problems.Count(p => p.Contains("dropped")), string.Join("; ", problems));
        }

        [Test]
        public void NextRecordId_IsAlwaysAboveTheHighestRecordId()
        {
            FlawSaveData data = DataWith(Record(3), Record(9));
            data.nextRecordId = 4;
            var problems = new List<string>();

            FlawSaveCodec.Sanitize(data, problems);

            Assert.Equal(10, data.nextRecordId);
            Assert.Equal(1, problems.Count);

            FlawSaveData zero = DataWith();
            zero.nextRecordId = 0;
            FlawSaveCodec.Sanitize(zero, null);
            Assert.Equal(1, zero.nextRecordId);

            FlawSaveData fine = DataWith(Record(3));
            fine.nextRecordId = 40;
            FlawSaveCodec.Sanitize(fine, null);
            Assert.Equal(40, fine.nextRecordId, "a gap is allowed: ids are never reused");
        }

        [Test]
        public void CpuIds_OfARecord_AlwaysHoldTheBaseCpu()
        {
            FlawSaveData data = DataWith(Record(1));
            data.records[0].cpuIds = new List<int> { 11, -2, 12 };
            data.records[0].baseCpuId = 10;

            FlawSaveCodec.Sanitize(data, null);

            AssertList(new[] { 10, 11, 12 }, data.records[0].cpuIds);
            Assert.Equal((int)FlawState.Public, data.records[0].state, "repairing the list does not close the record");
        }

        [Test]
        public void PendingQueue_KeepsOnlyOpenRecords_Once()
        {
            FlawSaveData data = DataWith(Record(1), Record(2), Record(3));
            data.records[1].state = (int)FlawState.Resolved;
            data.records[2].state = (int)FlawState.Closed;
            data.pendingDecisionQueue = new List<int> { 1, 2, 3, 99, 1 };
            var problems = new List<string>();

            FlawSaveCodec.Sanitize(data, problems);

            AssertList(new[] { 1 }, data.pendingDecisionQueue);
            Assert.Equal(1, problems.Count(p => p.Contains("pendingDecisionQueue")));
        }

        [Test]
        public void PendingQueue_DropsTheRecordsClosedByTheChecks()
        {
            FlawSaveData data = DataWith(Record(1), Record(2));
            data.records[0].state = 99;
            data.pendingDecisionQueue = new List<int> { 1, 2 };

            FlawSaveCodec.Sanitize(data, null);

            AssertList(new[] { 2 }, data.pendingDecisionQueue);
        }

        [Test]
        public void RushProjects_NeedAValidKeyAndAnEndDay()
        {
            FlawSaveData data = new FlawSaveData();
            data.rushProjects = new List<RushProjectDto>
            {
                new RushProjectDto { companyKey = "player", cpuName = "Nova", plannedEndDay = 100 },
                new RushProjectDto { companyKey = "ai:7", cpuName = null, plannedEndDay = 0 },
                new RushProjectDto { companyKey = "someone", cpuName = "X", plannedEndDay = 5 },
                new RushProjectDto { companyKey = "player", cpuName = "Y", plannedEndDay = -1 },
                new RushProjectDto { companyKey = null, cpuName = "Z", plannedEndDay = 5 },
                null,
            };
            var problems = new List<string>();

            FlawSaveCodec.Sanitize(data, problems);

            Assert.Equal(2, data.rushProjects.Count);
            Assert.Equal("Nova", data.rushProjects[0].cpuName);
            Assert.Equal(string.Empty, data.rushProjects[1].cpuName, "a missing name is an empty one");
            Assert.Equal(4, problems.Count, string.Join("; ", problems));
        }

        [Test]
        public void IdLists_DropNegativeAndRepeatedIds()
        {
            FlawSaveData data = new FlawSaveData();
            data.rolledCpuIds = new List<int> { 5, 5, -1, 7 };
            data.rushCpuIds = new List<int> { 3, 3 };
            var problems = new List<string>();

            FlawSaveCodec.Sanitize(data, problems);

            AssertList(new[] { 5, 7 }, data.rolledCpuIds);
            AssertList(new[] { 3 }, data.rushCpuIds);
            Assert.Equal(2, problems.Count);
        }

        [Test]
        public void LastActiveDay_CannotBeNegative()
        {
            FlawSaveData data = new FlawSaveData { lastActiveDay = -50 };

            FlawSaveCodec.Sanitize(data, null);

            Assert.Equal(0, data.lastActiveDay);
        }

        [Test]
        public void Sanitize_OfNull_GivesTheEmptyState()
        {
            FlawSaveData data = FlawSaveCodec.Sanitize(null, null);

            Assert.NotNull(data);
            Assert.Equal(1, data.nextRecordId);
        }

        [Test]
        public void Sanitize_IsIdempotent()
        {
            FlawSaveData data = DataWith(Record(1), Record(1), Record(2));
            data.records[1].severity = 8;
            data.rolledCpuIds = new List<int> { 1, 1, -3 };

            FlawSaveCodec.Sanitize(data, null);
            FlawSaveData once = (FlawSaveData)Clone(data);
            var problems = new List<string>();
            FlawSaveCodec.Sanitize(data, problems);

            Assert.Equal(0, problems.Count, string.Join("; ", problems));
            Assert.True(FlawTestKit.Diff(once, data).Count == 0);
        }

        // ---- the log callback ----

        [Test]
        public void Problems_GoToTheWarnCallback_AndAThrowingCallbackIsHarmless()
        {
            var warnings = new List<string>();
            FlawSaveSection quiet = NewSection(warnings);
            quiet.Read(FlawTestKit.ParseObject("{ \"nextRecordId\": 2, \"records\": [ " + RecordJson(1, "state=99") + " ] }"), 1);
            Assert.Equal(1, warnings.Count);
            Assert.True(warnings[0].StartsWith("save: "), warnings[0]);

            var loud = new FlawSaveSection(null, line => { throw new InvalidOperationException("the log is broken"); });
            loud.Read(FlawTestKit.ParseObject("{ \"nextRecordId\": 2, \"records\": [ " + RecordJson(1, "state=99") + " ] }"), 1);
            Assert.Equal(Closed, loud.Data.records[0].state);
        }

        [Test]
        public void Read_NeverThrows_OnOddSections()
        {
            string[] sections =
            {
                "{ \"records\": [ {} ] }",
                "{ \"records\": [ { \"id\": 1 } ] }",
                "{ \"records\": [ { \"id\": 1, \"companyKey\": \"player\", \"typeId\": \"x\", \"cpuIds\": [\"a\"], \"quote\": 5 } ] }",
                "{ \"records\": [ { \"id\": 1, \"quote\": { \"RecallCost\": \"x\" } } ] }",
                "{ \"rushProjects\": [ 1, {}, { \"companyKey\": 3 } ] }",
                "{ \"pendingDecisionQueue\": [ \"a\", 1.5, null ] }",
                "{ \"rolledCpuIds\": [ 1, \"b\", null, 2.5 ] }",
            };

            foreach (string text in sections)
            {
                FlawSaveSection section = NewSection();
                section.Read(FlawTestKit.ParseObject(text), 1);

                Assert.NotNull(section.Data, text);
                Assert.NotNull(section.Write(), text);
                Assert.True(section.Data.records.All(r => Enum.IsDefined(typeof(FlawState), r.state)), text);
            }
        }

        // ---- helpers ----

        private static void AssertList<T>(T[] expected, List<T> actual, string message = null)
        {
            Assert.Equal(expected, actual.ToArray(), message);
        }

        private static FlawSaveSection NewSection(List<string> warnings = null, params int[] cpus)
        {
            return new FlawSaveSection(() => cpus, warnings == null ? (Action<string>)null : warnings.Add);
        }

        private static IEnumerable<int> FailingList()
        {
            yield return 1;
            throw new InvalidOperationException("the second CPU is gone");
        }

        private static string SideFile(string features)
        {
            return "{\"schema\":1,\"modVersion\":\"0.1.0\",\"gameVersion\":\"0.2.3\",\"saveName\":\"1985_3_1\",\"gameDate\":\"1985-03-01\",\"features\":" + features + "}";
        }

        /// <summary>A record that is valid: public, owned by the player, the CPU 10.</summary>
        private static FlawRecordDto Record(int id)
        {
            return new FlawRecordDto
            {
                id = id,
                companyKey = "player",
                baseCpuId = 10,
                cpuIds = new List<int> { 10 },
                typeId = "arith",
                severity = (int)FlawSeverity.Major,
                state = (int)FlawState.Public,
                source = (int)DiscoverySource.External,
                response = (int)FlawResponse.None,
                riskAtRelease = 0.1f,
                inflationAtRelease = 2f,
                releaseDay = 100,
                discoveredDay = 200,
                publicSinceDay = 200,
                unitsAtDiscovery = 1000,
                quote = new FlawQuoteDto { RecallCost = 1000f, SteppingDays = 90, PatchMonths = 18, AffectedUnits = 1000 },
                modifierEntryIds = new List<string> { "m-000001" },
                spentTotal = 10f,
            };
        }

        /// <summary>JSON of a valid public record; each override is <c>name=json</c> and replaces (or adds) the member of that name.</summary>
        private static string RecordJson(int id, params string[] overrides)
        {
            var members = new List<KeyValuePair<string, string>>
            {
                new KeyValuePair<string, string>("id", id.ToString()),
                new KeyValuePair<string, string>("companyKey", "\"player\""),
                new KeyValuePair<string, string>("baseCpuId", "10"),
                new KeyValuePair<string, string>("cpuIds", "[10]"),
                new KeyValuePair<string, string>("typeId", "\"arith\""),
                new KeyValuePair<string, string>("severity", "1"),
                new KeyValuePair<string, string>("state", "2"),
                new KeyValuePair<string, string>("source", "2"),
                new KeyValuePair<string, string>("response", "0"),
                new KeyValuePair<string, string>("releaseDay", "100"),
                new KeyValuePair<string, string>("discoveredDay", "200"),
                new KeyValuePair<string, string>("publicSinceDay", "200"),
                new KeyValuePair<string, string>("unitsAtDiscovery", "1000"),
            };
            foreach (string over in overrides)
            {
                int eq = over.IndexOf('=');
                string name = over.Substring(0, eq);
                string value = over.Substring(eq + 1);
                int at = members.FindIndex(m => m.Key == name);
                if (at >= 0) members[at] = new KeyValuePair<string, string>(name, value);
                else members.Add(new KeyValuePair<string, string>(name, value));
            }
            return "{ " + string.Join(", ", members.Select(m => "\"" + m.Key + "\": " + m.Value)) + " }";
        }

        private static FlawSaveData DataWith(params FlawRecordDto[] records)
        {
            var data = new FlawSaveData();
            data.records.AddRange(records);
            int max = records.Length == 0 ? 0 : records.Max(r => r.id);
            data.nextRecordId = Math.Max(1, max + 1);
            return data;
        }

        private static object Clone(FlawSaveData data)
        {
            return ModJson.Bind<FlawSaveData>(ModJson.From(data), null);
        }

        /// <summary>One record is made bad by <paramref name="damage"/>: it is closed, a valid neighbour is not, and <paramref name="check"/> sees the repaired record.</summary>
        private static void AssertClosed(Action<FlawRecordDto> damage, Action<FlawRecordDto> check)
        {
            FlawSaveData data = DataWith(Record(1), Record(2));
            damage(data.records[0]);
            var problems = new List<string>();

            FlawSaveCodec.Sanitize(data, problems);

            Assert.Equal(Closed, data.records[0].state);
            Assert.Equal((int)FlawState.Public, data.records[1].state, "the neighbour is not touched");
            Assert.Equal(1, problems.Count, string.Join("; ", problems));
            Assert.True(problems[0].Contains("records[0] (id 1) is closed"), problems[0]);
            if (check != null) check(data.records[0]);
        }

        private static string[] FloatFields()
        {
            return new[] { "riskAtRelease", "inflationAtRelease", "steppingDailyCost", "ipsLoss", "spentTotal" };
        }

        private static string[] DayFields()
        {
            return new[] { "releaseDay", "discoveredDay", "publicSinceDay", "steppingEndDay", "lastChargedDay", "mitigationEndDay", "ignoreDrainUntilDay", "leakTailUntilDay" };
        }

        private static void SetFloat(FlawRecordDto record, string field, float value)
        {
            typeof(FlawRecordDto).GetField(field).SetValue(record, value);
        }

        private static float GetFloat(FlawRecordDto record, string field)
        {
            return (float)typeof(FlawRecordDto).GetField(field).GetValue(record);
        }
    }
}
