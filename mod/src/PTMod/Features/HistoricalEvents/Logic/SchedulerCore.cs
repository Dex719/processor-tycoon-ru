using System;
using System.Collections.Generic;
using PTMod.Core;

namespace PTMod.Features.HistoricalEvents
{
    /// <summary>What the scheduler needs from the game side. The tests give a fake; <c>CalendarScheduler</c> gives the real one.</summary>
    public interface ISchedulerHost
    {
        /// <summary>The switch of the category in the settings (an unknown category is off).</summary>
        bool IsCategoryEnabled(string category);

        /// <summary>The companies for the roles of the entry in the world as it is now (<see cref="RoleResolver"/>); never null.</summary>
        RoleResult ResolveRoles(CalendarEntry entry);

        /// <summary>The conditions of the entry hold now, with these roles in scope (<see cref="ConditionEvaluator"/>).</summary>
        bool ConditionsHold(CalendarEntry entry, RoleResult roles);

        /// <summary>Fires stage <paramref name="stageIndex"/> (<see cref="DispatcherCore.FireStage"/>) and says what came of it.</summary>
        FireOutcome FireStage(CalendarEntry entry, int stageIndex, RoleResult roles);

        /// <summary>The window of the entry ended without a firing and its <c>fallback</c> is <c>news</c>: tell the player (<see cref="DispatcherCore.FireFallback"/>).</summary>
        void FireFallback(CalendarEntry entry);

        /// <summary>A line for the log; <paramref name="level"/> is <c>debug</c>, <c>info</c>, <c>warn</c> or <c>error</c>.</summary>
        void Log(string level, string message);

        /// <summary>An exception was caught while the entry was judged or fired and the entry is now <c>Error</c>: count it against the feature (the circuit breaker of the core).</summary>
        void Fault(CalendarEntry entry, Exception ex);
    }

    /// <summary>An entry that has fired, as the chronicle shows it.</summary>
    public sealed class FiredEntryView
    {
        public string EntryId { get; set; }
        public string Category { get; set; }

        /// <summary>The day it fired, in days from 1971-01-01.</summary>
        public int FiredDay { get; set; }

        /// <summary>How many stages were played and how many the entry has.</summary>
        public int StagesFired { get; set; }

        public int StageCount { get; set; }

        /// <summary>The companies of its roles at the firing.</summary>
        public IReadOnlyList<RoleTargetsDto> Targets { get; set; }
    }

    /// <summary>
    /// The calendar scheduler without the game (design.md, Component 3; FR-2, FR-4, FR-10 item 4): decides <i>when</i> an entry fires and keeps its state
    /// in <see cref="HistSaveData"/>. Days are whole days from 1971-01-01. Everything the world does goes through <see cref="ISchedulerHost"/>.
    /// <list type="bullet">
    /// <item><b>The queue.</b> A list of "check this entry" and "play this stage" items sorted by day, then by the anchor of the entry, then by its id (two
    /// entries on one day go in that order); a pointer marks the first that is not done. A day on which nothing is due costs one comparison.</item>
    /// <item><b>A check</b> at the day of the entry (anchor + jitter, held to its window): a category that is off makes the entry <c>Disabled</c>; a <c>major</c>
    /// entry too close to the last major firing is moved to the day the gap is over (not past the end of its window); roles that are not found or
    /// conditions that are false make it <c>Waiting</c> and it is looked at again on the 1st of every month and on the last day of the window; then the
    /// window ends, it is <c>Expired</c> and the fallback runs. Otherwise stage 0 fires and the entry is <c>Fired</c>.</item>
    /// <item><b>Stages</b> play at <c>firedDay + OffsetDays</c> for the companies the roles had at the firing (saved), unless the entry carries the stage's
    /// <c>skipIfFlag</c> or its category has been switched off meanwhile.</item>
    /// <item><b>Start.</b> <see cref="Initialize"/> (once per game, <c>initialized</c>) rolls the jitter of every entry from one generator, one number per
    /// entry whatever its jitter, and puts the entries whose anchor is before the start into <c>SkippedBeforeStart</c>.</item>
    /// <item><b>Errors.</b> An exception in the host while an entry is judged makes it <c>Error</c> (never run again) and is logged with its id; the other
    /// entries go on.</item>
    /// </list>
    /// </summary>
    public sealed class SchedulerCore
    {
        /// <summary>An entry whose window ended more than this many days ago is closed without the fallback news (it was found long after its time).</summary>
        public const int StaleDays = 31;

