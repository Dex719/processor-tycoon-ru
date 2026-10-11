using System;
using System.Collections.Generic;
using System.Globalization;
using PTMod.Core.Logic.Json;

namespace PTMod.Features.ScenariosAchievements.Logic
{
    /// <summary>
    /// One achievement in the profile (design.md, "Профиль"): when it was earned (real UTC time and game date), by which company, in which run,
    /// and whether the run was assisted ("with help"). The company name is what the player typed.
    /// </summary>
    public sealed class AchievementEntry
    {
        /// <summary>The real moment, UTC (the profile stores it as <c>yyyy-MM-ddTHH:mm:ssZ</c>, whole seconds).</summary>
        public DateTime UnlockedAt;

        /// <summary>The game day of the run.</summary>
        public DateTime GameDate;

        /// <summary>The name of the player's company; at most <see cref="ProfileModel.MaxCompanyLength"/> characters are kept.</summary>
        public string Company;

        public string RunId;

        public bool Assisted;
    }

    /// <summary>The best result of a scenario in the profile: only a won run that was neither assisted nor custom can be one.</summary>
    public sealed class ProfileBest
    {
        public int Score;

        /// <summary><c>gold</c>, <c>silver</c>, <c>bronze</c> or <c>completed</c>.</summary>
        public string Medal;

        public string RunId;

        /// <summary>The game day the scenario ended; <c>default</c> when the profile has none.</summary>
        public DateTime FinishedOn;

        /// <summary>The challenges the best run was played with.</summary>
        public string[] Challenges = new string[0];
    }

    /// <summary>What the profile knows about a standard scenario: how often it was played and won, and the best result.</summary>
    public sealed class ProfileScenarioRecord
    {
        public string ScenarioId;

        public int Attempts;

        public int Wins;

        /// <summary>The best result, or null when there is none yet (nothing won clean).</summary>
        public ProfileBest Best;
    }

    /// <summary>How often a challenge was passed and the longest it was held (whole years).</summary>
    public sealed class ProfileChallengeRecord
    {
        public string ChallengeId;

        public int Passed;

        public int BestYears;
    }

    /// <summary>One line of the history of finished scenario runs.</summary>
    public sealed class ProfileHistoryEntry
    {
        public string ScenarioId;

        /// <summary><c>Won</c> or <c>Lost</c>.</summary>
        public string Outcome;

        public int Score;

        public string Medal;

        public bool Assisted;

        public bool Custom;

        /// <summary>The real moment, UTC; <c>default</c> when the profile has none.</summary>
        public DateTime RealDate;
    }

    /// <summary>The counters of the profile.</summary>
    public sealed class ProfileStats
    {
        public int RunsStarted;

        public int ScenariosWon;

        public int ScenariosLost;
    }

    /// <summary>
    /// A read-only view of the section of the profile (<c>ModProfile.Section("scenarios-achievements", 1)</c>): what the achievements window,
    /// the records tab and <see cref="AchievementEvaluator"/> look at. It reads the live JSON object on every call, so it always shows the
    /// current state, and it never throws at what a damaged or hand-edited file may hold: an entry of the wrong shape is skipped, a wrong
    /// number reads as 0, a wrong date as <c>default</c>.
    /// </summary>
    public sealed class ProfileView
    {
        private readonly JsonObject _section;

        internal ProfileView(JsonObject section)
        {
            _section = section;
        }

        // ---- achievements ----

        /// <summary>The achievement is in the profile.</summary>
        public bool IsUnlocked(string achievementId)
        {
            return achievementId != null && ProfileJson.ObjectAt(ProfileJson.Child(_section, ProfileModel.AchievementsKey), achievementId) != null;
        }

        /// <summary>How many achievements the profile holds.</summary>
        public int UnlockedCount
        {
            get
            {
                JsonObject all = ProfileJson.Child(_section, ProfileModel.AchievementsKey);
                if (all == null) return 0;
                int count = 0;
                foreach (KeyValuePair<string, JsonNode> pair in all)
                {
                    if (pair.Value is JsonObject) count++;
                }
                return count;
            }
        }

