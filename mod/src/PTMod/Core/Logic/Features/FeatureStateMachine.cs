using System;
using System.Collections.Generic;

namespace PTMod.Core
{
    /// <summary>
    /// State of a feature as the hub and the services see it. <see cref="Enabled"/> is the only state in
    /// which a feature runs; <see cref="Faulted"/> lasts until the game is restarted; <see cref="Unavailable"/>
    /// means the feature cannot run (the reason is in <c>ModFeatures.ReasonOf</c>).
    /// </summary>
    public enum FeatureState
    {
        Disabled,
        Enabled,
        Faulted,
        Unavailable
    }
}

namespace PTMod.Core.Logic.Features
{
    /// <summary>Why a feature is <see cref="FeatureState.Unavailable"/> or <see cref="FeatureState.Faulted"/>.</summary>
    public enum FeatureReasonKind
    {
        /// <summary>No problem: Enabled or Disabled by the player.</summary>
        None,

        /// <summary>The id is not a registered feature.</summary>
        NotRegistered,

        /// <summary>A declared dependency is not a registered feature.</summary>
        MissingDependency,

        /// <summary>A dependency is Disabled, Faulted or Unavailable (may clear when the dependency comes back).</summary>
        DependencyInactive,

        /// <summary>The feature is part of a dependency cycle.</summary>
        Cycle,

        /// <summary>Marked unavailable at runtime (patches could not be applied); lasts until restart.</summary>
        Unavailable,

        /// <summary>The circuit breaker tripped; lasts until restart.</summary>
        Faulted
    }

    /// <summary>
    /// Immutable snapshot of one feature. <see cref="ReasonKey"/> and <see cref="ReasonArg"/> go to ModL10n
    /// (<c>ModL10n.L(status.ReasonKey, status.ReasonArg)</c>); <see cref="Reason"/> is English text for the log.
    /// </summary>
    public sealed class FeatureStatus
    {
        internal FeatureStatus(string id, FeatureState state, FeatureReasonKind reasonKind, IReadOnlyList<string> reasonFeatures,
            string detail, string reasonText, bool wantsEnabled)
        {
            Id = id;
            State = state;
            ReasonKind = reasonKind;
            ReasonFeatures = reasonFeatures ?? new string[0];
            Detail = detail;
            Reason = reasonText;
            WantsEnabled = wantsEnabled;
        }

        public string Id { get; }

        public FeatureState State { get; }

        /// <summary>True only for <see cref="FeatureState.Enabled"/>.</summary>
        public bool IsActive
        {
            get { return State == FeatureState.Enabled; }
        }

        public FeatureReasonKind ReasonKind { get; }

        /// <summary>The features the reason is about: inactive or missing dependencies, or the members of the cycle; otherwise empty.</summary>
        public IReadOnlyList<string> ReasonFeatures { get; }

        /// <summary>Free text given to <c>MarkUnavailable</c> or <c>Fault</c> (for example "no method X (another game version?)"), or null.</summary>
        public string Detail { get; }

        /// <summary>What the player asked for (the hub toggle); can differ from <see cref="State"/>, for example for a dependent of a disabled feature.</summary>
        public bool WantsEnabled { get; }

        /// <summary>English reason for logs, or null when there is no problem.</summary>
        public string Reason { get; }

        /// <summary>ModL10n key of the reason (<c>core.feature.reason.*</c>), or null when there is no problem.</summary>
        public string ReasonKey
        {
            get
            {
                switch (ReasonKind)
                {
                    case FeatureReasonKind.NotRegistered: return "core.feature.reason.notRegistered";
                    case FeatureReasonKind.MissingDependency: return "core.feature.reason.missing";
                    case FeatureReasonKind.DependencyInactive: return "core.feature.reason.needs";
                    case FeatureReasonKind.Cycle: return "core.feature.reason.cycle";
                    case FeatureReasonKind.Unavailable: return "core.feature.reason.unavailable";
                    case FeatureReasonKind.Faulted: return "core.feature.reason.faulted";
                    default: return null;
                }
            }
        }

