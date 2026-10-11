using System;
using System.Collections.Generic;
using System.Globalization;

namespace PTMod.Features.PressReviews
{
    /// <summary>The yearly awards of one year as lines of text (FR-8, FR-7: the tab "Awards of the year" reads the state).</summary>
    public sealed class AwardsYearView
    {
        public int Year;

        /// <summary>The heading: "Awards of the year 1995".</summary>
        public string Heading = string.Empty;

        /// <summary>The winners: a line per "CPU of the Year", then the company of the year, then the disappointment. Empty when the year has none.</summary>
        public List<string> Lines = new List<string>();
    }

    /// <summary>One article another feature published (<c>PressReviewsApi.PublishArticle</c>), ready to be shown in the tab "Articles".</summary>
    public sealed class ArticleView
    {
        public string Date = string.Empty;
        public string Title = string.Empty;
        public string Body = string.Empty;

        /// <summary>The line "Source: Design flaws and recalls"; empty when the article names no source.</summary>
        public string Source = string.Empty;
    }

    /// <summary>
    /// The tabs "Awards of the year" and "Articles" of the window "Press" (FR-7, FR-8, FR-11 item 3): the state of the feature as text, the newest year
    /// and the newest article first. Pure; the window shows the lines as they come.
    /// </summary>
    public static class PressTabViews
    {
        /// <summary>The names the awards need: a company by its key and a reviewed CPU by its save id (<c>Company CPU</c>, null when unknown).</summary>
        public sealed class AwardsLookups
        {
            public Func<string, string> CompanyName;
            public Func<int, string> ReviewedCpu;
        }

        /// <summary>The years of the awards, the latest first; a year without a single winner is kept, with no lines.</summary>
        public static List<AwardsYearView> Awards(IList<YearAwards> awards, AwardsLookups lookups, PressText text)
        {
            if (text == null) throw new ArgumentNullException("text");

            var years = new List<AwardsYearView>();
            if (awards == null) return years;

            for (int i = 0; i < awards.Count; i++)
            {
                YearAwards year = awards[i];
                if (year == null) continue;
                years.Add(AwardsOf(year, lookups ?? new AwardsLookups(), text));
            }
            years.Sort((a, b) => b.Year.CompareTo(a.Year));
            return years;
        }

        /// <summary>The articles, the newest first (by date, a later one in the list first within a day).</summary>
        public static List<ArticleView> Articles(IList<ArticleRecord> articles, Func<string, string> sourceName, PressText text)
        {
            if (text == null) throw new ArgumentNullException("text");

            var found = new List<KeyValuePair<int, ArticleRecord>>();
            if (articles != null)
            {
                for (int i = 0; i < articles.Count; i++)
                {
                    if (articles[i] != null) found.Add(new KeyValuePair<int, ArticleRecord>(i, articles[i]));
                }
            }
            found.Sort((a, b) =>
            {
                int byDate = string.CompareOrdinal(b.Value.date ?? string.Empty, a.Value.date ?? string.Empty);
                return byDate != 0 ? byDate : b.Key.CompareTo(a.Key);
            });

            var views = new List<ArticleView>(found.Count);
            for (int i = 0; i < found.Count; i++) views.Add(ArticleOf(found[i].Value, sourceName, text));
            return views;
        }

        private static AwardsYearView AwardsOf(YearAwards year, AwardsLookups lookups, PressText text)
        {
            var view = new AwardsYearView { Year = year.year, Heading = text("press.awards.year", year.year) };

            if (year.cpuOfYear != null)
            {
                for (int i = 0; i < year.cpuOfYear.Count; i++)
                {
                    AwardLine line = year.cpuOfYear[i];
                    if (line == null) continue;

                    string label = Label(CompanyOf(lookups, line.companyKey), line.cpuName);
                    view.Lines.Add(text("press.awards.cpuOfYear", text(PressWords.SegmentKey(line.segment)), label, Score(line.score)));
                }
            }

            if (!string.IsNullOrEmpty(year.companyOfYearKey))
            {
                string company = CompanyOf(lookups, year.companyOfYearKey);
                if (string.IsNullOrEmpty(company)) company = year.companyOfYearKey;
                view.Lines.Add(text("press.awards.companyOfYear", PressWords.Plain(company),
                    year.companyOfYearAvg.ToString("0.0", CultureInfo.InvariantCulture)));
            }

            if (year.disappointmentCpuSaveId >= 0 && year.disappointmentScore > 0)
            {
                string label = lookups.ReviewedCpu == null ? null : lookups.ReviewedCpu(year.disappointmentCpuSaveId);
                if (string.IsNullOrEmpty(label)) label = text("press.awards.unknownCpu");
                view.Lines.Add(text("press.awards.disappointment", PressWords.Plain(label), Score(year.disappointmentScore)));
            }
            return view;
        }

        private static ArticleView ArticleOf(ArticleRecord article, Func<string, string> sourceName, PressText text)
        {
            object[] args = article.args == null ? new object[0] : article.args.ToArray();
            var view = new ArticleView
            {
                Date = article.date ?? string.Empty,
                Title = string.IsNullOrEmpty(article.titleKey) ? string.Empty : text(article.titleKey, args),
                Body = string.IsNullOrEmpty(article.bodyKey) ? string.Empty : text(article.bodyKey, args),
            };

            if (!string.IsNullOrEmpty(article.source))
            {
                string name = sourceName == null ? null : sourceName(article.source);
                view.Source = text("press.articles.source", string.IsNullOrEmpty(name) ? article.source : name);
            }
            return view;
        }

        private static string CompanyOf(AwardsLookups lookups, string companyKey)
        {
            if (string.IsNullOrEmpty(companyKey) || lookups.CompanyName == null) return string.Empty;
            return lookups.CompanyName(companyKey) ?? string.Empty;
        }

        private static string Label(string company, string cpu)
        {
            string a = PressWords.Plain(company).Trim();
            string b = PressWords.Plain(cpu).Trim();
            if (a.Length == 0) return b;
            return b.Length == 0 ? a : a + " " + b;
        }

        private static string Score(int score)
        {
            return score.ToString(CultureInfo.InvariantCulture) + "/100";
        }
    }
}
