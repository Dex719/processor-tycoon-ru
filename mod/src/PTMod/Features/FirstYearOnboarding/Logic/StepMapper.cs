using System;
using System.Collections.Generic;
using PTMod.Core.Logic.Json;

namespace PTMod.Features.FirstYearOnboarding.Logic
{
    /// <summary>
    /// Reads the JSON of <c>steps.json</c> and <c>config.json</c> into <see cref="StepDefinition"/> and <see cref="OnboardingConfig"/>
    /// (design.md, "StepMapper"). It only checks the <b>shape</b> of the JSON: a field of the wrong kind (a number where a string is expected,
    /// an unknown <c>kind</c>) makes the record unusable and is described in the error list; whether the values make sense (ranges, known tracks,
    /// known conditions, existing strings) is <see cref="StepValidator"/>'s business. Unknown fields are ignored (NFR-6). Never throws.
    /// Error texts are English, <c>field: message</c> with the path of the field (<c>mail.args[1]: expected a string</c>).
    /// </summary>
    public static class StepMapper
    {
        /// <summary>Builds the step from a record of <c>steps.json</c>; null (with at least one error) when the record cannot be read.</summary>
        public static StepDefinition Map(JsonObject json, IList<string> errors)
        {
            if (json == null)
            {
                Add(errors, "$: the record is missing");
                return null;
            }

            var found = new List<string>();
            try
            {
                var step = new StepDefinition();

                step.Id = ReadString(json, "id", true, found);
                step.Order = ReadInt(json, "order", 0, found);
                step.Kind = ReadKind(json, found);
                step.Tracks = ReadStrings(json, "tracks", found);
                step.ShowWhen = ReadCondition(json, "showWhen", found);
                step.CompleteWhen = ReadCondition(json, "completeWhen", found);
                step.CompleteOnShow = ReadBool(json, "completeOnShow", false, found);
                step.StreakDays = ReadInt(json, "streakDays", 0, found);
                step.Priority = ReadString(json, "priority", false, found);
                if (string.IsNullOrEmpty(step.Priority)) step.Priority = "normal";
                step.PauseOnShow = ReadBool(json, "pauseOnShow", false, found);
                step.Mail = ReadMail(json, found);
                step.Reminders = ReadReminders(json, found);

                if (found.Count == 0) return step;
            }
            catch (Exception ex)
            {
                found.Add("$: the record could not be read (" + ex.GetType().Name + ": " + ex.Message + ")");
            }

            if (errors != null)
            {
                foreach (string message in found) errors.Add(message);
            }
            return null;
        }

        /// <summary>
        /// Builds the rhythm from the merged <c>config.json</c>. Never null: a missing or wrong field keeps its default, a number out of its
        /// range is brought into the range; each such correction is a line in <paramref name="warnings"/> (may be null).
        /// </summary>
        public static OnboardingConfig MapConfig(JsonObject json, IList<string> warnings)
        {
            var config = new OnboardingConfig();
            if (json == null) return config;

            try
            {
                config.TutorialWindowDays = ReadConfigInt(json, "tutorialWindowDays", OnboardingConfig.DefaultTutorialWindowDays, 30, 3650, warnings);
                config.MinDaysBetweenMails = ReadConfigInt(json, "minDaysBetweenMails", OnboardingConfig.DefaultMinDaysBetweenMails, 1, 365, warnings);
                config.DefaultDaysFallback = ReadConfigInt(json, "defaultDaysFallback", OnboardingConfig.DefaultDefaultDaysFallback, 1, 3650, warnings);
            }
            catch (Exception ex)
            {
                Add(warnings, "config: could not be read, defaults are used (" + ex.GetType().Name + ": " + ex.Message + ")");
                return new OnboardingConfig();
            }
            return config;
        }

        // ---- config ----

        private static int ReadConfigInt(JsonObject json, string name, int defaultValue, int min, int max, IList<string> warnings)
        {
            JsonNode node = json[name];
            if (node == null) return defaultValue;

            var scalar = node as JsonValue;
            bool usable = scalar != null && scalar.Kind == JsonKind.Number
                          && !double.IsNaN(scalar.NumberValue) && !double.IsInfinity(scalar.NumberValue);
            if (!usable)
            {
                Add(warnings, "config." + name + ": expected a number, " + defaultValue + " is used");
                return defaultValue;
            }

            double number = scalar.NumberValue;
            int value = number >= int.MaxValue ? int.MaxValue : number <= int.MinValue ? int.MinValue : (int)Math.Round(number);
            if (value < min || value > max)
            {
                int clamped = value < min ? min : max;
                Add(warnings, "config." + name + ": " + value + " is outside " + min + ".." + max + ", " + clamped + " is used");
                return clamped;
            }
            return value;
        }

        // ---- the parts of a record ----

        private static StepKind ReadKind(JsonObject json, IList<string> errors)
        {
            string text = ReadString(json, "kind", false, errors);
            if (string.IsNullOrEmpty(text)) return StepKind.Sequential;
            if (string.Equals(text, "sequential", StringComparison.OrdinalIgnoreCase)) return StepKind.Sequential;
            if (string.Equals(text, "situational", StringComparison.OrdinalIgnoreCase)) return StepKind.Situational;

            Add(errors, "kind: unknown kind '" + text + "' (expected sequential or situational)");
            return StepKind.Sequential;
        }

