using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using PTMod.Core.Logic.Json;
using PTMod.Features.ScenariosAchievements.Logic;
using PTMod.Tests.Architecture;
using static PTMod.Tests.Features.ScenariosAchievements.RunTestData;

namespace PTMod.Tests.Features.ScenariosAchievements
{
    /// <summary>
    /// TSK-015: the model of the profile over the live <c>JsonObject</c> of the section (FR-13, NFR-4) - no files, no Unity. An achievement is
    /// unlocked once, the best result of a scenario is replaced only by a better clean win of a standard scenario, the history keeps the last 50,
    /// "reset" empties the section, and <c>Revision</c> moves only when the section changed (what the gate reports to <c>ModProfile.MarkDirty</c>).
    /// </summary>
    [Covers(typeof(ProfileModel), typeof(ProfileView), typeof(AchievementEntry), typeof(ProfileBest), typeof(ProfileScenarioRecord),
        typeof(ProfileChallengeRecord), typeof(ProfileHistoryEntry), typeof(ProfileStats))]
    public class ProfileModelTests
    {
        private static readonly DateTime Clock = new DateTime(2026, 10, 10, 19, 1, 0, DateTimeKind.Utc);

        private static ProfileModel Model(JsonObject section = null)
        {
            return new ProfileModel(section ?? new JsonObject(), () => Clock);
        }

        private static AchievementEntry Entry(string runId = "run-1", bool assisted = false, string company = "Garage CPU Inc.")
        {
            return new AchievementEntry
            {
                UnlockedAt = new DateTime(2026, 10, 10, 18, 22, 5, DateTimeKind.Utc),
                GameDate = D(1975, 8, 14),
                Company = company,
                RunId = runId,
                Assisted = assisted,
            };
        }

        private static ScenarioResult Won(int score, string medal = "silver")
        {
            return new ScenarioResult { Outcome = ScenarioStatus.Won, Score = score, Medal = medal, FinishedOn = D(1977, 1, 1) };
        }

        private static ScenarioResult Lost()
        {
            return new ScenarioResult { Outcome = ScenarioStatus.Lost, Score = 0, Medal = ScoreCalculator.MedalNone, FinishedOn = D(1976, 5, 5) };
        }

        private static RunInfo Info(string scenarioId = "garage-1975", bool assisted = false, bool custom = false, string runId = "run-1",
                                    params string[] challenges)
        {
            return new RunInfo { RunId = runId, ScenarioId = scenarioId, Assisted = assisted, Custom = custom, Challenges = challenges };
        }

        // ---- achievements ----

        [Test]
        public void Unlock_PutsTheAchievementIn_AndDoesNotUnlockItTwice()
        {
            ProfileModel model = Model();
            Assert.False(model.View.IsUnlocked("first-cpu"));

            Assert.True(model.Unlock("first-cpu", Entry()));
            Assert.True(model.View.IsUnlocked("first-cpu"));
            Assert.Equal(1, model.View.UnlockedCount);
            int revision = model.Revision;

            Assert.False(model.Unlock("first-cpu", Entry("run-2", true, "Other Co.")), "the second time changes nothing");
            Assert.Equal(revision, model.Revision, "a refused unlock does not move the revision");
            AchievementEntry kept = model.View.EntryOf("first-cpu");
            Assert.Equal("run-1", kept.RunId, "the first earning stays");
            Assert.False(kept.Assisted);
            Assert.Equal("Garage CPU Inc.", kept.Company);
        }

        [Test]
        public void TheEntry_IsStoredAsInTheDesign_AndReadBack()
        {
            var section = new JsonObject();
            ProfileModel model = Model(section);
            model.Unlock("first-cpu", Entry());

            JsonObject stored = section.GetObject("achievements").GetObject("first-cpu");
            Assert.Equal("2026-10-10T18:22:05Z", stored.GetString("unlockedAt"));
            Assert.Equal("1975-08-14", stored.GetString("gameDate"));
            Assert.Equal("Garage CPU Inc.", stored.GetString("company"));
            Assert.Equal("run-1", stored.GetString("runId"));
            Assert.False(stored.GetBool("assisted", true));

            AchievementEntry read = model.View.EntryOf("first-cpu");
            Assert.Equal(new DateTime(2026, 10, 10, 18, 22, 5), read.UnlockedAt);
            Assert.Equal(D(1975, 8, 14), read.GameDate);
            Assert.Null(model.View.EntryOf("nope"));
            Assert.Null(model.View.EntryOf(null));
        }

