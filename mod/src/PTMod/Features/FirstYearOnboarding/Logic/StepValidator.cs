using System;
using System.Collections.Generic;
using PTMod.Core.Logic.Json;

namespace PTMod.Features.FirstYearOnboarding.Logic
{
    /// <summary>
    /// What the validation of a step needs from the game layer (design.md, section StepCatalog and StepValidator): the parser of conditions of the core
    /// and the string tables. The game layer implements it over <c>ModConditions.Parse</c> and <c>ModL10n</c>; the tests use a fake.
    /// </summary>
    public interface IStepValidationContext
    {
        /// <summary>
        /// Parses a condition and checks that it names only known predicates with valid parameters. Adds the problems to
        /// <paramref name="errors"/> as <c>path: message</c> (the path starts with <paramref name="path"/>); true when there are none.
        /// </summary>
        bool ValidateCondition(JsonNode node, IList<string> errors, string path);

        /// <summary>The key exists in the string tables (in <c>ru</c> or <c>en</c>).</summary>
        bool HasString(string key);

        /// <summary>
        /// The raw Russian text of the key with its <c>{0}</c> placeholders, or null when the text is not available (no such key, or the game
        /// layer cannot read raw texts). The validator checks the placeholders, the mentor prefix and the length on it; null skips those checks.
        /// </summary>
        string GetRussianText(string key);
    }

    /// <summary>
    /// The checks of one step (design.md, "Validation Rules"): the identity, the track, the conditions, the strings, the placeholders, the
    /// reminders. A step either passes whole or is rejected with every reason found; the rules that do not make a step unusable (the mentor
    /// prefix of the title, a long text, a missing short name) are warnings. Pure logic: the game is reached only through
    /// <see cref="IStepValidationContext"/>. Never throws.
    /// </summary>
    public static class StepValidator
    {
        /// <summary>The three tracks of the tutorial, one per type of company.</summary>
        public static readonly IReadOnlyList<string> Tracks = new[] { "idm", "fabless", "foundry" };

        /// <summary>The priorities of a mail (the priorities of the core).</summary>
        public static readonly IReadOnlyList<string> Priorities = new[] { "critical", "normal", "low" };

        /// <summary>The buttons a mail of the tutorial can carry.</summary>
        public static readonly IReadOnlyList<string> Buttons = new[] { "skipAll", "skipStep", "hint", "openCpuDesigner", "openFactoryExpansion" };

        /// <summary>The names of the placeholders that <c>args</c> may list (FR-5 item 2).</summary>
        public static readonly IReadOnlyList<string> ArgNames = new[]
        {
            "lowEndBudget", "midRangeBudget", "highEndBudget", "industriesBudget", "debtLimit", "defaultDays", "money", "company", "founder"
        };

        public const int MinIdLength = 2;
        public const int MaxIdLength = 40;
        public const int MinOrder = 1;
        public const int MaxOrder = 9999;
        public const int MinAfterDays = 1;
        public const int MaxAfterDays = 3650;
        public const int MaxStreakDays = 365;

        /// <summary>A body longer than this many characters is a warning (NFR-4): the mail has to read without scrolling at 1080p.</summary>
        public const int MaxBodyLength = 1200;

        /// <summary>The prefix every title starts with in Russian: the word for "Mentor" in square brackets (written with escapes to keep the source ASCII).</summary>
        public const string MentorPrefixRu = "[\u041D\u0430\u0441\u0442\u0430\u0432\u043D\u0438\u043A]";

        public static bool IsKnownTrack(string track)
        {
            return Contains(Tracks, track);
        }

        public static bool IsKnownPriority(string priority)
        {
            return Contains(Priorities, priority);
        }

        public static bool IsKnownButton(string button)
        {
            return Contains(Buttons, button);
        }

        public static bool IsKnownArg(string name)
        {
            return Contains(ArgNames, name);
        }

        /// <summary><c>[a-z0-9_]{2,40}</c>.</summary>
        public static bool IsValidId(string id)
        {
            if (string.IsNullOrEmpty(id) || id.Length < MinIdLength || id.Length > MaxIdLength) return false;
            foreach (char ch in id)
            {
                bool ok = (ch >= 'a' && ch <= 'z') || (ch >= '0' && ch <= '9') || ch == '_';
                if (!ok) return false;
            }
            return true;
        }

