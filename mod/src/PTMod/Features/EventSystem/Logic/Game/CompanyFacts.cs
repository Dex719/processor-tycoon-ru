using System;
using System.Collections.Generic;
using PTMod.Core.Logic.Companies;

namespace PTMod.Features.EventSystem
{
    /// <summary>
    /// The rules the game facade applies to the raw numbers of a company (design.md, "Runtime-модель", "Деньги"), kept here so they are tested without the game.
    /// </summary>
    public static class CompanyFacts
    {
        /// <summary>Reports <c>CompanyFinance.QuarterlyRevenue</c> reads (it indexes the last three and throws with fewer, E3).</summary>
        public const int QuarterReports = 3;

        /// <summary>The finance has the three monthly reports a quarter needs.</summary>
        public static bool HasQuarter(int reportsCount)
        {
            return reportsCount >= QuarterReports;
        }

        /// <summary>
        /// The quarterly revenue from the <c>TotalRevenues</c> of the reports, oldest first: the sum of the last three, 0 when there are fewer (the guard of
        /// the facade, E3), never NaN.
        /// </summary>
        public static float QuarterlyRevenue(IList<float> totalRevenues)
        {
            if (totalRevenues == null || !HasQuarter(totalRevenues.Count)) return 0f;
            int n = totalRevenues.Count;
            float sum = totalRevenues[n - 1] + totalRevenues[n - 2] + totalRevenues[n - 3];
            return float.IsNaN(sum) || float.IsInfinity(sum) ? 0f : sum;
        }

        /// <summary><c>es.hasRevenueHistory</c>: three reports and a positive quarterly revenue.</summary>
        public static bool HasRevenueHistory(int reportsCount, float quarterlyRevenue)
        {
            return HasQuarter(reportsCount) && quarterlyRevenue > 0f;
        }

        /// <summary><c>es.hasFab</c>: at least one production line and not fabless.</summary>
        public static bool HasFab(int factoryLines, bool isFabless)
        {
            return factoryLines >= 1 && !isFabless;
        }
    }

    /// <summary>The ranking of the AI companies by market share (<c>IGameFacade.IsTopByMarketShare</c>, <c>PackOptions.RivalNewsTopN</c>).</summary>
    public static class MarketRanking
    {
        /// <summary>
        /// The AI company <paramref name="companyKey"/> is among the <paramref name="n"/> leaders by share. Only AI companies are ranked: the player is never
        /// counted and is never "top" (a news limit is about rivals). Equal shares keep the order of <paramref name="companies"/> (by <c>UniqueID</c>).
        /// False for <paramref name="n"/> &lt;= 0 and for a key that is not among the companies.
        /// </summary>
        public static bool IsTop(IList<string> companies, Func<string, float> shareOf, string companyKey, int n)
        {
            if (companies == null || shareOf == null || n <= 0 || !CompanyKey.IsAi(companyKey)) return false;

            var ranked = new List<KeyValuePair<string, float>>();
            foreach (string key in companies)
            {
                if (!CompanyKey.IsAi(key)) continue;
                float share = shareOf(key);
                if (float.IsNaN(share) || float.IsInfinity(share)) share = 0f;
                ranked.Add(new KeyValuePair<string, float>(key, share));
            }

            float own = -1f;
            int ownIndex = -1;
            for (int i = 0; i < ranked.Count; i++)
            {
                if (!string.Equals(ranked[i].Key, companyKey, StringComparison.Ordinal)) continue;
                own = ranked[i].Value;
                ownIndex = i;
                break;
            }
            if (ownIndex < 0) return false;

            // Position = companies ahead: a larger share, or an equal share earlier in the list.
            int position = 0;
            for (int i = 0; i < ranked.Count; i++)
            {
                if (i == ownIndex) continue;
                if (ranked[i].Value > own || (ranked[i].Value == own && i < ownIndex)) position++;
            }
            return position < n;
        }
    }

    /// <summary>The list of companies that can be targets (<c>IGameFacade.Companies</c>, <c>CompanyDirectory</c>).</summary>
    public static class CompanyRoster
    {
        /// <summary>
        /// The companies of <paramref name="all"/> (the order of <c>ModCompanies.All</c>: the player, then the AI by <c>UniqueID</c>) that have a key and are not
        /// bankrupt, with their keys. A company whose key is taken already is left out.
        /// </summary>
        public static List<KeyValuePair<string, T>> Alive<T>(IEnumerable<T> all, Func<T, string> keyOf, Func<T, bool> isBankrupt) where T : class
        {
            var result = new List<KeyValuePair<string, T>>();
            if (all == null || keyOf == null) return result;

            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (T company in all)
            {
                if (company == null) continue;
                string key = keyOf(company);
                if (!CompanyKey.IsValid(key) || !seen.Add(key)) continue;
                if (isBankrupt != null && isBankrupt(company)) continue;
                result.Add(new KeyValuePair<string, T>(key, company));
            }
            return result;
        }
    }
}