        /// <summary>
        /// Argument {0} of <see cref="ReasonKey"/>: the feature ids of <see cref="ReasonFeatures"/> joined with ", "
        /// (the feature's own id for NotRegistered) or <see cref="Detail"/> for Unavailable and Faulted (empty when
        /// there is none). Null when there is no problem.
        /// </summary>
        public string ReasonArg
        {
            get
            {
                switch (ReasonKind)
                {
                    case FeatureReasonKind.None:
                        return null;
                    case FeatureReasonKind.Unavailable:
                    case FeatureReasonKind.Faulted:
                        return Detail ?? string.Empty;
                    case FeatureReasonKind.NotRegistered:
                        return Id;
                    default:
                        return string.Join(", ", ReasonFeatures);
                }
            }
        }
    }

    /// <summary>
    /// A change of the effective state of one feature caused by a call of <see cref="FeatureStateMachine"/>.
    /// The caller reacts: <see cref="Deactivated"/> means remove patches and call <c>OnDisabled</c>,
    /// <see cref="Activated"/> means apply patches and call <c>OnEnabled</c>.
    /// </summary>
    public sealed class FeatureTransition
    {
        internal FeatureTransition(FeatureStatus before, FeatureStatus after)
        {
            Before = before;
            After = after;
        }

        public string Id
        {
            get { return After.Id; }
        }

        public FeatureStatus Before { get; }

        public FeatureStatus After { get; }

        public FeatureState From
        {
            get { return Before.State; }
        }

        public FeatureState To
        {
            get { return After.State; }
        }

        /// <summary>The feature was running and no longer is.</summary>
        public bool Deactivated
        {
            get { return Before.IsActive && !After.IsActive; }
        }

        /// <summary>The feature was not running and now is.</summary>
        public bool Activated
        {
            get { return !Before.IsActive && After.IsActive; }
        }
    }

    /// <summary>
    /// State model of the feature registry (FR-3). Per feature it keeps what the player asked for
    /// (enabled or not) and the hard facts (faulted, unavailable); the effective <see cref="FeatureState"/>
    /// is derived through the <see cref="FeatureGraph"/>, first match wins:
    /// <list type="number">
    /// <item><description>in a dependency cycle, or depends on an unknown feature: Unavailable (decided once);</description></item>
    /// <item><description>marked unavailable at runtime (<see cref="MarkUnavailable"/>): Unavailable, until restart;</description></item>
    /// <item><description>faulted (<see cref="Fault"/>): Faulted, until restart;</description></item>
    /// <item><description>a dependency is not Enabled (Disabled, Faulted or Unavailable): Unavailable, naming the dependency;
    /// it clears by itself when the dependency is Enabled again, and the feature returns to what the player asked for;</description></item>
    /// <item><description>otherwise Disabled or Enabled as the player asked.</description></item>
    /// </list>
    /// The framework id (<see cref="FeatureGraph.CoreFeatureId"/>) is always Enabled and cannot be changed.
    /// Every mutator returns the effective state changes it caused, deactivations first and dependents before
    /// their dependencies, then the others in dependency order; reason-only changes are not transitions
    /// (re-read <see cref="StatusOf"/>). Not thread-safe: use from the main thread.
    /// </summary>
    public sealed class FeatureStateMachine
    {
        private static readonly IReadOnlyList<FeatureTransition> NoTransitions = new FeatureTransition[0];

        /// <summary>Status of the framework id; shared so that the frequent checks for core handlers do not allocate.</summary>
        private static readonly FeatureStatus CoreStatus =
            new FeatureStatus(FeatureGraph.CoreFeatureId, FeatureState.Enabled, FeatureReasonKind.None, null, null, null, true);

        private readonly FeatureGraph graph;
        private readonly Dictionary<string, bool> wants = new Dictionary<string, bool>(StringComparer.Ordinal);
        private readonly Dictionary<string, string> unavailableDetail = new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly Dictionary<string, string> faultDetail = new Dictionary<string, string>(StringComparer.Ordinal);
        private Dictionary<string, FeatureStatus> statuses;

