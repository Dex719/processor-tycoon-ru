using System;
using System.Collections.Generic;
using System.Globalization;
using PTMod.Core.Logic.Json;

namespace PTMod.Features.ScenariosAchievements.Logic
{
    /// <summary>
    /// Reads and writes the section <c>scenarios-achievements</c> of the side save file (FR-16, NFR-3; design.md, "Секция ModSave"): the
    /// <see cref="RunState"/> to and from the JSON object the core hands over. The version of the section (<c>_schema</c>) is the core's
    /// business: <see cref="SchemaVersion"/> is what the participant reports, and <see cref="Write"/> never returns a <c>_schema</c> member.
    /// Never throws.
    /// <para>
    /// <see cref="Read"/> takes what a damaged or hand-edited file can hold and makes it usable (Security, "the section of the save is checked"):
    /// a status the code does not know is the default of its kind (a goal is open, a challenge is going, a race is undecided), a date that is
    /// not <c>yyyy-MM-dd</c> is missing, a number out of range is brought into range, a list is cut at its limit, an entry without an id is
    /// dropped. Every change is one line in the problems. A section without a readable run (no <c>run</c> block or no start date) gives no run:
    /// the director then makes a <c>legacy</c> one. The snapshot of the scenario is mapped again by <see cref="ContentMapper"/>; a snapshot
    /// that cannot be mapped leaves the run without a scenario.
    /// </para>
    /// <para>
    /// What the section holds beyond the example of the design: <c>run.holdDays</c> of the challenge states, <c>scenario.races</c> entries with
    /// the winner, <c>scenario.fail</c> with the hold counters, <c>scenario.result</c> with the breakdown, <c>metrics.runDays</c> and
    /// <c>metrics.lastMonthKey</c>. They read as defaults when missing, so a section written by an older build still loads.
    /// </para>
    /// </summary>
    public static class RunSaveCodec
    {
        /// <summary>The id of the section: the id of the feature.</summary>
        public const string SectionId = "scenarios-achievements";

        /// <summary>The version of the section this build writes (<c>IModSaveParticipant.SchemaVersion</c>).</summary>
        public const int SchemaVersion = 1;

        /// <summary>The format of the dates of the section.</summary>
        public const string DateFormat = "yyyy-MM-dd";

        /// <summary>The longest text read from the section (ids, names, keys).</summary>
        public const int MaxTextLength = 200;

        /// <summary>The most items read from a list of ids.</summary>
        public const int MaxListItems = 500;

        /// <summary>The most kinds of events the counters keep.</summary>
        public const int MaxEventKinds = 200;

        /// <summary>The most CPUs received by a transfer that the section keeps.</summary>
        public const int MaxTransferred = 100000;

        /// <summary>The longest ring of the share window that is accepted.</summary>
        public const int MaxWindowDays = 366;

        private const string SchemaMember = "_schema";
        private const int MaxCounter = 1000000000;
        private const long MaxLongCounter = 1000000000000000L;

        // ---- read ----

        /// <summary>
        /// The run from the section, or null when the section has no usable run (null section, no <c>run</c> block, no start date). The object
        /// is not changed. Lines about what was repaired or dropped go to <paramref name="problems"/> (may be null).
        /// </summary>
        public static RunState Read(JsonObject section, List<string> problems)
        {
            if (section == null) return null;

            try
            {
                JsonObject runBlock = section.GetObject("run");
                if (runBlock == null)
                {
                    Add(problems, "run: missing, the save has no run");
                    return null;
                }

                DateTime start;
                if (!TryReadDate(runBlock, "startDate", "run.startDate", problems, out start))
                {
                    Add(problems, "run.startDate: missing or not a date, the save has no run");
                    return null;
                }

                var run = new RunState { StartDate = start, ChallengeStates = new ChallengeStates(start) };
                ReadRun(runBlock, run, problems);
                ReadScenario(section, run, problems);
                ReadMetrics(section.GetObject("metrics"), run.Metrics, problems);
                run.AchievementsThisRun = ReadTextList(section, "achievementsThisRun", "achievementsThisRun", problems);
                return run;
            }
            catch (Exception e)
            {
                // Not expected (every read below is guarded); a surprise must not take the load down.
                Add(problems, "section: unreadable (" + e.GetType().Name + ": " + e.Message + "), the save has no run");
                return null;
            }
        }

