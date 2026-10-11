using System;
using System.Collections.Generic;
using PTMod.Core;

namespace PTMod.Features.RivalPersonalities
{
    /// <summary>
    /// The pairs "rival and segment" in which a price undercut of the player was counted lately, and until when it is not counted again
    /// (<c>triggerThresholds.undercutCooldownDays</c>, 90; FR-5 item 3). The list lives in the section of the side save file
    /// (<see cref="RivalState.undercutCooldowns"/>). Pure.
    /// </summary>
    public static class UndercutBook
    {
        /// <summary>The pair is cooling down: its entry runs until a day after <paramref name="today"/> or later.</summary>
        public static bool IsCoolingDown(RivalState state, int uniqueId, Segment segment, DateTime today)
        {
            UndercutCooldown entry = Find(state, uniqueId, segment);
            if (entry == null) return false;

            DateTime until;
            if (!RivalDates.TryParse(entry.until, out until)) return false;
            return today.Date < until.Date;
        }

        /// <summary>The undercut was counted on <paramref name="today"/>: the pair is not counted again for <paramref name="days"/> days.</summary>
        public static void Start(RivalState state, int uniqueId, Segment segment, DateTime today, int days)
        {
            if (state == null) return;
            if (state.undercutCooldowns == null) state.undercutCooldowns = new List<UndercutCooldown>();

            UndercutCooldown entry = Find(state, uniqueId, segment);
            if (entry == null)
            {
                entry = new UndercutCooldown { uid = uniqueId, segment = (int)segment };
                state.undercutCooldowns.Add(entry);
            }
            entry.until = RivalDates.ToText(today.Date.AddDays(days < 0 ? 0 : days));
        }

        /// <summary>Removes the entries that ran out (and the ones whose date cannot be read). Returns how many.</summary>
        public static int Prune(RivalState state, DateTime today)
        {
            if (state == null || state.undercutCooldowns == null) return 0;

            int removed = 0;
            for (int i = state.undercutCooldowns.Count - 1; i >= 0; i--)
            {
                UndercutCooldown entry = state.undercutCooldowns[i];
                DateTime until;
                bool keep = entry != null && RivalDates.TryParse(entry.until, out until) && today.Date < until.Date;
                if (keep) continue;
                state.undercutCooldowns.RemoveAt(i);
                removed++;
            }
            return removed;
        }

        private static UndercutCooldown Find(RivalState state, int uniqueId, Segment segment)
        {
            if (state == null || state.undercutCooldowns == null) return null;
            for (int i = 0; i < state.undercutCooldowns.Count; i++)
            {
                UndercutCooldown entry = state.undercutCooldowns[i];
                if (entry != null && entry.uid == uniqueId && entry.segment == (int)segment) return entry;
            }
            return null;
        }
    }

    /// <summary>
    /// The shares of the last full month that the next month is compared with (FR-5 item 2, <see cref="RivalState.shareSnapshot"/>). The
    /// snapshot is of one month, numbered like <see cref="MonthNumber"/>; one saved for another month than the one before the new month is of
    /// no use for the comparison. Pure.
    /// </summary>
    public static class ShareSnapshots
    {
        /// <summary>
        /// The running number of the last full month on <paramref name="today"/> (the month before the one that <paramref name="today"/> is in):
        /// <c>year × 12 + month − 2</c>, so the month after it has a number that is one more. The game side reads the sales of this month at
        /// <c>Count − 2</c> of the monthly lists.
        /// </summary>
        public static int MonthNumber(DateTime today)
        {
            return today.Year * 12 + today.Month - 2;
        }

        /// <summary>The snapshot as a table of shares by company key (a copy); empty for a snapshot without rows.</summary>
        public static Dictionary<string, float[]> ToTable(ShareSnapshot snapshot)
        {
            var table = new Dictionary<string, float[]>(StringComparer.Ordinal);
            if (snapshot == null || snapshot.rows == null) return table;
            foreach (ShareRow row in snapshot.rows)
            {
                if (row == null || string.IsNullOrEmpty(row.key)) continue;
                var shares = new float[ShareCalculator.SegmentCount];
                if (row.s != null) Array.Copy(row.s, shares, Math.Min(row.s.Length, shares.Length));
                table[row.key] = shares;
            }
            return table;
        }

        /// <summary>The snapshot of a month from a table of shares; the rows are sorted by key so the saved file does not change without a reason.</summary>
        public static ShareSnapshot FromTable(int monthNumber, Dictionary<string, float[]> table)
        {
            var snapshot = new ShareSnapshot { monthIndex = monthNumber, rows = new List<ShareRow>() };
            if (table == null) return snapshot;

            var keys = new List<string>(table.Keys);
            keys.Sort(StringComparer.Ordinal);
            foreach (string key in keys)
            {
                float[] shares = table[key];
                var row = new ShareRow { key = key, s = new float[ShareCalculator.SegmentCount] };
                if (shares != null) Array.Copy(shares, row.s, Math.Min(shares.Length, row.s.Length));
                snapshot.rows.Add(row);
            }
            return snapshot;
        }

        /// <summary>The saved snapshot is of the month right before <paramref name="monthNumber"/> and has rows: it can be compared.</summary>
        public static bool IsComparable(ShareSnapshot snapshot, int monthNumber)
        {
            return snapshot != null && snapshot.rows != null && snapshot.rows.Count > 0 && snapshot.monthIndex == monthNumber - 1;
        }
    }

    /// <summary>The caution of AI companies after a public design flaw (FR-17, <see cref="RivalState.cautions"/>): the monthly pass drops the entries that ran out. Pure.</summary>
    public static class CautionCleaner
    {
        /// <summary>
        /// Removes the entries whose day <c>until</c> has come (the caution lasts while <c>today &lt; until</c>) and the ones whose date cannot be
        /// read. Returns how many were removed.
        /// </summary>
        public static int RemoveExpired(RivalState state, DateTime today)
        {
            if (state == null || state.cautions == null) return 0;

            int removed = 0;
            for (int i = state.cautions.Count - 1; i >= 0; i--)
            {
                CautionEntry entry = state.cautions[i];
                DateTime until;
                bool keep = entry != null && RivalDates.TryParse(entry.until, out until) && today.Date < until.Date;
                if (keep) continue;
                state.cautions.RemoveAt(i);
                removed++;
            }
            return removed;
        }
    }
}
