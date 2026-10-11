using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace PTMod.Features.PressReviews
{
    /// <summary>The metascores of a set of reviews in three numbers: the median, the share of the high ones and the share of the low ones.</summary>
    public sealed class ScoreSummary
    {
        /// <summary>How many metascores were looked at.</summary>
        public int Count;

        /// <summary>The middle metascore (the mean of the two middle ones for an even count); 0 when there are none.</summary>
        public double Median;

        /// <summary>The share of the metascores at <see cref="ScoreDistribution.HighScore"/> or above, 0…1.</summary>
        public double ShareHigh;

        /// <summary>The share of the metascores below <see cref="ScoreDistribution.LowScore"/>, 0…1.</summary>
        public double ShareLow;
    }

    /// <summary>
    /// How the metascores are spread (requirements "Acceptance Metrics", design.md "Ручные сценарии" 12, TSK-019, TSK-021): over a good run the median is
    /// 55…70, no more than 15 % of the reviews are at 85 or above and no more than 15 % are below 40. The calibration test of the formulas and the yearly
    /// summary of the debug build use the same numbers, so what the log says is what the test holds the configuration to.
    /// </summary>
    public static class ScoreDistribution
    {
        /// <summary>A metascore at or above this counts as high.</summary>
        public const int HighScore = 85;

        /// <summary>A metascore below this counts as low.</summary>
        public const int LowScore = 40;

        public const double MedianMin = 55.0;
        public const double MedianMax = 70.0;
        public const double MaxShareHigh = 0.15;
        public const double MaxShareLow = 0.15;

        /// <summary>The median and the two shares of <paramref name="scores"/> (an empty set gives zeros).</summary>
        public static ScoreSummary Summarize(IEnumerable<int> scores)
        {
            var summary = new ScoreSummary();
            if (scores == null) return summary;

            var sorted = new List<int>(scores);
            if (sorted.Count == 0) return summary;
            sorted.Sort();

            int high = 0;
            int low = 0;
            for (int i = 0; i < sorted.Count; i++)
            {
                if (sorted[i] >= HighScore) high++;
                if (sorted[i] < LowScore) low++;
            }

            int mid = sorted.Count / 2;
            summary.Count = sorted.Count;
            summary.Median = sorted.Count % 2 == 1 ? sorted[mid] : (sorted[mid - 1] + sorted[mid]) / 2.0;
            summary.ShareHigh = high / (double)sorted.Count;
            summary.ShareLow = low / (double)sorted.Count;
            return summary;
        }

        /// <summary>The metascores of the reviews published in <paramref name="year"/> by every company (a record whose date does not parse is skipped).</summary>
        public static List<int> ScoresOfYear(IList<ReviewRecord> reviews, int year)
        {
            var scores = new List<int>();
            if (reviews == null) return scores;

            for (int i = 0; i < reviews.Count; i++)
            {
                DateTime published;
                if (reviews[i] == null || !PressDates.TryParse(reviews[i].publishedOn, out published)) continue;
                if (published.Year == year) scores.Add(reviews[i].metascore);
            }
            return scores;
        }

        /// <summary>The spread meets the targets: median in 55…70, shares of high and low scores at most 15 % each. An empty set has nothing to judge and passes.</summary>
        public static bool IsWithinTargets(ScoreSummary summary)
        {
            if (summary == null) throw new ArgumentNullException("summary");
            if (summary.Count == 0) return true;

            return summary.Median >= MedianMin && summary.Median <= MedianMax
                && summary.ShareHigh <= MaxShareHigh && summary.ShareLow <= MaxShareLow;
        }

        /// <summary>The line of the yearly summary: <c>balance 1995: 48 review(s), median 62, 85 or more: 6.3 %, below 40: 4.2 % (within the targets)</c>.</summary>
        public static string Line(int year, ScoreSummary summary)
        {
            if (summary == null) throw new ArgumentNullException("summary");

            var sb = new StringBuilder("balance ");
            sb.Append(year.ToString(CultureInfo.InvariantCulture)).Append(": ");
            sb.Append(summary.Count.ToString(CultureInfo.InvariantCulture)).Append(" review(s)");
            if (summary.Count == 0) return sb.ToString();

            sb.Append(", median ").Append(summary.Median.ToString("0.#", CultureInfo.InvariantCulture));
            sb.Append(", ").Append(HighScore).Append(" or more: ").Append(Percent(summary.ShareHigh));
            sb.Append(", below ").Append(LowScore).Append(": ").Append(Percent(summary.ShareLow));
            sb.Append(IsWithinTargets(summary)
                ? " (within the targets)"
                : " (OUT OF THE TARGETS: median " + MedianMin.ToString("0", CultureInfo.InvariantCulture) + "-" + MedianMax.ToString("0", CultureInfo.InvariantCulture)
                  + ", at most " + Percent(MaxShareHigh) + " high, at most " + Percent(MaxShareLow) + " low; see tau, g and the weights in review-config.json)");
            return sb.ToString();
        }

        private static string Percent(double share)
        {
            return (share * 100.0).ToString("0.#", CultureInfo.InvariantCulture) + " %";
        }
    }
}
