using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Reflection;
using System.Text;
using PTMod.Core.Logic.Features;

namespace PTMod.Core
{
    /// <summary>
    /// The feature registry (FR-3): the features found by <c>[ModFeature]</c>, their dependency order and their state. A thin runtime layer
    /// over <see cref="FeatureGraph"/> and <see cref="FeatureStateMachine"/>, which decide the order and the states; this class calls the
    /// features (<c>Register</c>, <c>OnEnabled</c>, <c>OnDisabled</c>), tells the core services (<see cref="FeatureActivating"/>,
    /// <see cref="FeatureDeactivated"/>: patches go on and off there) and raises <see cref="StateChanged"/>.
    /// <list type="bullet">
    /// <item><b>Start.</b> <c>ModBootstrap.Init</c> builds the registry after the Declare stage: the player's choice from <c>ModSettings</c>
    /// (the feature's <c>EnabledByDefault</c> until the player chooses), <c>Register</c> for each active feature in dependency order, and,
    /// once <see cref="PTModHost"/> exists, <c>OnEnabled</c> for each.</item>
    /// <item><b>Switching</b> (<see cref="SetEnabled"/>, a dependency that goes away, <see cref="Fault"/>, <see cref="MarkUnavailable"/>):
    /// every feature that stops running gets <c>OnDisabled</c> (dependents first) and then <see cref="FeatureDeactivated"/>; every feature
    /// that starts gets <c>Register</c> (first time only), <see cref="FeatureActivating"/> and then <c>OnEnabled</c>, dependencies first.
    /// A switch made from inside one of these calls is queued and settled before <see cref="StateChanged"/> goes out.</item>
    /// <item><b>Isolation.</b> Exceptions of the features and of the handlers never leave this class: <c>Register</c> failing makes the
    /// feature Unavailable, <c>OnEnabled</c> failing makes it Faulted, a failing handler is reported to its owner (<see cref="ReportFault"/>).
    /// Handlers of <see cref="StateChanged"/> whose owner feature is not active are skipped (FR-2 item 8).</item>
    /// <item><b>Failed start.</b> When <c>Init</c> fails every feature is switched off and reported Unavailable, the framework id included,
    /// so nothing of the mod runs (FR-1 item 4).</item>
    /// <item><b>Confirmation.</b> A feature may ask the hub to question the player before it is switched off (<c>FeatureContext.ConfirmBeforeDisable</c>,
    /// <see cref="GetDisableConfirmation"/>). Only the hub asks; <see cref="SetEnabled"/> itself never does, and neither does a dependency that goes
    /// away or the circuit breaker (FR-3 item 8, AC-3.3).</item>
    /// </list>
    /// The class is partial: a core service may add its own part of the registry next to its code. Main thread only.
    /// </summary>
    public static partial class ModFeatures
    {
        private const string CoreId = FeatureGraph.CoreFeatureId;

        private static readonly List<FeatureInfo> Ordered = new List<FeatureInfo>();
        private static readonly Dictionary<string, FeatureInfo> ById = new Dictionary<string, FeatureInfo>(StringComparer.Ordinal);
        private static readonly Dictionary<string, FeatureState> Reported = new Dictionary<string, FeatureState>(StringComparer.Ordinal);
        private static readonly Dictionary<string, bool> Pending = new Dictionary<string, bool>(StringComparer.Ordinal);
        private static readonly Dictionary<Type, string> OwnerCache = new Dictionary<Type, string>();
        private static readonly object OwnerSync = new object();
        private static readonly Dictionary<string, Func<DisableConfirmation>> DisableProviders =
            new Dictionary<string, Func<DisableConfirmation>>(StringComparer.Ordinal);

        private static IReadOnlyList<FeatureInfo> _all = new ReadOnlyCollection<FeatureInfo>(new FeatureInfo[0]);
        private static FeatureGraph _graph = new FeatureGraph(new FeatureNode[0]);
        private static FeatureStateMachine _machine = new FeatureStateMachine(_graph);

