using System;
using System.Collections.Generic;
using System.Globalization;
using PTMod.Core.Logic.Companies;

namespace PTMod.Features.PressReviews
{
    /// <summary>What goes into the monthly letter about the rivals' reviews (FR-4 item 3): the lines and the count of the reviews left out.</summary>
    public sealed class DigestContent
    {
        /// <summary>The reviews that get a line, the most remarkable (the farthest from the middle mark) first; at most <c>digest.maxLines</c>.</summary>
        public readonly List<ReviewRecord> Lines = new List<ReviewRecord>();

        /// <summary>How many reviews of the month are not in <see cref="Lines"/> ("and N more reviews in the archive").</summary>
        public int More;

        /// <summary>How many reviews of the rivals the month had in all.</summary>
        public int Total;

        /// <summary>No review is worth a line: the letter is not sent (AC-4.3).</summary>
        public bool IsEmpty
        {
            get { return Lines.Count == 0; }
        }
    }

    /// <summary>
    /// The digest of the rivals' reviews (FR-4 items 3-5, design.md Component 8a): on the first of a month the press sums up the reviews of the CPUs of the
    /// AI companies published in the month before. A review is worth a line when its metascore is high (<c>digest.notableHigh</c>, 85) or low
    /// (<c>digest.notableLow</c>, 40), or when it is about a segment where the player has a CPU on sale; the lines go in the order of the distance of the metascore
    /// from the middle mark (<c>|M − digest.parity|</c>, 65), at most <c>digest.maxLines</c> (10) of them, and every other review of the month is only counted
    /// ("and N more reviews in the archive"). A month without a worthy review gives no letter. Pure: the game side (<c>Game/DigestService</c>) reads the
    /// archive, the player's segments and the date, and sends the letter.
    /// </summary>
    public static class DigestBuilder
    {
        /// <summary>
        /// The reviews of the AI companies published in the month that begins on <paramref name="monthStart"/> (the day of the month does not matter), in
        /// the order of the archive. The player's own reviews are not in a digest (they come as letters of their own); a record whose date does not parse
        /// is skipped.
        /// </summary>
        public static List<ReviewRecord> ReviewsOfMonth(IList<ReviewRecord> archive, DateTime monthStart)
        {
            var result = new List<ReviewRecord>();
            if (archive == null) return result;

            var from = new DateTime(monthStart.Year, monthStart.Month, 1);
            DateTime to = from.AddMonths(1);
            for (int i = 0; i < archive.Count; i++)
            {
                ReviewRecord record = archive[i];
                if (record == null || CompanyKey.IsPlayer(record.companyKey)) continue;

                DateTime published;
                if (!PressDates.TryParse(record.publishedOn, out published)) continue;
                if (published.Date >= from && published.Date < to) result.Add(record);
            }
            return result;
        }

        /// <summary>
        /// The digest of the month of <paramref name="today"/> has not been made yet: <c>PressState.lastDigest</c> (the month of the last digest, <c>yyyy-MM</c>)
        /// is not this month. It is what keeps a save loaded on the first of a month, or a batch run that plays the first twice, from sending the letter twice.
        /// </summary>
        public static bool IsDue(string lastDigest, DateTime today)
        {
            return !string.Equals(lastDigest, PressDates.ToMonthText(today), StringComparison.Ordinal);
        }

        /// <summary>The first day of the month before the month of <paramref name="today"/>: the month a digest sent on <paramref name="today"/> is about.</summary>
        public static DateTime PreviousMonth(DateTime today)
        {
            return new DateTime(today.Year, today.Month, 1).AddMonths(-1);
        }

        /// <summary>How far the metascore of a review is from the middle mark: <c>|M − parity|</c>; the order of the lines.</summary>
        public static int Distance(ReviewRecord review, DigestConfig cfg)
        {
            if (review == null) return 0;
            DigestConfig rule = cfg ?? new DigestConfig();
            int distance = review.metascore - rule.Parity;
            return distance < 0 ? -distance : distance;
        }

        /// <summary>
        /// A review is worth a line when it is high (≥ <c>notableHigh</c>), low (≤ <c>notableLow</c>) or about a segment where the player has a CPU on sale.
        /// A segment outside 0…3 (a damaged record) never counts as the player's.
        /// </summary>
        public static bool IsNotable(ReviewRecord review, ISet<int> playerSegments, DigestConfig cfg)
        {
            if (review == null) return false;
            DigestConfig rule = cfg ?? new DigestConfig();

            if (review.metascore >= rule.NotableHigh || review.metascore <= rule.NotableLow) return true;
            return playerSegments != null && review.segment >= 0 && review.segment <= 3 && playerSegments.Contains(review.segment);
        }

