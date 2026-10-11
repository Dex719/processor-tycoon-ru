using System;
using System.Collections.Generic;
using PTMod.Core;
using ICompany = ProcessorTycoon.CompanySystem.ICompany;

namespace PTMod.Features.DesignFlawRecalls
{
    /// <summary>
    /// The public API of <c>design-flaw-recalls</c> for the other features (FR-13, FR-17): reveal or force a flaw, read the known flaws,
    /// follow their states, bias the reaction of AI companies. Get the one instance from <see cref="DesignFlawRecallsFeature.Registry"/>
    /// (or <see cref="DesignFlawRecallsApi.Registry"/>); it is never <c>null</c> and it is the same object for the whole session, so an
    /// event subscription survives the feature being switched off and on.
    /// <para>
    /// This type is a facade. The records live in the store of the feature (an <see cref="IFlawRegistryBackend"/> that the feature attaches
    /// while it is enabled). When the feature is not enabled, or has not started yet, or has failed, every method answers like a null
    /// object: <c>false</c>, an empty list or <c>null</c>; nothing throws (AC-13.4, FR-13 item 4). Methods of the registry are for the main
    /// thread. A consumer that depends on <c>design-flaw-recalls</c> only softly checks <c>ModFeatures.IsActive("design-flaw-recalls")</c>
    /// before it wires itself, but may call the registry without that check: the answer is then the null-object one.
    /// </para>
    /// <para>
    /// For the implementation of the feature: the type is <c>partial</c> so that the internal members listed in design.md (Component 4:
    /// <c>Create</c>, <c>AttachVariant</c>, <c>WasRolled</c>, <c>MarkRolled</c>, <c>IsRush</c>, <c>MoveOwner</c>, <c>Active</c>) can be added
    /// in the feature's own file, but the store itself should be a separate class that implements <see cref="IFlawRegistryBackend"/> and is
    /// attached with <see cref="AttachBackend"/>. State changes are announced with <see cref="RaiseFlawStateChanged"/>.
    /// </para>
    /// </summary>
    public sealed partial class FlawRegistry
    {
        private static readonly IReadOnlyList<FlawRecordView> NoFlaws = new FlawRecordView[0];

        /// <summary>The one registry of the session (what <see cref="DesignFlawRecallsApi.Registry"/> returns).</summary>
        internal static readonly FlawRegistry Shared = new FlawRegistry();

        private IFlawRegistryBackend _backend;
        private IFlawResponseBias _bias;

        private FlawRegistry()
        {
        }

        // ---- public API (design.md, Component 4) ----

        /// <summary>
        /// FR-13 item 1. Makes the hidden flaw of the CPU known by <paramref name="source"/> (a press review that found it:
        /// <see cref="DiscoverySource.External"/>) and returns <c>true</c>. Returns <c>false</c>, with no side effect, for a CPU without a
        /// hidden flaw, an unknown CPU, a flaw that is already known, and whenever the feature is not enabled.
        /// </summary>
        /// <param name="cpuSaveId"><c>Cpu.SaveID</c> of the CPU (the base CPU or one of its variants).</param>
        /// <param name="source">How the flaw came to light, as the caller sees it.</param>
        public bool TryReveal(int cpuSaveId, DiscoverySource source)
        {
            try
            {
                IFlawRegistryBackend backend = ActiveBackend();
                return backend != null && backend.TryReveal(cpuSaveId, source);
            }
            catch (Exception e)
            {
                Fault("FlawRegistry.TryReveal", e);
                return false;
            }
        }

        /// <summary>
        /// FR-13 item 2. Creates a flaw record for the CPU (or raises the severity of the existing one) and starts the discovery at once
        /// by <paramref name="discovery"/>; when the owner is the player, the decision window opens (AC-13.1). Returns <c>true</c> when the
        /// record exists afterwards and the discovery has started; <c>false</c> for an unknown CPU, an unknown <paramref name="typeId"/>,
        /// a CPU that cannot get a flaw, and whenever the feature is not enabled (the caller then uses its own fallback).
        /// </summary>
        /// <param name="cpuSaveId"><c>Cpu.SaveID</c> of the CPU.</param>
        /// <param name="typeId">Id of the flaw type, see <see cref="FlawTypeIds"/>; <c>null</c> or empty lets the feature choose by its usual rules.</param>
        /// <param name="severity">How bad the flaw is.</param>
        /// <param name="discovery">How it comes to light (<see cref="DiscoverySource.External"/> for a public one, <see cref="DiscoverySource.Api"/> if the caller has no better word).</param>
        public bool ForceFlaw(int cpuSaveId, string typeId, FlawSeverity severity, DiscoverySource discovery)
        {
            try
            {
                IFlawRegistryBackend backend = ActiveBackend();
                return backend != null && backend.ForceFlaw(cpuSaveId, typeId, severity, discovery);
            }
            catch (Exception e)
            {
                Fault("FlawRegistry.ForceFlaw", e);
                return false;
            }
        }

