using System.Collections.Generic;
using PTMod.Core;
using GameCpu = ProcessorTycoon.Hardware.Cpu;

namespace PTMod.Features.DesignFlawRecalls
{
    /// <summary>
    /// The internal half of <c>FlawRegistry</c> (design.md Component 4): the members the feature itself uses to create and look after the records. The
    /// public half (the API of the other features) is in <c>Api/FlawRegistry.cs</c>. The records live in the <see cref="FlawStore"/> of the feature
    /// (the saved data of the section); these members are the thin face of it with the game's <c>Cpu</c> in the signatures.
    /// </summary>
    public sealed partial class FlawRegistry
    {
        /// <summary>The store of the running feature, or <c>null</c> before the feature is registered.</summary>
        private static FlawStore Store
        {
            get
            {
                DesignFlawRecallsFeature feature = DesignFlawRecallsFeature.Instance;
                return feature == null ? null : feature.Store;
            }
        }

        /// <summary>
        /// Creates the record of a hidden flaw for a released base CPU: the next id, <see cref="FlawState.Hidden"/>, the release day and the inflation of
        /// today. <c>null</c> when a record already covers the CPU or the feature is not registered. The caller marks the CPU as rolled
        /// (<see cref="MarkRolled"/>).
        /// </summary>
        internal FlawRecord Create(string companyKey, GameCpu cpu, FlawTypeDef type, FlawSeverity severity, float risk)
        {
            FlawStore store = Store;
            if (store == null || cpu == null) return null;

            return store.Create(companyKey, cpu.SaveID, type, severity, risk, FlawDays.FromDate(ModTicks.Today), GameCpuView.Inflation());
        }

        /// <summary>Adds a variant (a CPU with <c>Cpu.Base</c>) to the CPUs the flaw affects. False for a record that is over.</summary>
        internal bool AttachVariant(FlawRecord record, GameCpu variant)
        {
            FlawStore store = Store;
            return store != null && variant != null && store.AttachVariant(record, variant.SaveID);
        }

        /// <summary>The CPU was rolled for a flaw already (FR-1 item 5).</summary>
        internal bool WasRolled(int cpuSaveId)
        {
            FlawStore store = Store;
            return store != null && store.WasRolled(cpuSaveId);
        }

        /// <summary>Remembers that the CPU was rolled.</summary>
        internal void MarkRolled(int cpuSaveId)
        {
            FlawStore store = Store;
            if (store != null) store.MarkRolled(cpuSaveId);
        }

        /// <summary>The CPU was made with the rush project (FR-20).</summary>
        internal bool IsRush(int cpuSaveId)
        {
            FlawStore store = Store;
            return store != null && store.IsRush(cpuSaveId);
        }

        /// <summary>The record follows its CPU to another company (FR-19, <c>FlawTransferHandler</c>).</summary>
        internal void MoveOwner(FlawRecord record, string newCompanyKey)
        {
            FlawStore store = Store;
            if (store != null) store.MoveOwner(record, newCompanyKey);
        }

        /// <summary>The records that are not over (not Resolved, Expired or Closed), in the order of the ids.</summary>
        internal IEnumerable<FlawRecord> Active
        {
            get
            {
                FlawStore store = Store;
                return store == null ? new FlawRecord[0] : (IEnumerable<FlawRecord>)store.Active();
            }
        }
    }
}