        /// <summary>The ids of the achievements in the profile, in the order they were stored.</summary>
        public IList<string> UnlockedIds
        {
            get
            {
                var ids = new List<string>();
                JsonObject all = ProfileJson.Child(_section, ProfileModel.AchievementsKey);
                if (all == null) return ids;
                foreach (KeyValuePair<string, JsonNode> pair in all)
                {
                    if (pair.Value is JsonObject) ids.Add(pair.Key);
                }
                return ids;
            }
        }

        /// <summary>The entry of an earned achievement, or null.</summary>
        public AchievementEntry EntryOf(string achievementId)
        {
            JsonObject entry = achievementId == null
                ? null
                : ProfileJson.ObjectAt(ProfileJson.Child(_section, ProfileModel.AchievementsKey), achievementId);
            if (entry == null) return null;

            return new AchievementEntry
            {
                UnlockedAt = ProfileJson.ReadUtc(entry, "unlockedAt"),
                GameDate = ProfileJson.ReadDate(entry, "gameDate"),
                Company = entry.GetString("company", string.Empty) ?? string.Empty,
                RunId = entry.GetString("runId", null),
                Assisted = entry.GetBool("assisted", false),
            };
        }

        // ---- scenario records ----

        /// <summary>The record of a scenario, or null when the profile has none (it was never played clean, or never at all).</summary>
        public ProfileScenarioRecord ScenarioRecordOf(string scenarioId)
        {
            JsonObject record = scenarioId == null
                ? null
                : ProfileJson.ObjectAt(ProfileJson.Child(_section, ProfileModel.ScenarioRecordsKey), scenarioId);
            if (record == null) return null;

            var result = new ProfileScenarioRecord
            {
                ScenarioId = scenarioId,
                Attempts = ProfileJson.ReadCount(record, "attempts"),
                Wins = ProfileJson.ReadCount(record, "wins"),
            };

            JsonObject best = record.GetObject("best");
            if (best != null)
            {
                var challenges = new List<string>();
                JsonArray list = best.GetArray("challenges");
                if (list != null)
                {
                    foreach (JsonNode item in list)
                    {
                        var text = item as JsonValue;
                        if (text != null && text.Kind == JsonKind.String && !string.IsNullOrEmpty(text.StringValue)) challenges.Add(text.StringValue);
                    }
                }
                result.Best = new ProfileBest
                {
                    Score = ProfileJson.ReadCount(best, "score"),
                    Medal = best.GetString("medal", null),
                    RunId = best.GetString("runId", null),
                    FinishedOn = ProfileJson.ReadDate(best, "finishedOn"),
                    Challenges = challenges.ToArray(),
                };
            }
            return result;
        }

        /// <summary>The medal of the best result of a scenario; null when there is none.</summary>
        public string BestMedalOf(string scenarioId)
        {
            ProfileScenarioRecord record = ScenarioRecordOf(scenarioId);
            return record == null || record.Best == null ? null : record.Best.Medal;
        }

        /// <summary>The score of the best result of a scenario; 0 when there is none.</summary>
        public int BestScoreOf(string scenarioId)
        {
            ProfileScenarioRecord record = ScenarioRecordOf(scenarioId);
            return record == null || record.Best == null ? 0 : record.Best.Score;
        }

        /// <summary>
        /// True when every scenario of the list has a best result with at least the given medal (<c>gold</c> &gt; <c>silver</c> &gt; <c>bronze</c>
        /// &gt; <c>completed</c>). An empty list is false: a rule over no scenarios is never satisfied.
        /// </summary>
        public bool HasMedalInAll(IEnumerable<string> scenarioIds, string minMedal)
        {
            int required = MedalRank(minMedal);
            if (scenarioIds == null || required < 0) return false;

            bool any = false;
            foreach (string id in scenarioIds)
            {
                any = true;
                if (MedalRank(BestMedalOf(id)) < required) return false;
            }
            return any;
        }

        /// <summary>The order of the medals: <c>gold</c> 3, <c>silver</c> 2, <c>bronze</c> 1, <c>completed</c> 0, anything else -1.</summary>
        public static int MedalRank(string medal)
        {
            switch (medal)
            {
                case "gold": return 3;
                case "silver": return 2;
                case "bronze": return 1;
                case "completed": return 0;
                default: return -1;
            }
        }

        // ---- challenges ----

