using System;
using System.Collections.Generic;
using System.Linq;
using PTMod.Core;
using PTMod.Core.Logic.Json;
using PTMod.Core.Logic.Save;
using PTMod.Features.FirstYearOnboarding.Logic;
using PTMod.Tests.Save;

namespace PTMod.Tests.Features.FirstYearOnboarding
{
    /// <summary>
    /// The section <c>first-year-onboarding</c> of the side save file (TSK-009, FR-10, NFR-3, NFR-6): the state written and read back without loss, no
    /// field of the version in what the feature writes, a game without the section or with a section from a newer build is a game without a
    /// tutorial, the damaged values of a hand-edited file are repaired or dropped, and a step the content no longer has keeps its record.
    /// </summary>
    [Covers(typeof(OnboardingSaveCodec), typeof(OnboardingSaveSection))]
    public class OnboardingSaveCodecTests
    {
        private const string Section = OnboardingSaveCodec.SectionId;

        /// <summary>The example of the section in design.md ("OnboardingState"), with single quotes.</summary>
        private const string DesignExample =
            "{ 'mode': 'Active', 'track': 'idm', 'startDate': '1975-01-01', 'currentStepId': 'release_price', 'lastMailDate': '1975-07-14', " +
            "'lastHintDate': '1975-03-02', 'pendingCaughtUp': '', 'source': 'menu', 'steps': [" +
            "{ 'id': 'welcome', 'status': 'Done', 'shownOn': '1975-01-01', 'deliveredOn': '1975-01-01', 'doneOn': '1975-01-01', 'mailId': 100000, 'remindersShown': 0, 'streakDays': 0 }," +
            "{ 'id': 'research', 'status': 'Done', 'shownOn': '1975-01-02', 'deliveredOn': '1975-01-02', 'doneOn': '1975-01-02', 'mailId': 100001, 'remindersShown': 0, 'streakDays': 0 }," +
            "{ 'id': 'design_cpu', 'status': 'Done', 'shownOn': '1975-01-03', 'deliveredOn': '1975-01-03', 'doneOn': '1975-01-19', 'mailId': 100002, 'remindersShown': 0, 'streakDays': 0 }," +
            "{ 'id': 'dev_wait', 'status': 'Done', 'shownOn': '1975-01-20', 'deliveredOn': '1975-01-20', 'doneOn': '1975-01-20', 'mailId': 100003, 'remindersShown': 0, 'streakDays': 0 }," +
            "{ 'id': 'release_price', 'status': 'Current', 'shownOn': '1975-07-14', 'deliveredOn': '1975-07-15', 'doneOn': null, 'mailId': 100005, 'remindersShown': 0, 'streakDays': 0 }," +
            "{ 'id': 'tip_debt', 'status': 'Done', 'shownOn': '1975-05-02', 'deliveredOn': '1975-05-02', 'doneOn': '1975-05-02', 'mailId': 100004, 'remindersShown': 0, 'streakDays': 0 } ] }";

        private static OnboardingState Read(string json, List<string> problems = null)
        {
            return OnboardingSaveCodec.Read(OnbJson.Object(json), problems);
        }

        private static JsonObject RoundTrip(OnboardingState state)
        {
            return OnboardingSaveCodec.Write(state);
        }

        private static bool Mentions(List<string> problems, string part)
        {
            return problems.Any(p => p.IndexOf(part, StringComparison.Ordinal) >= 0);
        }

        // ---- the round trip ----

