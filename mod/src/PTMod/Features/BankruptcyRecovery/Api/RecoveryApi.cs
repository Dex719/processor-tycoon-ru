using System;
using System.Collections.Generic;
using PTMod.Core;

namespace PTMod.Features.BankruptcyRecovery
{
    /// <summary>
    /// The public API of <c>bankruptcy-recovery</c> for the rescue menu, other features and statistics (design: "RecoveryApi"). Static, main
    /// thread only, never throws.
    /// <list type="bullet">
    /// <item><b>Null object.</b> Until the feature registers its implementation, and while the feature is not active (<see cref="IsActive"/> is
    /// false: disabled, faulted or unavailable), every member answers like an empty game: <c>default</c> structs, an empty list, 0, and
    /// <see cref="OpenMenu"/> does nothing. A caller may use the API without checking <see cref="IsActive"/> first; it checks it only to hide
    /// its own UI.</item>
    /// <item><b>Fault isolation.</b> The calls into the implementation go through <c>ModHooks.Guard</c>: an exception is counted against
    /// <c>bankruptcy-recovery</c> and the caller gets the null-object answer.</item>
    /// <item><b>Events.</b> Raised by the feature (<c>RaiseLoanOpened</c> and the others) through <c>ModHooks.Raise</c>: the handlers of features
    /// that are not Enabled are skipped, a failing handler is counted against its own feature and the others still run. Raised on the main
    /// thread. Subscribe in <c>ModTicks.GameReady</c> or at start, never from a patch of the game.</item>
    /// </list>
    /// The money rules shared with other features (<see cref="CapitalLedger"/>) are a separate static class and do not need the feature to be active.
    /// </summary>
    public static class RecoveryApi
    {
        /// <summary>The id of the feature (<c>ModFeatures.IsActive</c>, the log, the circuit breaker).</summary>
        public const string FeatureId = "bankruptcy-recovery";

        private const string LoanOpenedEvent = "RecoveryApi.LoanOpened";
        private const string LoanClosedEvent = "RecoveryApi.LoanClosed";
        private const string BailoutAcceptedEvent = "RecoveryApi.BailoutAccepted";

        private static IRecoveryService _service = NullRecoveryService.Instance;

        // ---- public API ----

        /// <summary>The feature is enabled and running: <c>ModFeatures.IsActive("bankruptcy-recovery")</c>.</summary>
        public static bool IsActive
        {
            get
            {
                try
                {
                    return ModFeatures.IsActive(FeatureId);
                }
                catch (Exception)
                {
                    return false;
                }
            }
        }

        /// <summary>The summary of the rescue menu (balance, threshold, deficit, days to bankruptcy, debt, next payment) taken now.</summary>
        public static RecoverySummary GetSummary()
        {
            IRecoveryService service = Current();
            return ModHooks.Guard(FeatureId, "RecoveryApi.GetSummary", () => service.GetSummary(), default(RecoverySummary));
        }

        /// <summary>The loans of the feature now (bridge, term, rescue): a snapshot. Never null.</summary>
        public static IReadOnlyList<LoanView> GetLoans()
        {
            IRecoveryService service = Current();
            IReadOnlyList<LoanView> loans = ModHooks.Guard<IReadOnlyList<LoanView>>(
                FeatureId, "RecoveryApi.GetLoans", () => service.GetLoans(), null);
            return loans ?? Array.Empty<LoanView>();
        }

        /// <summary>The startup credit line now; <see cref="CreditLineView.Open"/> is false when there is none.</summary>
        public static CreditLineView GetCreditLine()
        {
            IRecoveryService service = Current();
            return ModHooks.Guard(FeatureId, "RecoveryApi.GetCreditLine", () => service.GetCreditLine(), default(CreditLineView));
        }

        /// <summary>How many times the player accepted the "last chance" offer in this game; 0 while the feature is not active.</summary>
        public static int BailoutsUsed
        {
            get
            {
                IRecoveryService service = Current();
                return ModHooks.Guard(FeatureId, "RecoveryApi.BailoutsUsed", () => service.BailoutsUsed, 0);
            }
        }

        /// <summary>
        /// Opens the rescue menu on the tab (modal, the game pauses). Does nothing while the feature is not active and outside the game scene.
        /// </summary>
        public static void OpenMenu(RecoveryTab tab = RecoveryTab.Summary)
        {
            IRecoveryService service = Current();
            ModHooks.Guard(FeatureId, "RecoveryApi.OpenMenu", () => service.OpenMenu(tab));
        }

        /// <summary>A loan of the feature was opened (bridge loan taken, rescue loan accepted, credit line turned into a term loan).</summary>
        public static event Action<LoanView> LoanOpened;

        /// <summary>A loan of the feature was closed (paid off, prepaid, or converted into ordinary debt when the feature was switched off).</summary>
        public static event Action<LoanView> LoanClosed;

        /// <summary>
        /// The player accepted the "last chance" offer (FR-8). For statistics: the assisted flag of a scenario run is not set by it (owner's decision).
        /// </summary>
        public static event Action<BailoutKind> BailoutAccepted;

        // ---- for the feature ----

        /// <summary>
        /// Registers the implementation of the feature (once, in <c>BankruptcyRecoveryFeature.Register</c>); null puts the null object back.
        /// Not for other features.
        /// </summary>
        internal static void SetService(IRecoveryService service)
        {
            _service = service ?? NullRecoveryService.Instance;
        }

        /// <summary>The registered implementation; the null object when there is none. For the feature and its tests.</summary>
        internal static IRecoveryService Service
        {
            get { return _service; }
        }

        /// <summary>Raises <see cref="LoanOpened"/> (the feature calls it after the loan is in the state).</summary>
        internal static void RaiseLoanOpened(LoanView loan)
        {
            ModHooks.Raise(LoanOpenedEvent, LoanOpened, loan);
        }

        /// <summary>Raises <see cref="LoanClosed"/> (the feature calls it after the loan left the state).</summary>
        internal static void RaiseLoanClosed(LoanView loan)
        {
            ModHooks.Raise(LoanClosedEvent, LoanClosed, loan);
        }

        /// <summary>Raises <see cref="BailoutAccepted"/> (the feature calls it after the bailout was applied).</summary>
        internal static void RaiseBailoutAccepted(BailoutKind kind)
        {
            ModHooks.Raise(BailoutAcceptedEvent, BailoutAccepted, kind);
        }

        // The implementation while the feature is active, the null object otherwise.
        private static IRecoveryService Current()
        {
            return IsActive ? _service : NullRecoveryService.Instance;
        }
    }
}
