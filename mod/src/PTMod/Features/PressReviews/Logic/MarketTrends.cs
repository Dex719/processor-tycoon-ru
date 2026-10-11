using System;
using System.Collections.Generic;

namespace PTMod.Features.PressReviews
{
    /// <summary>
    /// The trends of the market the quarterly notes talk about (FR-10, design.md Component 9), worked out over plain numbers: the growth of the leader of a
    /// segment, the change of the median price, and the way both are told to the player. Only the qualitative form ever reaches the player: "about +40 %"
    /// (rounded to tens), "prices are falling". The coefficients of the formulas are never shown. A value that cannot be worked out (no CPU a year ago, a
    /// price of 0) is <see cref="float.NaN"/>, and a condition on it does not hold. Pure.
    /// </summary>
    public static class MarketTrends
    {
        /// <summary>The step of the rounding of a percentage told to the player: tens.</summary>
        public const int PercentStep = 10;

        private const float MaxFraction = 1000f;

        /// <summary>A value that can be compared: not NaN, not infinite.</summary>
        public static bool HasValue(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        /// <summary>The performance of the leader of a segment: the highest <c>Ips</c> among the leading CPUs (the best sellers); NaN when there are none.</summary>
        public static float LeaderIps(IReadOnlyList<CpuSnapshot> leaders)
        {
            float best = float.NaN;
            if (leaders == null) return best;

            for (int i = 0; i < leaders.Count; i++)
            {
                float ips = leaders[i].Ips;
                if (!HasValue(ips) || ips <= 0f) continue;
                if (!HasValue(best) || ips > best) best = ips;
            }
            return best;
        }

        /// <summary>The same over bare numbers.</summary>
        public static float LeaderIps(IReadOnlyList<float> ips)
        {
            float best = float.NaN;
            if (ips == null) return best;

            for (int i = 0; i < ips.Count; i++)
            {
                if (!HasValue(ips[i]) || ips[i] <= 0f) continue;
                if (!HasValue(best) || ips[i] > best) best = ips[i];
            }
            return best;
        }

        /// <summary>The growth of the leader's <c>Ips</c> over a year as a fraction (0.42 is +42 %); NaN when either year has no leader.</summary>
        public static float LeaderIpsGrowth(IReadOnlyList<CpuSnapshot> leadersNow, IReadOnlyList<CpuSnapshot> leadersYearAgo)
        {
            return Change(LeaderIps(leadersNow), LeaderIps(leadersYearAgo));
        }

        /// <summary>The median of the prices (the positive ones); NaN when there are none.</summary>
        public static float Median(IReadOnlyList<float> prices)
        {
            var usable = new List<float>();
            if (prices != null)
            {
                for (int i = 0; i < prices.Count; i++)
                {
                    if (HasValue(prices[i]) && prices[i] > 0f) usable.Add(prices[i]);
                }
            }
            if (usable.Count == 0) return float.NaN;

            usable.Sort();
            int middle = usable.Count / 2;
            return usable.Count % 2 == 1 ? usable[middle] : (usable[middle - 1] + usable[middle]) / 2f;
        }

        /// <summary>The change of the median price over a year as a fraction (−0.15 is −15 %); NaN when either year has no price.</summary>
        public static float MedianPriceChange(IReadOnlyList<float> pricesNow, IReadOnlyList<float> pricesYearAgo)
        {
            return Change(Median(pricesNow), Median(pricesYearAgo));
        }

        /// <summary><c>now / ago − 1</c>; NaN unless both numbers are usable and above 0.</summary>
        public static float Change(float now, float ago)
        {
            if (!HasValue(now) || !HasValue(ago) || now <= 0f || ago <= 0f) return float.NaN;
            return (float)((double)now / ago - 1.0);
        }

        /// <summary>A fraction as whole percent rounded to <see cref="PercentStep"/>, halves away from zero: 0.42 gives 40, −0.13 gives −10, 0.45 gives 50; NaN gives 0.</summary>
        public static int RoundedPercent(float fraction)
        {
            if (!HasValue(fraction)) return 0;
            if (fraction > MaxFraction) fraction = MaxFraction;
            if (fraction < -MaxFraction) fraction = -MaxFraction;

            // decimal, so that 0.45 (a float a hair below it) still rounds like the 45 % a player would expect
            decimal steps = Math.Round((decimal)fraction * 100m / PercentStep, 0, MidpointRounding.AwayFromZero);
            return (int)(steps * PercentStep);
        }

        /// <summary>The growth or change told in tens of percent: <c>about +40 %</c> for 0.42. The text is <c>press.note.qualitative</c>.</summary>
        public static string Qualitative(float value, PressText text)
        {
            if (text == null) throw new ArgumentNullException("text");
            return text("press.note.qualitative", RoundedPercent(value));
        }

        /// <summary>The direction of a change told in a word: <c>rising</c>, <c>falling</c>, or <c>holding steady</c> when it rounds to 0 (under 5 %).</summary>
        public static string QualitativeChange(float value, PressText text)
        {
            if (text == null) throw new ArgumentNullException("text");

            int percent = RoundedPercent(value);
            string key = percent > 0 ? "press.note.change.up" : percent < 0 ? "press.note.change.down" : "press.note.change.flat";
            return text(key);
        }
    }

    /// <summary>
    /// The yearly snapshots of the market the notes compare (<see cref="PressState.yearStats"/>, the last three years × four segments): the leader's <c>Ips</c>
    /// and the median price per segment and year, and the facts about the market they give. Also finds the company that has just entered the press's
    /// field of view. Pure.
    /// </summary>
    public static class MarketStatsBook
    {
        /// <summary>The press must have watched the market for this many months before it calls a company new (a game that has just begun knows nobody).</summary>
        public const int ObservedMonths = 12;

