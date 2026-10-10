using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using PTMod.Core.Logic.Log;

namespace PTMod.Core
{
    /// <summary>
    /// Fault isolation (FR-2 items 3, 4, 8): the wrappers for patch bodies and handlers, the fault counter and the circuit breaker.
    /// <list type="bullet">
    /// <item><b>Guard.</b> <see cref="Guard(string, string, Action)"/> runs the body in try/catch: an exception never reaches the game, it is
    /// counted against the feature and the caller gets false (or the fallback) so the call goes on as vanilla - a prefix returns true, a
    /// postfix leaves <c>__result</c> alone. The body is skipped when its owner is Faulted or Unavailable, or for the core when the unit is
    /// switched off. A hot path that cannot afford a delegate checks <see cref="CanRun"/> and calls <see cref="ReportFault"/> from its own catch.</item>
    /// <item><b>Log.</b> The first failure of a site (feature + <c>siteId</c>) is written in full, with its stack; the next ones are only
    /// counted, and one summary line "(повторено N раз)" follows when the breaker trips or the game quits (AC-2.2).</item>
    /// <item><b>Breaker.</b> <c>hooks.faultThreshold</c> (5, <c>core.json</c>) failures of a feature in a session make it Faulted until the game
    /// restarts. The core is never switched off as a whole: its unit is - the part of <c>siteId</c> before the first '/' (<c>"P2/prefix"</c>
    /// and <c>"P2/postfix"</c> are the unit <c>P2</c>) - and the game runs on as without that channel or service.</item>
    /// <item><b>Raise.</b> An event raised through <see cref="Raise(string, Delegate, object[])"/> (or the typed overloads) calls every
    /// handler on its own: a handler of a feature that is not Enabled is skipped, an exception is counted against the handler's feature
    /// (by the namespace of its type, <c>ModFeatures.FeatureOfType</c>) and the next handler still runs (AC-2.5).</item>
    /// </list>
    /// </summary>
    public static partial class ModHooks
    {
        private const int DefaultFaultThreshold = 5;

        private static readonly object FaultSync = new object();
        private static readonly Dictionary<string, int> FeatureFaults = new Dictionary<string, int>(StringComparer.Ordinal);
        private static readonly Dictionary<string, SiteFaults> SiteCounters = new Dictionary<string, SiteFaults>(StringComparer.Ordinal);
        private static readonly Dictionary<string, int> UnitFaults = new Dictionary<string, int>(StringComparer.Ordinal);
        private static readonly HashSet<string> TrippedFeatures = new HashSet<string>(StringComparer.Ordinal);
        private static readonly HashSet<string> TrippedUnits = new HashSet<string>(StringComparer.Ordinal);

        private static volatile bool _anyTripped;
        private static int _faultThreshold;   // 0 until read from core.json

        // How deep the current thread is inside a guarded body, a handler or a fault report: patches are not removed from under a running patch.
        [ThreadStatic] private static int _depth;

        /// <summary>
        /// Exceptions of a feature in a session that switch it off (<c>hooks.faultThreshold</c> in <c>core.json</c>, 5; read on first use,
        /// clamped to 1..1000).
        /// </summary>
        public static int FaultThreshold
        {
            get
            {
                int value = _faultThreshold;
                if (value > 0) return value;

                double read = DefaultFaultThreshold;
                try
                {
                    read = ModContent.GetCoreNumber("hooks", "faultThreshold", DefaultFaultThreshold);
                }
                catch (Exception)
                {
                    // ModContent logs its own problems and does not throw; the default stands.
                }
                value = (int)Math.Max(1, Math.Min(1000, Math.Round(read)));
                _faultThreshold = value;
                return value;
            }
        }

        // ---- guards ----

