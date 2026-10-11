using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using PTMod.Core;
using PTMod.Core.Logic.Json;
using PTMod.Core.Logic.Save;
using PTMod.Features.HistoricalEvents;
using PTMod.Tests.Save;

namespace PTMod.Tests.Features.HistoricalEvents
{
    /// <summary>
    /// The section "historical-events" of the side save file (TSK-003, FR-18, NFR-3, NFR-8): the round trip of the design's example, the rule that the
    /// feature's data has no version and <c>Write()</c> returns none, the statuses of a game without a section, the normalization of damaged values, and
    /// the way the core's model treats the section (a newer one is kept, a missing one gives defaults).
    /// </summary>
    [Covers(typeof(HistSaveData), typeof(EntryStateDto), typeof(RoleTargetsDto), typeof(CaseDto), typeof(SettlementDto), typeof(ExportRuleDto), typeof(ExemptionDto))]
    [Covers(typeof(PriceWarDto), typeof(PriceRestoreDto), typeof(DisputeDto), typeof(RaceDto), typeof(HistSaveCodec), typeof(HistSaveSectionBase))]
    public class HistSaveSectionTests
    {
        private const string Section = "historical-events";

        /// <summary>The example of "Секция ModSave" in design.md, as it stands there (without the version).</summary>
        private const string DesignExample =
            "{ 'initialized': true," +
            "  'entries': [" +
            "    { 'id': 'hist.1995.win95', 'status': 2, 'jitter': 5, 'firedDay': 9002, 'stagesFired': 1, 'targets': [], 'flags': [] }," +
            "    { 'id': 'hist.2009.eufine', 'status': 2, 'jitter': -3, 'firedDay': 13998, 'stagesFired': 1," +
            "      'targets': [ { 'role': 'target', 'companyKeys': ['player'] } ], 'flags': [] } ]," +
            "  'cases': [" +
            "    { 'caseId': 'eu2009', 'targetKey': 'player', 'mode': 'fine', 'stage': 2, 'fine': 1000000000, 'paid': 0, 'provisioned': 0," +
            "      'rulingDay': 15824, 'reimposeDay': 0, 'guaranteeFeePerMonth': 833333.3, 'openedDay': 13998, 'params': { 'appealMonths': 60 } } ]," +
            "  'settlements': [], 'exportRules': [], 'priceWars': [], 'disputes': [], 'races': []," +
            "  'lastMajorDay': 13998," +
            "  'catchUpEntryIds': [] }";

        /// <summary>A section that holds one object of every kind.</summary>
        private const string EveryKind =
            "{ 'initialized': true," +
            "  'entries': [ { 'id': 'hist.2022.exportcontrol', 'status': 2, 'jitter': 0, 'firedDay': 18541, 'stagesFired': 1," +
            "                 'targets': [ { 'role': 'allCpuCompanies', 'companyKeys': ['player','ai:3','ai:7'] } ], 'flags': ['recovered'] } ]," +
            "  'cases': [ { 'caseId': 'eu2009', 'targetKey': 'ai:3', 'mode': 'consent', 'stage': 1, 'fine': 250000000, 'paid': 1000, 'provisioned': 5000," +
            "               'rulingDay': 15824, 'reimposeDay': 16500, 'guaranteeFeePerMonth': 20000.5, 'openedDay': 13998, 'params': { 'pAnnul': 0.35, 'list': [1, 2] } } ]," +
            "  'settlements': [ { 'id': 'set1', 'plaintiffKey': 'player', 'defendantKey': 'ai:3', 'resolveDay': 15000, 'legalPerMonth': 400000.5, 'pWin': 0.7, 'settlementPct': 0.03 } ]," +
            "  'exportRules': [ { 'ruleId': 'us2022', 'threshold0': 120.5, 'avgIps0': 80.25, 'threshold': 130.5, 'percentile': 0.9, 'blockedShare': 0.15, 'indexing': 0.05," +
            "                     'segments': [0, 1], 'endDay': 19000," +
            "                     'exemptions': [ { 'companyKey': 'ai:3', 'kind': 'license', 'factor': 0.5, 'endDay': 18900 } ] } ]," +
            "  'priceWars': [ { 'warId': 'core2', 'endDay': 17000, 'viaRivalApi': true, 'restore': [ { 'cpuSaveId': 12, 'originalTargetPrice': 450.5 } ] } ]," +
            "  'disputes': [ { 'disputeId': 'd1', 'mode': 'infringement', 'plaintiffKey': 'ai:3', 'defendantKey': 'player', 'choice': 'settle'," +
            "                  'endDay': 16000, 'resolveDay': 15900, 'monthlyPaymentPct': 0.002 } ]," +
            "  'races': [ { 'raceId': 'ghz', 'attribute': 'frequency', 'topN': 3, 'bonus': 1.1, 'segments': [0, 1, 2], 'endDay': 20000, 'topCpuIds': [5, 6, 7] } ]," +
            "  'lastMajorDay': 18541," +
            "  'catchUpEntryIds': ['hist.2008.netbook'] }";

