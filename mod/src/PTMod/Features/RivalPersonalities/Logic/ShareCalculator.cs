using System;
using System.Collections.Generic;
using PTMod.Core;

namespace PTMod.Features.RivalPersonalities
{
    /// <summary>
    /// The sales of one product (or one company) in the last full month, by segment: a row for <see cref="ShareCalculator"/>. The game side
    /// fills it from <c>Cpu.SoldBy</c> of the month before the last (design.md, formula 12): one row per CPU is fine, rows of the same company
    /// are added up.
    /// </summary>
    public sealed class SalesRow
    {
        private readonly string _companyKey;
        private readonly int[] _sold;

        /// <param name="companyKey">The company: <c>player</c> or <c>ai:&lt;UniqueID&gt;</c> (<c>ModCompanies.KeyOf</c>).</param>
        /// <param name="highEnd">Units sold in the High End segment.</param>
        /// <param name="midRange">Units sold in the Mid Range segment.</param>
        /// <param name="lowEnd">Units sold in the Low End segment.</param>
        /// <param name="industries">Units sold in the Industries segment.</param>
        public SalesRow(string companyKey, int highEnd, int midRange, int lowEnd, int industries)
        {
            _companyKey = companyKey;
            _sold = new[] { highEnd, midRange, lowEnd, industries };
        }

        /// <param name="companyKey">The company key.</param>
        /// <param name="soldBySegment">Units sold, the index being <c>(int)Segment</c> (0 High End ... 3 Industries); a shorter array counts as 0 for the rest, longer entries are ignored; null means no sales.</param>
        public SalesRow(string companyKey, int[] soldBySegment)
        {
            _companyKey = companyKey;
            _sold = new int[ShareCalculator.SegmentCount];
            if (soldBySegment == null) return;
            for (int i = 0; i < _sold.Length && i < soldBySegment.Length; i++) _sold[i] = soldBySegment[i];
        }

        public string CompanyKey
        {
            get { return _companyKey; }
        }

        /// <summary>Units sold in a segment (0 for an index outside 0..3).</summary>
        public int SoldIn(int segmentIndex)
        {
            return segmentIndex >= 0 && segmentIndex < _sold.Length ? _sold[segmentIndex] : 0;
        }
    }

    /// <summary>
    /// The market share of every company in each of the four segments of the game (FR-5, design.md Component 7, formula 12): the units it sold
    /// in the segment over the units everybody sold there. Pure arithmetic; the classification of CPUs into segments is not done here (the game
    /// already counts sales by segment, and the segment of a CPU of the player is the core's <c>ModSegments.Classify</c>).
    /// </summary>
    public static class ShareCalculator
    {
        /// <summary>The number of segments: High End, Mid Range, Low End, Industries (<c>(int)Segment</c> 0..3).</summary>
        public const int SegmentCount = 4;

        /// <summary>
        /// The shares by company key. Each array has <see cref="SegmentCount"/> entries indexed by <c>(int)Segment</c>; a company that sold
        /// nothing in a segment has 0 there, and a segment nobody sold in has 0 for everyone (no division by zero). In a segment that has
        /// sales the shares of all companies add up to 1 (within rounding). Rows with a null or empty key and negative numbers are not counted
        /// (a negative number counts as 0). The result is empty for no rows.
        /// </summary>
        public static Dictionary<string, float[]> Shares(IEnumerable<SalesRow> rows)
        {
            var sold = new Dictionary<string, long[]>(StringComparer.Ordinal);
            var totals = new long[SegmentCount];
            var order = new List<string>();

            if (rows != null)
            {
                foreach (SalesRow row in rows)
                {
                    if (row == null || string.IsNullOrEmpty(row.CompanyKey)) continue;

                    long[] company;
                    if (!sold.TryGetValue(row.CompanyKey, out company))
                    {
                        company = new long[SegmentCount];
                        sold[row.CompanyKey] = company;
                        order.Add(row.CompanyKey);
                    }

                    for (int segment = 0; segment < SegmentCount; segment++)
                    {
                        int units = row.SoldIn(segment);
                        if (units <= 0) continue;
                        company[segment] += units;
                        totals[segment] += units;
                    }
                }
            }

            var result = new Dictionary<string, float[]>(StringComparer.Ordinal);
            foreach (string key in order)
            {
                long[] company = sold[key];
                var shares = new float[SegmentCount];
                for (int segment = 0; segment < SegmentCount; segment++)
                {
                    shares[segment] = totals[segment] > 0 ? (float)((double)company[segment] / totals[segment]) : 0f;
                }
                result[key] = shares;
            }
            return result;
        }

        /// <summary>
        /// The share of a company in a segment out of the result of <see cref="Shares"/>; 0 for an unknown company, a null table or a segment that
        /// is not one of the four (<see cref="Segment.Any"/>).
        /// </summary>
        public static float ShareOf(Dictionary<string, float[]> shares, string companyKey, Segment segment)
        {
            int index = (int)segment;
            float[] row;
            if (shares == null || companyKey == null || index < 0 || index >= SegmentCount) return 0f;
            return shares.TryGetValue(companyKey, out row) && row != null && row.Length > index ? row[index] : 0f;
        }
    }
}
