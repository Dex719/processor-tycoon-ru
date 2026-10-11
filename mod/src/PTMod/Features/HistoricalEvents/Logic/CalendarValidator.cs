using System;
using System.Collections.Generic;
using PTMod.Core;
using PTMod.Core.Logic.Ids;

namespace PTMod.Features.HistoricalEvents
{
    /// <summary>The events of the content packs as the validator sees them: does an event with this id exist? (The game side asks <c>EventRegistry</c>; a test brings a fake.)</summary>
    public interface IEventCatalog
    {
        bool Contains(string eventId);
    }

    /// <summary>
    /// A catalog that also knows whether the texts of an event are filled in (FR-14: every event of the pack has its <c>text</c> and the <c>cause</c>, the reference "how it was").
    /// A catalog that does not implement it is not asked; the validator then only checks that the events exist.
    /// </summary>
    public interface IEventTexts : IEventCatalog
    {
        /// <summary>The event has a <c>text</c> (a key or inline).</summary>
        bool HasText(string eventId);

        /// <summary>The event has a <c>cause</c> (a key or inline).</summary>
        bool HasCause(string eventId);
    }

    /// <summary>The keys of the string tables as the validator sees them: does the key have a text? (The game side asks <c>ModL10n.Has</c>.)</summary>
    public interface IL10nKeys
    {
        bool Has(string key);
    }

    /// <summary>
    /// The rules of the section "Security" of design.md for <c>calendar.json</c> and <c>params.json</c> (NFR-8): dates, ranges, identifiers, the events
    /// of the pack, the keys of the string tables, the sources, the predicates of the conditions. One entry is judged by
    /// <see cref="Validate(CalendarEntry, IEventCatalog, IL10nKeys, List{string})"/>; a broken entry is skipped by the loader and the rest of the calendar
    /// works. Never throws. The rules that need the world outside the entry are optional: a null <c>events</c> skips the existence of the events (the
    /// packs are not loaded yet), a null <c>keys</c> skips the existence of the texts.
    /// </summary>
    public static class CalendarValidator
    {
        /// <summary>Fewer valid entries than this and the feature is Unavailable (design.md, "Самопроверка на старте").</summary>
        public const int MinValidEntries = 25;

        /// <summary>The largest <c>jitterDays</c>.</summary>
        public const int MaxJitterDays = 60;

        /// <summary>The largest <c>offsetDays</c> of a stage (five years).</summary>
        public const int MaxOffsetDays = 1825;

        /// <summary>The largest <c>uniqueId</c> of a named role (the game has 13 historical companies, 0..12).</summary>
        public const int MaxUniqueId = 12;

        /// <summary>The widest window of months a share is counted over.</summary>
        public const int MaxMonths = 120;

        /// <summary>The first day an anchor or a window may name.</summary>
        public static readonly SimpleDate MinDate = new SimpleDate(1971, 1, 1);

        /// <summary>The last day an anchor or a window may name.</summary>
        public static readonly SimpleDate MaxDate = new SimpleDate(2030, 12, 31);

        private const string HttpsPrefix = "https://";
        /// <summary>The longest flag name (<c>skipIfFlag</c> of a stage, <c>flag</c> of the effect <c>HistFlag</c>).</summary>
        public const int MaxFlagLength = 40;

        /// <summary>The predicates a calendar may use come from the framework (no dot), the engine (<c>es.</c>) or the feature itself (<c>hist.</c>).</summary>
        private static readonly string[] AllowedPredicateOwners = { ModIds.CoreFeatureId, HistIds.EventSystem, HistIds.Feature };

        /// <summary>Enough valid entries to run the feature.</summary>
        public static bool IsUsable(int validCount)
        {
            return validCount >= MinValidEntries;
        }

        /// <summary>Judges an entry; the messages (errors, and warnings if there are no errors) go to <paramref name="errors"/>, which is what <c>ModContent.LoadRecords</c> wants.</summary>
        public static bool Validate(CalendarEntry e, IEventCatalog events, IL10nKeys keys, List<string> errors)
        {
            return Validate(e, events, keys, errors, errors);
        }