        /// <param name="initiallyEnabled">
        /// What the player asked for at start (settings over <c>EnabledByDefault</c>); null enables everything.
        /// Called once per feature, in dependency order.
        /// </param>
        public FeatureStateMachine(FeatureGraph graph, Func<string, bool> initiallyEnabled = null)
        {
            if (graph == null) throw new ArgumentNullException(nameof(graph));
            this.graph = graph;

            foreach (string id in graph.Order) wants[id] = initiallyEnabled == null || initiallyEnabled(id);
            statuses = Compute();
        }

        public FeatureGraph Graph
        {
            get { return graph; }
        }

        /// <summary>Statuses of all registered features in dependency order.</summary>
        public IReadOnlyList<FeatureStatus> All
        {
            get
            {
                var list = new List<FeatureStatus>(graph.Order.Count);
                foreach (string id in graph.Order) list.Add(statuses[id]);
                return list;
            }
        }

        /// <summary>Status of a feature; the framework id is Enabled, an unknown id is Unavailable (NotRegistered).</summary>
        public FeatureStatus StatusOf(string id)
        {
            FeatureStatus status;
            if (id != null && statuses.TryGetValue(id, out status)) return status;
            if (id == FeatureGraph.CoreFeatureId) return CoreStatus;
            return new FeatureStatus(id ?? string.Empty, FeatureState.Unavailable, FeatureReasonKind.NotRegistered, null, null,
                "feature is not registered", false);
        }

        /// <summary>State of a feature without allocating (called on hot paths such as event dispatch).</summary>
        public FeatureState StateOf(string id)
        {
            FeatureStatus status;
            if (id != null && statuses.TryGetValue(id, out status)) return status.State;
            return id == FeatureGraph.CoreFeatureId ? FeatureState.Enabled : FeatureState.Unavailable;
        }

        /// <summary>True when the feature is Enabled (not Disabled, Faulted or Unavailable).</summary>
        public bool IsActive(string id)
        {
            return StateOf(id) == FeatureState.Enabled;
        }

        /// <summary>English reason for Unavailable and Faulted, otherwise null.</summary>
        public string ReasonOf(string id)
        {
            return StatusOf(id).Reason;
        }

        /// <summary>
        /// The player turns the feature on or off. Remembered even when the feature cannot run now (a faulted or
        /// unavailable feature stays so; a dependent of a disabled feature starts when the dependency returns).
        /// Dependents of the feature follow it.
        /// </summary>
        public IReadOnlyList<FeatureTransition> SetEnabled(string id, bool enabled)
        {
            if (id == null || !wants.ContainsKey(id)) return NoTransitions;
            wants[id] = enabled;
            return Recompute();
        }

        /// <summary>
        /// Circuit breaker tripped: the feature is Faulted until the game restarts (no way back, <see cref="SetEnabled"/>
        /// does not undo it). Ignored for the framework, unknown ids and features that are already faulted or
        /// permanently unavailable (cycle, unknown dependency, <see cref="MarkUnavailable"/>). The first <paramref name="detail"/> is kept.
        /// </summary>
        public IReadOnlyList<FeatureTransition> Fault(string id, string detail)
        {
            if (id == null || !wants.ContainsKey(id) || IsTerminal(id)) return NoTransitions;
            faultDetail[id] = detail ?? string.Empty;
            return Recompute();
        }

        /// <summary>
        /// The feature cannot run for good (a required patch target is missing, a patch is rejected): Unavailable
        /// until the game restarts. Ignored for the framework, unknown ids and features that are already beyond
        /// recovery (faulted, marked, in a cycle, missing a dependency).
        /// </summary>
        public IReadOnlyList<FeatureTransition> MarkUnavailable(string id, string detail)
        {
            if (id == null || !wants.ContainsKey(id) || IsTerminal(id)) return NoTransitions;
            unavailableDetail[id] = detail ?? string.Empty;
            return Recompute();
        }

