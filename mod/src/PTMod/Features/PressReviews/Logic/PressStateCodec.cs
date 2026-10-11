using System;
using System.Collections.Generic;
using PTMod.Core.Logic.Json;

namespace PTMod.Features.PressReviews
{
    /// <summary>
    /// Turns the section "press-reviews" of the side save file into a <see cref="PressState"/> and back (FR-12), without the game: the binding is
    /// <c>ModJson</c>, the checks are the validation rules of design.md. <see cref="Read"/> never throws and never returns null: a missing section is the
    /// empty state, a damaged part is dropped or clipped and reported. <see cref="Write"/> produces the data without a version field: the core
    /// writes and reads <c>_schema</c> itself (<c>SchemaVersion</c> of <c>PressSaveAdapter</c>), migrations of older versions belong inside <c>Read</c>.
    /// </summary>
    public static class PressStateCodec
    {
        /// <summary>The version of the section this code reads and writes. <c>PressSaveAdapter.SchemaVersion</c> returns it.</summary>
        public const int SectionVersion = 1;

        public const int MaxReviews = 5000;
        public const int MaxPending = 1000;
        public const int MaxArticles = 200;
        public const int MaxAwards = 100;
        public const int MaxNoteCooldowns = 200;
        public const int MaxYearStats = 12;

        private const int MinYear = 1800;
        private const int MaxYear = 2200;
        private const int MaxAttempts = 100;

        /// <summary>
        /// Reads the section. <paramref name="section"/> is the data of the feature as the core hands it over (no <c>_schema</c>); null or empty gives
        /// the empty state. <paramref name="schema"/> is the stored version: a version below <see cref="SectionVersion"/> is migrated first, step by step
        /// (<see cref="PressStateMigrations"/>; there is nothing to migrate yet), and the section object itself is never modified. Problems are added to
        /// <paramref name="problems"/> (may be null) as English lines.
        /// </summary>
        public static PressState Read(JsonObject section, int schema, List<string> problems)
        {
            if (section == null || section.Count == 0) return new PressState();

            JsonObject current = PressStateMigrations.Upgrade(section, schema, SectionVersion, problems);
            var errors = new List<string>();
            PressState state = ModJson.Bind<PressState>(current, errors);
            if (problems != null) problems.AddRange(errors);
            return Sanitize(state, problems);
        }

        /// <summary>The data of the section as JSON, without <c>_schema</c>. Never null.</summary>
        public static JsonObject Write(PressState state)
        {
            if (state == null) state = new PressState();

            JsonNode node = ModJson.From(state);
            var obj = node as JsonObject;
            return obj ?? new JsonObject();
        }

        /// <summary>
        /// Brings a state to what the rest of the feature can rely on: no null list or item, dates that parse, scores in range, lists within their
        /// limits (the oldest entries go first). The same object is returned.
        /// </summary>
        public static PressState Sanitize(PressState state, List<string> problems)
        {
            if (state == null) return new PressState();

            SanitizePending(state, problems);
            SanitizeReviews(state, problems);
            SanitizeAwards(state, problems);
            SanitizeArticles(state, problems);
            SanitizeCooldowns(state, problems);
            SanitizeYearStats(state, problems);

            DateTime month;
            if (state.lastDigest != null && !PressDates.TryParseMonth(state.lastDigest, out month))
            {
                Report(problems, "lastDigest: '" + state.lastDigest + "' is not a month (yyyy-MM), reset");
                state.lastDigest = null;
            }
            return state;
        }

        // ---- lists ----

        private static void SanitizePending(PressState state, List<string> problems)
        {
            var result = new List<PendingReview>();
            var seen = new HashSet<int>();
            int dropped = 0;

            if (state.pending != null)
            {
                foreach (PendingReview item in state.pending)
                {
                    DateTime date;
                    if (item == null || !PressDates.TryParse(item.publishOn, out date) || !PressDates.TryParse(item.releasedOn, out date) || !seen.Add(item.cpuSaveId))
                    {
                        dropped++;
                        continue;
                    }

                    item.soldBySegment = FourNonNegative(item.soldBySegment);
                    item.attempts = item.attempts < 0 ? 0 : item.attempts > MaxAttempts ? MaxAttempts : item.attempts;
                    if (item.companyKey == null) item.companyKey = string.Empty;
                    result.Add(item);
                }
            }

            if (result.Count > MaxPending)
            {
                dropped += result.Count - MaxPending;
                result.RemoveRange(0, result.Count - MaxPending);
            }
            if (dropped > 0) Report(problems, "pending: " + dropped + " queued review(s) with a bad date, a repeated CPU or beyond the limit were dropped");
            state.pending = result;
        }