        private static void ReadRun(JsonObject block, RunState run, List<string> problems)
        {
            run.RunId = ReadText(block, "runId", "run.runId", problems) ?? Guid.NewGuid().ToString();

            RunOrigin origin;
            run.Origin = TryReadEnum(block, "origin", out origin) ? origin : RunOrigin.NewGame;

            run.DifficultyAtStart = ReadCounter(block, "difficultyAtStart", "run.difficultyAtStart", problems, 5);
            run.Custom = block.GetBool("custom", false);
            run.RestoreAssisted(block.GetBool("assisted", false), ReadTextList(block, "assistedReasons", "run.assistedReasons", problems));
            run.FeaturesAtStart = ReadTextList(block, "featuresAtStart", "run.featuresAtStart", problems);
            run.Challenges = ReadTextList(block, "challenges", "run.challenges", problems);
            run.ScenarioEntryIds = ReadTextList(block, "scenarioEntryIds", "run.scenarioEntryIds", problems);
            run.BriefingMailId = ReadCounter(block, "briefingMailId", "run.briefingMailId", problems, MaxCounter);
            run.ResultMailId = ReadCounter(block, "resultMailId", "run.resultMailId", problems, MaxCounter);

            ReadChallengeStates(block.GetObject("challengeStates"), run, problems);
            ReadTransferred(block.GetArray("transferredCpuIds"), run.Metrics.TransferredCpuIds, problems);
        }

        private static void ReadChallengeStates(JsonObject states, RunState run, List<string> problems)
        {
            if (states == null) return;

            foreach (KeyValuePair<string, JsonNode> pair in states)
            {
                string path = "run.challengeStates." + pair.Key;
                var item = pair.Value as JsonObject;
                if (item == null || !IsUsableId(pair.Key))
                {
                    Add(problems, path + ": not an object with a usable id, dropped");
                    continue;
                }
                if (run.ChallengeStates.Items.Count >= 100)
                {
                    Add(problems, "run.challengeStates: more than 100 challenges, the rest is ignored");
                    break;
                }

                var state = new ChallengeState { Id = pair.Key };
                ChallengeStatus status;
                state.Status = TryReadEnum(item, "status", out status) ? status : ChallengeStatus.Active;
                state.FailedOn = ReadDate(item, "failedOn", path + ".failedOn", problems);
                state.PassedOn = ReadDate(item, "passedOn", path + ".passedOn", problems);
                state.EntryIds = ReadTextList(item, "entryIds", path + ".entryIds", problems);
                ReadHoldDays(item.GetObject("holdDays"), state.Fail, path + ".holdDays", problems);
                run.ChallengeStates.Items[pair.Key] = state;
            }
        }

        private static void ReadTransferred(JsonArray ids, HashSet<int> target, List<string> problems)
        {
            if (ids == null) return;
            foreach (JsonNode node in ids)
            {
                if (target.Count >= MaxTransferred)
                {
                    Add(problems, "run.transferredCpuIds: more than " + MaxTransferred + " ids, the rest is ignored");
                    break;
                }
                var value = node as JsonValue;
                int id;
                if (value != null && value.Kind == JsonKind.Number && value.TryGetInt32(out id)) target.Add(id);
                else Add(problems, "run.transferredCpuIds: " + Describe(node) + " is not a whole number, dropped");
            }
        }

        // ---- scenario ----

        private static void ReadScenario(JsonObject section, RunState run, List<string> problems)
        {
            JsonNode node;
            if (!section.TryGetValue("scenario", out node) || node == null || node.IsNull) return;

            var block = node as JsonObject;
            if (block == null)
            {
                Add(problems, "scenario: " + Describe(node) + " is not an object, the run has no scenario");
                return;
            }

            JsonObject definition = block.GetObject("definition");
            if (definition == null)
            {
                Add(problems, "scenario.definition: missing, the run has no scenario");
                return;
            }

            var errors = new List<string>();
            ScenarioDefinition snapshot = ContentMapper.MapScenario(definition, errors);
            if (snapshot == null)
            {
                Add(problems, "scenario.definition: cannot be read (" + string.Join("; ", errors.ToArray()) + "), the run has no scenario");
                return;
            }
            snapshot.IsCustom = run.Custom;

            var state = new ScenarioState
            {
                Id = ReadText(block, "id", "scenario.id", problems) ?? snapshot.Id,
                StartDate = run.StartDate,
                ContentHash = ReadText(block, "contentHash", "scenario.contentHash", problems) ?? snapshot.ContentHash,
                Races = new RaceBook(run.StartDate),
            };
            ScenarioStatus status;
            state.Status = TryReadEnum(block, "status", out status) ? status : ScenarioStatus.None;

            ReadGoals(block.GetObject("goals"), state, problems);
            ReadRaces(block.GetObject("races"), state.Races, problems);
            state.Fail = ReadFail(block, "fail", "scenario.fail", problems);
            state.Result = ReadResult(block, problems);

            run.Scenario = snapshot;
            run.ScenarioState = state;
        }