        // ---- the participant ----

        [Test]
        public void TheSection_IsNamedLikeTheFeature_AndIsVersionOne()
        {
            var section = NewSection(null, null);
            Assert.Equal("historical-events", section.SectionId);
            Assert.Equal(1, section.SchemaVersion);
            Assert.Equal(1, HistSaveCodec.SchemaVersion);
            Assert.True(section is ISideFileParticipant, "the core's logic-side interface");
        }

        [Test]
        public void TheData_HasNoVersionField_AndWriteReturnsNone()
        {
            foreach (Type type in new[] { typeof(HistSaveData), typeof(EntryStateDto), typeof(RoleTargetsDto), typeof(CaseDto), typeof(SettlementDto), typeof(ExportRuleDto),
                                          typeof(ExemptionDto), typeof(PriceWarDto), typeof(PriceRestoreDto), typeof(DisputeDto), typeof(RaceDto) })
            {
                var names = new List<string>();
                names.AddRange(type.GetFields(BindingFlags.Public | BindingFlags.Instance).Select(f => f.Name));
                names.AddRange(type.GetProperties(BindingFlags.Public | BindingFlags.Instance).Select(p => p.Name));
                foreach (string name in names)
                {
                    string lower = name.ToLowerInvariant();
                    Assert.False(lower.Contains("schema") || lower.Contains("version"), type.Name + "." + name + ": the version of the section is the core's (_schema), not the feature's");
                }
            }

            JsonObject written = NewSection(null, null).Write();
            Assert.False(written.ContainsKey("_schema"), "Write() returns the data without _schema");
            Assert.False(HistSaveCodec.Write(new HistSaveData()).ContainsKey("_schema"));
            Assert.False(HistSaveCodec.Write(null).ContainsKey("_schema"), "a null DTO is written as an empty section");
        }

        [Test]
        public void Write_AlwaysGivesAFreshObject()
        {
            var section = NewSection(null, null);
            JsonObject first = section.Write();
            first.Set("tampered", true);
            Assert.False(section.Write().ContainsKey("tampered"));
        }

        [Test]
        public void ARead_DoesNotKeepAnythingFromTheSectionObject()
        {
            JsonObject stored = HistHelpers.Obj(DesignExample);
            JsonObject copy = (JsonObject)stored.DeepClone();
            var section = NewSection(null, null);

            section.Read(stored, 1);
            section.Data.entries.Clear();
            section.Data.lastMajorDay = 1;

            Assert.True(copy.Equals(stored), "Read must not modify the object it is given");
        }

        // ---- the round trip ----

        [Test]
        public void TheExampleOfTheDesign_SurvivesAReadAndAWrite()
        {
            JsonObject stored = HistHelpers.Obj(DesignExample);
            var problems = new List<string>();
            var section = NewSection(null, null, problems);

            section.Read(stored, 1);
            Assert.Equal(0, problems.Count, string.Join(" | ", problems));

            HistSaveData data = section.Data;
            Assert.True(data.initialized);
            Assert.Equal(2, data.entries.Count);
            Assert.Equal("hist.2009.eufine", data.entries[1].id);
            Assert.Equal(-3, data.entries[1].jitter);
            Assert.Equal("player", data.entries[1].targets[0].companyKeys[0]);
            Assert.Equal(1, data.cases.Count);
            Assert.Equal("eu2009", data.cases[0].caseId);
            Assert.Near(1000000000, data.cases[0].fine, 1);
            Assert.Near(833333.3, data.cases[0].guaranteeFeePerMonth, 0.1);
            Assert.Equal(60, data.cases[0].@params.GetInt32("appealMonths"));
            Assert.Equal(13998, data.lastMajorDay);

            HistFixtureJson.AssertSame(stored, section.Write());
        }