        [Test]
        public void AnAssistedAchievement_KeepsItsMark()
        {
            ProfileModel model = Model();
            model.Unlock("first-cpu", Entry(assisted: true));
            Assert.True(model.View.EntryOf("first-cpu").Assisted);
        }

        [Test]
        public void Unlock_RefusesAnEmptyOrTooLongId_AndAMissingEntry()
        {
            ProfileModel model = Model();
            Assert.False(model.Unlock(null, Entry()));
            Assert.False(model.Unlock("", Entry()));
            Assert.False(model.Unlock(new string('x', ProfileModel.MaxIdLength + 1), Entry()));
            Assert.False(model.Unlock("first-cpu", null));
            Assert.Equal(0, model.Revision);
            Assert.Equal(0, model.View.UnlockedCount);
        }

        [Test]
        public void TheCompanyName_IsCutToAHundredCharacters()
        {
            ProfileModel model = Model();
            model.Unlock("first-cpu", Entry(company: new string('c', 250)));
            Assert.Equal(ProfileModel.MaxCompanyLength, model.View.EntryOf("first-cpu").Company.Length);

            model.Unlock("first-sale", Entry(company: null));
            Assert.Equal(string.Empty, model.View.EntryOf("first-sale").Company);
        }

        [Test]
        public void UnlockedIds_ListTheEarnedOnesInTheOrderTheyCame()
        {
            ProfileModel model = Model();
            model.Unlock("b", Entry());
            model.Unlock("a", Entry());
            Assert.Equal(new[] { "b", "a" }, new List<string>(model.View.UnlockedIds).ToArray());
        }

        // ---- the records ----

        [Test]
        public void ACleanWin_BecomesTheBestResult_AndOnlyABetterOneReplacesIt()
        {
            ProfileModel model = Model();
            Assert.True(model.RecordResult(Won(400, "bronze"), Info(runId: "r1")), "the first win is a record");
            Assert.Equal(400, model.View.BestScoreOf("garage-1975"));
            Assert.Equal("bronze", model.View.BestMedalOf("garage-1975"));

            Assert.False(model.RecordResult(Won(300, "bronze"), Info(runId: "r2")), "a worse win is no record");
            Assert.False(model.RecordResult(Won(400, "bronze"), Info(runId: "r3")), "a tie keeps the older one");
            Assert.Equal("r1", model.View.ScenarioRecordOf("garage-1975").Best.RunId);

            Assert.True(model.RecordResult(Won(600, "silver"), Info(runId: "r4", challenges: new[] { "no-debt", "no-oem" })));
            ProfileScenarioRecord record = model.View.ScenarioRecordOf("garage-1975");
            Assert.Equal(600, record.Best.Score);
            Assert.Equal("silver", record.Best.Medal);
            Assert.Equal("r4", record.Best.RunId);
            Assert.Equal(D(1977, 1, 1), record.Best.FinishedOn);
            Assert.Equal(new[] { "no-debt", "no-oem" }, record.Best.Challenges);
            Assert.Equal(4, record.Attempts);
            Assert.Equal(4, record.Wins);
        }

