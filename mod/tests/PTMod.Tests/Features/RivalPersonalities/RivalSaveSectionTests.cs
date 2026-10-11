using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using PTMod.Core;
using PTMod.Core.Logic.Json;
using PTMod.Core.Logic.Save;
using PTMod.Features.RivalPersonalities;

namespace PTMod.Tests.Features.RivalPersonalities
{
    /// <summary>
    /// The section "rival-personalities" of the side save file (FR-10, TSK-001): the participant's identity, the state without a version field, the
    /// round trip, and the behaviour of the core's real <c>SideFileModel</c> around it (no section, a newer section).
    /// </summary>
    [Covers(typeof(RivalSaveSection), typeof(RivalSaveCodec), typeof(RivalState), typeof(AssignmentEntry), typeof(AttitudeRecord),
        typeof(AttitudeEntry), typeof(ShareSnapshot), typeof(ShareRow), typeof(UndercutCooldown), typeof(PendingReaction),
        typeof(ActiveReaction), typeof(PriceBookEntry), typeof(CautionEntry), typeof(Counters), typeof(LastReaction), typeof(HistoryEntry))]
    public class RivalSaveSectionTests
    {
        private const string Id = "rival-personalities";

        private static RivalSaveSection NewSection(List<string> warnings = null)
        {
            return new RivalSaveSection(Id, warnings == null ? (Action<string>)null : warnings.Add);
        }

        private static string Text(JsonNode node)
        {
            return ModJson.Write(node, false);
        }

        /// <summary>A state with something in every list, in the shape of the example of design.md.</summary>
        private static RivalState Sample()
        {
            var state = new RivalState
            {
                graceStart = "1980-01-01",
                nextReactionSeq = 19,   // above r-18, the highest id in use: the next id is never one that is taken (RivalStateSanitizer)
            };
            state.assignments.Add(new AssignmentEntry { uid = 0, archetype = "hegemon" });
            state.assignments.Add(new AssignmentEntry { uid = 1, archetype = "price_warrior" });
            state.revealed.Add(0);
            state.attitudes.Add(new AttitudeRecord
            {
                uid = 1,
                value = -25.4f,
                history = new List<AttitudeEntry> { new AttitudeEntry { date = "1984-03-12", delta = -10.4f, reason = "StrongLaunch:1" } },
            });
            state.shareSnapshot = new ShareSnapshot
            {
                monthIndex = 158,
                rows = new List<ShareRow>
                {
                    new ShareRow { key = "player", s = new[] { 0.10f, 0.04f, 0f, 0.02f } },
                    new ShareRow { key = "ai:0", s = new[] { 0.60f, 0.35f, 0f, 0.05f } },
                },
            };
            state.undercutCooldowns.Add(new UndercutCooldown { uid = 1, segment = 1, until = "1984-06-01" });
            state.pending.Add(new PendingReaction
            {
                id = "r-17", uid = 1, type = "PriceCut", segment = 1, trigger = "StrongLaunch",
                plannedOn = "1984-03-12", mailId = 100231, telegraphDays = 33, fireOn = "1984-04-20",
            });
            state.pending.Add(new PendingReaction { id = "r-18", uid = 0, type = "Smear", segment = 0, trigger = "ShareGain", plannedOn = "1984-04-01", mailId = 100240, telegraphDays = 20, fireOn = "" });
            state.active.Add(new ActiveReaction { id = "r-12", uid = 0, type = "CounterLaunch", segment = 0, start = "1983-11-02", end = "1984-04-01" });
            state.priceBook.Add(new PriceBookEntry { cpuSaveId = 4711, uid = 1, reactionId = "r-17", baseTargetPrice = 239f, cutTarget = 203.15f, startedOn = "1984-04-20" });
            state.cautions.Add(new CautionEntry { uid = 4, flawId = 3, until = "1986-02-01" });
            state.counters = new Counters
            {
                year = 1984,
                hostileExecutedThisYear = 1,
                lastByUid = new List<LastReaction> { new LastReaction { uid = 0, date = "1983-11-02" } },
            };
            state.history.Add(new HistoryEntry { uid = 0, date = "1983-11-02", type = "CounterLaunch", segment = 0, outcome = "started" });
            state.dormant.Add(9);
            return state;
        }