        private readonly Func<IReadOnlyList<CalendarEntry>> _calendar;
        private readonly Func<HistSaveData> _data;
        private readonly ISchedulerHost _host;
        private readonly Func<IModRng> _rng;
        private readonly Func<int> _majorSpacingDays;

        private readonly List<DueItem> _due = new List<DueItem>();
        private readonly Dictionary<string, CalendarEntry> _entries = new Dictionary<string, CalendarEntry>(StringComparer.Ordinal);
        private readonly Dictionary<string, EntryStateDto> _states = new Dictionary<string, EntryStateDto>(StringComparer.Ordinal);
        private readonly HashSet<string> _engineOff = new HashSet<string>(StringComparer.Ordinal);   // set aside because the engine was off, not because of a category
        private int _pointer;
        private HistSaveData _builtFor;

        /// <param name="calendar">The valid entries of the calendar, in the order of the file; may change between calls (<see cref="Rebuild"/> then).</param>
        /// <param name="data">The state of the feature; the object is replaced when a save is read, the scheduler rebuilds its queue when it sees another.</param>
        /// <param name="host">The world.</param>
        /// <param name="rng">The generator of <c>ModRandom.Stream("historical-events")</c>; asked when a jitter is rolled.</param>
        /// <param name="majorSpacingDays"><c>majorSpacingDays</c> of <c>params.json</c> (45).</param>
        public SchedulerCore(Func<IReadOnlyList<CalendarEntry>> calendar, Func<HistSaveData> data, ISchedulerHost host, Func<IModRng> rng, Func<int> majorSpacingDays)
        {
            if (calendar == null) throw new ArgumentNullException("calendar");
            if (data == null) throw new ArgumentNullException("data");
            if (host == null) throw new ArgumentNullException("host");
            _calendar = calendar;
            _data = data;
            _host = host;
            _rng = rng;
            _majorSpacingDays = majorSpacingDays ?? (() => 45);
        }

        /// <summary>The start of the game was processed (jitter rolled, the entries before the start set aside).</summary>
        public bool IsInitialized
        {
            get { return _data().initialized; }
        }

        /// <summary>Items waiting in the queue (checks and stages not done yet).</summary>
        public int DueCount
        {
            get { return _due.Count - _pointer; }
        }

        /// <summary>The day of the next item in the queue, or -1 when the queue is empty.</summary>
        public int NextDueDay
        {
            get { return _pointer < _due.Count ? _due[_pointer].Day : -1; }
        }

        // ---- start and load ----

        /// <summary>
        /// Processes the start of a new game (FR-4 items 2, 5): rolls the jitter of every entry and sets the status by the start day. Runs once per game:
        /// the flag <c>initialized</c> stops a second call (the fast-forward reports its end and the game reports itself ready). True when it ran.
        /// </summary>
        public bool Initialize(int startDay)
        {
            HistSaveData data = _data();
            if (data.initialized) return false;

            IModRng rng = _rng == null ? null : _rng();
            foreach (CalendarEntry entry in Entries())
            {
                EntryStateDto dto = Take(data, entry);
                dto.jitter = Roll(entry, rng);
                dto.status = (int)(entry.Anchor.Days < startDay ? EntryStatus.SkippedBeforeStart : EntryStatus.Pending);
                dto.firedDay = 0;
                dto.stagesFired = 0;
                dto.targets = new List<RoleTargetsDto>();
                dto.flags = new List<string>();
            }

            data.initialized = true;
            data.lastMajorDay = 0;
            Rebuild(startDay);
            _host.Log("info", "scheduler: the start of the game was processed on day " + startDay + " for " + _states.Count + " entries");
            return true;
        }