        private static bool _built;
        private static bool _activationOpen;   // false until the host exists: Register may run before that, OnEnabled may not
        private static bool _halted;
        private static string _haltReason;
        private static bool _reconciling;
        private static bool _dirty;
        private static bool _raising;

        /// <summary>Every discovered feature in dependency order (dependencies first); empty before the mod has started.</summary>
        public static IReadOnlyList<FeatureInfo> All
        {
            get { return _all; }
        }

        /// <summary>
        /// The state of a feature changed: (feature id, new state). Raised after the feature was switched (after <c>OnEnabled</c> or
        /// <c>OnDisabled</c>), features that stopped running first. A handler of a feature that is not active is not called; a throwing handler
        /// is counted against its owner and the others still run.
        /// </summary>
        public static event Action<string, FeatureState> StateChanged;

        // ---- core services ----

        /// <summary>The feature's <c>Register</c> returned (once per session). The patches it declared can be checked here.</summary>
        internal static event Action<FeatureInfo> FeatureRegistered;

        /// <summary>
        /// The feature is about to start: apply its patches (all or nothing). A listener refuses by calling <see cref="MarkUnavailable"/>
        /// (or by throwing, which counts as the same); then <see cref="FeatureDeactivated"/> follows at once so every listener can undo its
        /// part, and <c>OnEnabled</c> is not called.
        /// </summary>
        internal static event Action<FeatureInfo> FeatureActivating;

        /// <summary>The feature stopped running and its <c>OnDisabled</c> has returned: remove its patches.</summary>
        internal static event Action<FeatureInfo> FeatureDeactivated;

        /// <summary>
        /// Where the failures of feature code caught by the registry and the host go: (feature id, site, exception). <c>ModHooks</c> sets it
        /// to its fault counter (the circuit breaker, FR-2 item 4); until then a failure is one <c>ModLog</c> error.
        /// </summary>
        internal static Action<string, string, Exception> FaultSink { get; set; }

        // ---- queries ----

        /// <summary>The registry entry of a feature, or null.</summary>
        public static FeatureInfo Find(string featureId)
        {
            FeatureInfo info;
            return featureId != null && ById.TryGetValue(featureId, out info) ? info : null;
        }

        /// <summary>
        /// The state of a feature. The framework id (<c>mod-framework</c>) is Enabled; an unknown id is Unavailable; after a failed start
        /// everything is Unavailable. Does not allocate (used on hot paths).
        /// </summary>
        public static FeatureState StateOf(string featureId)
        {
            return _halted ? FeatureState.Unavailable : _machine.StateOf(featureId);
        }

        /// <summary>The feature is Enabled: its patches, handlers, ticks and providers run (not Disabled, Faulted or Unavailable).</summary>
        public static bool IsActive(string featureId)
        {
            return StateOf(featureId) == FeatureState.Enabled;
        }

        /// <summary>The structured state: <see cref="FeatureStatus.ReasonKey"/> and <see cref="FeatureStatus.ReasonArg"/> for the hub, English <see cref="FeatureStatus.Reason"/> for logs.</summary>
        public static FeatureStatus StatusOf(string featureId)
        {
            FeatureStatus status = _machine.StatusOf(featureId);
            if (!_halted) return status;

            string reason = _haltReason ?? "the mod failed to start";
            return new FeatureStatus(status.Id, FeatureState.Unavailable, FeatureReasonKind.Unavailable, null, reason, "unavailable: " + reason,
                status.WantsEnabled);
        }

        /// <summary>
        /// Why the feature is Unavailable or Faulted, as text for the player (<c>core.feature.reason.*</c> in the current language, AC-3.1:
        /// "нужна фича event-system"); the English reason when the key is missing; null when the feature has no problem.
        /// </summary>
        public static string ReasonOf(string featureId)
        {
            FeatureStatus status = StatusOf(featureId);
            if (status.Reason == null) return null;

            string key = status.ReasonKey;
            if (key != null && ModL10n.Has(key)) return ModL10n.L(key, status.ReasonArg);
            return status.Reason;
        }