        /// <summary>
        /// The digest of <paramref name="aiReviewsOfMonth"/> (the reviews of the rivals published last month, see <see cref="ReviewsOfMonth"/>): the
        /// notable reviews sorted by the distance from the middle mark, the first <c>maxLines</c> as lines, every other review counted in
        /// <see cref="DigestContent.More"/>. Equal distances go the higher metascore first, then the earlier review, then the smaller CPU id, so the same
        /// archive always gives the same letter. The argument is not changed.
        /// </summary>
        public static DigestContent Build(IList<ReviewRecord> aiReviewsOfMonth, ISet<int> playerSegments, DigestConfig cfg)
        {
            DigestConfig rule = cfg ?? new DigestConfig();
            var content = new DigestContent();
            if (aiReviewsOfMonth == null) return content;

            var all = new List<ReviewRecord>(aiReviewsOfMonth.Count);
            var notable = new List<ReviewRecord>();
            for (int i = 0; i < aiReviewsOfMonth.Count; i++)
            {
                ReviewRecord review = aiReviewsOfMonth[i];
                if (review == null) continue;

                all.Add(review);
                if (IsNotable(review, playerSegments, rule)) notable.Add(review);
            }
            content.Total = all.Count;
            if (notable.Count == 0) return content;

            notable.Sort((a, b) => Compare(a, b, rule));
            int lines = Math.Min(Math.Max(1, rule.MaxLines), notable.Count);
            for (int i = 0; i < lines; i++) content.Lines.Add(notable[i]);
            content.More = all.Count - lines;
            return content;
        }

        private static int Compare(ReviewRecord a, ReviewRecord b, DigestConfig rule)
        {
            int byDistance = Distance(b, rule).CompareTo(Distance(a, rule));
            if (byDistance != 0) return byDistance;

            int byScore = b.metascore.CompareTo(a.metascore);
            if (byScore != 0) return byScore;

            int byDate = string.CompareOrdinal(a.publishedOn ?? string.Empty, b.publishedOn ?? string.Empty);
            if (byDate != 0) return byDate;

            return a.cpuSaveId.CompareTo(b.cpuSaveId);
        }
    }

    /// <summary>What goes into the letter of the digest: its tag and the arguments of its title and body.</summary>
    public sealed class DigestMailContent
    {
        /// <summary><c>press:digest:&lt;yyyy-MM&gt;</c>.</summary>
        public string Tag = string.Empty;

        /// <summary>The arguments of the title and of the body (the core fills both with the same list): 0 the lines, one per line, then the line "and N more".</summary>
        public string[] Args = new string[0];
    }

    /// <summary>
    /// The letter "Press: rival reviews" (FR-4 item 3, design.md "Письма"): <c>Low</c>, tag <c>press:digest:&lt;yyyy-MM&gt;</c> with the month the letter is sent
    /// in (the first day of it), no buttons. A line is "company CPU — 91/100 (verdict)"; the last one, when reviews were left out, is "and N more reviews in the
    /// archive" in the form that fits N (the plural comes in as a function: <c>ModL10n.Plural</c> in the game). Pure; the texts come in through
    /// <see cref="PressText"/>.
    /// </summary>
    public static class DigestMailText
    {
        /// <summary>The prefix of the tag of the digests; the month follows.</summary>
        public const string TagPrefix = "press:digest:";

        /// <summary>The tag of the digest sent on <paramref name="today"/> (the month it arrives in; the lines are about the month before).</summary>
        public static string TagOf(DateTime today)
        {
            return TagPrefix + PressDates.ToMonthText(today);
        }

        /// <summary>The line of one review: "<c>{company} {CPU} — {M}/100 ({verdict})</c>".</summary>
        public static string LineOf(ReviewRecord review, PressText text, ReviewConfig cfg)
        {
            if (review == null) throw new ArgumentNullException("review");
            if (text == null) throw new ArgumentNullException("text");

            string verdictKey = string.IsNullOrEmpty(review.verdictKey) ? OutletPanel.VerdictKey(review.metascore, cfg ?? ReviewConfig.CreateDefault()) : review.verdictKey;
            return text("press.mail.digest.line", PressWords.Plain(review.companyName), PressWords.Plain(review.cpuName),
                review.metascore.ToString(CultureInfo.InvariantCulture), PressWords.TextOrEmpty(text, verdictKey));
        }

        /// <summary>
        /// The tag and the arguments of the letter. <paramref name="more"/> turns the number of the reviews left out into "…and N more reviews in the archive"
        /// (null: the line is left out). An empty digest gives no arguments, and the caller does not send it.
        /// </summary>
        public static DigestMailContent Build(DigestContent content, DateTime today, PressText text, Func<int, string> more, ReviewConfig cfg)
        {
            if (content == null) throw new ArgumentNullException("content");
            if (text == null) throw new ArgumentNullException("text");

            var result = new DigestMailContent { Tag = TagOf(today) };
            if (content.IsEmpty) return result;

            var lines = new List<string>(content.Lines.Count + 1);
            for (int i = 0; i < content.Lines.Count; i++) lines.Add(LineOf(content.Lines[i], text, cfg));
            if (content.More > 0 && more != null)
            {
                string tail = more(content.More);
                if (!string.IsNullOrWhiteSpace(tail)) lines.Add(tail);
            }

            result.Args = new[] { string.Join("\n", lines.ToArray()) };
            return result;
        }
    }
}
