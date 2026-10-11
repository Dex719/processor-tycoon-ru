using System;
using System.Collections.Generic;

namespace PTMod.Features.CpuArchitectures
{
    /// <summary>
    /// One line of the section "Экосистема" of the panel (FR-11 item 4): a family of the catalog with its share of the sales of the last twelve full months,
    /// shown as a whole percent (the percents of all the lines add up to 100) and the change against the year before in percentage points.
    /// </summary>
    public sealed class EcosystemRow
    {
        public string FamilyId;

        /// <summary><c>E</c>: the share in the sales, 0..1.</summary>
        public float Share;

        /// <summary>The share as a whole percent, rounded so that the lines of the section add up to 100.</summary>
        public int Percent;

        /// <summary>The share now minus the share a year ago, in percentage points (signed, rounded).</summary>
        public int ChangePoints;
    }

    /// <summary>
    /// The arithmetic of the ecosystem of an architecture (FR-11, stage 2; design.md Component 11, "Экосистема"): the share of a family in the sales of CPUs
    /// over a window of full months.
    /// <code>
    /// sales_a = Σ over the CPUs of family a, over the window, of (Sold[i] + SoldByContracts[i])      i = Count−2 … Count−1−windowMonths
    /// E_a     = sales_a / Σ_b sales_b            no sales at all: E(fallback) = 1, the others 0
    /// f_eco   = 0.95 + 0.10 × √E                 (<see cref="ArchRatingMath.EcosystemFactor"/>)
    /// </code>
    /// The monthly series of a CPU in the game (<c>CpuBase.MarketData</c>) start at the beginning of the game and end with the month that is running now,
    /// so the last full month is <c>Count − 2</c> (the same window as <c>MarketDataProvider.TakeSoldAtPeriod</c>). Pure: the series come in as lists.
    /// </summary>
    public static class EcosystemMath
    {
        /// <summary>The year in months: the distance of the window "a year ago".</summary>
        public const int YearMonths = 12;

        /// <summary>
        /// The units sold over the last <paramref name="windowMonths"/> full months, market and contracts together, <paramref name="offsetMonths"/> months back
        /// (0: the last full month is the first of the window; 12: the window of a year ago). Months before the start of a series count 0, a missing series
        /// counts 0, a negative entry counts 0.
        /// </summary>
        public static long WindowSum(IReadOnlyList<int> sold, IReadOnlyList<int> contracts, int windowMonths, int offsetMonths = 0)
        {
            return SumLast(sold, windowMonths, offsetMonths) + SumLast(contracts, windowMonths, offsetMonths);
        }

        /// <summary>The same for a series of one kind (the sales of a segment, the income): the last full months of one list.</summary>
        public static long SumLast(IReadOnlyList<int> series, int windowMonths, int offsetMonths = 0)
        {
            if (series == null || windowMonths <= 0) return 0;

            long sum = 0;
            int last = series.Count - 2 - Math.Max(0, offsetMonths);   // Count − 1 is the month that has just opened
            for (int k = 0; k < windowMonths; k++)
            {
                int index = last - k;
                if (index < 0) break;
                int value = series[index];
                if (value > 0) sum += value;
            }
            return sum;
        }

        /// <summary>The income of the last full month of a series (<c>CpuBase.MarketData.Income[Count − 2]</c>); 0 for a missing series, a series that has no full month yet and an unusable value.</summary>
        public static float LastMonth(IReadOnlyList<float> series)
        {
            if (series == null || series.Count < 2) return 0f;
            float value = series[series.Count - 2];
            return float.IsNaN(value) || float.IsInfinity(value) || value < 0f ? 0f : value;
        }

        /// <summary>There were sales in the window (some family has more than 0 units).</summary>
        public static bool HasSales(IReadOnlyDictionary<string, long> salesByFamily)
        {
            if (salesByFamily == null) return false;
            foreach (KeyValuePair<string, long> pair in salesByFamily)
            {
                if (pair.Value > 0) return true;
            }
            return false;
        }

