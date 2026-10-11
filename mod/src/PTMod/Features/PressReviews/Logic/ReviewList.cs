using System;
using System.Collections.Generic;

namespace PTMod.Features.PressReviews
{
    /// <summary>The filters of the list of reviews (FR-9 item 2): a company, a segment, a year. A filter that is not set lets everything through.</summary>
    public sealed class ReviewFilter
    {
        /// <summary>The key of the company (<c>player</c>, <c>ai:3</c>); null for all companies.</summary>
        public string CompanyKey;

        /// <summary><c>(int)Segment</c>, 0…3; -1 for all segments.</summary>
        public int Segment = -1;

        /// <summary>The year of the publication; 0 for all years.</summary>
        public int Year;

        /// <summary>No filter is set.</summary>
        public bool IsEmpty
        {
            get { return CompanyKey == null && Segment < 0 && Year <= 0; }
        }
    }

    /// <summary>A company in the filter of the list: its key and the name it was reviewed under.</summary>
    public sealed class CompanyOption
    {
        public string Key = string.Empty;
        public string Name = string.Empty;
    }

    /// <summary>One line of the list of reviews as columns of text (date, company, CPU, segment, score, verdict).</summary>
    public sealed class ReviewListRow
    {
        public int CpuSaveId;
        public string[] Columns = new string[0];
        public ScoreTone Tone;
    }

    /// <summary>
    /// The list of the archive of reviews (FR-9, design.md "Окно «Пресса»"): the newest first, filtered by company, segment and year, cut into pages of
    /// <see cref="PageSize"/>, with the options of the three filters taken from the archive itself. Pure; the window only shows what it returns.
    /// </summary>
    public static class ReviewList
    {
        /// <summary>The rows on one page (FR-9: 50).</summary>
        public const int PageSize = 50;

        /// <summary>The reviews that pass the filter, the newest first (by the day of the publication, and the later one in the archive first within a day).</summary>
        public static List<ReviewRecord> Select(IList<ReviewRecord> archive, ReviewFilter filter)
        {
            var found = new List<KeyValuePair<int, ReviewRecord>>();
            if (archive != null)
            {
                for (int i = 0; i < archive.Count; i++)
                {
                    ReviewRecord record = archive[i];
                    if (record != null && Passes(record, filter)) found.Add(new KeyValuePair<int, ReviewRecord>(i, record));
                }
            }

            found.Sort(Newest);
            var result = new List<ReviewRecord>(found.Count);
            for (int i = 0; i < found.Count; i++) result.Add(found[i].Value);
            return result;
        }

        /// <summary>The number of pages for a number of reviews: at least 1, so an empty list is "page 1 of 1".</summary>
        public static int PageCount(int count, int pageSize = PageSize)
        {
            int size = Math.Max(1, pageSize);
            if (count <= 0) return 1;
            return (count + size - 1) / size;
        }

        /// <summary>The zero-based page of an already ordered list; a page outside the list is cut to the nearest one.</summary>
        public static List<ReviewRecord> Page(IList<ReviewRecord> ordered, int page, int pageSize = PageSize)
        {
            var result = new List<ReviewRecord>();
            if (ordered == null || ordered.Count == 0) return result;

            int size = Math.Max(1, pageSize);
            int index = ClampPage(page, ordered.Count, size);
            int from = index * size;
            int to = Math.Min(ordered.Count, from + size);
            for (int i = from; i < to; i++) result.Add(ordered[i]);
            return result;
        }

        /// <summary>The page number (zero-based) held to the pages a list of <paramref name="count"/> reviews has.</summary>
        public static int ClampPage(int page, int count, int pageSize = PageSize)
        {
            int last = PageCount(count, pageSize) - 1;
            if (page < 0) return 0;
            return page > last ? last : page;
        }

        /// <summary>The companies the archive has reviews of: the player first, then the others by name.</summary>
        public static List<CompanyOption> Companies(IList<ReviewRecord> archive)
        {
            var byKey = new Dictionary<string, CompanyOption>(StringComparer.Ordinal);
            var order = new List<CompanyOption>();
            if (archive != null)
            {
                for (int i = 0; i < archive.Count; i++)
                {
                    ReviewRecord record = archive[i];
                    if (record == null || string.IsNullOrEmpty(record.companyKey)) continue;

                    CompanyOption option;
                    if (!byKey.TryGetValue(record.companyKey, out option))
                    {
                        option = new CompanyOption { Key = record.companyKey };
                        byKey[record.companyKey] = option;
                        order.Add(option);
                    }
                    if (!string.IsNullOrEmpty(record.companyName)) option.Name = record.companyName;   // the latest name wins
                }
            }

            foreach (CompanyOption option in order)
            {
                if (string.IsNullOrEmpty(option.Name)) option.Name = option.Key;
            }
            order.Sort(CompareCompanies);
            return order;
        }

        /// <summary>The years the archive has reviews of, the latest first.</summary>
        public static List<int> Years(IList<ReviewRecord> archive)
        {
            var years = new List<int>();
            if (archive != null)
            {
                for (int i = 0; i < archive.Count; i++)
                {
                    int year = YearOf(archive[i]);
                    if (year > 0 && !years.Contains(year)) years.Add(year);
                }
            }

            years.Sort((a, b) => b.CompareTo(a));
            return years;
        }

        /// <summary>The year a review was published in; 0 when its date does not parse.</summary>
        public static int YearOf(ReviewRecord record)
        {
            DateTime date;
            if (record == null || !PressDates.TryParse(record.publishedOn, out date)) return 0;
            return date.Year;
        }

        /// <summary>The columns of one row: the date, the company, the CPU, the segment, the metascore and the verdict.</summary>
        public static ReviewListRow Row(ReviewRecord record, PressText text)
        {
            if (record == null) throw new ArgumentNullException("record");
            if (text == null) throw new ArgumentNullException("text");

            return new ReviewListRow
            {
                CpuSaveId = record.cpuSaveId,
                Tone = ReviewCard.ToneOf(record.metascore),
                Columns = new[]
                {
                    record.publishedOn ?? string.Empty,
                    PressWords.Plain(record.companyName),
                    PressWords.Plain(record.cpuName),
                    text(PressWords.SegmentKey(record.segment)),
                    record.metascore.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    PressWords.TextOrEmpty(text, record.verdictKey),
                },
            };
        }

        private static bool Passes(ReviewRecord record, ReviewFilter filter)
        {
            if (filter == null) return true;
            if (filter.CompanyKey != null && !string.Equals(record.companyKey, filter.CompanyKey, StringComparison.Ordinal)) return false;
            if (filter.Segment >= 0 && record.segment != filter.Segment) return false;
            if (filter.Year > 0 && YearOf(record) != filter.Year) return false;
            return true;
        }

        private static int Newest(KeyValuePair<int, ReviewRecord> a, KeyValuePair<int, ReviewRecord> b)
        {
            int byDate = string.CompareOrdinal(b.Value.publishedOn ?? string.Empty, a.Value.publishedOn ?? string.Empty);
            return byDate != 0 ? byDate : b.Key.CompareTo(a.Key);
        }

        private static int CompareCompanies(CompanyOption a, CompanyOption b)
        {
            bool aPlayer = string.Equals(a.Key, "player", StringComparison.Ordinal);
            bool bPlayer = string.Equals(b.Key, "player", StringComparison.Ordinal);
            if (aPlayer != bPlayer) return aPlayer ? -1 : 1;

            int byName = string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
            return byName != 0 ? byName : string.CompareOrdinal(a.Key, b.Key);
        }
    }
}
