using System;
using System.Collections.Generic;

namespace PTMod.Features.ScenariosAchievements.Logic
{
    /// <summary>
    /// One line of the table that compares the companies on the results screen and in the career summary (design.md "UI", the table under
    /// <c>ScorecardWindow</c>; D12): the player or an AI company by the data the game keeps for every company, so no running window of the
    /// feature is needed. Built by <c>GameMetricsSource.BuildComparison</c>; the columns are those of the design.
    /// </summary>
    public sealed class ComparisonRow
    {
        /// <summary>The key of the company (<c>player</c>, <c>ai:&lt;UniqueID&gt;</c>); null when it is not known.</summary>
        public string CompanyKey;

        /// <summary>The name as the game shows it (<c>ICompany.Name</c>).</summary>
        public string Name;

        public bool IsPlayer;

        /// <summary>The money of the company now.</summary>
        public double Money;

        /// <summary>The revenue of the twelve last full months (<c>CompanyFinance.Reports</c>).</summary>
        public double Revenue12m;

        /// <summary>The share of the company in the pieces sold in the last full month (<c>CpuBase.Market.Sold[Count - 2]</c>), 0..1.</summary>
        public double MarketShare;

        /// <summary>How many CPUs the company released (withdrawn ones included).</summary>
        public int CpuCount;

        /// <summary>The highest frequency among those CPUs, in kHz (<c>StringFormatter.FloatToFrequency</c> writes it).</summary>
        public float MaxFrequency;

        public bool IsBankrupt;
    }

    /// <summary>
    /// Picks the lines of the comparison table (FR-9 item 2, FR-17): the player first, then up to <see cref="DefaultMaxAi"/> AI companies - the
    /// companies that still exist before the bankrupt ones, the larger share of the market first, then the larger revenue, then the name.
    /// The order is deterministic, so the table does not jump from one opening to the next.
    /// </summary>
    public static class ComparisonBuilder
    {
        /// <summary>The most AI companies in the table (the table has seven lines with the player).</summary>
        public const int DefaultMaxAi = 6;

        /// <summary>The share of <paramref name="sold"/> in <paramref name="total"/>, 0..1; 0 when nothing was sold.</summary>
        public static double Share(double sold, double total)
        {
            if (!(total > 0) || double.IsInfinity(total) || double.IsNaN(sold) || sold <= 0) return 0.0;
            double share = sold / total;
            return share > 1.0 ? 1.0 : share;
        }

        /// <summary>The table: <paramref name="player"/> (when not null) and the best <paramref name="maxAi"/> of <paramref name="rivals"/>.</summary>
        public static ComparisonRow[] Build(ComparisonRow player, IList<ComparisonRow> rivals, int maxAi = DefaultMaxAi)
        {
            var picked = new List<ComparisonRow>();
            if (rivals != null)
            {
                foreach (ComparisonRow row in rivals)
                {
                    if (row != null && !ReferenceEquals(row, player)) picked.Add(row);
                }
            }
            picked.Sort(Compare);

            int limit = Math.Max(0, maxAi);
            var table = new List<ComparisonRow>(limit + 1);
            if (player != null) table.Add(player);
            for (int i = 0; i < picked.Count && i < limit; i++) table.Add(picked[i]);
            return table.ToArray();
        }

        /// <summary>The order of the rivals: not bankrupt first, the larger share, the larger revenue, the name (ordinal, case blind).</summary>
        public static int Compare(ComparisonRow a, ComparisonRow b)
        {
            if (a.IsBankrupt != b.IsBankrupt) return a.IsBankrupt ? 1 : -1;
            int byShare = b.MarketShare.CompareTo(a.MarketShare);
            if (byShare != 0) return byShare;
            int byRevenue = b.Revenue12m.CompareTo(a.Revenue12m);
            if (byRevenue != 0) return byRevenue;
            int byName = string.Compare(a.Name ?? string.Empty, b.Name ?? string.Empty, StringComparison.OrdinalIgnoreCase);
            if (byName != 0) return byName;
            return string.Compare(a.CompanyKey ?? string.Empty, b.CompanyKey ?? string.Empty, StringComparison.Ordinal);
        }
    }
}
