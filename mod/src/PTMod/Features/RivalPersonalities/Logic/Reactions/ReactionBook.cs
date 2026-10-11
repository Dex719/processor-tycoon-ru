using System;
using System.Collections.Generic;
using System.Globalization;

namespace PTMod.Features.RivalPersonalities
{
    /// <summary>
    /// The words of <see cref="HistoryEntry.outcome"/>: what became of a reaction (design.md, "DTO состояния"). The panel of the intelligence
    /// window (TSK-018) reads them, so they must not change. A choice of the player in <c>Poach</c> is written as <c>counterOffer</c> or
    /// <c>letGo</c>, with <see cref="AutoSuffix"/> when the timer or an AI company decided.
    /// </summary>
    public static class HistoryOutcomes
    {
        public const string Planned = "planned";
        public const string Started = "started";
        public const string Ended = "ended";
        public const string Cancelled = "cancelled";
        public const string CounterLaunchDone = "counterLaunchDone";
        public const string Revealed = "revealed";
        public const string CounterOffer = "counterOffer";
        public const string LetGo = "letGo";
        public const string AutoSuffix = ":auto";
    }

    /// <summary>
    /// The lists of the reactions in the section of the side save file (FR-6, FR-7, FR-10): what is planned (<see cref="RivalState.pending"/>), what is in
    /// effect (<see cref="RivalState.active"/>), the counters of the honesty limits (<see cref="RivalState.counters"/>) and the history of the
    /// reactions (<see cref="RivalState.history"/>, the last <see cref="MaxHistory"/>). The planner and the executor work through it, so both see
    /// one state. The section is asked from the provider at every call (a load replaces the object, never keep it). Pure; main thread only.
    /// <para>
    /// Every date is a text <c>yyyy-MM-dd</c> (<see cref="RivalDates"/>). A date that cannot be read counts as "never": the reader gives
    /// <see cref="int.MaxValue"/> days or no date, and the caller decides (the planner drops such a record with a log line).
    /// </para>
    /// </summary>
    public sealed class ReactionBook
    {
        /// <summary>The longest history: the last 100 entries of all rivals.</summary>
        public const int MaxHistory = 100;

        /// <summary>The most reactions that are planned at one time (the caps of FR-6 are far below it; this only bounds a damaged save).</summary>
        public const int MaxPending = 20;

        /// <summary>The most reactions in effect at one time.</summary>
        public const int MaxActive = 20;

        /// <summary>Asks <see cref="HasHistory(int, string, string, DateTime, int)"/> for the lines of every segment (a real segment is -1..3).</summary>
        public const int AnySegment = int.MinValue;

        /// <summary>The ids of the reactions of the feature are <c>r-&lt;n&gt;</c>.</summary>
        public const string IdPrefix = PriceCutMath.InternalPrefix;

        private readonly Func<RivalState> _state;

        /// <param name="state">The section as it is now (never keep the object: a load replaces it); null gives an empty state that nothing keeps.</param>
        public ReactionBook(Func<RivalState> state)
        {
            _state = state;
        }

        private RivalState State
        {
            get
            {
                RivalState state = _state != null ? _state() : null;
                return state ?? new RivalState();
            }
        }

        /// <summary>The first day of the grace period as the section stores it (<c>yyyy-MM-dd</c>); empty while it has not started.</summary>
        public string GraceStart
        {
            get { return State.graceStart ?? string.Empty; }
        }

        // ---- ids ----

        /// <summary>The id of the next reaction, <c>r-&lt;n&gt;</c>; the counter of the section moves on (the id is never used twice, also after a load).</summary>
        public string NextId()
        {
            RivalState state = State;
            int sequence = state.nextReactionSeq < 1 ? 1 : state.nextReactionSeq;
            state.nextReactionSeq = sequence + 1;
            return IdPrefix + sequence.ToString(CultureInfo.InvariantCulture);
        }

        // ---- the planned reactions ----

        /// <summary>The planned reactions, a copy (the planner changes the list while it walks it).</summary>
        public List<PendingReaction> PendingSnapshot()
        {
            return new List<PendingReaction>(Live(State.pending));
        }

        public PendingReaction FindPending(string reactionId)
        {
            if (string.IsNullOrEmpty(reactionId)) return null;
            foreach (PendingReaction pending in Live(State.pending))
            {
                if (string.Equals(pending.id, reactionId, StringComparison.Ordinal)) return pending;
            }
            return null;
        }