        /// <summary>The player's choice for the feature (the position of the hub toggle), including a choice waiting for the scene to change.</summary>
        public static bool WantsEnabled(string featureId)
        {
            bool pending;
            if (featureId != null && Pending.TryGetValue(featureId, out pending)) return pending;
            return _machine.StatusOf(featureId).WantsEnabled;
        }

        /// <summary>A switch of a feature without hot toggle waits for the scene to change ("takes effect after loading a save", FR-3 item 5).</summary>
        public static bool HasPendingChange(string featureId)
        {
            return featureId != null && Pending.ContainsKey(featureId);
        }

        /// <summary>
        /// The feature that owns code of this type, by namespace: <c>PTMod.Features.&lt;Name&gt;[.…]</c> gives the slug of &lt;Name&gt;
        /// (<c>EventSystem</c> - <c>event-system</c>), the rest of <c>PTMod</c> gives <c>mod-framework</c>, a type outside <c>PTMod</c> (or null) gives null.
        /// Nested and compiler-generated types (lambdas, iterators) belong to the namespace of their outer type. Cached per type.
        /// </summary>
        public static string FeatureOfType(Type type)
        {
            if (type == null) return null;

            lock (OwnerSync)
            {
                string owner;
                if (OwnerCache.TryGetValue(type, out owner)) return owner;

                owner = FeatureGraph.FeatureOfNamespace(type.Namespace);
                OwnerCache[type] = owner;
                return owner;
            }
        }

        // ---- switching ----

        /// <summary>
        /// The player switches a feature (the hub; no confirmation here - the hub asks first, see <see cref="GetDisableConfirmation"/>; a call from code
        /// switches off without a question, AC-3.3). The choice is saved in <c>ptmod_settings.json</c>
        /// at once. A feature with <see cref="IModFeature.SupportsHotToggle"/>, or any feature outside the game scene, switches now: its
        /// patches go on or off, its handlers start or stop (FR-3 item 4); its records of <c>ModModifiers</c> are kept. A feature without hot
        /// toggle switched in the game scene keeps running as it was until the scene changes (<see cref="HasPendingChange"/>, FR-3 item 5).
        /// A Faulted or Unavailable feature keeps its state; the choice applies after a restart or when the dependency returns.
        /// </summary>
        public static void SetEnabled(string featureId, bool enabled)
        {
            try
            {
                FeatureInfo info = Find(featureId);
                if (info == null)
                {
                    ModLog.Warn(CoreId, "features: SetEnabled for unknown feature '" + featureId + "' is ignored");
                    return;
                }

                ModSettings.SetFeatureEnabled(info.Id, enabled);
                if (_halted) return;

                bool current = _machine.StatusOf(info.Id).WantsEnabled;
                if (!info.SupportsHotToggle && PTModHost.CurrentScene == PTModHost.GameScene)
                {
                    if (enabled == current) Pending.Remove(info.Id);
                    else Pending[info.Id] = enabled;
                    ModLog.Info(info.Id, "switched " + OnOff(enabled) + " by the player; takes effect when the scene changes");
                    return;
                }

                Pending.Remove(info.Id);
                if (enabled == current) return;

                ModLog.Info(info.Id, "switched " + OnOff(enabled) + " by the player");
                _machine.SetEnabled(info.Id, enabled);
                Settle();
            }
            catch (Exception ex)
            {
                ModLog.Error(CoreId, "features: cannot switch feature '" + featureId + "'", ex);
            }
        }