        /// <summary>
        /// Runs <paramref name="body"/> in try/catch. True when it ran to the end; false when it threw (the exception is counted against
        /// <paramref name="featureId"/>, see <see cref="ReportFault"/>) or was skipped (<see cref="CanRun"/> is false). Never throws.
        /// </summary>
        /// <param name="featureId">The feature the body belongs to (<c>FeatureId</c> of the feature; null or empty is the core).</param>
        /// <param name="siteId">Where: <c>Class.Method/kind</c> or the handler's name; part of the log line and of the repeat key.</param>
        public static bool Guard(string featureId, string siteId, Action body)
        {
            if (body == null || !CanRun(featureId, siteId)) return false;

            _depth++;
            try
            {
                body();
                return true;
            }
            catch (Exception ex)
            {
                ReportFault(featureId, siteId, ex);
                return false;
            }
            finally
            {
                _depth--;
            }
        }

        /// <summary>
        /// Runs <paramref name="body"/> in try/catch and returns its value; <paramref name="fallback"/> when it threw (counted against the
        /// feature) or was skipped. A prefix passes <c>fallback: true</c> so the original runs. Never throws.
        /// </summary>
        public static T Guard<T>(string featureId, string siteId, Func<T> body, T fallback)
        {
            if (body == null || !CanRun(featureId, siteId)) return fallback;

            _depth++;
            try
            {
                return body();
            }
            catch (Exception ex)
            {
                ReportFault(featureId, siteId, ex);
                return fallback;
            }
            finally
            {
                _depth--;
            }
        }

        /// <summary>
        /// May code of this feature run at this site: false when the feature is Faulted or Unavailable (an unknown id too, and every id after a
        /// failed start), or,
        /// for the core, when the unit of <paramref name="siteId"/> was switched off. A Disabled feature may still run its own code (its
        /// <c>OnDisabled</c> cleanup); its patches are removed and its handlers are not called anyway. Allocation-free.
        /// </summary>
        public static bool CanRun(string featureId, string siteId)
        {
            string owner = string.IsNullOrEmpty(featureId) ? CoreId : featureId;
            if (_anyTripped)
            {
                lock (FaultSync)
                {
                    if (owner == CoreId ? TrippedUnits.Count > 0 && TrippedUnits.Contains(UnitOf(siteId)) : TrippedFeatures.Contains(owner)) return false;
                }
            }

            FeatureState state = ModFeatures.StateOf(owner);
            return state != FeatureState.Faulted && state != FeatureState.Unavailable;
        }

        /// <summary>
        /// Counts a failure of feature code caught at the boundary of a hook (FR-2 items 3, 4): the first failure of the site goes to the log
        /// with its stack, later ones only add to the count; at <see cref="FaultThreshold"/> failures the feature becomes Faulted (the core
        /// switches off the unit of the site). Safe from any context: never throws, never calls back into the feature that failed.
        /// </summary>
        public static void ReportFault(string featureId, string siteId, Exception e)
        {
            _depth++;
            try
            {
                Count(string.IsNullOrEmpty(featureId) ? CoreId : featureId, string.IsNullOrEmpty(siteId) ? "unknown site" : siteId, e);
            }
            catch (Exception inner)
            {
                try
                {
                    ModLog.Error(CoreId, "hooks: the fault counter failed while reporting a fault of '" + featureId + "'", inner);
                }
                catch (Exception)
                {
                    // ModLog does not throw; nothing else to do.
                }
            }
            finally
            {
                _depth--;
            }
        }

        /// <summary>Failures counted against the feature in this session (the core: all its units together).</summary>
        public static int FaultsOf(string featureId)
        {
            string owner = string.IsNullOrEmpty(featureId) ? CoreId : featureId;
            lock (FaultSync)
            {
                int count;
                return FeatureFaults.TryGetValue(owner, out count) ? count : 0;
            }
        }

        /// <summary>The core units switched off by the breaker in this session (for the Diagnostics tab).</summary>
        internal static IReadOnlyList<string> TrippedCoreUnits()
        {
            lock (FaultSync)
            {
                var list = new List<string>(TrippedUnits);
                list.Sort(StringComparer.Ordinal);
                return list;
            }
        }

        /// <summary>The unit of a core site: the part before the first '/', or the whole id.</summary>
        internal static string UnitOf(string siteId)
        {
            if (string.IsNullOrEmpty(siteId)) return "unknown site";
            int slash = siteId.IndexOf('/');
            return slash > 0 ? siteId.Substring(0, slash) : siteId;
        }

