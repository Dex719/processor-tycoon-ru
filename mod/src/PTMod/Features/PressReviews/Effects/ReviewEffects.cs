using System;
using System.Globalization;
using PTMod.Core;
using Cpu = ProcessorTycoon.Hardware.Cpu;
using ICompany = ProcessorTycoon.CompanySystem.ICompany;

namespace PTMod.Features.PressReviews
{
    /// <summary>
    /// What a published review does to the game (FR-5, FR-6, FR-7, design.md Component 7): it moves the reputation of the company
    /// (<c>ModReputation.Add</c>, the reason <c>press.review:&lt;SaveID&gt;</c>) and starts the hype of the CPU, and the one provider of the standard
    /// channel <c>popularity.gain</c> (<see cref="ProvideWithReason"/>) returns the reputation's effect <c>R</c> times the hype <c>H</c> of the CPU.
    /// The patch <c>P5</c> of the core applies the product to the daily rise of a CPU's popularity and holds it, with the contributions of the other
    /// features, to the balance range of the channel; this class does not clip anything of its own beyond the hype's own bounds (D12).
    /// <list type="bullet">
    /// <item><b>Cache.</b> The hype of every CPU whose review is less than a year old (<see cref="HypeBook"/>) and the effect of every company's
    /// reputation (<see cref="PopularityModel"/>) are kept for the game day: <see cref="DailyRefresh"/> brings them up to date and drops what is
    /// over; a change of a company's reputation by any feature (<see cref="OnReputationChanged"/>) drops that company's value and the core's cached
    /// values of the channel for the company. The hype is made from the archive of reviews, so a load or a new game builds it again by itself.</item>
    /// <item><b>No monthly pull.</b> The reputation's slow return to 50 is the core's (<c>ModReputation.Drifted</c> only drops the cached effect).</item>
    /// <item><b>The parameter</b> "reputation affects the AI" makes the effect of an AI company's reputation 1; the player's always counts.</item>
    /// </list>
    /// The provider is registered once in <c>Register</c> and is not touched when the feature is switched; the core skips it while the feature is off.
    /// Main thread only.
    /// </summary>
    public sealed class ReviewEffects
    {
        private const string PopularityTarget = "Market.UpdatePopularities";

        private readonly Func<PressState> stateOf;
        private readonly HypeBook hype = new HypeBook();
        private readonly PopularityModel model;

        private PressState builtFor;
        private bool channelChecked;
        private DateTime providerFailureDay;

        /// <param name="stateOf">The live state of the feature; a new object means the save was loaded or a new game began.</param>
        public ReviewEffects(Func<PressState> stateOf)
        {
            if (stateOf == null) throw new ArgumentNullException("stateOf");
            this.stateOf = stateOf;
            model = new PopularityModel(() => PressContent.Current.Config, hype, ReputationOf, () => PressReviewsFeature.ReputationAffectsAI);
        }

        /// <summary>The hype of the reviewed CPUs and the effect of the reputations (for the architecture of the feature and the tests of the game build).</summary>
        internal PopularityModel Model
        {
            get { return model; }
        }

        // ---- after a review ----

        /// <summary>
        /// A review was published (called by the scheduler once, after the record joined the archive): the reputation of the company changes by
        /// <c>clamp(round((M − 65)/5), −6, 6)</c> (nothing is written for a change of 0), the CPU starts its hype, and the cached values of the company
        /// are dropped. The record gets <c>reputationDelta</c> and <c>hypeAtPublish</c>.
        /// </summary>
        public void OnReviewPublished(ReviewRecord record)
        {
            if (record == null) return;
            ReviewConfig cfg = PressContent.Current.Config;
            CheckChannel();
            EnsureFresh();

            DateTime published;
            if (!PressDates.TryParse(record.publishedOn, out published)) published = ModTicks.Today;
            DateTime today = ModTicks.Today;
            if (today == default(DateTime)) today = published;

            int delta = EffectMath.ReputationDelta(record.metascore, cfg.ReputationPerReview);
            record.reputationDelta = delta;
            record.hypeAtPublish = EffectMath.Hype(record.metascore, 0, cfg.Hype);
            hype.Add(record.cpuSaveId, record.metascore, published, today, cfg.Hype);

            if (delta != 0) ChangeReputation(record, delta);

            model.Invalidate(record.companyKey);
            ModModifiers.Invalidate(ModChannels.PopularityGain, record.companyKey);
            ModLog.Debug(PressReviewsFeature.FeatureId, "effects: '" + record.cpuName + "' (" + record.companyKey + ") M " + record.metascore
                + ", reputation " + (delta >= 0 ? "+" : string.Empty) + delta + ", hype x" + record.hypeAtPublish.ToString("0.###", CultureInfo.InvariantCulture));
        }

