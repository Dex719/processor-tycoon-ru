using System;
using System.Collections.Generic;

namespace PTMod.Features.PressReviews
{
    /// <summary>How a mark is painted: the colour of the theme's good news, of its bad news, or the neutral colour (FR-4 item 2, design.md "UI").</summary>
    public enum ScoreTone
    {
        Neutral = 0,
        Positive = 1,
        Negative = 2,
    }

    /// <summary>The reputation of a company before and after the change a review made, read from its history (null when the history no longer has the entry).</summary>
    public sealed class ReputationStep
    {
        public int Before;
        public int After;
    }

    /// <summary>One of the four axes of the card: the name, the mark, the word for the mark and the length of the bar.</summary>
    public sealed class CardAxisRow
    {
        public string Name = string.Empty;
        public int Score;

        /// <summary>The word for the mark: weak, average, good, excellent (NFR-5: every axis has a word and a number).</summary>
        public string Word = string.Empty;

        /// <summary>The mark as the line shows it: <c>73 — good</c>.</summary>
        public string Value = string.Empty;

        /// <summary>0…1, the share of the bar that is filled.</summary>
        public float Fraction;

        public ScoreTone Tone;
    }

    /// <summary>One outlet of the card: its name, its mark and the quote it was given (null when it has none).</summary>
    public sealed class CardOutletRow
    {
        public string Name = string.Empty;
        public int Score;
        public string Quote;
    }

    /// <summary>What the window of a review shows, as ready text (FR-4 item 2). Nothing here refers to Unity.</summary>
    public sealed class ReviewCardModel
    {
        public int CpuSaveId;

        /// <summary><c>Company CPU</c>.</summary>
        public string Title = string.Empty;

        /// <summary>The segment and the date of the review.</summary>
        public string Subtitle = string.Empty;

        public int Metascore;

        /// <summary>The name of the verdict.</summary>
        public string Verdict = string.Empty;

        /// <summary>The big line: <c>84/100 — Excellent</c>.</summary>
        public string Headline = string.Empty;

        public ScoreTone Tone;

        /// <summary>The record is the short form of the old archive: it has no axes, outlets or reference CPUs.</summary>
        public bool Compact;

        /// <summary>The line that says why a compact record has no details; empty for a full one.</summary>
        public string CompactNote = string.Empty;

        public List<CardAxisRow> Axes = new List<CardAxisRow>();
        public List<CardOutletRow> Outlets = new List<CardOutletRow>();

        /// <summary>"Compared against: …" or "No direct competitors"; empty for a compact record.</summary>
        public string Compared = string.Empty;

        /// <summary>The effects in words: the reputation and the popularity.</summary>
        public List<string> Effects = new List<string>();

        /// <summary>The notes of the adjusters and of the press itself, as text.</summary>
        public List<string> Notes = new List<string>();
    }

    /// <summary>What <see cref="ReviewCard.Build"/> needs beside the record: the texts and the lookups of the game.</summary>
    public sealed class ReviewCardContext
    {
        /// <summary>The strings by key (required).</summary>
        public PressText Text;

        /// <summary>The name of an outlet by its id; null means the id itself is shown.</summary>
        public Func<string, string> OutletName;

        /// <summary>The name of a reference CPU by its save id, with its company (<c>P5-90 (Intel)</c>); null for a CPU the game no longer has.</summary>
        public Func<int, string> PeerName;

        /// <summary>The reputation before and after the review's change; null when it is not known.</summary>
        public ReputationStep Reputation;

        /// <summary>The verdicts for a record that carries no verdict key; the built-in numbers when null.</summary>
        public ReviewConfig Config;
    }

    /// <summary>
    /// The card of a review and the effects of it in words (FR-4 items 1-2, NFR-5, design.md "UI"): the metascore and the verdict with their tone, four
    /// axes (a number, a word and a bar each), the outlets with their quotes, the reference CPUs by name, the effects and the notes. Formulas and
    /// coefficients are not shown; the explanations are qualitative. Pure: the texts come in through <see cref="PressText"/>.
    /// </summary>
    public static class ReviewCard
    {
        /// <summary>A metascore from here on is painted as good news (design.md: 70 and above).</summary>
        public const int PositiveFrom = 70;

        /// <summary>A metascore below this is painted as bad news (design.md: below 55).</summary>
        public const int NegativeBelow = 55;

        /// <summary>An axis from here on is "good" (65…84) and painted as good news.</summary>
        public const int AxisGoodFrom = 65;

        /// <summary>An axis from here on is "excellent".</summary>
        public const int AxisExcellentFrom = 85;

        /// <summary>An axis below this is "weak" and painted as bad news; from here to <see cref="AxisGoodFrom"/> it is "average".</summary>
        public const int AxisWeakBelow = 40;

        /// <summary>The tone of the metascore: 70 and above positive, below 55 negative, otherwise neutral.</summary>
        public static ScoreTone ToneOf(int metascore)
        {
            if (metascore >= PositiveFrom) return ScoreTone.Positive;
            if (metascore < NegativeBelow) return ScoreTone.Negative;
            return ScoreTone.Neutral;
        }

        /// <summary>The tone of an axis: from 65 positive, below 40 negative, otherwise neutral.</summary>
        public static ScoreTone AxisToneOf(float score)
        {
            int whole = PressWords.Whole(score);
            if (whole >= AxisGoodFrom) return ScoreTone.Positive;
            if (whole < AxisWeakBelow) return ScoreTone.Negative;
            return ScoreTone.Neutral;
        }