        /// <summary>
        /// The shares of the families (FR-11 items 1 and 3): the sales of a family over all the sales; the families without sales are not in the result (their
        /// share is 0). With no sales at all the fallback family has the whole ecosystem (the early game: everybody has an ISA of his own).
        /// </summary>
        public static Dictionary<string, float> Shares(IReadOnlyDictionary<string, long> salesByFamily, string fallbackFamily)
        {
            var shares = new Dictionary<string, float>(StringComparer.Ordinal);

            long total = 0;
            if (salesByFamily != null)
            {
                foreach (KeyValuePair<string, long> pair in salesByFamily)
                {
                    if (pair.Value > 0) total += pair.Value;
                }
            }

            if (total <= 0)
            {
                if (!string.IsNullOrEmpty(fallbackFamily)) shares[fallbackFamily] = 1f;
                return shares;
            }

            foreach (KeyValuePair<string, long> pair in salesByFamily)
            {
                if (pair.Value > 0 && pair.Key != null) shares[pair.Key] = (float)((double)pair.Value / total);
            }
            return shares;
        }

        /// <summary>The share of a family; 0 when it has none in the map, a negative number ("not measured") when there is no map.</summary>
        public static float ShareOf(IReadOnlyDictionary<string, float> shares, string familyId)
        {
            if (shares == null) return -1f;
            float share;
            return familyId != null && shares.TryGetValue(familyId, out share) ? share : 0f;
        }

        // ---- the lines of the section ----

        /// <summary>
        /// The lines of the section: every family of the catalog in the catalog's order, with its share, its whole percent (the percents add up to 100 when
        /// there is any share at all) and the change against <paramref name="yearAgo"/> (a missing map: no change).
        /// </summary>
        public static List<EcosystemRow> Rows(ArchCatalog catalog, IReadOnlyDictionary<string, float> current, IReadOnlyDictionary<string, float> yearAgo)
        {
            var rows = new List<EcosystemRow>();
            if (catalog == null) return rows;

            var shares = new List<float>(catalog.Count);
            foreach (ArchFamily family in catalog.Families)
            {
                float share = ShareOf(current, family.Id);
                shares.Add(share < 0f || float.IsNaN(share) ? 0f : share);
            }

            int[] percents = RoundToHundred(shares);
            int index = 0;
            foreach (ArchFamily family in catalog.Families)
            {
                float before = yearAgo == null ? shares[index] : Math.Max(0f, ShareOf(yearAgo, family.Id));
                rows.Add(new EcosystemRow
                {
                    FamilyId = family.Id,
                    Share = shares[index],
                    Percent = percents[index],
                    ChangePoints = ChangePoints(shares[index], before),
                });
                index++;
            }
            return rows;
        }

        /// <summary>The shares as whole percents that add up to exactly 100 (the largest remainders get the missing units); all 0 when there is no share.</summary>
        public static int[] RoundToHundred(IReadOnlyList<float> shares)
        {
            int count = shares == null ? 0 : shares.Count;
            var result = new int[count];
            if (count == 0) return result;

            double sum = 0;
            for (int i = 0; i < count; i++)
            {
                if (IsUsable(shares[i])) sum += shares[i];
            }
            if (!(sum > 0)) return result;

            var remainders = new double[count];
            int used = 0;
            for (int i = 0; i < count; i++)
            {
                double exact = IsUsable(shares[i]) ? shares[i] / sum * 100.0 : 0.0;
                int whole = (int)Math.Floor(exact + 1e-9);
                result[i] = whole;
                remainders[i] = exact - whole;
                used += whole;
            }

            int left = 100 - used;
            while (left > 0)
            {
                int best = -1;
                for (int i = 0; i < count; i++)
                {
                    if (!IsUsable(shares[i]) || shares[i] <= 0f) continue;
                    if (best < 0 || remainders[i] > remainders[best] + 1e-12) best = i;   // the earlier family wins a tie
                }
                if (best < 0) break;
                result[best]++;
                remainders[best] = -1.0;
                left--;
            }
            return result;
        }

        /// <summary>The change of a share in percentage points, rounded away from zero at the half: 0.62 against 0.58 is +4.</summary>
        public static int ChangePoints(float current, float yearAgo)
        {
            if (!IsUsable(current) || !IsUsable(yearAgo)) return 0;
            return (int)Math.Round((current - yearAgo) * 100.0, MidpointRounding.AwayFromZero);
        }

        /// <summary>A change for the text: "+4", "-3", "0" (the sign is written, the minus is the plain hyphen).</summary>
        public static string SignedPoints(int points)
        {
            if (points > 0) return "+" + points.ToString(System.Globalization.CultureInfo.InvariantCulture);
            return points.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        private static bool IsUsable(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value) && value >= 0f;
        }
    }
}
