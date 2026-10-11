using System;
using System.Collections.Generic;
using PTMod.Core;
using PTMod.Core.Logic.Json;
using PTMod.Features.EventSystem;

namespace PTMod.Features.HistoricalEvents
{
    /// <summary>
    /// The calendar as the feature loads it (FR-1, NFR-8): <c>calendar.json</c> through <c>ModContent.LoadRecords</c> (the built-in file with the
    /// player's override on top, a broken record skipped with its reasons in the log) and <c>params.json</c> through <c>ModContent.LoadConfig</c>.
    /// The thin game side of <see cref="CalendarEntry"/>, <see cref="CalendarValidator"/> and <see cref="HistContentParams"/>: the parsing and the
    /// rules are theirs. Conditions are read by <c>ModConditions.Parse</c>, so the predicates of the feature (<c>hist.*</c>) must be registered before
    /// the calendar is loaded.
    /// </summary>
    internal static class CalendarContent
    {
        /// <summary>
        /// Loads and validates the calendar. <paramref name="events"/> is the catalog of the loaded packs, or null when they are not loaded yet (then the
        /// existence of the events is not checked). The records that pass are returned in the order of the file; the others were reported by
        /// <c>ModContent</c> (one line per problem under the feature's id, <c>ModContent.ReportOf</c> keeps them).
        /// </summary>
        public static IReadOnlyList<CalendarEntry> LoadCalendar(IEventCatalog events)
        {
            IReadOnlyList<CalendarEntry> records = ModContent.LoadRecords<CalendarEntry>(
                HistIds.Feature, HistIds.CalendarFile,
                CalendarEntry.Mapper(ModConditions.Parse),
                (entry, messages) => CalendarValidator.Validate(entry, events, TextKeys.Instance, messages),
                HistIds.ContentSchema);

            ModLog.Debug(HistIds.Feature, "content: " + records.Count + " valid calendar entries");
            return records;
        }

        /// <summary>
        /// The numbers of <c>params.json</c>. A broken value, or a value outside its range, is a line in the log and gives the default of the code;
        /// the file as a whole is never a reason to fail.
        /// </summary>
        public static HistContentParams LoadParams()
        {
            try
            {
                JsonObject config = ModContent.LoadConfig(HistIds.Feature, HistIds.ParamsFile, HistIds.ContentSchema);
                var problems = new List<string>();
                HistContentParams result = HistContentParams.FromJson(config, problems);
                foreach (string problem in problems) ModLog.Warn(HistIds.Feature, "content: " + problem);

                var errors = new List<string>();
                if (!CalendarValidator.ValidateParams(result, errors))
                {
                    foreach (string error in errors) ModLog.Error(HistIds.Feature, "content: " + error);
                    ModLog.Error(HistIds.Feature, "content: params.json is out of range; the defaults of the code are used");
                    return HistContentParams.Defaults();
                }
                return result;
            }
            catch (Exception ex)
            {
                ModLog.Error(HistIds.Feature, "content: params.json could not be read; the defaults of the code are used", ex);
                return HistContentParams.Defaults();
            }
        }

        /// <summary>
        /// The events of the loaded packs, or null while they are not loaded (the engine reads the packs on the first <c>GameReady</c> of the session; an
        /// empty list means it has not yet, or the engine is off).
        /// </summary>
        public static IEventCatalog LoadedPacks()
        {
            IList<EventDefinition> all = EventRegistry.All;
            return all == null || all.Count == 0 ? null : new PackEvents(all);
        }

        /// <summary>The ids of a snapshot of the engine's definitions, and which of them have a text and a cause (FR-14).</summary>
        private sealed class PackEvents : IEventTexts
        {
            private readonly HashSet<string> _ids = new HashSet<string>(StringComparer.Ordinal);
            private readonly HashSet<string> _withText = new HashSet<string>(StringComparer.Ordinal);
            private readonly HashSet<string> _withCause = new HashSet<string>(StringComparer.Ordinal);

            public PackEvents(IList<EventDefinition> definitions)
            {
                foreach (EventDefinition definition in definitions)
                {
                    if (definition == null || string.IsNullOrEmpty(definition.id)) continue;

                    _ids.Add(definition.id);
                    if (IsFilled(definition.text)) _withText.Add(definition.id);
                    if (IsFilled(definition.cause)) _withCause.Add(definition.id);
                }
            }

            public bool Contains(string eventId)
            {
                return eventId != null && _ids.Contains(eventId);
            }

            public bool HasText(string eventId)
            {
                return eventId != null && _withText.Contains(eventId);
            }

            public bool HasCause(string eventId)
            {
                return eventId != null && _withCause.Contains(eventId);
            }

            private static bool IsFilled(LocText text)
            {
                return text != null && (!string.IsNullOrEmpty(text.Key) || !string.IsNullOrEmpty(text.Ru) || !string.IsNullOrEmpty(text.En));
            }
        }

        /// <summary>The string tables of <c>ModL10n</c>.</summary>
        private sealed class TextKeys : IL10nKeys
        {
            public static readonly TextKeys Instance = new TextKeys();

            public bool Has(string key)
            {
                return ModL10n.Has(key);
            }
        }
    }
}