        // ---- identity ----

        [Test]
        public void Participant_IsTheSectionOfTheFeature_AtVersionOne()
        {
            RivalSaveSection section = NewSection();
            Assert.Equal("rival-personalities", section.SectionId);
            Assert.Equal(1, section.SchemaVersion);
            Assert.Equal(1, RivalSaveCodec.SchemaVersion);
        }

        [Test]
        public void TheState_HasNoVersionField()
        {
            // the core writes and reads _schema; a field of the feature would be a second version (design.md, D9)
            var names = new List<string>();
            foreach (Type type in typeof(RivalState).Assembly.GetTypes().Where(t => t.IsPublic && t.Namespace == "PTMod.Features.RivalPersonalities"))
            {
                if (type.GetCustomAttributes(typeof(SerializableAttribute), false).Length == 0) continue;   // the DTOs of the section
                foreach (FieldInfo field in type.GetFields(BindingFlags.Public | BindingFlags.Instance))
                {
                    string lower = field.Name.ToLowerInvariant();
                    if (lower.Contains("schema") || lower.Contains("version")) names.Add(type.Name + "." + field.Name);
                }
            }
            Assert.Equal(0, names.Count, "version-like fields in the state: " + string.Join(", ", names));
        }

        [Test]
        public void Write_NeverContainsTheSchemaMember()
        {
            RivalSaveSection section = NewSection();
            Assert.False(section.Write().ContainsKey("_schema"), "default state");

            section.Read(RivalTestKit.Obj("{'_schema':1,'graceStart':'1990-05-05'}"), 1);
            Assert.False(section.Write().ContainsKey("_schema"), "a stray _schema in the data is dropped");
            Assert.Equal("1990-05-05", section.State.graceStart);
            Assert.False(RivalSaveCodec.Write(Sample()).ContainsKey("_schema"));
        }

        // ---- defaults ----

        [Test]
        public void DefaultState_IsEmpty_AndDoesNotStartTheGracePeriodByItself()
        {
            RivalState state = NewSection().State;

            Assert.Equal(string.Empty, state.graceStart);
            Assert.Equal(0, state.assignments.Count);
            Assert.Equal(0, state.revealed.Count);
            Assert.Equal(0, state.attitudes.Count);
            Assert.NotNull(state.shareSnapshot);
            Assert.Equal(0, state.shareSnapshot.rows.Count);
            Assert.Equal(0, state.undercutCooldowns.Count);
            Assert.Equal(0, state.pending.Count);
            Assert.Equal(0, state.active.Count);
            Assert.Equal(0, state.priceBook.Count);
            Assert.Equal(0, state.cautions.Count);
            Assert.NotNull(state.counters);
            Assert.Equal(0, state.counters.hostileExecutedThisYear);
            Assert.Equal(0, state.counters.lastByUid.Count);
            Assert.Equal(0, state.history.Count);
            Assert.Equal(0, state.dormant.Count);
            Assert.Equal(1, state.nextReactionSeq);
        }

        [Test]
        public void ResetToDefaults_ThrowsTheReadStateAway()
        {
            RivalSaveSection section = NewSection();
            section.Read(RivalSaveCodec.Write(Sample()), 1);
            Assert.Equal(2, section.State.assignments.Count);

            section.ResetToDefaults();

            Assert.Equal(0, section.State.assignments.Count);
            Assert.Equal(string.Empty, section.State.graceStart);
            Assert.Equal(1, section.State.nextReactionSeq);
        }

        [Test]
        public void ANullSection_GivesTheDefaultState()
        {
            var problems = new List<string>();
            RivalState state = RivalSaveCodec.Read(null, problems);

            Assert.NotNull(state);
            Assert.Equal(0, problems.Count);
            Assert.Equal(1, state.nextReactionSeq);
        }

        // ---- the round trip ----