        private static void ReadGoals(JsonObject goals, ScenarioState state, List<string> problems)
        {
            if (goals == null) return;

            foreach (KeyValuePair<string, JsonNode> pair in goals)
            {
                string path = "scenario.goals." + pair.Key;
                var item = pair.Value as JsonObject;
                if (item == null || !IsUsableId(pair.Key))
                {
                    Add(problems, path + ": not an object with a usable id, dropped");
                    continue;
                }

                var goal = new GoalState();
                GoalStatus status;
                goal.Status = TryReadEnum(item, "status", out status) ? status : GoalStatus.Open;
                goal.DoneOn = ReadDate(item, "doneOn", path + ".doneOn", problems);
                goal.FailedOn = ReadDate(item, "failedOn", path + ".failedOn", problems);
                goal.HoldDays = ReadCounter(item, "holdDays", path + ".holdDays", problems, MaxCounter);
                state.Goals[pair.Key] = goal;
            }
        }

        private static void ReadRaces(JsonObject races, RaceBook book, List<string> problems)
        {
            if (races == null) return;

            foreach (KeyValuePair<string, JsonNode> pair in races)
            {
                string path = "scenario.races." + pair.Key;
                var item = pair.Value as JsonObject;
                RaceStatus status;
                if (item == null || !IsUsableId(pair.Key) || !TryReadEnum(item, "status", out status) || status == RaceStatus.Open)
                {
                    Add(problems, path + ": not a decided race, dropped");
                    continue;
                }

                book.Entries[pair.Key] = new RaceEntry
                {
                    GoalId = pair.Key,
                    Status = status,
                    WinnerName = ReadText(item, "winnerName", path + ".winnerName", problems),
                    WonOn = ReadDate(item, "wonOn", path + ".wonOn", problems),
                };
            }
        }

        private static FailState ReadFail(JsonObject parent, string key, string path, List<string> problems)
        {
            var fail = new FailState();
            JsonObject block = parent.GetObject(key);
            if (block == null) return fail;

            ReadHoldDays(block.GetObject("holdDays"), fail, path + ".holdDays", problems);
            fail.TriggeredId = ReadText(block, "triggeredId", path + ".triggeredId", problems);
            fail.TextKey = ReadText(block, "textKey", path + ".textKey", problems);
            fail.TextArgs = ReadTextList(block, "textArgs", path + ".textArgs", problems).ToArray();
            fail.TriggeredOn = ReadDate(block, "triggeredOn", path + ".triggeredOn", problems);
            return fail;
        }

        private static void ReadHoldDays(JsonObject holds, FailState fail, string path, List<string> problems)
        {
            if (holds == null) return;
            foreach (KeyValuePair<string, JsonNode> pair in holds)
            {
                int days;
                var value = pair.Value as JsonValue;
                if (!IsUsableId(pair.Key) || value == null || !value.TryGetInt32(out days) || days < 0)
                {
                    Add(problems, path + "." + pair.Key + ": not a count of days, dropped");
                    continue;
                }
                fail.HoldDays[pair.Key] = Math.Min(days, MaxCounter);
            }
        }