        /// <summary>The feature is already beyond recovery: in a cycle, missing a dependency, faulted or marked unavailable.</summary>
        private bool IsTerminal(string id)
        {
            return graph.IsInCycle(id) || graph.MissingDependenciesOf(id).Count > 0
                   || faultDetail.ContainsKey(id) || unavailableDetail.ContainsKey(id);
        }

        private IReadOnlyList<FeatureTransition> Recompute()
        {
            Dictionary<string, FeatureStatus> fresh = Compute();

            var deactivations = new List<FeatureTransition>();
            var others = new List<FeatureTransition>();
            foreach (string id in graph.Order)
            {
                FeatureStatus before = statuses[id];
                FeatureStatus after = fresh[id];
                if (before.State == after.State) continue;

                var transition = new FeatureTransition(before, after);
                if (transition.Deactivated) deactivations.Add(transition);
                else others.Add(transition);
            }
            statuses = fresh;

            if (deactivations.Count == 0 && others.Count == 0) return NoTransitions;

            deactivations.Reverse(); // dependents go down before the features they depend on
            deactivations.AddRange(others);
            return deactivations;
        }

        private Dictionary<string, FeatureStatus> Compute()
        {
            var result = new Dictionary<string, FeatureStatus>(StringComparer.Ordinal);
            foreach (string id in graph.Order)
            {
                // Dependencies precede their dependents in the order, except inside a cycle, and cycle members never read it.
                result[id] = Resolve(id, result);
            }
            return result;
        }

        private FeatureStatus Resolve(string id, Dictionary<string, FeatureStatus> resolved)
        {
            bool asked = wants[id];

            IReadOnlyList<string> cycle = graph.CycleOf(id);
            if (cycle.Count > 0)
                return Make(id, FeatureState.Unavailable, FeatureReasonKind.Cycle, cycle, null, "dependency cycle: " + string.Join(", ", cycle), asked);

            IReadOnlyList<string> absent = graph.MissingDependenciesOf(id);
            if (absent.Count > 0)
                return Make(id, FeatureState.Unavailable, FeatureReasonKind.MissingDependency, absent, null,
                    "requires " + Describe(absent, "feature", "features") + " that " + (absent.Count == 1 ? "is" : "are") + " not found", asked);

            string detail;
            if (unavailableDetail.TryGetValue(id, out detail))
                return Make(id, FeatureState.Unavailable, FeatureReasonKind.Unavailable, null, detail,
                    detail.Length == 0 ? "unavailable" : "unavailable: " + detail, asked);

            if (faultDetail.TryGetValue(id, out detail))
                return Make(id, FeatureState.Faulted, FeatureReasonKind.Faulted, null, detail,
                    detail.Length == 0 ? "faulted" : "faulted: " + detail, asked);

            List<string> inactive = null;
            var states = new List<string>();
            foreach (string dependency in graph.DependenciesOf(id))
            {
                if (dependency == FeatureGraph.CoreFeatureId) continue;
                FeatureState state = resolved[dependency].State;
                if (state == FeatureState.Enabled) continue;
                if (inactive == null) inactive = new List<string>();
                inactive.Add(dependency);
                states.Add(dependency + " (" + state.ToString().ToLowerInvariant() + ")");
            }
            if (inactive != null)
                return Make(id, FeatureState.Unavailable, FeatureReasonKind.DependencyInactive, inactive, null,
                    "requires " + Describe(states, "feature", "features"), asked);

            return Make(id, asked ? FeatureState.Enabled : FeatureState.Disabled, FeatureReasonKind.None, null, null, null, asked);
        }

        private static FeatureStatus Make(string id, FeatureState state, FeatureReasonKind kind, IReadOnlyList<string> features,
            string detail, string reasonText, bool asked)
        {
            return new FeatureStatus(id, state, kind, features, detail, reasonText, asked);
        }

        private static string Describe(IReadOnlyList<string> items, string one, string many)
        {
            return (items.Count == 1 ? one : many) + " " + string.Join(", ", items);
        }
    }
}