        /// <summary>
        /// FR-13 item 3. The records of one company that are not <see cref="FlawState.Hidden"/> (known records of any other state, final
        /// ones included). Never <c>null</c>: empty for a <c>null</c> company, a company without records, and when the feature is not enabled.
        /// </summary>
        public IReadOnlyList<FlawRecordView> GetKnownFlaws(ICompany company)
        {
            try
            {
                if (ReferenceEquals(company, null)) return NoFlaws;

                IFlawRegistryBackend backend = ActiveBackend();
                if (backend == null) return NoFlaws;
                return backend.GetKnownFlaws(company) ?? NoFlaws;
            }
            catch (Exception e)
            {
                Fault("FlawRegistry.GetKnownFlaws", e);
                return NoFlaws;
            }
        }

        /// <summary>The records of every company that are not <see cref="FlawState.Hidden"/>. Never <c>null</c>; empty when the feature is not enabled.</summary>
        public IReadOnlyList<FlawRecordView> GetAllKnownFlaws()
        {
            try
            {
                IFlawRegistryBackend backend = ActiveBackend();
                if (backend == null) return NoFlaws;
                return backend.GetAllKnownFlaws() ?? NoFlaws;
            }
            catch (Exception e)
            {
                Fault("FlawRegistry.GetAllKnownFlaws", e);
                return NoFlaws;
            }
        }

        /// <summary>
        /// The record that covers the CPU (the base CPU or one of its variants), or <c>null</c> if there is none, if the flaw is still
        /// <see cref="FlawState.Hidden"/> (a hidden flaw is never revealed by a lookup), or when the feature is not enabled.
        /// </summary>
        public FlawRecordView FindByCpu(int cpuSaveId)
        {
            try
            {
                IFlawRegistryBackend backend = ActiveBackend();
                return backend == null ? null : backend.FindByCpu(cpuSaveId);
            }
            catch (Exception e)
            {
                Fault("FlawRegistry.FindByCpu", e);
                return null;
            }
        }

        /// <summary>
        /// A flaw changed its state (found, publicised, answered, resolved, leaked, transferred...). The argument is a fresh snapshot of
        /// the record; its <see cref="FlawRecordView.PublicSinceDay"/> is filled when the record is <see cref="FlawState.Public"/> (FR-17
        /// item 2). It is not raised for the creation of a <see cref="FlawState.Hidden"/> record, nor while a save loads, nor during the
        /// new game's fast-forward. Subscribers: <c>press-reviews</c> (the article about a public flaw), <c>rival-personalities</c> (the
        /// caution of an AI company), the journal of the feature. A handler is called through <c>ModHooks.Raise</c>: handlers of features that
        /// are not enabled are skipped, an exception in a handler is counted against its own feature and the next handler still runs.
        /// Subscribe once per scene (or when the feature is enabled) and unsubscribe when your feature is disabled.
        /// </summary>
        public event Action<FlawRecordView> OnFlawStateChanged;

        /// <summary>
        /// FR-17 item 1. Sets the one slot of the optional bias of the AI reaction to a flaw; <c>null</c> clears it. A second call replaces
        /// the first (a warning goes to the log if a different non-null bias is replaced). The slot is kept even while
        /// <c>design-flaw-recalls</c> is switched off, so a consumer may call this at any time; the bias applies only while the feature
        /// is enabled and its owner is enabled too. Call it from <c>Register</c> or on <c>ModTicks.GameReady</c> and clear it when your
        /// feature is disabled.
        /// </summary>
        /// <param name="bias">The bias, or <c>null</c> to remove it.</param>
        public void SetResponseBias(IFlawResponseBias bias)
        {
            IFlawResponseBias previous = _bias;
            _bias = bias;

            if (previous != null && bias != null && !ReferenceEquals(previous, bias))
            {
                ModLog.Warn(DesignFlawRecallsApi.FeatureId, "response bias of " + previous.GetType().FullName + " is replaced by " + bias.GetType().FullName + " (one slot)");
            }
            else
            {
                ModLog.Debug(DesignFlawRecallsApi.FeatureId, bias == null ? "response bias cleared" : "response bias set by " + bias.GetType().FullName);
            }
        }