        /// <summary>True while the current thread runs a guarded body, an event handler or a fault report.</summary>
        internal static bool InGuardedCall
        {
            get { return _depth > 0; }
        }

        // ---- events ----

        /// <summary>
        /// Raises an event handler by handler (FR-2 item 8): handlers of features that are not Enabled are skipped, an exception is counted
        /// against the handler's feature and the next handler runs. Calls through reflection (<c>DynamicInvoke</c>); the typed overloads
        /// below do not and should be preferred. Arguments that do not fit the delegate are a bug of the raiser: one error under the core.
        /// </summary>
        public static void Raise(string eventName, Delegate handlers, params object[] args)
        {
            if (handlers == null) return;

            _depth++;
            try
            {
                Delegate[] list = handlers.GetInvocationList();
                for (int i = 0; i < list.Length; i++)
                {
                    string owner;
                    if (!ShouldCall(eventName, list[i], out owner)) continue;
                    try
                    {
                        list[i].DynamicInvoke(args);
                    }
                    catch (TargetInvocationException ex)
                    {
                        HandlerFailed(eventName, list[i], owner, ex.InnerException ?? ex);
                    }
                    catch (Exception ex) when (ex is ArgumentException || ex is TargetParameterCountException || ex is MemberAccessException)
                    {
                        ModLog.Error(CoreId, "hooks: " + (eventName ?? "event") + " was raised with arguments that do not fit its handlers", ex);
                        return;
                    }
                    catch (Exception ex)
                    {
                        HandlerFailed(eventName, list[i], owner, ex);
                    }
                }
            }
            finally
            {
                _depth--;
            }
        }

        /// <summary>Raises an event without arguments; see <see cref="Raise(string, Delegate, object[])"/>.</summary>
        public static void Raise(string eventName, Action handlers)
        {
            if (handlers == null) return;

            _depth++;
            try
            {
                Delegate[] list = handlers.GetInvocationList();
                for (int i = 0; i < list.Length; i++)
                {
                    string owner;
                    if (!ShouldCall(eventName, list[i], out owner)) continue;
                    try
                    {
                        ((Action)list[i])();
                    }
                    catch (Exception ex)
                    {
                        HandlerFailed(eventName, list[i], owner, ex);
                    }
                }
            }
            finally
            {
                _depth--;
            }
        }

        /// <summary>Raises an event with one argument; see <see cref="Raise(string, Delegate, object[])"/>.</summary>
        public static void Raise<T1>(string eventName, Action<T1> handlers, T1 arg1)
        {
            if (handlers == null) return;

            _depth++;
            try
            {
                Delegate[] list = handlers.GetInvocationList();
                for (int i = 0; i < list.Length; i++)
                {
                    string owner;
                    if (!ShouldCall(eventName, list[i], out owner)) continue;
                    try
                    {
                        ((Action<T1>)list[i])(arg1);
                    }
                    catch (Exception ex)
                    {
                        HandlerFailed(eventName, list[i], owner, ex);
                    }
                }
            }
            finally
            {
                _depth--;
            }
        }

        /// <summary>Raises an event with two arguments; see <see cref="Raise(string, Delegate, object[])"/>.</summary>
        public static void Raise<T1, T2>(string eventName, Action<T1, T2> handlers, T1 arg1, T2 arg2)
        {
            if (handlers == null) return;

            _depth++;
            try
            {
                Delegate[] list = handlers.GetInvocationList();
                for (int i = 0; i < list.Length; i++)
                {
                    string owner;
                    if (!ShouldCall(eventName, list[i], out owner)) continue;
                    try
                    {
                        ((Action<T1, T2>)list[i])(arg1, arg2);
                    }
                    catch (Exception ex)
                    {
                        HandlerFailed(eventName, list[i], owner, ex);
                    }
                }
            }
            finally
            {
                _depth--;
            }
        }

