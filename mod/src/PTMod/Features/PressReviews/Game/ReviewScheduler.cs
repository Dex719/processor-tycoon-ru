using System;
using System.Collections.Generic;
using System.Globalization;
using PTMod.Core;
using Cpu = ProcessorTycoon.Hardware.Cpu;
using CpuDataProvider = ProcessorTycoon.Hardware.CpuDataProvider;
using GamePlayer = ProcessorTycoon.Player;
using ICompany = ProcessorTycoon.CompanySystem.ICompany;

namespace PTMod.Features.PressReviews
{
    /// <summary>
    /// The queue of reviews on the game's side (FR-1, design.md Component 3): a CPU that the core announces as released (<c>ModEvents.CpuReleased</c>,
    /// the postfix P20 of the core; the feature patches no <c>AddCpu</c>) is queued with a wait of 7–21 days, the CPUs of the player add up what they
    /// sell while they wait, and on the day a review is due the scheduler reviews the CPU (<see cref="ReviewComposer"/>), files the review in the
    /// archive and hands it to <see cref="ReviewEffects"/> and to the subscribers of <see cref="Published"/> (the mail, the API of the feature).
    /// The rules that need no game are <see cref="ReviewQueue"/>.
    /// <list type="bullet">
    /// <item><b>Which releases count.</b> Not those of the new game's fast-forward, not variants (D6), not CPUs that are not released, not CPUs the
    /// feature has seen. A save being loaded and a second call for the same CPU object never reach the handler (the core does not raise them), a
    /// transfer of a CPU (<c>ModEvents.CpuTransferred</c>) is not a release and is not listened to.</item>
    /// <item><b>When.</b> The wait is drawn from the feature's stream of <c>ModRandom</c>; the instant mode (parameter <c>instantReviews</c>) makes it 0,
    /// and the review is written at the next tick of the day. The ticks are <c>TickPhase.DayPassed</c>: the due date is compared with <c>ModTicks.Today</c>.</item>
    /// <item><b>Failure.</b> A review that cannot be calculated (the market has no list of CPUs yet, a number is wrong) is tried again the next day, three
    /// times; the third failure cancels it and is counted against the feature. A CPU that is retired or gone, and a company that is bankrupt, cancel the
    /// review without a count.</item>
    /// </list>
    /// Everything runs on the main thread inside <c>ModHooks.Guard</c> or the guarded tick of the core.
    /// </summary>
    public sealed class ReviewScheduler
    {
        private readonly Func<PressState> stateOf;
        private readonly ReviewEffects effects;

        public ReviewScheduler(Func<PressState> stateOf, ReviewEffects effects)
        {
            if (stateOf == null) throw new ArgumentNullException("stateOf");
            if (effects == null) throw new ArgumentNullException("effects");
            this.stateOf = stateOf;
            this.effects = effects;
        }

        /// <summary>
        /// A review was published and is in the archive (and its effects are in): the mail and the API of the feature listen. The handlers run through
        /// <c>ModHooks.Raise</c>, so one that fails does not stop the others and cannot send the review back to the queue.
        /// </summary>
        public event Action<ReviewRecord> Published;

        /// <summary>
        /// The adjusters of other features for the review of a CPU (the API of the feature, FR-11 item 3); null for none. Set by the feature when it starts.
        /// Asked inside a guard: a failure here means no adjusters for that review, not a failed review.
        /// </summary>
        internal Func<Cpu, AdjusterSet> AdjusterSource { get; set; }

        /// <summary>The reviews that wait for their date.</summary>
        public IReadOnlyList<PendingReview> Pending
        {
            get { return stateOf().pending; }
        }

        // ---- the release ----

        /// <summary>The handler of <c>ModEvents.CpuReleased</c>, subscribed once in <c>Register</c>. Never throws.</summary>
        public void OnCpuReleased(ICompany owner, Cpu cpu, bool isVariant, bool duringFastForward)
        {
            ModHooks.Guard(PressReviewsFeature.FeatureId, "scheduler/release", () => Enqueue(owner, cpu, isVariant, duringFastForward));
        }