        /// <summary>Judges an entry. True when there is no error; warnings (a text key that has no text) do not reject the entry.</summary>
        public static bool Validate(CalendarEntry e, IEventCatalog events, IL10nKeys keys, List<string> errors, List<string> warnings)
        {
            if (e == null)
            {
                if (errors != null) errors.Add("entry (null): there is no entry to validate");
                return false;
            }

            var c = new Check(e, errors, warnings);
            try
            {
                CheckIdentity(e, c);
                CheckDates(e, c);
                var roleNames = new HashSet<string>(StringComparer.Ordinal);
                CheckRoles(e, c, roleNames);
                CheckConditions(e, c, roleNames);
                CheckStages(e, events, c, roleNames);
                CheckFallback(e, events, c, roleNames);
                CheckHistory(e, keys, c);
            }
            catch (Exception ex)
            {
                c.Error(string.Empty, "the validator failed: " + ex.GetType().Name + ": " + ex.Message);
            }
            return c.Errors == 0;
        }

        /// <summary>
        /// Judges a whole calendar: the entries that pass, in the order given. An entry whose id was seen before is dropped (the first one stays);
        /// <c>ModContent</c> does the same for the files, this is the second line of defence for a calendar built another way.
        /// </summary>
        public static List<CalendarEntry> ValidateAll(IEnumerable<CalendarEntry> entries, IEventCatalog events, IL10nKeys keys, List<string> errors)
        {
            var valid = new List<CalendarEntry>();
            if (entries == null) return valid;

            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (CalendarEntry entry in entries)
            {
                if (entry != null && entry.Id != null && !seen.Add(entry.Id))
                {
                    if (errors != null) errors.Add("entry " + entry.Id + ": duplicate id; the first entry with this id is kept");
                    continue;
                }

                if (Validate(entry, events, keys, errors, errors)) valid.Add(entry);
            }
            return valid;
        }

        /// <summary>
        /// Checks the events of the pack that the entry fires, once the pack is loaded (the second phase of the existence rule, for a calendar that was
        /// validated before the packs were read). The messages are errors.
        /// </summary>
        public static bool ValidateEventsExist(CalendarEntry e, IEventCatalog events, List<string> errors)
        {
            if (e == null || events == null) return true;

            var c = new Check(e, errors, errors);
            for (int s = 0; s < e.Stages.Count; s++)
            {
                StageSpec stage = e.Stages[s];
                if (stage == null || stage.Fires == null) continue;
                for (int f = 0; f < stage.Fires.Count; f++) CheckEventExists(stage.Fires[f], "stages[" + s + "].fires[" + f + "]", events, c);
            }
            if (e.FallbackFires != null)
            {
                for (int f = 0; f < e.FallbackFires.Count; f++) CheckEventExists(e.FallbackFires[f], "fallbackFires[" + f + "]", events, c);
            }
            return c.Errors == 0;
        }

        /// <summary>The block <c>acquisitionAntitrust</c> and the other numbers of <c>params.json</c> (design.md, "Security"). The messages are errors.</summary>
        public static bool ValidateParams(HistContentParams p, List<string> errors)
        {
            if (p == null)
            {
                if (errors != null) errors.Add("params.json: there are no parameters to validate");
                return false;
            }

            int before = errors == null ? 0 : errors.Count;
            int problems = 0;
            Action<string> fail = message =>
            {
                problems++;
                if (errors != null) errors.Add("params.json " + message);
            };

            if (p.MajorSpacingDays < 0 || p.MajorSpacingDays > 365) fail("majorSpacingDays: " + p.MajorSpacingDays + " is outside 0..365");
            if (p.ShareCacheMonths < 1 || p.ShareCacheMonths > MaxMonths) fail("shareCacheMonths: " + p.ShareCacheMonths + " is outside 1.." + MaxMonths);
            if (p.AiChooserPriority < -1000 || p.AiChooserPriority > 1000) fail("aiChooserPriority: " + p.AiChooserPriority + " is outside -1000..1000");

            AcquisitionAntitrustParams a = p.AcquisitionAntitrust;
            if (a == null)
            {
                fail("acquisitionAntitrust: the block is missing");
            }
            else
            {
                if (!InRange(a.SoftShare, 0, 1)) fail("acquisitionAntitrust.softShare: " + Show(a.SoftShare) + " is outside 0..1");
                if (!InRange(a.Slope, 0, 10)) fail("acquisitionAntitrust.slope: " + Show(a.Slope) + " is outside 0..10");
                if (!InRange(a.CaseSurcharge, 1, 3)) fail("acquisitionAntitrust.caseSurcharge: " + Show(a.CaseSurcharge) + " is outside 1..3");
                if (!InRange(a.MaxMultiplier, 1, 3)) fail("acquisitionAntitrust.maxMultiplier: " + Show(a.MaxMultiplier) + " is outside 1..3");
                if (a.ShareMonths < 1 || a.ShareMonths > MaxMonths) fail("acquisitionAntitrust.shareMonths: " + a.ShareMonths + " is outside 1.." + MaxMonths);
            }

            return problems == 0 && (errors == null || errors.Count == before);
        }