        /// <summary>Raises an event with three arguments; see <see cref="Raise(string, Delegate, object[])"/>.</summary>
        public static void Raise<T1, T2, T3>(string eventName, Action<T1, T2, T3> handlers, T1 arg1, T2 arg2, T3 arg3)
        {
            if (handlers == null) return;

            _depth++;
            try
            {
                Delegate[] list = handlers.GetInvocationList();
                for (int i = 0; i < list.Length; i++)
                {
                    string owner;
                    if (!ShouldCall(eventName, list[i], out owner)) continue;
                    try
                    {
                        ((Action<T1, T2, T3>)list[i])(arg1, arg2, arg3);
                    }
                    catch (Exception ex)
                    {
                        HandlerFailed(eventName, list[i], owner, ex);
                    }
                }
            }
            finally
            {
                _depth--;
            }
        }

        /// <summary>Raises an event with four arguments (<c>ModEvents.CpuReleased</c>); see <see cref="Raise(string, Delegate, object[])"/>.</summary>
        public static void Raise<T1, T2, T3, T4>(string eventName, Action<T1, T2, T3, T4> handlers, T1 arg1, T2 arg2, T3 arg3, T4 arg4)
        {
            if (handlers == null) return;

            _depth++;
            try
            {
                Delegate[] list = handlers.GetInvocationList();
                for (int i = 0; i < list.Length; i++)
                {
                    string owner;
                    if (!ShouldCall(eventName, list[i], out owner)) continue;
                    try
                    {
                        ((Action<T1, T2, T3, T4>)list[i])(arg1, arg2, arg3, arg4);
                    }
                    catch (Exception ex)
                    {
                        HandlerFailed(eventName, list[i], owner, ex);
                    }
                }
            }
            finally
            {
                _depth--;
            }
        }

        // ---- internals ----

        /// <summary>Per site (feature + site id): how often it failed and how many of those the log has not heard of yet.</summary>
        private sealed class SiteFaults
        {
            public SiteFaults(string featureId, string siteId)
            {
                FeatureId = featureId;
                SiteId = siteId;
            }

            public string FeatureId { get; }

            public string SiteId { get; }

            public int Count;

            public int Unreported;

            public string LastError;
        }

        /// <summary>The handler's owner is active (and, for the core, the handler's unit is on).</summary>
        private static bool ShouldCall(string eventName, Delegate handler, out string owner)
        {
            owner = ModFeatures.OwnerOf(handler);
            if (!ModFeatures.IsActive(owner)) return false;
            return owner != CoreId || !_anyTripped || CanRun(owner, SiteOf(eventName, handler));
        }

        private static void HandlerFailed(string eventName, Delegate handler, string owner, Exception ex)
        {
            ReportFault(owner, SiteOf(eventName, handler), ex);
        }

        /// <summary>
        /// The site of an event handler: <c>Type.Method@event</c>. It has no '/', so for the core the unit is the handler itself (a broken core
        /// handler of an event does not switch off the others).
        /// </summary>
        private static string SiteOf(string eventName, Delegate handler)
        {
            MethodInfo method = handler == null ? null : handler.Method;
            string name = method == null ? "?" : (method.DeclaringType == null ? "" : method.DeclaringType.Name + ".") + method.Name;
            return eventName == null ? name : name + "@" + eventName;
        }

