using System.Collections.Generic;

namespace PTMod.Features.DesignFlawRecalls
{
    /// <summary>
    /// A read-only snapshot of one flaw record, as the public API hands it out (<see cref="FlawRegistry.GetKnownFlaws"/>,
    /// <see cref="FlawRegistry.GetAllKnownFlaws"/>, <see cref="FlawRegistry.FindByCpu"/>, <see cref="FlawRegistry.OnFlawStateChanged"/>).
    /// A snapshot never changes after it was handed out; ask the registry again for the current state. Consumers must not modify the
    /// fields (they are public only so that a snapshot is cheap to build and easy to fake in a test).
    /// Days are whole days counted from 1971-01-01 (the same count as the save data of <c>event-system</c>).
    /// </summary>
    public sealed class FlawRecordView
    {
        /// <summary>Id of the record, unique in a save (never reused).</summary>
        public int Id;

        /// <summary>Key of the owner company (<c>player</c> or <c>ai:&lt;UniqueID&gt;</c>, see <c>ModCompanies.KeyOf</c>); changes when the CPU moves to another company.</summary>
        public string CompanyKey;

        /// <summary><c>SaveID</c> of the base CPU the flaw was rolled for.</summary>
        public int BaseCpuId;

        /// <summary><c>SaveID</c>s of every CPU the flaw affects: the base CPU and its variants. Never null.</summary>
        public IReadOnlyList<int> CpuIds;

        /// <summary>Id of the flaw type (see <see cref="FlawTypeIds"/> for the built-in ones).</summary>
        public string TypeId;

        /// <summary>How bad the flaw is.</summary>
        public FlawSeverity Severity;

        /// <summary>Where the record is in its life. A snapshot from the public API is never <see cref="FlawState.Hidden"/>.</summary>
        public FlawState State;

        /// <summary>The decision of the owner; <see cref="FlawResponse.None"/> until there is one.</summary>
        public FlawResponse Response;

        /// <summary>How the flaw came to light.</summary>
        public DiscoverySource Source;

        /// <summary>The owner ignored or quietly revised the flaw, which makes the reputation hit worse if it leaks.</summary>
        public bool CoverUp;

        /// <summary>Days left of the current term (revision or patch); 0 when no term is running.</summary>
        public int DaysLeft;

        /// <summary>Money spent on the flaw so far (recall, revision, patch, lawsuit).</summary>
        public float SpentTotal;

        /// <summary>Share of performance (IPS) the flaw costs, 0..1; 0 when there is no loss.</summary>
        public float IpsLoss;

        /// <summary>
        /// The day the flaw became <see cref="FlawState.Public"/> for the first time; 0 if it never did. <c>rival-personalities</c> starts the
        /// caution of an AI company from it.
        /// </summary>
        public int PublicSinceDay;

        /// <summary>A one-line description for logs; not for the player.</summary>
        public override string ToString()
        {
            return "flaw #" + Id + " " + TypeId + "/" + Severity + " " + State + " of " + CompanyKey + " (cpu " + BaseCpuId + ")";
        }
    }
}