        private static void ChangeReputation(ReviewRecord record, int delta)
        {
            ICompany company = ModCompanies.Resolve(record.companyKey);
            if (company == null)
            {
                ModLog.Debug(PressReviewsFeature.FeatureId, "effects: the company " + record.companyKey + " is not in the game, its reputation is left alone");
                return;
            }
            ModReputation.Add(company, delta, EffectMath.ReviewReason(record.cpuSaveId));
        }

        // ---- the hype and the reputation as the channel asks for them ----

        /// <summary>
        /// The hype multiplier <c>H</c> of a CPU: that of its base design for a variant, 1 for a CPU nobody reviewed (or whose review is a year old).
        /// </summary>
        public float HypeMultiplier(Cpu cpu)
        {
            if (ReferenceEquals(cpu, null)) return 1f;
            EnsureFresh();

            float h;
            int metascore;
            return hype.TryGet(ReviewedSaveIdOf(cpu), out h, out metascore) ? h : 1f;
        }

        /// <summary>The effect <c>R</c> of the company's reputation, <c>1 + 0.3 × (rep − 50)/50</c>; 1 for an AI company when the parameter is off.</summary>
        public float ReputationMultiplier(ICompany company)
        {
            return model.ReputationMultiplier(ModCompanies.KeyOf(company));
        }

        /// <summary>
        /// The provider of <c>popularity.gain</c>: <c>R(company) × H(cpu)</c> when the question names a CPU, <c>R(company)</c> otherwise; the segment of
        /// the question does not matter. The reason is <c>press.review</c> with the metascore, or <c>press.review.brand</c> without a review.
        /// A failure is written to the log at most once a game day and passed on: the core counts it against the feature and uses ×1.
        /// </summary>
        public ProviderResult ProvideWithReason(ModifierContext ctx)
        {
            try
            {
                EnsureFresh();
                string key = ctx.CompanyKey ?? ModCompanies.KeyOf(ctx.Company);
                int reviewed = ReferenceEquals(ctx.Cpu, null) ? -1 : ReviewedSaveIdOf(ctx.Cpu);
                return model.Evaluate(key, reviewed);
            }
            catch (Exception ex)
            {
                LogProviderFailure(ex);
                throw;
            }
        }

        // ---- days, events ----

        /// <summary>
        /// A new game day (after the queue): the hype of every CPU is worked out for today, the reviews a year old are forgotten and the effects of the
        /// reputations are worked out again.
        /// </summary>
        public void DailyRefresh()
        {
            // The debug build times the daily work (PressTimings); otherwise Begin is a constant and End returns at once.
            long started = PressTimings.Begin();
            try
            {
                Refresh();
            }
            finally
            {
                PressTimings.End(started);
            }
        }

        private void Refresh()
        {
            DateTime today = ModTicks.Today;
            if (today == default(DateTime)) return;

            EnsureFresh();
            int forgotten = hype.Refresh(today, PressContent.Current.Config.Hype);
            model.InvalidateAll();
            if (forgotten > 0) ModLog.Debug(PressReviewsFeature.FeatureId, "effects: the hype of " + forgotten + " CPU(s) is over");
        }