        /// <summary>The record of a challenge, or null when it was never passed.</summary>
        public ProfileChallengeRecord ChallengeRecordOf(string challengeId)
        {
            JsonObject record = challengeId == null
                ? null
                : ProfileJson.ObjectAt(ProfileJson.Child(_section, ProfileModel.ChallengeRecordsKey), challengeId);
            if (record == null) return null;

            return new ProfileChallengeRecord
            {
                ChallengeId = challengeId,
                Passed = ProfileJson.ReadCount(record, "passed"),
                BestYears = ProfileJson.ReadCount(record, "bestYears"),
            };
        }

        /// <summary>The challenge was passed at least once.</summary>
        public bool IsChallengePassed(string challengeId)
        {
            ProfileChallengeRecord record = ChallengeRecordOf(challengeId);
            return record != null && record.Passed > 0;
        }

        // ---- history and counters ----

        /// <summary>The finished runs, oldest first (at most <see cref="ProfileModel.MaxHistory"/>).</summary>
        public IList<ProfileHistoryEntry> History
        {
            get
            {
                var list = new List<ProfileHistoryEntry>();
                JsonArray history = _section.GetArray(ProfileModel.HistoryKey);
                if (history == null) return list;

                foreach (JsonNode node in history)
                {
                    var item = node as JsonObject;
                    if (item == null) continue;
                    list.Add(new ProfileHistoryEntry
                    {
                        ScenarioId = item.GetString("scenarioId", null),
                        Outcome = item.GetString("outcome", null),
                        Score = ProfileJson.ReadCount(item, "score"),
                        Medal = item.GetString("medal", null),
                        Assisted = item.GetBool("assisted", false),
                        Custom = item.GetBool("custom", false),
                        RealDate = ProfileJson.ReadUtc(item, "realDate"),
                    });
                }
                return list;
            }
        }

        public ProfileStats Stats
        {
            get
            {
                JsonObject stats = ProfileJson.Child(_section, ProfileModel.StatsKey);
                if (stats == null) return new ProfileStats();
                return new ProfileStats
                {
                    RunsStarted = ProfileJson.ReadCount(stats, "runsStarted"),
                    ScenariosWon = ProfileJson.ReadCount(stats, "scenariosWon"),
                    ScenariosLost = ProfileJson.ReadCount(stats, "scenariosLost"),
                };
            }
        }
    }

    /// <summary>
    /// The model of the feature's part of the player's profile (FR-13, NFR-4; design.md, "Профиль"): the achievements earned, the records of the
    /// scenarios, the challenges passed, the history of the finished runs, a few counters. Pure logic over the <see cref="JsonObject"/> of the
    /// section that <c>ModProfile.Section("scenarios-achievements", 1)</c> hands out; the model never opens a file, never writes the version of
    /// the section (<c>_schema</c> is the core's) and never throttles - the Unity-side <c>ProfileGate</c> watches <see cref="Revision"/> and calls
    /// <c>ModProfile.MarkDirty</c> when it moved. Nothing here throws at the content of the section: a part of the wrong shape is replaced when
    /// something is written into it and skipped when it is read.
    /// </summary>
    public sealed class ProfileModel
    {
        /// <summary>The version of the section this build asks <c>ModProfile</c> for.</summary>
        public const int SchemaVersion = 1;

        /// <summary>The history keeps the last this many finished runs.</summary>
        public const int MaxHistory = 50;

        /// <summary>The longest company name stored with an achievement.</summary>
        public const int MaxCompanyLength = 100;

        /// <summary>The longest id the profile accepts for an achievement, a scenario or a challenge.</summary>
        public const int MaxIdLength = 80;

        /// <summary>The most years a challenge record can hold.</summary>
        public const int MaxYears = 1000;

        internal const string AchievementsKey = "achievements";
        internal const string ScenarioRecordsKey = "scenarioRecords";
        internal const string ChallengeRecordsKey = "challengeRecords";
        internal const string HistoryKey = "history";
        internal const string StatsKey = "stats";

        private const string UtcFormat = "yyyy-MM-dd'T'HH:mm:ss'Z'";

        private readonly JsonObject _section;
        private readonly Func<DateTime> _utcNow;
        private int _revision;

        /// <param name="section">The live section of the profile; the model changes it in place.</param>
        /// <param name="utcNow">The clock for the history (real time, UTC); null: the system clock.</param>
        public ProfileModel(JsonObject section, Func<DateTime> utcNow = null)
        {
            if (section == null) throw new ArgumentNullException("section");
            _section = section;
            _utcNow = utcNow ?? (() => DateTime.UtcNow);
            View = new ProfileView(section);
        }

