using System;
using System.Collections.Generic;
using System.Globalization;
using PTMod.Core;
using PTMod.Core.Logic.Companies;
using PTMod.Features.RivalAcquisitions;

namespace PTMod.Features.RivalPersonalities
{
    /// <summary>
    /// The soft link with <c>rival-acquisitions</c> (design.md, Component 14; FR-15, TSK-027). Two parts:
    /// <list type="bullet">
    /// <item><b>The price.</b> A provider on the channel <c>acquisition.price</c> of that feature (<see cref="RegisterProvider"/>): the price of taking a
    /// company over is multiplied by <c>1 + max(0, -a) / 200</c>, where <c>a</c> is its attitude to the player (<see cref="AcquisitionRules.PriceMultiplier"/>:
    /// a hostile rival costs up to 1.5 times more). Registered once, in <c>Register</c>: the core binds a provider to a channel by name, so the order in which
    /// the two features register does not matter, and it applies the provider only while this feature is active, so switching the feature off and on
    /// needs nothing. The cached value of a company is dropped when its attitude moves.</item>
    /// <item><b>The deals.</b> <see cref="Attach"/> subscribes to <c>RivalAcquisitionsApi.DealCompleted</c> (on <c>ModTicks.GameReady</c> and when a feature is
    /// switched on) while <c>rival-acquisitions</c> is active: a <c>Hostile</c> deal lowers the attitude of the other rivals, a target that left the game is
    /// put aside (<see cref="AcquisitionLinkCore"/>). After a load the targets of the earlier deals are checked once.</item>
    /// </list>
    /// Without <c>rival-acquisitions</c> neither part does anything. Main thread only.
    /// </summary>
    internal sealed class AcquisitionLink
    {
        private const string Feature = RivalPersonalitiesFeature.FeatureId;
        private const string PriceReasonKey = "rival.reason.acquisitionPrice";

        private static bool _providerRegistered;

        private readonly AcquisitionLinkCore _core;
        private bool _attached;

        public AcquisitionLink(RivalPersonalitiesFeature feature, AcquisitionLinkCore core)
        {
            _core = core;
            feature.Attitudes.Changed += OnAttitudeChanged;
        }

        /// <summary>The subscription to the deals is in place.</summary>
        public bool IsAttached
        {
            get { return _attached; }
        }

        /// <summary>
        /// Registers the provider of the price (once per session; later calls do nothing). Returns true the time it registered. Call it from <c>Register</c>:
        /// the provider stays in the core when the feature is switched off and is skipped there.
        /// </summary>
        public static bool RegisterProvider()
        {
            if (_providerRegistered) return false;
            _providerRegistered = true;

            ModModifiers.RegisterProvider(Feature, ModChannels.AcquisitionPrice, PriceMultiplier, PriceReasonKey);
            ModLog.Debug(Feature, "acquisition link: provider on '" + ModChannels.AcquisitionPrice + "' registered");
            return true;
        }

        /// <summary>The multiplier of the price of the company in the question (1 for the player, a company without a character and a question without a company).</summary>
        private static float PriceMultiplier(ModifierContext context)
        {
            RivalPersonalitiesFeature feature = RivalPersonalitiesFeature.Instance;
            if (feature == null) return 1f;

            int? uid = RivalApiService.UidOf(context.Company);
            int fromKey;
            if (!uid.HasValue && context.CompanyKey != null && CompanyKey.TryGetAiId(context.CompanyKey, out fromKey)) uid = fromKey;
            if (!uid.HasValue || !feature.Attitudes.IsKnown(uid.Value)) return 1f;

            return AcquisitionRules.PriceMultiplier(feature.Attitudes.Get(uid.Value), feature.Catalog.Config.acquisition.hostilePriceDivisor);
        }

        /// <summary>The attitude of a company moved: its cached price is out of date.</summary>
        private void OnAttitudeChanged(int uniqueId)
        {
            string key = CompanyKey.ForAi(uniqueId);
            if (key != null && ModModifiers.HasAny(ModChannels.AcquisitionPrice)) ModModifiers.Invalidate(ModChannels.AcquisitionPrice, key);
        }

        // ---- the deals ----

        /// <summary>
        /// Subscribes to the deals when this feature and <c>rival-acquisitions</c> are active (once), and checks the targets of the earlier deals (every call:
        /// a save that was loaded may hold deals this feature has not seen). Safe to call any number of times.
        /// </summary>
        public void Attach()
        {
            if (!ModFeatures.IsActive(Feature) || !ModFeatures.IsActive(RivalAcquisitionsApi.FeatureId)) return;

            if (!_attached)
            {
                RivalAcquisitionsApi.DealCompleted += OnDeal;
                _attached = true;
                ModLog.Debug(Feature, "acquisition link: attached (following RivalAcquisitionsApi.DealCompleted)");
            }

            ModHooks.Guard(Feature, "AcquisitionLink/sync", () => SyncTargets());
        }

        /// <summary>Ends the subscription.</summary>
        public void Detach()
        {
            if (!_attached) return;
            _attached = false;
            RivalAcquisitionsApi.DealCompleted -= OnDeal;
            ModLog.Debug(Feature, "acquisition link: detached");
        }

        /// <summary><c>ModFeatures.StateChanged</c>: <c>rival-acquisitions</c> was switched on or off (the handler is not called while this feature is off).</summary>
        public void OnFeatureStateChanged(string featureId, FeatureState state)
        {
            if (!string.Equals(featureId, RivalAcquisitionsApi.FeatureId, StringComparison.Ordinal)) return;

            if (state == FeatureState.Enabled)
            {
                if (ModTicks.IsGameReady) Attach();
            }
            else
            {
                Detach();
            }
        }

        /// <summary><c>RivalAcquisitionsApi.DealCompleted</c>: a deal of the player is done (the core raises it through <c>ModHooks.Raise</c>: never while this feature is off).</summary>
        private void OnDeal(DealRecord deal)
        {
            if (deal == null) return;
            ModHooks.Guard(Feature, "AcquisitionLink/DealCompleted", () =>
            {
                bool absorbed = RivalAcquisitionsApi.IsAbsorbedByPlayer(deal.targetUniqueId);
                DealEffect effect = _core.OnDeal(deal.type, deal.targetUniqueId, absorbed);
                ModLog.Info(Feature, "acquisition link: deal '" + deal.type + "' for " + AttitudeNotifier.NameOf(deal.targetUniqueId) + ": "
                                     + (effect.TargetDormant ? "the target is put aside, " : string.Empty)
                                     + effect.Alarmed.ToString(CultureInfo.InvariantCulture) + " other rival(s) took note");
            });
        }

        /// <summary>After a load: puts aside the targets of the earlier deals that left the game. Returns how many.</summary>
        public int SyncTargets()
        {
            var deals = new List<KeyValuePair<string, int>>();
            IReadOnlyList<DealRecord> all = RivalAcquisitionsApi.Deals;
            for (int i = 0; i < all.Count; i++)
            {
                if (all[i] != null) deals.Add(new KeyValuePair<string, int>(all[i].type, all[i].targetUniqueId));
            }

            int count = _core.SyncTargets(deals, RivalAcquisitionsApi.IsAbsorbedByPlayer);
            if (count > 0) ModLog.Info(Feature, "acquisition link: " + count.ToString(CultureInfo.InvariantCulture) + " earlier target(s) put aside");
            return count;
        }
    }
}