        /// <summary>The reaction whose intelligence mail has this id (<see cref="PendingReaction.mailId"/>), or null.</summary>
        public PendingReaction FindPendingByMail(int mailId)
        {
            if (mailId <= 0) return null;
            foreach (PendingReaction pending in Live(State.pending))
            {
                if (pending.mailId == mailId) return pending;
            }
            return null;
        }

        /// <summary>Adds a planned reaction. False when the id is used already or <see cref="MaxPending"/> reactions wait.</summary>
        public bool AddPending(PendingReaction pending)
        {
            if (pending == null || string.IsNullOrEmpty(pending.id) || FindPending(pending.id) != null) return false;

            RivalState state = State;
            if (state.pending == null) state.pending = new List<PendingReaction>();
            if (state.pending.Count >= MaxPending) return false;
            state.pending.Add(pending);
            return true;
        }

        public bool RemovePending(string reactionId)
        {
            List<PendingReaction> list = State.pending;
            if (list == null || string.IsNullOrEmpty(reactionId)) return false;
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i] != null && string.Equals(list[i].id, reactionId, StringComparison.Ordinal))
                {
                    list.RemoveAt(i);
                    return true;
                }
            }
            return false;
        }

        /// <summary>The rival has a reaction that is planned and not carried out yet.</summary>
        public bool HasPending(int uniqueId)
        {
            foreach (PendingReaction pending in Live(State.pending))
            {
                if (pending.uid == uniqueId) return true;
            }
            return false;
        }

        // ---- the reactions in effect ----

        /// <summary>The reactions in effect, a copy.</summary>
        public List<ActiveReaction> ActiveSnapshot()
        {
            return new List<ActiveReaction>(Live(State.active));
        }

        public ActiveReaction FindActive(string reactionId)
        {
            if (string.IsNullOrEmpty(reactionId)) return null;
            foreach (ActiveReaction active in Live(State.active))
            {
                if (string.Equals(active.id, reactionId, StringComparison.Ordinal)) return active;
            }
            return null;
        }

        /// <summary>The reactions of one rival that are in effect, a new list.</summary>
        public List<ActiveReaction> ActiveOf(int uniqueId)
        {
            var result = new List<ActiveReaction>();
            foreach (ActiveReaction active in Live(State.active))
            {
                if (active.uid == uniqueId) result.Add(active);
            }
            return result;
        }

        /// <summary>
        /// Starts a reaction: it is in effect from <paramref name="start"/> to <paramref name="end"/> (the day it ends; the same day for one that
        /// takes no time). Returns the record, the existing one when the id is known already, null when <see cref="MaxActive"/> reactions are in effect.
        /// </summary>
        public ActiveReaction BeginActive(string reactionId, int uniqueId, ReactionType type, int segment, DateTime start, DateTime end)
        {
            if (string.IsNullOrEmpty(reactionId)) return null;
            ActiveReaction existing = FindActive(reactionId);
            if (existing != null) return existing;

            RivalState state = State;
            if (state.active == null) state.active = new List<ActiveReaction>();
            if (state.active.Count >= MaxActive) return null;

            var record = new ActiveReaction
            {
                id = reactionId,
                uid = uniqueId,
                type = ReactionKinds.NameOf(type),
                segment = segment,
                start = RivalDates.ToText(start),
                end = RivalDates.ToText(end < start ? start : end),
            };
            state.active.Add(record);
            return record;
        }

        public bool RemoveActive(string reactionId)
        {
            List<ActiveReaction> list = State.active;
            if (list == null || string.IsNullOrEmpty(reactionId)) return false;
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i] != null && string.Equals(list[i].id, reactionId, StringComparison.Ordinal))
                {
                    list.RemoveAt(i);
                    return true;
                }
            }
            return false;
        }

        // ---- planned or in effect ----

        /// <summary>
        /// A reaction of this type is planned or in effect for the rival (FR-6 item 8: the effects on <c>contract.signChance</c> and
        /// <c>research.speed</c> do not pile up on themselves).
        /// </summary>
        public bool HasInFlight(int uniqueId, ReactionType type)
        {
            string name = ReactionKinds.NameOf(type);
            foreach (PendingReaction pending in Live(State.pending))
            {
                if (pending.uid == uniqueId && string.Equals(pending.type, name, StringComparison.Ordinal)) return true;
            }
            foreach (ActiveReaction active in Live(State.active))
            {
                if (active.uid == uniqueId && string.Equals(active.type, name, StringComparison.Ordinal)) return true;
            }
            return false;
        }

        /// <summary>
        /// The hostile reactions that are planned or in effect now, all rivals together (the cap of two at a time, FR-6 item 4). A type that is not
        /// known counts as hostile.
        /// </summary>
        public int HostileInFlight()
        {
            int count = 0;
            RivalState state = State;
            foreach (PendingReaction pending in Live(state.pending))
            {
                if (IsHostileName(pending.type)) count++;
            }
            foreach (ActiveReaction active in Live(state.active))
            {
                if (IsHostileName(active.type)) count++;
            }
            return count;
        }

        // ---- the counters of the limits ----

        /// <summary>The hostile reactions carried out in the game year of <paramref name="today"/> (0 when the counter belongs to another year).</summary>
        public int HostileExecutedThisYear(DateTime today)
        {
            Counters counters = State.counters;
            if (counters == null || counters.year != today.Year) return 0;
            return counters.hostileExecutedThisYear < 0 ? 0 : counters.hostileExecutedThisYear;
        }

        /// <summary>A new game year starts the count again (<c>TickPhase.YearPassed</c>; every other call of the book does the same by itself). True when the counter was reset.</summary>
        public bool RollYear(DateTime today)
        {
            RivalState state = State;
            if (state.counters == null) state.counters = new Counters();
            if (state.counters.year == today.Year) return false;
            state.counters.year = today.Year;
            state.counters.hostileExecutedThisYear = 0;
            return true;
        }

        /// <summary>
        /// A reaction was carried out on <paramref name="today"/>: the cooldown of the rival starts and, for a hostile one, the count of the year
        /// goes up (FR-6 items 3 and 4).
        /// </summary>
        public void MarkExecuted(int uniqueId, DateTime today, bool hostile)
        {
            RollYear(today);
            RivalState state = State;
            if (hostile)
            {
                state.counters.hostileExecutedThisYear++;
                IncrementExecuted(state.counters, uniqueId);
            }

            if (state.counters.lastByUid == null) state.counters.lastByUid = new List<LastReaction>();
            SetLast(state.counters.lastByUid, uniqueId, today);
        }

        /// <summary>
        /// How many reactions the rival has carried out so far (all game years): the counter of <see cref="MarkExecuted"/> for a hostile reaction. 0
        /// for a rival that has never carried one out. The friendly offer of <see cref="MarkPartnerOffer"/> is not counted.
        /// </summary>
        public int ExecutedBy(int uniqueId)
        {
            Counters counters = State.counters;
            if (counters == null || counters.executed == null) return 0;
            foreach (ExecutedCount item in counters.executed)
            {
                if (item != null && item.uid == uniqueId) return item.count < 0 ? 0 : item.count;
            }
            return 0;
        }

        /// <summary>The reactions all rivals have carried out so far (the sum of <see cref="ExecutedBy"/>).</summary>
        public int ExecutedTotal()
        {
            Counters counters = State.counters;
            if (counters == null || counters.executed == null) return 0;
            long total = 0;
            foreach (ExecutedCount item in counters.executed)
            {
                if (item != null && item.count > 0) total += item.count;
            }
            return total > int.MaxValue ? int.MaxValue : (int)total;
        }

        // ---- the friendly offers (FR-12) ----

        /// <summary>A foundry sent its offer on <paramref name="today"/>: the next one waits for <c>repeatMonths</c> (FR-12 item 2).</summary>
        public void MarkPartnerOffer(int uniqueId, DateTime today)
        {
            RivalState state = State;
            if (state.counters == null) state.counters = new Counters();
            if (state.counters.lastPartnerByUid == null) state.counters.lastPartnerByUid = new List<LastReaction>();
            SetLast(state.counters.lastPartnerByUid, uniqueId, today);
        }

        /// <summary>
        /// Whole calendar months from the last offer of the foundry to <paramref name="today"/> (<c>year × 12 + month</c> difference, so an offer sent on
        /// 1990-03-01 is 6 months old on 1990-09-01); <see cref="int.MaxValue"/> when it never made one or the date cannot be read. Never negative.
        /// </summary>
        public int MonthsSincePartnerOffer(int uniqueId, DateTime today)
        {
            Counters counters = State.counters;
            if (counters == null || counters.lastPartnerByUid == null) return int.MaxValue;

            foreach (LastReaction item in counters.lastPartnerByUid)
            {
                if (item == null || item.uid != uniqueId) continue;
                DateTime date;
                if (!RivalDates.TryParse(item.date, out date)) return int.MaxValue;
                int months = (today.Year * 12 + today.Month) - (date.Year * 12 + date.Month);
                return months < 0 ? 0 : months;
            }
            return int.MaxValue;
        }

        /// <summary>
        /// Whole days since the last reaction of the rival was carried out; <see cref="int.MaxValue"/> when it never had one (or the date cannot be
        /// read). Never negative.
        /// </summary>
        public int DaysSinceLastReaction(int uniqueId, DateTime today)
        {
            Counters counters = State.counters;
            if (counters == null || counters.lastByUid == null) return int.MaxValue;

            foreach (LastReaction item in counters.lastByUid)
            {
                if (item == null || item.uid != uniqueId) continue;
                DateTime date;
                if (!RivalDates.TryParse(item.date, out date)) return int.MaxValue;
                int days = RivalDates.DaysBetween(date, today);
                return days < 0 ? 0 : days;
            }
            return int.MaxValue;
        }

        // ---- the history ----

        /// <summary>Writes one line of the history (a word of <see cref="HistoryOutcomes"/>); the oldest lines beyond <see cref="MaxHistory"/> go.</summary>
        public void AddHistory(int uniqueId, DateTime date, string type, int segment, string outcome)
        {
            RivalState state = State;
            if (state.history == null) state.history = new List<HistoryEntry>();
            state.history.Add(new HistoryEntry
            {
                uid = uniqueId,
                date = RivalDates.ToText(date),
                type = type ?? string.Empty,
                segment = segment,
                outcome = outcome ?? string.Empty,
            });
            if (state.history.Count > MaxHistory) state.history.RemoveRange(0, state.history.Count - MaxHistory);
        }

        /// <summary>The last <paramref name="max"/> lines of the rival's history, oldest first. A new list.</summary>
        public List<HistoryEntry> HistoryOf(int uniqueId, int max)
        {
            var all = new List<HistoryEntry>();
            foreach (HistoryEntry entry in Live(State.history))
            {
                if (entry.uid == uniqueId) all.Add(entry);
            }
            if (max >= 0 && all.Count > max) all.RemoveRange(0, all.Count - max);
            return all;
        }

        /// <summary>The history has a line of this rival, type and outcome from <paramref name="since"/> on (the dates are compared by day).</summary>
        public bool HasHistory(int uniqueId, string type, string outcome, DateTime since)
        {
            return HasHistory(uniqueId, type, outcome, since, AnySegment);
        }

        /// <summary>The same, for the lines of one segment only (<see cref="AnySegment"/> asks for every segment).</summary>
        public bool HasHistory(int uniqueId, string type, string outcome, DateTime since, int segment)
        {
            foreach (HistoryEntry entry in Live(State.history))
            {
                if (entry.uid != uniqueId) continue;
                if (!string.Equals(entry.type, type, StringComparison.Ordinal) || !string.Equals(entry.outcome, outcome, StringComparison.Ordinal)) continue;
                if (segment != AnySegment && entry.segment != segment) continue;
                DateTime date;
                if (RivalDates.TryParse(entry.date, out date) && date.Date >= since.Date) return true;
            }
            return false;
        }

        // ---- internals ----

        private static void IncrementExecuted(Counters counters, int uniqueId)
        {
            if (counters.executed == null) counters.executed = new List<ExecutedCount>();
            foreach (ExecutedCount item in counters.executed)
            {
                if (item == null || item.uid != uniqueId) continue;
                if (item.count < int.MaxValue) item.count = item.count < 0 ? 1 : item.count + 1;
                return;
            }
            counters.executed.Add(new ExecutedCount { uid = uniqueId, count = 1 });
        }

        /// <summary>Writes the day of a rival into a list "uid and date", adding the record when the rival has none.</summary>
        private static void SetLast(List<LastReaction> list, int uniqueId, DateTime today)
        {
            LastReaction last = null;
            foreach (LastReaction item in list)
            {
                if (item != null && item.uid == uniqueId)
                {
                    last = item;
                    break;
                }
            }
            if (last == null)
            {
                last = new LastReaction { uid = uniqueId };
                list.Add(last);
            }
            last.date = RivalDates.ToText(today);
        }

        private static bool IsHostileName(string typeName)
        {
            ReactionType type;
            return !ReactionKinds.TryParseReaction(typeName, out type) || ReactionKinds.IsHostile(type);
        }

        /// <summary>The entries of a list without the null ones, in a form that is safe to walk (a list that does not exist is empty).</summary>
        private static IEnumerable<T> Live<T>(List<T> list) where T : class
        {
            if (list == null) yield break;
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i] != null) yield return list[i];
            }
        }
    }
}