        /// <summary>Queues a released CPU unless the rules of <see cref="ReviewQueue.Admit"/> turn it away. Returns what was decided.</summary>
        public QueueVerdict Enqueue(ICompany owner, Cpu cpu, bool isVariant, bool duringFastForward)
        {
            if (cpu == null) return QueueVerdict.NotReleased;

            PressState state = stateOf();
            string companyKey = ModCompanies.KeyOf(owner ?? cpu.Company);
            QueueVerdict verdict = ReviewQueue.Admit(state, cpu.SaveID, companyKey, isVariant, duringFastForward, cpu.IsReleased);
            if (verdict != QueueVerdict.Queue)
            {
                ModLog.Debug(PressReviewsFeature.FeatureId, "scheduler: '" + cpu.Name + "' (id " + cpu.SaveID + ") gets no review: " + verdict);
                return verdict;
            }

            DateTime today = ModTicks.Today;
            if (today == default(DateTime))
            {
                ModLog.Warn(PressReviewsFeature.FeatureId, "scheduler: '" + cpu.Name + "' was released but the game has no date; no review is queued");
                return QueueVerdict.NotReleased;
            }

            int delay = ReviewQueue.PickDelay(PressContent.Current.Config, PressReviewsFeature.InstantReviews, NewRandom());
            PendingReview entry = ReviewQueue.Enqueue(state, cpu.SaveID, companyKey, today, delay);
            ModLog.Debug(PressReviewsFeature.FeatureId, "scheduler: '" + cpu.Name + "' (id " + cpu.SaveID + ", " + companyKey + ") is queued, review on " + entry.publishOn);
            return QueueVerdict.Queue;
        }

        // ---- the day ----

        /// <summary>
        /// The tick of a game day: the player's CPUs that wait add what they sold today, then every review that is due is written. Registered with
        /// <c>ctx.OnTick(TickPhase.DayPassed, …)</c>, so it does not run in the fast-forward or while a save loads.
        /// </summary>
        public void DailyTick()
        {
            // The debug build times the daily work (PressTimings); otherwise Begin is a constant and End returns at once.
            long started = PressTimings.Begin();
            try
            {
                Tick();
            }
            finally
            {
                PressTimings.End(started);
            }
        }

        private void Tick()
        {
            PressState state = stateOf();
            if (state.pending == null || state.pending.Count == 0) return;

            DateTime today = ModTicks.Today;
            if (today == default(DateTime)) return;

            AccumulateSales(state);
            PublishDue(state, today);
        }

        /// <summary>The sales of the day by segment (<c>SoldLastDayBy*</c>) of every CPU of the player that waits; they decide the segment of its review.</summary>
        private static void AccumulateSales(PressState state)
        {
            for (int i = 0; i < state.pending.Count; i++)
            {
                PendingReview entry = state.pending[i];
                if (entry == null || entry.companyKey != ModCompanies.PlayerKey) continue;

                Cpu cpu = FindCpu(entry.cpuSaveId);
                if (cpu == null) continue;

                ReviewQueue.AddSales(entry, cpu.SoldLastDayByHighEnd, cpu.SoldLastDayByMidRange, cpu.SoldLastDayByLowEnd, cpu.SoldLastDayByIndustries);
            }
        }

        private void PublishDue(PressState state, DateTime today)
        {
            List<PendingReview> due = ReviewQueue.DueOn(state, today);
            if (due.Count == 0) return;

            PressContent content = PressContent.Current;
            ReleaseDateIndex releases = ReleaseDateIndex.From(state);
            int difficulty = DifficultyLevel();
            var selector = new PeerSelector(content.Config.Peers, releases);

            for (int i = 0; i < due.Count; i++)
            {
                PendingReview entry = due[i];
                ReviewRecord record = null;
                try
                {
                    record = Review(state, entry, today, content, releases, selector, difficulty);
                }
                catch (Exception ex)
                {
                    OnFailure(state, entry, ex);
                }

                // From here on the review exists in the archive: nothing below may put it back in the queue.
                if (record != null) AfterPublished(record);
            }
        }

