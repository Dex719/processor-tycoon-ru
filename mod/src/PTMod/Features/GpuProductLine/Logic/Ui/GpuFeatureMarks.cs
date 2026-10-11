using System;
using System.Collections.Generic;

namespace PTMod.Features.GpuProductLine
{
    /// <summary>
    /// What a card lacks in one segment: the functions of the architecture the segment asks for this year and the card has not, and what that does to the rating there
    /// (AC-11.2: "рейтинг 0 и пометка «Нет: ...»").
    /// </summary>
    public sealed class GpuFeatureMark
    {
        public string SegmentId = string.Empty;
        public string SegmentName = string.Empty;

        /// <summary>The ids of the functions the card lacks.</summary>
        public string[] Missing = new string[0];

        /// <summary><c>missingFeatureFactor ^ the number of the missing functions</c> (F34): 0 means the card does not sell in the segment at all.</summary>
        public float Factor = 1f;

        public bool NoSales
        {
            get { return Factor <= 0f; }
        }

        /// <summary>"Нет: 3D, шейдеры".</summary>
        public string Text = string.Empty;

        /// <summary>"рейтинг ×0,35", or "рейтинг 0: в сегменте не продаётся".</summary>
        public string Effect = string.Empty;

        /// <summary>The whole mark of the segment: "Нет: вычисления (GPGPU) — «Вычисления»: рейтинг 0, в сегменте не продаётся".</summary>
        public string Line = string.Empty;
    }

    /// <summary>
    /// The marks "Нет: &lt;функция&gt;" (FR-11 item 2, AC-11.2) for the lineup and the market: the segments that are open today ask for the functions of the year
    /// (<see cref="GpuSegmentView.RequiredFeatures"/>, F34), and a card whose architecture lacks one is rated <c>missingFeatureFactor</c> times lower for each (0 in the segments of
    /// computing and AI: no function, no sales). A card of the player competes in every open segment, so the marks are asked for all of them. The designer says the same about a card
    /// that is only designed (<see cref="GpuSegmentEstimate.MissingFeatures"/>).
    /// </summary>
    public static class GpuFeatureMarks
    {
        /// <summary>
        /// The marks of a card with the functions <paramref name="have"/> in the segments of <paramref name="segmentIds"/> (null: every open one) that are open now; a segment where
        /// nothing is missing has no mark. The order is the order of the segments.
        /// </summary>
        public static List<GpuFeatureMark> For(string[] have, ICollection<string> segmentIds, IEnumerable<GpuSegmentView> open, GpuContentDb content, IGpuText text)
        {
            var marks = new List<GpuFeatureMark>();
            if (open == null) return marks;
            string[] owned = have ?? new string[0];

            foreach (GpuSegmentView view in open)
            {
                if (view == null || (segmentIds != null && !segmentIds.Contains(view.Id))) continue;
                string[] required = view.RequiredFeatures ?? new string[0];
                var missing = new List<string>();
                foreach (string need in required)
                {
                    if (Array.IndexOf(owned, need) < 0) missing.Add(need);
                }
                if (missing.Count == 0) continue;

                GpuSegmentDto segment = content != null ? content.AnySegment(view.Id) : null;
                float factor = GpuMarketMath.FeatureFactor(owned, required, segment != null ? segment.missingFeatureFactor : 1f);
                marks.Add(Describe(view.Id, content, text, missing.ToArray(), factor));
            }
            return marks;
        }

        /// <summary>The mark of a card in one segment, in words.</summary>
        public static GpuFeatureMark Describe(string segmentId, GpuContentDb content, IGpuText text, string[] missing, float factor)
        {
            var mark = new GpuFeatureMark
            {
                SegmentId = segmentId ?? string.Empty,
                SegmentName = GpuNames.Segment(content, text, segmentId),
                Missing = missing ?? new string[0],
                Factor = factor,
            };
            mark.Text = text.L("gpu.mark.missing", GpuNames.Features(text, mark.Missing));
            mark.Effect = mark.NoSales
                ? text.L("gpu.mark.zero")
                : text.L("gpu.mark.factor", GpuUiMath.Plain((float)Math.Round(factor, 2)));
            mark.Line = text.L("gpu.mark.line", mark.Text, mark.SegmentName, mark.Effect);
            return mark;
        }

        /// <summary>The marks joined into lines of text, one mark a line.</summary>
        public static string Lines(IEnumerable<GpuFeatureMark> marks)
        {
            var lines = new List<string>();
            if (marks != null)
            {
                foreach (GpuFeatureMark mark in marks) lines.Add(mark.Line);
            }
            return string.Join("\n", lines.ToArray());
        }

        /// <summary>The ids of the segments a card is in (its <c>segIds</c>): the marks of a card are asked only for them.</summary>
        public static HashSet<string> SegmentSet(IEnumerable<string> segmentIds)
        {
            var set = new HashSet<string>(StringComparer.Ordinal);
            if (segmentIds != null)
            {
                foreach (string id in segmentIds)
                {
                    if (!string.IsNullOrEmpty(id)) set.Add(id);
                }
            }
            return set;
        }
    }
}