        [Test]
        public void ASectionWithEveryKindOfObject_SurvivesAReadAndAWrite()
        {
            JsonObject stored = HistHelpers.Obj(EveryKind);
            var problems = new List<string>();
            var section = NewSection(null, null, problems);

            section.Read(stored, 1);
            Assert.Equal(0, problems.Count, string.Join(" | ", problems));
            HistSaveData data = section.Data;
            Assert.Equal(3, data.entries[0].targets[0].companyKeys.Count);
            Assert.Equal("ai:3", data.exportRules[0].exemptions[0].companyKey);
            Assert.Equal(new[] { 0, 1 }, data.exportRules[0].segments.ToArray());
            Assert.True(data.priceWars[0].viaRivalApi);
            Assert.Equal(12, data.priceWars[0].restore[0].cpuSaveId);
            Assert.Equal(new[] { 5, 6, 7 }, data.races[0].topCpuIds.ToArray());

            HistFixtureJson.AssertSame(stored, section.Write());
        }

        [Test]
        public void AnEmptySection_IsWrittenAndReadBackEmpty()
        {
            JsonObject written = HistSaveCodec.Write(new HistSaveData());
            Assert.False(written.GetBool("initialized", true));
            Assert.Equal(0, written.GetArray("entries").Count);
            Assert.Equal(0, written.GetInt32("lastMajorDay", -1));

            HistSaveData back = HistSaveCodec.Read(written, null);
            Assert.False(back.initialized);
            Assert.Equal(0, back.entries.Count + back.cases.Count + back.settlements.Count + back.exportRules.Count + back.priceWars.Count + back.disputes.Count + back.races.Count);
        }

        [Test]
        public void ASectionWithTheVersion_IsReadLikeOneWithout_AndNeverWrittenBackWithIt()
        {
            // The core strips _schema before Read; if it ever got through, the codec drops it from what it writes.
            JsonObject stored = HistHelpers.Obj(DesignExample);
            stored.Set("_schema", 1);
            var section = NewSection(null, null);
            section.Read(stored, 1);

            Assert.False(section.Write().ContainsKey("_schema"));
            Assert.Equal(2, section.Data.entries.Count);
        }

        [Test]
        public void AFieldOfANewerMod_IsIgnored_AndAMissingOneKeepsItsDefault()
        {
            var problems = new List<string>();
            HistSaveData data = HistSaveCodec.Read(HistHelpers.Obj("{'initialized':true,'futureThing':{'a':1},'entries':[{'id':'hist.x','status':2,'extra':5}]}"), problems);

            Assert.Equal(0, problems.Count, string.Join(" | ", problems));
            Assert.True(data.initialized);
            Assert.Equal(1, data.entries.Count);
            Assert.Equal(0, data.entries[0].firedDay);
            Assert.NotNull(data.entries[0].flags);
            Assert.Equal(0, data.cases.Count);
            Assert.Equal(0, data.lastMajorDay);
        }

        [Test]
        public void AMemberOfTheWrongType_IsReportedAndKeepsItsDefault()
        {
            var problems = new List<string>();
            HistSaveData data = HistSaveCodec.Read(HistHelpers.Obj("{'lastMajorDay':'soon','entries':{},'cases':[{'caseId':'c','targetKey':'player','fine':'lots','stage':1}]}"), problems);

            Assert.Equal(0, data.lastMajorDay);
            Assert.Equal(0, data.entries.Count);
            Assert.Equal(1, data.cases.Count, "the case stays; the sum that is not a number reads as 0");
            Assert.Equal(0f, data.cases[0].fine);
            Assert.Equal(1, data.cases[0].stage);
            Assert.True(problems.Any(p => p.Contains("lastMajorDay")), string.Join(" | ", problems));
            Assert.True(problems.Any(p => p.Contains("fine")), string.Join(" | ", problems));
        }

        [Test]
        public void ANullSection_IsAnEmptyOne()
        {
            HistSaveData data = HistSaveCodec.Read(null, null);
            Assert.NotNull(data);
            Assert.False(data.initialized);
            Assert.Equal(0, data.entries.Count);
        }

        // ---- the normalization of damaged values (NFR-8) ----

