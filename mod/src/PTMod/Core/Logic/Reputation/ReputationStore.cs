using System;
using System.Collections.Generic;
using System.Globalization;
using PTMod.Core.Logic.Companies;

namespace PTMod.Core
{
    /// <summary>
    /// One entry of a company's reputation history: written by <c>Add</c> and <c>Set</c> only, never by the monthly drift.
    /// For <c>Add</c> the <see cref="Delta"/> is the requested change (so +10 on 95 is recorded as +10 with <see cref="Value"/> 100);
    /// for <c>Set</c> it is the effective change.
    /// </summary>
    public readonly struct ReputationChange
    {
        public ReputationChange(DateTime date, float delta, float value, string reason)
        {
            Date = date;
            Delta = delta;
            Value = value;
            Reason = reason ?? string.Empty;
        }

        /// <summary>Game date of the change.</summary>
        public DateTime Date { get; }

        public float Delta { get; }

        /// <summary>Reputation right after the change.</summary>
        public float Value { get; }

        /// <summary>Reason key (an L10n key of the feature that wrote it) or free text; never null.</summary>
        public string Reason { get; }
    }
}

namespace PTMod.Core.Logic.Reputation
{
    /// <summary>
    /// The state and rules of brand reputation (FR-13), without Unity and without game objects: companies are
    /// identified by <see cref="CompanyKey"/> strings (<c>player</c>, <c>ai:&lt;UniqueID&gt;</c>) and the date comes from the caller.
    /// The runtime facade <c>ModReputation</c> (TSK-026) resolves <c>ICompany</c> to keys, supplies the game date and
    /// the settings (<c>reputation.historyPerCompany</c>, <c>driftMonthlyRate</c>, <c>driftTarget</c>), converts to and
    /// from the side file section and routes warnings to <c>ModLog</c>.
    /// <list type="bullet">
    /// <item>Value range [0, 100]; a company without a stored value has <see cref="Default"/> (50), so a new AI company needs no registration.</item>
    /// <item><see cref="Add"/> and <see cref="Set"/> clamp, write one history entry and raise <see cref="Changed"/>. Invalid input
    /// (bad key, NaN, infinity) changes nothing and is reported through the warning sink.</item>
    /// <item><see cref="Drift"/> and <see cref="DriftAll"/> move values towards a target without history and without <see cref="Changed"/>
    /// (history keeps only significant entries, D22); <see cref="DriftAll"/> raises <see cref="Drifted"/> once after the whole pass.</item>
    /// <item>History is limited to <see cref="HistoryLimit"/> newest entries per company.</item>
    /// </list>
    /// Events are raised after the state is updated; an exception in a subscriber propagates to the caller
    /// (the facade subscribes through <c>ModHooks.Raise</c>, which isolates subscribers). Not thread-safe: main thread only.
    /// </summary>
    public sealed class ReputationStore
    {
        public const float Min = 0f;
        public const float Max = 100f;
        public const float Default = 50f;

        /// <summary>History length per company when the settings do not say otherwise (<c>reputation.historyPerCompany</c>).</summary>
        public const int DefaultHistoryLimit = 50;

        /// <summary>A drifted value closer to the target than this snaps to the target, so the exponential tail ends.</summary>
        public const float DriftSnap = 0.05f;

        private const int MaxHistoryLimit = 1000;
        private const int MaxShownKey = 40;

        private static readonly IReadOnlyList<ReputationChange> NoHistory = new ReputationChange[0];

        private sealed class Entry
        {
            public float Value = Default;
            public readonly List<ReputationChange> History = new List<ReputationChange>();
        }

        private readonly Dictionary<string, Entry> entries = new Dictionary<string, Entry>(StringComparer.Ordinal);
        private readonly Action<string> warn;
        private int historyLimit;

        /// <param name="historyLimit">Entries kept per company; clamped to [1, 1000].</param>
        /// <param name="warn">Receives the text of every ignored call (English, one line); null drops the warnings.</param>
        public ReputationStore(int historyLimit = DefaultHistoryLimit, Action<string> warn = null)
        {
            this.historyLimit = ClampLimit(historyLimit);
            this.warn = warn;
        }

        /// <summary>Raised by <see cref="Add"/> and <see cref="Set"/> only: key, old value, new value, reason.</summary>
        public event Action<string, float, float, string> Changed;

        /// <summary>Raised once at the end of every <see cref="DriftAll"/> pass.</summary>
        public event Action Drifted;