        [Test]
        public void TheExampleOfTheDesign_ReadsAndWritesBackWithoutLoss()
        {
            JsonObject example = OnbJson.Object(DesignExample);
            var problems = new List<string>();

            OnboardingState state = OnboardingSaveCodec.Read(example, problems);

            Assert.Equal(0, problems.Count, string.Join(" | ", problems));
            Assert.Equal(OnboardingMode.Active, state.Mode);
            Assert.Equal("idm", state.Track);
            Assert.Equal<DateTime?>(new DateTime(1975, 1, 1), state.StartDate);
            Assert.Equal("release_price", state.CurrentStepId);
            Assert.Equal<DateTime?>(new DateTime(1975, 7, 14), state.LastMailDate);
            Assert.Equal<DateTime?>(new DateTime(1975, 3, 2), state.LastHintDate);
            Assert.Equal("menu", state.Source);
            Assert.Equal(6, state.Steps.Count);
            StepState current = state.Find("release_price");
            Assert.Equal(StepStatus.Current, current.Status);
            Assert.Equal(100005, current.MailId);
            Assert.Equal<DateTime?>(new DateTime(1975, 7, 15), current.DeliveredOn);
            Assert.Null(current.DoneOn);

            JsonObject written = OnboardingSaveCodec.Write(state);
            Assert.True(example.Equals(written), "expected " + example + "\n but got " + written);
        }

        [Test]
        public void ARichState_SurvivesAWriteAndARead()
        {
            var state = new OnboardingState
            {
                Mode = OnboardingMode.Skipped,
                Track = "foundry",
                StartDate = new DateTime(1980, 2, 29),
                CurrentStepId = "first_client",
                LastMailDate = new DateTime(1981, 12, 31),
                LastHintDate = new DateTime(1981, 12, 30),
                Source = "scenario:garage-1975"
            };
            state.PendingCaughtUp.AddRange(new[] { "foundry_services", "research" });
            state.Steps.Add(new StepState
            {
                Id = "first_client",
                Status = StepStatus.Current,
                ShownOn = new DateTime(1981, 12, 31),
                DeliveredOn = new DateTime(1982, 1, 1),
                MailId = 100123,
                RemindersShown = 1,
                StreakDays = 3
            });
            state.Steps.Add(new StepState { Id = "design_cpu", Status = StepStatus.NotApplicable, DoneOn = new DateTime(1981, 5, 5) });

            OnboardingState back = OnboardingSaveCodec.Read(RoundTrip(state), null);

            Assert.Equal(OnboardingMode.Skipped, back.Mode);
            Assert.Equal("foundry", back.Track);
            Assert.Equal<DateTime?>(new DateTime(1980, 2, 29), back.StartDate);
            Assert.Equal("first_client", back.CurrentStepId);
            Assert.Equal<DateTime?>(new DateTime(1981, 12, 31), back.LastMailDate);
            Assert.Equal<DateTime?>(new DateTime(1981, 12, 30), back.LastHintDate);
            Assert.Equal("scenario:garage-1975", back.Source);
            Assert.Equal(new[] { "foundry_services", "research" }, back.PendingCaughtUp.ToArray());
            Assert.Equal(2, back.Steps.Count);
            StepState step = back.Find("first_client");
            Assert.Equal(StepStatus.Current, step.Status);
            Assert.Equal<DateTime?>(new DateTime(1981, 12, 31), step.ShownOn);
            Assert.Equal<DateTime?>(new DateTime(1982, 1, 1), step.DeliveredOn);
            Assert.Equal(100123, step.MailId);
            Assert.Equal(1, step.RemindersShown);
            Assert.Equal(3, step.StreakDays);
            Assert.Equal(StepStatus.NotApplicable, back.Find("design_cpu").Status);
            Assert.True(RoundTrip(state).Equals(RoundTrip(back)), "a second round trip changes nothing");
        }

        [Test]
        public void Write_NeverContainsAVersionField_EvenForAnEmptyOrNullState()
        {
            Assert.False(RoundTrip(new OnboardingState()).ContainsKey("_schema"));
            Assert.False(RoundTrip(null).ContainsKey("_schema"));
            Assert.False(OnboardingSaveCodec.Write(Read(DesignExample)).ContainsKey("_schema"));
            foreach (string key in new[] { "schema", "version", "_schema", "schemaVersion" })
                Assert.False(RoundTrip(Read(DesignExample)).ContainsKey(key), key);
        }