        [Test]
        public void NegativeDays_BecomeZero()
        {
            var problems = new List<string>();
            HistSaveData data = HistSaveCodec.Read(HistHelpers.Obj(
                "{'lastMajorDay':-5,'entries':[{'id':'hist.x','firedDay':-1,'stagesFired':-2}]," +
                " 'cases':[{'caseId':'c','targetKey':'player','rulingDay':-3,'reimposeDay':-4,'openedDay':-5,'stage':-1}]," +
                " 'settlements':[{'id':'s','plaintiffKey':'player','defendantKey':'ai:1','resolveDay':-9}]," +
                " 'exportRules':[{'ruleId':'r','endDay':-1}]," +
                " 'priceWars':[{'warId':'w','endDay':-1}]," +
                " 'disputes':[{'disputeId':'d','plaintiffKey':'player','defendantKey':'ai:1','endDay':-1,'resolveDay':-2}]," +
                " 'races':[{'raceId':'r','endDay':-1,'topN':-3}]}"), problems);

            Assert.Equal(0, data.lastMajorDay);
            Assert.Equal(0, data.entries[0].firedDay);
            Assert.Equal(0, data.entries[0].stagesFired);
            Assert.Equal(0, data.cases[0].rulingDay + data.cases[0].reimposeDay + data.cases[0].openedDay + data.cases[0].stage);
            Assert.Equal(0, data.settlements[0].resolveDay);
            Assert.Equal(0, data.exportRules[0].endDay);
            Assert.Equal(0, data.priceWars[0].endDay);
            Assert.Equal(0, data.disputes[0].endDay + data.disputes[0].resolveDay);
            Assert.Equal(0, data.races[0].endDay + data.races[0].topN);
            Assert.True(problems.Count >= 14, "every repair is a line: " + problems.Count);
        }

        [Test]
        public void SumsThatAreNegative_OrNotFinite_BecomeZero()
        {
            HistSaveData data = new HistSaveData();
            data.cases.Add(new CaseDto { caseId = "c", targetKey = "player", fine = -1f, paid = float.NaN, provisioned = float.PositiveInfinity, guaranteeFeePerMonth = float.NegativeInfinity });
            data.settlements.Add(new SettlementDto { id = "s", plaintiffKey = "player", defendantKey = "ai:1", legalPerMonth = -5f });
            data.exportRules.Add(new ExportRuleDto { ruleId = "r", threshold0 = -1, avgIps0 = float.NaN, threshold = -2, indexing = -0.5f });
            data.disputes.Add(new DisputeDto { disputeId = "d", plaintiffKey = "player", defendantKey = "ai:1", monthlyPaymentPct = float.NaN });
            data.races.Add(new RaceDto { raceId = "r", bonus = -2f });

            HistSaveData back = HistSaveCodec.Read(HistSaveCodec.Write(data), null);

            CaseDto c = back.cases[0];
            Assert.Equal(0f, c.fine + c.paid + c.provisioned + c.guaranteeFeePerMonth);
            Assert.Equal(0f, back.settlements[0].legalPerMonth);
            ExportRuleDto r = back.exportRules[0];
            Assert.Equal(0f, r.threshold0 + r.avgIps0 + r.threshold + r.indexing);
            Assert.Equal(0f, back.disputes[0].monthlyPaymentPct);
            Assert.Equal(0f, back.races[0].bonus);
        }

        [Test]
        public void AStatusOutsideTheEnum_IsTakenAsPending()
        {
            var problems = new List<string>();
            HistSaveData data = HistSaveCodec.Read(HistHelpers.Obj("{'entries':[{'id':'a','status':99},{'id':'b','status':-1},{'id':'c','status':6},{'id':'d','status':0}]}"), problems);

            Assert.Equal(new[] { 0, 0, 6, 0 }, data.entries.Select(e => e.status).ToArray());
            Assert.True(problems.Any(p => p.Contains("entries[0].status") && p.Contains("Pending")), string.Join(" | ", problems));
            Assert.True(HistSaveCodec.IsKnownStatus(0) && HistSaveCodec.IsKnownStatus(6));
            Assert.False(HistSaveCodec.IsKnownStatus(7) || HistSaveCodec.IsKnownStatus(-1));
        }

        [Test]
        public void TheJitter_IsKeptInsideTheRangeTheCalendarAllows()
        {
            HistSaveData data = HistSaveCodec.Read(HistHelpers.Obj("{'entries':[{'id':'a','jitter':500},{'id':'b','jitter':-61},{'id':'c','jitter':-60},{'id':'d','jitter':60}]}"), null);
            Assert.Equal(new[] { 60, -60, -60, 60 }, data.entries.Select(e => e.jitter).ToArray());
        }