        // ---- the rules ----

        private static void CheckIdentity(CalendarEntry e, Check c)
        {
            if (string.IsNullOrEmpty(e.Id))
            {
                c.Error("id", "is required");
            }
            else if (!ModIds.IsEventIdAllowed(HistIds.Feature, e.Id))
            {
                c.Error("id", ModIds.Explain(IdKind.EventId, HistIds.Feature, e.Id));
            }

            if (string.IsNullOrEmpty(e.Category))
            {
                c.Error("category", "is required");
            }
            else if (!HistCategories.IsKnown(e.Category))
            {
                c.Error("category", "\"" + e.Category + "\" is not a category (" + string.Join(", ", HistCategories.All) + ")");
            }
        }

        private static void CheckDates(CalendarEntry e, Check c)
        {
            bool anchorOk = CheckDate(e.Anchor, "anchor", c);
            bool fromOk = CheckDate(e.WindowFrom, "window.from", c);
            bool toOk = CheckDate(e.WindowTo, "window.to", c);

            if (fromOk && toOk && e.WindowFrom > e.WindowTo) c.Error("window", "from " + e.WindowFrom + " is after to " + e.WindowTo);
            if (anchorOk && fromOk && e.Anchor < e.WindowFrom) c.Error("anchor", e.Anchor + " is before the window starts (" + e.WindowFrom + ")");
            if (anchorOk && toOk && e.Anchor > e.WindowTo) c.Error("anchor", e.Anchor + " is after the window ends (" + e.WindowTo + ")");

            if (e.JitterDays < 0 || e.JitterDays > MaxJitterDays) c.Error("jitterDays", e.JitterDays + " is outside 0.." + MaxJitterDays);
        }

        private static bool CheckDate(SimpleDate date, string path, Check c)
        {
            if (!date.IsSet)
            {
                c.Error(path, "is required (a date as yyyy-MM-dd)");
                return false;
            }

            if (date < MinDate || date > MaxDate)
            {
                c.Error(path, date + " is outside " + MinDate + " .. " + MaxDate);
                return false;
            }
            return true;
        }

        private static void CheckRoles(CalendarEntry e, Check c, HashSet<string> roleNames)
        {
            if (e.Roles == null)
            {
                c.Error("roles", "the list is missing");
                return;
            }

            for (int i = 0; i < e.Roles.Count; i++)
            {
                RoleSpec role = e.Roles[i];
                string path = "roles[" + i + "]";
                if (role == null)
                {
                    c.Error(path, "the role is missing");
                    continue;
                }

                if (!string.IsNullOrEmpty(role.Name))
                {
                    if (RoleKinds.IsImplicitRole(role.Name)) c.Error(path + ".name", "\"" + role.Name + "\" is a built-in role name and cannot be declared");
                    else if (!roleNames.Add(role.Name)) c.Error(path + ".name", "\"" + role.Name + "\" is declared twice");
                }

                CheckRole(role, path, 0, c);
            }
        }