        [Test]
        public void Read_DropsAVersionFieldThatGotIn()
        {
            OnboardingState state = Read("{ '_schema': 1, 'mode': 'Completed' }");

            Assert.Equal(OnboardingMode.Completed, state.Mode);
            Assert.False(RoundTrip(state).ContainsKey("_schema"));
        }

        [Test]
        public void Write_GivesAFreshObjectEachTime_AndTheStateIsNotChanged()
        {
            OnboardingState state = Read(DesignExample);
            string before = ModJson.Write(RoundTrip(state), false);

            JsonObject first = RoundTrip(state);
            first.Set("mode", "Off");
            first.GetArray("steps").Clear();

            Assert.Equal(before, ModJson.Write(RoundTrip(state), false));
            Assert.Equal(6, state.Steps.Count);
        }

        [Test]
        public void Read_DoesNotChangeTheObjectItGot()
        {
            JsonObject section = OnbJson.Object(DesignExample);
            JsonObject copy = (JsonObject)section.DeepClone();

            Read("{ 'mode': 'nonsense', 'steps': [ 5, { 'id': 'x', 'status': 'weird' } ] }");
            OnboardingSaveCodec.Read(section, null);

            Assert.True(copy.Equals(section), "the core requires that Read leaves the section as it is");
        }

        // ---- nothing, or nonsense ----

        [Test]
        public void ANullOrEmptySection_IsAGameWithoutATutorial()
        {
            OnboardingState none = OnboardingSaveCodec.Read(null, null);
            OnboardingState empty = Read("{}");

            foreach (OnboardingState state in new[] { none, empty })
            {
                Assert.Equal(OnboardingMode.Off, state.Mode);
                Assert.Null(state.Track);
                Assert.Null(state.StartDate);
                Assert.Equal(0, state.Steps.Count);
                Assert.Equal(0, state.PendingCaughtUp.Count);
            }
        }

        [Test]
        public void ModeNames_AreReadInAnyCase_AndByNumber()
        {
            Assert.Equal(OnboardingMode.Active, Read("{ 'mode': 'active' }").Mode);
            Assert.Equal(OnboardingMode.Aborted, Read("{ 'mode': 'ABORTED' }").Mode);
            Assert.Equal(OnboardingMode.Completed, Read("{ 'mode': 3 }").Mode);
            Assert.Equal(OnboardingMode.Skipped, Read("{ 'mode': ' Skipped ' }").Mode);
        }

        [Test]
        public void AnUnknownMode_IsOff()
        {
            foreach (string bad in new[] { "'Paused'", "'7'", "'-1'", "'Active,Skipped'", "9", "-1", "1.5", "true", "[]", "{}", "''" })
            {
                var problems = new List<string>();

                OnboardingState state = Read("{ 'mode': " + bad + " }", problems);

                Assert.Equal(OnboardingMode.Off, state.Mode, bad);
                Assert.True(Mentions(problems, "mode"), "a line for " + bad + ": " + string.Join(" | ", problems));
            }
        }

        [Test]
        public void AnUnknownStatus_IsPending()
        {
            var problems = new List<string>();
            OnboardingState state = Read("{ 'mode': 'Active', 'steps': [" +
                                         "{ 'id': 'a_step', 'status': 'Finished' }, { 'id': 'b_step', 'status': 99 }, { 'id': 'c_step', 'status': '2' }," +
                                         "{ 'id': 'd_step', 'status': true }, { 'id': 'e_step' }, { 'id': 'f_step', 'status': 'done' }, { 'id': 'g_step', 'status': 3 } ] }", problems);

            Assert.Equal(StepStatus.Pending, state.Find("a_step").Status);
            Assert.Equal(StepStatus.Pending, state.Find("b_step").Status);
            Assert.Equal(StepStatus.Pending, state.Find("c_step").Status);
            Assert.Equal(StepStatus.Pending, state.Find("d_step").Status);
            Assert.Equal(StepStatus.Pending, state.Find("e_step").Status, "a missing status is pending, without a complaint");
            Assert.Equal(StepStatus.Done, state.Find("f_step").Status, "any case");
            Assert.Equal(StepStatus.Skipped, state.Find("g_step").Status, "by number");
            Assert.Equal(4, problems.Count(p => p.Contains(".status")), string.Join(" | ", problems));
        }

