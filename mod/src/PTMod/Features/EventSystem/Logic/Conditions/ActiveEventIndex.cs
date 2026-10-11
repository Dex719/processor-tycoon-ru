using System;
using System.Collections.Generic;

namespace PTMod.Features.EventSystem
{
    /// <summary>
    /// The instances that are "going on" now: waiting for a choice, or with an effect still running. The runtime keeps it current (an instance is
    /// <see cref="Set"/> when it is created or changes, <see cref="Remove"/>d when it is over; rebuilt after a load); the scheduler and the predicates read it:
    /// the player's pending choices (<c>maxPendingChoices</c>, FR-2 item 6), the active events of a category (<c>maxActive</c>, FR-2 item 7) and
    /// <c>es.activeInCategory</c> (AC-6.5). Main thread only.
    /// </summary>
    public sealed class ActiveEventIndex
    {
        private readonly Dictionary<int, Entry> _entries = new Dictionary<int, Entry>();

        private sealed class Entry
        {
            public int GroupId;
            public string EventId;
            public string Category;
            public string CompanyKey;
            public bool AwaitingChoice;
        }

        /// <summary>Instances in the index.</summary>
        public int Count
        {
            get { return _entries.Count; }
        }

        /// <summary>
        /// Records the state of an instance. An instance that neither waits for a choice nor has a running effect is removed. <paramref name="groupId"/>
        /// is shared by the instances of one <c>AllCompanies</c> firing (0 - the instance is its own group).
        /// </summary>
        public void Set(int instanceId, int groupId, string eventId, string category, string companyKey, bool awaitingChoice, bool hasActiveEffects)
        {
            if (!awaitingChoice && !hasActiveEffects)
            {
                _entries.Remove(instanceId);
                return;
            }

            _entries[instanceId] = new Entry
            {
                GroupId = groupId != 0 ? groupId : instanceId,
                EventId = eventId,
                Category = category,
                CompanyKey = companyKey,
                AwaitingChoice = awaitingChoice,
            };
        }

        public void Remove(int instanceId)
        {
            _entries.Remove(instanceId);
        }

        public void Clear()
        {
            _entries.Clear();
        }

        /// <summary>Choices the company has to make now (for the player: the open choice mails).</summary>
        public int PendingChoices(string companyKey)
        {
            int count = 0;
            foreach (Entry entry in _entries.Values)
            {
                if (entry.AwaitingChoice && string.Equals(entry.CompanyKey, companyKey, StringComparison.Ordinal)) count++;
            }
            return count;
        }

        /// <summary>Firings of the category that are going on; the instances of one group count once.</summary>
        public int ActiveInCategory(string category)
        {
            if (string.IsNullOrEmpty(category)) return 0;
            var groups = new HashSet<int>();
            foreach (Entry entry in _entries.Values)
            {
                if (string.Equals(entry.Category, category, StringComparison.Ordinal)) groups.Add(entry.GroupId);
            }
            return groups.Count;
        }

        /// <summary>
        /// An event of the category is going on for the company (a choice to make or a running effect); for a null company: for any company, or with a global
        /// effect (<c>scope: "any"</c> of <c>es.activeInCategory</c>).
        /// </summary>
        public bool IsActiveInCategory(string category, string companyKey)
        {
            if (string.IsNullOrEmpty(category)) return false;
            foreach (Entry entry in _entries.Values)
            {
                if (!string.Equals(entry.Category, category, StringComparison.Ordinal)) continue;
                if (companyKey == null || string.Equals(entry.CompanyKey, companyKey, StringComparison.Ordinal)) return true;
            }
            return false;
        }
    }
}
