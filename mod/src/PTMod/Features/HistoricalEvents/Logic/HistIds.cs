namespace PTMod.Features.HistoricalEvents
{
    /// <summary>
    /// Names that the pure logic and the game side of historical-events share (the slug of the feature, the pack, the content files).
    /// Internal: the logic of this folder must not depend on the entry class <c>HistoricalEventsFeature</c>, which needs the game.
    /// </summary>
    internal static class HistIds
    {
        /// <summary>The slug of the feature: its namespace, content folder, string tables, settings and save section.</summary>
        public const string Feature = "historical-events";

        /// <summary>The hard dependency: the engine that applies the effects, shows the mails and keeps the log.</summary>
        public const string EventSystem = "event-system";

        /// <summary>The id of the content pack registered with the event system (the same as the feature).</summary>
        public const string PackId = "historical-events";

        /// <summary>The folder of the pack in <c>Content/</c> (a leading <c>content/</c> is allowed by <c>EventRegistry.RegisterPack</c>).</summary>
        public const string PackFolder = "content/historical-events";

        /// <summary>The calendar: the records of <see cref="CalendarEntry"/>.</summary>
        public const string CalendarFile = "calendar.json";

        /// <summary>The numbers of the feature: <see cref="HistContentParams"/>.</summary>
        public const string ParamsFile = "params.json";

        /// <summary>The <c>schema</c> of the content files this version reads (the format of the file, not of the save section).</summary>
        public const int ContentSchema = 1;

        /// <summary>The section of the side save file (the same as the feature id).</summary>
        public const string SaveSection = Feature;

        /// <summary>The prefix of every identifier of the feature (events, string keys, predicates).</summary>
        public const string Prefix = "hist.";

        /// <summary>The reason shown by <c>ModModifiers.Explain</c> for the entries the catch-up of a late start makes (FR-4 item 3).</summary>
        public const string CatchUpReasonKey = "hist.catchup";

        /// <summary>The tag of the letter "State of the industry" (<c>hist:&lt;...&gt;</c>, FR-25).</summary>
        public const string CatchUpMailTag = "hist:catchup";
    }
}