        [Test]
        public void AStepThatTheContentNoLongerHas_KeepsItsRecord()
        {
            var problems = new List<string>();
            OnboardingState state = Read("{ 'mode': 'Active', 'steps': [ { 'id': 'long_gone_step', 'status': 'Done', 'mailId': 100777, 'doneOn': '1975-04-04' } ] }", problems);

            JsonObject written = OnboardingSaveCodec.Write(state);

            Assert.Equal(0, problems.Count);
            Assert.Equal(1, state.Steps.Count);
            JsonObject record = (JsonObject)written.GetArray("steps")[0];
            Assert.Equal("long_gone_step", record.GetString("id"));
            Assert.Equal("Done", record.GetString("status"));
            Assert.Equal(100777, record.GetInt32("mailId"));
            Assert.Equal("1975-04-04", record.GetString("doneOn"));
        }

        [Test]
        public void ABadDate_IsDropped_WithALine()
        {
            var problems = new List<string>();
            OnboardingState state = Read("{ 'mode': 'Active', 'startDate': '1975-13-45', 'lastMailDate': '01/02/1975', 'lastHintDate': 19750101," +
                                         "'steps': [ { 'id': 'a_step', 'shownOn': '1975-01-01T00:00:00', 'deliveredOn': '', 'doneOn': 'yesterday' } ] }", problems);

            Assert.Null(state.StartDate);
            Assert.Null(state.LastMailDate);
            Assert.Null(state.LastHintDate);
            StepState step = state.Find("a_step");
            Assert.Null(step.ShownOn, "only yyyy-MM-dd is a date here");
            Assert.Null(step.DeliveredOn);
            Assert.Null(step.DoneOn);
            Assert.True(Mentions(problems, "startDate") && Mentions(problems, "lastMailDate") && Mentions(problems, "lastHintDate"), string.Join(" | ", problems));
            Assert.True(Mentions(problems, "steps[0].shownOn") && Mentions(problems, "steps[0].doneOn"), string.Join(" | ", problems));
        }

        [Test]
        public void TheNumbersOfAStep_AreBroughtIntoRange()
        {
            var problems = new List<string>();
            OnboardingState state = Read("{ 'steps': [ " +
                                         "{ 'id': 'a_step', 'mailId': -5, 'remindersShown': 3000000000, 'streakDays': 1.5 }," +
                                         "{ 'id': 'b_step', 'mailId': '100001', 'remindersShown': 2, 'streakDays': 7 }," +
                                         "{ 'id': 'c_step', 'mailId': 99999999, 'streakDays': 99999999, 'remindersShown': null } ] }", problems);

            StepState a = state.Find("a_step");
            Assert.Equal(0, a.MailId);
            Assert.Equal(0, a.RemindersShown, "a number that does not fit an int is not a count");
            Assert.Equal(0, a.StreakDays);
            Assert.Equal(0, state.Find("b_step").MailId, "a text is not a number");
            Assert.Equal(2, state.Find("b_step").RemindersShown);
            Assert.Equal(7, state.Find("b_step").StreakDays);
            Assert.Equal(99999999, state.Find("c_step").MailId, "a mail id may be large");
            Assert.Equal(1000000, state.Find("c_step").StreakDays, "brought down to the largest count");
            Assert.Equal(0, state.Find("c_step").RemindersShown);
            Assert.Equal(5, problems.Count, string.Join(" | ", problems));
        }