        public int HistoryLimit
        {
            get { return historyLimit; }
        }

        /// <summary>Number of companies with a stored value or history (others read as <see cref="Default"/>).</summary>
        public int Count
        {
            get { return entries.Count; }
        }

        /// <summary>Changes the history length; longer histories are cut to the newest entries at once.</summary>
        public void SetHistoryLimit(int limit)
        {
            historyLimit = ClampLimit(limit);
            foreach (Entry entry in entries.Values) Trim(entry);
        }

        /// <summary>Current reputation; <see cref="Default"/> for a company that has none and for an invalid key.</summary>
        public float Get(string key)
        {
            Entry entry;
            return key != null && entries.TryGetValue(key, out entry) ? entry.Value : Default;
        }

        /// <summary>
        /// Changes the reputation by <paramref name="delta"/>, clamped to [<see cref="Min"/>, <see cref="Max"/>], writes a history
        /// entry and raises <see cref="Changed"/>. Returns false (nothing changed, a warning is issued) for an invalid key
        /// or a delta that is NaN or infinite.
        /// </summary>
        public bool Add(string key, float delta, string reason, DateTime date)
        {
            if (!CheckKey(key, "Add")) return false;
            if (!IsFinite(delta))
            {
                Warn("Add ignored: delta " + Show(delta) + " is not a finite number (company " + Show(key) + ")");
                return false;
            }

            float old = Get(key);
            float value = Clamp((double)old + delta);
            Record(key, value, new ReputationChange(date, delta, value, reason));
            RaiseChanged(key, old, value, reason);
            return true;
        }

        /// <summary>
        /// Sets the reputation to <paramref name="value"/> (clamped), writes a history entry with the effective change and
        /// raises <see cref="Changed"/>. Returns false for an invalid key or a value that is NaN or infinite.
        /// </summary>
        public bool Set(string key, float value, string reason, DateTime date)
        {
            if (!CheckKey(key, "Set")) return false;
            if (!IsFinite(value))
            {
                Warn("Set ignored: value " + Show(value) + " is not a finite number (company " + Show(key) + ")");
                return false;
            }

            float old = Get(key);
            float clamped = Clamp(value);
            Record(key, clamped, new ReputationChange(date, clamped - old, clamped, reason));
            RaiseChanged(key, old, clamped, reason);
            return true;
        }

        /// <summary>
        /// Moves one company towards <paramref name="target"/> by the share <paramref name="rate"/> of the distance
        /// (<c>v += (target - v) * rate</c>); a result closer to the target than <see cref="DriftSnap"/> becomes the target.
        /// No history entry, no <see cref="Changed"/>, no <see cref="Drifted"/>. Returns true when the value changed;
        /// false (with a warning for bad arguments) otherwise. The rate must be a finite number in [0, 1], the target is clamped to [0, 100].
        /// </summary>
        public bool Drift(string key, float rate, float target = Default)
        {
            if (!CheckDriftArguments(rate, target, out target)) return false;
            if (!CheckKey(key, "Drift")) return false;
            return DriftCore(key, rate, target);
        }

        /// <summary>
        /// One monthly pass: <see cref="Drift"/> for every key of the list (invalid keys are skipped with a warning), then
        /// <see cref="Drifted"/> once. Allocation-free for a valid list. Returns the number of companies whose value changed;
        /// a null list or bad arguments do nothing (and raise nothing).
        /// </summary>
        public int DriftAll(IReadOnlyList<string> keys, float rate, float target = Default)
        {
            if (keys == null) return 0;
            if (!CheckDriftArguments(rate, target, out target)) return 0;

            int changed = 0;
            for (int i = 0; i < keys.Count; i++)
            {
                string key = keys[i];
                if (!CheckKey(key, "Drift")) continue;
                if (DriftCore(key, rate, target)) changed++;
            }

            Action handler = Drifted;
            if (handler != null) handler();
            return changed;
        }

        /// <summary>The newest-last history of the company (a snapshot copy); empty for an unknown company and an invalid key.</summary>
        public IReadOnlyList<ReputationChange> History(string key)
        {
            Entry entry;
            if (key == null || !entries.TryGetValue(key, out entry) || entry.History.Count == 0) return NoHistory;
            return entry.History.ToArray();
        }

