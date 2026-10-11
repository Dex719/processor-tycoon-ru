using System;

namespace PTMod.Features.PressReviews
{
    /// <summary>
    /// The archive of published reviews (FR-9 item 4, design.md "Записи обзоров и состояние"): the newest record is last, the oldest ones are stored in a
    /// short form (no axes, no marks of the outlets, no reference CPUs) once the archive holds more than <see cref="ArchiveConfig.MaxFull"/> records, and
    /// a record can be found by the CPU it is about. Pure; <see cref="ReviewQueue.Publish"/> calls <see cref="Compact"/> after each new review.
    /// </summary>
    public static class ReviewArchive
    {
        /// <summary>
        /// Brings the archive to its form: every record except the newest <see cref="ArchiveConfig.MaxFull"/> ones becomes compact. A record that is
        /// compact already is left alone, so the call is cheap to repeat. The numbers a compact record keeps (the metascore, the verdict, the dates, the
        /// segment, the company and the CPU) are enough for the list of reviews, the yearly awards and the hype. Returns how many records it compacted.
        /// </summary>
        public static int Compact(PressState state, ArchiveConfig archive)
        {
            if (state == null || state.reviews == null) return 0;

            int keepFull = Math.Max(1, (archive ?? new ArchiveConfig()).MaxFull);
            int count = state.reviews.Count;
            if (count <= keepFull) return 0;

            int compacted = 0;
            int oldEnd = count - keepFull;
            for (int i = 0; i < oldEnd; i++)
            {
                if (CompactRecord(state.reviews[i])) compacted++;
            }
            return compacted;
        }

        /// <summary>Makes one record compact (false when it is null or compact already).</summary>
        public static bool CompactRecord(ReviewRecord record)
        {
            if (record == null || record.compact) return false;

            record.compact = true;
            record.axes = new float[0];
            record.outlets = null;
            record.peerSaveIds = null;
            return true;
        }

        /// <summary>The newest record about the CPU, or null (a CPU gets one review; a damaged save might hold two).</summary>
        public static ReviewRecord Find(PressState state, int cpuSaveId)
        {
            if (state == null || state.reviews == null) return null;

            for (int i = state.reviews.Count - 1; i >= 0; i--)
            {
                ReviewRecord record = state.reviews[i];
                if (record != null && record.cpuSaveId == cpuSaveId) return record;
            }
            return null;
        }
    }
}