        private static ScenarioResult ReadResult(JsonObject parent, List<string> problems)
        {
            JsonObject block = parent.GetObject("result");
            if (block == null) return null;

            ScenarioStatus outcome;
            if (!TryReadEnum(block, "outcome", out outcome) || (outcome != ScenarioStatus.Won && outcome != ScenarioStatus.Lost))
            {
                Add(problems, "scenario.result.outcome: not Won or Lost, the result is dropped");
                return null;
            }

            var result = new ScenarioResult
            {
                Outcome = outcome,
                Score = ReadCounter(block, "score", "scenario.result.score", problems, MaxCounter),
                Medal = ReadText(block, "medal", "scenario.result.medal", problems),
                FailId = ReadText(block, "failId", "scenario.result.failId", problems),
                FailTextKey = ReadText(block, "failTextKey", "scenario.result.failTextKey", problems),
                FailArgs = ReadTextList(block, "failArgs", "scenario.result.failArgs", problems).ToArray(),
            };
            DateTime finished;
            if (TryReadDate(block, "finishedOn", "scenario.result.finishedOn", problems, out finished)) result.FinishedOn = finished;

            JsonObject breakdown = block.GetObject("breakdown");
            if (breakdown != null)
            {
                const string path = "scenario.result.breakdown";
                result.Breakdown = new ScoreBreakdown
                {
                    GoalPoints = ReadCounter(breakdown, "goalPoints", path + ".goalPoints", problems, MaxCounter),
                    TimeBonus = ReadCounter(breakdown, "timeBonus", path + ".timeBonus", problems, MaxCounter),
                    DaysLeft = ReadCounter(breakdown, "daysLeft", path + ".daysLeft", problems, MaxCounter),
                    TotalDays = ReadCounter(breakdown, "totalDays", path + ".totalDays", problems, MaxCounter),
                    ChallengeBonusPercent = ReadCounter(breakdown, "challengeBonusPercent", path + ".challengeBonusPercent", problems, MaxCounter),
                    Score = ReadCounter(breakdown, "score", path + ".score", problems, MaxCounter),
                };
            }
            return result;
        }

        // ---- metrics ----

        private static void ReadMetrics(JsonObject block, MetricsState metrics, List<string> problems)
        {
            if (block == null) return;

            metrics.ShareWindow = ReadWindow(block.GetObject("shareWindow"), problems);
            metrics.ProfitStreakMonths = ReadCounter(block, "profitStreakMonths", "metrics.profitStreakMonths", problems, MaxCounter);
            metrics.SolventStreakDays = ReadCounter(block, "solventStreakDays", "metrics.solventStreakDays", problems, MaxCounter);
            metrics.NegativeStreakDays = ReadCounter(block, "negativeStreakDays", "metrics.negativeStreakDays", problems, MaxCounter);
            metrics.MissedZeroStreakDays = ReadCounter(block, "missedZeroStreakDays", "metrics.missedZeroStreakDays", problems, MaxCounter);
            metrics.IpsLeaderStreakDays = ReadCounter(block, "ipsLeaderStreakDays", "metrics.ipsLeaderStreakDays", problems, MaxCounter);
            metrics.SalesTotal = ReadLongCounter(block, "salesTotal", "metrics.salesTotal", problems);
            metrics.OemSignedTotal = ReadCounter(block, "oemSignedTotal", "metrics.oemSignedTotal", problems, MaxCounter);
            metrics.RivalsBankrupt = ReadCounter(block, "rivalsBankrupt", "metrics.rivalsBankrupt", problems, MaxCounter);
            metrics.DefaultRecovered = ReadCounter(block, "defaultRecovered", "metrics.defaultRecovered", problems, MaxCounter);
            metrics.RunDays = ReadCounter(block, "runDays", "metrics.runDays", problems, MaxCounter);
            metrics.LastMonthKey = ReadCounter(block, "lastMonthKey", "metrics.lastMonthKey", problems, MaxCounter);
            metrics.WasInDefaultYesterday = block.GetBool("wasInDefaultYesterday", false);

            double minMoney;
            metrics.MinMoney = block.TryGetDouble("minMoney", out minMoney) ? minMoney : MetricsState.NoMinMoney;

            ReadEventCounts(block.GetArray("eventCounts"), metrics, problems);
        }

        private static RollingWindow ReadWindow(JsonObject block, List<string> problems)
        {
            if (block == null) return new RollingWindow();

            int days = block.GetInt32("days", RollingWindow.DefaultDays);
            if (days < 1 || days > MaxWindowDays)
            {
                Add(problems, "metrics.shareWindow.days: " + days + " is out of range, read as " + RollingWindow.DefaultDays);
                days = RollingWindow.DefaultDays;
            }
            int head = block.GetInt32("head", 0);
            return RollingWindow.Restore(days, head, ReadRows(block.GetArray("player")), ReadRows(block.GetArray("total")));
        }