        /// <summary>
        /// Builds the queue from the state (after a save was loaded, after the feature was switched on, after the calendar changed). An entry that the
        /// state does not know yet (a newer calendar, a save from an older mod) gets a state: set aside if its anchor is before <paramref name="today"/>,
        /// otherwise pending with its own jitter. Does nothing to a game that was not initialized.
        /// </summary>
        public void Rebuild(int today)
        {
            HistSaveData data = _data();
            _builtFor = data;
            _due.Clear();
            _pointer = 0;
            _entries.Clear();
            _states.Clear();
            _engineOff.Clear();
            if (!data.initialized) return;

            IModRng rng = null;
            foreach (CalendarEntry entry in Entries())
            {
                _entries[entry.Id] = entry;
                EntryStateDto dto = HistSaveCodec.FindEntry(data, entry.Id);
                if (dto == null)
                {
                    dto = Take(data, entry);
                    bool past = entry.Anchor.Days < today;
                    dto.status = (int)(past ? EntryStatus.SkippedBeforeStart : EntryStatus.Pending);
                    if (!past)
                    {
                        if (rng == null && _rng != null) rng = _rng();
                        dto.jitter = Roll(entry, rng);
                    }
                }
                _states[entry.Id] = dto;

                switch ((EntryStatus)dto.status)
                {
                    case EntryStatus.Pending:
                        AddItem(DueDayOf(entry, dto), entry, -1);
                        break;
                    case EntryStatus.Waiting:
                        AddItem(Math.Max(today, NextCheckAfter(entry, today - 1)), entry, -1);
                        break;
                    case EntryStatus.Fired:
                        if (dto.stagesFired < entry.Stages.Count) AddItem(dto.firedDay + entry.Stages[dto.stagesFired].OffsetDays, entry, dto.stagesFired);
                        break;
                }
            }
            _due.Sort(Compare);
        }

        // ---- the days ----

        /// <summary>Plays everything that is due on <paramref name="today"/> or before. O(1) on a day with nothing due.</summary>
        public void OnDay(int today)
        {
            HistSaveData data = _data();
            if (!ReferenceEquals(_builtFor, data)) Rebuild(today);
            if (!data.initialized) return;

            while (_pointer < _due.Count && _due[_pointer].Day <= today)
            {
                DueItem item = _due[_pointer];
                _pointer++;
                Process(item, today);
            }
            Compact();
        }

        /// <summary>
        /// The monthly pass: an entry that was set aside because its category was off gets its turn again when the category is on and its window is still
        /// open (the player may switch a category on in the middle of a game).
        /// </summary>
        public void OnMonth(int today)
        {
            HistSaveData data = _data();
            if (!ReferenceEquals(_builtFor, data)) Rebuild(today);
            if (!data.initialized) return;

            foreach (CalendarEntry entry in Entries())
            {
                EntryStateDto dto;
                if (!_states.TryGetValue(entry.Id, out dto) || dto.status != (int)EntryStatus.Disabled || _engineOff.Contains(entry.Id)) continue;
                if (today > entry.WindowTo.Days || DueDayOf(entry, dto) > today || !SafeCategory(entry)) continue;

                dto.status = (int)EntryStatus.Waiting;
                Insert(new DueItem(today, entry.Anchor.Days, entry.Id, -1));
                _host.Log("info", "scheduler: " + entry.Id + " is back (the category " + entry.Category + " is on again)");
            }
        }

        // ---- the state ----

        /// <summary>The status of an entry; <see cref="EntryStatus.Pending"/> for an id the scheduler does not know.</summary>
        public EntryStatus GetStatus(string entryId)
        {
            EntryStateDto dto;
            return entryId != null && _states.TryGetValue(entryId, out dto) ? (EntryStatus)dto.status : EntryStatus.Pending;
        }

        /// <summary>The state of an entry as it is saved (the object of the section: do not keep it), or null.</summary>
        public EntryStateDto StateOf(string entryId)
        {
            EntryStateDto dto;
            return entryId != null && _states.TryGetValue(entryId, out dto) ? dto : null;
        }

        /// <summary>The entries that have fired, the earliest first (a tie by id).</summary>
        public IReadOnlyList<FiredEntryView> GetFired()
        {
            var list = new List<FiredEntryView>();
            foreach (CalendarEntry entry in Entries())
            {
                EntryStateDto dto;
                if (!_states.TryGetValue(entry.Id, out dto) || dto.status != (int)EntryStatus.Fired) continue;

                list.Add(new FiredEntryView
                {
                    EntryId = entry.Id,
                    Category = entry.Category,
                    FiredDay = dto.firedDay,
                    StagesFired = dto.stagesFired,
                    StageCount = entry.Stages.Count,
                    Targets = dto.targets == null ? new List<RoleTargetsDto>() : new List<RoleTargetsDto>(dto.targets),
                });
            }
            list.Sort((a, b) =>
            {
                int byDay = a.FiredDay.CompareTo(b.FiredDay);
                return byDay != 0 ? byDay : string.CompareOrdinal(a.EntryId, b.EntryId);
            });
            return list;
        }