        [Test]
        public void RoundTrip_KeepsEverything()
        {
            RivalSaveSection first = NewSection();
            first.Read(RivalSaveCodec.Write(Sample()), 1);
            JsonObject written = first.Write();

            var warnings = new List<string>();
            RivalSaveSection second = NewSection(warnings);
            second.Read(RivalTestKit.ParseObject(ModJson.Write(written, true)), 1);   // through the text, as the file does it

            Assert.Equal(0, warnings.Count, string.Join("; ", warnings));
            Assert.Equal(Text(written), Text(second.Write()), "writing what was read gives the same section");

            RivalState s = second.State;
            Assert.Equal("1980-01-01", s.graceStart);
            Assert.Equal(new[] { "hegemon", "price_warrior" }, s.assignments.Select(a => a.archetype).ToArray());
            Assert.Equal(new[] { 0 }, s.revealed.ToArray());
            Assert.Equal(-25.4f, s.attitudes[0].value);
            Assert.Equal("StrongLaunch:1", s.attitudes[0].history[0].reason);
            Assert.Equal(158, s.shareSnapshot.monthIndex);
            Assert.Equal(new[] { 0.60f, 0.35f, 0f, 0.05f }, s.shareSnapshot.rows[1].s);
            Assert.Equal("1984-06-01", s.undercutCooldowns[0].until);
            Assert.Equal("r-17", s.pending[0].id);
            Assert.Equal(100231, s.pending[0].mailId);
            Assert.Equal("1984-04-20", s.pending[0].fireOn);
            Assert.Equal(string.Empty, s.pending[1].fireOn, "a reaction whose mail is not delivered has no date");
            Assert.Equal("1984-04-01", s.active[0].end);
            Assert.Equal(203.15f, s.priceBook[0].cutTarget);
            Assert.Equal(3, s.cautions[0].flawId);
            Assert.Equal(1984, s.counters.year);
            Assert.Equal("1983-11-02", s.counters.lastByUid[0].date);
            Assert.Equal("started", s.history[0].outcome);
            Assert.Equal(new[] { 9 }, s.dormant.ToArray());
            Assert.Equal(19, s.nextReactionSeq);
        }

        [Test]
        public void TheSectionOfTheDesignExample_IsRead()
        {
            // design.md, "Пример секции бокового файла"
            JsonObject section = RivalTestKit.Obj(
                "{'graceStart':'1980-01-01',"
                + "'assignments':[{'uid':0,'archetype':'hegemon'},{'uid':1,'archetype':'price_warrior'},{'uid':10,'archetype':'fast_follower'}],"
                + "'revealed':[0],"
                + "'attitudes':[{'uid':1,'value':-25.4,'history':[{'date':'1984-03-12','delta':-10.4,'reason':'StrongLaunch:1'}]}],"
                + "'shareSnapshot':{'monthIndex':158,'rows':[{'key':'player','s':[0.10,0.04,0.0,0.02]},{'key':'ai:0','s':[0.60,0.35,0.0,0.05]}]},"
                + "'undercutCooldowns':[{'uid':1,'segment':1,'until':'1984-06-01'}],"
                + "'pending':[{'id':'r-17','uid':1,'type':'PriceCut','segment':1,'trigger':'StrongLaunch','plannedOn':'1984-03-12','mailId':100231,'telegraphDays':33,'fireOn':'1984-04-20'}],"
                + "'active':[{'id':'r-12','uid':0,'type':'CounterLaunch','segment':0,'start':'1983-11-02','end':'1984-04-01'}],"
                + "'priceBook':[],"
                + "'cautions':[{'uid':4,'flawId':3,'until':'1986-02-01'}],"
                + "'counters':{'year':1984,'hostileExecutedThisYear':1,'lastByUid':[{'uid':0,'date':'1983-11-02'}]},"
                + "'history':[{'uid':0,'date':'1983-11-02','type':'CounterLaunch','segment':0,'outcome':'started'}],"
                + "'dormant':[],'nextReactionSeq':18}");

            var warnings = new List<string>();
            RivalSaveSection reader = NewSection(warnings);
            reader.Read(section, 1);

            Assert.Equal(0, warnings.Count, string.Join("; ", warnings));
            Assert.Equal(3, reader.State.assignments.Count);
            Assert.Equal(10, reader.State.assignments[2].uid);
            Assert.Equal(33, reader.State.pending[0].telegraphDays);
            Assert.Equal(1984, reader.State.counters.year);
            Assert.Equal(18, reader.State.nextReactionSeq);
        }