        private static long[][] ReadRows(JsonArray rows)
        {
            if (rows == null) return null;

            var result = new long[Math.Min(rows.Count, MaxWindowDays)][];
            for (int i = 0; i < result.Length; i++)
            {
                var row = rows[i] as JsonArray;
                if (row == null) continue;

                var values = new long[Math.Min(row.Count, RollingWindow.Columns)];
                for (int c = 0; c < values.Length; c++)
                {
                    long value;
                    var number = row[c] as JsonValue;
                    values[c] = number != null && number.TryGetInt64(out value) && value > 0 ? Math.Min(value, MaxLongCounter) : 0L;
                }
                result[i] = values;
            }
            return result;
        }

        private static void ReadEventCounts(JsonArray pairs, MetricsState metrics, List<string> problems)
        {
            if (pairs == null) return;

            foreach (JsonNode node in pairs)
            {
                if (metrics.EventCounts.Count >= MaxEventKinds)
                {
                    Add(problems, "metrics.eventCounts: more than " + MaxEventKinds + " kinds of events, the rest is ignored");
                    break;
                }

                var pair = node as JsonArray;
                string id = null;
                int count = 0;
                bool ok = pair != null && pair.Count == 2;
                if (ok)
                {
                    var name = pair[0] as JsonValue;
                    var number = pair[1] as JsonValue;
                    ok = name != null && name.Kind == JsonKind.String && IsUsableId(name.StringValue)
                         && number != null && number.TryGetInt32(out count) && count >= 0;
                    if (ok) id = name.StringValue;
                }
                if (!ok)
                {
                    Add(problems, "metrics.eventCounts: " + Describe(node) + " is not a pair [id, count], dropped");
                    continue;
                }
                metrics.EventCounts[id] = Math.Min(count, MaxCounter);
            }
        }

        // ---- write ----

        /// <summary>
        /// The run as the section, without <c>_schema</c> (the core adds it); a fresh object each time. A null run gives null: nothing to store
        /// (the core leaves the section out). A run without a scenario writes <c>"scenario": null</c>. The snapshot of the scenario is the JSON
        /// record the definition was mapped from (<see cref="ScenarioDefinition.Source"/>): a definition built by hand has none, is written with
        /// <c>"definition": null</c> and reads back as a run without a scenario.
        /// </summary>
        public static JsonObject Write(RunState run)
        {
            if (run == null) return null;

            var section = new JsonObject();
            section.Set("run", WriteRun(run));
            section.Set("scenario", run.HasScenario ? (JsonNode)WriteScenario(run) : JsonValue.Null);
            section.Set("metrics", WriteMetrics(run.Metrics ?? new MetricsState()));
            section.Set("achievementsThisRun", TextArray(run.AchievementsThisRun));
            section.Remove(SchemaMember);
            return section;
        }

        private static JsonObject WriteRun(RunState run)
        {
            var block = new JsonObject();
            block.Set("runId", JsonValue.Of(run.RunId));
            block.Set("origin", run.Origin == RunOrigin.Legacy ? "legacy" : "newGame");
            block.Set("startDate", DateValue(run.StartDate));
            block.Set("difficultyAtStart", run.DifficultyAtStart);
            block.Set("assisted", run.Assisted);
            block.Set("assistedReasons", TextArray(run.AssistedReasons));
            block.Set("custom", run.Custom);
            block.Set("featuresAtStart", TextArray(run.FeaturesAtStart));
            block.Set("challenges", TextArray(run.Challenges));
            block.Set("challengeStates", WriteChallengeStates(run));
            block.Set("scenarioEntryIds", TextArray(run.ScenarioEntryIds));

            var transferred = new List<int>(run.Metrics == null ? new HashSet<int>() : run.Metrics.TransferredCpuIds);
            transferred.Sort();
            var ids = new JsonArray();
            foreach (int id in transferred) ids.Add(id);
            block.Set("transferredCpuIds", ids);

            block.Set("briefingMailId", run.BriefingMailId);
            block.Set("resultMailId", run.ResultMailId);
            return block;
        }

        private static JsonObject WriteChallengeStates(RunState run)
        {
            var states = new JsonObject();
            if (run.ChallengeStates == null) return states;

            foreach (string id in OrderedKeys(run.Challenges, run.ChallengeStates.Items.Keys))
            {
                ChallengeState state = run.ChallengeStates.Items[id];
                if (state == null) continue;

                var item = new JsonObject();
                item.Set("status", state.Status.ToString());
                item.Set("failedOn", DateValue(state.FailedOn));
                item.Set("passedOn", DateValue(state.PassedOn));
                item.Set("entryIds", TextArray(state.EntryIds));
                if (state.Fail != null && state.Fail.HoldDays.Count > 0) item.Set("holdDays", HoldDaysObject(state.Fail));
                states.Set(id, item);
            }
            return states;
        }