        /// <summary>
        /// Reviews one CPU: the segment, the reference CPUs, the marks; the review goes to the archive and the entry leaves the queue. Null when the
        /// review was cancelled. Throws when the calculation fails (the caller counts the attempt).
        /// </summary>
        private ReviewRecord Review(PressState state, PendingReview entry, DateTime today, PressContent content, ReleaseDateIndex releases,
            PeerSelector selector, int difficulty)
        {
            Cpu cpu = FindCpu(entry.cpuSaveId);
            if (cpu == null) return Cancel(state, entry, "the CPU is not in the game any more");
            if (cpu.IsRetired) return Cancel(state, entry, "the CPU is retired");

            ICompany company = cpu.Company;
            if (company == null || company.IsBankrupt) return Cancel(state, entry, "the company is bankrupt or gone");

            bool instant = entry.publishOn == entry.releasedOn;
            Segment segment = CpuSnapshotFactory.ResolveSegment(cpu, entry.soldBySegment, instant);

            var input = new ReviewInput
            {
                Cpu = CpuSnapshotFactory.Create(cpu, segment, releases),
                Peers = selector.SelectPeers(cpu, segment),
                NowYearFraction = PressDates.YearFraction(today),
                Difficulty = difficulty,
            };
            if (input.Peers.Count == 0) input.OwnPrevious = selector.PreviousOwnCpu(cpu, segment);

            AdjusterSet adjusters = AdjustersOf(cpu);
            ReviewRecord record = ReviewComposer.Compose(entry, company.Name, input, today, content.Outlets, content.Config, NewRandom(), adjusters);
            ReviewQueue.Publish(state, entry, record, content.Config.Archive);
            ModLog.Debug(PressReviewsFeature.FeatureId, "scheduler: '" + record.cpuName + "' (id " + record.cpuSaveId + ") reviewed, " + segment + ", M " + record.metascore
                + " (" + record.verdictKey + "), " + record.peerSaveIds.Count + " reference CPU(s)" + (record.noAnalog ? ", no analogue" : string.Empty));
            return record;
        }

        private AdjusterSet AdjustersOf(Cpu cpu)
        {
            Func<Cpu, AdjusterSet> source = AdjusterSource;
            if (source == null) return null;
            return ModHooks.Guard(PressReviewsFeature.FeatureId, "scheduler/adjusters", () => source(cpu), (AdjusterSet)null);
        }

        private ReviewRecord Cancel(PressState state, PendingReview entry, string why)
        {
            ReviewQueue.Cancel(state, entry);
            ModLog.Info(PressReviewsFeature.FeatureId, "scheduler: the review of CPU " + entry.cpuSaveId + " is cancelled: " + why);
            return null;
        }

        private void OnFailure(PressState state, PendingReview entry, Exception ex)
        {
            bool cancelled = ReviewQueue.Fail(state, entry);
            if (!cancelled)
            {
                ModLog.Warn(PressReviewsFeature.FeatureId, "scheduler: the review of CPU " + entry.cpuSaveId + " failed (attempt " + entry.attempts + " of "
                    + ReviewQueue.MaxAttempts + "), it will be tried again tomorrow: " + ex.GetType().Name + ": " + ex.Message);
                return;
            }

            ModLog.Warn(PressReviewsFeature.FeatureId, "scheduler: the review of CPU " + entry.cpuSaveId + " is cancelled after " + ReviewQueue.MaxAttempts + " failed attempts");
            ModHooks.ReportFault(PressReviewsFeature.FeatureId, "scheduler/review", ex);
        }

        /// <summary>The effects (reputation, hype), then the subscribers (mail, API). Each in its own guard.</summary>
        private void AfterPublished(ReviewRecord record)
        {
            ModHooks.Guard(PressReviewsFeature.FeatureId, "scheduler/effects", () => effects.OnReviewPublished(record));
            if (PressReviewsFeature.DebugVerbose && record.companyKey == ModCompanies.PlayerKey) LogVerbose(record);
            ModHooks.Raise("ReviewScheduler.Published", Published, record);
        }

        /// <summary>The debug build's journal for the player's CPUs: the axes, the reference CPUs and the multipliers.</summary>
        private void LogVerbose(ReviewRecord record)
        {
            string axes = record.axes == null ? string.Empty : string.Join(" / ", Array.ConvertAll(record.axes, a => a.ToString("0.0", CultureInfo.InvariantCulture)));
            string peers = record.peerSaveIds == null ? string.Empty : string.Join(", ", record.peerSaveIds.ConvertAll(id => id.ToString(CultureInfo.InvariantCulture)).ToArray());
            ModLog.Debug(PressReviewsFeature.FeatureId, "review of '" + record.cpuName + "': axes " + axes + "; reference CPUs [" + peers + "]; reputation "
                + (record.reputationDelta >= 0 ? "+" : string.Empty) + record.reputationDelta + ", hype x" + record.hypeAtPublish.ToString("0.###", CultureInfo.InvariantCulture));
        }

        // ---- the game ----

        private static IRandomSource NewRandom()
        {
            return new ModRngSource(ModRandom.Stream(PressReviewsFeature.FeatureId));
        }

        private static Cpu FindCpu(int saveId)
        {
            CpuDataProvider provider = CpuDataProvider.Instance;
            return provider == null ? null : provider.GetCpu(saveId);
        }

        private static int DifficultyLevel()
        {
            GamePlayer player = GamePlayer.Instance;
            return player == null ? 2 : player.DifficultyLevel;
        }
    }
}