        [Test]
        public void Companies_AreCompanyKeys_AndAnythingElseIsDropped()
        {
            var problems = new List<string>();
            HistSaveData data = HistSaveCodec.Read(HistHelpers.Obj(
                "{'entries':[{'id':'hist.x','targets':[{'role':'target','companyKeys':['player','ai:4','Intel','ai:','ai:-1','ai:4','',null]},{'role':'','companyKeys':['player']}]}]," +
                " 'cases':[{'caseId':'ok','targetKey':'ai:12'},{'caseId':'bad','targetKey':'Intel'},{'caseId':'none'}]," +
                " 'settlements':[{'id':'s1','plaintiffKey':'player','defendantKey':'ai:2'},{'id':'s2','plaintiffKey':'player','defendantKey':'nobody'}]," +
                " 'disputes':[{'disputeId':'d1','plaintiffKey':'ai:1','defendantKey':'player'},{'disputeId':'d2','plaintiffKey':'','defendantKey':'player'}]," +
                " 'exportRules':[{'ruleId':'r','exemptions':[{'companyKey':'ai:3','kind':'license'},{'companyKey':'x'}]}]}"), problems);

            TargetsCheck(data);
            Assert.Equal(new[] { "ok" }, data.cases.Select(c => c.caseId).ToArray());
            Assert.Equal(new[] { "s1" }, data.settlements.Select(s => s.id).ToArray());
            Assert.Equal(new[] { "d1" }, data.disputes.Select(d => d.disputeId).ToArray());
            Assert.Equal(1, data.exportRules[0].exemptions.Count);
            Assert.True(problems.Any(p => p.Contains("\"Intel\" is not a company key")), string.Join(" | ", problems));
        }

        private static void TargetsCheck(HistSaveData data)
        {
            Assert.Equal(1, data.entries[0].targets.Count, "the target list without a role name is dropped");
            Assert.Equal(new[] { "player", "ai:4" }, data.entries[0].targets[0].companyKeys.ToArray());
        }

        [Test]
        public void ObjectsWithoutAnId_AreDropped_AndARepeatedIdKeepsTheFirst()
        {
            var problems = new List<string>();
            HistSaveData data = HistSaveCodec.Read(HistHelpers.Obj(
                "{'entries':[{'id':''},{'id':'a','jitter':1},{'id':'a','jitter':2},{'status':2}]," +
                " 'cases':[{'targetKey':'player'},{'caseId':'c','targetKey':'player','stage':1},{'caseId':'c','targetKey':'player','stage':2}]," +
                " 'priceWars':[{'warId':''},{'warId':'w'},{'warId':'w','endDay':5}]," +
                " 'races':[{'attribute':'x'},{'raceId':'r'}]}"), problems);

            Assert.Equal(1, data.entries.Count);
            Assert.Equal(1, data.entries[0].jitter);
            Assert.Equal(1, data.cases.Count);
            Assert.Equal(1, data.cases[0].stage);
            Assert.Equal(1, data.priceWars.Count);
            Assert.Equal(0, data.priceWars[0].endDay);
            Assert.Equal(1, data.races.Count);
            Assert.True(problems.Any(p => p.Contains("appears twice")), string.Join(" | ", problems));
        }

        [Test]
        public void NullItemsOfAList_AreDropped()
        {
            HistSaveData data = HistSaveCodec.Read(HistHelpers.Obj("{'entries':[null,{'id':'a','flags':['x',null,'','x']}],'cases':null,'catchUpEntryIds':['a',null,'','a','b']}"), null);

            Assert.Equal(1, data.entries.Count);
            Assert.Equal(new[] { "x" }, data.entries[0].flags.ToArray());
            Assert.NotNull(data.cases, "a list that was null is an empty list");
            Assert.Equal(0, data.cases.Count);
            Assert.Equal(new[] { "a", "b" }, data.catchUpEntryIds.ToArray());
        }

        [Test]
        public void ARestoreWithoutAUsablePrice_IsDropped_SoNoCpuIsSetToNonsense()
        {
            var problems = new List<string>();
            HistSaveData data = HistSaveCodec.Read(HistHelpers.Obj(
                "{'priceWars':[{'warId':'w','restore':[{'cpuSaveId':1,'originalTargetPrice':0},{'cpuSaveId':2,'originalTargetPrice':-5},{'cpuSaveId':-3,'originalTargetPrice':100},{'cpuSaveId':4,'originalTargetPrice':99.5}]}]}"), problems);

            PriceWarDto war = data.priceWars[0];
            Assert.Equal(1, war.restore.Count);
            Assert.Equal(4, war.restore[0].cpuSaveId);
            Assert.Near(99.5, war.restore[0].originalTargetPrice, 1e-4);
            Assert.True(problems.Count >= 3, string.Join(" | ", problems));
        }