        [Test]
        public void ANeverSetAttitude_StaysNotANumber_AndComesBackThatWay()
        {
            // the reader gives such a rival the baseline of its archetype (design.md, "Validation Rules")
            RivalSaveSection section = NewSection();
            section.Read(RivalTestKit.Obj("{'attitudes':[{'uid':3}]}"), 1);

            Assert.True(float.IsNaN(section.State.attitudes[0].value));

            RivalSaveSection again = NewSection();
            again.Read(RivalTestKit.ParseObject(Text(section.Write())), 1);
            Assert.True(float.IsNaN(again.State.attitudes[0].value), "it survives a round trip");
        }

        // ---- damaged data ----

        [Test]
        public void MembersOfTheWrongType_KeepTheirDefaults_AndAreReported()
        {
            var warnings = new List<string>();
            RivalSaveSection section = NewSection(warnings);

            section.Read(RivalTestKit.Obj("{'graceStart':5,'assignments':'many','nextReactionSeq':'x','counters':{'year':'soon','hostileExecutedThisYear':2}}"), 1);

            Assert.Equal(string.Empty, section.State.graceStart);
            Assert.Equal(0, section.State.assignments.Count);
            Assert.Equal(1, section.State.nextReactionSeq);
            Assert.Equal(0, section.State.counters.year);
            Assert.Equal(2, section.State.counters.hostileExecutedThisYear, "the rest of the group is read");
            Assert.True(warnings.Count >= 4, "each problem is reported: " + string.Join("; ", warnings));
        }

        [Test]
        public void NullEntriesAndNullGroups_AreRepaired()
        {
            RivalSaveSection section = NewSection();
            section.Read(RivalTestKit.Obj(
                "{'assignments':[null,{'uid':3,'archetype':'hegemon'}],"
                + "'attitudes':[{'uid':1,'value':5,'history':null},null],"
                + "'shareSnapshot':null,'counters':null,'pending':null,'history':[null],'nextReactionSeq':-5}"), 1);

            RivalState s = section.State;
            Assert.Equal(1, s.assignments.Count);
            Assert.Equal(1, s.attitudes.Count);
            Assert.NotNull(s.attitudes[0].history);
            Assert.NotNull(s.shareSnapshot);
            Assert.NotNull(s.shareSnapshot.rows);
            Assert.NotNull(s.counters);
            Assert.NotNull(s.counters.lastByUid);
            Assert.Equal(0, s.pending.Count);
            Assert.Equal(0, s.history.Count);
            Assert.Equal(1, s.nextReactionSeq, "the number of the next reaction is at least 1");
        }

        [Test]
        public void ARowOfSharesOfTheWrongLength_IsMadeFourLong()
        {
            RivalSaveSection section = NewSection();
            section.Read(RivalTestKit.Obj("{'shareSnapshot':{'monthIndex':3,'rows':[{'key':'a','s':[0.5,0.25]},{'key':'b','s':[0.1,0.2,0.3,0.4,0.5]},{'key':'c'}]}}"), 1);

            List<ShareRow> rows = section.State.shareSnapshot.rows;
            Assert.Equal(new[] { 0.5f, 0.25f, 0f, 0f }, rows[0].s);
            Assert.Equal(new[] { 0.1f, 0.2f, 0.3f, 0.4f }, rows[1].s);
            Assert.Equal(4, rows[2].s.Length);
        }

        [Test]
        public void UnknownMembers_AreIgnored()
        {
            var warnings = new List<string>();
            RivalSaveSection section = NewSection(warnings);
            section.Read(RivalTestKit.Obj("{'graceStart':'1981-01-01','fromTheFuture':{'a':[1,2,3]}}"), 1);

            Assert.Equal("1981-01-01", section.State.graceStart);
            Assert.Equal(0, warnings.Count, string.Join("; ", warnings));
        }