        /// <summary>
        /// What the hub has to ask the player before it calls <c>SetEnabled(featureId, false)</c> (FR-3 item 8, AC-3.3, AC-17.3): the feature's
        /// <c>FeatureContext.ConfirmBeforeDisable</c> provider is called now, so the answer is as fresh as the player's click. The result:
        /// <list type="bullet">
        /// <item><b>null</b> - switch off without a question: the feature declared no provider, the provider returned null, the feature is not Enabled (nothing runs,
        /// so there is nothing to lose; its provider is not called), or the id is unknown.</item>
        /// <item><b>a <see cref="DisableConfirmation"/></b> - ask it. The object is the provider's own.</item>
        /// <item><b>a <see cref="DisableConfirmation"/> without message</b> - the provider threw (counted against the feature, the log has it): the hub then asks its
        /// plain "Turn off &lt;name&gt;?", so the player can still switch the feature off but not by accident. A throw that trips the breaker makes the feature
        /// Faulted, which is null.</item>
        /// </list>
        /// Asking is the hub's business only and shows nothing by itself; calling this from a feature has no effect on the feature. Never throws.
        /// </summary>
        public static DisableConfirmation GetDisableConfirmation(string featureId)
        {
            try
            {
                FeatureInfo info = Find(featureId);
                Func<DisableConfirmation> provider;
                if (info == null || !DisableProviders.TryGetValue(info.Id, out provider)) return null;
                if (StateOf(info.Id) != FeatureState.Enabled) return null;

                DisableConfirmation result = null;
                bool returned = false;
                ModHooks.Guard(info.Id, "ConfirmBeforeDisable provider", () =>
                {
                    result = provider();
                    returned = true;
                });

                if (returned) return result;
                if (StateOf(info.Id) != FeatureState.Enabled) return null;   // the failure tripped the breaker; the feature is off already
                return new DisableConfirmation();
            }
            catch (Exception ex)
            {
                ModLog.Error(CoreId, "features: cannot ask '" + featureId + "' for its disable confirmation", ex);
                return null;
            }
        }

        /// <summary>
        /// <c>FeatureContext.ConfirmBeforeDisable</c>: remembers the provider of the feature (the latest call wins, null removes it). It lasts the session and is
        /// consulted only by <see cref="GetDisableConfirmation"/>.
        /// </summary>
        internal static void SetDisableConfirmationProvider(string featureId, Func<DisableConfirmation> provider)
        {
            FeatureInfo info = Find(featureId);
            if (info == null)
            {
                ModLog.Warn(CoreId, "features: ConfirmBeforeDisable for unknown feature '" + featureId + "' is ignored");
                return;
            }

            if (provider == null)
            {
                if (DisableProviders.Remove(info.Id)) ModLog.Debug(info.Id, "hub: the disable confirmation is withdrawn");
                return;
            }

            bool replaced = DisableProviders.ContainsKey(info.Id);
            DisableProviders[info.Id] = provider;
            ModLog.Debug(info.Id, "hub: the disable confirmation is " + (replaced ? "replaced" : "declared"));
        }

        /// <summary>
        /// The feature cannot run for good (a required patch could not be applied, a patch targets a reserved method): Unavailable until the
        /// game restarts, switched off now if it ran. For <c>ModHooks</c>; safe to call from the lifecycle events and from feature code.
        /// </summary>
        internal static void MarkUnavailable(string featureId, string detail)
        {
            try
            {
                if (_halted || Find(featureId) == null) return;
                _machine.MarkUnavailable(featureId, detail);
                Settle();
            }
            catch (Exception ex)
            {
                ModLog.Error(CoreId, "features: cannot mark '" + featureId + "' unavailable", ex);
            }
        }

        /// <summary>The circuit breaker tripped (FR-2 item 4): Faulted until the game restarts, switched off now. For <c>ModHooks</c>.</summary>
        internal static void Fault(string featureId, string detail)
        {
            try
            {
                if (_halted || Find(featureId) == null) return;
                _machine.Fault(featureId, detail);
                Settle();
            }
            catch (Exception ex)
            {
                ModLog.Error(CoreId, "features: cannot mark '" + featureId + "' faulted", ex);
            }
        }

        /// <summary>
        /// A failure of feature code caught outside <c>ModHooks.Guard</c> (an event handler, a coroutine): goes to <see cref="FaultSink"/>
        /// (counted against the owner), or is logged when there is none. Never throws.
        /// </summary>
        internal static void ReportFault(string featureId, string site, Exception exception)
        {
            string owner = string.IsNullOrEmpty(featureId) ? CoreId : featureId;
            Action<string, string, Exception> sink = FaultSink;
            if (sink != null)
            {
                try
                {
                    sink(owner, site, exception);
                    return;
                }
                catch (Exception sinkError)
                {
                    ModLog.Error(CoreId, "features: the fault sink failed", sinkError);
                }
            }
            ModLog.Error(owner, (site ?? "handler") + " failed", exception);
        }