        [Test]
        public void SharesAreClamped_AndSegmentsMustExist()
        {
            HistSaveData data = HistSaveCodec.Read(HistHelpers.Obj(
                "{'exportRules':[{'ruleId':'r','percentile':1.5,'blockedShare':-0.2,'segments':[0,7,-1,2,2]}]," +
                " 'races':[{'raceId':'k','segments':[3,4],'topCpuIds':[1,-1,2]}]}"), null);

            ExportRuleDto rule = data.exportRules[0];
            Assert.Near(1.0, rule.percentile, 1e-6);
            Assert.Near(0.0, rule.blockedShare, 1e-6);
            Assert.Equal(new[] { 0, 2 }, rule.segments.ToArray());
            Assert.Equal(new[] { 3 }, data.races[0].segments.ToArray());
            Assert.Equal(new[] { 1, 2 }, data.races[0].topCpuIds.ToArray());
        }

        [Test]
        public void ACaseWithoutParams_GetsAnEmptyObject()
        {
            HistSaveData data = HistSaveCodec.Read(HistHelpers.Obj("{'cases':[{'caseId':'c','targetKey':'player'},{'caseId':'d','targetKey':'player','params':null}]}"), null);
            Assert.NotNull(data.cases[0].@params);
            Assert.NotNull(data.cases[1].@params);
            Assert.Equal(0, data.cases[1].@params.Count);
            Assert.NotNull(HistSaveCodec.Write(data).GetArray("cases"));
        }

        [Test]
        public void Strings_ThatAreMissing_BecomeEmpty()
        {
            HistSaveData data = HistSaveCodec.Read(HistHelpers.Obj(
                "{'cases':[{'caseId':'c','targetKey':'player'}],'disputes':[{'disputeId':'d','plaintiffKey':'player','defendantKey':'ai:1'}],'races':[{'raceId':'r'}]}"), null);
            Assert.Equal(string.Empty, data.cases[0].mode);
            Assert.Equal(string.Empty, data.disputes[0].mode + data.disputes[0].choice);
            Assert.Equal(string.Empty, data.races[0].attribute);
        }

        // ---- a game without a section (FR-18 item 3) ----

        [Test]
        public void WithoutASection_EntriesBeforeTheCurrentDateAreSkipped_TheOthersPending()
        {
            CalendarEntry old = Entry("hist.1990.old", "1990-01-01");
            CalendarEntry sameDay = Entry("hist.1995.win95", "1995-08-24");
            CalendarEntry next = Entry("hist.1996.next", "1996-02-01");
            CalendarEntry last = Entry("hist.2000.later", "2000-01-01");
            int today = new SimpleDate(1995, 8, 24).Days;
            var section = NewSection(() => new List<CalendarEntry> { old, sameDay, next, last }, () => today);

            section.ResetToDefaults();

            Assert.True(section.Data.initialized, "initialized = true");
            Assert.False(section.NeedsDefaults);
            Assert.Equal(
                new[] { (int)EntryStatus.SkippedBeforeStart, (int)EntryStatus.Pending, (int)EntryStatus.Pending, (int)EntryStatus.Pending },
                section.Data.entries.Select(e => e.status).ToArray());
            Assert.Equal(new[] { "hist.1990.old", "hist.1995.win95", "hist.1996.next", "hist.2000.later" }, section.Data.entries.Select(e => e.id).ToArray());
            Assert.True(section.Data.entries.All(e => e.jitter == 0 && e.firedDay == 0 && e.stagesFired == 0), "nothing has fired in a game that had no section");
            Assert.Equal(0, section.Data.cases.Count + section.Data.priceWars.Count + section.Data.catchUpEntryIds.Count);
        }

        [Test]
        public void TheDefaultState_IsAValidSection()
        {
            CalendarEntry a = Entry("hist.1990.old", "1990-01-01");
            CalendarEntry b = Entry("hist.2000.later", "2000-01-01");
            var section = NewSection(() => new List<CalendarEntry> { a, b }, () => new SimpleDate(1995, 1, 1).Days);
            section.ResetToDefaults();

            JsonObject written = section.Write();
            Assert.False(written.ContainsKey("_schema"));
            var problems = new List<string>();
            HistSaveData back = HistSaveCodec.Read(written, problems);
            Assert.Equal(0, problems.Count, string.Join(" | ", problems));
            Assert.True(back.initialized);
            Assert.Equal(new[] { 4, 0 }, back.entries.Select(e => e.status).ToArray());
        }

