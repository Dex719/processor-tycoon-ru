using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using PTMod.Core.Logic.Companies;

namespace PTMod.Features.EventSystem
{
    /// <summary>
    /// The journal of events behind <c>EventLog.Entries</c> (design.md "Журнал"): what fired, what was chosen, how effects ended or failed, with the numbers
    /// of the <c>AppliedEffect</c> (D1, NFR-5). Kept in the section of the engine and cut to the newest <c>logLimit</c> entries (400 by default, AC-8.2). The
    /// window of the journal reads it. Main thread only.
    /// </summary>
    public sealed class EventLogStore
    {
        private static readonly string[] Kinds =
        {
            EventLogKinds.Fired, EventLogKinds.Resolved, EventLogKinds.AutoResolved, EventLogKinds.Cancelled, EventLogKinds.EffectEnded, EventLogKinds.EffectFailed,
        };

        private readonly List<EventLogEntry> _entries = new List<EventLogEntry>();
        private readonly Func<int> _limit;

        /// <param name="limit">The number of entries kept (<c>logLimit</c> of the packs), read at every addition; below 1 means the default 400.</param>
        public EventLogStore(Func<int> limit = null)
        {
            _limit = limit ?? (() => EventConfig.DefaultLogLimit);
        }

        public int Count
        {
            get { return _entries.Count; }
        }

        /// <summary>The entries, oldest first (a read-only copy).</summary>
        public IList<EventLogEntry> Entries
        {
            get { return new ReadOnlyCollection<EventLogEntry>(new List<EventLogEntry>(_entries)); }
        }

        /// <summary>Called with every entry added (the window of the journal redraws).</summary>
        public event Action<EventLogEntry> Added;

        /// <summary>Adds an entry and drops the oldest beyond the limit.</summary>
        public void Add(EventLogEntry entry)
        {
            if (entry == null) return;
            if (entry.effects == null) entry.effects = new List<EffectRecord>();
            _entries.Add(entry);
            Trim();

            Action<EventLogEntry> added = Added;
            if (added != null) added(entry);
        }

        public void Clear()
        {
            _entries.Clear();
        }

        /// <summary>An entry about an instance with the records of <paramref name="effects"/>.</summary>
        public static EventLogEntry Entry(int day, EventInstanceDto instance, string kind, string choiceId, IEnumerable<AppliedEffect> effects)
        {
            var entry = new EventLogEntry
            {
                day = day,
                instanceId = instance != null ? instance.id : 0,
                eventId = instance != null ? instance.eventId : null,
                companyKey = instance != null ? instance.companyKey : null,
                kind = kind,
                choiceId = choiceId,
            };
            if (effects != null)
            {
                foreach (AppliedEffect effect in effects)
                {
                    if (effect != null) entry.effects.Add(Record(effect));
                }
            }
            return entry;
        }

        /// <summary>What the journal keeps of an effect: the numbers it was shown and applied with.</summary>
        public static EffectRecord Record(AppliedEffect effect)
        {
            return new EffectRecord
            {
                type = effect.type,
                channel = effect.channel ?? string.Empty,
                value = effect.value,
                durationDays = effect.durationDays,
                hit = effect.hit,
                clamped = effect.clamped,
                textKey = effect.textKey ?? string.Empty,
                textArgs = effect.textArgs != null ? (string[])effect.textArgs.Clone() : new string[0],
            };
        }

        // ---- save ----

        public void Export(EventSystemSaveDto dto)
        {
            dto.log = new List<EventLogEntry>(_entries);
        }

        /// <summary>Takes the saved entries; one that does not make sense (an unknown kind, no event, a bad company key, a bad number) is dropped with a line in <paramref name="problems"/>.</summary>
        public void Import(EventSystemSaveDto dto, List<string> problems)
        {
            _entries.Clear();
            if (dto == null || dto.log == null) return;

            foreach (EventLogEntry entry in dto.log)
            {
                if (!IsValid(entry))
                {
                    if (problems != null) problems.Add("log: an entry is dropped (kind, event, company or numbers are not valid)");
                    continue;
                }
                if (entry.effects == null) entry.effects = new List<EffectRecord>();
                _entries.Add(entry);
            }
            Trim();
        }

        private static bool IsValid(EventLogEntry entry)
        {
            if (entry == null || string.IsNullOrEmpty(entry.eventId) || entry.day < 0 || entry.instanceId < 0) return false;
            if (Array.IndexOf(Kinds, entry.kind) < 0) return false;
            if (!string.IsNullOrEmpty(entry.companyKey) && !CompanyKey.IsValid(entry.companyKey)
                && !string.Equals(entry.companyKey, EventCompanyKeys.Global, StringComparison.Ordinal)) return false;
            if (entry.effects == null) return true;
            foreach (EffectRecord record in entry.effects)
            {
                if (record == null || float.IsNaN(record.value) || float.IsInfinity(record.value) || record.durationDays < 0) return false;
                if (record.textArgs == null) record.textArgs = new string[0];
            }
            return true;
        }

        private void Trim()
        {
            int limit = _limit();
            if (limit < 1) limit = EventConfig.DefaultLogLimit;
            if (_entries.Count > limit) _entries.RemoveRange(0, _entries.Count - limit);
        }
    }
}
