namespace PTMod.Features.BankruptcyRecovery
{
    /// <summary>
    /// The tab of the rescue menu that <see cref="RecoveryApi.OpenMenu"/> opens (design: tabs "Summary", "Assets", "Loans", "Journal").
    /// </summary>
    public enum RecoveryTab
    {
        /// <summary>Balance, default threshold, deficit, days to bankruptcy, debt, next payment.</summary>
        Summary,

        /// <summary>Sale of production lines, stock, retirement of CPUs, exit to fabless.</summary>
        Assets,

        /// <summary>Startup credit line, bridge loan, rescue loans and the investor share.</summary>
        Loans,

        /// <summary>The journal of capital operations of the feature.</summary>
        Journal,
    }

    /// <summary>The way the player accepted the "last chance" offer (FR-8); see <see cref="RecoveryApi.BailoutAccepted"/>.</summary>
    public enum BailoutKind
    {
        /// <summary>A rescue loan: 36 months, a high rate.</summary>
        RescueLoan,

        /// <summary>The investor share: a monthly part of the profit for a fixed time, capped.</summary>
        InvestorShare,
    }

    /// <summary>The kind of a loan of the feature (<see cref="LoanView.Kind"/>); stored in the save as the name of the member.</summary>
    public enum LoanKind
    {
        /// <summary>The 12-month bridge loan (FR-6).</summary>
        Bridge,

        /// <summary>The term loan the startup credit line turns into when its time is over (FR-7).</summary>
        Term,

        /// <summary>The rescue loan of the "last chance" offer (FR-8).</summary>
        RescueLoan,
    }
}
