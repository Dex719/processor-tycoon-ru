using System;
using System.Collections.Generic;
using System.Globalization;

namespace PTMod.Features.PressReviews
{
    /// <summary>One cell of the column "Press": the text and the number the table sorts the column by.</summary>
    public struct MarketCell
    {
        public string Text;

        /// <summary>The value for sorting: the metascore, or <see cref="MarketColumn.NoScoreNumber"/> for a CPU nobody reviewed (it sorts below every review).</summary>
        public float Number;
    }

    /// <summary>
    /// The pure side of the optional column "Press" of the market table of the CPU window (FR-13, TSK-020): which cell a CPU gets - its metascore, or a dash
    /// when the press has not reviewed it - which review counts for a CPU, and whether the rows the game built can be matched with the CPUs they show. The patch
    /// itself (<c>UI/MarketColumnPatch</c>) only reads the game's table and writes these cells.
    /// </summary>
    public static class MarketColumn
    {
        /// <summary>The text of a cell for a CPU without a review: an em dash.</summary>
        public const string NoScoreText = "—";

        /// <summary>The sort value of a CPU without a review (below any metascore).</summary>
        public const float NoScoreNumber = -1f;

        /// <summary>The width of the column in pixels (the game's own columns are 60…180).</summary>
        public const float Width = 70f;

        /// <summary>The cell of a CPU with the review <paramref name="review"/>; the dash cell for null.</summary>
        public static MarketCell CellFor(ReviewRecord review)
        {
            if (review == null) return new MarketCell { Text = NoScoreText, Number = NoScoreNumber };

            int score = review.metascore < 0 ? 0 : review.metascore > 100 ? 100 : review.metascore;
            return new MarketCell { Text = score.ToString(CultureInfo.InvariantCulture), Number = score };
        }

        /// <summary>
        /// The newest review of every CPU of the archive, by the save id of the CPU. A CPU is reviewed once, so this is normally the only one; when the
        /// archive has two (a damaged one), the later by date wins.
        /// </summary>
        public static Dictionary<int, ReviewRecord> LatestByCpu(IList<ReviewRecord> archive)
        {
            var result = new Dictionary<int, ReviewRecord>();
            if (archive == null) return result;

            for (int i = 0; i < archive.Count; i++)
            {
                ReviewRecord record = archive[i];
                if (record == null) continue;

                ReviewRecord known;
                if (!result.TryGetValue(record.cpuSaveId, out known) || string.CompareOrdinal(record.publishedOn ?? string.Empty, known.publishedOn ?? string.Empty) >= 0)
                    result[record.cpuSaveId] = record;
            }
            return result;
        }

        /// <summary>
        /// The rows the game filled and the CPUs the patch worked out for them can be matched one to one: the same count. When they differ (the game changed
        /// the way it picks the CPUs of the table) no cell is written, so a cell never stands in a wrong row.
        /// </summary>
        public static bool RowsMatch(int activeRows, int listedCpus)
        {
            return activeRows >= 0 && activeRows == listedCpus;
        }
    }
}