        /// <summary>The snapshot of a year and a segment; null when there is none.</summary>
        public static SegmentYearStat Find(IList<SegmentYearStat> stats, int year, int segment)
        {
            if (stats == null) return null;

            for (int i = 0; i < stats.Count; i++)
            {
                SegmentYearStat stat = stats[i];
                if (stat != null && stat.year == year && stat.segment == segment) return stat;
            }
            return null;
        }

        /// <summary>Is there a snapshot of the year (of any segment)?</summary>
        public static bool HasYear(IList<SegmentYearStat> stats, int year)
        {
            if (stats == null) return false;

            for (int i = 0; i < stats.Count; i++)
            {
                if (stats[i] != null && stats[i].year == year) return true;
            }
            return false;
        }

        /// <summary>
        /// Writes the snapshot of a year and a segment (a snapshot of the same year and segment is replaced) and drops the oldest years beyond
        /// <see cref="PressStateCodec.MaxYearStats"/> entries. A leader or a price that is not usable is stored as 0 (unknown); a segment with neither is not stored.
        /// </summary>
        public static void Upsert(IList<SegmentYearStat> stats, int year, int segment, float leaderIps, float medianPrice)
        {
            if (stats == null) throw new ArgumentNullException("stats");
            if (segment < 0 || segment > 3) return;

            float ips = MarketTrends.HasValue(leaderIps) && leaderIps > 0f ? leaderIps : 0f;
            float price = MarketTrends.HasValue(medianPrice) && medianPrice > 0f ? medianPrice : 0f;
            if (ips <= 0f && price <= 0f) return;

            SegmentYearStat stat = Find(stats, year, segment);
            if (stat == null)
            {
                stat = new SegmentYearStat { year = year, segment = segment };
                stats.Add(stat);
            }
            stat.leaderIps = ips;
            stat.medianPrice = price;
            Trim(stats);
        }

        /// <summary>
        /// Fills the facts of the market for the notes: the growth of each segment's leader and the change of its median price from the year before
        /// <paramref name="latestYear"/> to <paramref name="latestYear"/>. A segment that lacks either snapshot stays NaN.
        /// </summary>
        public static void FillFacts(IList<SegmentYearStat> stats, int latestYear, NoteFacts facts)
        {
            if (facts == null) throw new ArgumentNullException("facts");

            for (int segment = 0; segment < 4; segment++)
            {
                SegmentYearStat now = Find(stats, latestYear, segment);
                SegmentYearStat before = Find(stats, latestYear - 1, segment);
                facts.LeaderGrowth[segment] = now == null || before == null ? float.NaN : MarketTrends.Change(now.leaderIps, before.leaderIps);
                facts.MedianPriceChange[segment] = now == null || before == null ? float.NaN : MarketTrends.Change(now.medianPrice, before.medianPrice);
            }
        }

        /// <summary>
        /// The name of the company (other than <paramref name="excludeKey"/>, the player's) whose first review in the archive was published from
        /// <paramref name="from"/> up to <paramref name="to"/> (the earliest if several); null when there is none. Nobody is new until the archive reaches
        /// back <see cref="ObservedMonths"/> months before <paramref name="to"/>: at the start of a game every company has no review yet.
        /// </summary>
        public static string FindNewEntrant(IList<ReviewRecord> reviews, DateTime from, DateTime to, string excludeKey)
        {
            if (reviews == null || reviews.Count == 0) return null;

            DateTime oldest = DateTime.MaxValue;
            var first = new Dictionary<string, DateTime>(StringComparer.Ordinal);
            var names = new Dictionary<string, string>(StringComparer.Ordinal);
            for (int i = 0; i < reviews.Count; i++)
            {
                ReviewRecord record = reviews[i];
                DateTime date;
                if (record == null || string.IsNullOrEmpty(record.companyKey) || !PressDates.TryParse(record.publishedOn, out date)) continue;

                if (date < oldest) oldest = date;
                DateTime known;
                if (!first.TryGetValue(record.companyKey, out known) || date < known) first[record.companyKey] = date;
                if (!string.IsNullOrEmpty(record.companyName)) names[record.companyKey] = record.companyName;
            }
            if (first.Count == 0 || oldest > to.AddMonths(-ObservedMonths)) return null;

            string bestKey = null;
            DateTime bestDate = DateTime.MaxValue;
            foreach (KeyValuePair<string, DateTime> pair in first)
            {
                if (string.Equals(pair.Key, excludeKey, StringComparison.Ordinal)) continue;
                if (pair.Value < from || pair.Value >= to) continue;
                if (bestKey == null || pair.Value < bestDate || (pair.Value == bestDate && string.CompareOrdinal(pair.Key, bestKey) < 0))
                {
                    bestKey = pair.Key;
                    bestDate = pair.Value;
                }
            }
            if (bestKey == null) return null;

            string name;
            return names.TryGetValue(bestKey, out name) ? name : bestKey;
        }

        private static void Trim(IList<SegmentYearStat> stats)
        {
            while (stats.Count > PressStateCodec.MaxYearStats)
            {
                int oldest = 0;
                for (int i = 1; i < stats.Count; i++)
                {
                    SegmentYearStat a = stats[i];
                    SegmentYearStat b = stats[oldest];
                    if (a.year < b.year || (a.year == b.year && a.segment < b.segment)) oldest = i;
                }
                stats.RemoveAt(oldest);
            }
        }
    }
}