        [Test]
        public void WithoutASection_AndNoCalendarYet_TheStatusesWait()
        {
            IReadOnlyList<CalendarEntry> calendar = null;
            int? today = null;
            var section = NewSection(() => calendar, () => today);

            section.ResetToDefaults();
            Assert.True(section.NeedsDefaults, "the calendar is loaded when the game is ready, after the core has read the file");
            Assert.False(section.Data.initialized);
            Assert.Equal(0, section.Data.entries.Count);
            Assert.False(section.TryApplyDefaults(), "nothing is known yet");

            calendar = new List<CalendarEntry> { Entry("hist.1990.old", "1990-01-01"), Entry("hist.2000.later", "2000-01-01") };
            Assert.False(section.TryApplyDefaults(), "the calendar is there, the date is not");

            today = new SimpleDate(1995, 6, 1).Days;
            Assert.True(section.TryApplyDefaults());
            Assert.False(section.NeedsDefaults);
            Assert.True(section.Data.initialized);
            Assert.Equal(new[] { 4, 0 }, section.Data.entries.Select(e => e.status).ToArray());
            Assert.False(section.TryApplyDefaults(), "done once");
        }

        [Test]
        public void AnEmptyCalendar_LeavesTheStatusesOpen()
        {
            var section = NewSection(() => new List<CalendarEntry>(), () => 100);
            section.ResetToDefaults();
            Assert.True(section.NeedsDefaults);
            Assert.False(section.Data.initialized);
        }

        [Test]
        public void ASectionThatWasRead_IsNeverReplacedByDefaults()
        {
            CalendarEntry a = Entry("hist.1990.old", "1990-01-01");
            var section = NewSection(() => new List<CalendarEntry> { a }, () => new SimpleDate(1995, 1, 1).Days);

            section.Read(HistHelpers.Obj("{'initialized':true,'entries':[{'id':'hist.1990.old','status':2,'firedDay':5}]}"), 1);

            Assert.False(section.NeedsDefaults);
            Assert.False(section.TryApplyDefaults());
            Assert.Equal(2, section.Data.entries[0].status);
        }

        [Test]
        public void WhenTheOwnerSetsTheStateUp_TheDefaultsByTheDateAreNotAppliedOverIt()
        {
            // A new game: the scheduler initializes the state after the fast-forward and says so.
            var calendar = new List<CalendarEntry> { Entry("hist.1990.old", "1990-01-01") };
            int? today = null;
            var section = NewSection(() => calendar, () => today);

            section.ResetToDefaults();
            Assert.True(section.NeedsDefaults);
            section.SetUpByOwner();
            section.Data.initialized = true;
            today = new SimpleDate(1995, 1, 1).Days;

            Assert.False(section.NeedsDefaults);
            Assert.False(section.TryApplyDefaults());
            Assert.Equal(0, section.Data.entries.Count, "the owner's state is left alone");
        }

        [Test]
        public void AResetAfterARead_StartsOver()
        {
            var section = NewSection(null, null);
            section.Read(HistHelpers.Obj(DesignExample), 1);
            Assert.Equal(2, section.Data.entries.Count);

            section.ResetToDefaults();
            Assert.Equal(0, section.Data.entries.Count);
            Assert.False(section.Data.initialized);
            Assert.Equal(0, section.Data.cases.Count);
            Assert.Equal(0, section.Data.lastMajorDay);
        }

        [Test]
        public void TheDefaults_TakeARepeatedIdOnce_AndSkipBrokenEntries()
        {
            var a = Entry("hist.a", "1990-01-01");
            HistSaveData data = HistSaveCodec.CreateDefaults(new[] { a, a, null, new CalendarEntry { Id = null }, new CalendarEntry { Id = "hist.unset" } }, new SimpleDate(1995, 1, 1).Days);

            Assert.Equal(new[] { "hist.a", "hist.unset" }, data.entries.Select(e => e.id).ToArray());
            Assert.Equal(4, data.entries[0].status);
            Assert.Equal(0, data.entries[1].status, "an entry whose date is not set is not 'before the start'");
            Assert.True(HistSaveCodec.CreateDefaults(null, 0).initialized);
            Assert.NotNull(HistSaveCodec.FindEntry(data, "hist.a"));
            Assert.Null(HistSaveCodec.FindEntry(data, "hist.zz"));
            Assert.Null(HistSaveCodec.FindEntry(null, "hist.a"));
            Assert.Null(HistSaveCodec.FindEntry(data, null));
        }

        // ---- the core's model: newer and missing sections ----

