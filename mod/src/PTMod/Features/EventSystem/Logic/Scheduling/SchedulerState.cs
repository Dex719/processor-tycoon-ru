using System;
using System.Collections.Generic;
using PTMod.Core.Logic.Companies;

namespace PTMod.Features.EventSystem
{
    // The saved shape of the scheduler's part of the section "event-system" (design.md, "Секция ModSave"): plain public fields whose names are the JSON keys.

    /// <summary>The last firing of an event for a company (the key <c>*</c> for an <c>AllCompanies</c> event as a whole): what <c>cooldownDays</c> counts from.</summary>
    public sealed class CooldownRecord
    {
        public string eventId;
        public string companyKey;
        public int lastDay;
    }

    /// <summary>The last firing of any event of a category: what <c>minGapDays</c> counts from.</summary>
    public sealed class CategoryRecord
    {
        public string category;
        public int lastDay;
    }

    /// <summary>A firing that comes due on a later day (a <c>FireEvent</c> effect with a delay, <c>EventBus.Fire</c> with <c>DelayDays</c>).</summary>
    public sealed class ScheduledFire
    {
        public string eventId;
        public int dueDay;

        /// <summary>The target; null or empty - the <c>targets</c> of the definition decide.</summary>
        public string companyKey;

        public int parentId;
        public string[] args = new string[0];

        /// <summary>Skip the conditions when it comes due (a chain: the parent has decided already).</summary>
        public bool ignoreConditions = true;

        /// <summary>The depth of the chain the firing continues (<c>ChainFlags</c>); 0 - not a chain, or unknown.</summary>
        public int depth;
    }

    /// <summary>
    /// A <c>Date</c> / <c>Condition</c> / delayed firing for the player that waits because the player has <c>maxPendingChoices</c> choices open already (FR-2
    /// item 6). It is retried every day and dropped after <see cref="SchedulerState.DeferLimitDays"/> days.
    /// </summary>
    public sealed class DeferredFire
    {
        public string eventId;

        /// <summary>The target chosen when the event fired; null - the whole group of an <c>AllCompanies</c> event, chosen again.</summary>
        public string companyKey;

        public int queuedDay;
        public int parentId;
        public string[] args = new string[0];

        /// <summary>The depth of the chain the firing continues; 0 - not a chain.</summary>
        public int depth;
    }

    /// <summary>
    /// What the scheduler remembers between days and saves: the last processed day, cooldowns, the last day of each category, the events used up
    /// (<c>oncePerGame</c> and <c>Date</c> triggers), the level of every edge-triggered condition, the firings scheduled for later days and the firings
    /// waiting for a free choice slot. Lookups are dictionaries; the lists of <see cref="Export"/> are in a stable order. Main thread only.
    /// </summary>
    public sealed class SchedulerState
    {
        /// <summary>No day recorded.</summary>
        public const int NoDay = int.MinValue;

        /// <summary>How long a firing may wait for a free choice slot of the player (FR-2 item 6).</summary>
        public const int DeferLimitDays = 30;

        private readonly Dictionary<string, int> _cooldowns = new Dictionary<string, int>(StringComparer.Ordinal);
        private readonly Dictionary<string, int> _categories = new Dictionary<string, int>(StringComparer.Ordinal);
        private readonly HashSet<string> _firedOnce = new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<string> _edges = new HashSet<string>(StringComparer.Ordinal);
        private readonly List<ScheduledFire> _scheduled = new List<ScheduledFire>();
        private readonly List<DeferredFire> _deferred = new List<DeferredFire>();

        public SchedulerState()
        {
            LastProcessedDay = NoDay;
        }

        /// <summary>The last day <c>ProcessDay</c> ran for; the same day is not processed twice.</summary>
        public int LastProcessedDay { get; set; }

        // ---- cooldowns ----

        /// <summary>The day the event last fired for the company (or for <c>*</c>), or <see cref="NoDay"/>.</summary>
        public int LastFired(string eventId, string companyKey)
        {
            int day;
            return _cooldowns.TryGetValue(Key(eventId, companyKey), out day) ? day : NoDay;
        }

