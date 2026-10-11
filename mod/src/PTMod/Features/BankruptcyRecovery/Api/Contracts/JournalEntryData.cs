using System;

namespace PTMod.Features.BankruptcyRecovery.Logic
{
    /// <summary>
    /// One record of the journal of the feature (FR-10): part of the save section (<c>RecoveryState.journal</c>) and the element of
    /// <see cref="CapitalLedger.Journal"/>. A plain data class without Unity or game references.
    /// <para>
    /// The type lives in <c>Api\Contracts\</c> (not in <c>Logic\</c>) because <see cref="CapitalLedger"/> returns it and a feature that depends on
    /// <c>bankruptcy-recovery</c> is built with the <c>Api\</c> folder only. The folder holds pure data types only (no <c>PTMod.Core</c>, Unity or
    /// game references) so that the test builds can compile it next to <c>Logic\</c> (see <c>mod/requests/bankruptcy-recovery.md</c>). The namespace
    /// stays <c>...BankruptcyRecovery.Logic</c> as in the design, so the state classes of <c>Logic\</c> and the tests find it by their usual <c>using</c>.
    /// </para>
    /// </summary>
    [Serializable]
    public sealed class JournalEntryData
    {
        /// <summary>The game date of the operation, <c>yyyy-MM-dd</c>.</summary>
        public string date = "";

        /// <summary>The operation code (<c>bridge.open</c>, <c>loan.payment</c>, <c>disable.convert</c>...).</summary>
        public string op = "";

        /// <summary>The signed amount: positive when the balance grew, negative when it fell (the example in the design: <c>-420000.0</c> for a fee).</summary>
        public double amount;

        /// <summary>A short note for the player (<c>12m @ 30%</c>, a loan id); may be empty.</summary>
        public string details = "";
    }
}