        private static void Count(string owner, string siteId, Exception e)
        {
            int threshold = FaultThreshold;
            bool isCore = owner == CoreId;
            string unit = isCore ? UnitOf(siteId) : null;
            string error = e == null ? "no exception" : LogFormatter.DescribeException(e);

            bool first;
            int featureCount, unitCount = 0;
            bool trip = false;
            List<SiteFaults> summaries = null;
            lock (FaultSync)
            {
                int count;
                FeatureFaults.TryGetValue(owner, out count);
                featureCount = count + 1;
                FeatureFaults[owner] = featureCount;

                string key = owner + "\n" + siteId;
                SiteFaults site;
                if (!SiteCounters.TryGetValue(key, out site))
                {
                    site = new SiteFaults(owner, siteId);
                    SiteCounters[key] = site;
                }
                site.Count++;
                site.LastError = error;
                first = site.Count == 1;
                if (!first) site.Unreported++;

                if (isCore)
                {
                    UnitFaults.TryGetValue(unit, out unitCount);
                    unitCount++;
                    UnitFaults[unit] = unitCount;
                    if (unitCount >= threshold && TrippedUnits.Add(unit)) trip = true;
                }
                else if (featureCount >= threshold && TrippedFeatures.Add(owner))
                {
                    trip = true;
                }

                if (trip)
                {
                    _anyTripped = true;
                    summaries = TakeSummaries(owner, isCore ? unit : null);
                }
            }

            // Outside the lock: the log and the registry may call back.
            if (first)
            {
                string what = isCore
                    ? "failure " + unitCount + " of " + threshold + " before '" + unit + "' is switched off until restart"
                    : "failure " + featureCount + " of " + threshold + " before the feature is switched off until restart";
                ModLog.Error(owner, siteId + " failed (" + what + "; the game goes on)", e);
            }

            if (!trip) return;

            WriteSummaries(summaries);
            if (isCore)
            {
                ModLog.Error(owner, "hooks: '" + unit + "' failed " + unitCount + " times; it is switched off until the game restarts, the game runs on as without it");
                return;
            }

            ModLog.Warn(owner, "hooks: " + featureCount + " failures (hooks.faultThreshold = " + threshold + "), the last at " + siteId + "; the feature is switched off until the game restarts");
            ModFeatures.Fault(owner, featureCount.ToString(CultureInfo.InvariantCulture) + "; " + siteId);
        }

        /// <summary>Takes the unreported repeats of a feature (or of one core unit), resetting them. Called under <see cref="FaultSync"/>.</summary>
        private static List<SiteFaults> TakeSummaries(string owner, string unit)
        {
            var list = new List<SiteFaults>();
            foreach (SiteFaults site in SiteCounters.Values)
            {
                if (site.Unreported <= 0 || site.FeatureId != owner) continue;
                if (unit != null && UnitOf(site.SiteId) != unit) continue;
                list.Add(new SiteFaults(site.FeatureId, site.SiteId) { Unreported = site.Unreported, LastError = site.LastError, Count = site.Count });
                site.Unreported = 0;
            }
            list.Sort((a, b) => string.CompareOrdinal(a.SiteId, b.SiteId));
            return list;
        }

        private static void WriteSummaries(List<SiteFaults> summaries)
        {
            if (summaries == null) return;
            foreach (SiteFaults site in summaries)
            {
                ModLog.Error(site.FeatureId, site.SiteId + " failed again: " + site.LastError + " (" + Repeated(site.Unreported) + ")");
            }
        }

        /// <summary>The repeats nobody has heard of yet, as summary lines: on quit (the host calls it) and when a breaker trips.</summary>
        internal static void FlushFaultSummaries()
        {
            List<SiteFaults> all;
            lock (FaultSync)
            {
                all = new List<SiteFaults>();
                var owners = new HashSet<string>(StringComparer.Ordinal);
                foreach (SiteFaults site in SiteCounters.Values)
                {
                    if (site.Unreported > 0) owners.Add(site.FeatureId);
                }
                foreach (string owner in owners) all.AddRange(TakeSummaries(owner, null));
            }
            WriteSummaries(all);
        }

        /// <summary>"повторено 4 раза": the wording of the log's repeat summaries, the same as <c>ModLog</c> writes (<see cref="LogFormatter.RepeatedPhrase"/>).</summary>
        private static string Repeated(int n)
        {
            return LogFormatter.RepeatedPhrase(n);
        }

        /// <summary>Forget every count and breaker (tests and the harness only; the game never resets a session's faults).</summary>
        internal static void ResetFaultsForTests()
        {
            lock (FaultSync)
            {
                FeatureFaults.Clear();
                SiteCounters.Clear();
                UnitFaults.Clear();
                TrippedFeatures.Clear();
                TrippedUnits.Clear();
                _anyTripped = false;
                _faultThreshold = 0;
            }
        }
    }
}
