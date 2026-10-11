using System;
using System.Collections.Generic;
using System.Globalization;
using PTMod.Core.Logic.Json;

namespace PTMod.Features.FirstYearOnboarding.Logic
{
    /// <summary>
    /// Reads and writes the section <c>first-year-onboarding</c> of the side save file (FR-10, NFR-3, NFR-6): the <see cref="OnboardingState"/> to
    /// and from the JSON object the core hands over (design.md, "OnboardingState"). The version of the section (<c>_schema</c>) is the core's
    /// business: <see cref="SchemaVersion"/> is what the participant reports, and <see cref="Write"/> never returns a <c>_schema</c> member.
    /// Never throws.
    /// <para>
    /// <see cref="Read"/> takes what a damaged or hand-edited file can hold and makes it usable (Security, "the section of the save is checked"):
    /// an unknown mode is <see cref="OnboardingMode.Off"/>, an unknown status is <see cref="StepStatus.Pending"/>, a date that is not
    /// <c>yyyy-MM-dd</c> is missing, a number out of range is brought into range, a step record without an id is dropped. Every change is one line
    /// in the problems. A record of a step that the content no longer has is kept as it is, so a later version of the content finds it again.
    /// </para>
    /// </summary>
    public static class OnboardingSaveCodec
    {
        /// <summary>The id of the section: the id of the feature.</summary>
        public const string SectionId = "first-year-onboarding";

        /// <summary>The version of the section this build writes (<c>IModSaveParticipant.SchemaVersion</c>).</summary>
        public const int SchemaVersion = 1;

        /// <summary>The format of the dates of the section.</summary>
        public const string DateFormat = "yyyy-MM-dd";

        /// <summary>The most step records read from a section; a longer list is cut (a file made by hand, not by the game).</summary>
        public const int MaxSteps = 500;

        /// <summary>The longest text read from the section (ids, track, source).</summary>
        public const int MaxTextLength = 200;

        private const string SchemaMember = "_schema";
        private const int MaxCounter = 1000000;

        // ---- read ----

        /// <summary>
        /// The state from the section. A null section (nothing saved) gives the state of a game without a tutorial (<see cref="OnboardingMode.Off"/>).
        /// The object is not changed. Never null. Lines about what was repaired or dropped go to <paramref name="problems"/> (may be null).
        /// </summary>
        public static OnboardingState Read(JsonObject section, List<string> problems)
        {
            var state = new OnboardingState();
            if (section == null) return state;

            try
            {
                state.Mode = ReadMode(section, problems);
                state.Track = ReadText(section, "track", "track", problems);
                state.StartDate = ReadDate(section, "startDate", "startDate", problems);
                state.CurrentStepId = ReadText(section, "currentStepId", "currentStepId", problems);
                state.LastMailDate = ReadDate(section, "lastMailDate", "lastMailDate", problems);
                state.LastHintDate = ReadDate(section, "lastHintDate", "lastHintDate", problems);
                state.Source = ReadText(section, "source", "source", problems);
                ReadCaughtUp(section, state, problems);
                ReadSteps(section, state, problems);
            }
            catch (Exception e)
            {
                // Not expected (every read above is guarded); a surprise must not take the load down.
                Add(problems, "section: unreadable (" + e.GetType().Name + ": " + e.Message + "), the tutorial is off");
                return new OnboardingState();
            }

            return state;
        }

        private static OnboardingMode ReadMode(JsonObject section, List<string> problems)
        {
            JsonNode node;
            if (!section.TryGetValue("mode", out node) || node == null || node.IsNull) return OnboardingMode.Off;

            OnboardingMode mode;
            if (TryParseEnum(node, out mode)) return mode;

            Add(problems, "mode: " + Describe(node) + " is not a mode, the tutorial is off");
            return OnboardingMode.Off;
        }

        private static StepStatus ReadStatus(JsonObject step, string path, List<string> problems)
        {
            JsonNode node;
            if (!step.TryGetValue("status", out node) || node == null || node.IsNull) return StepStatus.Pending;

            StepStatus status;
            if (TryParseEnum(node, out status)) return status;

            Add(problems, path + ".status: " + Describe(node) + " is not a status, read as Pending");
            return StepStatus.Pending;
        }

        private static void ReadCaughtUp(JsonObject section, OnboardingState state, List<string> problems)
        {
            JsonNode node;
            if (!section.TryGetValue("pendingCaughtUp", out node) || node == null || node.IsNull) return;

            var ids = new List<string>();
            var text = node as JsonValue;
            var array = node as JsonArray;
            if (text != null && text.Kind == JsonKind.String)
            {
                ids.AddRange((text.StringValue ?? string.Empty).Split(','));
            }
            else if (array != null)
            {
                foreach (JsonNode item in array)
                {
                    var value = item as JsonValue;
                    if (value != null && value.Kind == JsonKind.String) ids.Add(value.StringValue);
                }
            }
            else
            {
                Add(problems, "pendingCaughtUp: " + Describe(node) + " is not a list of step ids, ignored");
                return;
            }

            foreach (string raw in ids)
            {
                string id = (raw ?? string.Empty).Trim();
                if (id.Length == 0 || id.Length > MaxTextLength || state.PendingCaughtUp.Contains(id)) continue;
                state.PendingCaughtUp.Add(id);
            }
        }

