using System;
using System.Collections.Generic;
using PTMod.Core;

namespace PTMod.Features.RivalAcquisitions
{
    /// <summary>
    /// The public API of <c>rival-acquisitions</c> (FR-13): what <c>rival-personalities</c> and <c>scenarios-achievements</c> call. A static
    /// facade over the implementation that the feature registers; other features use only this class, <see cref="DealType"/>,
    /// <see cref="DealRecord"/> and <see cref="WaferLines"/>, never the feature's internals.
    /// <list type="bullet">
    /// <item><b>Soft dependency.</b> Nothing here throws and nothing needs the feature: while <c>rival-acquisitions</c> is off, Faulted,
    /// Unavailable or has not registered its implementation, the reads answer with the null object (no deals, share 0, nobody absorbed,
    /// <see cref="OpenWindow"/> does nothing) and no event is raised. The result is the same as on a game without the feature, so a caller needs
    /// no <c>ModFeatures.IsActive</c> check around it (and must not list the feature in its <c>DependsOn</c>).</item>
    /// <item><b>Events.</b> Raised by the feature itself (never by a caller), on the main thread, one handler at a time through
    /// <see cref="ModHooks"/>: a handler of a feature that is not active is skipped, an exception in a handler is counted against the feature
    /// that owns the handler and the next handler still runs. Subscribe once the game is ready (<c>ModTicks.GameReady</c>) and unsubscribe when
    /// your own feature stops. The events are not published through <c>EventBus</c>; <c>event-system</c> is not needed.</item>
    /// <item><b>Faults.</b> A failure inside the implementation is caught here, counted against <c>rival-acquisitions</c> (the circuit breaker
    /// of the core) and answered like the null object.</item>
    /// </list>
    /// Main thread only.
    /// </summary>
    public static class RivalAcquisitionsApi
    {
        /// <summary>The id of the feature that owns this API (<c>IModFeature.Id</c>).</summary>
        public const string FeatureId = "rival-acquisitions";

        private const string SiteDeals = "RivalAcquisitionsApi.Deals/api";
        private const string SiteGetPlayerShare = "RivalAcquisitionsApi.GetPlayerShare/api";
        private const string SiteIsAbsorbedByPlayer = "RivalAcquisitionsApi.IsAbsorbedByPlayer/api";
        private const string SiteOpenWindow = "RivalAcquisitionsApi.OpenWindow/api";
        private const string SiteRaise = "RivalAcquisitionsApi.Raise/api";

        private const string DealCompletedName = "RivalAcquisitionsApi.DealCompleted";
        private const string StakeChangedName = "RivalAcquisitionsApi.StakeChanged";
        private const string RivalDefaultedName = "RivalAcquisitionsApi.RivalDefaulted";
        private const string RivalBankruptName = "RivalAcquisitionsApi.RivalBankrupt";

        private static IRivalAcquisitionsService _service;

        // ---- reads ----

        /// <summary>
        /// All deals of the current save, oldest first; an empty list while the feature is off. A read-only view of the feature's own records:
        /// do not change them and do not keep the list across a save load (read it again).
        /// </summary>
        public static IReadOnlyList<DealRecord> Deals
        {
            get
            {
                try
                {
                    return Instance.Deals ?? Array.Empty<DealRecord>();
                }
                catch (Exception e)
                {
                    ModHooks.ReportFault(FeatureId, SiteDeals, e);
                    return Array.Empty<DealRecord>();
                }
            }
        }

        /// <summary>The share of the AI company <paramref name="uniqueId"/> that the player owns, 0 to 0.49; 0 for no stake, an unknown id or a feature that is off.</summary>
        public static double GetPlayerShare(int uniqueId)
        {
            try
            {
                double share = Instance.GetPlayerShare(uniqueId);
                return share > 0.0 && !double.IsInfinity(share) ? share : 0.0;
            }
            catch (Exception e)
            {
                ModHooks.ReportFault(FeatureId, SiteGetPlayerShare, e);
                return 0.0;
            }
        }

        /// <summary>
        /// The AI company <paramref name="uniqueId"/> was bought by the player (a <see cref="DealType.BankruptAssets"/>,
        /// <see cref="DealType.Distressed"/> or <see cref="DealType.Hostile"/> deal exists for it), so it left the game because of the player and
        /// not because of a bankruptcy of its own. True already inside the game's <c>OnCompanyWentBankrupt</c> that the takeover itself causes.
        /// False for every company while the feature is off.
        /// </summary>
        public static bool IsAbsorbedByPlayer(int uniqueId)
        {
            try
            {
                return Instance.IsAbsorbedByPlayer(uniqueId);
            }
            catch (Exception e)
            {
                ModHooks.ReportFault(FeatureId, SiteIsAbsorbedByPlayer, e);
                return false;
            }
        }

        /// <summary>
        /// Opens the "Deals" window; <paramref name="focusUniqueId"/> (0 or more) selects that company. Does nothing while the feature is off
        /// and outside the game scene.
        /// </summary>
        public static void OpenWindow(int focusUniqueId = -1)
        {
            try
            {
                Instance.OpenWindow(focusUniqueId);
            }
            catch (Exception e)
            {
                ModHooks.ReportFault(FeatureId, SiteOpenWindow, e);
            }
        }