        /// <summary>Keys with a stored value or history, in ordinal order (stable output for the side file).</summary>
        public IReadOnlyList<string> Keys()
        {
            var keys = new List<string>(entries.Keys);
            keys.Sort(StringComparer.Ordinal);
            return keys;
        }

        /// <summary>
        /// Restores one company from the side file: replaces its value and history. A value outside [0, 100] is clamped, history
        /// entries with a NaN or infinite number are dropped, only the newest <see cref="HistoryLimit"/> entries are kept.
        /// Returns false (with a warning) for an invalid key or a non-finite value. No events are raised.
        /// </summary>
        public bool Load(string key, float value, IEnumerable<ReputationChange> history)
        {
            if (!CheckKey(key, "Load")) return false;
            if (!IsFinite(value))
            {
                Warn("Load ignored: value " + Show(value) + " is not a finite number (company " + Show(key) + ")");
                return false;
            }

            var entry = new Entry { Value = Clamp(value) };
            if (history != null)
            {
                foreach (ReputationChange change in history)
                {
                    if (!IsFinite(change.Delta) || !IsFinite(change.Value))
                    {
                        Warn("Load: a history entry of " + Show(key) + " with a non-finite number was dropped");
                        continue;
                    }
                    entry.History.Add(new ReputationChange(change.Date, change.Delta, Clamp(change.Value), change.Reason));
                }
            }
            Trim(entry);

            if (entry.Value == Default && entry.History.Count == 0) entries.Remove(key);
            else entries[key] = entry;
            return true;
        }

        /// <summary>Forgets everything (exit to the menu, start of a new game, before a save is loaded). No events.</summary>
        public void Clear()
        {
            entries.Clear();
        }

        // ---- internals ----

        private bool DriftCore(string key, float rate, float target)
        {
            Entry entry;
            entries.TryGetValue(key, out entry);
            float old = entry != null ? entry.Value : Default;

            double next = old + ((double)target - old) * rate;
            if (Math.Abs(next - target) < DriftSnap) next = target;
            float value = Clamp(next);
            if (value == old) return false;

            if (entry == null)
            {
                entry = new Entry();
                entries[key] = entry;
            }
            entry.Value = value;

            // A company that drifted back to the default with no history carries no information: do not keep it in the save.
            if (value == Default && entry.History.Count == 0) entries.Remove(key);
            return true;
        }

        private void Record(string key, float value, ReputationChange change)
        {
            Entry entry;
            if (!entries.TryGetValue(key, out entry))
            {
                entry = new Entry();
                entries[key] = entry;
            }
            entry.Value = value;
            entry.History.Add(change);
            Trim(entry);
        }

        private void Trim(Entry entry)
        {
            int extra = entry.History.Count - historyLimit;
            if (extra > 0) entry.History.RemoveRange(0, extra);
        }

        private void RaiseChanged(string key, float old, float value, string reason)
        {
            Action<string, float, float, string> handler = Changed;
            if (handler != null) handler(key, old, value, reason ?? string.Empty);
        }

        private bool CheckKey(string key, string operation)
        {
            if (CompanyKey.IsValid(key)) return true;
            Warn(operation + " ignored: " + Show(key) + " is not a company key (expected \"player\" or \"ai:<number>\")");
            return false;
        }

        private bool CheckDriftArguments(float rate, float target, out float clampedTarget)
        {
            clampedTarget = target;
            if (!IsFinite(rate) || rate < 0f || rate > 1f)
            {
                Warn("Drift ignored: rate " + Show(rate) + " is not a number in [0, 1]");
                return false;
            }
            if (!IsFinite(target))
            {
                Warn("Drift ignored: target " + Show(target) + " is not a finite number");
                return false;
            }
            clampedTarget = Clamp(target);
            return true;
        }

        private void Warn(string message)
        {
            Action<string> sink = warn;
            if (sink != null) sink("reputation: " + message);
        }

        private static float Clamp(double value)
        {
            if (value < Min) return Min;
            if (value > Max) return Max;
            return (float)value;
        }

        private static int ClampLimit(int limit)
        {
            if (limit < 1) return 1;
            return limit > MaxHistoryLimit ? MaxHistoryLimit : limit;
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private static string Show(float value)
        {
            return value.ToString("R", CultureInfo.InvariantCulture);
        }

        private static string Show(string key)
        {
            if (key == null) return "null";
            string text = key.Length > MaxShownKey ? key.Substring(0, MaxShownKey) + "..." : key;
            return "\"" + text + "\"";
        }
    }
}
