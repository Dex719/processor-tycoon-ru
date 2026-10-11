using System;
using PTMod.Core;

namespace PTMod.Features.CpuArchitectures
{
    /// <summary>
    /// The shares of the four segments in the sales of an AI company (FR-14 item 1): <c>share_s</c> of the attractiveness of an architecture. The feature has no
    /// idea of its own of "the main segment of a company": the shares come from the sales of its CPUs over the last months
    /// (<c>CpuBase.MarketData.SoldByHighEnd / SoldByMidRange / SoldByLowEnd / SoldByIndustries</c>), and when there are no sales from the segment of the price of its
    /// current CPUs, one vote per CPU, as <c>ModSegments.Classify</c> says; with no CPUs at all every segment has a quarter.
    /// The four numbers are in the order of <see cref="Segment"/>: HighEnd, MidRange, LowEnd, Industries.
    /// </summary>
    public static class SegmentShares
    {
        /// <summary>The number of segments a CPU can be sold in.</summary>
        public const int Count = 4;

        /// <summary>A quarter for each: the answer for a company with neither sales nor CPUs.</summary>
        public static float[] Equal()
        {
            return new[] { 0.25f, 0.25f, 0.25f, 0.25f };
        }

        /// <summary>The index of a segment in the four numbers; -1 for <see cref="Segment.Any"/> and for anything else.</summary>
        public static int IndexOf(Segment segment)
        {
            int index = (int)segment;
            return index >= 0 && index < Count ? index : -1;
        }

        /// <summary>The shares of the sales by segment: each segment's units over all of them; null when nothing was sold.</summary>
        public static float[] FromSales(long highEnd, long midRange, long lowEnd, long industries)
        {
            return Normalize(new[] { highEnd, midRange, lowEnd, industries });
        }

        /// <summary>The shares of the votes (one per CPU, by the segment of its price); null when nobody voted.</summary>
        public static float[] FromVotes(int[] votes)
        {
            if (votes == null || votes.Length != Count) return null;
            return Normalize(new long[] { votes[0], votes[1], votes[2], votes[3] });
        }

        /// <summary>
        /// The shares for the decision: the sales when there are any, otherwise the votes of the CPUs, otherwise a quarter each. <paramref name="salesByHighEnd"/> and
        /// the rest are the units sold in the window (all four or none); <paramref name="votes"/> is the number of CPUs per segment.
        /// </summary>
        public static float[] Resolve(long salesByHighEnd, long salesByMidRange, long salesByLowEnd, long salesByIndustries, int[] votes)
        {
            return FromSales(salesByHighEnd, salesByMidRange, salesByLowEnd, salesByIndustries) ?? FromVotes(votes) ?? Equal();
        }

        private static float[] Normalize(long[] counts)
        {
            long total = 0;
            for (int i = 0; i < Count; i++)
            {
                if (counts[i] > 0) total += counts[i];
            }
            if (total <= 0) return null;

            var shares = new float[Count];
            for (int i = 0; i < Count; i++) shares[i] = counts[i] > 0 ? (float)((double)counts[i] / total) : 0f;
            return shares;
        }

        /// <summary>The shares are four finite numbers that are not negative; the policy falls back to a quarter each when they are not.</summary>
        public static bool IsUsable(float[] shares)
        {
            if (shares == null || shares.Length != Count) return false;
            double sum = 0;
            for (int i = 0; i < Count; i++)
            {
                if (float.IsNaN(shares[i]) || float.IsInfinity(shares[i]) || shares[i] < 0f) return false;
                sum += shares[i];
            }
            return sum > 0;
        }
    }
}