        [Test]
        public void BrokenRecordsOfSteps_AreDropped_TheGoodOnesStay()
        {
            var problems = new List<string>();
            OnboardingState state = Read("{ 'steps': [ 5, 'text', null, [], { 'status': 'Done' }, { 'id': '' }, { 'id': 12 }, " +
                                         "{ 'id': 'ok_step', 'status': 'Done' }, { 'id': 'ok_step', 'status': 'Skipped' } ] }", problems);

            Assert.Equal(new[] { "ok_step" }, state.Steps.Select(s => s.Id).ToArray());
            Assert.Equal(StepStatus.Done, state.Find("ok_step").Status, "the first record of an id wins");
            Assert.True(Mentions(problems, "second record"), string.Join(" | ", problems));
            Assert.True(Mentions(problems, "steps[0]") && Mentions(problems, "no id"), string.Join(" | ", problems));
        }

        [Test]
        public void StepsThatAreNotAList_AreIgnored()
        {
            var problems = new List<string>();

            OnboardingState state = Read("{ 'mode': 'Active', 'steps': { 'id': 'a_step' } }", problems);

            Assert.Equal(OnboardingMode.Active, state.Mode);
            Assert.Equal(0, state.Steps.Count);
            Assert.True(Mentions(problems, "steps"));
        }

        [Test]
        public void ARunawayList_IsCutAtTheLimit()
        {
            var steps = new JsonArray();
            for (int i = 0; i < OnboardingSaveCodec.MaxSteps + 25; i++) steps.Add(new JsonObject().Set("id", "step_" + i));
            var section = new JsonObject().Set("mode", "Active").Set("steps", steps);
            var problems = new List<string>();

            OnboardingState state = OnboardingSaveCodec.Read(section, problems);

            Assert.Equal(OnboardingSaveCodec.MaxSteps, state.Steps.Count);
            Assert.True(Mentions(problems, "more than"), string.Join(" | ", problems));
        }

        [Test]
        public void TheListOfCaughtUpSteps_IsReadFromATextOrAnArray_AndCleaned()
        {
            Assert.Equal(new[] { "research", "design_cpu" }, Read("{ 'pendingCaughtUp': 'research, design_cpu,,research ' }").PendingCaughtUp.ToArray());
            Assert.Equal(new[] { "a_step", "b_step" }, Read("{ 'pendingCaughtUp': [ 'a_step', 4, 'b_step', 'a_step' ] }").PendingCaughtUp.ToArray());
            Assert.Equal(0, Read("{ 'pendingCaughtUp': '' }").PendingCaughtUp.Count);

            var problems = new List<string>();
            Assert.Equal(0, Read("{ 'pendingCaughtUp': 7 }", problems).PendingCaughtUp.Count);
            Assert.True(Mentions(problems, "pendingCaughtUp"));
        }

        [Test]
        public void TextsOfTheWrongKindOrTooLong_AreDropped()
        {
            string tooLong = new string('x', OnboardingSaveCodec.MaxTextLength + 1);
            var problems = new List<string>();

            OnboardingState state = Read("{ 'track': 5, 'source': '" + tooLong + "', 'currentStepId': '' }", problems);

            Assert.Null(state.Track);
            Assert.Null(state.Source);
            Assert.Null(state.CurrentStepId);
            Assert.True(Mentions(problems, "track") && Mentions(problems, "source"), string.Join(" | ", problems));
        }

        [Test]
        public void ATrackTheCodecDoesNotKnow_IsKept_TheContentDecides()
        {
            Assert.Equal("some-new-track", Read("{ 'mode': 'Active', 'track': 'some-new-track' }").Track);
        }

