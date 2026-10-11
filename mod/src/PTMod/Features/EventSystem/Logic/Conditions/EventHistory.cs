using System;
using System.Collections.Generic;
using PTMod.Core.Logic.Companies;

namespace PTMod.Features.EventSystem
{
    /// <summary>What the engine's predicates ask about the past and the present of the events (FR-6, design.md "Условия").</summary>
    public interface IEventHistory
    {
        /// <summary>The event fired for the company; with <paramref name="withinDays"/> &gt; 0, no longer ago than that on <paramref name="today"/>.</summary>
        bool HasFired(string eventId, string companyKey, int withinDays, int today);

        /// <summary>The company chose this choice of the event (at any time).</summary>
        bool HasChosen(string eventId, string choiceId, string companyKey);

        /// <summary>The company has the flag of a chain.</summary>
        bool HasFlag(string flag, string companyKey);

        /// <summary>An event of the category is going on for the company; for a null company - for any company or with a global effect.</summary>
        bool IsActiveInCategory(string category, string companyKey);
    }

    // The saved shape of the history (part of the section "event-system"); the names are the JSON keys.

    /// <summary>The last firing of an event for a company, kept for <c>es.eventFired</c> (cooldowns forget groups per company, the journal is cut at 400).</summary>
    public sealed class FiredRecord
    {
        public string eventId;
        public string companyKey;
        public int lastDay;
    }

    /// <summary>A choice a company made (<c>es.choiceMade</c>).</summary>
    public sealed class ChoiceRecord
    {
        public string eventId;
        public string choiceId;
        public string companyKey;
        public int day;
    }

    /// <summary>A flag of a chain set for a company (<c>es.eventFlag</c>).</summary>
    public sealed class FlagRecord
    {
        public string flag;
        public string companyKey;
        public int day;
    }

    /// <summary>
    /// The memory behind the engine's predicates: the last firing of every event per company (the scheduler records it), the choices made (the choice
    /// controller records them), the flags of chains, and the index of what is going on now (<see cref="Active"/>). Saved with the section of the engine.
    /// Main thread only.
    /// </summary>
    public sealed class EventHistory : IEventHistory
    {
        private readonly Dictionary<string, int> _fired = new Dictionary<string, int>(StringComparer.Ordinal);
        private readonly Dictionary<string, ChoiceRecord> _choices = new Dictionary<string, ChoiceRecord>(StringComparer.Ordinal);
        private readonly Dictionary<string, int> _flags = new Dictionary<string, int>(StringComparer.Ordinal);
        private readonly ActiveEventIndex _active = new ActiveEventIndex();

        /// <summary>The instances going on now; kept by the runtime, not saved (rebuilt from the instances after a load).</summary>
        public ActiveEventIndex Active
        {
            get { return _active; }
        }

        // ---- recording ----

        public void RecordFired(string eventId, string companyKey, int day)
        {
            if (string.IsNullOrEmpty(eventId) || string.IsNullOrEmpty(companyKey)) return;
            _fired[Key(eventId, companyKey)] = day;
        }

        public void RecordChoice(string eventId, string choiceId, string companyKey, int day)
        {
            if (string.IsNullOrEmpty(eventId) || string.IsNullOrEmpty(choiceId) || string.IsNullOrEmpty(companyKey)) return;
            _choices[Key(eventId, choiceId, companyKey)] = new ChoiceRecord { eventId = eventId, choiceId = choiceId, companyKey = companyKey, day = day };
        }

        public void SetFlag(string flag, string companyKey, int day)
        {
            if (string.IsNullOrEmpty(flag) || string.IsNullOrEmpty(companyKey)) return;
            _flags[Key(flag, companyKey)] = day;
        }

        public void ClearFlag(string flag, string companyKey)
        {
            if (flag == null || companyKey == null) return;
            _flags.Remove(Key(flag, companyKey));
        }

