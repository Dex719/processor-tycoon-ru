using System;
using System.Collections.Generic;
using System.Globalization;

namespace PTMod.Features.RivalPersonalities
{
    /// <summary>
    /// The attitude of the rivals to the player (design.md, Component 6; FR-4): the value on the scale -100..+100, the history of its changes, the
    /// level and the monthly drift. Pure: the state is the section of the side save file (<see cref="RivalState.attitudes"/>, read again at every
    /// call because a load replaces the object), the numbers are the archetype's (<c>baseline</c>, <c>sensitivity</c>) and the config's
    /// (<c>attitude.thresholds</c>, <c>monthlyDrift</c>, <c>historySize</c>); the clock and the bankruptcy of a company come from the caller. The game
    /// side (<c>Attitude/AttitudeNotifier</c>) shows the notification when <see cref="LevelChanged"/> is raised.
    /// <list type="bullet">
    /// <item><b>Start.</b> A company that has an archetype but no saved value is at the baseline of the archetype (FR-4 item 1;
    /// <see cref="PersonalityBook"/> writes it when the archetype is given).</item>
    /// <item><b>Change.</b> <c>a' = clamp(a + delta × sensitivity, -100, 100)</c>; the change that really happened goes to the history (the newest
    /// <c>historySize</c>, 20, are kept) with its date and reason (FR-4 item 2).</item>
    /// <item><b>Drift.</b> <see cref="MonthlyDrift"/> moves every value 10 % of the way to the baseline (FR-4 item 3). It is not written to the
    /// history: twelve lines a year would push the reasons out.</item>
    /// <item><b>Bankrupt.</b> The attitude of a bankrupt company is frozen: no change, no drift (FR-4 item 5).</item>
    /// </list>
    /// A company without an archetype in effect is unknown: nothing is read from it or written to it. Main thread only.
    /// </summary>
    public sealed class AttitudeLedger
    {
        /// <summary>The longest reason kept in the history (longer ones are cut).</summary>
        public const int MaxReasonLength = 64;

        /// <summary>A change smaller than this (the attitude is already at the edge of the scale) is not written to the history.</summary>
        private const float MinRecorded = 0.0005f;

        private readonly PersonalityBook _book;
        private readonly Func<RivalState> _state;
        private readonly Func<ArchetypeCatalog> _catalog;
        private readonly Func<DateTime> _today;
        private readonly Func<int, bool> _isBankrupt;
        private readonly Action<string> _warn;

        /// <param name="book">The characters: which archetype a company has.</param>
        /// <param name="state">The section as it is now (never keep the object: a load replaces it); null gives an empty state.</param>
        /// <param name="catalog">The archetypes and the numbers; null gives an empty catalog.</param>
        /// <param name="today">The game date for the history; null gives no date.</param>
        /// <param name="isBankrupt">Tells whether the company with this <c>UniqueID</c> is bankrupt; null means nobody is.</param>
        /// <param name="warn">One English line per problem (a handler of the event that failed); may be null.</param>
        public AttitudeLedger(PersonalityBook book, Func<RivalState> state, Func<ArchetypeCatalog> catalog, Func<DateTime> today,
                              Func<int, bool> isBankrupt, Action<string> warn = null)
        {
            _book = book;
            _state = state;
            _catalog = catalog;
            _today = today;
            _isBankrupt = isBankrupt;
            _warn = warn;
        }

        /// <summary>
        /// The level of a company changed: <c>(UniqueID, level before, level after)</c>. Raised after the value was saved, by <see cref="Change"/>
        /// and by <see cref="MonthlyDrift"/>. A handler that throws is reported to <c>warn</c> and the others still run.
        /// </summary>
        public event Action<int, AttitudeLevel, AttitudeLevel> LevelChanged;

        /// <summary>
        /// The value of a company moved: <c>UniqueID</c>. Raised by <see cref="Change"/> when the attitude really moved and by <see cref="MonthlyDrift"/>
        /// for each value that drifted, after the value was saved. A reader that caches something computed from the attitude (the price of a takeover)
        /// drops it here. A handler that throws is reported to <c>warn</c> and the others still run.
        /// </summary>
        public event Action<int> Changed;

        private RivalState State
        {
            get
            {
                RivalState state = _state != null ? _state() : null;
                return state ?? new RivalState();
            }
        }