        private static void CheckRole(RoleSpec role, string path, int depth, Check c)
        {
            if (string.IsNullOrEmpty(role.Name)) c.Error(path + ".name", "is required");

            if (string.IsNullOrEmpty(role.Kind))
            {
                c.Error(path + ".kind", "is required");
            }
            else if (!RoleKinds.IsKnown(role.Kind))
            {
                c.Error(path + ".kind", "\"" + role.Kind + "\" is not a kind of role (" + string.Join(", ", RoleKinds.All) + ")");
            }

            if (role.Segments == null || role.Segments.Length == 0)
            {
                c.Error(path + ".segments", "needs at least one segment (all, highend, midrange, lowend, industries)");
            }
            else if (role.Segments.Length > 1 && Array.IndexOf(role.Segments, Segment.Any) >= 0)
            {
                c.Error(path + ".segments", "\"all\" cannot be combined with other segments");
            }

            if (float.IsNaN(role.MinShare) || float.IsInfinity(role.MinShare) || role.MinShare < 0 || role.MinShare > 1)
                c.Error(path + ".minShare", Show(role.MinShare) + " is outside 0..1");

            if (role.Months < 1 || role.Months > MaxMonths) c.Error(path + ".months", role.Months + " is outside 1.." + MaxMonths);

            if (role.Kind == RoleKinds.Named && role.UniqueId == -1) c.Error(path + ".uniqueId", "a named role needs the uniqueId of its company");
            else if (role.UniqueId != -1 && (role.UniqueId < 0 || role.UniqueId > MaxUniqueId)) c.Error(path + ".uniqueId", role.UniqueId + " is outside 0.." + MaxUniqueId);

            if (role.Fallback == null) return;
            if (depth >= CalendarEntry.MaxRoleDepth)
            {
                if (role.Fallback.Count > 0) c.Error(path + ".fallback", "fallback roles are nested deeper than " + CalendarEntry.MaxRoleDepth + " levels");
                return;
            }

            for (int i = 0; i < role.Fallback.Count; i++)
            {
                RoleSpec inner = role.Fallback[i];
                string innerPath = path + ".fallback[" + i + "]";
                if (inner == null) c.Error(innerPath, "the role is missing");
                else CheckRole(inner, innerPath, depth + 1, c);
            }
        }

        private static void CheckConditions(CalendarEntry e, Check c, HashSet<string> roleNames)
        {
            if (e.Conditions == null)
            {
                c.Error("conditions", "the list is missing");
                return;
            }

            for (int i = 0; i < e.Conditions.Count; i++)
            {
                if (e.Conditions[i] == null) c.Error("conditions[" + i + "]", "the condition is missing");
            }
            CheckConditionUse(e.ConditionUse, "conditions", c, roleNames);
        }

        private static void CheckConditionUse(ConditionUse use, string path, Check c, HashSet<string> roleNames)
        {
            if (use == null) return;

            foreach (string predicate in use.Predicates)
            {
                string owner = ModIds.FeatureOfId(IdKind.PredicateName, predicate);
                if (owner == null)
                {
                    c.Error(path, "predicate \"" + predicate + "\" has an unknown prefix; use hist.<name>, es.<name> or a predicate of the framework");
                }
                else if (Array.IndexOf(AllowedPredicateOwners, owner) < 0)
                {
                    c.Error(path, "predicate \"" + predicate + "\" belongs to the feature " + owner + "; a calendar may use hist.<name>, es.<name> and the predicates of the framework");
                }
            }

            foreach (string role in use.Roles)
            {
                if (!roleNames.Contains(role) && !RoleKinds.IsImplicitRole(role)) c.Error(path, "refers to the role \"" + role + "\" that the entry does not declare");
            }
        }

