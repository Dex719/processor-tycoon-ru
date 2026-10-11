using System;
using System.Collections.Generic;

namespace PTMod.Features.PressReviews
{
    /// <summary>
    /// The hype of the reviewed CPUs (FR-5): for every CPU whose review is less than a year old, the metascore and the day of the review, and the
    /// multiplier <see cref="EffectMath.Hype"/> worked out for the current day. It is a cache, not a state: it is made from the archive of reviews
    /// (<see cref="Rebuild"/>, after a load or a new game) and from new reviews (<see cref="Add"/>), brought up to date once a game day
    /// (<see cref="Refresh"/>) and forgets a record when its year is over. Nothing here is saved. Pure; main thread only.
    /// </summary>
    public sealed class HypeBook
    {
        private sealed class Entry
        {
            public int Metascore;
            public DateTime PublishedOn;
            public float Value;
        }

        private readonly Dictionary<int, Entry> entries = new Dictionary<int, Entry>();

        /// <summary>How many CPUs have a hype now.</summary>
        public int Count
        {
            get { return entries.Count; }
        }

        public void Clear()
        {
            entries.Clear();
        }

        /// <summary>
        /// Remembers the review of a CPU and works out its hype for <paramref name="today"/>. A review that is already over (a year old or more) is
        /// not kept, and a hype that was kept for the CPU is dropped. A newer review of the same CPU replaces the older one. True when the CPU has a hype.
        /// </summary>
        public bool Add(int cpuSaveId, int metascore, DateTime publishedOn, DateTime today, HypeConfig cfg)
        {
            HypeConfig rule = cfg ?? new HypeConfig();

            Entry existing;
            if (entries.TryGetValue(cpuSaveId, out existing) && existing.PublishedOn > publishedOn) return true;   // a newer review stays

            int days = DaysBetween(publishedOn, today);
            if (EffectMath.IsExpired(days, rule.DurationDays))
            {
                entries.Remove(cpuSaveId);
                return false;
            }

            entries[cpuSaveId] = new Entry
            {
                Metascore = metascore,
                PublishedOn = publishedOn,
                Value = EffectMath.Hype(metascore, days, rule),
            };
            return true;
        }

        /// <summary>
        /// Starts the book again from an archive: the reviews that are still within their year at <paramref name="today"/> (the date of a record that does
        /// not parse is skipped). Returns how many CPUs have a hype.
        /// </summary>
        public int Rebuild(IEnumerable<ReviewRecord> reviews, DateTime today, HypeConfig cfg)
        {
            entries.Clear();
            if (reviews == null) return 0;

            foreach (ReviewRecord record in reviews)
            {
                DateTime published;
                if (record == null || !PressDates.TryParse(record.publishedOn, out published)) continue;
                Add(record.cpuSaveId, record.metascore, published, today, cfg);
            }
            return entries.Count;
        }

        /// <summary>
        /// A new game day: the hype of every CPU is worked out again for <paramref name="today"/> and the records whose year is over are removed
        /// (365 days after the review the hype is gone). Returns how many records were removed.
        /// </summary>
        public int Refresh(DateTime today, HypeConfig cfg)
        {
            HypeConfig rule = cfg ?? new HypeConfig();
            List<int> expired = null;
            List<int> ids = new List<int>(entries.Keys);
            for (int i = 0; i < ids.Count; i++)
            {
                Entry entry = entries[ids[i]];
                int days = DaysBetween(entry.PublishedOn, today);
                if (EffectMath.IsExpired(days, rule.DurationDays))
                {
                    if (expired == null) expired = new List<int>();
                    expired.Add(ids[i]);
                    continue;
                }
                entry.Value = EffectMath.Hype(entry.Metascore, days, rule);
            }

            if (expired == null) return 0;
            for (int i = 0; i < expired.Count; i++) entries.Remove(expired[i]);
            return expired.Count;
        }

        /// <summary>The CPU has a hype.</summary>
        public bool Contains(int cpuSaveId)
        {
            return entries.ContainsKey(cpuSaveId);
        }

        /// <summary>The hype multiplier of the CPU as of the last <see cref="Add"/> or <see cref="Refresh"/>, and the metascore it comes from. False for a CPU without a hype.</summary>
        public bool TryGet(int cpuSaveId, out float hype, out int metascore)
        {
            Entry entry;
            if (entries.TryGetValue(cpuSaveId, out entry))
            {
                hype = entry.Value;
                metascore = entry.Metascore;
                return true;
            }
            hype = 1f;
            metascore = 0;
            return false;
        }

        /// <summary>The whole days from the review to today; 0 when today is not after the review.</summary>
        private static int DaysBetween(DateTime publishedOn, DateTime today)
        {
            double days = (today.Date - publishedOn.Date).TotalDays;
            return days <= 0.0 ? 0 : days > int.MaxValue ? int.MaxValue : (int)days;
        }
    }
}
