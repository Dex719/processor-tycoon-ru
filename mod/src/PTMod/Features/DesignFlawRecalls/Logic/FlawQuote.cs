namespace PTMod.Features.DesignFlawRecalls
{
    /// <summary>
    /// The prices and consequences of the four reactions to a known flaw, computed once at the discovery and saved in the record (D9, FR-5 item 2):
    /// the numbers the player sees under the buttons are exactly the numbers that are applied, before and after a save and a load (NFR-5). The fields
    /// repeat <see cref="FlawQuoteDto"/> one to one (a test checks that); money is rounded to whole units of currency.
    /// </summary>
    public sealed class FlawQuote
    {
        /// <summary>The recall (<c>ExpenseOrigin.Fines</c>, once), already limited by the ceiling when <see cref="RecallCapped"/>.</summary>
        public float RecallCost;

        /// <summary>The recall hit the ceiling of the share of the revenue of the last 12 months.</summary>
        public bool RecallCapped;

        /// <summary>The whole price of the revision (<c>ExpenseOrigin.Development</c>, spread over <see cref="SteppingDays"/> days).</summary>
        public float SteppingCost;

        /// <summary>The length of the revision in days.</summary>
        public int SteppingDays;

        /// <summary>The daily charge of the revision: <see cref="SteppingCost"/> divided by <see cref="SteppingDays"/>.</summary>
        public float SteppingDailyCost;

        /// <summary>The claims after a public revision, charged when it is finished (<c>ExpenseOrigin.Fines</c>).</summary>
        public float ResidualClaimsCost;

        /// <summary>A patch exists for the type of the flaw in the current year.</summary>
        public bool PatchAvailable;

        /// <summary>The price of the patch (<c>ExpenseOrigin.Development</c>); filled also when the patch is not available (informative).</summary>
        public float PatchCost;

        /// <summary>The share of the performance the patch costs (the multiplier of the rating is <c>1 - PatchIpsLoss</c>).</summary>
        public float PatchIpsLoss;

        /// <summary>How long the patch limits the damage, in months.</summary>
        public int PatchMonths;

        /// <summary>The lawsuit that an ignored public flaw may bring (<c>ExpenseOrigin.Fines</c>, at most once).</summary>
        public float ClassActionCost;

        /// <summary>The chance that each OEM contract with the CPU is cancelled when the flaw is ignored.</summary>
        public float OemCancelChance;

        /// <summary>The multiplier of <c>contract.signChance</c> that an ignored flaw brings.</summary>
        public float IgnoreSignChanceMultiplier;

        /// <summary>The change of the brand reputation for each reaction, points.</summary>
        public int ReputationRecall, ReputationStepping, ReputationPatch, ReputationIgnore;

        /// <summary>The units of the affected CPUs sold when the quote was made.</summary>
        public long AffectedUnits;

        /// <summary>The saved form of the quote.</summary>
        public FlawQuoteDto ToDto()
        {
            return new FlawQuoteDto
            {
                RecallCost = RecallCost,
                RecallCapped = RecallCapped,
                SteppingCost = SteppingCost,
                SteppingDays = SteppingDays,
                SteppingDailyCost = SteppingDailyCost,
                ResidualClaimsCost = ResidualClaimsCost,
                PatchAvailable = PatchAvailable,
                PatchCost = PatchCost,
                PatchIpsLoss = PatchIpsLoss,
                PatchMonths = PatchMonths,
                ClassActionCost = ClassActionCost,
                OemCancelChance = OemCancelChance,
                IgnoreSignChanceMultiplier = IgnoreSignChanceMultiplier,
                ReputationRecall = ReputationRecall,
                ReputationStepping = ReputationStepping,
                ReputationPatch = ReputationPatch,
                ReputationIgnore = ReputationIgnore,
                AffectedUnits = AffectedUnits,
            };
        }

        /// <summary>The quote of a saved record; <c>null</c> for <c>null</c> (a record that has no quote yet).</summary>
        public static FlawQuote FromDto(FlawQuoteDto dto)
        {
            if (dto == null) return null;

            return new FlawQuote
            {
                RecallCost = dto.RecallCost,
                RecallCapped = dto.RecallCapped,
                SteppingCost = dto.SteppingCost,
                SteppingDays = dto.SteppingDays,
                SteppingDailyCost = dto.SteppingDailyCost,
                ResidualClaimsCost = dto.ResidualClaimsCost,
                PatchAvailable = dto.PatchAvailable,
                PatchCost = dto.PatchCost,
                PatchIpsLoss = dto.PatchIpsLoss,
                PatchMonths = dto.PatchMonths,
                ClassActionCost = dto.ClassActionCost,
                OemCancelChance = dto.OemCancelChance,
                IgnoreSignChanceMultiplier = dto.IgnoreSignChanceMultiplier,
                ReputationRecall = dto.ReputationRecall,
                ReputationStepping = dto.ReputationStepping,
                ReputationPatch = dto.ReputationPatch,
                ReputationIgnore = dto.ReputationIgnore,
                AffectedUnits = dto.AffectedUnits,
            };
        }
    }
}
