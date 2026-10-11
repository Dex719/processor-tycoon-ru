using System.Collections.Generic;

namespace PTMod.Features.EventSystem
{
    /// <summary>The journal of events: what fired, what was chosen, how effects ended or failed. Kept in the save, limited to the latest entries.</summary>
    public static class EventLog
    {
        /// <summary>The entries, oldest first (a view to read, not to change). Empty while the journal is empty or the event system has not started.</summary>
        public static IList<EventLogEntry> Entries
        {
            get
            {
                IList<EventLogEntry> entries = EventSystemApi.Query(EventSystemSite.Entries, api => api.GetLogEntries(), (IList<EventLogEntry>)null);
                return entries ?? new EventLogEntry[0];
            }
        }

        /// <summary>Opens the "Event journal" window; does nothing when the event system is off.</summary>
        public static void OpenWindow()
        {
            EventSystemApi.Run(EventSystemSite.OpenWindow, api => { api.OpenLogWindow(); return true; }, false);
        }
    }
}