        /// <summary>The read side, for <see cref="AchievementEvaluator"/> and the windows.</summary>
        public ProfileView View { get; private set; }

        /// <summary>Grows with every change of the section and only then (a refused <see cref="Unlock"/> or a <see cref="Clear"/> of an empty section leave it).</summary>
        public int Revision
        {
            get { return _revision; }
        }

        /// <summary>
        /// Puts an achievement into the profile. False, and nothing changes, when it is already there or the id is empty or longer than
        /// <see cref="MaxIdLength"/>; an earned achievement is never overwritten (the first time stays).
        /// </summary>
        public bool Unlock(string achievementId, AchievementEntry entry)
        {
            if (!IsValidId(achievementId) || entry == null) return false;
            if (View.IsUnlocked(achievementId)) return false;

            var record = new JsonObject();
            record.Set("unlockedAt", FormatUtc(entry.UnlockedAt));
            record.Set("gameDate", ContentVocabulary.FormatDate(entry.GameDate));
            record.Set("company", Truncate(entry.Company, MaxCompanyLength));
            record.Set("runId", JsonValue.Of(entry.RunId));
            record.Set("assisted", entry.Assisted);

            ProfileJson.Ensure(_section, AchievementsKey).Set(achievementId, record);
            _revision++;
            return true;
        }

        /// <summary>The player started a run (a counter of the profile).</summary>
        public void RecordRunStarted()
        {
            JsonObject stats = ProfileJson.Ensure(_section, StatsKey);
            stats.Set("runsStarted", ProfileJson.ReadCount(stats, "runsStarted") + 1);
            _revision++;
        }

        /// <summary>
        /// Records how a scenario run ended: a line in the history (the last <see cref="MaxHistory"/> stay), the counters, and - for a run
        /// of a standard scenario - the attempts and wins of its record. The best result is replaced only by a won run that was neither assisted
        /// nor custom and scored more than the best so far (a tie keeps the older one). A custom run (changed or added content) leaves the
        /// records alone. Returns true when the best result changed (a new record). A result that is not an ending, or a run without a scenario,
        /// is ignored.
        /// </summary>
        public bool RecordResult(ScenarioResult result, RunInfo run)
        {
            if (result == null) throw new ArgumentNullException("result");
            if (run == null) throw new ArgumentNullException("run");
            if (!IsValidId(run.ScenarioId)) return false;
            if (result.Outcome != ScenarioStatus.Won && result.Outcome != ScenarioStatus.Lost) return false;

            bool won = result.Outcome == ScenarioStatus.Won;

            JsonObject stats = ProfileJson.Ensure(_section, StatsKey);
            string counter = won ? "scenariosWon" : "scenariosLost";
            stats.Set(counter, ProfileJson.ReadCount(stats, counter) + 1);

            AppendHistory(result, run, won);

            bool newBest = false;
            if (!run.Custom)
            {
                JsonObject record = ProfileJson.EnsureChild(ProfileJson.Ensure(_section, ScenarioRecordsKey), run.ScenarioId);
                record.Set("attempts", ProfileJson.ReadCount(record, "attempts") + 1);
                if (won) record.Set("wins", ProfileJson.ReadCount(record, "wins") + 1);

                if (won && !run.Assisted)
                {
                    JsonObject best = record.GetObject("best");
                    if (best == null || result.Score > ProfileJson.ReadCount(best, "score"))
                    {
                        record.Set("best", BuildBest(result, run));
                        newBest = true;
                    }
                }
            }

            _revision++;
            return newBest;
        }

        /// <summary>A challenge was passed after <paramref name="years"/> whole years: one more pass, and the longest hold if this is longer.</summary>
        public void RecordChallenge(string challengeId, int years)
        {
            if (!IsValidId(challengeId)) return;

            JsonObject record = ProfileJson.EnsureChild(ProfileJson.Ensure(_section, ChallengeRecordsKey), challengeId);
            record.Set("passed", ProfileJson.ReadCount(record, "passed") + 1);
            int held = Math.Max(0, Math.Min(years, MaxYears));
            record.Set("bestYears", Math.Max(ProfileJson.ReadCount(record, "bestYears"), held));
            _revision++;
        }