        /// <summary>The owner of a handler: the feature of the type that declares its method, the framework for anything else.</summary>
        internal static string OwnerOf(Delegate handler)
        {
            MethodInfo method = handler == null ? null : handler.Method;
            return (method == null ? null : FeatureOfType(method.DeclaringType)) ?? CoreId;
        }

        // ---- start (ModBootstrap) ----

        /// <summary>
        /// Builds the registry from the classes marked <c>[ModFeature]</c>: reads each feature's declaration, orders them, takes the player's
        /// choices from <c>ModSettings</c>. No feature code runs here except the constructors and the property getters.
        /// </summary>
        internal static void Build(IList<Type> featureTypes)
        {
            if (_built) return;
            _built = true;

            var found = new List<FeatureInfo>();
            foreach (Type type in featureTypes)
            {
                FeatureInfo info = ReadFeature(type);
                if (info == null) continue;

                FeatureInfo first;
                if (ById.TryGetValue(info.Id, out first))
                {
                    ModLog.Error(info.Id, "features: " + type.FullName + " declares the same id as " + first.FeatureType.FullName + " and is ignored");
                    continue;
                }
                ById[info.Id] = info;
                found.Add(info);
            }

            // Before the state machine: it asks ModSettings, and ModSettings asks this provider for features the player has not chosen yet.
            ModSettings.FeatureDefaultProvider = DefaultOf;

            var nodes = new List<FeatureNode>(found.Count);
            foreach (FeatureInfo info in found) nodes.Add(new FeatureNode(info.Id, info.DependsOn));
            var graph = new FeatureGraph(nodes);
            foreach (string problem in graph.Problems) ModLog.Warn(CoreId, "features: " + problem);

            var machine = new FeatureStateMachine(graph, id => ModSettings.IsFeatureEnabled(id));
            foreach (string id in graph.Order)
            {
                FeatureInfo info = ById[id];
                Ordered.Add(info);
                if (info.Problem != null) machine.MarkUnavailable(id, info.Problem);
            }

            _graph = graph;
            _machine = machine;
            _all = new ReadOnlyCollection<FeatureInfo>(Ordered.ToArray());

            // The starting states are not changes: subscribers hear only what happens from here on.
            foreach (FeatureInfo info in Ordered) Reported[info.Id] = machine.StateOf(info.Id);
        }

        /// <summary>Calls <c>Register</c> of every active feature in dependency order (a failure makes the feature and its dependents Unavailable).</summary>
        internal static void RegisterActive()
        {
            foreach (FeatureInfo info in Ordered)
            {
                if (_halted) return;
                if (info.Feature == null || info.RegisterAttempted || !_machine.IsActive(info.Id)) continue;
                Register(info);
            }
            Settle();
        }

        /// <summary>The host exists: the active features start (<c>OnEnabled</c>). Called once by the bootstrap after the AfterRegister stage.</summary>
        internal static void OpenActivation()
        {
            if (_halted) return;
            _activationOpen = true;
            Settle();
        }

        /// <summary>
        /// The start failed: every running feature is switched off (dependents first) and from now on every id, the framework's included,
        /// is Unavailable, so no handler, tick, patch body or coroutine of the mod runs. No <see cref="StateChanged"/> is raised.
        /// </summary>
        internal static void Halt(string reason)
        {
            if (_halted) return;
            _activationOpen = false;
            for (int i = Ordered.Count - 1; i >= 0; i--)
            {
                if (Ordered[i].Live) Deactivate(Ordered[i]);
            }
            _haltReason = reason;
            _halted = true;
            Pending.Clear();
        }