        /// <summary>Sets a flag on the entry (the <c>HistFlag</c> effect): a stage with this <c>skipIfFlag</c> will not play. False for an unknown entry.</summary>
        public bool SetFlag(string entryId, string flag)
        {
            EntryStateDto dto;
            if (string.IsNullOrEmpty(flag) || entryId == null || !_states.TryGetValue(entryId, out dto)) return false;

            if (dto.flags == null) dto.flags = new List<string>();
            if (!dto.flags.Contains(flag)) dto.flags.Add(flag);
            return true;
        }

        /// <summary>
        /// The debug button "run now": the entry is judged today as if its day had come, without waiting for the date and the gap between major entries;
        /// the category switch, the roles and the conditions still count. False when it did not fire (and why is in the log).
        /// </summary>
        public bool DebugFireNow(string entryId, int today)
        {
            HistSaveData data = _data();
            if (!ReferenceEquals(_builtFor, data)) Rebuild(today);

            CalendarEntry entry;
            EntryStateDto dto;
            if (entryId == null || !_entries.TryGetValue(entryId, out entry) || !_states.TryGetValue(entryId, out dto))
            {
                _host.Log("warn", "scheduler: debug fire of an unknown entry " + entryId);
                return false;
            }

            if (dto.status == (int)EntryStatus.Fired || dto.status == (int)EntryStatus.Error)
            {
                _host.Log("warn", "scheduler: " + entryId + " is " + (EntryStatus)dto.status + " and cannot be fired again");
                return false;
            }

            try
            {
                bool fired = Check(entry, dto, today, true) == CheckResult.Fired;
                if (!fired) _host.Log("info", "scheduler: debug fire of " + entryId + " did not fire (" + (EntryStatus)dto.status + ")");
                return fired;
            }
            catch (Exception ex)
            {
                Fail(entry, dto, ex);
                return false;
            }
        }

        // ---- one item ----

        private void Process(DueItem item, int today)
        {
            CalendarEntry entry;
            EntryStateDto dto;
            if (!_entries.TryGetValue(item.Id, out entry) || !_states.TryGetValue(item.Id, out dto)) return;

            try
            {
                if (item.Stage < 0)
                {
                    if (dto.status == (int)EntryStatus.Pending || dto.status == (int)EntryStatus.Waiting) Check(entry, dto, today, false);
                }
                else if (dto.status == (int)EntryStatus.Fired && dto.stagesFired == item.Stage)
                {
                    Advance(entry, dto, today);
                }
            }
            catch (Exception ex)
            {
                Fail(entry, dto, ex);
            }
        }

        private enum CheckResult
        {
            Fired,
            Waiting,
            Deferred,
            Disabled,
            Expired,
            Error,
        }

        /// <summary>One look at an entry on <paramref name="today"/>; sets its status and queues what follows.</summary>
        private CheckResult Check(CalendarEntry entry, EntryStateDto dto, int today, bool debug)
        {
            if (!_host.IsCategoryEnabled(entry.Category))
            {
                dto.status = (int)EntryStatus.Disabled;
                _engineOff.Remove(entry.Id);
                _host.Log("info", "scheduler: " + entry.Id + " is disabled (the category " + entry.Category + " is off)");
                return CheckResult.Disabled;
            }

            int to = entry.WindowTo.Days;
            if (!debug && today - to > StaleDays)
            {
                dto.status = (int)EntryStatus.Expired;
                _host.Log("info", "scheduler: " + entry.Id + " is closed without news, its window ended on day " + to);
                return CheckResult.Expired;
            }

            HistSaveData data = _data();
            int spacing = _majorSpacingDays();
            if (!debug && entry.Major && spacing > 0 && data.lastMajorDay > 0 && today - data.lastMajorDay < spacing)
            {
                int next = Math.Min(data.lastMajorDay + spacing, to);
                if (next > today)
                {
                    Insert(new DueItem(next, entry.Anchor.Days, entry.Id, -1));
                    _host.Log("debug", "scheduler: " + entry.Id + " is moved to day " + next + " (the last major entry was on day " + data.lastMajorDay + ")");
                    return CheckResult.Deferred;
                }
            }

            RoleResult roles = _host.ResolveRoles(entry) ?? new RoleResult { Ok = false, FailedRole = "(no result)" };
            if (roles.Ok && _host.ConditionsHold(entry, roles)) return Fire(entry, dto, roles, today);

            if (debug)
            {
                _host.Log("info", "scheduler: " + entry.Id + " does not fit now (" + (roles.Ok ? "a condition is false" : "no company for the role " + roles.FailedRole) + ")");
                return CheckResult.Waiting;
            }

            int nextCheck = NextCheckAfter(entry, today);
            if (nextCheck < 0) return Expire(entry, dto, roles);

            dto.status = (int)EntryStatus.Waiting;
            Insert(new DueItem(nextCheck, entry.Anchor.Days, entry.Id, -1));
            return CheckResult.Waiting;
        }