        /// <summary>"Reset the profile": empties the section. An empty section stays as it is (no change, no new revision).</summary>
        public void Clear()
        {
            if (_section.Count == 0) return;
            _section.Clear();
            _revision++;
        }

        // ---- internals ----

        private void AppendHistory(ScenarioResult result, RunInfo run, bool won)
        {
            JsonArray history = _section.GetArray(HistoryKey);
            if (history == null)
            {
                history = new JsonArray();
                _section.Set(HistoryKey, history);
            }

            var line = new JsonObject();
            line.Set("scenarioId", run.ScenarioId);
            line.Set("outcome", won ? "Won" : "Lost");
            line.Set("score", result.Score);
            line.Set("medal", JsonValue.Of(result.Medal));
            line.Set("assisted", run.Assisted);
            line.Set("custom", run.Custom);
            line.Set("realDate", FormatUtc(_utcNow()));
            history.Add(line);

            while (history.Count > MaxHistory) history.RemoveAt(0);
        }

        private static JsonObject BuildBest(ScenarioResult result, RunInfo run)
        {
            var best = new JsonObject();
            best.Set("score", result.Score);
            best.Set("medal", JsonValue.Of(result.Medal));
            best.Set("runId", JsonValue.Of(run.RunId));
            best.Set("finishedOn", ContentVocabulary.FormatDate(result.FinishedOn));

            var challenges = new JsonArray();
            if (run.Challenges != null)
            {
                foreach (string id in run.Challenges)
                {
                    if (IsValidId(id)) challenges.Add(id);
                }
            }
            best.Set("challenges", challenges);
            return best;
        }

        private static bool IsValidId(string id)
        {
            return !string.IsNullOrEmpty(id) && id.Length <= MaxIdLength;
        }

        private static string Truncate(string text, int max)
        {
            if (text == null) return string.Empty;
            return text.Length <= max ? text : text.Substring(0, max);
        }

        internal static string FormatUtc(DateTime utc)
        {
            DateTime value = utc.Kind == DateTimeKind.Local ? utc.ToUniversalTime() : utc;
            return value.ToString(UtcFormat, CultureInfo.InvariantCulture);
        }

        internal static bool TryParseUtc(string text, out DateTime utc)
        {
            return DateTime.TryParseExact(text, UtcFormat, CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out utc);
        }
    }

    /// <summary>The lenient readers and the careful writers of the profile section (internal).</summary>
    internal static class ProfileJson
    {
        /// <summary>The member as an object, or null.</summary>
        public static JsonObject Child(JsonObject parent, string key)
        {
            return parent == null ? null : parent.GetObject(key);
        }

        /// <summary>The object stored under a key of an object (null for a missing parent, key or a value of another kind).</summary>
        public static JsonObject ObjectAt(JsonObject parent, string key)
        {
            return parent == null || key == null ? null : parent.GetObject(key);
        }

        /// <summary>The object under the key, created (replacing a value of another kind) when it is not there.</summary>
        public static JsonObject Ensure(JsonObject parent, string key)
        {
            JsonObject child = parent.GetObject(key);
            if (child == null)
            {
                child = new JsonObject();
                parent.Set(key, child);
            }
            return child;
        }

        public static JsonObject EnsureChild(JsonObject parent, string key)
        {
            return Ensure(parent, key);
        }

        /// <summary>A whole number from 0 up; anything else (missing, text, fraction, negative) reads as 0.</summary>
        public static int ReadCount(JsonObject obj, string key)
        {
            int value;
            return obj != null && obj.TryGetInt32(key, out value) && value > 0 ? value : 0;
        }

        /// <summary>A game date <c>yyyy-MM-dd</c>; <c>default</c> when missing or malformed.</summary>
        public static DateTime ReadDate(JsonObject obj, string key)
        {
            string text;
            DateTime date;
            return obj != null && obj.TryGetString(key, out text) && ContentVocabulary.TryParseDate(text, out date) ? date : default(DateTime);
        }

        /// <summary>A real UTC time <c>yyyy-MM-ddTHH:mm:ssZ</c>; <c>default</c> when missing or malformed.</summary>
        public static DateTime ReadUtc(JsonObject obj, string key)
        {
            string text;
            DateTime utc;
            return obj != null && obj.TryGetString(key, out text) && ProfileModel.TryParseUtc(text, out utc) ? utc : default(DateTime);
        }
    }
}