        private static void ReadSteps(JsonObject section, OnboardingState state, List<string> problems)
        {
            JsonNode node;
            if (!section.TryGetValue("steps", out node) || node == null || node.IsNull) return;

            var array = node as JsonArray;
            if (array == null)
            {
                Add(problems, "steps: " + Describe(node) + " is not a list, ignored");
                return;
            }

            for (int i = 0; i < array.Count; i++)
            {
                string path = "steps[" + i + "]";
                if (state.Steps.Count >= MaxSteps)
                {
                    Add(problems, "steps: more than " + MaxSteps + " records, the rest is ignored");
                    break;
                }

                var obj = array[i] as JsonObject;
                if (obj == null)
                {
                    Add(problems, path + ": not an object, dropped");
                    continue;
                }

                string id = ReadText(obj, "id", path + ".id", problems);
                if (string.IsNullOrEmpty(id))
                {
                    Add(problems, path + ": no id, dropped");
                    continue;
                }
                if (state.Find(id) != null)
                {
                    Add(problems, path + ": the id '" + id + "' is a second record, dropped");
                    continue;
                }

                state.Steps.Add(new StepState
                {
                    Id = id,
                    Status = ReadStatus(obj, path, problems),
                    ShownOn = ReadDate(obj, "shownOn", path + ".shownOn", problems),
                    DeliveredOn = ReadDate(obj, "deliveredOn", path + ".deliveredOn", problems),
                    DoneOn = ReadDate(obj, "doneOn", path + ".doneOn", problems),
                    MailId = ReadCounter(obj, "mailId", path + ".mailId", int.MaxValue, problems),
                    RemindersShown = ReadCounter(obj, "remindersShown", path + ".remindersShown", MaxCounter, problems),
                    StreakDays = ReadCounter(obj, "streakDays", path + ".streakDays", MaxCounter, problems)
                });
            }
        }

        // ---- write ----

        /// <summary>
        /// The state as the section, without <c>_schema</c> (the core adds it). A fresh object each time, never null; every member is written, the
        /// ones without a value as JSON null (as in the example of design.md). A null state is written as the state of a game without a tutorial.
        /// </summary>
        public static JsonObject Write(OnboardingState state)
        {
            state = state ?? new OnboardingState();

            var section = new JsonObject();
            section.Set("mode", state.Mode.ToString());
            section.Set("track", JsonValue.Of(state.Track));
            section.Set("startDate", DateValue(state.StartDate));
            section.Set("currentStepId", JsonValue.Of(state.CurrentStepId));
            section.Set("lastMailDate", DateValue(state.LastMailDate));
            section.Set("lastHintDate", DateValue(state.LastHintDate));
            section.Set("pendingCaughtUp", string.Join(",", (state.PendingCaughtUp ?? new List<string>()).ToArray()));
            section.Set("source", JsonValue.Of(state.Source));

            var steps = new JsonArray();
            if (state.Steps != null)
            {
                foreach (StepState step in state.Steps)
                {
                    if (step == null || string.IsNullOrEmpty(step.Id)) continue;

                    var record = new JsonObject();
                    record.Set("id", step.Id);
                    record.Set("status", step.Status.ToString());
                    record.Set("shownOn", DateValue(step.ShownOn));
                    record.Set("deliveredOn", DateValue(step.DeliveredOn));
                    record.Set("doneOn", DateValue(step.DoneOn));
                    record.Set("mailId", step.MailId);
                    record.Set("remindersShown", step.RemindersShown);
                    record.Set("streakDays", step.StreakDays);
                    steps.Add(record);
                }
            }
            section.Set("steps", steps);

            section.Remove(SchemaMember);
            return section;
        }

        // ---- field readers ----

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

        private static DateTime? ReadDate(JsonObject obj, string key, string path, List<string> problems)
        {
            JsonNode node;
            if (!obj.TryGetValue(key, out node) || node == null || node.IsNull) return null;

            string text;
            DateTime date;
            if (obj.TryGetString(key, out text) &&
                DateTime.TryParseExact(text, DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out date))
                return date.Date;

            Add(problems, path + ": " + Describe(node) + " is not a date (" + DateFormat + "), ignored");
            return null;
        }

        private static int ReadCounter(JsonObject obj, string key, string path, int max, List<string> problems)
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

        /// <summary>A name of the enum (any case) or its number; anything else, and a number the enum does not have, fails.</summary>
        private static bool TryParseEnum<T>(JsonNode node, out T value) where T : struct
        {
            value = default(T);
            var scalar = node as JsonValue;
            if (scalar == null) return false;

            if (scalar.Kind == JsonKind.String)
            {
                string text = (scalar.StringValue ?? string.Empty).Trim();
                // "7" is not a name, and "Active,Skipped" must not be read as a combination of two.
                if (text.Length == 0 || char.IsDigit(text[0]) || text[0] == '-' || text.IndexOf(',') >= 0) return false;
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

        private static JsonNode DateValue(DateTime? date)
        {
            return date == null ? (JsonNode)JsonValue.Null : JsonValue.Of(date.Value.ToString(DateFormat, CultureInfo.InvariantCulture));
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