        /// <summary>
        /// The highest <c>{n}</c> placeholder of a text (the index of <c>{2}</c> and <c>{2:N0}</c> is 2; <c>{{</c> and <c>}}</c> are escaped braces),
        /// or -1 when the text has none.
        /// </summary>
        public static int MaxPlaceholderIndex(string text)
        {
            int max = -1;
            if (string.IsNullOrEmpty(text)) return max;

            int i = 0;
            while (i < text.Length)
            {
                char c = text[i];
                if (c == '{')
                {
                    if (i + 1 < text.Length && text[i + 1] == '{')
                    {
                        i += 2;
                        continue;
                    }

                    int j = i + 1;
                    long value = 0;
                    int digits = 0;
                    while (j < text.Length && text[j] >= '0' && text[j] <= '9' && digits < 9)
                    {
                        value = value * 10 + (text[j] - '0');
                        j++;
                        digits++;
                    }
                    if (digits > 0 && j < text.Length && (text[j] == '}' || text[j] == ',' || text[j] == ':'))
                    {
                        if (value > max) max = (int)value;
                    }
                    i = j > i ? j : i + 1;
                    continue;
                }
                i++;
            }
            return max;
        }

        /// <summary>
        /// Checks one step. True when it can be used; false when it has at least one error (all of them are added to
        /// <paramref name="errors"/>, <c>field: message</c>, English). Soft findings go to <paramref name="warnings"/> whether or not the
        /// step passes. Either list may be null. <paramref name="context"/> may be null: the conditions and the strings are then not checked.
        /// </summary>
        public static bool Validate(StepDefinition step, IStepValidationContext context, IList<string> errors, IList<string> warnings)
        {
            if (step == null)
            {
                Add(errors, "$: the step is missing");
                return false;
            }

            var found = new List<string>();
            try
            {
                CheckIdentity(step, found);
                CheckTracks(step, found);
                CheckConditions(step, context, found);
                CheckMail(step, context, found, warnings);
                CheckReminders(step, context, found, warnings);
                CheckNames(step, context, warnings);
            }
            catch (Exception ex)
            {
                found.Add("$: the step could not be checked (" + ex.GetType().Name + ": " + ex.Message + ")");
            }

            if (found.Count == 0) return true;
            if (errors != null)
            {
                foreach (string message in found) errors.Add(message);
            }
            return false;
        }

        // ---- identity, tracks, kind ----

        private static void CheckIdentity(StepDefinition step, IList<string> errors)
        {
            if (!IsValidId(step.Id))
                errors.Add("id: '" + step.Id + "' is not valid (lower-case letters, digits and '_', " + MinIdLength + ".." + MaxIdLength + " characters)");

            if (!Enum.IsDefined(typeof(StepKind), step.Kind))
                errors.Add("kind: unknown kind");

            if (step.Kind == StepKind.Sequential && (step.Order < MinOrder || step.Order > MaxOrder))
                errors.Add("order: " + step.Order + " is outside " + MinOrder + ".." + MaxOrder + " (required for a sequential step)");

            if (step.StreakDays < 0 || step.StreakDays > MaxStreakDays)
                errors.Add("streakDays: " + step.StreakDays + " is outside 0.." + MaxStreakDays);

            if (!IsKnownPriority(step.Priority))
                errors.Add("priority: unknown priority '" + step.Priority + "' (expected " + string.Join(", ", Priorities) + ")");
        }

        private static void CheckTracks(StepDefinition step, IList<string> errors)
        {
            if (step.Tracks == null || step.Tracks.Length == 0)
            {
                errors.Add("tracks: at least one track is required (" + string.Join(", ", Tracks) + ")");
                return;
            }

            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < step.Tracks.Length; i++)
            {
                string track = step.Tracks[i];
                if (!IsKnownTrack(track))
                    errors.Add("tracks[" + i + "]: unknown track '" + track + "' (known: " + string.Join(", ", Tracks) + ")");
                else if (!seen.Add(track))
                    errors.Add("tracks[" + i + "]: duplicate track '" + track + "'");
            }
        }

