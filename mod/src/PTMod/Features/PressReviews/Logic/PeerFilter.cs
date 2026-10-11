using System;
using System.Collections.Generic;

namespace PTMod.Features.PressReviews
{
    /// <summary>A candidate reference CPU as the filter sees it: who owns it, whether it is retired and what it sold in the segment over the last full months.</summary>
    public struct PeerCandidate
    {
        public int SaveId;

        /// <summary>The key of the owner (<c>player</c>, <c>ai:&lt;UniqueID&gt;</c>).</summary>
        public string CompanyKey;

        public bool IsRetired;

        /// <summary>The sales in the reviewed segment over the last full months (<see cref="PeerFilter.SoldOverFullMonths"/>).</summary>
        public int SoldInSegment;
    }

    /// <summary>
    /// The pure part of the choice of reference CPUs (FR-2 item 2, design.md "Формулы и баланс" 2). The game side
    /// (<c>Game/PeerSelector</c>) asks the game for the best sellers of the segment and turns them into <see cref="PeerCandidate"/>s in the order the
    /// game gave; this class decides which of them are references.
    /// </summary>
    public static class PeerFilter
    {
        /// <summary>
        /// The indices (into <paramref name="candidates"/>, in the same order) of the first <paramref name="maxCount"/> candidates that are references:
        /// not the reviewed CPU itself, of another company, not retired and with sales in the segment over the last full months above 0.
        /// A null <paramref name="reviewedCompanyKey"/> turns the company check off.
        /// </summary>
        public static List<int> Select(IList<PeerCandidate> candidates, string reviewedCompanyKey, int reviewedSaveId, int maxCount)
        {
            var chosen = new List<int>();
            if (candidates == null || maxCount <= 0) return chosen;

            for (int i = 0; i < candidates.Count && chosen.Count < maxCount; i++)
            {
                PeerCandidate candidate = candidates[i];
                if (candidate.SaveId == reviewedSaveId) continue;
                if (reviewedCompanyKey != null && string.Equals(candidate.CompanyKey, reviewedCompanyKey, StringComparison.Ordinal)) continue;
                if (candidate.IsRetired) continue;
                if (candidate.SoldInSegment <= 0) continue;
                chosen.Add(i);
            }
            return chosen;
        }

        /// <summary>
        /// The sales of the last <paramref name="months"/> full months of a monthly series whose last element is the month in progress, like the game's
        /// <c>MarketDataProvider.TakeSoldAtPeriod</c>: the elements before the last one, newest first, at most <paramref name="months"/> of them.
        /// A series of one element (or none) has no full month and gives 0.
        /// </summary>
        public static int SoldOverFullMonths(IList<int> monthly, int months)
        {
            if (monthly == null || months <= 0) return 0;

            long sum = 0;
            int count = monthly.Count;
            for (int k = 1; k <= months && k < count; k++) sum += monthly[count - 1 - k];
            return sum > int.MaxValue ? int.MaxValue : sum < 0 ? 0 : (int)sum;
        }
    }
}