        public void SetLastFired(string eventId, string companyKey, int day)
        {
            if (string.IsNullOrEmpty(eventId) || string.IsNullOrEmpty(companyKey)) return;
            _cooldowns[Key(eventId, companyKey)] = day;
        }

        // ---- categories ----

        /// <summary>The day an event of the category last fired, or <see cref="NoDay"/>.</summary>
        public int CategoryLastDay(string category)
        {
            int day;
            return category != null && _categories.TryGetValue(category, out day) ? day : NoDay;
        }

        public void SetCategoryLastDay(string category, int day)
        {
            if (string.IsNullOrEmpty(category)) return;
            _categories[category] = day;
        }

        // ---- once ----

        /// <summary>The event is used up: a <c>oncePerGame</c> event that fired, a <c>Date</c> trigger that fired or was queued.</summary>
        public bool IsUsedUp(string eventId)
        {
            return eventId != null && _firedOnce.Contains(eventId);
        }

        public void MarkUsedUp(string eventId)
        {
            if (!string.IsNullOrEmpty(eventId)) _firedOnce.Add(eventId);
        }

        // ---- edges ----

        /// <summary>The level of an edge-triggered <c>Condition</c> at its last check.</summary>
        public bool EdgeLevel(string eventId)
        {
            return eventId != null && _edges.Contains(eventId);
        }

        public void SetEdgeLevel(string eventId, bool level)
        {
            if (string.IsNullOrEmpty(eventId)) return;
            if (level) _edges.Add(eventId);
            else _edges.Remove(eventId);
        }

        // ---- queues ----

        /// <summary>The firings scheduled for later days, in the order they were scheduled.</summary>
        public List<ScheduledFire> Scheduled
        {
            get { return _scheduled; }
        }

        /// <summary>The firings waiting for a free choice slot of the player, oldest first.</summary>
        public List<DeferredFire> Deferred
        {
            get { return _deferred; }
        }

        /// <summary>A firing of the event waits in <see cref="Deferred"/>.</summary>
        public bool IsDeferred(string eventId)
        {
            foreach (DeferredFire fire in _deferred)
            {
                if (string.Equals(fire.eventId, eventId, StringComparison.Ordinal)) return true;
            }
            return false;
        }

        // ---- whole state ----

        /// <summary>Forgets everything (a new game, a save without the section).</summary>
        public void Clear()
        {
            LastProcessedDay = NoDay;
            _cooldowns.Clear();
            _categories.Clear();
            _firedOnce.Clear();
            _edges.Clear();
            _scheduled.Clear();
            _deferred.Clear();
        }

        /// <summary>The state as save records, sorted so that the same state always writes the same JSON.</summary>
        public void Export(EventSystemSaveDto dto)
        {
            dto.lastProcessedDay = LastProcessedDay == NoDay ? 0 : LastProcessedDay;

            dto.cooldowns.Clear();
            var cooldownKeys = new List<string>(_cooldowns.Keys);
            cooldownKeys.Sort(StringComparer.Ordinal);
            foreach (string key in cooldownKeys)
            {
                int split = key.IndexOf('\n');
                dto.cooldowns.Add(new CooldownRecord { eventId = key.Substring(0, split), companyKey = key.Substring(split + 1), lastDay = _cooldowns[key] });
            }

            dto.categories.Clear();
            var categories = new List<string>(_categories.Keys);
            categories.Sort(StringComparer.Ordinal);
            foreach (string category in categories) dto.categories.Add(new CategoryRecord { category = category, lastDay = _categories[category] });

            dto.firedOnce = Sorted(_firedOnce);
            dto.edges = Sorted(_edges);

            dto.scheduled.Clear();
            foreach (ScheduledFire fire in _scheduled)
            {
                dto.scheduled.Add(new ScheduledFire
                {
                    eventId = fire.eventId, dueDay = fire.dueDay, companyKey = fire.companyKey, parentId = fire.parentId,
                    args = Copy(fire.args), ignoreConditions = fire.ignoreConditions, depth = fire.depth,
                });
            }

            dto.deferred.Clear();
            foreach (DeferredFire fire in _deferred)
            {
                dto.deferred.Add(new DeferredFire
                {
                    eventId = fire.eventId, companyKey = fire.companyKey, queuedDay = fire.queuedDay, parentId = fire.parentId, args = Copy(fire.args),
                    depth = fire.depth,
                });
            }
        }

