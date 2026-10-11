using System;
using System.Collections.Generic;
using System.Globalization;

namespace PTMod.Features.PressReviews
{
    /// <summary>
    /// The articles other features publish through <c>PressReviewsApi.PublishArticle</c> (FR-11 item 2, design.md Component 11): what an article must have,
    /// how its arguments become the strings the tables fill in, the tag of its letter (<c>press:article:&lt;n&gt;</c>) and the log of the last 200 in
    /// <see cref="PressState.articles"/>. Pure.
    /// </summary>
    public static class ArticleLog
    {
        /// <summary>The prefix of the tag of an article's letter; the number of the article follows.</summary>
        public const string TagPrefix = "press:article:";

        /// <summary>The longest text an argument may have; more is cut.</summary>
        public const int MaxArgLength = 400;

        /// <summary>The most arguments an article may carry.</summary>
        public const int MaxArgs = 8;

        /// <summary>An article needs the key of its title; the key of the body falls back to it.</summary>
        public static bool IsValid(string titleKey)
        {
            return !string.IsNullOrWhiteSpace(titleKey);
        }

        /// <summary>The tag of the letter of the <paramref name="number"/>th article.</summary>
        public static string TagOf(int number)
        {
            return TagPrefix + number.ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// The number the next article gets: one more than the highest number among the tags of the letters sent so far (1 when there are none). The
        /// letters are the one place the numbers are kept, so the count survives a load and is never reused.
        /// </summary>
        public static int NextNumber(IEnumerable<string> tags)
        {
            int highest = 0;
            if (tags != null)
            {
                foreach (string tag in tags)
                {
                    int number;
                    if (TryParseTag(tag, out number) && number > highest) highest = number;
                }
            }
            return highest == int.MaxValue ? int.MaxValue : highest + 1;
        }

        public static bool TryParseTag(string tag, out int number)
        {
            number = 0;
            if (string.IsNullOrEmpty(tag) || !tag.StartsWith(TagPrefix, StringComparison.Ordinal)) return false;
            return int.TryParse(tag.Substring(TagPrefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out number);
        }

        /// <summary>The arguments as the strings the tables fill into <c>{0}</c> …: a number or a date in the invariant culture, text as it is, null as empty.</summary>
        public static List<string> Stringify(IList<object> args)
        {
            var result = new List<string>();
            if (args == null) return result;

            for (int i = 0; i < args.Count && i < MaxArgs; i++)
            {
                string text = Convert.ToString(args[i], CultureInfo.InvariantCulture) ?? string.Empty;
                result.Add(text.Length > MaxArgLength ? text.Substring(0, MaxArgLength) : text);
            }
            return result;
        }

        /// <summary>
        /// Files an article in the log: the newest is last, the log keeps <see cref="PressStateCodec.MaxArticles"/>. The source is the id of the feature
        /// that published it (empty for none). The record is returned; null when there is no title key.
        /// </summary>
        public static ArticleRecord Add(PressState state, DateTime today, string source, string titleKey, string bodyKey, IList<string> args)
        {
            if (state == null) throw new ArgumentNullException("state");
            if (!IsValid(titleKey)) return null;

            var record = new ArticleRecord
            {
                date = PressDates.ToText(today),
                source = source ?? string.Empty,
                titleKey = titleKey,
                bodyKey = string.IsNullOrWhiteSpace(bodyKey) ? string.Empty : bodyKey,
                args = args == null ? new List<string>() : new List<string>(args),
            };

            if (state.articles == null) state.articles = new List<ArticleRecord>();
            state.articles.Add(record);
            if (state.articles.Count > PressStateCodec.MaxArticles) state.articles.RemoveRange(0, state.articles.Count - PressStateCodec.MaxArticles);
            return record;
        }

        /// <summary>
        /// Is an article with this source, title and last argument in the log already? The press itself uses it so one flaw is announced once
        /// (the last argument is the id of the flaw record); the log is saved, so the answer survives a load.
        /// </summary>
        public static bool Has(PressState state, string source, string titleKey, string lastArg)
        {
            if (state == null || state.articles == null) return false;

            for (int i = state.articles.Count - 1; i >= 0; i--)
            {
                ArticleRecord article = state.articles[i];
                if (article == null) continue;
                if (!string.Equals(article.source, source ?? string.Empty, StringComparison.Ordinal)) continue;
                if (!string.Equals(article.titleKey, titleKey, StringComparison.Ordinal)) continue;

                int count = article.args == null ? 0 : article.args.Count;
                string last = count == 0 ? string.Empty : article.args[count - 1];
                if (string.Equals(last, lastArg ?? string.Empty, StringComparison.Ordinal)) return true;
            }
            return false;
        }
    }
}
