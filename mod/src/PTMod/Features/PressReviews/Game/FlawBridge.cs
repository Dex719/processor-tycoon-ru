using System;
using System.Collections.Generic;
using System.Globalization;
using PTMod.Core;
using PTMod.Features.DesignFlawRecalls;

namespace PTMod.Features.PressReviews
{
    /// <summary>
    /// The soft link between the press and <c>design-flaw-recalls</c> (FR-11 items 6 and 7, design.md Component 12). The press only talks; whether a CPU has
    /// a hidden flaw, and what comes of it, is decided by <c>design-flaw-recalls</c>:
    /// <list type="bullet">
    /// <item><b>A review reveals.</b> When a review is published, with the chance <c>flawReveal.chance</c> of the content (10 % by default) the press asks
    /// <c>FlawRegistry.TryReveal(cpuSaveId, DiscoverySource.External)</c>. The answer decides nothing here: a CPU without a hidden flaw gives <c>false</c> and
    /// nothing happens, the player is never told that a flaw exists. The dice are a stream of their own (<c>press-reviews/flaw</c>), so whether the other
    /// feature is on does not change the marks of the outlets.</item>
    /// <item><b>A public flaw is written about.</b> When <c>FlawRegistry.OnFlawStateChanged</c> reports a flaw that is public, the press publishes an
    /// article "Press: a flaw in X" (letter and line in the tab "Articles", once per flaw) and adds a note to the review of that CPU, if it has one.</item>
    /// </list>
    /// The bridge is not made in <c>Register</c>: it attaches on <c>ModTicks.GameReady</c> and on <c>ModFeatures.StateChanged</c> of either feature, when
    /// both are enabled, and detaches when one is not. Without <c>design-flaw-recalls</c> it is never attached and the press does nothing about flaws.
    /// </summary>
    internal sealed class FlawBridge
    {
        /// <summary>The id of the other feature.</summary>
        internal const string FlawFeatureId = DesignFlawRecallsApi.FeatureId;

        /// <summary>The stream of the dice of the reveal.</summary>
        internal const string StreamId = PressReviewsFeature.FeatureId + "/flaw";

        /// <summary>The keys of the article about a public flaw and of the note in the review of that CPU.</summary>
        internal const string ArticleTitleKey = "press.article.flaw.title";
        internal const string ArticleBodyKey = "press.article.flaw.body";
        internal const string ArticleUnknownCpuKey = "press.article.flaw.unknownCpu";
        internal const string FlawPublicNoteKey = "press.note.flawPublic";

        private const string Feature = PressReviewsFeature.FeatureId;

        private readonly Func<PressState> stateOf;
        private readonly PressApiBackend publisher;
        private bool attached;

        internal FlawBridge(Func<PressState> stateOf, PressApiBackend publisher)
        {
            if (stateOf == null) throw new ArgumentNullException("stateOf");
            if (publisher == null) throw new ArgumentNullException("publisher");
            this.stateOf = stateOf;
            this.publisher = publisher;
        }

        /// <summary>The bridge listens to <c>FlawRegistry.OnFlawStateChanged</c>.</summary>
        internal bool IsAttached
        {
            get { return attached; }
        }

        // ---- attaching ----

        /// <summary><c>ModTicks.GameReady</c>: the game is ready, both features have their state.</summary>
        internal void OnGameReady(GameReadyReason reason)
        {
            ModHooks.Guard(Feature, "flaw/gameReady", Refresh);
        }

        /// <summary><c>ModFeatures.StateChanged</c>: the bridge follows the two features it joins.</summary>
        internal void OnFeatureStateChanged(string featureId, FeatureState state)
        {
            if (featureId != FlawFeatureId && featureId != Feature) return;
            ModHooks.Guard(Feature, "flaw/stateChanged", Refresh);
        }

        /// <summary>Attaches when both features are enabled and the game is ready, detaches otherwise. Safe to call as often as needed.</summary>
        internal void Refresh()
        {
            bool wanted = ModTicks.IsGameReady && ModFeatures.IsActive(Feature) && ModFeatures.IsActive(FlawFeatureId);
            if (wanted) Attach();
            else Detach();
        }

        /// <summary>Starts listening (once: a second call changes nothing).</summary>
        internal void Attach()
        {
            if (attached) return;

            DesignFlawRecallsApi.Registry.OnFlawStateChanged -= OnFlawStateChanged;
            DesignFlawRecallsApi.Registry.OnFlawStateChanged += OnFlawStateChanged;
            attached = true;
            ModLog.Debug(Feature, "flaws: the bridge to " + FlawFeatureId + " is attached");
        }

        /// <summary>Stops listening.</summary>
        internal void Detach()
        {
            if (!attached) return;

            DesignFlawRecallsApi.Registry.OnFlawStateChanged -= OnFlawStateChanged;
            attached = false;
            ModLog.Debug(Feature, "flaws: the bridge to " + FlawFeatureId + " is detached");
        }

        // ---- a review reveals ----

        /// <summary>
        /// <c>ReviewScheduler.Published</c>: with the chance of the content the press asks <c>design-flaw-recalls</c> to reveal the hidden flaw of the CPU, if
        /// it has one. Nothing is drawn while the bridge is not attached.
        /// </summary>
        internal void OnReviewPublished(ReviewRecord record)
        {
            if (record == null || !attached || !ModFeatures.IsActive(FlawFeatureId)) return;

            float chance = PressContent.Current.Config.FlawReveal.Chance;
            if (!ModRandom.Stream(StreamId).Chance(chance)) return;

            bool revealed = DesignFlawRecallsApi.Registry.TryReveal(record.cpuSaveId, DiscoverySource.External);
            ModLog.Debug(Feature, "flaws: the review of '" + record.cpuName + "' asked " + FlawFeatureId + " to reveal a flaw: " + (revealed ? "revealed" : "nothing to reveal"));
        }

        // ---- a flaw is public ----

        /// <summary><c>FlawRegistry.OnFlawStateChanged</c>: a flaw that is public gets an article and a note in the review of its CPU.</summary>
        internal void OnFlawStateChanged(FlawRecordView view)
        {
            if (view == null || view.State != FlawState.Public) return;

            PressState state = stateOf();
            string flawId = view.Id.ToString(CultureInfo.InvariantCulture);
            if (ArticleLog.Has(state, Feature, ArticleTitleKey, flawId)) return;   // announced before (the flaw changed in some other way while still public)

            string cpu = CpuLabelOf(view.BaseCpuId);
            publisher.Publish(Feature, ArticleTitleKey, ArticleBodyKey, new List<string> { cpu, flawId });
            AddNote(state, view.BaseCpuId);
            ModLog.Debug(Feature, "flaws: the flaw #" + flawId + " of '" + cpu + "' is public, the press writes about it");
        }

        private static void AddNote(PressState state, int cpuSaveId)
        {
            ReviewRecord review = ReviewArchive.Find(state, cpuSaveId);
            if (review == null) return;

            if (review.notes == null) review.notes = new List<string>();
            if (!review.notes.Contains(FlawPublicNoteKey)) review.notes.Add(FlawPublicNoteKey);
        }

        private static string CpuLabelOf(int cpuSaveId)
        {
            string label = PressUi.CpuLabelOf(cpuSaveId);
            if (string.IsNullOrEmpty(label)) label = PressUi.ReviewedCpuLabel(cpuSaveId);
            return string.IsNullOrEmpty(label) ? ModL10n.L(ArticleUnknownCpuKey) : label;
        }
    }
}
