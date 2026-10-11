using System.Collections.Generic;

namespace PTMod.Features.DesignFlawRecalls
{
    // The DTOs of the section "design-flaw-recalls" of the side save file (FR-16, design.md, data models of the save section). Plain public fields, so that
    // ModJson.Bind / ModJson.From map them by name. Days are whole days counted from 1971-01-01 (FlawDays). There is NO version field: the
    // version of the section (_schema) is written and read by the core (IModSaveParticipant.SchemaVersion, D20). The enums are stored as ints
    // (FlawSeverity, FlawState, DiscoverySource, FlawResponse) and are checked on load (FlawSaveCodec). These files are pure (no Unity, no game).
    //
    // Why they live in Logic/ and not in Save/: the unit tests compile Features/<Pascal>/Logic only.

    /// <summary>
    /// The whole section of the feature in the side save file. The initial values are the empty state of a new game; the lists are never
    /// <c>null</c> after <see cref="FlawSaveCodec.Read"/>.
    /// </summary>
    public sealed class FlawSaveData
    {
        /// <summary>The id the next record gets (ids are unique in a save and never reused).</summary>
        public int nextRecordId = 1;

        /// <summary>The last game day the feature saw while enabled (the end of <c>OnDay</c>, <c>OnDisabled</c>): the pause shift of the revisions is counted from it (D12).</summary>
        public int lastActiveDay;

        /// <summary><c>SaveID</c>s of the CPUs that were already rolled (FR-1 item 5); in a save without the section, every CPU that existed.</summary>
        public List<int> rolledCpuIds = new List<int>();

        /// <summary><c>SaveID</c>s of the CPUs created with a rush project (FR-20).</summary>
        public List<int> rushCpuIds = new List<int>();

        /// <summary>Rush projects that are not completed yet (FR-20).</summary>
        public List<RushProjectDto> rushProjects = new List<RushProjectDto>();

        /// <summary>The flaw records, in the order of their ids.</summary>
        public List<FlawRecordDto> records = new List<FlawRecordDto>();

        /// <summary>Ids of the records that wait for the player's decision and have not got their window yet (FR-5 item 6).</summary>
        public List<int> pendingDecisionQueue = new List<int>();
    }

    /// <summary>A rush project that was started in the designer and has not produced its CPU yet (FR-20).</summary>
    public sealed class RushProjectDto
    {
        /// <summary>Key of the company (<c>player</c>; the AI does not rush): <c>^(player|ai:\d+)$</c>.</summary>
        public string companyKey;

        /// <summary>The name of the CPU the project makes.</summary>
        public string cpuName;

        /// <summary>The day the project is planned to end.</summary>
        public int plannedEndDay;
    }

    /// <summary>One flaw record as it is saved (the runtime <c>FlawRecord</c> is made from it and writes back into it).</summary>
    public sealed class FlawRecordDto
    {
        public int id;

        /// <summary>Key of the owner (<c>player</c> or <c>ai:&lt;UniqueID&gt;</c>).</summary>
        public string companyKey;

        /// <summary><c>SaveID</c> of the base CPU the flaw was rolled for.</summary>
        public int baseCpuId;

        /// <summary>The base CPU and its variants.</summary>
        public List<int> cpuIds = new List<int>();

        public string typeId;

        /// <summary><c>FlawSeverity</c>, <c>FlawState</c>, <c>DiscoverySource</c>, <c>FlawResponse</c> as ints.</summary>
        public int severity, state, source, response;

        public bool coverUp, automaticChoice, suitFiled, quietStepping;

        public float riskAtRelease, inflationAtRelease;

        public int releaseDay, discoveredDay, publicSinceDay;

        public long unitsAtDiscovery;

        /// <summary>The prices of the four reactions, computed once at the discovery (D9); <c>null</c> before that.</summary>
        public FlawQuoteDto quote;

        public int steppingEndDay, lastChargedDay;

        public float steppingDailyCost;

        public int mitigationEndDay;

        public float ipsLoss;

        public int ignoreDrainUntilDay, leakTailUntilDay;

        /// <summary>Ids (<c>m-...</c>) of the <c>ModModifiers</c> entries this record made (<c>market.rating.cpu</c>, <c>contract.signChance</c>).</summary>
        public List<string> modifierEntryIds = new List<string>();

        public float spentTotal;
    }

    /// <summary>
    /// The quote of a record as it is saved: the fields of <c>FlawQuote</c> one to one, with the same (capitalised) names, so that the numbers
    /// the player saw in the decision window stay the same after a load (NFR-5).
    /// </summary>
    public sealed class FlawQuoteDto
    {
        public float RecallCost;
        public bool RecallCapped;
        public float SteppingCost;
        public int SteppingDays;
        public float SteppingDailyCost;
        public float ResidualClaimsCost;
        public bool PatchAvailable;
        public float PatchCost;
        public float PatchIpsLoss;
        public int PatchMonths;
        public float ClassActionCost;
        public float OemCancelChance;
        public float IgnoreSignChanceMultiplier;
        public int ReputationRecall, ReputationStepping, ReputationPatch, ReputationIgnore;
        public long AffectedUnits;
    }
}
