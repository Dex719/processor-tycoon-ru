using System;
using System.Collections.Generic;
using System.Globalization;
using PTMod.Core;
using PTMod.Core.Logic.Companies;
using PTMod.Features.DesignFlawRecalls;

namespace PTMod.Features.RivalPersonalities
{
    /// <summary>
    /// The soft link with <c>design-flaw-recalls</c> (design.md, Component 15; FR-13 item 5, FR-17; TSK-020, TSK-030). While both features are active it
    /// <list type="bullet">
    /// <item>gives the registry of that feature the multipliers of the archetypes (<see cref="FlawResponseBias"/>, <c>FlawRegistry.SetResponseBias</c>);</item>
    /// <item>listens to <c>FlawRegistry.OnFlawStateChanged</c>: a <c>Major</c> or <c>Critical</c> flaw of an AI company that has become public starts the
    /// caution of the company for <c>flawCaution.months</c> (<see cref="CautionBook.OnPublicFlaw"/>), once per flaw record.</item>
    /// </list>
    /// <see cref="Attach"/> is called on <c>ModTicks.GameReady</c>, when this feature is switched on in a ready game and when <c>design-flaw-recalls</c>
    /// is switched on; <see cref="Detach"/> when this feature or that one is switched off (the slot of the bias is cleared, the subscription ends). Both are
    /// safe to call at any time and any number of times. Without <c>design-flaw-recalls</c> nothing is registered and no caution is made. Main thread only.
    /// </summary>
    internal sealed class FlawLink
    {
        private const string Feature = RivalPersonalitiesFeature.FeatureId;

        private readonly RivalPersonalitiesFeature _feature;
        private readonly CautionBook _cautions;
        private readonly FlawResponseBias _bias = new FlawResponseBias();
        private bool _attached;

        public FlawLink(RivalPersonalitiesFeature feature, CautionBook cautions)
        {
            _feature = feature;
            _cautions = cautions;
        }

        /// <summary>The bias is registered and the flaws are followed.</summary>
        public bool IsAttached
        {
            get { return _attached; }
        }

        /// <summary>
        /// Registers the bias and subscribes, when this feature and <c>design-flaw-recalls</c> are active (once; the subscription is the registry's, it
        /// outlives a scene). Every call then looks at the flaws that are public already: a save that was loaded may have flaws this feature missed.
        /// </summary>
        public void Attach()
        {
            if (!ModFeatures.IsActive(Feature) || !ModFeatures.IsActive(DesignFlawRecallsApi.FeatureId)) return;

            if (!_attached)
            {
                FlawRegistry registry = DesignFlawRecallsApi.Registry;
                registry.SetResponseBias(_bias);
                registry.OnFlawStateChanged += OnFlawChanged;
                _attached = true;
                ModLog.Debug(Feature, "flaw link: attached (response bias set, following FlawRegistry.OnFlawStateChanged)");
            }

            ModHooks.Guard(Feature, "FlawLink/sync", () => SyncKnown());
        }

        /// <summary>Takes the bias out of the slot and ends the subscription.</summary>
        public void Detach()
        {
            if (!_attached) return;
            _attached = false;

            FlawRegistry registry = DesignFlawRecallsApi.Registry;
            registry.OnFlawStateChanged -= OnFlawChanged;
            registry.SetResponseBias(null);
            ModLog.Debug(Feature, "flaw link: detached (response bias cleared)");
        }

        /// <summary><c>ModFeatures.StateChanged</c>: <c>design-flaw-recalls</c> was switched on or off (the handler is not called while this feature is off).</summary>
        public void OnFeatureStateChanged(string featureId, FeatureState state)
        {
            if (!string.Equals(featureId, DesignFlawRecallsApi.FeatureId, StringComparison.Ordinal)) return;

            if (state == FeatureState.Enabled)
            {
                if (ModTicks.IsGameReady) Attach();
            }
            else
            {
                Detach();
            }
        }

        /// <summary>
        /// The flaws that are public already, looked at again (the feature was off when they went public, a save was loaded): the cautions that are due
        /// are started; the entries that exist or that ended are left as they are. Returns how many cautions were started.
        /// </summary>
        public int SyncKnown()
        {
            int started = 0;
            IReadOnlyList<FlawRecordView> flaws = DesignFlawRecallsApi.Registry.GetAllKnownFlaws();
            for (int i = 0; i < flaws.Count; i++)
            {
                FlawRecordView flaw = flaws[i];
                if (flaw != null && flaw.State == FlawState.Public && Handle(flaw)) started++;
            }
            if (started > 0) ModLog.Info(Feature, "flaw link: " + started.ToString(CultureInfo.InvariantCulture) + " caution(s) started from the flaws that were public already");
            return started;
        }

        // ---- the flaws ----

        /// <summary><c>FlawRegistry.OnFlawStateChanged</c>: only a flaw that is <c>Public</c> now starts a caution.</summary>
        private void OnFlawChanged(FlawRecordView flaw)
        {
            if (flaw == null || flaw.State != FlawState.Public) return;
            ModHooks.Guard(Feature, "FlawLink/OnFlawStateChanged", () => Handle(flaw));
        }

        /// <summary>True when a caution was started for the flaw.</summary>
        private bool Handle(FlawRecordView flaw)
        {
            int uid;
            if (!CompanyKey.TryGetAiId(flaw.CompanyKey, out uid)) return false;   // a flaw of the player is not a rival's business
            if (_feature.Book.IsDormant(uid)) return false;

            DateTime today = ModTicks.Today;
            DateTime since = flaw.PublicSinceDay > 0 ? RivalDates.FromDayNumber(flaw.PublicSinceDay) : today;
            CautionOutcome outcome = _cautions.OnPublicFlaw(uid, flaw.Id, (int)flaw.Severity, since, today);
            if (outcome != CautionOutcome.Started) return false;

            DateTime? until = _cautions.UntilOf(uid, today);
            ModLog.Info(Feature, "caution: " + AttitudeNotifier.NameOf(uid) + " (ai:" + uid.ToString(CultureInfo.InvariantCulture) + ") designs more carefully until "
                                 + (until.HasValue ? RivalDates.ToText(until.Value) : "?") + " after the public flaw #" + flaw.Id.ToString(CultureInfo.InvariantCulture)
                                 + " (" + flaw.Severity + ")");
            return true;
        }
    }
}
