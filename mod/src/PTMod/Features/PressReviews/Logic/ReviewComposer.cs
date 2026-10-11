using System;
using System.Collections.Generic;
using PTMod.Core;

namespace PTMod.Features.PressReviews
{
    /// <summary>
    /// Writes the review of a CPU on the day it is due (FR-1 item 4, design.md Component 3): the four axes of <see cref="ReviewMath"/>, the marks of
    /// the outlets of the year and the segment (<see cref="OutletPanel"/>), the metascore and the verdict, packed in the <see cref="ReviewRecord"/> of
    /// the archive. Pure: the CPU, its reference CPUs and the moment come in as plain data and the dice through <see cref="IRandomSource"/>, so the same
    /// input and the same stream give the same review. The effects of the review (reputation, hype) and the mail are not made here.
    /// </summary>
    public static class ReviewComposer
    {
        /// <summary>The note of the card for a CPU whose price is 0 or below on the day of the review: "the price was not announced".</summary>
        public const string NoPriceNoteKey = "press.review.noPrice";

        /// <summary>
        /// Reviews the CPU of <paramref name="input"/> on <paramref name="today"/>. The outlets are those that work in that year and review the segment
        /// of the CPU (the stand-in when there are none, the stub outlet when the content has no stand-in either). The record carries the axes, the
        /// marks, the metascore, the verdict, the dates and the save ids of the reference CPUs; its effects (<c>reputationDelta</c>,
        /// <c>hypeAtPublish</c>) and mail (<c>mailId</c>) are left for the caller. <paramref name="adjusters"/> (may be null) are the adjusters of other
        /// features: they correct the draft before the outlets mark it (<see cref="AdjusterPipeline"/>), and their notes land in the record.
        /// </summary>
        public static ReviewRecord Compose(PendingReview pending, string companyName, ReviewInput input, DateTime today,
            IReadOnlyList<Outlet> outlets, ReviewConfig cfg, IRandomSource rng, AdjusterSet adjusters = null)
        {
            if (pending == null) throw new ArgumentNullException("pending");
            if (input == null) throw new ArgumentNullException("input");
            if (rng == null) throw new ArgumentNullException("rng");
            ReviewConfig config = cfg ?? ReviewConfig.CreateDefault();

            ReviewDraft draft = ReviewMath.Compute(input, config);
            // A CPU without a price has a value axis of 0 (edge case of the requirements): the card says why.
            if (!(input.Cpu.Price > 0f)) draft.Notes.Add(NoPriceNoteKey);
            // The factory never gives Any; the weights of the formulas already treat it as Mid Range, so the outlets and the record do too.
            Segment segment = input.Cpu.Segment == Segment.Any ? Segment.MidRange : input.Cpu.Segment;

            // The adjusters of other features see the draft before the outlets do (FR-11 item 3); one that throws changes nothing (item 5).
            if (adjusters != null) AdjusterPipeline.Apply(draft, adjusters, config.AdjusterLimits, config.WeightsFor(segment), config.Cap);

            IReadOnlyList<Outlet> active = OutletPanel.ActiveFor(today.Year, segment, outlets);
            if (active.Count == 0) active = new[] { PressContentValidator.StubOutlet() };

            var lines = new List<OutletLine>(active.Count);
            var scores = new List<OutletScore>(active.Count);
            for (int i = 0; i < active.Count; i++)
            {
                OutletScore score = OutletPanel.ScoreBy(active[i], draft, segment, config, rng);
                scores.Add(score);
                lines.Add(new OutletLine { outletId = score.OutletId, score = score.Score, quoteKey = score.QuoteKey });
            }

            int metascore = OutletPanel.Metascore(scores);
            return new ReviewRecord
            {
                cpuSaveId = input.Cpu.SaveId,
                cpuName = input.Cpu.Name ?? string.Empty,
                companyKey = input.Cpu.CompanyKey ?? pending.companyKey ?? string.Empty,
                companyName = companyName ?? string.Empty,
                releasedOn = pending.releasedOn ?? PressDates.ToText(today),
                publishedOn = PressDates.ToText(today),
                segment = (int)segment,
                axes = (float[])draft.AxisScores.Clone(),
                metascore = metascore,
                verdictKey = OutletPanel.VerdictKey(metascore, config),
                noAnalog = draft.NoAnalog,
                outlets = lines,
                peerSaveIds = ReferenceIds(input),
                notes = new List<string>(draft.Notes),
                reputationDelta = 0,
                hypeAtPublish = 1f,
                mailId = 0,
                compact = false,
            };
        }

        /// <summary>The save ids of the CPUs the review compared with: the reference CPUs, or the company's previous CPU when there were none.</summary>
        public static List<int> ReferenceIds(ReviewInput input)
        {
            var ids = new List<int>();
            if (input == null) return ids;

            if (input.Peers != null)
            {
                for (int i = 0; i < input.Peers.Count; i++) ids.Add(input.Peers[i].SaveId);
            }
            if (ids.Count == 0 && input.OwnPrevious.HasValue) ids.Add(input.OwnPrevious.Value.SaveId);
            return ids;
        }
    }
}