        /// <summary>
        /// Replaces the state with the saved records. A record that does not make sense (no event id, a company key that is not <c>player</c> /
        /// <c>ai:&lt;n&gt;</c> / <c>*</c>, a negative day) is dropped with a line in <paramref name="problems"/> (NFR-7).
        /// </summary>
        public void Import(EventSystemSaveDto dto, List<string> problems)
        {
            Clear();
            if (dto == null) return;
            problems = problems ?? new List<string>();

            if (dto.lastProcessedDay > SaveChecks.MaxDay) problems.Add("lastProcessedDay: " + dto.lastProcessedDay + " is not a game day, it is dropped");
            LastProcessedDay = dto.lastProcessedDay > 0 && dto.lastProcessedDay <= SaveChecks.MaxDay ? dto.lastProcessedDay : NoDay;

            foreach (CooldownRecord record in dto.cooldowns ?? new List<CooldownRecord>())
            {
                if (record == null || string.IsNullOrEmpty(record.eventId) || !IsCooldownKey(record.companyKey) || record.lastDay < 0)
                {
                    problems.Add("cooldowns: a record is dropped (event, company or day is not valid)");
                    continue;
                }
                SetLastFired(record.eventId, record.companyKey, record.lastDay);
            }

            foreach (CategoryRecord record in dto.categories ?? new List<CategoryRecord>())
            {
                if (record == null || string.IsNullOrEmpty(record.category) || record.lastDay < 0)
                {
                    problems.Add("categories: a record is dropped (category or day is not valid)");
                    continue;
                }
                SetCategoryLastDay(record.category, record.lastDay);
            }

            foreach (string id in dto.firedOnce ?? new List<string>()) MarkUsedUp(id);
            foreach (string id in dto.edges ?? new List<string>()) SetEdgeLevel(id, true);

            foreach (ScheduledFire fire in dto.scheduled ?? new List<ScheduledFire>())
            {
                if (fire == null || string.IsNullOrEmpty(fire.eventId) || fire.dueDay < 0 || !IsTargetKey(fire.companyKey) || fire.parentId < 0
                    || fire.depth < 0 || fire.depth > ChainFlags.MaxDepth)
                {
                    problems.Add("scheduled: a record is dropped (event, day, company, parent or depth is not valid)");
                    continue;
                }
                fire.args = Copy(fire.args);
                _scheduled.Add(fire);
            }

            foreach (DeferredFire fire in dto.deferred ?? new List<DeferredFire>())
            {
                if (fire == null || string.IsNullOrEmpty(fire.eventId) || fire.queuedDay < 0 || !IsTargetKey(fire.companyKey) || fire.parentId < 0
                    || fire.depth < 0 || fire.depth > ChainFlags.MaxDepth)
                {
                    problems.Add("deferred: a record is dropped (event, day, company, parent or depth is not valid)");
                    continue;
                }
                fire.args = Copy(fire.args);
                _deferred.Add(fire);
            }
        }

        // ---- helpers ----

        private static string Key(string eventId, string companyKey)
        {
            return eventId + "\n" + companyKey;
        }

        private static bool IsCooldownKey(string key)
        {
            return CompanyKey.IsValid(key) || string.Equals(key, EventCompanyKeys.AllCompaniesCooldown, StringComparison.Ordinal);
        }

        private static bool IsTargetKey(string key)
        {
            return string.IsNullOrEmpty(key) || CompanyKey.IsValid(key);
        }

        private static List<string> Sorted(IEnumerable<string> items)
        {
            var list = new List<string>(items);
            list.Sort(StringComparer.Ordinal);
            return list;
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