        [Test]
        public void AnAssistedWin_CountsAsAnAttempt_ButNeverUpdatesTheBest()
        {
            ProfileModel model = Model();
            model.RecordResult(Won(300, "bronze"), Info(runId: "clean"));

            Assert.False(model.RecordResult(Won(900, "gold"), Info(assisted: true, runId: "helped")), "an assisted run makes no record");
            ProfileScenarioRecord record = model.View.ScenarioRecordOf("garage-1975");
            Assert.Equal(300, record.Best.Score);
            Assert.Equal("clean", record.Best.RunId);
            Assert.Equal(2, record.Attempts);
            Assert.Equal(2, record.Wins);

            ProfileModel fresh = Model();
            Assert.False(fresh.RecordResult(Won(900, "gold"), Info(assisted: true)));
            Assert.Null(fresh.View.ScenarioRecordOf("garage-1975").Best, "no best at all when every win had help");
            Assert.Null(fresh.View.BestMedalOf("garage-1975"));
            Assert.Equal(0, fresh.View.BestScoreOf("garage-1975"));
        }

        [Test]
        public void ACustomRun_LeavesTheRecordsAlone_ButIsInTheHistoryAndTheCounters()
        {
            ProfileModel model = Model();
            Assert.False(model.RecordResult(Won(900, "gold"), Info(custom: true)));

            Assert.Null(model.View.ScenarioRecordOf("garage-1975"), "no record for a run of changed content");
            Assert.Equal(1, model.View.History.Count);
            Assert.True(model.View.History[0].Custom);
            Assert.Equal(1, model.View.Stats.ScenariosWon);
        }

        [Test]
        public void ALostRun_CountsAnAttempt_NotAWin_AndNotABest()
        {
            ProfileModel model = Model();
            model.RecordResult(Won(500, "bronze"), Info(runId: "w"));
            Assert.False(model.RecordResult(Lost(), Info(runId: "l")));

            ProfileScenarioRecord record = model.View.ScenarioRecordOf("garage-1975");
            Assert.Equal(2, record.Attempts);
            Assert.Equal(1, record.Wins);
            Assert.Equal(500, record.Best.Score);
            Assert.Equal(1, model.View.Stats.ScenariosWon);
            Assert.Equal(1, model.View.Stats.ScenariosLost);
        }

        [Test]
        public void TheHistory_KeepsTheLastFifty()
        {
            ProfileModel model = Model();
            for (int i = 1; i <= 60; i++) model.RecordResult(Won(i, "completed"), Info(runId: "r" + i));

            IList<ProfileHistoryEntry> history = model.View.History;
            Assert.Equal(ProfileModel.MaxHistory, history.Count);
            Assert.Equal(11, history[0].Score, "the oldest ten are gone");
            Assert.Equal(60, history[49].Score);
            Assert.Equal("garage-1975", history[0].ScenarioId);
            Assert.Equal("Won", history[0].Outcome);
            Assert.Equal(Clock, history[0].RealDate);
            Assert.Equal(60, model.View.ScenarioRecordOf("garage-1975").Attempts, "the counters are not cut with the history");
        }

        [Test]
        public void TheHistoryLine_CarriesTheFlagsAndTheMedal()
        {
            ProfileModel model = Model();
            model.RecordResult(Won(450, "bronze"), Info(assisted: true, custom: false));
            ProfileHistoryEntry line = model.View.History[0];
            Assert.Equal(450, line.Score);
            Assert.Equal("bronze", line.Medal);
            Assert.True(line.Assisted);
            Assert.False(line.Custom);
        }

        [Test]
        public void ANonEndingResult_OrARunWithoutAScenario_IsIgnored()
        {
            ProfileModel model = Model();
            var running = new ScenarioResult { Outcome = ScenarioStatus.Active };
            Assert.False(model.RecordResult(running, Info()));
            Assert.False(model.RecordResult(Won(100), Info(scenarioId: null)));
            Assert.Equal(0, model.Revision, "nothing was recorded");
            Assert.Equal(0, model.View.History.Count);

            Assert.Throws<ArgumentNullException>(() => model.RecordResult(null, Info()));
            Assert.Throws<ArgumentNullException>(() => model.RecordResult(Won(1), null));
        }