        private static JsonObject WriteScenario(RunState run)
        {
            ScenarioState state = run.ScenarioState;
            var block = new JsonObject();
            block.Set("id", JsonValue.Of(state.Id ?? run.Scenario.Id));
            block.Set("status", state.Status.ToString());
            block.Set("definition", run.Scenario.Source == null ? (JsonNode)JsonValue.Null : run.Scenario.Source.DeepClone());
            block.Set("contentHash", JsonValue.Of(state.ContentHash ?? run.Scenario.ContentHash));

            var goals = new JsonObject();
            var goalIds = new List<string>();
            if (run.Scenario.Goals != null)
            {
                foreach (GoalSpec goal in run.Scenario.Goals)
                {
                    if (goal != null && goal.Id != null) goalIds.Add(goal.Id);
                }
            }
            foreach (string id in OrderedKeys(goalIds, state.Goals.Keys))
            {
                GoalState goal = state.Goals[id];
                if (goal == null) continue;

                var item = new JsonObject();
                item.Set("status", goal.Status.ToString());
                item.Set("doneOn", DateValue(goal.DoneOn));
                item.Set("failedOn", DateValue(goal.FailedOn));
                item.Set("holdDays", goal.HoldDays);
                goals.Set(id, item);
            }
            block.Set("goals", goals);

            var races = new JsonObject();
            if (state.Races != null)
            {
                foreach (string id in OrderedKeys(goalIds, state.Races.Entries.Keys))
                {
                    RaceEntry entry = state.Races.Entries[id];
                    if (entry == null || entry.Status == RaceStatus.Open) continue;

                    var item = new JsonObject();
                    item.Set("status", entry.Status.ToString());
                    item.Set("winnerName", JsonValue.Of(entry.WinnerName));
                    item.Set("wonOn", DateValue(entry.WonOn));
                    races.Set(id, item);
                }
            }
            block.Set("races", races);

            block.Set("fail", WriteFail(state.Fail));
            block.Set("result", WriteResult(state.Result));
            return block;
        }

        // The fail block is null until a rule has a hold counter or the scenario is lost (as in the example of the design).
        private static JsonNode WriteFail(FailState fail)
        {
            if (fail == null || (fail.HoldDays.Count == 0 && !fail.IsTriggered)) return JsonValue.Null;

            var block = new JsonObject();
            if (fail.HoldDays.Count > 0) block.Set("holdDays", HoldDaysObject(fail));
            block.Set("triggeredId", JsonValue.Of(fail.TriggeredId));
            block.Set("textKey", JsonValue.Of(fail.TextKey));
            block.Set("textArgs", TextArray(fail.TextArgs));
            block.Set("triggeredOn", DateValue(fail.TriggeredOn));
            return block;
        }

        private static JsonNode WriteResult(ScenarioResult result)
        {
            if (result == null) return JsonValue.Null;

            var block = new JsonObject();
            block.Set("outcome", result.Outcome.ToString());
            block.Set("finishedOn", DateValue(result.FinishedOn));
            block.Set("score", result.Score);
            block.Set("medal", JsonValue.Of(result.Medal));
            block.Set("failId", JsonValue.Of(result.FailId));
            block.Set("failTextKey", JsonValue.Of(result.FailTextKey));
            block.Set("failArgs", TextArray(result.FailArgs));
            if (result.Breakdown != null)
            {
                ScoreBreakdown b = result.Breakdown;
                var breakdown = new JsonObject();
                breakdown.Set("goalPoints", b.GoalPoints);
                breakdown.Set("timeBonus", b.TimeBonus);
                breakdown.Set("daysLeft", b.DaysLeft);
                breakdown.Set("totalDays", b.TotalDays);
                breakdown.Set("challengeBonusPercent", b.ChallengeBonusPercent);
                breakdown.Set("score", b.Score);
                block.Set("breakdown", breakdown);
            }
            else
            {
                block.Set("breakdown", JsonValue.Null);
            }
            return block;
        }

