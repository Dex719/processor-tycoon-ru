using System;
using System.Collections.Generic;

namespace PTMod.Features.PressReviews
{
    /// <summary>
    /// The release dates of the CPUs the feature knows (FR-2 item 3): from the archive of the published reviews and from the queue of the pending
    /// ones. A reference CPU's age comes from here; a CPU the feature never saw (it was released before the mod, or before the archive) counts
    /// as released on 1 July of its <c>Year</c> (<see cref="Fallback"/>).
    /// </summary>
    public sealed class ReleaseDateIndex
    {
        private readonly Dictionary<int, float> byId = new Dictionary<int, float>();

        /// <summary>How many CPUs have a known date.</summary>
        public int Count
        {
            get { return byId.Count; }
        }

        /// <summary>The index of the reviews and the queue of a state. A date that does not parse is skipped; the newest entry of a CPU wins.</summary>
        public static ReleaseDateIndex From(PressState state)
        {
            var index = new ReleaseDateIndex();
            if (state == null) return index;

            if (state.pending != null)
            {
                foreach (PendingReview item in state.pending) index.Add(item == null ? 0 : item.cpuSaveId, item == null ? null : item.releasedOn);
            }
            if (state.reviews != null)
            {
                foreach (ReviewRecord record in state.reviews) index.Add(record == null ? 0 : record.cpuSaveId, record == null ? null : record.releasedOn);
            }
            return index;
        }

        private void Add(int saveId, string releasedOn)
        {
            DateTime date;
            if (!PressDates.TryParse(releasedOn, out date)) return;
            byId[saveId] = PressDates.YearFraction(date);
        }

        public bool TryGet(int saveId, out float yearFraction)
        {
            return byId.TryGetValue(saveId, out yearFraction);
        }

        /// <summary>The release date of the CPU as a year with a fraction: the known date, otherwise <see cref="Fallback"/> of its year.</summary>
        public float Resolve(int saveId, int cpuYear)
        {
            float known;
            return byId.TryGetValue(saveId, out known) ? known : Fallback(cpuYear);
        }

        /// <summary>1 July of the year: <c>Year + 0.5</c>.</summary>
        public static float Fallback(int cpuYear)
        {
            return cpuYear + 0.5f;
        }
    }
}
