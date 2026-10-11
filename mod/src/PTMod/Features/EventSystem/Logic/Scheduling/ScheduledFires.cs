using System;
using System.Collections.Generic;

namespace PTMod.Features.EventSystem
{
    /// <summary>
    /// The delayed firings of chains (<c>FireEvent</c>, D9): the effect never fires the next event at once, it puts it into <c>scheduled</c> with
    /// <c>dueDay = today + delay</c>, the target and the depth of the chain, and the scheduler fires it in the step "delayed firings" of that day (FR-2 item 1)
    /// with the conditions of the event skipped (the parent has decided). A delay of 0 is due the next processed day. The flags of chains go to the history.
    /// </summary>
    public sealed class ScheduledFires : IChainHost
    {
        private readonly SchedulerState _state;
        private readonly EventHistory _history;

        public ScheduledFires(SchedulerState state, EventHistory history)
        {
            if (state == null) throw new ArgumentNullException("state");
            if (history == null) throw new ArgumentNullException("history");
            _state = state;
            _history = history;
        }

        /// <summary>The firings waiting for their day.</summary>
        public IList<ScheduledFire> Pending
        {
            get { return _state.Scheduled.AsReadOnly(); }
        }

        public void Schedule(string eventId, int dueDay, string companyKey, int parentId, string[] args, int depth)
        {
            if (string.IsNullOrEmpty(eventId)) throw new ArgumentException("no event id", "eventId");
            _state.Scheduled.Add(new ScheduledFire
            {
                eventId = eventId,
                dueDay = dueDay,
                companyKey = string.IsNullOrEmpty(companyKey) ? null : companyKey,
                parentId = Math.Max(0, parentId),
                args = Copy(args),
                ignoreConditions = true,
                depth = Math.Max(0, depth),
            });
        }

        public void SetFlag(string flag, string companyKey, int day)
        {
            _history.SetFlag(flag, companyKey, day);
        }

        private static string[] Copy(string[] args)
        {
            if (args == null) return new string[0];
            var copy = new string[args.Length];
            for (int i = 0; i < args.Length; i++) copy[i] = args[i] ?? string.Empty;
            return copy;
        }
    }
}
