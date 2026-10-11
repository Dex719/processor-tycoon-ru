using System.Collections.Generic;

namespace PTMod.Features.PressReviews
{
    /// <summary>
    /// A copy of a <see cref="ReviewRecord"/> that shares nothing with the original. The API of the feature (<c>ReviewPublished</c>, <c>GetReview</c>) hands
    /// out copies, so a feature that changes what it got cannot change the archive. Pure.
    /// </summary>
    public static class ReviewRecordCopy
    {
        /// <summary>The copy; null for null. A list or array that is null in the original stays null (a compact record has no marks and no reference CPUs).</summary>
        public static ReviewRecord Of(ReviewRecord source)
        {
            if (source == null) return null;

            var copy = new ReviewRecord
            {
                cpuSaveId = source.cpuSaveId,
                cpuName = source.cpuName,
                companyKey = source.companyKey,
                companyName = source.companyName,
                releasedOn = source.releasedOn,
                publishedOn = source.publishedOn,
                segment = source.segment,
                axes = source.axes == null ? null : (float[])source.axes.Clone(),
                metascore = source.metascore,
                verdictKey = source.verdictKey,
                noAnalog = source.noAnalog,
                outlets = null,
                peerSaveIds = source.peerSaveIds == null ? null : new List<int>(source.peerSaveIds),
                notes = source.notes == null ? null : new List<string>(source.notes),
                reputationDelta = source.reputationDelta,
                hypeAtPublish = source.hypeAtPublish,
                mailId = source.mailId,
                compact = source.compact,
            };

            if (source.outlets != null)
            {
                copy.outlets = new List<OutletLine>(source.outlets.Count);
                for (int i = 0; i < source.outlets.Count; i++)
                {
                    OutletLine line = source.outlets[i];
                    copy.outlets.Add(line == null ? null : new OutletLine { outletId = line.outletId, score = line.score, quoteKey = line.quoteKey });
                }
            }
            return copy;
        }
    }
}