        /// <summary><c>ModReputation.Changed</c>: a reputation changed (by this feature or another): its effect and the channel's cached values for the company are dropped.</summary>
        public void OnReputationChanged(string companyKey, float oldValue, float newValue, string reason)
        {
            ModHooks.Guard(PressReviewsFeature.FeatureId, "effects/reputationChanged", () =>
            {
                model.Invalidate(companyKey);
                ModModifiers.Invalidate(ModChannels.PopularityGain, companyKey);
            });
        }

        /// <summary><c>ModReputation.Drifted</c>: the core moved every reputation; the effects are worked out again (the core drops its own cache).</summary>
        public void OnReputationDrifted()
        {
            ModHooks.Guard(PressReviewsFeature.FeatureId, "effects/reputationDrifted", () => model.InvalidateAll());
        }

        /// <summary><c>ModSettings.Changed</c>: the parameter "reputation affects the AI" changed; every effect is worked out again.</summary>
        public void OnSettingChanged(string featureId, string key)
        {
            if (featureId != PressReviewsFeature.FeatureId || key != PressReviewsFeature.ReputationAffectsAiParam) return;

            ModHooks.Guard(PressReviewsFeature.FeatureId, "effects/settingChanged", () =>
            {
                model.InvalidateAll();
                ModModifiers.Invalidate(ModChannels.PopularityGain);
            });
        }

        /// <summary>The feature was switched on: whatever was cached while it was off is stale (reputations moved, days passed).</summary>
        public void Reset()
        {
            builtFor = null;
            model.InvalidateAll();
        }

        // ---- internals ----

        /// <summary>Builds the hype again from the archive when the state is not the one it was built for (a save was loaded, a new game began).</summary>
        private void EnsureFresh()
        {
            PressState state = stateOf();
            if (ReferenceEquals(state, builtFor)) return;

            DateTime today = ModTicks.Today;
            if (today == default(DateTime)) return;   // no game date (the menu): there is nothing to count the days from

            int kept = hype.Rebuild(state.reviews, today, PressContent.Current.Config.Hype);
            model.InvalidateAll();
            builtFor = state;
            if (kept > 0) ModLog.Debug(PressReviewsFeature.FeatureId, "effects: " + kept + " reviewed CPU(s) still have a hype");
        }

        private static float ReputationOf(string companyKey)
        {
            return ModReputation.Get(companyKey);
        }

        /// <summary>The CPU whose review counts for <paramref name="cpu"/>: itself, or the base design of a variant.</summary>
        internal static int ReviewedSaveIdOf(Cpu cpu)
        {
            Cpu current = cpu;
            for (int depth = 0; depth < 4; depth++)
            {
                Cpu parent = current.Base;
                if (parent == null || ReferenceEquals(parent, current)) break;
                current = parent;
            }
            return current.SaveID;
        }

        /// <summary>Says once per session if the core's patch on the daily popularity is not in the game: the reviews still work, their effect on sales does not.</summary>
        private void CheckChannel()
        {
            if (channelChecked) return;
            channelChecked = true;

            bool found = false;
            bool allApplied = true;
            foreach (PatchReport report in ModHooks.Report)
            {
                if (report.FeatureId != ModHooks.CoreId || report.Target != PopularityTarget) continue;
                found = true;
                if (report.Status != PatchStatus.Applied) allApplied = false;
            }
            if (!found || !allApplied)
            {
                ModLog.Warn(PressReviewsFeature.FeatureId, "effects: the patch of the core on " + PopularityTarget
                    + " is not applied, so the reputation and the hype do not change popularity; the reviews and the mail work as usual");
            }
        }

        private void LogProviderFailure(Exception ex)
        {
            DateTime today = ModTicks.Today;
            if (providerFailureDay != default(DateTime) && providerFailureDay == today) return;

            providerFailureDay = today;
            ModLog.Error(PressReviewsFeature.FeatureId, "effects: the provider of " + ModChannels.PopularityGain + " failed, the core uses x1", ex);
        }
    }
}