        [Test]
        public void Read_NeverThrows_OnAnythingTheCoreCouldHandOver()
        {
            string[] samples =
            {
                "{ 'mode': 'Active', 'steps': [ { 'id': 'a_step', 'mailId': 1e400 } ] }",
                "{ 'steps': [ [ [ [ [] ] ] ] ] }",
                "{ 'mode': { 'x': { 'y': 1 } }, 'track': [ 1 ], 'startDate': {}, 'pendingCaughtUp': [ [], {} ] }"
            };

            foreach (string json in samples)
            {
                JsonNode node;
                JsonError error;
                if (!ModJson.TryParse(json.Replace('\'', '"'), out node, out error)) continue;   // 1e400 may be refused by the parser itself

                OnboardingState state = OnboardingSaveCodec.Read((JsonObject)node, new List<string>());
                Assert.NotNull(state, json);
                Assert.NotNull(RoundTrip(state), json);
            }
        }

        // ---- the participant as the core drives it ----

        [Test]
        public void TheParticipant_ReportsItsSectionAndVersion()
        {
            var section = new OnboardingSaveSection();

            Assert.Equal("first-year-onboarding", section.SectionId);
            Assert.Equal(1, section.SchemaVersion);
            Assert.Equal(OnboardingMode.Off, section.State.Mode, "before anything is read");
            Assert.Equal("first-year-onboarding", OnboardingSaveCodec.SectionId);
            Assert.Equal(1, OnboardingSaveCodec.SchemaVersion);
        }

        [Test]
        public void AGameWithoutTheSection_GetsNoTutorial()
        {
            SideFileModel model = RegisteredModel(out OnboardingSaveSection section);
            section.State.Mode = OnboardingMode.Active;   // what an earlier game left behind in memory

            SideFileReadReport report = model.Apply(SideFileTestKit.File1(string.Empty));

            Assert.Equal(SideFileStatus.Loaded, report.Status);
            Assert.Equal(OnboardingMode.Off, section.State.Mode, "AC-9.3: an old save has no tutorial");

            SideFileModel missing = RegisteredModel(out OnboardingSaveSection other);
            other.State.Mode = OnboardingMode.Active;
            Assert.Equal(SideFileStatus.Missing, missing.ApplyMissing().Status);
            Assert.Equal(OnboardingMode.Off, other.State.Mode);
        }

        [Test]
        public void ANewGame_ResetsTheTutorial()
        {
            SideFileModel model = RegisteredModel(out OnboardingSaveSection section);
            section.State.Mode = OnboardingMode.Active;
            section.State.GetOrAdd("research").Status = StepStatus.Done;

            model.StartNewGame();

            Assert.Equal(OnboardingMode.Off, section.State.Mode);
            Assert.Equal(0, section.State.Steps.Count);
        }

        [Test]
        public void ANewerSection_IsNotRead_AndTheStoredTextIsKeptAsItWas()
        {
            SideFileModel model = RegisteredModel(out OnboardingSaveSection section);
            string stored = "{\"_schema\":2,\"mode\":\"Active\",\"track\":\"idm\",\"steps\":[{\"id\":\"welcome\",\"status\":\"Done\"}],\"brandNewField\":{\"x\":[1,2,3]}}";

            SideFileReadReport report = model.Apply(SideFileTestKit.File1("\"" + Section + "\":" + stored));

            Assert.Equal(SideFileStatus.Newer, report.Status);
            Assert.Equal(new[] { Section }, report.NewerSections.ToArray());
            Assert.Equal(OnboardingMode.Off, section.State.Mode, "FR-10 item 2 / FR-9 item 7: the feature got ResetToDefaults, the tutorial is off");
            Assert.Equal(0, section.State.Steps.Count);

            SideFileBuildReport build = model.Build(null);
            SideFileTestKit.AssertJson(SideFileTestKit.Parse(stored), SideFileTestKit.Features(build)[Section]);
        }

