using System;
using System.Collections.Generic;
using System.Globalization;

namespace PTMod.Features.PressReviews
{
    /// <summary>A change of a company's reputation the awards make: who, by how much, and the reason written to the history of the reputation.</summary>
    public sealed class AwardPayout
    {
        public string CompanyKey = string.Empty;
        public int Delta;

        /// <summary><c>press.award:cpu:&lt;year&gt;:&lt;segment&gt;</c>, <c>press.award:company:&lt;year&gt;</c> or <c>press.award:flop:&lt;year&gt;</c>.</summary>
        public string Reason = string.Empty;
    }

    /// <summary>The awards of a year and what they pay in reputation.</summary>
    public sealed class AwardsResult
    {
        /// <summary>The awards as the archive keeps them: no CPU lines, no company, and <c>disappointmentCpuSaveId</c> -1 (none) until they are filled.</summary>
        public readonly YearAwards Awards = new YearAwards
        {
            cpuOfYear = new List<AwardLine>(),
            companyOfYearKey = string.Empty,
            disappointmentCpuSaveId = -1,
        };

        public readonly List<AwardPayout> Payouts = new List<AwardPayout>();

        /// <summary>Nothing was awarded: no "CPU of the Year", no "Company of the Year", no "Disappointment of the Year". Such a year gets no letter and no entry.</summary>
        public bool IsEmpty
        {
            get { return Awards.cpuOfYear.Count == 0 && string.IsNullOrEmpty(Awards.companyOfYearKey) && Awards.disappointmentCpuSaveId < 0; }
        }

        /// <summary>The reputation change the awards make to the company, summed.</summary>
        public int DeltaOf(string companyKey)
        {
            int sum = 0;
            for (int i = 0; i < Payouts.Count; i++)
            {
                if (string.Equals(Payouts[i].CompanyKey, companyKey, StringComparison.Ordinal)) sum += Payouts[i].Delta;
            }
            return sum;
        }
    }

    /// <summary>
    /// The yearly awards of the press (FR-8, design.md Component 8), worked out from the reviews published in the year. Pure and deterministic: the same
    /// reviews give the same awards, a tie goes to the review that came first and then to the lower CPU id.
    /// <list type="bullet">
    /// <item><b>CPU of the Year</b>, per segment: the highest metascore of the segment, from <see cref="AwardsConfig.CpuOfYearMin"/> (75) up.</item>
    /// <item><b>Company of the Year</b>: the highest mean metascore among the companies with at least <see cref="AwardsConfig.CompanyOfYearMinReviews"/>
    /// (2) reviews.</item>
    /// <item><b>Disappointment of the Year</b>: the lowest metascore, below <see cref="AwardsConfig.DisappointmentMax"/> (45).</item>
    /// </list>
    /// The reputation: +4 to the company of each CPU of the Year, +3 to the Company of the Year, −3 to the company of the Disappointment.
    /// </summary>
    public static class AwardsCalculator
    {
        /// <summary>The prefix of the reasons of the history of the reputation the awards write.</summary>
        public const string ReasonPrefix = "press.award:";

        /// <summary>The reason of the reputation of a "CPU of the Year" (<c>press.award:cpu:1995:0</c>).</summary>
        public static string CpuReason(int year, int segment)
        {
            return ReasonPrefix + "cpu:" + Number(year) + ":" + Number(segment);
        }

        /// <summary>The reason of the reputation of the "Company of the Year" (<c>press.award:company:1995</c>).</summary>
        public static string CompanyReason(int year)
        {
            return ReasonPrefix + "company:" + Number(year);
        }

        /// <summary>The reason of the reputation of the "Disappointment of the Year" (<c>press.award:flop:1995</c>).</summary>
        public static string DisappointmentReason(int year)
        {
            return ReasonPrefix + "flop:" + Number(year);
        }

        /// <summary>
        /// The awards of <paramref name="year"/> from the reviews published in it (by <see cref="ReviewRecord.publishedOn"/>); reviews of other years, of
        /// no company, with a date that does not read or a segment outside 0…3 are left out. A compact record counts: it keeps all it needs.
        /// </summary>
        public static AwardsResult Compute(int year, IList<ReviewRecord> reviews, AwardsConfig cfg)
        {
            AwardsConfig rule = cfg ?? new AwardsConfig();
            var result = new AwardsResult();
            result.Awards.year = year;

            List<ReviewRecord> ofYear = OfYear(year, reviews);
            if (ofYear.Count == 0) return result;

            CpusOfTheYear(result, ofYear, rule, year);
            CompanyOfTheYear(result, ofYear, rule, year);
            Disappointment(result, ofYear, rule, year);
            return result;
        }

        // ---- the three awards ----