        // ---- for the implementation of the feature (internal) ----

        /// <summary>
        /// Attaches the store of the feature (its <c>Register</c> or <c>OnEnabled</c>); <c>null</c> detaches. From then on, while the feature
        /// is enabled, the public methods delegate to <paramref name="backend"/>.
        /// </summary>
        internal void AttachBackend(IFlawRegistryBackend backend)
        {
            _backend = backend;
        }

        /// <summary>Detaches the store (<c>OnDisabled</c>): the public methods answer like a null object again. Does nothing if another store is attached.</summary>
        internal void DetachBackend(IFlawRegistryBackend backend)
        {
            if (ReferenceEquals(_backend, backend)) _backend = null;
        }

        /// <summary>A store is attached and the feature is enabled: the public methods do real work.</summary>
        internal bool IsBackendActive
        {
            get { return ActiveBackend() != null; }
        }

        /// <summary>
        /// Announces that a record changed its state (raises <see cref="OnFlawStateChanged"/>). Called by the feature after the change is
        /// done and the record is in its new state; <paramref name="view"/> is a fresh snapshot. A <c>null</c> view is ignored. Never throws.
        /// </summary>
        internal void RaiseFlawStateChanged(FlawRecordView view)
        {
            if (view == null) return;

            try
            {
                ModHooks.Raise("FlawRegistry.OnFlawStateChanged", OnFlawStateChanged, view);
            }
            catch (Exception e)
            {
                Fault("FlawRegistry.RaiseFlawStateChanged", e);
            }
        }

        /// <summary>A bias is registered (it may still be refused by <see cref="GetResponseBiasMultipliers"/>).</summary>
        internal bool HasResponseBias
        {
            get { return _bias != null; }
        }

        /// <summary>
        /// The answer of the registered bias for an AI company, ready for <c>AiDecisionInput.BiasMultipliers</c>: <c>[Recall, Stepping, Patch,
        /// Ignore]</c>, or <c>null</c> for "all 1.0". The call runs inside <c>ModHooks.Guard</c> on behalf of the feature that owns the type of
        /// the bias, so an exception there counts against that feature (FR-17 item 1) and gives <c>null</c>. <c>null</c> too: no bias, the owner of
        /// the bias is not enabled, a wrong length, a negative, NaN or infinite multiplier. Never throws.
        /// </summary>
        internal float[] GetResponseBiasMultipliers(ICompany company, bool isPublic)
        {
            IFlawResponseBias bias = _bias;
            if (bias == null) return null;

            try
            {
                string owner = ModFeatures.FeatureOfType(bias.GetType());
                if (string.IsNullOrEmpty(owner)) owner = ModHooks.CoreId;
                if (owner != ModHooks.CoreId && !ModFeatures.IsActive(owner)) return null;

                float[] raw = ModHooks.Guard<float[]>(owner, "FlawResponseBias.Multipliers", () => bias.Multipliers(company, isPublic), null);
                return SanitizeMultipliers(raw);
            }
            catch (Exception e)
            {
                Fault("FlawRegistry.GetResponseBiasMultipliers", e);
                return null;
            }
        }

        // ---- internals ----

        private IFlawRegistryBackend ActiveBackend()
        {
            IFlawRegistryBackend backend = _backend;
            if (backend == null) return null;
            return ModFeatures.IsActive(DesignFlawRecallsApi.FeatureId) ? backend : null;
        }

        private static float[] SanitizeMultipliers(float[] raw)
        {
            if (raw == null || raw.Length != FlawResponseBiasOrder.Length) return null;

            for (int i = 0; i < raw.Length; i++)
            {
                float value = raw[i];
                if (float.IsNaN(value) || float.IsInfinity(value) || value < 0f) return null;
            }
            return raw;
        }

        private static void Fault(string site, Exception e)
        {
            ModHooks.ReportFault(DesignFlawRecallsApi.FeatureId, site, e);
        }
    }
}