        private static void SanitizeReviews(PressState state, List<string> problems)
        {
            var result = new List<ReviewRecord>();
            int dropped = 0;
            int clipped = 0;

            if (state.reviews != null)
            {
                foreach (ReviewRecord record in state.reviews)
                {
                    if (record == null)
                    {
                        dropped++;
                        continue;
                    }

                    int score = Clip(record.metascore, 0, 100);
                    if (score != record.metascore) clipped++;
                    record.metascore = score;

                    if (record.segment < 0 || record.segment > 3)
                    {
                        // Kept (the CPU did get a review), but compact, so it carries no segment into the totals.
                        record.compact = true;
                        clipped++;
                    }

                    if (record.axes == null || record.axes.Length != 4)
                    {
                        record.axes = new float[0];
                        record.compact = true;
                    }
                    else
                    {
                        for (int i = 0; i < record.axes.Length; i++) record.axes[i] = ClipFloat(record.axes[i], 0f, 100f);
                    }

                    record.hypeAtPublish = ClipFloat(record.hypeAtPublish, 0f, 3f);
                    record.reputationDelta = Clip(record.reputationDelta, -50, 50);
                    if (record.verdictKey == null) record.verdictKey = string.Empty;
                    if (record.compact)
                    {
                        record.outlets = null;
                        record.peerSaveIds = null;
                    }
                    else if (record.outlets != null)
                    {
                        record.outlets.RemoveAll(line => line == null);
                        foreach (OutletLine line in record.outlets) line.score = Clip(line.score, 0, 100);
                    }
                    result.Add(record);
                }
            }

            if (result.Count > MaxReviews)
            {
                dropped += result.Count - MaxReviews;
                result.RemoveRange(0, result.Count - MaxReviews);
            }
            if (dropped > 0) Report(problems, "reviews: " + dropped + " empty or surplus record(s) were dropped");
            if (clipped > 0) Report(problems, "reviews: " + clipped + " value(s) were clipped or marked compact");
            state.reviews = result;
        }

        private static void SanitizeAwards(PressState state, List<string> problems)
        {
            var result = new List<YearAwards>();
            int dropped = 0;

            if (state.awards != null)
            {
                foreach (YearAwards year in state.awards)
                {
                    if (year == null || year.year < MinYear || year.year > MaxYear)
                    {
                        dropped++;
                        continue;
                    }
                    if (year.cpuOfYear == null) year.cpuOfYear = new List<AwardLine>();
                    dropped += year.cpuOfYear.RemoveAll(line => line == null || line.segment < 0 || line.segment > 3);
                    result.Add(year);
                }
            }

            if (result.Count > MaxAwards) result.RemoveRange(0, result.Count - MaxAwards);
            if (dropped > 0) Report(problems, "awards: " + dropped + " bad entry(ies) were dropped");
            state.awards = result;
        }

        private static void SanitizeArticles(PressState state, List<string> problems)
        {
            var result = new List<ArticleRecord>();
            int dropped = 0;

            if (state.articles != null)
            {
                foreach (ArticleRecord article in state.articles)
                {
                    if (article == null)
                    {
                        dropped++;
                        continue;
                    }
                    if (article.args == null) article.args = new List<string>();
                    result.Add(article);
                }
            }

            if (result.Count > MaxArticles)
            {
                dropped += result.Count - MaxArticles;
                result.RemoveRange(0, result.Count - MaxArticles);
            }
            if (dropped > 0) Report(problems, "articles: " + dropped + " empty or surplus article(s) were dropped");
            state.articles = result;
        }

        private static void SanitizeCooldowns(PressState state, List<string> problems)
        {
            var result = new List<NoteCooldown>();
            int dropped = 0;

            if (state.noteCooldowns != null)
            {
                foreach (NoteCooldown cooldown in state.noteCooldowns)
                {
                    DateTime until;
                    if (cooldown == null || string.IsNullOrEmpty(cooldown.noteId) || !PressDates.TryParse(cooldown.until, out until))
                    {
                        dropped++;
                        continue;
                    }
                    result.Add(cooldown);
                }
            }

            if (result.Count > MaxNoteCooldowns) result.RemoveRange(0, result.Count - MaxNoteCooldowns);
            if (dropped > 0) Report(problems, "noteCooldowns: " + dropped + " bad entry(ies) were dropped");
            state.noteCooldowns = result;
        }

        private static void SanitizeYearStats(PressState state, List<string> problems)
        {
            var result = new List<SegmentYearStat>();
            int dropped = 0;

            if (state.yearStats != null)
            {
                foreach (SegmentYearStat stat in state.yearStats)
                {
                    if (stat == null || stat.year < MinYear || stat.year > MaxYear || stat.segment < 0 || stat.segment > 3)
                    {
                        dropped++;
                        continue;
                    }
                    result.Add(stat);
                }
            }

            // Only the last three years × four segments are kept (the oldest years go first).
            if (result.Count > MaxYearStats)
            {
                result.Sort((a, b) => a.year != b.year ? a.year.CompareTo(b.year) : a.segment.CompareTo(b.segment));
                result.RemoveRange(0, result.Count - MaxYearStats);
            }
            if (dropped > 0) Report(problems, "yearStats: " + dropped + " bad entry(ies) were dropped");
            state.yearStats = result;
        }

        // ---- helpers ----

        private static int[] FourNonNegative(int[] source)
        {
            var result = new int[4];
            if (source == null) return result;
            for (int i = 0; i < result.Length && i < source.Length; i++) result[i] = source[i] < 0 ? 0 : source[i];
            return result;
        }

        private static int Clip(int value, int min, int max)
        {
            return value < min ? min : value > max ? max : value;
        }

        private static float ClipFloat(float value, float min, float max)
        {
            if (float.IsNaN(value)) return min;
            return value < min ? min : value > max ? max : value;
        }

        private static void Report(List<string> problems, string message)
        {
            if (problems != null) problems.Add(message);
        }
    }
}