        [Test]
        public void HasMedalInAll_NeedsTheMedalOrBetterInEveryScenario()
        {
            ProfileModel model = Model();
            string[] ids = ScenarioTestData.ScenarioIds;
            foreach (string id in ids) model.RecordResult(Won(900, "gold"), Info(scenarioId: id));
            Assert.True(model.View.HasMedalInAll(ids, "gold"));
            Assert.True(model.View.HasMedalInAll(ids, "bronze"));

            ProfileModel mixed = Model();
            for (int i = 0; i < ids.Length; i++) mixed.RecordResult(Won(700, i == 3 ? "silver" : "gold"), Info(scenarioId: ids[i]));
            Assert.False(mixed.View.HasMedalInAll(ids, "gold"), "one silver among the golds");
            Assert.True(mixed.View.HasMedalInAll(ids, "silver"));

            Assert.False(Model().View.HasMedalInAll(ids, "completed"), "nothing played");
            Assert.False(model.View.HasMedalInAll(new string[0], "gold"), "a rule over no scenarios is never satisfied");
            Assert.False(model.View.HasMedalInAll(ids, "platinum"), "an unknown medal");
            Assert.False(model.View.HasMedalInAll(null, "gold"));
        }

        [Test]
        public void TheMedalsAreOrdered_GoldOverSilverOverBronzeOverCompleted()
        {
            Assert.True(ProfileView.MedalRank("gold") > ProfileView.MedalRank("silver"));
            Assert.True(ProfileView.MedalRank("silver") > ProfileView.MedalRank("bronze"));
            Assert.True(ProfileView.MedalRank("bronze") > ProfileView.MedalRank("completed"));
            Assert.Equal(-1, ProfileView.MedalRank("none"));
            Assert.Equal(-1, ProfileView.MedalRank(null));
        }

        // ---- challenges and counters ----

        [Test]
        public void ARecordedChallenge_CountsThePassesAndKeepsTheLongestHold()
        {
            ProfileModel model = Model();
            Assert.False(model.View.IsChallengePassed("no-debt"));

            model.RecordChallenge("no-debt", 10);
            model.RecordChallenge("no-debt", 5);
            ProfileChallengeRecord record = model.View.ChallengeRecordOf("no-debt");
            Assert.Equal(2, record.Passed);
            Assert.Equal(10, record.BestYears);
            Assert.True(model.View.IsChallengePassed("no-debt"));

            model.RecordChallenge("no-debt", 15);
            Assert.Equal(15, model.View.ChallengeRecordOf("no-debt").BestYears);
            Assert.Null(model.View.ChallengeRecordOf("no-oem"));

            int revision = model.Revision;
            model.RecordChallenge("", 3);
            Assert.Equal(revision, model.Revision, "an empty id is ignored");
        }

        [Test]
        public void RunsStarted_AreCounted()
        {
            ProfileModel model = Model();
            model.RecordRunStarted();
            model.RecordRunStarted();
            Assert.Equal(2, model.View.Stats.RunsStarted);
            Assert.Equal(2, model.Revision);
        }

        // ---- reset and revision ----

        [Test]
        public void Clear_EmptiesTheSection_AndAnEmptyOneIsLeftAlone()
        {
            var section = new JsonObject();
            ProfileModel model = Model(section);
            model.Unlock("first-cpu", Entry());
            model.RecordResult(Won(500, "bronze"), Info());
            model.RecordChallenge("no-debt", 10);
            int revision = model.Revision;

            model.Clear();
            Assert.Equal(0, section.Count, "the section is empty");
            Assert.Equal(0, model.View.UnlockedCount);
            Assert.Null(model.View.ScenarioRecordOf("garage-1975"));
            Assert.Equal(0, model.View.History.Count);
            Assert.Equal(revision + 1, model.Revision);

            model.Clear();
            Assert.Equal(revision + 1, model.Revision, "clearing an empty profile is no change");
        }

        [Test]
        public void TheRevision_GrowsOnlyWhenTheSectionChanges()
        {
            ProfileModel model = Model();
            Assert.Equal(0, model.Revision);

            int unlocked = model.View.UnlockedCount;
            IList<ProfileHistoryEntry> history = model.View.History;
            Assert.Equal(0, unlocked);
            Assert.Equal(0, history.Count);
            Assert.Equal(0, model.Revision, "reading changes nothing");

            model.Unlock("first-cpu", Entry());
            Assert.Equal(1, model.Revision);
            model.Unlock("first-cpu", Entry());
            Assert.Equal(1, model.Revision);
            model.RecordResult(Won(100), Info());
            Assert.Equal(2, model.Revision);
            model.RecordChallenge("no-debt", 10);
            Assert.Equal(3, model.Revision);
        }