        // ---- conditions ----

        private static void CheckConditions(StepDefinition step, IStepValidationContext context, IList<string> errors)
        {
            if (step.Kind == StepKind.Situational && step.ShowWhen == null)
                errors.Add("showWhen: a situational step needs the condition that triggers it");

            if (step.CompleteWhen != null && step.CompleteOnShow)
                errors.Add("completeWhen: cannot be used together with completeOnShow (set exactly one of them)");
            else if (step.CompleteWhen == null && !step.CompleteOnShow)
                errors.Add("completeWhen: the step needs completeWhen or completeOnShow (set exactly one of them)");

            CheckCondition(step.ShowWhen, "showWhen", context, errors);
            CheckCondition(step.CompleteWhen, "completeWhen", context, errors);

            if (step.Mail != null && step.Mail.Variants != null)
            {
                for (int i = 0; i < step.Mail.Variants.Length; i++)
                {
                    BodyVariant variant = step.Mail.Variants[i];
                    if (variant == null) continue;
                    if (variant.When == null) errors.Add("mail.variants[" + i + "].when: the condition is missing");
                    else CheckCondition(variant.When, "mail.variants[" + i + "].when", context, errors);
                }
            }
        }

        private static void CheckCondition(ConditionRef condition, string path, IStepValidationContext context, IList<string> errors)
        {
            if (condition == null) return;
            if (condition.Node == null)
            {
                errors.Add(path + ": the condition is empty");
                return;
            }
            if (context == null) return;

            var local = new List<string>();
            bool ok = context.ValidateCondition(condition.Node, local, path);
            if (!ok && local.Count == 0) local.Add(path + ": the condition is not valid");
            foreach (string message in local) errors.Add(message);
        }

        // ---- mail and strings ----

        private static void CheckMail(StepDefinition step, IStepValidationContext context, IList<string> errors, IList<string> warnings)
        {
            MailSpec mail = step.Mail;
            if (mail == null)
            {
                errors.Add("mail: the object is missing");
                return;
            }

            string[] args = mail.Args ?? new string[0];
            CheckArgs(args, "mail.args", errors);

            if (mail.Buttons != null)
            {
                for (int i = 0; i < mail.Buttons.Length; i++)
                {
                    if (!IsKnownButton(mail.Buttons[i]))
                        errors.Add("mail.buttons[" + i + "]: unknown button '" + mail.Buttons[i] + "' (known: " + string.Join(", ", Buttons) + ")");
                }

                bool hasHint = Array.IndexOf(mail.Buttons, "hint") >= 0;
                if (hasHint && (step.Reminders == null || step.Reminders.Length == 0))
                    Add(warnings, "mail.buttons: the 'hint' button has no hints to show because the step has no reminders");
            }

            bool titleOk = CheckKey(mail.TitleKey, "mail.titleKey", context, errors);
            bool bodyOk = CheckKey(mail.BodyKey, "mail.bodyKey", context, errors);

            if (titleOk) CheckText(mail.TitleKey, "mail.titleKey", args.Length, context, errors, warnings, true, false);
            if (bodyOk)
            {
                CheckText(mail.BodyKey, "mail.bodyKey", args.Length, context, errors, warnings, false, true);

                // The text of a track, "<bodyKey>.<track>", is optional but when it exists it has to fit the same placeholders.
                if (context != null && step.Tracks != null)
                {
                    foreach (string track in step.Tracks)
                    {
                        if (!IsKnownTrack(track)) continue;
                        string trackKey = mail.BodyKey + "." + track;
                        if (context.HasString(trackKey))
                            CheckText(trackKey, "mail.bodyKey." + track, args.Length, context, errors, warnings, false, true);
                    }
                }
            }

            if (mail.Variants != null)
            {
                for (int i = 0; i < mail.Variants.Length; i++)
                {
                    BodyVariant variant = mail.Variants[i];
                    if (variant == null) continue;
                    string path = "mail.variants[" + i + "].bodyKey";
                    if (CheckKey(variant.BodyKey, path, context, errors))
                        CheckText(variant.BodyKey, path, args.Length, context, errors, warnings, false, true);
                }
            }
        }