        private static MailSpec ReadMail(JsonObject json, IList<string> errors)
        {
            var mail = new MailSpec();
            JsonNode node = json["mail"];
            if (node == null || node.IsNull)
            {
                Add(errors, "mail: the object is missing");
                return mail;
            }

            var obj = node as JsonObject;
            if (obj == null)
            {
                Add(errors, "mail: expected an object");
                return mail;
            }

            mail.TitleKey = ReadString(obj, "titleKey", false, errors, "mail.");
            mail.BodyKey = ReadString(obj, "bodyKey", false, errors, "mail.");
            mail.Args = ReadStrings(obj, "args", errors, "mail.");
            mail.Buttons = ReadStrings(obj, "buttons", errors, "mail.");
            mail.Variants = ReadVariants(obj, errors);
            return mail;
        }

        private static BodyVariant[] ReadVariants(JsonObject mail, IList<string> errors)
        {
            JsonNode node = mail["variants"];
            if (node == null || node.IsNull) return new BodyVariant[0];

            var array = node as JsonArray;
            if (array == null)
            {
                Add(errors, "mail.variants: expected an array");
                return new BodyVariant[0];
            }

            var result = new List<BodyVariant>(array.Count);
            for (int i = 0; i < array.Count; i++)
            {
                string path = "mail.variants[" + i + "]";
                var item = array[i] as JsonObject;
                if (item == null)
                {
                    Add(errors, path + ": expected an object");
                    continue;
                }

                var variant = new BodyVariant();
                variant.When = ReadCondition(item, "when", errors, path + ".");
                if (variant.When == null) Add(errors, path + ".when: the condition is missing");
                variant.BodyKey = ReadString(item, "bodyKey", false, errors, path + ".");
                result.Add(variant);
            }
            return result.ToArray();
        }

        private static ReminderSpec[] ReadReminders(JsonObject json, IList<string> errors)
        {
            JsonNode node = json["reminders"];
            if (node == null || node.IsNull) return new ReminderSpec[0];

            var array = node as JsonArray;
            if (array == null)
            {
                Add(errors, "reminders: expected an array");
                return new ReminderSpec[0];
            }

            var result = new List<ReminderSpec>(array.Count);
            for (int i = 0; i < array.Count; i++)
            {
                string path = "reminders[" + i + "]";
                var item = array[i] as JsonObject;
                if (item == null)
                {
                    Add(errors, path + ": expected an object");
                    continue;
                }

                var reminder = new ReminderSpec();
                reminder.AfterDays = ReadInt(item, "afterDays", 0, errors, path + ".");
                reminder.HintKey = ReadString(item, "hintKey", false, errors, path + ".");
                reminder.Args = ReadStrings(item, "args", errors, path + ".");
                result.Add(reminder);
            }
            return result.ToArray();
        }

        // ---- scalars ----

        private static ConditionRef ReadCondition(JsonObject json, string name, IList<string> errors, string prefix = "")
        {
            JsonNode node = json[name];
            if (node == null || node.IsNull) return null;

            if (!node.IsObject)
            {
                Add(errors, prefix + name + ": expected an object (a condition)");
                return null;
            }
            return new ConditionRef(node);
        }

        private static string ReadString(JsonObject json, string name, bool required, IList<string> errors, string prefix = "")
        {
            JsonNode node = json[name];
            if (node == null || node.IsNull)
            {
                if (required) Add(errors, prefix + name + ": required");
                return null;
            }

            var scalar = node as JsonValue;
            if (scalar == null || scalar.Kind != JsonKind.String)
            {
                Add(errors, prefix + name + ": expected a string");
                return null;
            }

            if (required && scalar.StringValue.Length == 0) Add(errors, prefix + name + ": required");
            return scalar.StringValue;
        }

        private static int ReadInt(JsonObject json, string name, int defaultValue, IList<string> errors, string prefix = "")
        {
            JsonNode node = json[name];
            if (node == null || node.IsNull) return defaultValue;

            int value;
            var scalar = node as JsonValue;
            if (scalar == null || !scalar.TryGetInt32(out value))
            {
                Add(errors, prefix + name + ": expected an integer");
                return defaultValue;
            }
            return value;
        }

        private static bool ReadBool(JsonObject json, string name, bool defaultValue, IList<string> errors)
        {
            JsonNode node = json[name];
            if (node == null || node.IsNull) return defaultValue;

            var scalar = node as JsonValue;
            if (scalar == null || scalar.Kind != JsonKind.Bool)
            {
                Add(errors, name + ": expected true or false");
                return defaultValue;
            }
            return scalar.BoolValue;
        }

        private static string[] ReadStrings(JsonObject json, string name, IList<string> errors, string prefix = "")
        {
            JsonNode node = json[name];
            if (node == null || node.IsNull) return new string[0];

            var array = node as JsonArray;
            if (array == null)
            {
                Add(errors, prefix + name + ": expected an array of strings");
                return new string[0];
            }

            var result = new List<string>(array.Count);
            for (int i = 0; i < array.Count; i++)
            {
                var scalar = array[i] as JsonValue;
                if (scalar == null || scalar.Kind != JsonKind.String)
                {
                    Add(errors, prefix + name + "[" + i + "]: expected a string");
                    continue;
                }
                result.Add(scalar.StringValue);
            }
            return result.ToArray();
        }

        private static void Add(IList<string> list, string message)
        {
            if (list != null) list.Add(message);
        }
    }
}