        private static void CheckStages(CalendarEntry e, IEventCatalog events, Check c, HashSet<string> roleNames)
        {
            if (e.Stages == null || e.Stages.Count == 0)
            {
                c.Error("stages", "needs at least one stage (stage 0 plays at the firing)");
                return;
            }

            int previous = 0;
            for (int s = 0; s < e.Stages.Count; s++)
            {
                StageSpec stage = e.Stages[s];
                string path = "stages[" + s + "]";
                if (stage == null)
                {
                    c.Error(path, "the stage is missing");
                    continue;
                }

                if (stage.OffsetDays < 0 || stage.OffsetDays > MaxOffsetDays)
                    c.Error(path + ".offsetDays", stage.OffsetDays + " is outside 0.." + MaxOffsetDays);
                else if (s == 0 && stage.OffsetDays != 0)
                    c.Error(path + ".offsetDays", "stage 0 plays at the firing and must have offsetDays 0, got " + stage.OffsetDays);
                else if (stage.OffsetDays < previous)
                    c.Error(path + ".offsetDays", stage.OffsetDays + " is before the previous stage (" + previous + "); stages go in order");
                if (stage.OffsetDays >= 0) previous = Math.Max(previous, stage.OffsetDays);

                if (stage.SkipIfFlag != null && !IsFlagName(stage.SkipIfFlag))
                    c.Error(path + ".skipIfFlag", "\"" + stage.SkipIfFlag + "\" is not a flag name (letters, digits, '.', '_' and '-', up to " + MaxFlagLength + ")");

                if (stage.Fires == null || stage.Fires.Count == 0)
                {
                    c.Error(path + ".fires", "a stage needs at least one fire");
                    continue;
                }

                for (int f = 0; f < stage.Fires.Count; f++) CheckFire(stage.Fires[f], path + ".fires[" + f + "]", events, c, roleNames);
            }
        }

        private static void CheckFallback(CalendarEntry e, IEventCatalog events, Check c, HashSet<string> roleNames)
        {
            if (string.IsNullOrEmpty(e.Fallback))
                c.Error("fallback", "is required (" + FallbackModes.News + " or " + FallbackModes.Skip + ")");
            else if (!FallbackModes.IsKnown(e.Fallback))
                c.Error("fallback", "\"" + e.Fallback + "\" is not " + FallbackModes.News + " or " + FallbackModes.Skip);

            if (e.FallbackFires == null) return;
            for (int f = 0; f < e.FallbackFires.Count; f++) CheckFire(e.FallbackFires[f], "fallbackFires[" + f + "]", events, c, roleNames);
        }

        private static void CheckFire(FireSpec fire, string path, IEventCatalog events, Check c, HashSet<string> roleNames)
        {
            if (fire == null)
            {
                c.Error(path, "the fire is missing");
                return;
            }

            if (string.IsNullOrEmpty(fire.EventId))
            {
                c.Error(path + ".eventId", "is required");
            }
            else
            {
                if (!ModIds.IsEventIdAllowed(HistIds.Feature, fire.EventId)) c.Error(path + ".eventId", ModIds.Explain(IdKind.EventId, HistIds.Feature, fire.EventId));
                CheckEventExists(fire, path, events, c);
            }

            if (string.IsNullOrEmpty(fire.Role))
                c.Error(path + ".role", "is required");
            else if (!roleNames.Contains(fire.Role) && !RoleKinds.IsImplicitRole(fire.Role))
                c.Error(path + ".role", "\"" + fire.Role + "\" is not a role of the entry (declared: " + (roleNames.Count == 0 ? "none" : string.Join(", ", roleNames)) + "; built-in: global, player, allCpuCompanies, allManufacturers, allFabless)");

            if (fire.ExtraArgs == null) c.Error(path + ".extraArgs", "the list is missing");
            else
            {
                if (fire.ExtraArgs.Length > HistNotes.MaxExtraArgs)
                    c.Error(path + ".extraArgs", "has " + fire.ExtraArgs.Length + " arguments, at most " + HistNotes.MaxExtraArgs + " (the next positions hold the source line and the reference of the event)");

                for (int i = 0; i < fire.ExtraArgs.Length; i++)
                {
                    if (fire.ExtraArgs[i] == null) c.Error(path + ".extraArgs[" + i + "]", "the argument is missing");
                }
            }

            CheckConditionUse(fire.OnlyIfUse, path + ".onlyIf", c, roleNames);
        }

