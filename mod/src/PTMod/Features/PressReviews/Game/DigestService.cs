using System;
using System.Collections.Generic;
using PTMod.Core;
using Cpu = ProcessorTycoon.Hardware.Cpu;
using CpuDataProvider = ProcessorTycoon.Hardware.CpuDataProvider;

namespace PTMod.Features.PressReviews
{
    /// <summary>
    /// The monthly digest of the rivals' reviews on the game's side (FR-4 items 3-5, design.md Component 8a): on the first of a month
    /// (<c>TickPhase.MonthPassed</c>, subscribed with <c>ctx.OnTick</c>) and when the parameter "rival digest" (<c>rivalDigest</c>) is on, the reviews of the AI
    /// companies' CPUs published last month are sorted out by the pure <see cref="DigestBuilder"/> and, when any is worth a line, sent as one letter
    /// "Press: rival reviews" (<c>Low</c>, tag <c>press:digest:&lt;yyyy-MM&gt;</c>, <see cref="PressMail.SendDigest"/>). A month without a worthy review sends
    /// nothing; the archive fills in whatever the parameter says (the digest only reads it). The reviews of the rivals send no letter and no notification of
    /// their own.
    /// <para>
    /// <b>Once a month.</b> <c>PressState.lastDigest</c> holds the month of the last digest, so a save loaded on the first of the month, or a batch run of a
    /// month that plays the first twice, does not send the letter twice (a letter with the tag is looked for as well). During a batch run
    /// (<c>ModTicks.IsBatchRun</c>) the core holds the letter back until the run ends, so nothing special is done here.
    /// </para>
    /// <para>
    /// <b>The player's segments</b> are those where a CPU of the player that is released and not retired sold something over the last three full months (a
    /// CPU without any sales yet counts in the segment its price puts it in). The ticks of a feature that is switched off do not run, so a month passed in
    /// that time has no digest.
    /// </para>
    /// </summary>
    public sealed class DigestService
    {
        private const string Feature = PressReviewsFeature.FeatureId;
        private const int SalesMonths = 3;

        private readonly Func<PressState> stateOf;

        public DigestService(Func<PressState> stateOf)
        {
            if (stateOf == null) throw new ArgumentNullException("stateOf");
            this.stateOf = stateOf;
        }

        /// <summary><c>TickPhase.MonthPassed</c>: the digest of the month that has just ended, if there is one to send.</summary>
        public void OnMonth()
        {
            if (!PressReviewsFeature.RivalDigest) return;

            DateTime today = ModTicks.Today;
            if (today == default(DateTime)) return;

            PressState state = stateOf();
            string month = PressDates.ToMonthText(today);
            if (!DigestBuilder.IsDue(state.lastDigest, today))
            {
                ModLog.Debug(Feature, "digest: the digest of " + month + " is done already");
                return;
            }
            if (ModMail.Find(Feature, DigestMailText.TagOf(today)) != null)
            {
                state.lastDigest = month;
                ModLog.Debug(Feature, "digest: the letter of " + month + " is in the mail already");
                return;
            }

            DigestConfig cfg = PressContent.Current.Config.Digest;
            List<ReviewRecord> reviews = DigestBuilder.ReviewsOfMonth(state.reviews, DigestBuilder.PreviousMonth(today));
            DigestContent content = DigestBuilder.Build(reviews, reviews.Count == 0 ? null : PlayerSegments(), cfg);

            // From here the month is settled, whatever the mail service says: a letter that was not taken is not tried again every time the first is ticked.
            state.lastDigest = month;
            if (content.IsEmpty)
            {
                ModLog.Debug(Feature, "digest: " + reviews.Count + " review(s) of the rivals in " + PressDates.ToMonthText(DigestBuilder.PreviousMonth(today)) + ", none worth a line");
                return;
            }

            PressMail.SendDigest(today, content);
            ModLog.Info(Feature, "digest: " + month + ": " + content.Lines.Count + " line(s), " + content.More + " more in the archive");
        }

        // ---- the player's segments ----

        /// <summary>The segments (0…3) where the player has a CPU on sale. Never null; empty when the game cannot say.</summary>
        internal static HashSet<int> PlayerSegments()
        {
            var segments = new HashSet<int>();
            CpuDataProvider provider = CpuDataProvider.Instance;
            if (provider == null) return segments;

            List<Cpu> mine = provider.CpusOrderedByTotalSales(true, false);   // the player's CPUs that are not retired
            if (mine == null) return segments;

            for (int i = 0; i < mine.Count; i++)
            {
                Cpu cpu = mine[i];
                if (cpu == null || !cpu.IsReleased || cpu.IsRetired) continue;

                bool sold = false;
                if (cpu.Market != null)
                {
                    sold |= Mark(segments, 0, cpu.Market.SoldByHighEnd);
                    sold |= Mark(segments, 1, cpu.Market.SoldByMidRange);
                    sold |= Mark(segments, 2, cpu.Market.SoldByLowEnd);
                    sold |= Mark(segments, 3, cpu.Market.SoldByIndustries);
                }
                if (sold) continue;

                // No sales to go by (a CPU that has just come out): the segment its price and its maker's strategy put it in.
                Segment fallback = CpuSnapshotFactory.SegmentOfExisting(cpu);
                if (fallback != Segment.Any) segments.Add((int)fallback);
            }
            return segments;
        }

        private static bool Mark(HashSet<int> segments, int segment, List<int> monthly)
        {
            if (PeerFilter.SoldOverFullMonths(monthly, SalesMonths) <= 0) return false;
            segments.Add(segment);
            return true;
        }
    }
}