        /// <summary>The scene changes (<see cref="PTModHost.SceneLeaving"/>): the switches that waited for it take effect, in dependency order.</summary>
        internal static void ApplyPendingChanges()
        {
            try
            {
                if (Pending.Count == 0) return;
                if (_halted)
                {
                    Pending.Clear();
                    return;
                }

                var changes = new List<KeyValuePair<string, bool>>(Pending);
                Pending.Clear();
                changes.Sort((a, b) => _graph.IndexOf(a.Key).CompareTo(_graph.IndexOf(b.Key)));
                foreach (KeyValuePair<string, bool> change in changes)
                {
                    ModLog.Info(change.Key, "the switch " + OnOff(change.Value) + " takes effect (scene change)");
                    _machine.SetEnabled(change.Key, change.Value);
                }
                Settle();
            }
            catch (Exception ex)
            {
                ModLog.Error(CoreId, "features: cannot apply the switches made in the game scene", ex);
            }
        }

        /// <summary>One line for the start log: the features by state, with the reasons of those that cannot run.</summary>
        internal static string Summary()
        {
            if (Ordered.Count == 0) return "no features";

            var enabled = new List<string>();
            var disabled = new List<string>();
            var other = new List<string>();
            foreach (FeatureInfo info in Ordered)
            {
                FeatureStatus status = StatusOf(info.Id);
                if (status.State == FeatureState.Enabled) enabled.Add(info.Id);
                else if (status.State == FeatureState.Disabled) disabled.Add(info.Id);
                else other.Add(info.Id + " " + status.State.ToString().ToLowerInvariant() + (status.Reason == null ? "" : " (" + status.Reason + ")"));
            }

            var sb = new StringBuilder();
            sb.Append(Ordered.Count).Append(Ordered.Count == 1 ? " feature" : " features");
            sb.Append("; enabled: ").Append(enabled.Count == 0 ? "none" : string.Join(", ", enabled.ToArray()));
            if (disabled.Count > 0) sb.Append("; disabled: ").Append(string.Join(", ", disabled.ToArray()));
            if (other.Count > 0) sb.Append("; not running: ").Append(string.Join("; ", other.ToArray()));
            return sb.ToString();
        }

        // ---- discovery ----

        /// <summary>
        /// Reads one <c>[ModFeature]</c> class. A class that cannot be used is still listed (Unavailable, with <see cref="FeatureInfo.Problem"/>)
        /// when an id can be told for it; null when it cannot.
        /// </summary>
        private static FeatureInfo ReadFeature(Type type)
        {
            string slug = FeatureOfType(type);
            bool inFeatureNamespace = slug != null && slug != CoreId;

            IModFeature feature = null;
            string id = null, nameKey = null, descriptionKey = null, problem = null;
            string[] dependsOn = null;
            bool byDefault = false, hotToggle = false;
            try
            {
                if (!typeof(IModFeature).IsAssignableFrom(type)) problem = "the class does not implement IModFeature";
                else if (type.IsAbstract || type.ContainsGenericParameters) problem = "the class is abstract or generic";
                else
                {
                    feature = (IModFeature)Activator.CreateInstance(type);
                    id = feature.Id;
                    nameKey = feature.NameKey;
                    descriptionKey = feature.DescriptionKey;
                    string[] declared = feature.DependsOn;
                    dependsOn = declared == null ? null : (string[])declared.Clone();
                    byDefault = feature.EnabledByDefault;
                    hotToggle = feature.SupportsHotToggle;
                }
            }
            catch (Exception ex)
            {
                Exception cause = ex is TargetInvocationException && ex.InnerException != null ? ex.InnerException : ex;
                problem = "the class cannot be used: " + cause.GetType().Name + ": " + cause.Message;
                feature = null;
            }

            if (problem == null && string.IsNullOrWhiteSpace(id)) problem = "the feature's Id is empty";
            if (problem == null && id == CoreId) problem = "the id '" + CoreId + "' belongs to the framework";
            if (problem == null && !inFeatureNamespace) problem = "the class must be in the namespace PTMod.Features.<Name>";
            if (problem == null && id != slug) problem = "the id '" + id + "' does not match the namespace " + type.Namespace + " (expected '" + slug + "')";

            string listedId = !string.IsNullOrWhiteSpace(id) && id != CoreId ? id : inFeatureNamespace ? slug : null;
            if (listedId == null)
            {
                ModLog.Error(CoreId, "features: " + type.FullName + " is ignored: " + (problem ?? "no usable id"));
                return null;
            }
            if (problem != null)
            {
                ModLog.Error(listedId, "features: " + type.FullName + " cannot run: " + problem);
                feature = null;
            }

            return new FeatureInfo(type, feature, listedId, nameKey, descriptionKey, dependsOn, byDefault, hotToggle, problem);
        }