        private static void CpusOfTheYear(AwardsResult result, List<ReviewRecord> ofYear, AwardsConfig rule, int year)
        {
            for (int segment = 0; segment < 4; segment++)
            {
                ReviewRecord best = null;
                for (int i = 0; i < ofYear.Count; i++)
                {
                    ReviewRecord candidate = ofYear[i];
                    if (candidate.segment != segment || candidate.metascore < rule.CpuOfYearMin) continue;
                    if (best == null || IsBetter(candidate, best)) best = candidate;
                }
                if (best == null) continue;

                result.Awards.cpuOfYear.Add(new AwardLine
                {
                    segment = segment,
                    cpuSaveId = best.cpuSaveId,
                    cpuName = best.cpuName ?? string.Empty,
                    companyKey = best.companyKey,
                    score = best.metascore,
                });
                result.Payouts.Add(new AwardPayout { CompanyKey = best.companyKey, Delta = rule.CpuOfYearRep, Reason = CpuReason(year, segment) });
            }
        }

        private static void CompanyOfTheYear(AwardsResult result, List<ReviewRecord> ofYear, AwardsConfig rule, int year)
        {
            var sums = new Dictionary<string, long>(StringComparer.Ordinal);
            var counts = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int i = 0; i < ofYear.Count; i++)
            {
                string key = ofYear[i].companyKey;
                long sum;
                int count;
                sums.TryGetValue(key, out sum);
                counts.TryGetValue(key, out count);
                sums[key] = sum + ofYear[i].metascore;
                counts[key] = count + 1;
            }

            string bestKey = null;
            double bestAverage = 0.0;
            int bestCount = 0;
            int needed = rule.CompanyOfYearMinReviews < 1 ? 1 : rule.CompanyOfYearMinReviews;
            foreach (KeyValuePair<string, int> pair in counts)
            {
                if (pair.Value < needed) continue;

                double average = (double)sums[pair.Key] / pair.Value;
                bool better = bestKey == null
                    || average > bestAverage
                    || (average == bestAverage && pair.Value > bestCount)
                    || (average == bestAverage && pair.Value == bestCount && string.CompareOrdinal(pair.Key, bestKey) < 0);
                if (!better) continue;

                bestKey = pair.Key;
                bestAverage = average;
                bestCount = pair.Value;
            }
            if (bestKey == null) return;

            result.Awards.companyOfYearKey = bestKey;
            result.Awards.companyOfYearAvg = (float)bestAverage;
            result.Payouts.Add(new AwardPayout { CompanyKey = bestKey, Delta = rule.CompanyOfYearRep, Reason = CompanyReason(year) });
        }

        private static void Disappointment(AwardsResult result, List<ReviewRecord> ofYear, AwardsConfig rule, int year)
        {
            ReviewRecord worst = null;
            for (int i = 0; i < ofYear.Count; i++)
            {
                ReviewRecord candidate = ofYear[i];
                if (candidate.metascore >= rule.DisappointmentMax) continue;
                if (worst == null || IsWorse(candidate, worst)) worst = candidate;
            }
            if (worst == null) return;

            result.Awards.disappointmentCpuSaveId = worst.cpuSaveId;
            result.Awards.disappointmentScore = worst.metascore;
            result.Payouts.Add(new AwardPayout { CompanyKey = worst.companyKey, Delta = rule.DisappointmentRep, Reason = DisappointmentReason(year) });
        }

        // ---- internals ----

        private static List<ReviewRecord> OfYear(int year, IList<ReviewRecord> reviews)
        {
            var found = new List<ReviewRecord>();
            if (reviews == null) return found;

            for (int i = 0; i < reviews.Count; i++)
            {
                ReviewRecord record = reviews[i];
                if (record == null || string.IsNullOrEmpty(record.companyKey) || record.segment < 0 || record.segment > 3) continue;

                DateTime published;
                if (!PressDates.TryParse(record.publishedOn, out published) || published.Year != year) continue;
                found.Add(record);
            }
            return found;
        }

        /// <summary>The higher score wins; a tie goes to the earlier review, then to the lower CPU id.</summary>
        private static bool IsBetter(ReviewRecord candidate, ReviewRecord best)
        {
            if (candidate.metascore != best.metascore) return candidate.metascore > best.metascore;
            return Earlier(candidate, best);
        }

        /// <summary>The lower score is worse; a tie goes to the earlier review, then to the lower CPU id.</summary>
        private static bool IsWorse(ReviewRecord candidate, ReviewRecord worst)
        {
            if (candidate.metascore != worst.metascore) return candidate.metascore < worst.metascore;
            return Earlier(candidate, worst);
        }

        private static bool Earlier(ReviewRecord a, ReviewRecord b)
        {
            int byDate = string.CompareOrdinal(a.publishedOn, b.publishedOn);
            return byDate != 0 ? byDate < 0 : a.cpuSaveId < b.cpuSaveId;
        }

        private static string Number(int value)
        {
            return value.ToString(CultureInfo.InvariantCulture);
        }
    }
}
