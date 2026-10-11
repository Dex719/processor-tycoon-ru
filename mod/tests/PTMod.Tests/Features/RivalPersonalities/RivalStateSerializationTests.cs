using System;
using System.Collections;
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
    /// The section of the side save file in full (FR-10, NFR-3, TSK-023): the round trip without a loss, no version field in the data, the clips and the limits
    /// of the sizes at reading, a section newer than the mod, an old section, and the migrations inside <c>Read(section, schema)</c>. The identity of the participant
    /// and the first cases of the core's model are in <see cref="RivalSaveSectionTests"/>; the switching off and on is in <see cref="RivalDisableFlowTests"/>.
    /// </summary>
    [Covers(typeof(RivalStateMigrations), typeof(RivalStateSanitizer), typeof(RivalStateLimits))]
    public class RivalStateSerializationTests
    {
        private const string Id = "rival-personalities";

        // ---- a state with a value in every field of every record ----

        private static RivalState FullSample()
        {
            var state = new RivalState { graceStart = "1980-01-01", nextReactionSeq = 19 };
            state.assignments.Add(new AssignmentEntry { uid = 0, archetype = "hegemon" });
            state.assignments.Add(new AssignmentEntry { uid = 12, archetype = "foundry_partner" });
            state.revealed.Add(0);
            state.attitudes.Add(new AttitudeRecord
            {
                uid = 1,
                value = -25.4f,
                history = new List<AttitudeEntry> { new AttitudeEntry { date = "1984-03-12", delta = -10.4f, reason = "StrongLaunch:1" } },
            });
            state.attitudes.Add(new AttitudeRecord { uid = 2 });   // never set: NaN
            state.shareSnapshot = new ShareSnapshot
            {
                monthIndex = 158,
                rows = new List<ShareRow> { new ShareRow { key = "player", s = new[] { 0.10f, 0.04f, 0f, 0.02f } }, new ShareRow { key = "ai:0", s = new[] { 0.60f, 0.35f, 0f, 0.05f } } },
            };
            state.undercutCooldowns.Add(new UndercutCooldown { uid = 1, segment = 1, until = "1984-06-01" });
            state.pending.Add(new PendingReaction
            {
                id = "r-17", uid = 1, type = "PriceCut", segment = 1, trigger = "StrongLaunch", plannedOn = "1984-03-12", mailId = 100231, telegraphDays = 33, fireOn = "1984-04-20",
            });
            state.pending.Add(new PendingReaction { id = "r-18", uid = 0, type = "Smear", segment = -1, trigger = "ShareGain", plannedOn = "1984-04-01", mailId = 100240, telegraphDays = 20, fireOn = string.Empty });
            state.active.Add(new ActiveReaction { id = "r-12", uid = 0, type = "CounterLaunch", segment = 0, start = "1983-11-02", end = "1984-04-01" });
            state.priceBook.Add(new PriceBookEntry { cpuSaveId = 4711, uid = 1, reactionId = "r-17", baseTargetPrice = 239f, cutTarget = 203.15f, startedOn = "1984-04-20" });
            state.priceBook.Add(new PriceBookEntry { cpuSaveId = 4712, uid = 1, reactionId = "hist:war", baseTargetPrice = 120f, cutTarget = 100f, startedOn = "1984-05-01" });
            state.cautions.Add(new CautionEntry { uid = 4, flawId = 3, until = "1986-02-01" });
            state.counters = new Counters
            {
                year = 1984,
                hostileExecutedThisYear = 2,
                lastByUid = new List<LastReaction> { new LastReaction { uid = 0, date = "1983-11-02" } },
                executed = new List<ExecutedCount> { new ExecutedCount { uid = 0, count = 3 }, new ExecutedCount { uid = 1, count = 1 } },
                lastPartnerByUid = new List<LastReaction> { new LastReaction { uid = 12, date = "1984-01-05" } },
            };
            state.history.Add(new HistoryEntry { uid = 0, date = "1983-11-02", type = "CounterLaunch", segment = 0, outcome = "started" });
            state.dormant.Add(9);
            return state;
        }

        private static JsonObject Written(RivalState state)
        {
            return RivalSaveCodec.Write(state);
        }

        private static string Text(JsonNode node)
        {
            return ModJson.Write(node, false);
        }

        private static RivalState Read(JsonObject section, List<string> problems = null)
        {
            return RivalSaveCodec.Read(section, problems);
        }

        private static List<string> Sanitize(RivalState state)
        {
            var problems = new List<string>();
            RivalStateSanitizer.Sanitize(state, problems);
            return problems;
        }

        // ---- the round trip ----

        [Test]
        public void RoundTrip_LosesNothing_InAnyField()
        {
            RivalState original = FullSample();
            var problems = new List<string>();

            // through the text, as the file does it
            RivalState read = RivalSaveCodec.Read(RivalTestKit.ParseObject(ModJson.Write(Written(original), true)), 1, problems);

            Assert.Equal(0, problems.Count, string.Join("; ", problems));
            string difference = FirstDifference("state", original, read);
            Assert.True(difference == null, "the round trip changed " + difference);
            Assert.Equal(Text(Written(original)), Text(Written(read)), "writing what was read gives the same section");
        }

        [Test]
        public void TheSample_HasAValueInEveryFieldOfEveryRecord()
        {
            // when a field is added to a record, this fails until the sample (and so the round trip above) covers it
            var missing = new List<string>();
            CollectUncovered("state", typeof(RivalState), Written(FullSample()), missing);
            Assert.Equal(0, missing.Count, "fields of the state that the sample leaves empty: " + string.Join(", ", missing));
        }

        [Test]
        public void TheData_HasNoVersionField_AndWriteNeverHasTheSchema()
        {
            JsonObject section = Written(FullSample());
            Assert.False(section.ContainsKey("_schema"), "the core writes it");
            Assert.False(section.ContainsKey("schema") || section.ContainsKey("version"), "no version of our own");
            var names = new List<string>();
            CollectNames(section, names);
            Assert.False(names.Any(n => n.ToLowerInvariant().Contains("schema") || n.ToLowerInvariant().Contains("version")), "version-like member in the data");
        }

        [Test]
        public void RoundTrip_ThroughTheRealSideFile_KeepsTheSection_AndTheCoreAddsTheVersion()
        {
            var writer = new RivalSaveSection(Id);
            writer.Read(Written(FullSample()), 1);
            var model = new SideFileModel();
            string problem;
            Assert.True(model.TryRegister(writer, out problem), problem);
            SideFileBuildReport built = model.Build(null);
            Assert.Equal(0, built.Warnings.Count, string.Join("; ", built.Warnings));
            Assert.Equal(1, built.Document.GetObject("features").GetObject(Id).GetInt32("_schema", -1), "the core adds _schema");

            var reader = new RivalSaveSection(Id);
            var readModel = new SideFileModel();
            Assert.True(readModel.TryRegister(reader, out problem), problem);
            SideFileReadReport report = readModel.Apply(ModJson.Write(built.Document, true));

            Assert.Equal(SideFileStatus.Loaded, report.Status);
            Assert.Equal(0, report.Warnings.Count, string.Join("; ", report.Warnings));
            Assert.True(FirstDifference("state", writer.State, reader.State) == null, "the state after the file: " + FirstDifference("state", writer.State, reader.State));
        }

        [Test]
        public void AValidState_IsNotTouchedByTheChecks()
        {
            RivalState state = FullSample();
            string before = Text(Written(state));

            List<string> problems = Sanitize(state);

            Assert.Equal(0, problems.Count, string.Join("; ", problems));
            Assert.Equal(before, Text(Written(state)));
        }

        [Test]
        public void TheChecks_AreIdempotent()
        {
            RivalState state = Garbage();
            Sanitize(state);
            string once = Text(Written(state));

            List<string> second = Sanitize(state);

            Assert.Equal(0, second.Count, "a second pass finds nothing: " + string.Join("; ", second));
            Assert.Equal(once, Text(Written(state)));
        }

        // ---- old sections ----

        [Test]
        public void ASectionOfAnOlderBuildOfVersionOne_ReadsWithEmptyNewLists_AndWithoutProblems()
        {
            // written before counters.executed and counters.lastPartnerByUid existed (TSK-020, TSK-022): additive fields, the version stays 1
            JsonObject old = RivalTestKit.Obj(
                "{'graceStart':'1980-01-01','assignments':[{'uid':1,'archetype':'price_warrior'}],"
                + "'counters':{'year':1984,'hostileExecutedThisYear':1,'lastByUid':[{'uid':0,'date':'1983-11-02'}]},'nextReactionSeq':5}");
            var warnings = new List<string>();
            var section = new RivalSaveSection(Id, warnings.Add);

            section.Read(old, 1);

            Assert.Equal(0, warnings.Count, string.Join("; ", warnings));
            Assert.Equal(0, section.State.counters.executed.Count);
            Assert.Equal(0, section.State.counters.lastPartnerByUid.Count);
            Assert.Equal(1, section.State.counters.lastByUid.Count);
            Assert.Equal(5, section.State.nextReactionSeq);
            Assert.Equal("price_warrior", section.State.assignments[0].archetype);
        }

        [Test]
        public void AnEmptySection_IsTheDefaultState()
        {
            var warnings = new List<string>();
            var section = new RivalSaveSection(Id, warnings.Add);

            section.Read(new JsonObject(), 1);

            Assert.Equal(0, warnings.Count, string.Join("; ", warnings));
            Assert.Equal(string.Empty, section.State.graceStart);
            Assert.Equal(1, section.State.nextReactionSeq);
            Assert.Equal(0, section.State.assignments.Count);
        }

        [Test]
        public void TheObjectOfTheCore_IsNeverChangedByRead()
        {
            JsonObject given = RivalTestKit.Obj("{'graceStart':'nope','attitudes':[{'uid':1,'value':900}],'nextReactionSeq':-4}");
            string before = Text(given);

            new RivalSaveSection(Id).Read(given, 1);

            Assert.Equal(before, Text(given), "Read must not modify the object it is given (ISideFileParticipant.Read)");
        }

        // ---- a section newer than the mod ----

        [Test]
        public void ASectionNewerThanTheMod_GivesTheDefaults_AndTheCoreKeepsItAsItWas()
        {
            var section = new RivalSaveSection(Id);
            section.Read(Written(FullSample()), 1);
            var model = new SideFileModel();
            string problem;
            Assert.True(model.TryRegister(section, out problem), problem);
            string newer = "{\"schema\":1,\"modVersion\":\"9.0.0\",\"gameVersion\":\"0.2.3\",\"saveName\":\"s\",\"gameDate\":\"1985-03-01\",\"features\":{\""
                           + Id + "\":{\"_schema\":2,\"graceStart\":\"1999-09-09\",\"somethingNew\":{\"a\":[1,2,3]}}}}";

            SideFileReadReport report = model.Apply(newer);

            Assert.Equal(SideFileStatus.Newer, report.Status);
            Assert.True(report.Warnings.Count >= 1, "the core says so in the log");
            Assert.Equal(string.Empty, section.State.graceStart, "the defaults, not a guess at the future");
            Assert.Equal(0, section.State.attitudes.Count);

            JsonObject kept = model.Build(null).Document.GetObject("features").GetObject(Id);
            Assert.Equal(2, kept.GetInt32("_schema", -1), "the stored section is written back as it is");
            Assert.Equal("1999-09-09", kept.GetString("graceStart", null));
            Assert.NotNull(kept.GetObject("somethingNew"));
        }

        [Test]
        public void IfANewerSectionEverReachedRead_ItIsNotInterpreted()
        {
            var warnings = new List<string>();
            var section = new RivalSaveSection(Id, warnings.Add);
            section.Read(Written(FullSample()), 1);

            section.Read(RivalTestKit.Obj("{'graceStart':'1999-09-09','pending':[{'id':'r-1','uid':1,'type':'PriceCut','plannedOn':'1999-09-09'}]}"), 2);

            Assert.Equal(1, warnings.Count, string.Join("; ", warnings));
            Assert.True(warnings[0].Contains("version 2"), warnings[0]);
            Assert.Equal(string.Empty, section.State.graceStart);
            Assert.Equal(0, section.State.pending.Count);
        }

        // ---- the migrations ----

        [Test]
        public void EveryVersionOfTheSection_HasItsMigrationStep()
        {
            // there are SchemaVersion - 1 steps: none at version 1. The day SchemaVersion becomes 2 this is red until the step 1 -> 2 is written.
            Assert.Equal(RivalSaveCodec.SchemaVersion - 1, RivalStateMigrations.Steps.Count);
            Assert.Equal(1, RivalSaveCodec.SchemaVersion);
        }

        [Test]
        public void Upgrade_AtTheCurrentVersion_ReturnsTheSameObject()
        {
            JsonObject section = Written(FullSample());
            var problems = new List<string>();

            Assert.True(ReferenceEquals(section, RivalStateMigrations.Upgrade(section, 1, 1, problems)));
            Assert.True(ReferenceEquals(section, RivalStateMigrations.Upgrade(section, 1, RivalSaveCodec.SchemaVersion, problems)));
            Assert.Equal(0, problems.Count);
            Assert.Null(RivalStateMigrations.Upgrade(null, 1, 2, problems), "nothing to migrate");
        }

        // made-up changes of the layout, to try the mechanism before the first real one exists
        private static JsonObject UseNewName(JsonObject section)
        {
            // 1 -> 2: the list "dormantUids" of the old layout is "dormant" from now on
            JsonNode value = section["dormantUids"];
            section.Remove("dormantUids");
            section.Set("dormant", value);
            return section;
        }

        private static JsonObject AddField(JsonObject section)
        {
            // 2 -> 3: a field appears
            section.Set("addedInThree", 8);
            return section;
        }

        private static JsonObject OldLayoutOf(RivalState state)
        {
            JsonObject stored = Written(state);
            JsonNode dormant = stored["dormant"];
            stored.Remove("dormant");
            stored.Set("dormantUids", dormant);
            return stored;
        }

        [Test]
        public void Upgrade_RunsTheStep_OnACopy_AndTheResultReadsAsTheNewShape()
        {
            RivalState source = FullSample();
            JsonObject stored = OldLayoutOf(source);
            string untouched = Text(stored);
            var steps = new List<RivalStateMigrations.Step> { UseNewName };
            var problems = new List<string>();

            JsonObject migrated = RivalStateMigrations.Upgrade(stored, 1, 2, steps, problems);

            Assert.Equal(0, problems.Count, string.Join("; ", problems));
            Assert.Equal(untouched, Text(stored), "the object of the core is never changed");
            Assert.False(ReferenceEquals(stored, migrated));
            Assert.True(migrated.ContainsKey("dormant"));
            Assert.False(migrated.ContainsKey("dormantUids"));

            // the migrated section binds to the current DTO, the data of the other lists intact
            var readProblems = new List<string>();
            RivalState read = Read(migrated, readProblems);
            Assert.Equal(new[] { 9 }, read.dormant.ToArray());
            Assert.True(FirstDifference("state", source, read) == null, FirstDifference("state", source, read));
            Assert.Equal(0, readProblems.Count, string.Join("; ", readProblems));
        }

        [Test]
        public void Upgrade_ChainsTheSteps_AndStartsAtTheStoredVersion()
        {
            var steps = new List<RivalStateMigrations.Step> { UseNewName, AddField };

            JsonObject fromOne = RivalStateMigrations.Upgrade(OldLayoutOf(FullSample()), 1, 3, steps, null);
            Assert.True(fromOne.ContainsKey("dormant"), "step 1 -> 2 ran");
            Assert.Equal(8, fromOne.GetInt32("addedInThree", 0));

            // stored as version 2: only the step 2 -> 3 runs
            JsonObject fromTwo = RivalStateMigrations.Upgrade(Written(FullSample()), 2, 3, steps, null);
            Assert.True(fromTwo.ContainsKey("dormant"));
            Assert.Equal(8, fromTwo.GetInt32("addedInThree", 0));
        }

        [Test]
        public void Upgrade_AMissingStep_StopsTheChain_WithAReason()
        {
            JsonObject stored = Written(FullSample());
            var problems = new List<string>();
            var steps = new List<RivalStateMigrations.Step> { AddField };   // 1 -> 2 only

            JsonObject migrated = RivalStateMigrations.Upgrade(stored, 1, 3, steps, problems);

            Assert.Equal(1, problems.Count, string.Join("; ", problems));
            Assert.True(problems[0].Contains("no step from version 2 to 3"), problems[0]);
            Assert.Equal(8, migrated.GetInt32("addedInThree", 0));
            Assert.True(Read(migrated).assignments.Count > 0, "and still reads");
        }

        [Test]
        public void Upgrade_AStepThatThrows_StopsTheChain_AndTheSectionStillReads()
        {
            JsonObject stored = Written(FullSample());
            var problems = new List<string>();
            var steps = new List<RivalStateMigrations.Step> { s => { throw new InvalidOperationException("boom"); } };

            JsonObject migrated = RivalStateMigrations.Upgrade(stored, 1, 2, steps, problems);

            Assert.Equal(1, problems.Count, string.Join("; ", problems));
            Assert.True(problems[0].Contains("failed") && problems[0].Contains("boom"), problems[0]);
            Assert.NotNull(migrated);
            Assert.Equal(FullSample().assignments.Count, Read(migrated).assignments.Count);
        }

        [Test]
        public void Upgrade_AStepThatReturnsNull_KeepsTheData()
        {
            JsonObject stored = Written(FullSample());
            var steps = new List<RivalStateMigrations.Step> { s => null };

            JsonObject migrated = RivalStateMigrations.Upgrade(stored, 1, 2, steps, null);

            Assert.NotNull(migrated);
            Assert.Equal(Text(stored), Text(migrated));
        }

        [Test]
        public void Upgrade_AVersionBelowOne_IsReadAsOne_AndANewerOneAsItIs()
        {
            JsonObject stored = Written(FullSample());
            var problems = new List<string>();

            JsonObject low = RivalStateMigrations.Upgrade(stored, 0, 1, problems);
            Assert.True(ReferenceEquals(stored, low));
            Assert.Equal(1, problems.Count);
            Assert.True(problems[0].Contains("below 1"), problems[0]);

            problems.Clear();
            JsonObject high = RivalStateMigrations.Upgrade(stored, 5, 1, problems);
            Assert.True(ReferenceEquals(stored, high));
            Assert.True(problems[0].Contains("newer"), problems[0]);
        }

        [Test]
        public void ReadOfTheCurrentVersion_GoesThroughTheMigrations_AndTheChecks()
        {
            var problems = new List<string>();

            RivalState state = RivalSaveCodec.Read(RivalTestKit.Obj("{'graceStart':'soon','attitudes':[{'uid':1,'value':400}]}"), 1, problems);

            Assert.Equal(string.Empty, state.graceStart);
            Assert.Equal(100f, state.attitudes[0].value);
            Assert.Equal(2, problems.Count, string.Join("; ", problems));
        }

        // ---- the clips and the limits ----

        [Test]
        public void TheGracePeriod_WithAnUnreadableStart_StartsAgain()
        {
            // an unreadable start would hold every reaction back for good: the planner counts 0 days from it
            var state = new RivalState { graceStart = "01.01.1980" };

            List<string> problems = Sanitize(state);

            Assert.Equal(string.Empty, state.graceStart);
            Assert.Equal(1, problems.Count);
            Assert.True(problems[0].StartsWith("graceStart:"), problems[0]);

            var good = new RivalState { graceStart = "1980-01-01" };
            Assert.Equal(0, Sanitize(good).Count);
            Assert.Equal("1980-01-01", good.graceStart);
        }

        [Test]
        public void Dates_AreReadStrictly()
        {
            foreach (string bad in new[] { "1980-1-1", "1980/01/01", "1980-13-01", "1980-02-30", " 1980-01-01", "1980-01-01T00:00:00", "yesterday", "0" })
            {
                var state = new RivalState();
                state.cautions.Add(new CautionEntry { uid = 1, flawId = 1, until = bad });
                Sanitize(state);
                Assert.Equal(0, state.cautions.Count, "'" + bad + "' is not a date");
            }
        }

        [Test]
        public void Assignments_OnePerCompany_WithAnArchetype_AtMost64()
        {
            var state = new RivalState();
            state.assignments.Add(new AssignmentEntry { uid = -1, archetype = "hegemon" });
            state.assignments.Add(new AssignmentEntry { uid = 3, archetype = string.Empty });
            state.assignments.Add(new AssignmentEntry { uid = 4, archetype = "hegemon" });
            state.assignments.Add(new AssignmentEntry { uid = 4, archetype = "price_warrior" });
            state.assignments.Add(new AssignmentEntry { uid = 5, archetype = new string('x', 200) });
            for (int uid = 10; uid < 200; uid++) state.assignments.Add(new AssignmentEntry { uid = uid, archetype = "conservative" });

            List<string> problems = Sanitize(state);

            Assert.Equal(RivalStateLimits.MaxCompanies, state.assignments.Count);
            Assert.Equal("hegemon", state.assignments.Single(a => a.uid == 4).archetype, "the first record of a company wins");
            Assert.Equal(RivalStateLimits.MaxTextLength, state.assignments.Single(a => a.uid == 5).archetype.Length);
            Assert.False(state.assignments.Any(a => a.uid < 0 || a.uid == 3));
            Assert.True(problems.Count <= 2, "one line for the dropped, one for the corrected: " + string.Join("; ", problems));
        }

        [Test]
        public void Revealed_AreUnique_AndAtMostThirteen()
        {
            var state = new RivalState();
            state.revealed.AddRange(new[] { 3, 3, -1, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17 });

            List<string> problems = Sanitize(state);

            Assert.Equal(RivalStateLimits.MaxRevealed, state.revealed.Count);
            Assert.Equal(13, RivalStateLimits.MaxRevealed);
            Assert.Equal(state.revealed.Distinct().Count(), state.revealed.Count);
            Assert.False(state.revealed.Contains(-1));
            Assert.Equal(1, problems.Count, string.Join("; ", problems));
        }

        [Test]
        public void Attitudes_AreClippedToTheScale_AndNotANumberStays()
        {
            var state = new RivalState();
            state.attitudes.Add(new AttitudeRecord { uid = 1, value = 250f });
            state.attitudes.Add(new AttitudeRecord { uid = 2, value = -1e9f });
            state.attitudes.Add(new AttitudeRecord { uid = 3, value = float.PositiveInfinity });
            state.attitudes.Add(new AttitudeRecord { uid = 4, value = float.NaN });
            state.attitudes.Add(new AttitudeRecord { uid = 5, value = -37.5f });
            state.attitudes.Add(new AttitudeRecord { uid = 5, value = 99f });
            state.attitudes.Add(new AttitudeRecord { uid = -2, value = 1f });

            List<string> problems = Sanitize(state);

            Assert.Equal(5, state.attitudes.Count);
            Assert.Equal(100f, state.attitudes.Single(a => a.uid == 1).value);
            Assert.Equal(-100f, state.attitudes.Single(a => a.uid == 2).value);
            Assert.Equal(100f, state.attitudes.Single(a => a.uid == 3).value);
            Assert.True(float.IsNaN(state.attitudes.Single(a => a.uid == 4).value), "never set: the baseline of the archetype is given later");
            Assert.Equal(-37.5f, state.attitudes.Single(a => a.uid == 5).value, "the first record of a company wins");
            Assert.True(problems.Count >= 1);
        }

        [Test]
        public void AttitudeHistory_KeepsTheLast20_DropsUnreadableDates_ClipsDeltas_AndCutsReasons()
        {
            var record = new AttitudeRecord { uid = 1, value = 0f };
            record.history.Add(new AttitudeEntry { date = "garbage", delta = 1f, reason = "x" });
            for (int i = 0; i < 30; i++)
            {
                record.history.Add(new AttitudeEntry { date = new DateTime(1980, 1, 1).AddDays(i).ToString("yyyy-MM-dd"), delta = i, reason = "r" + i });
            }
            record.history.Add(new AttitudeEntry { date = "1981-01-01", delta = float.NaN, reason = new string('y', 500) });
            record.history.Add(new AttitudeEntry { date = "1981-01-02", delta = 5000f, reason = null });
            var state = new RivalState();
            state.attitudes.Add(record);

            Sanitize(state);

            List<AttitudeEntry> history = state.attitudes[0].history;
            Assert.Equal(RivalStateLimits.MaxAttitudeHistory, history.Count);
            Assert.Equal(20, RivalStateLimits.MaxAttitudeHistory);
            Assert.Equal("1981-01-02", history[history.Count - 1].date, "the newest lines stay");
            Assert.Equal(RivalStateLimits.MaxDelta, history[history.Count - 1].delta, "5000 is clipped");
            Assert.Equal(string.Empty, history[history.Count - 1].reason, "a null reason is empty");
            Assert.Equal(0f, history[history.Count - 2].delta, "not a number is 0");
            Assert.Equal(64, history[history.Count - 2].reason.Length);
            Assert.False(history.Any(h => h.date == "garbage"));
        }

        [Test]
        public void Shares_AreBetweenZeroAndOne_WithOneRowPerKey()
        {
            var state = new RivalState();
            state.shareSnapshot = new ShareSnapshot
            {
                monthIndex = -4,
                rows = new List<ShareRow>
                {
                    new ShareRow { key = "player", s = new[] { 1.5f, -0.2f, float.NaN, 0.5f } },
                    new ShareRow { key = "player", s = new[] { 0.1f, 0.1f, 0.1f, 0.1f } },
                    new ShareRow { key = string.Empty, s = new float[4] },
                    new ShareRow { key = new string('k', 100), s = new float[4] },
                    new ShareRow { key = "ai:0", s = new[] { 0.5f } },
                },
            };

            Sanitize(state);

            Assert.Equal(0, state.shareSnapshot.monthIndex);
            Assert.Equal(2, state.shareSnapshot.rows.Count);
            Assert.Equal(new[] { 1f, 0f, 0f, 0.5f }, state.shareSnapshot.rows[0].s);
            Assert.Equal(new[] { 0.5f, 0f, 0f, 0f }, state.shareSnapshot.rows[1].s);
        }

        [Test]
        public void UndercutCooldowns_NeedASegment_ADate_AndAreOnePerPair()
        {
            var state = new RivalState();
            state.undercutCooldowns.Add(new UndercutCooldown { uid = 1, segment = 4, until = "1984-06-01" });
            state.undercutCooldowns.Add(new UndercutCooldown { uid = 1, segment = -1, until = "1984-06-01" });
            state.undercutCooldowns.Add(new UndercutCooldown { uid = 1, segment = 1, until = "nope" });
            state.undercutCooldowns.Add(new UndercutCooldown { uid = 1, segment = 2, until = "1984-06-01" });
            state.undercutCooldowns.Add(new UndercutCooldown { uid = 1, segment = 2, until = "1984-09-01" });
            state.undercutCooldowns.Add(new UndercutCooldown { uid = -1, segment = 2, until = "1984-09-01" });

            Sanitize(state);

            Assert.Equal(1, state.undercutCooldowns.Count);
            Assert.Equal("1984-09-01", state.undercutCooldowns[0].until, "the later end of the same pair wins");
        }

        [Test]
        public void PendingReactions_NeedAnId_ACompany_ATypeAndDates_AreUnique_AndAtMost20()
        {
            var state = new RivalState();
            state.pending.Add(Pending("r-1"));
            state.pending.Add(Pending(null));
            state.pending.Add(Pending(new string('i', 80)));
            state.pending.Add(Pending("r-1"));                                   // the id again
            state.pending.Add(Pending("r-2", uid: -1));
            state.pending.Add(Pending("r-3", type: string.Empty));
            state.pending.Add(Pending("r-4", plannedOn: "never"));
            state.pending.Add(Pending("r-5", fireOn: "tomorrow"));
            state.pending.Add(Pending("r-6", fireOn: string.Empty));             // the mail is not delivered yet: allowed
            state.pending.Add(Pending("r-7", telegraph: 9999, mail: -5, segment: 9));
            for (int i = 100; i < 200; i++) state.pending.Add(Pending("r-" + i));

            List<string> problems = Sanitize(state);

            Assert.Equal(RivalStateLimits.MaxPending, state.pending.Count);
            Assert.Equal(20, RivalStateLimits.MaxPending);
            Assert.Equal(new[] { "r-1", "r-6", "r-7" }, state.pending.Take(3).Select(p => p.id).ToArray());
            PendingReaction seven = state.pending.Single(p => p.id == "r-7");
            Assert.Equal(RivalStateLimits.MaxTelegraphDays, seven.telegraphDays);
            Assert.Equal(0, seven.mailId);
            Assert.Equal(-1, seven.segment);
            Assert.Equal(string.Empty, state.pending.Single(p => p.id == "r-6").fireOn);
            Assert.True(problems.Count <= 3, "a few lines, not one per record: " + string.Join("; ", problems));
        }

        private static PendingReaction Pending(string id, int uid = 1, string type = "PriceCut", string plannedOn = "1984-03-12", string fireOn = "1984-04-20", int telegraph = 20, int mail = 7, int segment = 1)
        {
            return new PendingReaction { id = id, uid = uid, type = type, segment = segment, trigger = "StrongLaunch", plannedOn = plannedOn, mailId = mail, telegraphDays = telegraph, fireOn = fireOn };
        }

        [Test]
        public void ActiveReactions_NeedAnId_AnEndDate_AreUnique_AndAtMost20()
        {
            var state = new RivalState();
            state.active.Add(new ActiveReaction { id = "r-1", uid = 0, type = "PriceCut", segment = 1, start = "1984-01-01", end = "1984-06-30" });
            state.active.Add(new ActiveReaction { id = "r-1", uid = 0, type = "PriceCut", segment = 1, start = "1984-01-01", end = "1984-06-30" });
            state.active.Add(new ActiveReaction { id = "r-2", uid = 0, type = "PriceCut", segment = 1, start = "1984-01-01", end = string.Empty });
            state.active.Add(new ActiveReaction { id = "r-3", uid = 0, type = "PriceCut", segment = 1, start = "1984-01-01", end = "whenever" });
            state.active.Add(new ActiveReaction { id = "r-4", uid = 0, type = "PriceCut", segment = 1, start = "bad", end = "1984-06-30" });
            state.active.Add(new ActiveReaction { id = "r-5", uid = 0, type = "PriceCut", segment = 1, start = string.Empty, end = "1984-06-30" });   // the executor fills a missing start
            state.active.Add(new ActiveReaction { id = "r-6", uid = 0, type = string.Empty, segment = 1, start = "1984-01-01", end = "1984-06-30" });
            for (int i = 100; i < 150; i++) state.active.Add(new ActiveReaction { id = "r-" + i, uid = 0, type = "OemPush", segment = -1, start = "1984-01-01", end = "1984-12-31" });

            Sanitize(state);

            Assert.Equal(RivalStateLimits.MaxActive, state.active.Count);
            Assert.Equal(new[] { "r-1", "r-5" }, state.active.Take(2).Select(a => a.id).ToArray());
        }

        [Test]
        public void ThePriceBook_NeverHoldsANumberThatIsNotAPrice()
        {
            // the way back writes baseTargetPrice into the CPU of the game
            var state = new RivalState();
            state.priceBook.Add(new PriceBookEntry { cpuSaveId = 1, uid = 1, reactionId = "r-1", baseTargetPrice = 239f, cutTarget = 203f, startedOn = "1984-04-20" });
            state.priceBook.Add(new PriceBookEntry { cpuSaveId = 2, uid = 1, reactionId = "r-1", baseTargetPrice = float.NaN, cutTarget = 203f, startedOn = "1984-04-20" });
            state.priceBook.Add(new PriceBookEntry { cpuSaveId = 3, uid = 1, reactionId = "r-1", baseTargetPrice = 239f, cutTarget = 0f, startedOn = "1984-04-20" });
            state.priceBook.Add(new PriceBookEntry { cpuSaveId = 4, uid = 1, reactionId = "r-1", baseTargetPrice = -5f, cutTarget = 3f, startedOn = "1984-04-20" });
            state.priceBook.Add(new PriceBookEntry { cpuSaveId = 5, uid = 1, reactionId = "r-1", baseTargetPrice = float.PositiveInfinity, cutTarget = 3f, startedOn = "1984-04-20" });
            state.priceBook.Add(new PriceBookEntry { cpuSaveId = 6, uid = 1, reactionId = "r-1", baseTargetPrice = 5e8f, cutTarget = 3f, startedOn = "1984-04-20" });
            state.priceBook.Add(new PriceBookEntry { cpuSaveId = -1, uid = 1, reactionId = "r-1", baseTargetPrice = 239f, cutTarget = 203f, startedOn = "1984-04-20" });
            state.priceBook.Add(new PriceBookEntry { cpuSaveId = 8, uid = -1, reactionId = "r-1", baseTargetPrice = 239f, cutTarget = 203f, startedOn = "1984-04-20" });
            state.priceBook.Add(new PriceBookEntry { cpuSaveId = 9, uid = 1, reactionId = string.Empty, baseTargetPrice = 239f, cutTarget = 203f, startedOn = "1984-04-20" });
            state.priceBook.Add(new PriceBookEntry { cpuSaveId = 1, uid = 1, reactionId = "r-1", baseTargetPrice = 100f, cutTarget = 90f, startedOn = "1984-04-20" });   // CPU and reaction again
            state.priceBook.Add(new PriceBookEntry { cpuSaveId = 1, uid = 1, reactionId = "hist:war", baseTargetPrice = 239f, cutTarget = 190f, startedOn = "last spring" });

            Sanitize(state);

            Assert.Equal(new[] { 1, 1 }, state.priceBook.Select(e => e.cpuSaveId).ToArray());
            Assert.Equal(239f, state.priceBook[0].baseTargetPrice, "the first record of a CPU and a reaction wins");
            Assert.Equal(string.Empty, state.priceBook[1].startedOn, "a day that is not a date is empty: the book counts from the first look");
        }

        [Test]
        public void ThePriceBook_IsLimited()
        {
            var state = new RivalState();
            for (int i = 0; i < 1500; i++) state.priceBook.Add(new PriceBookEntry { cpuSaveId = i, uid = 1, reactionId = "r-1", baseTargetPrice = 100f, cutTarget = 90f, startedOn = "1984-04-20" });

            Sanitize(state);

            Assert.Equal(RivalStateLimits.MaxPriceBook, state.priceBook.Count);
        }

        [Test]
        public void Cautions_OnePerFlaw_AtMost50_TheOnesThatEndFirstGoFirst()
        {
            var state = new RivalState();
            state.cautions.Add(new CautionEntry { uid = 1, flawId = 1, until = "1990-01-01" });
            state.cautions.Add(new CautionEntry { uid = 1, flawId = 1, until = "1991-01-01" });   // the same flaw: the later end
            for (int i = 0; i < 70; i++)
            {
                state.cautions.Add(new CautionEntry { uid = 2, flawId = 100 + i, until = new DateTime(1985, 1, 1).AddDays(i).ToString("yyyy-MM-dd") });
            }

            Sanitize(state);

            Assert.Equal(RivalStateLimits.MaxCautions, state.cautions.Count);
            Assert.Equal(50, RivalStateLimits.MaxCautions);
            CautionEntry flaw1 = state.cautions.Single(c => c.uid == 1);
            Assert.Equal("1991-01-01", flaw1.until);
            Assert.False(state.cautions.Any(c => c.flawId == 100), "the one that ends first is gone");
            Assert.True(state.cautions.Any(c => c.flawId == 169), "the one that ends last stays");
        }

        [Test]
        public void Counters_AreClipped_AndTheListsAreOnePerCompany()
        {
            var state = new RivalState();
            state.counters = new Counters
            {
                year = 70000,
                hostileExecutedThisYear = -4,
                lastByUid = new List<LastReaction>
                {
                    new LastReaction { uid = 1, date = "1984-01-01" }, new LastReaction { uid = 1, date = "1984-02-01" },
                    new LastReaction { uid = 2, date = "oops" }, new LastReaction { uid = -3, date = "1984-01-01" },
                },
                executed = new List<ExecutedCount>
                {
                    new ExecutedCount { uid = 1, count = -2 }, new ExecutedCount { uid = 1, count = 5 },
                    new ExecutedCount { uid = 2, count = int.MaxValue }, new ExecutedCount { uid = -9, count = 1 },
                },
                lastPartnerByUid = new List<LastReaction> { new LastReaction { uid = 12, date = "1984-01-05" }, new LastReaction { uid = 12, date = "1984-03-05" } },
            };

            Sanitize(state);

            Assert.Equal(0, state.counters.year);
            Assert.Equal(0, state.counters.hostileExecutedThisYear);
            Assert.Equal(1, state.counters.lastByUid.Count);
            Assert.Equal("1984-01-01", state.counters.lastByUid[0].date, "the first record of a company wins");
            Assert.Equal(2, state.counters.executed.Count);
            Assert.Equal(0, state.counters.executed.Single(e => e.uid == 1).count);
            Assert.Equal(RivalStateLimits.MaxCounter, state.counters.executed.Single(e => e.uid == 2).count);
            Assert.Equal(1, state.counters.lastPartnerByUid.Count);

            var many = new RivalState();
            for (int uid = 0; uid < 200; uid++) many.counters.executed.Add(new ExecutedCount { uid = uid, count = 1 });
            Sanitize(many);
            Assert.Equal(RivalStateLimits.MaxCompanies, many.counters.executed.Count);
        }

        [Test]
        public void TheHistory_KeepsTheLast100Lines_WithReadableDates()
        {
            var state = new RivalState();
            state.history.Add(new HistoryEntry { uid = 0, date = "garbage", type = "x", outcome = "started" });
            state.history.Add(new HistoryEntry { uid = -1, date = "1980-01-01", type = "x", outcome = "started" });
            for (int i = 0; i < 150; i++)
            {
                state.history.Add(new HistoryEntry { uid = i % 13, date = new DateTime(1980, 1, 1).AddDays(i).ToString("yyyy-MM-dd"), type = null, segment = 7, outcome = "planned" });
            }

            Sanitize(state);

            Assert.Equal(RivalStateLimits.MaxHistory, state.history.Count);
            Assert.Equal(100, RivalStateLimits.MaxHistory);
            Assert.Equal(new DateTime(1980, 1, 1).AddDays(149).ToString("yyyy-MM-dd"), state.history[99].date, "the newest line is last");
            Assert.Equal(new DateTime(1980, 1, 1).AddDays(50).ToString("yyyy-MM-dd"), state.history[0].date, "the oldest lines went");
            Assert.Equal(string.Empty, state.history[0].type);
            Assert.Equal(-1, state.history[0].segment);
        }

        [Test]
        public void Dormant_AreUniqueCompanies()
        {
            var state = new RivalState();
            state.dormant.AddRange(new[] { 4, 4, -1, 5 });

            Sanitize(state);

            Assert.Equal(new[] { 4, 5 }, state.dormant.ToArray());
        }

        [Test]
        public void TheNextReactionNumber_IsAboveEveryIdInUse_AndBelowTheCeiling()
        {
            var state = new RivalState { nextReactionSeq = 3 };
            state.pending.Add(Pending("r-40"));
            state.active.Add(new ActiveReaction { id = "r-77", uid = 0, type = "PriceCut", segment = 1, start = "1984-01-01", end = "1984-06-30" });
            state.priceBook.Add(new PriceBookEntry { cpuSaveId = 1, uid = 1, reactionId = "r-90", baseTargetPrice = 100f, cutTarget = 90f, startedOn = "1984-04-20" });
            state.priceBook.Add(new PriceBookEntry { cpuSaveId = 2, uid = 1, reactionId = "hist:war-7000", baseTargetPrice = 100f, cutTarget = 90f, startedOn = "1984-04-20" });

            List<string> problems = Sanitize(state);

            Assert.Equal(91, state.nextReactionSeq, "r-90 is the highest id of the feature; 'hist:war-7000' is somebody else's");
            Assert.True(problems.Any(p => p.StartsWith("nextReactionSeq:")), string.Join("; ", problems));

            var huge = new RivalState { nextReactionSeq = int.MaxValue };
            Sanitize(huge);
            Assert.Equal(RivalStateLimits.MaxSequence, huge.nextReactionSeq);

            var negative = new RivalState { nextReactionSeq = -7 };
            Sanitize(negative);
            Assert.Equal(1, negative.nextReactionSeq);
        }

        [Test]
        public void ADamagedFile_IsAShortParagraph_NotAHundredLines()
        {
            var state = Garbage();

            List<string> problems = Sanitize(state);

            Assert.True(problems.Count > 3, "it was damaged");
            Assert.True(problems.Count <= 40, problems.Count + " lines for 14 lists: " + string.Join("; ", problems.Take(5)));
            Assert.True(problems.All(p => p.Length < 400), "each line is short");
        }

        [Test]
        public void AHugeDamagedList_IsCutInAFewSeconds_NotInHours()
        {
            // the file is at most 8 MB, so a list can hold some hundred thousand records; the checks must stay near-linear
            var state = new RivalState();
            for (int i = 0; i < 60000; i++)
            {
                state.cautions.Add(new CautionEntry { uid = i % 13, flawId = i, until = new DateTime(1980, 1, 1).AddDays(i % 3000).ToString("yyyy-MM-dd") });
                state.priceBook.Add(new PriceBookEntry { cpuSaveId = i, uid = 1, reactionId = "r-1", baseTargetPrice = 100f, cutTarget = 90f, startedOn = "1984-04-20" });
                state.history.Add(new HistoryEntry { uid = 1, date = "1984-04-20", type = "PriceCut", segment = 1, outcome = "planned" });
                state.assignments.Add(new AssignmentEntry { uid = i, archetype = "conservative" });
                state.undercutCooldowns.Add(new UndercutCooldown { uid = i, segment = i % 4, until = "1984-06-01" });
                state.pending.Add(Pending("r-" + i));
            }

            var watch = System.Diagnostics.Stopwatch.StartNew();
            Sanitize(state);
            watch.Stop();

            Assert.Equal(RivalStateLimits.MaxCautions, state.cautions.Count);
            Assert.Equal(RivalStateLimits.MaxPriceBook, state.priceBook.Count);
            Assert.Equal(RivalStateLimits.MaxHistory, state.history.Count);
            Assert.Equal(RivalStateLimits.MaxCompanies, state.assignments.Count);
            Assert.Equal(RivalStateLimits.MaxCooldowns, state.undercutCooldowns.Count);
            Assert.Equal(RivalStateLimits.MaxPending, state.pending.Count);
            Assert.True(watch.ElapsedMilliseconds < 5000, "took " + watch.ElapsedMilliseconds + " ms");
            Assert.True(state.cautions.All(c => string.CompareOrdinal(c.until, new DateTime(1980, 1, 1).AddDays(2900).ToString("yyyy-MM-dd")) >= 0), "the ones that end last stay");
        }

        [Test]
        public void ARecordListWithNullsAndNullGroups_DoesNotThrow()
        {
            var state = new RivalState
            {
                assignments = null, revealed = null, attitudes = null, shareSnapshot = null, undercutCooldowns = null, pending = null, active = null,
                priceBook = null, cautions = null, counters = null, history = null, dormant = null, graceStart = null,
            };

            RivalState same = RivalStateSanitizer.Sanitize(state, null);

            Assert.True(ReferenceEquals(state, same));
            Assert.NotNull(state.counters);
            Assert.NotNull(state.counters.executed);
            Assert.NotNull(state.shareSnapshot);
            Assert.Equal(string.Empty, state.graceStart);
            Assert.Equal(0, state.pending.Count);

            var withNulls = new RivalState();
            withNulls.attitudes.Add(null);
            withNulls.attitudes.Add(new AttitudeRecord { uid = 1, history = null });
            withNulls.pending.Add(null);
            withNulls.shareSnapshot.rows.Add(null);
            RivalStateSanitizer.Sanitize(withNulls, null);
            Assert.Equal(1, withNulls.attitudes.Count);
            Assert.NotNull(withNulls.attitudes[0].history);

            Assert.NotNull(RivalStateSanitizer.Sanitize(null, null), "a missing state is a default one");
        }

        [Test]
        public void TheLimits_AreTheOnesOfTheDesign()
        {
            Assert.Equal(ReactionBook.MaxHistory, RivalStateLimits.MaxHistory);
            Assert.Equal(ReactionBook.MaxPending, RivalStateLimits.MaxPending);
            Assert.Equal(ReactionBook.MaxActive, RivalStateLimits.MaxActive);
            Assert.Equal(CautionBook.MaxEntries, RivalStateLimits.MaxCautions);
            Assert.Equal(AttitudeLedger.MaxReasonLength, 64);
            Assert.Equal(PriceCutMath.MaxReactionIdLength, RivalStateLimits.MaxIdLength);
        }

        // ---- a damaged state ----

        private static RivalState Garbage()
        {
            var state = new RivalState { graceStart = "?", nextReactionSeq = -1 };
            for (int i = 0; i < 300; i++)
            {
                state.assignments.Add(new AssignmentEntry { uid = i % 5 - 1, archetype = i % 7 == 0 ? null : "x" });
                state.revealed.Add(i % 20 - 2);
                state.attitudes.Add(new AttitudeRecord { uid = i % 30 - 3, value = i * 7f, history = new List<AttitudeEntry> { new AttitudeEntry { date = "x", delta = 1e9f, reason = new string('r', 200) } } });
                state.undercutCooldowns.Add(new UndercutCooldown { uid = i, segment = i, until = "x" });
                state.pending.Add(Pending(i % 3 == 0 ? null : "r-" + i, plannedOn: i % 2 == 0 ? "x" : "1984-01-01"));
                state.active.Add(new ActiveReaction { id = "r-" + i, uid = -1, type = "", segment = 99, start = "x", end = "y" });
                state.priceBook.Add(new PriceBookEntry { cpuSaveId = -i, uid = i, reactionId = "", baseTargetPrice = float.NaN, cutTarget = -1f, startedOn = "z" });
                state.cautions.Add(new CautionEntry { uid = -1, flawId = i, until = "q" });
                state.history.Add(new HistoryEntry { uid = -1, date = "w", type = new string('t', 90), segment = 50, outcome = "o" });
                state.dormant.Add(-i);
            }
            state.shareSnapshot.rows.Add(new ShareRow { key = "", s = null });
            state.counters.year = -5;
            state.counters.executed.Add(new ExecutedCount { uid = -1, count = -1 });
            return state;
        }

        // ---- reflection helpers ----

        /// <summary>The path of the first public field that differs between two DTO trees, or null when they are equal (NaN equals NaN).</summary>
        private static string FirstDifference(string path, object a, object b)
        {
            if (a == null || b == null) return a == b ? null : path + " (null)";

            Type type = a.GetType();
            if (type != b.GetType()) return path + " (type)";
            if (type.IsPrimitive || type == typeof(string) || type.IsEnum)
            {
                return Equals(a, b) ? null : path + " (" + a + " != " + b + ")";
            }
            if (a is IList)
            {
                var la = (IList)a;
                var lb = (IList)b;
                if (la.Count != lb.Count) return path + " (count " + la.Count + " != " + lb.Count + ")";
                for (int i = 0; i < la.Count; i++)
                {
                    string inner = FirstDifference(path + "[" + i + "]", la[i], lb[i]);
                    if (inner != null) return inner;
                }
                return null;
            }

            foreach (FieldInfo field in type.GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                string inner = FirstDifference(path + "." + field.Name, field.GetValue(a), field.GetValue(b));
                if (inner != null) return inner;
            }
            return null;
        }

        private static void CollectNames(JsonNode node, List<string> names)
        {
            var obj = node as JsonObject;
            if (obj != null)
            {
                foreach (KeyValuePair<string, JsonNode> member in obj)
                {
                    names.Add(member.Key);
                    CollectNames(member.Value, names);
                }
                return;
            }
            var array = node as JsonArray;
            if (array == null) return;
            foreach (JsonNode item in array) CollectNames(item, names);
        }

        /// <summary>Every public field of <paramref name="type"/> (and of the records in its lists) must be a member of a written record, and a list must have a record.</summary>
        private static void CollectUncovered(string path, Type type, JsonNode written, List<string> missing)
        {
            var obj = written as JsonObject;
            if (obj == null)
            {
                missing.Add(path);
                return;
            }

            foreach (FieldInfo field in type.GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                string here = path + "." + field.Name;
                JsonNode member;
                if (!obj.TryGetValue(field.Name, out member)) { missing.Add(here); continue; }

                Type fieldType = field.FieldType;
                if (fieldType.IsGenericType && fieldType.GetGenericTypeDefinition() == typeof(List<>))
                {
                    Type element = fieldType.GetGenericArguments()[0];
                    var array = member as JsonArray;
                    if (array == null || array.Count == 0) { missing.Add(here + " (empty list)"); continue; }
                    if (element.IsPrimitive) continue;

                    JsonNode first;
                    array.TryGetValue(0, out first);
                    CollectUncovered(here + "[0]", element, first, missing);
                }
                else if (fieldType.IsClass && fieldType != typeof(string) && !fieldType.IsArray)
                {
                    CollectUncovered(here, fieldType, member, missing);
                }
            }
        }
    }
}