        /// <summary>Forgets everything (a new game, a save without the section); the index of active instances too.</summary>
        public void Clear()
        {
            _fired.Clear();
            _choices.Clear();
            _flags.Clear();
            _active.Clear();
        }

        // ---- IEventHistory ----

        public bool HasFired(string eventId, string companyKey, int withinDays, int today)
        {
            int day;
            if (eventId == null || companyKey == null || !_fired.TryGetValue(Key(eventId, companyKey), out day)) return false;
            return withinDays <= 0 || today - day <= withinDays;
        }

        public bool HasChosen(string eventId, string choiceId, string companyKey)
        {
            return eventId != null && choiceId != null && companyKey != null && _choices.ContainsKey(Key(eventId, choiceId, companyKey));
        }

        public bool HasFlag(string flag, string companyKey)
        {
            return flag != null && companyKey != null && _flags.ContainsKey(Key(flag, companyKey));
        }

        public bool IsActiveInCategory(string category, string companyKey)
        {
            return _active.IsActiveInCategory(category, companyKey);
        }

        // ---- save ----

        /// <summary>The history as save records, sorted.</summary>
        public void Export(EventSystemSaveDto dto)
        {
            dto.fired.Clear();
            foreach (string key in SortedKeys(_fired))
            {
                string[] parts = key.Split('\n');
                dto.fired.Add(new FiredRecord { eventId = parts[0], companyKey = parts[1], lastDay = _fired[key] });
            }

            dto.choices.Clear();
            foreach (string key in SortedKeys(_choices))
            {
                ChoiceRecord record = _choices[key];
                dto.choices.Add(new ChoiceRecord { eventId = record.eventId, choiceId = record.choiceId, companyKey = record.companyKey, day = record.day });
            }

            dto.flags.Clear();
            foreach (string key in SortedKeys(_flags))
            {
                string[] parts = key.Split('\n');
                dto.flags.Add(new FlagRecord { flag = parts[0], companyKey = parts[1], day = _flags[key] });
            }
        }

        /// <summary>Replaces the history with the saved records; a record with a missing name or an invalid company key is dropped (NFR-7).</summary>
        public void Import(EventSystemSaveDto dto, List<string> problems)
        {
            _fired.Clear();
            _choices.Clear();
            _flags.Clear();
            if (dto == null) return;
            problems = problems ?? new List<string>();

            foreach (FiredRecord record in dto.fired ?? new List<FiredRecord>())
            {
                if (record == null || string.IsNullOrEmpty(record.eventId) || !CompanyKey.IsValid(record.companyKey) || record.lastDay < 0)
                {
                    problems.Add("fired: a record is dropped (event, company or day is not valid)");
                    continue;
                }
                RecordFired(record.eventId, record.companyKey, record.lastDay);
            }

            foreach (ChoiceRecord record in dto.choices ?? new List<ChoiceRecord>())
            {
                if (record == null || string.IsNullOrEmpty(record.eventId) || string.IsNullOrEmpty(record.choiceId) || !CompanyKey.IsValid(record.companyKey))
                {
                    problems.Add("choices: a record is dropped (event, choice or company is not valid)");
                    continue;
                }
                RecordChoice(record.eventId, record.choiceId, record.companyKey, record.day);
            }

            foreach (FlagRecord record in dto.flags ?? new List<FlagRecord>())
            {
                if (record == null || string.IsNullOrEmpty(record.flag) || !CompanyKey.IsValid(record.companyKey))
                {
                    problems.Add("flags: a record is dropped (flag or company is not valid)");
                    continue;
                }
                SetFlag(record.flag, record.companyKey, record.day);
            }
        }

        // ---- helpers ----

        private static string Key(params string[] parts)
        {
            return string.Join("\n", parts);
        }

        private static List<string> SortedKeys<T>(Dictionary<string, T> map)
        {
            var keys = new List<string>(map.Keys);
            keys.Sort(StringComparer.Ordinal);
            return keys;
        }
    }
}