        private CheckResult Fire(CalendarEntry entry, EntryStateDto dto, RoleResult roles, int today)
        {
            FireOutcome outcome = _host.FireStage(entry, 0, roles);
            if (outcome == FireOutcome.UnknownEvent)
            {
                dto.status = (int)EntryStatus.Error;
                _host.Log("error", "scheduler: " + entry.Id + " names an event the pack does not have; the entry will not run");
                return CheckResult.Error;
            }
            if (outcome == FireOutcome.Disabled)
            {
                dto.status = (int)EntryStatus.Disabled;
                _engineOff.Add(entry.Id);
                _host.Log("info", "scheduler: " + entry.Id + " is disabled (the event system or the pack is off)");
                return CheckResult.Disabled;
            }

            dto.status = (int)EntryStatus.Fired;
            dto.firedDay = today;
            dto.stagesFired = 1;
            dto.targets = roles.ToTargets();
            if (entry.Major) _data().lastMajorDay = today;
            _host.Log("info", "scheduler: fired " + entry.Id + " on day " + today + " (" + roles + ")");

            Advance(entry, dto, today);
            return dto.status == (int)EntryStatus.Error ? CheckResult.Error : CheckResult.Fired;
        }

        /// <summary>Plays every stage of a fired entry that is due on <paramref name="today"/>; queues the first that is not.</summary>
        private void Advance(CalendarEntry entry, EntryStateDto dto, int today)
        {
            while (dto.status == (int)EntryStatus.Fired && dto.stagesFired < entry.Stages.Count)
            {
                int k = dto.stagesFired;
                StageSpec stage = entry.Stages[k];
                int due = dto.firedDay + stage.OffsetDays;
                if (due > today)
                {
                    Insert(new DueItem(due, entry.Anchor.Days, entry.Id, k));
                    return;
                }

                if (!_host.IsCategoryEnabled(entry.Category))
                {
                    _host.Log("info", "scheduler: the stages of " + entry.Id + " from " + k + " on are dropped (the category " + entry.Category + " is off)");
                    dto.stagesFired = entry.Stages.Count;
                    return;
                }

                if (stage.SkipIfFlag != null && dto.flags != null && dto.flags.Contains(stage.SkipIfFlag))
                {
                    _host.Log("info", "scheduler: stage " + k + " of " + entry.Id + " is skipped (the flag " + stage.SkipIfFlag + ")");
                    dto.stagesFired = k + 1;
                    continue;
                }

                FireOutcome outcome = _host.FireStage(entry, k, RoleResult.FromTargets(dto.targets));
                if (outcome == FireOutcome.UnknownEvent)
                {
                    dto.status = (int)EntryStatus.Error;
                    _host.Log("error", "scheduler: stage " + k + " of " + entry.Id + " names an event the pack does not have; the entry will not run");
                    return;
                }
                dto.stagesFired = k + 1;
            }
        }

        private CheckResult Expire(CalendarEntry entry, EntryStateDto dto, RoleResult roles)
        {
            dto.status = (int)EntryStatus.Expired;
            _host.Log("info", "scheduler: " + entry.Id + " expired (the window ended with " + (roles.Ok ? "a condition false" : "no company for the role " + roles.FailedRole) + "), fallback " + entry.Fallback);
            if (string.Equals(entry.Fallback, FallbackModes.News, StringComparison.Ordinal)) _host.FireFallback(entry);
            return CheckResult.Expired;
        }

        private void Fail(CalendarEntry entry, EntryStateDto dto, Exception ex)
        {
            dto.status = (int)EntryStatus.Error;
            _host.Log("error", "scheduler: " + entry.Id + " failed and will not run again: " + ex.GetType().Name + ": " + ex.Message);
            try
            {
                _host.Fault(entry, ex);
            }
            catch (Exception)
            {
                // Reporting must not break the pass over the other entries.
            }
        }

        private bool SafeCategory(CalendarEntry entry)
        {
            try
            {
                return _host.IsCategoryEnabled(entry.Category);
            }
            catch (Exception)
            {
                return false;
            }
        }

