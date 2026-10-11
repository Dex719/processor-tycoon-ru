namespace PTMod.Features.DesignFlawRecalls
{
    // Pure public model of design-flaw-recalls (no Unity, no game types): everything in Api/Model/ must stay free of
    // UnityEngine and Assembly-CSharp so that the pure logic of the feature (Logic/**) and its tests can share these files.
    // The numeric values are part of the save format (FlawRecordDto stores them as ints) - never renumber them.

    /// <summary>How bad a design flaw is (design.md, "Data Models"). The values are saved as ints.</summary>
    public enum FlawSeverity
    {
        /// <summary>A small defect: a quiet patch or a rare return.</summary>
        Minor = 0,

        /// <summary>A serious defect: it hurts reputation and sales if it becomes public.</summary>
        Major = 1,

        /// <summary>A defect that threatens the product line: the heaviest losses and the strongest press reaction.</summary>
        Critical = 2,
    }

    /// <summary>
    /// Where a flaw record is in its life (state diagram in design.md). A consumer sees only the states other than <see cref="Hidden"/>
    /// (<see cref="FlawRegistry.GetKnownFlaws"/>, <see cref="FlawRegistry.FindByCpu"/>). The values are saved as ints.
    /// </summary>
    public enum FlawState
    {
        /// <summary>The CPU has a flaw nobody has found yet. Never shown through the public API.</summary>
        Hidden = 0,

        /// <summary>The company knows about the flaw, the public does not (found by its own engineers).</summary>
        InternalKnown = 1,

        /// <summary>The flaw is public: press, buyers and partners know.</summary>
        Public = 2,

        /// <summary>A silent or announced revision of the design is under way (the flaw is being fixed in the next batch).</summary>
        Stepping = 3,

        /// <summary>A patch limits the damage for a while (the flaw itself stays until the patch term ends or a revision is paid).</summary>
        Mitigated = 4,

        /// <summary>The flaw is closed (recall done, revision finished or patch term over). Final.</summary>
        Resolved = 5,

        /// <summary>The CPU left the market before anyone found the flaw. Final.</summary>
        Expired = 6,

        /// <summary>The record was closed without a solution (bankruptcy of the owner, CPU not found). Final.</summary>
        Closed = 7,
    }

    /// <summary>How a flaw came to light. The values are saved as ints.</summary>
    public enum DiscoverySource
    {
        /// <summary>No discovery yet (a <see cref="FlawState.Hidden"/> record).</summary>
        None = 0,

        /// <summary>The company's own engineers found it: the state becomes <see cref="FlawState.InternalKnown"/>.</summary>
        Internal = 1,

        /// <summary>Customers, reviewers or the press found it: the state becomes <see cref="FlawState.Public"/>.</summary>
        External = 2,

        /// <summary>A cover-up leaked (an ignored or quietly revised flaw became <see cref="FlawState.Public"/>).</summary>
        Leak = 3,

        /// <summary>The discovery was forced by the model itself (the flaw cannot stay hidden any longer).</summary>
        Forced = 4,

        /// <summary>Another feature asked for it through the public API (<c>press-reviews</c>, <c>historical-events</c>).</summary>
        Api = 5,
    }

    /// <summary>
    /// What the owner decided to do about a known flaw. The order Recall, Stepping, Patch, Ignore is also the order of the multipliers
    /// of <see cref="IFlawResponseBias.Multipliers"/> (<see cref="FlawResponseBiasOrder"/>). The values are saved as ints.
    /// </summary>
    public enum FlawResponse
    {
        /// <summary>No decision yet.</summary>
        None = 0,

        /// <summary>Recall: replace the CPUs already sold (the most expensive, the best for reputation).</summary>
        Recall = 1,

        /// <summary>Revision (stepping): fix the design for the next batches, optionally without announcing it.</summary>
        Stepping = 2,

        /// <summary>Patch: a cheap mitigation (microcode, firmware, driver) that costs some performance.</summary>
        Patch = 3,

        /// <summary>Ignore: do nothing; risk a reputation drain, lost OEM contracts and a lawsuit.</summary>
        Ignore = 4,
    }

    /// <summary>
    /// The order of the four multipliers of <see cref="IFlawResponseBias.Multipliers"/>: <c>[Recall, Stepping, Patch, Ignore]</c>
    /// (FR-17 item 1). The index of a response is <c>(int)response - 1</c>.
    /// </summary>
    public static class FlawResponseBiasOrder
    {
        /// <summary>Index of the multiplier of <see cref="FlawResponse.Recall"/>.</summary>
        public const int Recall = 0;

        /// <summary>Index of the multiplier of <see cref="FlawResponse.Stepping"/>.</summary>
        public const int Stepping = 1;

        /// <summary>Index of the multiplier of <see cref="FlawResponse.Patch"/>.</summary>
        public const int Patch = 2;

        /// <summary>Index of the multiplier of <see cref="FlawResponse.Ignore"/>.</summary>
        public const int Ignore = 3;

        /// <summary>How many multipliers the array holds; any other length is read as "all 1.0".</summary>
        public const int Length = 4;
    }

    /// <summary>
    /// The ids of the built-in flaw types (<c>types</c> in <c>Content/design-flaw-recalls/flaws.json</c>), for the <c>typeId</c> of
    /// <see cref="FlawRegistry.ForceFlaw"/> and <see cref="FlawRecordView.TypeId"/>. A player or modder can add types in the content
    /// override, so an id outside this list is possible; <c>ForceFlaw</c> refuses an id the content does not know.
    /// </summary>
    public static class FlawTypeIds
    {
        /// <summary>Arithmetic error of the core (the Pentium FDIV kind).</summary>
        public const string Arithmetic = "arith";

        /// <summary>Instability, especially under overclocking.</summary>
        public const string Stability = "stability";

        /// <summary>Thermal problems.</summary>
        public const string Thermal = "thermal";

        /// <summary>Security hole in the microarchitecture (the Meltdown kind); the content allows it from 2015.</summary>
        public const string Security = "security";
    }
}