        private static void CheckEventExists(FireSpec fire, string path, IEventCatalog events, Check c)
        {
            if (fire == null || events == null || string.IsNullOrEmpty(fire.EventId)) return;
            if (!events.Contains(fire.EventId))
            {
                c.Error(path + ".eventId", "the event \"" + fire.EventId + "\" is not in the pack (events.json)");
                return;
            }

            // FR-14: the letter of the event has its text and the reference "how it was". A missing one is a gap in the content, not a reason to drop the entry.
            var texts = events as IEventTexts;
            if (texts == null) return;
            if (!texts.HasText(fire.EventId)) c.Warning(path + ".eventId", "the event \"" + fire.EventId + "\" has no text (events.json)");
            if (!texts.HasCause(fire.EventId)) c.Warning(path + ".eventId", "the event \"" + fire.EventId + "\" has no cause, the reference \"how it was\" (events.json)");
        }

        private static void CheckHistory(CalendarEntry e, IL10nKeys keys, Check c)
        {
            HistoryInfo history = e.History;
            if (history == null)
            {
                c.Error("history", "is required: titleKey, factKey and at least one source");
                c.Error("missing sources", "history.sources needs at least one https:// URL");
                return;
            }

            CheckTextKey(history.TitleKey, "history.titleKey", keys, c);
            CheckTextKey(history.FactKey, "history.factKey", keys, c);

            int good = 0;
            if (history.Sources != null)
            {
                for (int i = 0; i < history.Sources.Count; i++)
                {
                    string url = history.Sources[i];
                    if (IsHttpsUrl(url)) good++;
                    else c.Error("history.sources[" + i + "]", "\"" + url + "\" is not an https:// URL");
                }
            }
            if (good == 0) c.Error("missing sources", "history.sources needs at least one https:// URL");
        }

        private static void CheckTextKey(string key, string path, IL10nKeys keys, Check c)
        {
            if (string.IsNullOrEmpty(key))
            {
                c.Error(path, "is required");
                return;
            }

            if (!ModIds.IsL10nKeyAllowed(HistIds.Feature, key))
            {
                c.Error(path, ModIds.Explain(IdKind.L10nKey, HistIds.Feature, key));
                return;
            }

            if (keys != null && !keys.Has(key)) c.Warning(path, "the key \"" + key + "\" has no text in the string tables");
        }

        // ---- small checks ----

        private static bool IsHttpsUrl(string url)
        {
            if (string.IsNullOrEmpty(url) || url.Length > 500 || !url.StartsWith(HttpsPrefix, StringComparison.Ordinal)) return false;
            if (url.Length == HttpsPrefix.Length) return false;
            foreach (char ch in url)
            {
                if (ch <= ' ' || ch == '"' || ch == '<' || ch == '>') return false;
            }
            return true;
        }

        /// <summary>A flag name: letters, digits, '.', '_' and '-', at most <see cref="MaxFlagLength"/> characters.</summary>
        public static bool IsFlagName(string flag)
        {
            if (string.IsNullOrEmpty(flag) || flag.Length > MaxFlagLength) return false;
            foreach (char ch in flag)
            {
                bool ok = (ch >= 'a' && ch <= 'z') || (ch >= 'A' && ch <= 'Z') || (ch >= '0' && ch <= '9') || ch == '.' || ch == '_' || ch == '-';
                if (!ok) return false;
            }
            return true;
        }

        private static bool InRange(double value, double min, double max)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value) && value >= min && value <= max;
        }

        private static string Show(double value)
        {
            return value.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
        }

        /// <summary>The messages of one run: counts the errors and prefixes every line with the entry.</summary>
        private sealed class Check
        {
            private readonly string _subject;
            private readonly List<string> _errors;
            private readonly List<string> _warnings;

            public Check(CalendarEntry entry, List<string> errors, List<string> warnings)
            {
                _subject = "entry " + (string.IsNullOrEmpty(entry.Id) ? "(no id)" : entry.Id);
                _errors = errors;
                _warnings = warnings;
            }

            public int Errors { get; private set; }

            public void Error(string path, string message)
            {
                Errors++;
                if (_errors != null) _errors.Add(Line(path, message));
            }

            public void Warning(string path, string message)
            {
                if (_warnings != null) _warnings.Add(Line(path, message));
            }

            private string Line(string path, string message)
            {
                // "entry hist.x missing sources: ..." reads like the line of AC-1.2; every other rule names its member: "entry hist.x anchor: ...".
                string where = string.IsNullOrEmpty(path) ? string.Empty : " " + path;
                return _subject + where + ": " + message;
            }
        }
    }
}