        private static JsonObject WriteMetrics(MetricsState metrics)
        {
            var block = new JsonObject();
            block.Set("shareWindow", WriteWindow(metrics.ShareWindow ?? new RollingWindow()));
            block.Set("profitStreakMonths", metrics.ProfitStreakMonths);
            block.Set("solventStreakDays", metrics.SolventStreakDays);
            block.Set("negativeStreakDays", metrics.NegativeStreakDays);
            block.Set("missedZeroStreakDays", metrics.MissedZeroStreakDays);
            block.Set("ipsLeaderStreakDays", metrics.IpsLeaderStreakDays);
            block.Set("minMoney", metrics.MinMoney < MetricsState.NoMinMoney ? (JsonNode)JsonValue.Of(metrics.MinMoney) : JsonValue.Null);
            block.Set("salesTotal", metrics.SalesTotal);
            block.Set("oemSignedTotal", metrics.OemSignedTotal);
            block.Set("rivalsBankrupt", metrics.RivalsBankrupt);
            block.Set("defaultRecovered", metrics.DefaultRecovered);
            block.Set("wasInDefaultYesterday", metrics.WasInDefaultYesterday);
            block.Set("runDays", metrics.RunDays);
            block.Set("lastMonthKey", metrics.LastMonthKey);

            var pairs = new JsonArray();
            var kinds = new List<string>(metrics.EventCounts.Keys);
            kinds.Sort(StringComparer.Ordinal);
            foreach (string kind in kinds)
            {
                var pair = new JsonArray();
                pair.Add(kind);
                pair.Add(metrics.EventCounts[kind]);
                pairs.Add(pair);
            }
            block.Set("eventCounts", pairs);
            return block;
        }

        private static JsonObject WriteWindow(RollingWindow window)
        {
            var block = new JsonObject();
            block.Set("head", window.Head);
            block.Set("days", window.Days);

            var player = new JsonArray(window.Days);
            var total = new JsonArray(window.Days);
            for (int slot = 0; slot < window.Days; slot++)
            {
                var p = new JsonArray(RollingWindow.Columns);
                var t = new JsonArray(RollingWindow.Columns);
                for (int column = 0; column < RollingWindow.Columns; column++)
                {
                    p.Add(window.PlayerAt(slot, column));
                    t.Add(window.TotalAt(slot, column));
                }
                player.Add(p);
                total.Add(t);
            }
            block.Set("player", player);
            block.Set("total", total);
            return block;
        }

        // ---- helpers ----

        private static JsonObject HoldDaysObject(FailState fail)
        {
            var holds = new JsonObject();
            var ids = new List<string>(fail.HoldDays.Keys);
            ids.Sort(StringComparer.Ordinal);
            foreach (string id in ids) holds.Set(id, fail.HoldDays[id]);
            return holds;
        }

        // The keys in the order of the preferred list (those that exist), then the rest in ordinal order: a stable text for the same state.
        private static List<string> OrderedKeys(IEnumerable<string> preferred, IEnumerable<string> all)
        {
            var keys = new HashSet<string>(all, StringComparer.Ordinal);
            var ordered = new List<string>();
            if (preferred != null)
            {
                foreach (string id in preferred)
                {
                    if (id != null && keys.Remove(id)) ordered.Add(id);
                }
            }
            var rest = new List<string>(keys);
            rest.Sort(StringComparer.Ordinal);
            ordered.AddRange(rest);
            return ordered;
        }

        private static JsonArray TextArray(IEnumerable<string> items)
        {
            var array = new JsonArray();
            if (items != null)
            {
                foreach (string item in items)
                {
                    if (item != null) array.Add(item);
                }
            }
            return array;
        }

        private static JsonNode DateValue(DateTime? date)
        {
            return date == null || date.Value == default(DateTime)
                ? (JsonNode)JsonValue.Null
                : JsonValue.Of(date.Value.ToString(DateFormat, CultureInfo.InvariantCulture));
        }

        private static string ReadText(JsonObject obj, string key, string path, List<string> problems)
        {
            JsonNode node;
            if (!obj.TryGetValue(key, out node) || node == null || node.IsNull) return null;

            string text;
            if (!obj.TryGetString(key, out text))
            {
                Add(problems, path + ": " + Describe(node) + " is not a text, ignored");
                return null;
            }
            if (text.Length > MaxTextLength)
            {
                Add(problems, path + ": longer than " + MaxTextLength + " characters, ignored");
                return null;
            }
            return text.Length == 0 ? null : text;
        }

