using System.Collections.Generic;
using ICompany = ProcessorTycoon.CompanySystem.ICompany;

namespace PTMod.Features.DesignFlawRecalls
{
    /// <summary>
    /// What the implementation of <c>design-flaw-recalls</c> provides behind the public <see cref="FlawRegistry"/>. The feature attaches
    /// its store when it is registered or enabled and detaches it when it is disabled (<c>FlawRegistry.AttachBackend</c> /
    /// <c>DetachBackend</c>, internal); until then, and while the feature is not enabled, the registry answers like a null object
    /// (<c>false</c>, an empty list, <c>null</c>). Other features never implement or call this interface, they use <see cref="FlawRegistry"/>.
    /// <para>
    /// The registry wraps every call in try/catch, so a backend may throw: the exception is counted against <c>design-flaw-recalls</c>
    /// and the caller gets the null-object answer. A backend must still not throw for ordinary input (an unknown CPU is <c>false</c> or
    /// <c>null</c>, not an exception).
    /// </para>
    /// </summary>
    public interface IFlawRegistryBackend
    {
        /// <summary>
        /// FR-13 item 1. Finds the hidden flaw of the CPU (if it has one), makes it known by <paramref name="source"/> and returns
        /// <c>true</c>. A CPU without a flaw, a flaw that is not <see cref="FlawState.Hidden"/> and an unknown CPU give <c>false</c>
        /// without any side effect.
        /// </summary>
        bool TryReveal(int cpuSaveId, DiscoverySource source);

        /// <summary>
        /// FR-13 item 2. Creates the record of a flaw for the CPU (or raises the severity of an existing one) and starts the discovery
        /// at once by <paramref name="discovery"/>. Returns <c>true</c> when a record exists afterwards and the discovery was started;
        /// <c>false</c> for an unknown CPU, an unknown <paramref name="typeId"/>, or a CPU that cannot get a flaw (not released, already
        /// resolved).
        /// </summary>
        bool ForceFlaw(int cpuSaveId, string typeId, FlawSeverity severity, DiscoverySource discovery);

        /// <summary>Snapshots of the non-<see cref="FlawState.Hidden"/> records of one company (any other state, final ones included). Never null.</summary>
        IReadOnlyList<FlawRecordView> GetKnownFlaws(ICompany company);

        /// <summary>Snapshots of the non-<see cref="FlawState.Hidden"/> records of every company. Never null.</summary>
        IReadOnlyList<FlawRecordView> GetAllKnownFlaws();

        /// <summary>The snapshot of the record that covers the CPU (the base CPU or one of its variants), or <c>null</c> if there is none or it is hidden.</summary>
        FlawRecordView FindByCpu(int cpuSaveId);
    }
}