        private static bool DefaultOf(string featureId)
        {
            FeatureInfo info = Find(featureId);
            return info != null && info.EnabledByDefault;
        }

        // ---- reconciliation: make the running features match the state machine ----

        /// <summary>Brings the features in line with the states, then tells the subscribers. Re-entrant: a nested call only marks the work.</summary>
        private static void Settle()
        {
            Reconcile();
            RaiseChanges();
        }

        private static void Reconcile()
        {
            if (_reconciling)
            {
                _dirty = true;   // the outer pass repeats
                return;
            }
            if (_halted) return;

            _reconciling = true;
            try
            {
                int limit = 4 * (Ordered.Count + 2);
                for (int pass = 0; pass < limit; pass++)
                {
                    _dirty = false;

                    // Stop first, dependents before their dependencies.
                    for (int i = Ordered.Count - 1; i >= 0; i--)
                    {
                        FeatureInfo info = Ordered[i];
                        if (info.Live && !_machine.IsActive(info.Id)) Deactivate(info);
                    }

                    // Then start, dependencies first; a state change on the way restarts the pass, so its dependents stop first.
                    if (_activationOpen)
                    {
                        for (int i = 0; i < Ordered.Count && !_dirty; i++)
                        {
                            FeatureInfo info = Ordered[i];
                            if (!info.Live && _machine.IsActive(info.Id)) Activate(info);
                        }
                    }

                    if (!_dirty) return;
                }
                ModLog.Error(CoreId, "features: the feature states did not settle after " + limit + " passes");
            }
            finally
            {
                _reconciling = false;
            }
        }

        private static void Activate(FeatureInfo info)
        {
            if (!info.RegisterAttempted && !Register(info)) return;
            if (!info.IsRegistered || !_machine.IsActive(info.Id)) return;

            RaiseLifecycle(FeatureActivating, info, "activation", true);
            if (!_machine.IsActive(info.Id))
            {
                // Refused (patches could not be applied): let every listener undo what it did.
                RaiseLifecycle(FeatureDeactivated, info, "rollback", false);
                return;
            }

            info.Live = true;
            try
            {
                info.Feature.OnEnabled();
            }
            catch (Exception ex)
            {
                ModLog.Error(info.Id, "OnEnabled failed; the feature is off until the game restarts", ex);
                _machine.Fault(info.Id, "OnEnabled failed: " + ex.GetType().Name + ": " + ex.Message);
                _dirty = true;
            }
        }

        private static void Deactivate(FeatureInfo info)
        {
            info.Live = false;
            try
            {
                if (info.Feature != null) info.Feature.OnDisabled();
            }
            catch (Exception ex)
            {
                ModLog.Error(info.Id, "OnDisabled failed", ex);
            }
            RaiseLifecycle(FeatureDeactivated, info, "deactivation", false);
        }

        /// <summary>Runs <c>Register</c> once. False when it failed (the feature is then Unavailable until restart) or the feature is no longer active.</summary>
        private static bool Register(FeatureInfo info)
        {
            info.RegisterAttempted = true;
            try
            {
                info.Feature.Register(info.Context);
                info.IsRegistered = true;
            }
            catch (Exception ex)
            {
                ModLog.Error(info.Id, "Register failed; the feature is unavailable until the game restarts", ex);
                _machine.MarkUnavailable(info.Id, "Register failed: " + ex.GetType().Name + ": " + ex.Message);
                _dirty = true;
                return false;
            }

            RaiseLifecycle(FeatureRegistered, info, "registration", false);
            return _machine.IsActive(info.Id);
        }