        [Test]
        public void AStoredSection_ComesBackThroughTheModel_WithTheVersionTheCoreAdds()
        {
            SideFileModel model = RegisteredModel(out OnboardingSaveSection section);
            JsonObject stored = OnbJson.Object(DesignExample);
            JsonObject versioned = (JsonObject)stored.DeepClone();
            versioned.Set("_schema", 1);

            SideFileReadReport report = model.Apply(SideFileTestKit.File1("\"" + Section + "\":" + ModJson.Write(versioned, false)));

            Assert.Equal(SideFileStatus.Loaded, report.Status);
            Assert.Equal(OnboardingMode.Active, section.State.Mode);
            Assert.Equal(6, section.State.Steps.Count);

            JsonObject written = (JsonObject)SideFileTestKit.Features(model.Build(null))[Section];
            Assert.Equal(1, written.GetInt32("_schema", -1), "the core writes the version, the feature does not");
            written.Remove("_schema");
            Assert.True(stored.Equals(written), "expected " + stored + "\n but got " + written);
        }

        [Test]
        public void ASectionWithoutAVersion_IsReadAsVersionOne()
        {
            SideFileModel model = RegisteredModel(out OnboardingSaveSection section);

            SideFileReadReport report = model.Apply(SideFileTestKit.File1("\"" + Section + "\":" + ModJson.Write(OnbJson.Object(DesignExample), false)));

            Assert.Equal(SideFileStatus.Loaded, report.Status);
            Assert.Equal(OnboardingMode.Active, section.State.Mode);
            Assert.True(report.Warnings.Any(w => w.Contains("_schema")), "the core warns: " + string.Join(" | ", report.Warnings));
        }

        [Test]
        public void ASectionThatIsNotAnObject_IsTreatedAsMissing()
        {
            SideFileModel model = RegisteredModel(out OnboardingSaveSection section);
            section.State.Mode = OnboardingMode.Active;

            model.Apply(SideFileTestKit.File1("\"" + Section + "\":42"));

            Assert.Equal(OnboardingMode.Off, section.State.Mode);
        }

        // ---- the holder of the state ----

        [Test]
        public void TheState_IsReplacedByAReadAndAReset_AndTheSubscribersAreTold()
        {
            var warnings = new List<string>();
            var section = new OnboardingSaveSection(warnings.Add);
            int replaced = 0;
            OnboardingState seen = null;
            section.StateReplaced += () =>
            {
                replaced++;
                seen = section.State;
            };
            OnboardingState first = section.State;

            section.Read(OnbJson.Object("{ 'mode': 'Active', 'startDate': 'garbage' }"), 1);

            Assert.Equal(1, replaced);
            Assert.False(ReferenceEquals(first, section.State));
            Assert.True(ReferenceEquals(seen, section.State), "the event sees the new state");
            Assert.Equal(OnboardingMode.Active, section.State.Mode);
            Assert.True(warnings.Any(w => w.Contains("startDate")), "problems of the file go to the log: " + string.Join(" | ", warnings));

            section.ResetToDefaults();

            Assert.Equal(2, replaced);
            Assert.Equal(OnboardingMode.Off, section.State.Mode);
        }

        [Test]
        public void ASubscriberThatThrows_DoesNotFailTheLoad()
        {
            var warnings = new List<string>();
            var section = new OnboardingSaveSection(warnings.Add);
            section.StateReplaced += () => { throw new InvalidOperationException("boom"); };

            section.Read(OnbJson.Object("{ 'mode': 'Active' }"), 1);
            section.ResetToDefaults();

            Assert.Equal(2, warnings.Count(w => w.Contains("boom")));
        }

        [Test]
        public void Write_OfTheHolder_GivesTheStateOfTheGame()
        {
            var section = new OnboardingSaveSection();
            section.Read(OnbJson.Object(DesignExample), 1);

            JsonObject written = section.Write();

            Assert.True(OnbJson.Object(DesignExample).Equals(written));
            Assert.False(ReferenceEquals(written, section.Write()), "a fresh object each time");
        }

        // ---- helpers ----

        private static SideFileModel RegisteredModel(out OnboardingSaveSection section)
        {
            section = new OnboardingSaveSection();
            var model = new SideFileModel();
            string problem;
            Assert.True(model.TryRegister(section, out problem), problem);
            return model;
        }
    }
}