        [Test]
        public void TheSection_NeverCarriesAVersionField_TheCoreOwnsIt()
        {
            var section = new JsonObject();
            ProfileModel model = Model(section);
            model.Unlock("first-cpu", Entry());
            model.RecordResult(Won(100), Info());
            model.RecordChallenge("no-debt", 10);
            model.RecordRunStarted();

            Assert.False(section.ContainsKey("_schema"));
            Assert.False(section.ContainsKey("schema"));
            Assert.Equal(1, ProfileModel.SchemaVersion, "the section asked of ModProfile is version 1");
            foreach (string key in new[] { "achievements", "scenarioRecords", "challengeRecords", "history", "stats" })
            {
                Assert.True(section.ContainsKey(key), "design.md, 'Профиль': " + key);
            }
        }

        // ---- persistence and damage ----

        [Test]
        public void TheSection_SurvivesAWriteAndAReadOfItsText()
        {
            var section = new JsonObject();
            ProfileModel model = Model(section);
            model.Unlock("first-cpu", Entry());
            model.RecordResult(Won(600, "silver"), Info(runId: "r9", challenges: new[] { "no-debt" }));
            model.RecordChallenge("no-debt", 10);

            JsonNode parsed;
            JsonError error;
            Assert.True(ModJson.TryParse(ModJson.Write(section, true), out parsed, out error), "the section is valid JSON");

            ProfileView again = new ProfileModel((JsonObject)parsed).View;
            Assert.True(again.IsUnlocked("first-cpu"));
            Assert.Equal(600, again.BestScoreOf("garage-1975"));
            Assert.Equal("r9", again.ScenarioRecordOf("garage-1975").Best.RunId);
            Assert.Equal(new[] { "no-debt" }, again.ScenarioRecordOf("garage-1975").Best.Challenges);
            Assert.Equal(10, again.ChallengeRecordOf("no-debt").BestYears);
            Assert.Equal(1, again.History.Count);
        }

        [Test]
        public void ADamagedSection_ReadsAsEmptyWhereItIsWrong_AndIsRepairedByTheNextWrite()
        {
            JsonNode parsed;
            JsonError error;
            const string text = "{ \"achievements\": 7, \"scenarioRecords\": { \"garage-1975\": { \"attempts\": -4, \"wins\": \"x\", \"best\": 5 }, \"x\": 3 },"
                                + " \"history\": [ 1, { \"scenarioId\": \"a\", \"score\": -9, \"realDate\": \"yesterday\" }, null ], \"stats\": [] }";
            Assert.True(ModJson.TryParse(text, out parsed, out error));
            var section = (JsonObject)parsed;
            ProfileModel model = Model(section);

            Assert.Equal(0, model.View.UnlockedCount);
            Assert.False(model.View.IsUnlocked("first-cpu"));
            ProfileScenarioRecord record = model.View.ScenarioRecordOf("garage-1975");
            Assert.Equal(0, record.Attempts, "a negative count reads as 0");
            Assert.Equal(0, record.Wins, "text reads as 0");
            Assert.Null(record.Best, "a best of the wrong shape is no best");
            Assert.Null(model.View.ScenarioRecordOf("x"));

            IList<ProfileHistoryEntry> history = model.View.History;
            Assert.Equal(1, history.Count, "only the line that is an object");
            Assert.Equal(0, history[0].Score);
            Assert.Equal(default(DateTime), history[0].RealDate);
            Assert.Equal(0, model.View.Stats.RunsStarted);

            Assert.True(model.Unlock("first-cpu", Entry()), "a damaged part is replaced when something is written into it");
            Assert.True(model.View.IsUnlocked("first-cpu"));
            Assert.True(model.RecordResult(Won(100), Info()));
            Assert.Equal(1, model.View.ScenarioRecordOf("garage-1975").Attempts);
        }