        private static List<string> ReadTextList(JsonObject obj, string key, string path, List<string> problems)
        {
            var list = new List<string>();
            JsonNode node;
            if (!obj.TryGetValue(key, out node) || node == null || node.IsNull) return list;

            var array = node as JsonArray;
            if (array == null)
            {
                Add(problems, path + ": " + Describe(node) + " is not a list, ignored");
                return list;
            }

            foreach (JsonNode item in array)
            {
                if (list.Count >= MaxListItems)
                {
                    Add(problems, path + ": more than " + MaxListItems + " items, the rest is ignored");
                    break;
                }
                var text = item as JsonValue;
                if (text == null || text.Kind != JsonKind.String || text.StringValue.Length == 0 || text.StringValue.Length > MaxTextLength)
                {
                    Add(problems, path + ": " + Describe(item) + " is not a usable text, dropped");
                    continue;
                }
                list.Add(text.StringValue);
            }
            return list;
        }

        private static DateTime? ReadDate(JsonObject obj, string key, string path, List<string> problems)
        {
            DateTime date;
            return TryReadDate(obj, key, path, problems, out date) ? (DateTime?)date : null;
        }

        private static bool TryReadDate(JsonObject obj, string key, string path, List<string> problems, out DateTime date)
        {
            date = default(DateTime);
            JsonNode node;
            if (!obj.TryGetValue(key, out node) || node == null || node.IsNull) return false;

            string text;
            if (obj.TryGetString(key, out text) &&
                DateTime.TryParseExact(text, DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out date))
            {
                date = date.Date;
                return true;
            }

            Add(problems, path + ": " + Describe(node) + " is not a date (" + DateFormat + "), ignored");
            return false;
        }

        private static int ReadCounter(JsonObject obj, string key, string path, List<string> problems, int max)
        {
            JsonNode node;
            if (!obj.TryGetValue(key, out node) || node == null || node.IsNull) return 0;

            int value;
            if (!obj.TryGetInt32(key, out value))
            {
                Add(problems, path + ": " + Describe(node) + " is not a whole number, read as 0");
                return 0;
            }
            if (value < 0)
            {
                Add(problems, path + ": " + value + " is below 0, read as 0");
                return 0;
            }
            if (value > max)
            {
                Add(problems, path + ": " + value + " is above " + max + ", brought down");
                return max;
            }
            return value;
        }

        private static long ReadLongCounter(JsonObject obj, string key, string path, List<string> problems)
        {
            JsonNode node;
            if (!obj.TryGetValue(key, out node) || node == null || node.IsNull) return 0;

            long value;
            if (!obj.TryGetInt64(key, out value))
            {
                Add(problems, path + ": " + Describe(node) + " is not a whole number, read as 0");
                return 0;
            }
            if (value < 0) return 0;
            return Math.Min(value, MaxLongCounter);
        }

        /// <summary>A name of the enum (any case) or its number; anything else, and a number the enum does not have, fails.</summary>
        private static bool TryReadEnum<T>(JsonObject obj, string key, out T value) where T : struct
        {
            value = default(T);
            JsonNode node;
            if (!obj.TryGetValue(key, out node)) return false;

            var scalar = node as JsonValue;
            if (scalar == null) return false;

            if (scalar.Kind == JsonKind.String)
            {
                string text = (scalar.StringValue ?? string.Empty).Trim();
                if (text.Length == 0 || char.IsDigit(text[0]) || text[0] == '-') return false;   // "7" is not a name
                return Enum.TryParse(text, true, out value) && Enum.IsDefined(typeof(T), value);
            }

            int number;
            if (scalar.Kind == JsonKind.Number && scalar.TryGetInt32(out number) && Enum.IsDefined(typeof(T), number))
            {
                value = (T)Enum.ToObject(typeof(T), number);
                return true;
            }
            return false;
        }

        private static bool IsUsableId(string id)
        {
            return !string.IsNullOrEmpty(id) && id.Length <= MaxTextLength;
        }

        private static string Describe(JsonNode node)
        {
            string text = node == null ? "null" : ModJson.Write(node, false);
            return text.Length > 40 ? text.Substring(0, 37) + "..." : text;
        }

        private static void Add(List<string> problems, string line)
        {
            if (problems != null) problems.Add(line);
        }
    }
}
