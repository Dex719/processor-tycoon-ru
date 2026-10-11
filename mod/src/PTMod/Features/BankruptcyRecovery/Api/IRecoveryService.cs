using System;
using System.Collections.Generic;

namespace PTMod.Features.BankruptcyRecovery
{
    /// <summary>
    /// What <see cref="RecoveryApi"/> forwards to: the implementation of the feature (the services, the state and the rescue menu). Other
    /// features never use this interface, they use the static <see cref="RecoveryApi"/>; the feature registers its implementation once in
    /// <c>BankruptcyRecoveryFeature.Register</c> with <c>RecoveryApi.SetService</c>.
    /// <para>
    /// <see cref="RecoveryApi"/> calls the service only while the feature is active, inside <c>ModHooks.Guard</c> (an exception is counted
    /// against <c>bankruptcy-recovery</c> and the caller gets the null-object answer), on the main thread. The service may therefore assume the
    /// feature is active and does not need its own try/catch for the callers' sake.
    /// </para>
    /// </summary>
    public interface IRecoveryService
    {
        /// <summary>The summary of the rescue menu, taken now. Never null (a struct).</summary>
        RecoverySummary GetSummary();

        /// <summary>The loans of the feature now, oldest first: a snapshot the caller may keep. Empty when there are none; null is treated as empty.</summary>
        IReadOnlyList<LoanView> GetLoans();

        /// <summary>The startup credit line now; <see cref="CreditLineView.Open"/> is false when it was never opened or is closed.</summary>
        CreditLineView GetCreditLine();

        /// <summary>How many times the player accepted the "last chance" offer in this game (the save's counter).</summary>
        int BailoutsUsed { get; }

        /// <summary>Opens the rescue menu on the tab (modal, the game pauses). Does nothing outside the game scene.</summary>
        void OpenMenu(RecoveryTab tab);
    }

    /// <summary>
    /// The null object behind <see cref="RecoveryApi"/> until the feature registers its service, and while the feature is not active: empty
    /// answers, no menu. Callers need no checks.
    /// </summary>
    internal sealed class NullRecoveryService : IRecoveryService
    {
        public static readonly NullRecoveryService Instance = new NullRecoveryService();

        private NullRecoveryService()
        {
        }

        public RecoverySummary GetSummary()
        {
            return default(RecoverySummary);
        }

        public IReadOnlyList<LoanView> GetLoans()
        {
            return Array.Empty<LoanView>();
        }

        public CreditLineView GetCreditLine()
        {
            return default(CreditLineView);
        }

        public int BailoutsUsed
        {
            get { return 0; }
        }

        public void OpenMenu(RecoveryTab tab)
        {
        }
    }
}