        /// <summary>The key of the word for an axis mark: below 40 weak, 40…64 average, 65…84 good, 85 and above excellent. The mark is rounded first.</summary>
        public static string AxisWordKey(float score)
        {
            int whole = PressWords.Whole(score);
            if (whole >= AxisExcellentFrom) return "press.axis.word.excellent";
            if (whole >= AxisGoodFrom) return "press.axis.word.good";
            if (whole >= AxisWeakBelow) return "press.axis.word.average";
            return "press.axis.word.weak";
        }

        /// <summary>The card of a review. A record without a context text cannot be shown: the call throws <see cref="ArgumentNullException"/>.</summary>
        public static ReviewCardModel Build(ReviewRecord record, ReviewCardContext context)
        {
            if (record == null) throw new ArgumentNullException("record");
            if (context == null || context.Text == null) throw new ArgumentNullException("context");

            PressText text = context.Text;
            var model = new ReviewCardModel
            {
                CpuSaveId = record.cpuSaveId,
                Title = Join(record.companyName, record.cpuName),
                Metascore = record.metascore,
                Tone = ToneOf(record.metascore),
                Compact = record.compact,
            };

            string verdictKey = !string.IsNullOrEmpty(record.verdictKey) ? record.verdictKey : OutletPanel.VerdictKey(record.metascore, context.Config);
            model.Verdict = PressWords.TextOrEmpty(text, verdictKey);
            model.Headline = text("press.card.score", record.metascore, model.Verdict);
            model.Subtitle = text("press.card.subtitle", text(PressWords.SegmentKey(record.segment)), record.publishedOn ?? string.Empty);

            if (record.compact || record.axes == null || record.axes.Length != 4)
            {
                model.Compact = true;
                model.CompactNote = text("press.card.compact");
            }
            else
            {
                AddAxes(model, record, text);
                AddOutlets(model, record, context);
                model.Compared = ComparedLine(record, context);
            }

            model.Effects.AddRange(EffectLines(record, context.Reputation, text));
            AddNotes(model, record, text);
            return model;
        }

        /// <summary>
        /// The effects of a review in words (NFR-5): the change of the reputation (with the values before and after when they are known) and what the
        /// hype does to the popularity of the CPU for a year. Used by the card and by the letter.
        /// </summary>
        public static List<string> EffectLines(ReviewRecord record, ReputationStep step, PressText text)
        {
            if (record == null) throw new ArgumentNullException("record");
            if (text == null) throw new ArgumentNullException("text");

            var lines = new List<string>();
            int delta = record.reputationDelta;
            if (delta == 0) lines.Add(text("press.effects.reputation.none"));
            else if (step != null) lines.Add(text("press.effects.reputation", delta, step.Before, step.After));
            else lines.Add(text("press.effects.reputation.short", delta));

            int percent = PressWords.Whole((record.hypeAtPublish - 1f) * 100f);
            if (percent > 0) lines.Add(text("press.effects.hype.up", percent));
            else if (percent < 0) lines.Add(text("press.effects.hype.down", -percent));
            else lines.Add(text("press.effects.hype.none"));
            return lines;
        }

        private static void AddAxes(ReviewCardModel model, ReviewRecord record, PressText text)
        {
            for (int i = 0; i < record.axes.Length; i++)
            {
                float score = record.axes[i];
                string word = text(AxisWordKey(score));
                int whole = PressWords.Whole(score);
                model.Axes.Add(new CardAxisRow
                {
                    Name = PressWords.TextOrEmpty(text, PressWords.AxisKey(i)),
                    Score = whole,
                    Word = word,
                    Value = text("press.card.axisValue", whole, word),
                    Fraction = Math.Max(0f, Math.Min(1f, score / 100f)),
                    Tone = AxisToneOf(score),
                });
            }
        }

        private static void AddOutlets(ReviewCardModel model, ReviewRecord record, ReviewCardContext context)
        {
            if (record.outlets == null) return;

            for (int i = 0; i < record.outlets.Count; i++)
            {
                OutletLine line = record.outlets[i];
                if (line == null) continue;

                model.Outlets.Add(new CardOutletRow
                {
                    Name = OutletNameOf(context, line.outletId),
                    Score = line.score,
                    Quote = string.IsNullOrEmpty(line.quoteKey) ? null : context.Text(line.quoteKey),
                });
            }
        }

        /// <summary>The name of an outlet for the card and the letter: the lookup of the context, otherwise the id.</summary>
        public static string OutletNameOf(ReviewCardContext context, string outletId)
        {
            string id = outletId ?? string.Empty;
            if (context == null || context.OutletName == null) return id;

            string name = context.OutletName(id);
            return string.IsNullOrEmpty(name) ? id : name;
        }

        private static string ComparedLine(ReviewRecord record, ReviewCardContext context)
        {
            PressText text = context.Text;
            if (record.noAnalog) return text("press.noAnalog");
            if (record.peerSaveIds == null || record.peerSaveIds.Count == 0) return string.Empty;

            var names = new List<string>();
            for (int i = 0; i < record.peerSaveIds.Count; i++)
            {
                string name = context.PeerName == null ? null : context.PeerName(record.peerSaveIds[i]);
                if (!string.IsNullOrEmpty(name)) names.Add(name);
            }
            if (names.Count == 0) return text("press.compare.gone");
            return text("press.compare.with", string.Join(", ", names.ToArray()));
        }

        private static void AddNotes(ReviewCardModel model, ReviewRecord record, PressText text)
        {
            if (record.notes == null) return;

            for (int i = 0; i < record.notes.Count; i++)
            {
                string key = record.notes[i];
                if (string.IsNullOrEmpty(key)) continue;
                model.Notes.Add(text(key));
            }
        }

        private static string Join(string company, string cpu)
        {
            string a = (company ?? string.Empty).Trim();
            string b = (cpu ?? string.Empty).Trim();
            if (a.Length == 0) return b;
            if (b.Length == 0) return a;
            return a + " " + b;
        }
    }
}