        private ArchetypeCatalog Catalog
        {
            get
            {
                ArchetypeCatalog catalog = _catalog != null ? _catalog() : null;
                return catalog ?? ArchetypeCatalog.Empty;
            }
        }

        // ---- reading ----

        /// <summary>The company has an archetype in effect, so its attitude is kept and can be changed.</summary>
        public bool IsKnown(int uniqueId)
        {
            return _book.ArchetypeOf(uniqueId) != null;
        }

        /// <summary>
        /// The attitude of the rival to the player, -100..+100: the saved value, or the baseline of its archetype when none was saved. 0 for a company
        /// that has no archetype.
        /// </summary>
        public float Get(int uniqueId)
        {
            Archetype archetype = _book.ArchetypeOf(uniqueId);
            if (archetype == null) return 0f;

            float? saved = _book.AttitudeOf(uniqueId);
            return saved.HasValue ? AttitudeMath.Clamp(saved.Value) : BaselineOf(archetype);
        }

        /// <summary>The level of the attitude of the company (<see cref="AttitudeLevel.Neutral"/> for a company that has no archetype).</summary>
        public AttitudeLevel GetLevel(int uniqueId)
        {
            return LevelOf(Get(uniqueId));
        }

        /// <summary>The level of a value by the borders of the config (-60 / -20 / +20 / +60).</summary>
        public AttitudeLevel LevelOf(float attitude)
        {
            return AttitudeMath.LevelOf(attitude, AttitudeThresholds.FromConfig(Catalog.Config.attitude.thresholds));
        }

        /// <summary>The company is frozen (bankrupt): its attitude does not change.</summary>
        public bool IsFrozen(int uniqueId)
        {
            return _isBankrupt != null && _isBankrupt(uniqueId);
        }

        /// <summary>
        /// The changes of the attitude of the company, oldest first, at most <c>historySize</c> of them. A copy; empty for a company without a
        /// record.
        /// </summary>
        public IReadOnlyList<AttitudeEntry> History(int uniqueId)
        {
            var copy = new List<AttitudeEntry>();
            AttitudeRecord record = Find(State, uniqueId);
            if (record == null || record.history == null) return copy;
            for (int i = 0; i < record.history.Count; i++)
            {
                AttitudeEntry entry = record.history[i];
                if (entry != null) copy.Add(new AttitudeEntry { date = entry.date, delta = entry.delta, reason = entry.reason });
            }
            return copy;
        }

        // ---- changing ----

        /// <summary>
        /// Changes the attitude of the rival by <paramref name="delta"/> × the sensitivity of its archetype, keeps it on the scale, writes the change
        /// to the history with <paramref name="reason"/> (a short English code, at most <see cref="MaxReasonLength"/> characters) and raises
        /// <see cref="LevelChanged"/> when the level changed. True when the change was accepted, even when the attitude was already at the edge of
        /// the scale and did not move (then nothing is written to the history). False for a company without an archetype, for a bankrupt one and
        /// for a delta that is not a number.
        /// </summary>
        public bool Change(int uniqueId, float delta, string reason)
        {
            if (float.IsNaN(delta) || float.IsInfinity(delta)) return false;

            Archetype archetype = _book.ArchetypeOf(uniqueId);
            if (archetype == null || IsFrozen(uniqueId)) return false;

            AttitudeRecord record = FindOrAdd(State, uniqueId);
            float before = ValueOf(record, archetype);
            float sensitivity = archetype.attitude != null ? archetype.attitude.sensitivity : 1f;
            float after = AttitudeMath.Apply(before, delta, sensitivity);
            record.value = after;

            float applied = after - before;
            if (Math.Abs(applied) >= MinRecorded) AddHistory(record, applied, reason);

            RaiseIfLevelChanged(uniqueId, before, after);
            if (after != before) RaiseChanged(uniqueId);
            return true;
        }