        // ---- events ----

        /// <summary>
        /// A deal was completed (FR-13 item 1); used by <c>rival-personalities</c> and <c>scenarios-achievements</c>. The record is the one the
        /// feature keeps: read it, do not change it. For a takeover it comes after <c>ModEvents.CpuTransferred</c> of every transferred CPU and
        /// after the target left the game (<see cref="IsAbsorbedByPlayer"/> is true then).
        /// </summary>
        public static event Action<DealRecord> DealCompleted;

        /// <summary>The player's stake or rescue order in a company changed (FR-13 item 2): (<c>UniqueID</c> of the company, the new share, 0 to 0.49; 0 when the stake is gone).</summary>
        public static event Action<int, double> StakeChanged;

        /// <summary>An AI company went into default (FR-13 item 3): its <c>UniqueID</c>. Not raised while a save loads or the new game fast-forwards.</summary>
        public static event Action<int> RivalDefaulted;

        /// <summary>
        /// An AI company went bankrupt on its own (FR-13 item 3): its <c>UniqueID</c>. Not raised for a company that the mod took out of the
        /// game after a deal (see <see cref="IsAbsorbedByPlayer"/> and <see cref="DealCompleted"/> for that).
        /// </summary>
        public static event Action<int> RivalBankrupt;

        // ---- state and wiring ----

        /// <summary>The implementation to talk to: the registered one while the feature is Enabled, the null object otherwise. Never null.</summary>
        public static IRivalAcquisitionsService Instance
        {
            get { return Active() ?? NullRivalAcquisitionsService.Instance; }
        }

        /// <summary>True when the feature is Enabled and has registered its implementation (the reads are real, the events can come).</summary>
        public static bool IsAvailable
        {
            get { return Active() != null; }
        }

        /// <summary>
        /// The feature registers its implementation (in <c>Register</c> or <c>OnEnabled</c>); a null <paramref name="service"/> is the same as
        /// <see cref="Unregister"/>. The implementation is only used while the feature is Enabled, so a missed <see cref="Unregister"/> is harmless.
        /// </summary>
        internal static void Register(IRivalAcquisitionsService service)
        {
            _service = service;
        }

        /// <summary>
        /// The feature drops its implementation (in <c>OnDisabled</c>). Only the registered instance is dropped: a late call of an old
        /// instance does not remove a newer one.
        /// </summary>
        internal static void Unregister(IRivalAcquisitionsService service)
        {
            if (ReferenceEquals(_service, service)) _service = null;
        }

        private static IRivalAcquisitionsService Active()
        {
            IRivalAcquisitionsService service = _service;
            return service != null && ModFeatures.IsActive(FeatureId) ? service : null;
        }

        // ---- raising (the feature only) ----

        /// <summary>Raises <see cref="DealCompleted"/>. Ignored for a null record and while the feature is not Enabled. Never throws.</summary>
        internal static void RaiseDealCompleted(DealRecord deal)
        {
            if (deal == null || DealCompleted == null || !ModFeatures.IsActive(FeatureId)) return;

            try
            {
                ModHooks.Raise(DealCompletedName, DealCompleted, deal);
            }
            catch (Exception e)
            {
                ModHooks.ReportFault(FeatureId, SiteRaise, e);
            }
        }

        /// <summary>Raises <see cref="StakeChanged"/> with the new share of the company. Ignored while the feature is not Enabled. Never throws.</summary>
        internal static void RaiseStakeChanged(int uniqueId, double share)
        {
            if (StakeChanged == null || !ModFeatures.IsActive(FeatureId)) return;

            try
            {
                ModHooks.Raise(StakeChangedName, StakeChanged, uniqueId, share);
            }
            catch (Exception e)
            {
                ModHooks.ReportFault(FeatureId, SiteRaise, e);
            }
        }

        /// <summary>Raises <see cref="RivalDefaulted"/>. Ignored while the feature is not Enabled. Never throws.</summary>
        internal static void RaiseRivalDefaulted(int uniqueId)
        {
            if (RivalDefaulted == null || !ModFeatures.IsActive(FeatureId)) return;

            try
            {
                ModHooks.Raise(RivalDefaultedName, RivalDefaulted, uniqueId);
            }
            catch (Exception e)
            {
                ModHooks.ReportFault(FeatureId, SiteRaise, e);
            }
        }

        /// <summary>Raises <see cref="RivalBankrupt"/>. The feature does not call it for a company it took out of the game itself. Ignored while the feature is not Enabled. Never throws.</summary>
        internal static void RaiseRivalBankrupt(int uniqueId)
        {
            if (RivalBankrupt == null || !ModFeatures.IsActive(FeatureId)) return;

            try
            {
                ModHooks.Raise(RivalBankruptName, RivalBankrupt, uniqueId);
            }
            catch (Exception e)
            {
                ModHooks.ReportFault(FeatureId, SiteRaise, e);
            }
        }
    }
}
