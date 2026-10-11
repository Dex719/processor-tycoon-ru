using System;
using System.Collections.Generic;

namespace PTMod.Features.PressReviews
{
    /// <summary>What <see cref="ReviewQueue.Admit"/> decided about one released CPU.</summary>
    public enum QueueVerdict
    {
        /// <summary>The CPU gets a review: put it in the queue.</summary>
        Queue = 0,

        /// <summary>Released in the fast-forward of a new game: no reviews then (owner's policy, D10).</summary>
        FastForward = 1,

        /// <summary>A variant of an earlier design: it takes the hype of its base and is not reviewed on its own (D6).</summary>
        Variant = 2,

        /// <summary>The CPU is not released.</summary>
        NotReleased = 3,

        /// <summary>The owner has no key (not a company of the game).</summary>
        NoCompany = 4,

        /// <summary>The CPU is in the queue or in the archive already (one review per CPU).</summary>
        AlreadyKnown = 5,
    }

    /// <summary>
    /// The queue of reviews (FR-1, design.md Component 3) without the game: which released CPUs are queued, when their review is due, the sales
    /// a player's CPU makes while it waits, the failed attempts, and the move of a finished review from the queue to the archive. All of it works on
    /// the <see cref="PressState"/> that is saved with the game, so a load puts the queue back as it was. The game's side (the event of the core,
    /// the CPUs, the market) is <c>ReviewScheduler</c>.
    /// </summary>
    public static class ReviewQueue
    {
        /// <summary>The calculation of a review may fail this many times (one try a day); then the review is cancelled.</summary>
        public const int MaxAttempts = 3;

        /// <summary>
        /// Decides what a released CPU does to the queue. The checks come in the order of the design: the fast-forward, a variant, a CPU that is not
        /// released, a company without a key, a CPU the feature has seen. The load of a save and a second call for the same CPU object never get here:
        /// the core does not raise the event for them.
        /// </summary>
        public static QueueVerdict Admit(PressState state, int cpuSaveId, string companyKey, bool isVariant, bool duringFastForward, bool isReleased)
        {
            if (duringFastForward) return QueueVerdict.FastForward;
            if (isVariant) return QueueVerdict.Variant;
            if (!isReleased) return QueueVerdict.NotReleased;
            if (string.IsNullOrEmpty(companyKey)) return QueueVerdict.NoCompany;
            if (IsKnown(state, cpuSaveId)) return QueueVerdict.AlreadyKnown;
            return QueueVerdict.Queue;
        }

        /// <summary>The CPU waits for its review or has been reviewed.</summary>
        public static bool IsKnown(PressState state, int cpuSaveId)
        {
            if (state == null) return false;

            List<PendingReview> pending = state.pending;
            if (pending != null)
            {
                for (int i = 0; i < pending.Count; i++)
                {
                    if (pending[i] != null && pending[i].cpuSaveId == cpuSaveId) return true;
                }
            }

            List<ReviewRecord> reviews = state.reviews;
            if (reviews != null)
            {
                // The newest reviews are the likeliest match, and the archive can hold thousands.
                for (int i = reviews.Count - 1; i >= 0; i--)
                {
                    if (reviews[i] != null && reviews[i].cpuSaveId == cpuSaveId) return true;
                }
            }
            return false;
        }

        /// <summary>
        /// The wait for a review in days: 0 in the instant mode, otherwise a whole number from <c>delayDays</c> of the configuration (7…21), both ends
        /// included, drawn from the feature's stream.
        /// </summary>
        public static int PickDelay(ReviewConfig cfg, bool instant, IRandomSource rng)
        {
            if (instant) return 0;

            ReviewConfig config = cfg ?? ReviewConfig.CreateDefault();
            int min = Math.Max(0, config.DelayMinDays);
            int max = Math.Max(min, config.DelayMaxDays);
            if (min == max || rng == null) return min;

            int days = rng.Range(min, max + 1);
            return days < min ? min : days > max ? max : days;
        }