        [Test]
        public void ANullSection_IsRefused()
        {
            Assert.Throws<ArgumentNullException>(() => new ProfileModel(null));
        }

        // ---- the game-side gate, as text (it needs Unity and the core's ModProfile) ----

        private static string Source(params string[] path)
        {
            string file = Path.Combine(new[] { SourceScan.SrcDirOrFail(), "Features", "ScenariosAchievements" }.Concat(path).ToArray());
            Assert.True(File.Exists(file), "no source file: " + file);
            return SourceScan.Strip(SourceScan.ReadText(file), false);
        }

        [Test]
        public void TheGate_WorksOnTheSectionOfTheCore_AndTellsTheCoreOfEveryChange_NFR4()
        {
            string code = Source("Integration", "ProfileGate.cs");

            // the section, version 1, the same live object
            Assert.True(Regex.IsMatch(code, @"ModProfile\.Section\(\s*ScenariosFeature\.FeatureId\s*,\s*ProfileModel\.SchemaVersion\s*\)"), "ModProfile.Section(\"scenarios-achievements\", 1)");
            Assert.True(code.Contains("new ProfileModel(section)"), "the logic of the model is the tested one");

            // MarkDirty only when the revision moved
            Assert.True(Regex.IsMatch(code, @"if\s*\(\s*model\.Revision\s*==\s*_reportedRevision\s*\)\s*return\s*;"), "no change, no MarkDirty");
            Assert.True(Regex.IsMatch(code, @"ModProfile\.MarkDirty\(\s*ScenariosFeature\.FeatureId\s*\)"), "MarkDirty after a change");

            // Save when the results are shown and when the profile is reset
            string save = Between(code, "internal static void Save()", "internal static void Reset()");
            Assert.True(save.Contains("ModProfile.Save()"), "Save() writes at once");
            string reset = Between(code, "internal static void Reset()", "private static ProfileModel Model()");
            Assert.True(reset.Contains("model.Clear()") && reset.Contains("ModProfile.Save()"), "the reset empties the section and saves at once");

            // the status of the profile for the page of the hub
            Assert.True(Regex.IsMatch(code, @"ProfileStatus\s+Status\s*\{\s*get\s*\{\s*return\s+ModProfile\.Status\s*;"), "Status comes from ModProfile");

            // every call is guarded; no file, no backup, no throttling of its own
            Assert.True(code.Contains("ModHooks.Guard("), "a failure is counted against the feature");
            Assert.False(Regex.IsMatch(code, @"\bFile\b|\bDirectory\b|\bStreamWriter\b|\.bak|Stopwatch|Time\.(unscaled)?[tT]ime|DateTime\.UtcNow"), "the files, the backup and the throttling are the core's");

            string page = Source("ScenariosHubPage.cs");
            Assert.True(page.Contains("scn.hub.profile") && page.Contains("ProfileGate.StatusText()"), "FR-13 item 3: the hub page shows the status of the profile");
        }

        [Test]
        public void TheModel_IsPureLogic_WithNoFileOfItsOwn()
        {
            string code = SourceScan.Strip(SourceScan.ReadText(Path.Combine(SourceScan.SrcDirOrFail(), "Features", "ScenariosAchievements", "Logic", "Profile", "ProfileModel.cs")), true);
            Assert.False(Regex.IsMatch(code, @"\bSystem\.IO\b|\bFile\.|\bStream|ModProfile|MarkDirty"), "the model touches no file and does not talk to the core");
            Assert.False(code.Contains("_schema"), "the version of the section is the core's");
        }

        private static string Between(string text, string from, string to)
        {
            int start = text.IndexOf(from, StringComparison.Ordinal);
            Assert.True(start >= 0, "no '" + from + "' in the source");
            int end = text.IndexOf(to, start + from.Length, StringComparison.Ordinal);
            return end < 0 ? text.Substring(start) : text.Substring(start, end - start);
        }
    }
}