        [Test]
        public void ANewerSection_IsNotReadByTheFeature_AndIsKeptAsItWas()
        {
            var calendar = new List<CalendarEntry> { Entry("hist.1990.old", "1990-01-01") };
            var section = NewSection(() => calendar, () => new SimpleDate(1995, 1, 1).Days);
            SideFileModel model = RegisteredModel(section);
            string stored = "{\"_schema\":2,\"initialized\":true,\"entries\":[{\"id\":\"hist.1990.old\",\"status\":2}],\"brandNewField\":{\"x\":[1,2,3]}}";

            SideFileReadReport report = model.Apply(SideFileTestKit.File1("\"historical-events\":" + stored));

            Assert.Equal(SideFileStatus.Newer, report.Status);
            Assert.Equal(new[] { Section }, report.NewerSections.ToArray());
            Assert.True(section.Data.initialized, "the feature got ResetToDefaults and, knowing the calendar and the date, the statuses by the date");
            Assert.Equal((int)EntryStatus.SkippedBeforeStart, section.Data.entries[0].status, "its data is not the stored section's");

            SideFileBuildReport build = model.Build(null);
            SideFileTestKit.AssertJson(SideFileTestKit.Parse(stored), SideFileTestKit.Features(build)[Section]);
        }

        [Test]
        public void ASectionWithoutTheSection_GetsDefaults()
        {
            var calendar = new List<CalendarEntry> { Entry("hist.1990.old", "1990-01-01"), Entry("hist.2000.later", "2000-01-01") };
            var section = NewSection(() => calendar, () => new SimpleDate(1995, 1, 1).Days);
            SideFileModel model = RegisteredModel(section);

            SideFileReadReport report = model.Apply(SideFileTestKit.File1(string.Empty));

            Assert.Equal(SideFileStatus.Loaded, report.Status);
            Assert.True(section.Data.initialized);
            Assert.Equal(new[] { 4, 0 }, section.Data.entries.Select(e => e.status).ToArray());

            SideFileModel missing = RegisteredModel(NewSection(() => calendar, () => new SimpleDate(1995, 1, 1).Days));
            Assert.Equal(SideFileStatus.Missing, missing.ApplyMissing().Status);
        }

        [Test]
        public void AStoredSection_ComesBackThroughTheModelWithTheVersionTheCoreAdds()
        {
            var section = NewSection(null, null);
            SideFileModel model = RegisteredModel(section);
            JsonObject stored = HistHelpers.Obj(DesignExample);

            JsonObject versioned = (JsonObject)stored.DeepClone();
            versioned.Set("_schema", 1);
            SideFileReadReport report = model.Apply(SideFileTestKit.File1("\"historical-events\":" + ModJson.Write(versioned, false)));

            Assert.Equal(SideFileStatus.Loaded, report.Status);
            Assert.Equal(2, section.Data.entries.Count);

            JsonObject out1 = (JsonObject)SideFileTestKit.Features(model.Build(null))[Section];
            Assert.Equal(1, out1.GetInt32("_schema", -1), "the core writes the version");
            out1.Remove("_schema");
            HistFixtureJson.AssertSame(stored, out1);
        }

        [Test]
        public void ASectionWithoutAVersion_IsReadAsVersionOne()
        {
            var section = NewSection(null, null);
            SideFileModel model = RegisteredModel(section);

            SideFileReadReport report = model.Apply(SideFileTestKit.File1("\"historical-events\":" + ModJson.Write(HistHelpers.Obj(DesignExample), false)));

            Assert.Equal(SideFileStatus.Loaded, report.Status);
            Assert.Equal(2, section.Data.entries.Count);
            Assert.True(report.Warnings.Any(w => w.Contains("_schema")), "the core warns: " + string.Join(" | ", report.Warnings));
        }

        // ---- helpers ----

        private static HistSaveSectionBase NewSection(Func<IReadOnlyList<CalendarEntry>> calendar, Func<int?> today, List<string> warnings = null)
        {
            return new HistSaveSectionBase(calendar, today, warnings == null ? (Action<string>)null : warnings.Add);
        }

        private static SideFileModel RegisteredModel(HistSaveSectionBase section)
        {
            var model = new SideFileModel();
            string problem;
            Assert.True(model.TryRegister(section, out problem), problem);
            return model;
        }

        private static CalendarEntry Entry(string id, string anchor)
        {
            SimpleDate date;
            Assert.True(SimpleDate.TryParse(anchor, out date), anchor);
            return new CalendarEntry { Id = id, Anchor = date };
        }
    }

    internal static class HistHelpers
    {
        public static JsonObject Obj(string text)
        {
            return HistFixture.Obj(text);
        }
    }

    internal static class HistFixtureJson
    {
        /// <summary>The two trees hold the same data (object members in any order).</summary>
        public static void AssertSame(JsonNode expected, JsonNode actual)
        {
            Assert.True(expected.Equals(actual), "expected " + expected + "\n  but got " + actual);
        }
    }
}