        /// <summary>
        /// Puts a CPU in the queue: released <paramref name="today"/>, due <paramref name="delayDays"/> days later, no sales yet. A queue at its limit
        /// (<see cref="PressStateCodec.MaxPending"/>) loses its oldest entry first. Returns the entry.
        /// </summary>
        public static PendingReview Enqueue(PressState state, int cpuSaveId, string companyKey, DateTime today, int delayDays)
        {
            if (state == null) throw new ArgumentNullException("state");
            if (state.pending == null) state.pending = new List<PendingReview>();

            if (state.pending.Count >= PressStateCodec.MaxPending) state.pending.RemoveAt(0);

            var entry = new PendingReview
            {
                cpuSaveId = cpuSaveId,
                companyKey = companyKey ?? string.Empty,
                releasedOn = PressDates.ToText(today),
                publishOn = PressDates.ToText(today.AddDays(delayDays < 0 ? 0 : delayDays)),
                soldBySegment = new int[4],
                attempts = 0,
            };
            state.pending.Add(entry);
            return entry;
        }

        /// <summary>
        /// The reviews due on <paramref name="today"/> or earlier, in the order they were queued. An entry whose date does not parse counts as due
        /// (it would never be published otherwise; the codec drops such entries on load).
        /// </summary>
        public static List<PendingReview> DueOn(PressState state, DateTime today)
        {
            var due = new List<PendingReview>();
            if (state == null || state.pending == null) return due;

            for (int i = 0; i < state.pending.Count; i++)
            {
                PendingReview item = state.pending[i];
                if (item == null) continue;

                DateTime publishOn;
                if (!PressDates.TryParse(item.publishOn, out publishOn) || publishOn.Date <= today.Date) due.Add(item);
            }
            return due;
        }

        /// <summary>The CPU of the player sold these units in the four segments today: they are added to what it sold while it waited.</summary>
        public static void AddSales(PendingReview entry, int highEnd, int midRange, int lowEnd, int industries)
        {
            if (entry == null) return;
            if (entry.soldBySegment == null || entry.soldBySegment.Length != 4) entry.soldBySegment = new int[4];

            entry.soldBySegment[0] = Add(entry.soldBySegment[0], highEnd);
            entry.soldBySegment[1] = Add(entry.soldBySegment[1], midRange);
            entry.soldBySegment[2] = Add(entry.soldBySegment[2], lowEnd);
            entry.soldBySegment[3] = Add(entry.soldBySegment[3], industries);
        }

        /// <summary>
        /// The calculation of an entry failed: one more attempt is counted. After <see cref="MaxAttempts"/> the entry is removed from the queue.
        /// True when it was cancelled.
        /// </summary>
        public static bool Fail(PressState state, PendingReview entry)
        {
            if (entry == null) return false;

            entry.attempts++;
            if (entry.attempts < MaxAttempts) return false;

            Cancel(state, entry);
            return true;
        }

        /// <summary>Takes the entry out of the queue (the CPU is retired, the company is gone, the CPU cannot be found). True when it was in it.</summary>
        public static bool Cancel(PressState state, PendingReview entry)
        {
            if (state == null || state.pending == null || entry == null) return false;
            return state.pending.Remove(entry);
        }

        /// <summary>
        /// The review is published: the entry leaves the queue and the record goes to the end of the archive. The archive keeps at most
        /// <see cref="ArchiveConfig.MaxTotal"/> records, the oldest go first, and all but the newest <see cref="ArchiveConfig.MaxFull"/> are
        /// stored in the short form (<see cref="ReviewArchive.Compact"/>).
        /// </summary>
        public static void Publish(PressState state, PendingReview entry, ReviewRecord record, ArchiveConfig archive)
        {
            if (state == null) throw new ArgumentNullException("state");
            if (record == null) throw new ArgumentNullException("record");

            Cancel(state, entry);
            if (state.reviews == null) state.reviews = new List<ReviewRecord>();
            state.reviews.Add(record);

            int limit = Math.Max(1, (archive ?? new ArchiveConfig()).MaxTotal);
            if (state.reviews.Count > limit) state.reviews.RemoveRange(0, state.reviews.Count - limit);

            ReviewArchive.Compact(state, archive);
        }

        private static int Add(int total, int units)
        {
            if (units <= 0) return total;
            long sum = (long)total + units;
            return sum > int.MaxValue ? int.MaxValue : (int)sum;
        }
    }
}