        /// <summary>
        /// One month of drift (FR-4 item 3): every saved attitude moves <c>attitude.monthlyDrift</c> (10 %) of the way to the baseline of its
        /// archetype. Companies without an archetype and bankrupt ones are left alone. A value that was never set (NaN) becomes the baseline.
        /// Returns how many values changed.
        /// </summary>
        public int MonthlyDrift()
        {
            RivalState state = State;
            if (state.attitudes == null) return 0;
            float rate = Catalog.Config.attitude.monthlyDrift;

            int changed = 0;
            for (int i = 0; i < state.attitudes.Count; i++)
            {
                AttitudeRecord record = state.attitudes[i];
                if (record == null) continue;

                Archetype archetype = _book.ArchetypeOf(record.uid);
                if (archetype == null || IsFrozen(record.uid)) continue;

                float baseline = BaselineOf(archetype);
                float before = ValueOf(record, archetype);
                float after = AttitudeMath.Drift(before, baseline, rate);
                bool wasUnset = float.IsNaN(record.value);
                record.value = after;
                if (after != before || wasUnset)
                {
                    if (after != before)
                    {
                        changed++;
                        RaiseChanged(record.uid);
                    }
                    RaiseIfLevelChanged(record.uid, before, after);
                }
            }
            return changed;
        }

        // ---- internals ----

        private static float BaselineOf(Archetype archetype)
        {
            return archetype.attitude != null ? AttitudeMath.Clamp(archetype.attitude.baseline) : 0f;
        }

        private static float ValueOf(AttitudeRecord record, Archetype archetype)
        {
            return float.IsNaN(record.value) ? BaselineOf(archetype) : AttitudeMath.Clamp(record.value);
        }

        private static AttitudeRecord Find(RivalState state, int uniqueId)
        {
            List<AttitudeRecord> list = state.attitudes;
            if (list == null) return null;
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i] != null && list[i].uid == uniqueId) return list[i];
            }
            return null;
        }

        private static AttitudeRecord FindOrAdd(RivalState state, int uniqueId)
        {
            AttitudeRecord record = Find(state, uniqueId);
            if (record != null) return record;
            if (state.attitudes == null) state.attitudes = new List<AttitudeRecord>();
            record = new AttitudeRecord { uid = uniqueId };
            state.attitudes.Add(record);
            return record;
        }

        private void AddHistory(AttitudeRecord record, float applied, string reason)
        {
            if (record.history == null) record.history = new List<AttitudeEntry>();

            string text = reason ?? string.Empty;
            if (text.Length > MaxReasonLength) text = text.Substring(0, MaxReasonLength);

            record.history.Add(new AttitudeEntry
            {
                date = _today != null ? RivalDates.ToText(_today()) : string.Empty,
                delta = (float)Math.Round(applied, 2),
                reason = text,
            });

            int size = Catalog.Config.attitude.historySize;
            if (size < 1) size = 1;
            if (record.history.Count > size) record.history.RemoveRange(0, record.history.Count - size);
        }

        private void RaiseChanged(int uniqueId)
        {
            Action<int> handlers = Changed;
            if (handlers == null) return;
            foreach (Delegate handler in handlers.GetInvocationList())
            {
                try
                {
                    ((Action<int>)handler)(uniqueId);
                }
                catch (Exception e)
                {
                    if (_warn != null) _warn("attitude: a handler of Changed failed (" + e.GetType().Name + ": " + e.Message + ")");
                }
            }
        }

        private void RaiseIfLevelChanged(int uniqueId, float before, float after)
        {
            AttitudeLevel from = LevelOf(before);
            AttitudeLevel to = LevelOf(after);
            if (from == to) return;

            Action<int, AttitudeLevel, AttitudeLevel> handlers = LevelChanged;
            if (handlers == null) return;
            foreach (Delegate handler in handlers.GetInvocationList())
            {
                try
                {
                    ((Action<int, AttitudeLevel, AttitudeLevel>)handler)(uniqueId, from, to);
                }
                catch (Exception e)
                {
                    if (_warn != null) _warn("attitude: a handler of LevelChanged failed (" + e.GetType().Name + ": " + e.Message + ")");
                }
            }
        }

        /// <summary>The level name as the string tables spell it: <c>hostile</c>, <c>rivalry</c>, <c>neutral</c>, <c>respect</c>, <c>partnership</c> (the key is <c>rival.level.&lt;name&gt;</c>).</summary>
        public static string LevelKeyName(AttitudeLevel level)
        {
            return level.ToString().ToLower(CultureInfo.InvariantCulture);
        }
    }
}