        // ---- days ----

        /// <summary>The day an entry is first looked at: anchor + jitter, held to its window.</summary>
        private static int DueDayOf(CalendarEntry entry, EntryStateDto dto)
        {
            int day = entry.Anchor.Days + dto.jitter;
            int from = entry.WindowFrom.Days;
            int to = entry.WindowTo.Days;
            if (day > to) day = to;
            if (day < from) day = from;
            return day;
        }

        /// <summary>
        /// The next day an entry that did not fit on <paramref name="day"/> is looked at: the 1st of the next month, or the last day of the window if that
        /// comes first; -1 when <paramref name="day"/> is the last day of the window (or later): it is over.
        /// </summary>
        private static int NextCheckAfter(CalendarEntry entry, int day)
        {
            int to = entry.WindowTo.Days;
            if (day >= to) return -1;
            return Math.Min(FirstOfNextMonth(day), to);
        }

        /// <summary>The day number of the 1st of the month after the one <paramref name="day"/> is in.</summary>
        public static int FirstOfNextMonth(int day)
        {
            SimpleDate date;
            if (!SimpleDate.TryFromDays(day, out date)) return day + 1;

            int year = date.Month == 12 ? date.Year + 1 : date.Year;
            int month = date.Month == 12 ? 1 : date.Month + 1;
            SimpleDate first;
            return SimpleDate.TryCreate(year, month, 1, out first) ? first.Days : day + 1;
        }

        private static int Roll(CalendarEntry entry, IModRng rng)
        {
            int jitter = Math.Max(0, Math.Min(CalendarValidator.MaxJitterDays, entry.JitterDays));
            if (rng == null) return 0;

            // One number per entry whatever its jitter, so a change of one entry does not move the rolls of the others.
            float value = rng.Value();
            int span = 2 * jitter + 1;
            int step = (int)(value * span);
            if (step >= span) step = span - 1;
            return step - jitter;
        }

        // ---- the queue ----

        private struct DueItem
        {
            public readonly int Day;
            public readonly int Anchor;
            public readonly string Id;
            public readonly int Stage;

            public DueItem(int day, int anchor, string id, int stage)
            {
                Day = day;
                Anchor = anchor;
                Id = id;
                Stage = stage;
            }
        }

        private static int Compare(DueItem a, DueItem b)
        {
            if (a.Day != b.Day) return a.Day < b.Day ? -1 : 1;
            if (a.Anchor != b.Anchor) return a.Anchor < b.Anchor ? -1 : 1;
            int byId = string.CompareOrdinal(a.Id, b.Id);
            if (byId != 0) return byId;
            return a.Stage.CompareTo(b.Stage);
        }

        private void AddItem(int day, CalendarEntry entry, int stage)
        {
            _due.Add(new DueItem(day, entry.Anchor.Days, entry.Id, stage));
        }

        /// <summary>Puts an item at its place among the ones that are not done (binary search; a tie goes after the equal ones).</summary>
        private void Insert(DueItem item)
        {
            int low = _pointer;
            int high = _due.Count;
            while (low < high)
            {
                int middle = low + (high - low) / 2;
                if (Compare(_due[middle], item) <= 0) low = middle + 1;
                else high = middle;
            }
            _due.Insert(low, item);
        }

        /// <summary>Drops the part of the list that is done, once it is big.</summary>
        private void Compact()
        {
            if (_pointer < 64 || _pointer * 2 < _due.Count) return;
            _due.RemoveRange(0, _pointer);
            _pointer = 0;
        }

        // ---- the calendar ----

        private IEnumerable<CalendarEntry> Entries()
        {
            IReadOnlyList<CalendarEntry> calendar = _calendar();
            if (calendar == null) yield break;

            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < calendar.Count; i++)
            {
                CalendarEntry entry = calendar[i];
                if (entry == null || string.IsNullOrEmpty(entry.Id) || entry.Stages == null || entry.Stages.Count == 0 || !seen.Add(entry.Id)) continue;
                yield return entry;
            }
        }

        /// <summary>The state of an entry in the section; made and added when the section has none.</summary>
        private static EntryStateDto Take(HistSaveData data, CalendarEntry entry)
        {
            EntryStateDto dto = HistSaveCodec.FindEntry(data, entry.Id);
            if (dto != null) return dto;

            dto = new EntryStateDto { id = entry.Id, status = (int)EntryStatus.Pending };
            data.entries.Add(dto);
            return dto;
        }
    }
}