        // ---- the real side-file model ----

        private static SideFileModel ModelWith(RivalSaveSection section)
        {
            var model = new SideFileModel();
            string problem;
            Assert.True(model.TryRegister(section, out problem), problem);
            return model;
        }

        private static string File1(string features)
        {
            return "{\"schema\":1,\"modVersion\":\"0.1.0\",\"gameVersion\":\"0.2.3\",\"saveName\":\"1985_3_1\",\"gameDate\":\"1985-03-01\","
                   + "\"writtenUtc\":\"2026-10-10T18:42:07Z\",\"features\":{" + features + "}}";
        }

        [Test]
        public void TheCoreWritesTheVersion_AndReadsItBack()
        {
            RivalSaveSection source = NewSection();
            source.Read(RivalSaveCodec.Write(Sample()), 1);
            SideFileModel writer = ModelWith(source);

            SideFileBuildReport built = writer.Build(null);
            JsonObject sectionInFile = built.Document.GetObject("features").GetObject(Id);
            Assert.NotNull(sectionInFile, "the section is in the file");
            Assert.Equal(1, sectionInFile.GetInt32("_schema", -1), "the core adds _schema");
            Assert.Equal("1980-01-01", sectionInFile.GetString("graceStart", null));

            RivalSaveSection target = NewSection();
            SideFileModel reader = ModelWith(target);
            SideFileReadReport report = reader.Apply(Text(built.Document));

            Assert.Equal(SideFileStatus.Loaded, report.Status);
            Assert.Equal(source.State.assignments.Count, target.State.assignments.Count);
            Assert.Equal(Text(source.Write()), Text(target.Write()));
            Assert.False(target.Write().ContainsKey("_schema"), "the core strips _schema before Read");
        }

        [Test]
        public void ASaveWithoutTheSection_GivesTheDefaultState_WithoutErrors()
        {
            // an old save, or the first run with the mod (AC-10.2)
            RivalSaveSection section = NewSection();
            section.Read(RivalSaveCodec.Write(Sample()), 1);
            SideFileModel model = ModelWith(section);

            SideFileReadReport report = model.Apply(File1("\"gpu-product-line\":{\"_schema\":1,\"x\":1}"));

            Assert.Equal(SideFileStatus.Loaded, report.Status);
            Assert.Equal(0, report.Warnings.Count, string.Join("; ", report.Warnings));
            Assert.Equal(0, section.State.assignments.Count, "the state of the previous game is gone");
            Assert.Equal(string.Empty, section.State.graceStart);
            Assert.Equal(1, section.State.nextReactionSeq);
        }

        [Test]
        public void ASaveWithoutAnySideFile_GivesTheDefaultState()
        {
            RivalSaveSection section = NewSection();
            section.Read(RivalSaveCodec.Write(Sample()), 1);
            SideFileModel model = ModelWith(section);

            model.ApplyMissing();
            Assert.Equal(0, section.State.assignments.Count);

            section.Read(RivalSaveCodec.Write(Sample()), 1);
            model.StartNewGame();
            Assert.Equal(0, section.State.assignments.Count, "a new game starts clean");
        }

        [Test]
        public void ASectionNewerThanTheMod_IsNotRead_AndIsKeptForTheFile()
        {
            RivalSaveSection section = NewSection();
            SideFileModel model = ModelWith(section);
            string newer = File1("\"" + Id + "\":{\"_schema\":2,\"graceStart\":\"1999-09-09\",\"shinyNewThing\":[1,2,3]}");

            SideFileReadReport report = model.Apply(newer);

            Assert.Equal(SideFileStatus.Newer, report.Status);
            Assert.Equal(string.Empty, section.State.graceStart, "the data of a newer version is not interpreted");

            JsonObject kept = model.Build(null).Document.GetObject("features").GetObject(Id);
            Assert.NotNull(kept, "the stored section is written back");
            Assert.Equal(2, kept.GetInt32("_schema", -1));
            Assert.Equal("1999-09-09", kept.GetString("graceStart", null));
            Assert.NotNull(kept.GetArray("shinyNewThing"), "as it was, unknown members included");
        }
    }
}