        /// <summary>Calls each core listener in turn; a throwing <see cref="FeatureActivating"/> listener refuses the start.</summary>
        private static void RaiseLifecycle(Action<FeatureInfo> handlers, FeatureInfo info, string what, bool throwRefuses)
        {
            if (handlers == null) return;

            foreach (Delegate handler in handlers.GetInvocationList())
            {
                try
                {
                    ((Action<FeatureInfo>)handler)(info);
                }
                catch (Exception ex)
                {
                    ModLog.Error(CoreId, "features: a " + what + " listener (" + NameOf(handler) + ") failed for '" + info.Id + "'", ex);
                    if (throwRefuses && _machine.IsActive(info.Id))
                    {
                        _machine.MarkUnavailable(info.Id, "activation failed: " + ex.GetType().Name + ": " + ex.Message);
                        _dirty = true;
                    }
                }
            }
        }

        /// <summary>
        /// Raises <see cref="StateChanged"/> for every feature whose state differs from what the subscribers last heard: the ones that stopped
        /// running first (dependents first), then the rest in dependency order. A switch made by a handler is settled and reported in the same loop.
        /// </summary>
        private static void RaiseChanges()
        {
            if (_raising || _reconciling || _halted) return;

            _raising = true;
            try
            {
                int limit = 8 * (Ordered.Count + 2);
                for (int raised = 0; raised < limit; raised++)
                {
                    FeatureInfo info;
                    FeatureState before;
                    if (!NextChange(out info, out before)) return;

                    FeatureState after = _machine.StateOf(info.Id);
                    Reported[info.Id] = after;
                    LogChange(info, before, after);
                    Raise(info.Id, after);
                }
                ModLog.Error(CoreId, "features: state changes kept coming after " + limit + " notifications");
            }
            finally
            {
                _raising = false;
            }
        }

        private static bool NextChange(out FeatureInfo info, out FeatureState before)
        {
            for (int i = Ordered.Count - 1; i >= 0; i--)
            {
                FeatureState was = Reported[Ordered[i].Id];
                if (was == FeatureState.Enabled && _machine.StateOf(Ordered[i].Id) != FeatureState.Enabled)
                {
                    info = Ordered[i];
                    before = was;
                    return true;
                }
            }
            for (int i = 0; i < Ordered.Count; i++)
            {
                FeatureState was = Reported[Ordered[i].Id];
                if (was != _machine.StateOf(Ordered[i].Id))
                {
                    info = Ordered[i];
                    before = was;
                    return true;
                }
            }
            info = null;
            before = FeatureState.Disabled;
            return false;
        }

        private static void LogChange(FeatureInfo info, FeatureState before, FeatureState after)
        {
            string reason = _machine.StatusOf(info.Id).Reason;
            string line = "state: " + before + " -> " + after + (reason == null ? "" : " (" + reason + ")");
            if (after == FeatureState.Faulted) ModLog.Warn(info.Id, line + "; off until the game restarts");
            else ModLog.Info(info.Id, line);
        }

        private static void Raise(string featureId, FeatureState state)
        {
            Action<string, FeatureState> handlers = StateChanged;
            if (handlers == null) return;

            foreach (Delegate handler in handlers.GetInvocationList())
            {
                string owner = OwnerOf(handler);
                if (!IsActive(owner)) continue;
                try
                {
                    ((Action<string, FeatureState>)handler)(featureId, state);
                }
                catch (Exception ex)
                {
                    ReportFault(owner, "ModFeatures.StateChanged handler " + NameOf(handler), ex);
                }
            }
        }

        private static string NameOf(Delegate handler)
        {
            MethodInfo method = handler.Method;
            return method == null ? "?" : (method.DeclaringType == null ? "" : method.DeclaringType.Name + ".") + method.Name;
        }

        private static string OnOff(bool enabled)
        {
            return enabled ? "on" : "off";
        }
    }
}
