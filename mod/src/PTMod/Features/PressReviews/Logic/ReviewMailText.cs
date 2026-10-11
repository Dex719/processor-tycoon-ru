using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace PTMod.Features.PressReviews
{
    /// <summary>What goes into the letter about a review of the player's CPU: its tag and the arguments of its title and body.</summary>
    public sealed class ReviewMailContent
    {
        /// <summary><c>press:review:&lt;cpuSaveId&gt;</c>.</summary>
        public string Tag = string.Empty;

        /// <summary>
        /// The arguments of the title and of the body (the core fills both texts with the same list): 0 the CPU, 1 the metascore, 2 the verdict, 3 the
        /// outlets with their quotes, 4 the effects in words.
        /// </summary>
        public string[] Args = new string[0];
    }

    /// <summary>
    /// The letter about the review of a CPU of the player (FR-4 item 1, NFR-5): the title "Reviews: X — 84/100", the verdict, the marks of the outlets with
    /// one quote each and the effects in words ("+4 reputation", "popularity grows faster by 36 % over a year"). The tag lets the letter be found again
    /// and lets its button open the right card. Pure: the texts come in through <see cref="PressText"/>.
    /// </summary>
    public static class ReviewMailText
    {
        /// <summary>The prefix of the tag of the letters about reviews; the CPU's save id follows.</summary>
        public const string TagPrefix = "press:review:";

        /// <summary>The tag of the letter about the review of a CPU.</summary>
        public static string TagOf(int cpuSaveId)
        {
            return TagPrefix + cpuSaveId.ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>The CPU a tag <c>press:review:&lt;id&gt;</c> is about; false for any other tag.</summary>
        public static bool TryParseTag(string tag, out int cpuSaveId)
        {
            cpuSaveId = 0;
            if (string.IsNullOrEmpty(tag) || !tag.StartsWith(TagPrefix, StringComparison.Ordinal)) return false;

            string number = tag.Substring(TagPrefix.Length);
            return number.Length > 0 && int.TryParse(number, NumberStyles.None, CultureInfo.InvariantCulture, out cpuSaveId);
        }

        /// <summary>The tag and the arguments of the letter. The context needs the texts and may carry the names of the outlets and the reputation step.</summary>
        public static ReviewMailContent Build(ReviewRecord record, ReviewCardContext context)
        {
            if (record == null) throw new ArgumentNullException("record");
            if (context == null || context.Text == null) throw new ArgumentNullException("context");

            PressText text = context.Text;
            string verdictKey = !string.IsNullOrEmpty(record.verdictKey) ? record.verdictKey : OutletPanel.VerdictKey(record.metascore, context.Config);

            return new ReviewMailContent
            {
                Tag = TagOf(record.cpuSaveId),
                Args = new[]
                {
                    PressWords.Plain(record.cpuName),
                    record.metascore.ToString(CultureInfo.InvariantCulture),
                    PressWords.TextOrEmpty(text, verdictKey),
                    OutletLines(record, context),
                    string.Join("\n", ReviewCard.EffectLines(record, context.Reputation, text).ToArray()),
                },
            };
        }

        /// <summary>One line per outlet: the name, the mark and the quote it was given. An old compact record has none, and the line says so.</summary>
        private static string OutletLines(ReviewRecord record, ReviewCardContext context)
        {
            PressText text = context.Text;
            if (record.outlets == null || record.outlets.Count == 0) return text("press.mail.review.noOutlets");

            var lines = new StringBuilder();
            for (int i = 0; i < record.outlets.Count; i++)
            {
                OutletLine line = record.outlets[i];
                if (line == null) continue;

                string name = ReviewCard.OutletNameOf(context, line.outletId);
                string quote = string.IsNullOrEmpty(line.quoteKey) ? null : text(line.quoteKey);
                if (lines.Length > 0) lines.Append('\n');
                lines.Append(string.IsNullOrEmpty(quote) ? text("press.mail.review.line", name, line.score) : text("press.mail.review.lineQuote", name, line.score, quote));
            }
            return lines.Length == 0 ? text("press.mail.review.noOutlets") : lines.ToString();
        }
    }
}