        private static void CheckReminders(StepDefinition step, IStepValidationContext context, IList<string> errors, IList<string> warnings)
        {
            if (step.Reminders == null) return;

            int previous = 0;
            for (int i = 0; i < step.Reminders.Length; i++)
            {
                ReminderSpec reminder = step.Reminders[i];
                string path = "reminders[" + i + "]";
                if (reminder == null)
                {
                    errors.Add(path + ": the reminder is missing");
                    continue;
                }

                if (reminder.AfterDays < MinAfterDays || reminder.AfterDays > MaxAfterDays)
                    errors.Add(path + ".afterDays: " + reminder.AfterDays + " is outside " + MinAfterDays + ".." + MaxAfterDays);
                else if (reminder.AfterDays <= previous)
                    errors.Add(path + ".afterDays: " + reminder.AfterDays + " must be greater than the previous reminder (" + previous + ")");
                if (reminder.AfterDays > previous) previous = reminder.AfterDays;

                string[] args = reminder.Args ?? new string[0];
                CheckArgs(args, path + ".args", errors);
                if (CheckKey(reminder.HintKey, path + ".hintKey", context, errors))
                    CheckText(reminder.HintKey, path + ".hintKey", args.Length, context, errors, warnings, false, false);
            }
        }

        private static void CheckNames(StepDefinition step, IStepValidationContext context, IList<string> warnings)
        {
            if (context == null || warnings == null || !IsValidId(step.Id)) return;

            // The short name (hub page, summary mail) and the "Already done" phrase belong to the steps of the track; a tip needs neither.
            if (step.Kind != StepKind.Sequential) return;

            if (!context.HasString(step.NameKey))
                Add(warnings, "step '" + step.Id + "': no short name, the string '" + step.NameKey + "' is missing");
            if (step.CompleteWhen != null && !context.HasString(step.DoneKey))
                Add(warnings, "step '" + step.Id + "': no phrase for \"Already done\", the string '" + step.DoneKey + "' is missing");
        }

        private static void CheckArgs(string[] args, string path, IList<string> errors)
        {
            for (int i = 0; i < args.Length; i++)
            {
                if (!IsKnownArg(args[i]))
                    errors.Add(path + "[" + i + "]: unknown placeholder '" + args[i] + "' (known: " + string.Join(", ", ArgNames) + ")");
            }
        }

        /// <summary>A required string key that exists in the tables. False (with an error) when the key is empty or unknown.</summary>
        private static bool CheckKey(string key, string path, IStepValidationContext context, IList<string> errors)
        {
            if (string.IsNullOrEmpty(key))
            {
                errors.Add(path + ": required");
                return false;
            }
            if (context != null && !context.HasString(key))
            {
                errors.Add(path + ": the string '" + key + "' does not exist");
                return false;
            }
            return true;
        }

        /// <summary>The Russian text of a key against the arguments of its mail: placeholder indexes, the mentor prefix, the length.</summary>
        private static void CheckText(string key, string path, int argCount, IStepValidationContext context,
            IList<string> errors, IList<string> warnings, bool isTitle, bool isBody)
        {
            if (context == null) return;

            string text = context.GetRussianText(key);
            if (text == null) return;

            int highest = MaxPlaceholderIndex(text);
            if (highest >= argCount)
                errors.Add(path + ": the string '" + key + "' uses {" + highest + "} but the step lists only " + argCount + " argument(s)");

            if (isTitle && !text.StartsWith(MentorPrefixRu, StringComparison.Ordinal))
                Add(warnings, path + ": the title '" + key + "' does not start with " + MentorPrefixRu);

            if (isBody && text.Length > MaxBodyLength)
                Add(warnings, path + ": the text '" + key + "' has " + text.Length + " characters, more than " + MaxBodyLength);
        }

        private static bool Contains(IReadOnlyList<string> list, string value)
        {
            if (value == null) return false;
            for (int i = 0; i < list.Count; i++)
            {
                if (string.Equals(list[i], value, StringComparison.Ordinal)) return true;
            }
            return false;
        }

        private static void Add(IList<string> list, string message)
        {
            if (list != null) list.Add(message);
        }
    }
}
