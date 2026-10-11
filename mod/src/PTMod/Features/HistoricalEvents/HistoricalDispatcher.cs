using PTMod.Core;
using PTMod.Features.EventSystem;

namespace PTMod.Features.HistoricalEvents
{
    /// <summary>
    /// The door from the calendar to <c>EventBus.Fire</c> (design.md, Component 6; TSK-008): <c>EventBus.Fire(eventId, new EventFireContext { CompanyKey, Args,
    /// IgnoreConditions = true })</c>. The calendar has already decided that the event happens, so the conditions of the event's definition are skipped; the
    /// engine applies the effects, the intensity (<c>PackOptions.Severity</c>), the letters and the windows.
    /// </summary>
    internal sealed class EventBusFirer : IDelayedEventFirer
    {
        public FireResult Fire(string eventId, string companyKey, string[] args)
        {
            return Map(EventBus.Fire(eventId, new EventFireContext
            {
                CompanyKey = companyKey,
                Args = WithNotes(eventId, args),
                IgnoreConditions = true,
            }));
        }

        /// <summary>The same firing a few days later (<c>EventFireContext.DelayDays</c>): the answer is then <see cref="FireResult.Scheduled"/>.</summary>
        public FireResult FireLater(string eventId, string companyKey, int delayDays, params string[] args)
        {
            return Map(EventBus.Fire(eventId, new EventFireContext
            {
                CompanyKey = companyKey,
                Args = WithNotes(eventId, args),
                IgnoreConditions = true,
                DelayDays = delayDays,
            }));
        }

        /// <summary>
        /// The arguments with the two notes of FR-14 in their slots (<see cref="HistNotes"/>): the line with the source that ends the text of the event, and the "how it was"
        /// reference, empty while <c>historyNotes</c> is off. Every firing of the feature goes through this one door, so all the events of the pack get them.
        /// </summary>
        internal static string[] WithNotes(string eventId, string[] args)
        {
            return HistNotes.Decorate(eventId, args, HistoryOf, HistSettings.HistoryNotes, (key, values) => ModL10n.L(key, (object[])values));
        }

        /// <summary>The history of a calendar entry by its id, or of a news event of the game's own events by the id of the event; null for any other id.</summary>
        internal static HistoryInfo HistoryOf(string id)
        {
            HistoryInfo news = BuiltInNews.Find(id);
            if (news != null) return news;

            HistoricalEventsFeature feature = HistoricalEventsFeature.Instance;
            if (feature == null || string.IsNullOrEmpty(id)) return null;

            foreach (CalendarEntry entry in feature.Calendar)
            {
                if (entry != null && string.Equals(entry.Id, id, System.StringComparison.Ordinal)) return entry.History;
            }
            return null;
        }

        private static FireResult Map(EventFireResult result)
        {
            switch (result)
            {
                case EventFireResult.Fired: return FireResult.Fired;
                case EventFireResult.Scheduled: return FireResult.Scheduled;
                case EventFireResult.UnknownEvent: return FireResult.UnknownEvent;
                case EventFireResult.Disabled: return FireResult.Disabled;
                default: return FireResult.Skipped;
            }
        }
    }

    /// <summary>
    /// Fires the events of the stages of an entry (design.md, Component 6; FR-2 item 1, FR-10): the game side of <see cref="DispatcherCore"/>, which does the
    /// work (who is fired at, the <c>onlyIf</c> per company, the arguments of the table, the answers). Here only the wiring: the engine's bus, the texts of
    /// <c>ModL10n</c> (the title of an entry in the news that it did not happen) and the log. Every call is guarded: an exception in the bus is a failed
    /// firing, never an exception in the scheduler.
    /// </summary>
    internal sealed class HistoricalDispatcher
    {
        private readonly DispatcherCore _core;

        internal HistoricalDispatcher(HistRuntime runtime)
        {
            _core = new DispatcherCore(
                new EventBusFirer(),
                runtime.World,
                runtime.Shares,
                runtime.Evaluator,
                runtime.State,
                key => ModL10n.L(key),
                message => ModLog.Warn(HistIds.Feature, "dispatcher: " + message));
        }

        /// <summary>Fires stage <paramref name="stageIndex"/> of the entry for the companies of its roles.</summary>
        internal FireReport FireStage(CalendarEntry entry, int stageIndex, RoleResult roles)
        {
            FireReport report = _core.FireStage(entry, stageIndex, roles);
            ModLog.Debug(HistIds.Feature, "dispatcher: " + entry.Id + " stage " + stageIndex + ": " + report.Fired + " fired, " + report.Skipped + " skipped, " + report.Dropped + " dropped");
            return report;
        }

        /// <summary>The window of the entry ended: its fallback fires, or the news <c>hist.fizzle</c> that it did not happen in this game.</summary>
        internal FireReport FireFallbackNews(CalendarEntry entry, RoleResult roles)
        {
            return _core.FireFallback(entry, roles);
        }
    }
}
