using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using PTMod.Core.Logic.Log;

namespace PTMod.Core
{
    /// <summary>
    /// The patch registry (FR-2, decision 0002): what every feature and the core declared with <see cref="ModPatchAttribute"/>, applied
    /// through one <see cref="IPatchBackend"/> (Harmony) with the rules the features rely on:
    /// <list type="bullet">
    /// <item><b>Declaration.</b> A feature declares its patch classes with <see cref="FeatureContext.Patch"/> in <c>Register</c>; each
    /// <c>[ModPatch]</c> becomes a <see cref="PatchSpec"/> right away: the target is resolved (<c>AccessTools.Method</c>/<c>Inner</c>
    /// semantics, private and nested types included), the handler checked, the reservations applied (<see cref="IsReserved"/>). Problems are
    /// logged once, with the target's name, and kept in <see cref="Report"/>. The core's own patch classes (namespace <c>PTMod.Core…</c>)
    /// are found by a scan of the assembly at the Patch stage - there is no central list to edit.</item>
    /// <item><b>All or nothing</b> (FR-2 item 2). <see cref="ApplyFeature"/> runs when the feature starts (<c>ModFeatures.FeatureActivating</c>):
    /// a required patch that cannot be applied - missing target, reserved target, second transpiler on a method, backend error - rolls back
    /// the patches already applied for the feature and makes it Unavailable with the reason; an <c>Optional</c> one is only a warning. For the
    /// core the unit is the patch class: a broken class is rolled back alone and its service is off, the rest of the core runs.</item>
    /// <item><b>Ownership.</b> Every feature patches under its own Harmony id <c>ptmod.&lt;feature-id&gt;</c>; <see cref="RemoveFeature"/> (on
    /// <c>ModFeatures.FeatureDeactivated</c>) removes exactly that feature's patches. A removal asked for from inside a running patch body
    /// (a breaker that trips there) waits for the next frame.</item>
    /// <item><b>Isolation</b> - <see cref="Guard(string, string, Action)"/>, <see cref="ReportFault"/>, <see cref="Raise(string, Delegate, object[])"/>,
    /// the circuit breaker - lives in Guard.cs.</item>
    /// </list>
    /// Without <c>0Harmony.dll</c> the backend is "none": nothing is patched, features that declare patches are Unavailable, the rest of the
    /// mod runs (FR-2 item 7). Main thread only, except the fault counter.
    /// </summary>
    public static partial class ModHooks
    {
        /// <summary>The framework's feature id; also the owner of the core's patches.</summary>
        public const string CoreId = ModLog.CoreFeatureId;

        /// <summary>Harmony owner ids are <c>ptmod.&lt;feature-id&gt;</c> (<c>ptmod.mod-framework</c> for the core).</summary>
        public const string HarmonyIdPrefix = "ptmod.";

        private const BindingFlags AnyMethod = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
        private const BindingFlags AnyStatic = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.DeclaredOnly;

        private static readonly List<Entry> Entries = new List<Entry>();
        private static readonly Dictionary<string, List<Entry>> ByFeature = new Dictionary<string, List<Entry>>(StringComparer.Ordinal);
        private static readonly Dictionary<string, HashSet<Type>> DeclaredClasses = new Dictionary<string, HashSet<Type>>(StringComparer.Ordinal);
        private static readonly Dictionary<string, Problem> ClassProblems = new Dictionary<string, Problem>(StringComparer.Ordinal);
        private static readonly HashSet<string> AppliedFeatures = new HashSet<string>(StringComparer.Ordinal);
        private static readonly List<string> PendingRemovals = new List<string>();
        private static readonly List<Type> CoreClasses = new List<Type>();
        private static readonly HashSet<Type> BrokenCoreClasses = new HashSet<Type>();

        private static IPatchBackend _backend;
        private static string _backendProblem;
        private static bool _started;
        private static bool _coreScanned;
        private static bool _frameHooked;

        // ---- public API ----

        /// <summary>The backend in use: Harmony, or "none" when it could not be loaded (see <see cref="BackendProblem"/>). Created on first use.</summary>
        public static IPatchBackend Backend
        {
            get
            {
                if (_backend == null) _backend = CreateBackend();
                return _backend;
            }
        }

        /// <summary>Why there is no real backend (English), or null.</summary>
        public static string BackendProblem
        {
            get
            {
                if (_backend == null) _backend = CreateBackend();
                return _backendProblem;
            }
        }

        /// <summary>
        /// Applies the declared patches of a feature, all or nothing for the required ones (FR-2 item 2), and returns the state it can have:
        /// Enabled, or Unavailable (then the feature has no patch in the game, <c>ModFeatures.MarkUnavailable</c> was called with the reason
        /// and one error is in the log). Already applied - only patches declared since then are added. For the core: applies the core's patch
        /// classes not applied yet (each class all or nothing) and returns Enabled. Never throws.
        /// </summary>
        public static FeatureState ApplyFeature(string featureId)
        {
            try
            {
                if (string.IsNullOrEmpty(featureId)) return FeatureState.Unavailable;
                if (featureId == CoreId)
                {
                    ApplyCore();
                    return FeatureState.Enabled;
                }
                return ApplyFeatureCore(featureId);
            }
            catch (Exception ex)
            {
                ModLog.Error(CoreId, "hooks: applying the patches of '" + featureId + "' failed", ex);
                Remove(featureId, "rolled back: an error while applying");
                MarkUnavailable(featureId, LocalizedOr("core.hooks.patchFailed", "could not patch {0}", featureId));
                return FeatureState.Unavailable;
            }
        }

        /// <summary>Removes every patch of the feature now (only that feature's; for the core: all core patches). Never throws.</summary>
        public static void RemoveFeature(string featureId)
        {
            Remove(featureId, "removed: the feature stopped");
        }

        /// <summary>Every declared patch with its status, in the order of declaration (a snapshot for the Diagnostics tab and the self-check).</summary>
        public static IReadOnlyList<PatchReport> Report
        {
            get
            {
                var list = new List<PatchReport>(Entries.Count);
                foreach (Entry entry in Entries) list.Add(entry.ToReport());
                return list;
            }
        }

        /// <summary>
        /// The method is reserved (FR-2 item 6): <paramref name="info"/> tells by whom, for which kinds of patch and what to use instead.
        /// Any overload of a reserved name on the type that declares it counts.
        /// </summary>
        public static bool IsReserved(MethodBase target, out ReservedTarget info)
        {
            info = ReservedTargets.Find(target);
            return info != null;
        }

        /// <summary>The method's formula is read by a channel of <c>ModModifiers</c> (a <see cref="ReservedKind.Channel"/> reservation); <paramref name="channel"/> names it.</summary>
        public static bool IsReservedByChannel(MethodBase target, out string channel)
        {
            ReservedTarget info = ReservedTargets.Find(target);
            channel = info != null && info.Kind == ReservedKind.Channel ? info.Name : null;
            return channel != null;
        }

        /// <summary>Every reserved method (the "Резерв" column of design.md).</summary>
        public static IReadOnlyList<ReservedTarget> ReservedTargetList
        {
            get { return ReservedTargets.All; }
        }

        /// <summary>
        /// All patches of a declared patch class are in the game (a core service asks before relying on its patch: <c>ModCredit</c> and the
        /// Optional transpiler P10). False for a class that was not declared or found by the core's scan.
        /// </summary>
        public static bool IsApplied(Type patchClass)
        {
            if (patchClass == null) return false;
            bool any = false;
            foreach (Entry entry in Entries)
            {
                if (entry.PatchClass != patchClass) continue;
                any = true;
                if (entry.Status != PatchStatus.Applied) return false;
            }
            return any;
        }

        // ---- declaration ----

        /// <summary>
        /// Reads the <c>[ModPatch]</c> methods of a feature's patch class (<see cref="FeatureContext.Patch"/>). Nothing is applied here unless
        /// the feature already runs (a class declared in <c>OnEnabled</c> is applied at once, all or nothing for the feature). Never throws.
        /// </summary>
        internal static void Declare(string featureId, Type patchClass)
        {
            if (string.IsNullOrEmpty(featureId)) return;
            try
            {
                if (patchClass == null)
                {
                    ModLog.Warn(featureId, "hooks: Patch(null) is ignored");
                    return;
                }

                HashSet<Type> classes;
                if (!DeclaredClasses.TryGetValue(featureId, out classes))
                {
                    classes = new HashSet<Type>();
                    DeclaredClasses[featureId] = classes;
                }
                if (!classes.Add(patchClass))
                {
                    ModLog.Warn(featureId, "hooks: " + patchClass.FullName + " is declared twice; the second time is ignored");
                    return;
                }

                // The class must belong to the feature: a fault in its bodies is counted by namespace, and the core's classes are the core's.
                string owner = ModFeatures.FeatureOfType(patchClass);
                if (owner != featureId)
                {
                    string why = patchClass.FullName + " belongs to '" + (owner ?? "no feature") + "', not to '" + featureId
                                 + "' (patch classes live in the feature's namespace PTMod.Features.<Name>)";
                    ModLog.Error(featureId, "hooks: " + why + "; the feature cannot start");
                    if (!ClassProblems.ContainsKey(featureId))
                        ClassProblems[featureId] = new Problem(why, LocalizedOr("core.hooks.patchFailed", "could not patch {0}", patchClass.Name));
                    return;
                }

                int added = Read(featureId, patchClass);
                if (added == 0) ModLog.Warn(featureId, "hooks: " + patchClass.FullName + " has no [ModPatch] methods");
                else if (AppliedFeatures.Contains(featureId)) ApplyFeature(featureId);   // declared while running: apply now
            }
            catch (Exception ex)
            {
                ModLog.Error(featureId, "hooks: cannot read the patches of " + (patchClass == null ? "?" : patchClass.FullName), ex);
                if (!ClassProblems.ContainsKey(featureId))
                    ClassProblems[featureId] = new Problem("the patch class could not be read: " + ex.GetType().Name + ": " + ex.Message,
                        LocalizedOr("core.hooks.patchFailed", "could not patch {0}", patchClass == null ? "?" : patchClass.Name));
            }
        }

        /// <summary>Turns the attributes of one class into entries; returns how many. Each problem is logged once, here.</summary>
        private static int Read(string featureId, Type patchClass)
        {
            var methods = new List<MethodInfo>(patchClass.GetMethods(AnyMethod));
            methods.Sort((a, b) => a.MetadataToken.CompareTo(b.MetadataToken));

            int added = 0;
            foreach (MethodInfo handler in methods)
            {
                object[] attributes;
                try
                {
                    attributes = handler.GetCustomAttributes(typeof(ModPatchAttribute), false);
                }
                catch (Exception ex)
                {
                    ModLog.Error(featureId, "hooks: cannot read the attributes of " + patchClass.Name + "." + handler.Name, ex);
                    continue;
                }

                foreach (object attribute in attributes)
                {
                    Entry entry = Build(featureId, patchClass, handler, (ModPatchAttribute)attribute);
                    Add(entry);
                    added++;
                }
            }
            return added;
        }

        private static Entry Build(string featureId, Type patchClass, MethodInfo handler, ModPatchAttribute patch)
        {
            string declaredName = DeclaredTargetName(patch);
            string error;
            MethodBase target = ResolveTarget(patch.Target, patch.InnerType, patch.Method, patch.ArgTypes, out error);
            string targetName = target != null ? NameOf(target) : declaredName;

            var entry = new Entry
            {
                FeatureId = featureId,
                PatchClass = patchClass,
                TargetName = targetName,
                Signature = target != null ? SignatureOf(target) : patch.ArgTypes != null ? "(" + TypeList(patch.ArgTypes) + ")" : null,
                HandlerName = patchClass.Name + "." + handler.Name,
                Kind = patch.Kind,
                Priority = patch.Priority,
                Optional = patch.Optional,
                Target = target,
                Handler = handler,
            };
            entry.Id = UniqueId(featureId + "/" + targetName + "/" + patch.Kind.ToString().ToLowerInvariant());

            string level = patch.Optional ? "optional patch " : "patch ";
            if (target == null)
            {
                entry.Fail(PatchStatus.Failed, "target " + declaredName + " not found: " + error,
                    LocalizedOr("core.hooks.targetMissing", "method {0} not found (another game version?)", declaredName));
                Log(entry, "hooks: " + level + entry.Id + ": target " + declaredName + " not found (" + error + ")" + Consequence(entry));
                return entry;
            }

            // Reservations first: they are the rule the author has to hear about even when the handler is also wrong (AC-2.3, AC-2.4).
            ReservedTarget reserved = ReservedTargets.Find(target);
            if (reserved != null && reserved.Rejects(featureId, patch.Kind))
            {
                entry.Reserved = reserved;
                entry.Fail(PatchStatus.Rejected, "reserved: " + reserved.Describe(),
                    LocalizedOr("core.hooks.targetReserved", "target {0} is reserved for {1}", targetName, reserved.Holder) + ": " + reserved.Hint());
                Log(entry, "hooks: " + level + entry.Id + " is rejected: " + reserved.Describe() + Consequence(entry));
                return entry;
            }

            string handlerProblem = CheckHandler(handler, patch.Kind);
            if (handlerProblem != null)
            {
                entry.Fail(PatchStatus.Failed, "handler " + entry.HandlerName + ": " + handlerProblem,
                    LocalizedOr("core.hooks.patchFailed", "could not patch {0}", targetName));
                Log(entry, "hooks: " + level + entry.Id + ": handler " + entry.HandlerName + " " + handlerProblem + Consequence(entry));
                return entry;
            }

            if (!Backend.Supports(patch.Kind))
            {
                entry.Fail(PatchStatus.Rejected, "the patch backend (" + Backend.Name + ") does not support " + patch.Kind.ToString().ToLowerInvariant(),
                    LocalizedOr("core.hooks.patchFailed", "could not patch {0}", targetName));
                Log(entry, "hooks: " + level + entry.Id + ": the patch backend " + Backend + " cannot apply it" + Consequence(entry));
                return entry;
            }

            entry.Spec = new PatchSpec(featureId, entry.Id, target, targetName, handler, patch.Kind, patch.Priority, patch.Optional);

            // A second transpiler on a method is refused when it is applied (FR-2 item 5); say it now so the author sees both declarations.
            if (patch.Kind == PatchKind.Transpiler)
            {
                Entry other = OtherTranspiler(entry, false);
                if (other != null)
                {
                    entry.Detail = "transpiler conflict with " + other.FeatureId + " (" + other.Id + "); the one applied second is refused";
                    entry.ConflictWith = other.FeatureId;
                    ModLog.Warn(featureId, "hooks: " + entry.Id + ": " + other.FeatureId + " also declares a transpiler on " + targetName
                                           + "; only one can be applied");
                }
            }
            return entry;
        }

        /// <summary>The error of a required patch, the warning of an optional one.</summary>
        private static void Log(Entry entry, string message)
        {
            if (entry.Optional) ModLog.Warn(entry.FeatureId, message);
            else ModLog.Error(entry.FeatureId, message);
        }

        private static string Consequence(Entry entry)
        {
            if (entry.FeatureId == CoreId) return entry.Optional ? "; the core goes on without it" : "; the core's " + entry.PatchClass.Name + " stays off";
            return entry.Optional ? "; the feature starts without it" : "; the feature is unavailable";
        }

        // ---- applying (features) ----

        private static FeatureState ApplyFeatureCore(string featureId)
        {
            // A removal still waiting for its frame is done now, before anything is applied again.
            if (PendingRemovals.Contains(featureId)) RemoveFeature(featureId);

            Problem classProblem;
            if (ClassProblems.TryGetValue(featureId, out classProblem)) return Refuse(featureId, classProblem.Detail, classProblem.Reason, null);

            List<Entry> list;
            if (!ByFeature.TryGetValue(featureId, out list) || list.Count == 0)
            {
                AppliedFeatures.Add(featureId);
                return FeatureState.Enabled;
            }
            list = new List<Entry>(list);

            // Required patches that cannot be applied at all: refuse before touching the game. Their error is in the log already (AC-2.1).
            foreach (Entry entry in list)
            {
                if (!entry.Optional && entry.Spec == null) return Refuse(featureId, entry.Detail, entry.Reason, null);
            }

            bool late = AppliedFeatures.Contains(featureId);
            int applied = 0, skipped = 0;
            Stopwatch watch = Stopwatch.StartNew();
            foreach (Entry entry in list)
            {
                if (entry.Status == PatchStatus.Applied) continue;
                if (entry.Spec == null)
                {
                    skipped++;   // optional and broken: already reported
                    continue;
                }

                string error;
                if (!TryApplyEntry(entry, out error))
                {
                    if (!entry.Optional)
                    {
                        ModLog.Error(featureId, "hooks: patch " + entry.Id + " " + error + "; all patches of the feature were removed, it is unavailable");
                        return Refuse(featureId, entry.Detail, entry.Reason ?? LocalizedOr("core.hooks.patchFailed", "could not patch {0}", entry.TargetName), entry);
                    }
                    ModLog.Warn(featureId, "hooks: optional patch " + entry.Id + " " + error + "; the feature runs without it");
                    skipped++;
                    continue;
                }
                applied++;
            }

            AppliedFeatures.Add(featureId);
            if (applied > 0 || skipped > 0)
            {
                ModLog.Debug(featureId, "hooks: " + applied + " patch(es) applied" + (late ? " (declared while running)" : "")
                                        + (skipped > 0 ? ", " + skipped + " optional skipped" : "") + " in " + watch.ElapsedMilliseconds + " ms");
            }
            return FeatureState.Enabled;
        }

        /// <summary>
        /// Applies one entry: the transpiler rule, then the backend. On failure the entry carries the status and the reason, and
        /// <paramref name="error"/> the English text (starting with a verb: "failed: …", "is refused: …").
        /// </summary>
        private static bool TryApplyEntry(Entry entry, out string error)
        {
            if (entry.Kind == PatchKind.Transpiler)
            {
                Entry holder = OtherTranspiler(entry, true);
                if (holder != null)
                {
                    entry.ConflictWith = holder.FeatureId;
                    error = "is refused: " + holder.FeatureId + " already has a transpiler on " + entry.TargetName + " (" + holder.Id + ")";
                    entry.Fail(PatchStatus.Rejected, "transpiler conflict with " + holder.FeatureId + " (" + holder.Id + ")",
                        LocalizedOr("core.hooks.patchFailed", "could not patch {0}", entry.TargetName));
                    return false;
                }
            }

            string backendError;
            if (Backend.TryApply(entry.Spec, out backendError))
            {
                entry.Status = PatchStatus.Applied;
                entry.Detail = null;
                error = null;
                return true;
            }

            error = "failed: " + backendError;
            entry.Fail(PatchStatus.Failed, backendError, LocalizedOr("core.hooks.patchFailed", "could not patch {0}", entry.TargetName));
            return false;
        }

        /// <summary>Rolls back everything of the feature and makes it Unavailable with the reason (FR-2 item 2).</summary>
        private static FeatureState Refuse(string featureId, string detail, string reason, Entry failed)
        {
            Remove(featureId, failed == null ? "rolled back: the feature cannot start" : "rolled back: " + failed.Id + " failed");
            MarkUnavailable(featureId, reason ?? detail ?? "the patches could not be applied");
            return FeatureState.Unavailable;
        }

        /// <summary>Takes the feature's patches out of the game; the entries that were applied get <paramref name="detail"/>. Never throws.</summary>
        private static void Remove(string featureId, string detail)
        {
            try
            {
                if (string.IsNullOrEmpty(featureId)) return;
                PendingRemovals.Remove(featureId);
                AppliedFeatures.Remove(featureId);
                if (_backend == null) return;   // nothing was ever applied
                _backend.RemoveFeature(featureId);

                List<Entry> list;
                if (!ByFeature.TryGetValue(featureId, out list)) return;
                int removed = 0;
                foreach (Entry entry in list)
                {
                    if (entry.Status != PatchStatus.Applied) continue;
                    entry.Status = PatchStatus.Removed;
                    entry.Detail = detail;
                    removed++;
                }
                if (removed > 0) ModLog.Debug(featureId, "hooks: " + removed + " patch(es) " + detail);
            }
            catch (Exception ex)
            {
                ModLog.Error(CoreId, "hooks: removing the patches of '" + featureId + "' failed", ex);
            }
        }

        /// <summary>The applied (or, with <paramref name="appliedOnly"/> false, declared) transpiler of another feature on the same method.</summary>
        private static Entry OtherTranspiler(Entry entry, bool appliedOnly)
        {
            foreach (Entry other in Entries)
            {
                if (other == entry || other.Kind != PatchKind.Transpiler || other.Target == null || other.FeatureId == entry.FeatureId) continue;
                if (!SameMethod(other.Target, entry.Target)) continue;
                if (appliedOnly ? other.Status == PatchStatus.Applied : other.Spec != null) return other;
            }
            return null;
        }

        // ---- applying (core) ----

        /// <summary>Applies every core patch class that is not applied yet; each class is all or nothing for its required patches.</summary>
        private static void ApplyCore()
        {
            ScanCore();

            Stopwatch watch = Stopwatch.StartNew();
            int classes = 0, applied = 0, failedClasses = 0, total = 0;
            foreach (Type patchClass in CoreClasses)
            {
                if (BrokenCoreClasses.Contains(patchClass)) continue;

                List<Entry> list = EntriesOfClass(CoreId, patchClass);
                if (list.Count == 0) continue;
                classes++;
                total += list.Count;

                string failure = null;
                foreach (Entry entry in list)
                {
                    if (!entry.Optional && entry.Spec == null)
                    {
                        failure = entry.Detail;
                        break;
                    }
                }

                var done = new List<Entry>();
                if (failure == null)
                {
                    foreach (Entry entry in list)
                    {
                        if (entry.Status == PatchStatus.Applied)
                        {
                            applied++;
                            continue;
                        }
                        if (entry.Spec == null) continue;

                        string error;
                        if (TryApplyEntry(entry, out error))
                        {
                            done.Add(entry);
                            applied++;
                            continue;
                        }
                        if (entry.Optional)
                        {
                            ModLog.Warn(CoreId, "hooks: optional core patch " + entry.Id + " " + error + "; the core goes on without it");
                            continue;
                        }
                        failure = entry.Id + " " + error;
                        break;
                    }
                }

                if (failure == null) continue;

                // The class is one service's patches: take back what was applied of it, keep the rest of the core.
                foreach (Entry entry in done)
                {
                    Backend.Remove(entry.Spec);
                    entry.Status = PatchStatus.Removed;
                    entry.Detail = "rolled back: another patch of " + patchClass.Name + " failed";
                    applied--;
                }
                BrokenCoreClasses.Add(patchClass);
                failedClasses++;
                ModLog.Error(CoreId, "hooks: core patch class " + patchClass.FullName + " is off for this session: " + failure);
            }

            if (classes > 0)
            {
                ModLog.Info(CoreId, "hooks: core patches: " + applied + " of " + total + " applied in " + classes + " class(es)"
                                    + (failedClasses > 0 ? ", " + failedClasses + " class(es) off" : "") + ", " + watch.ElapsedMilliseconds
                                    + " ms; backend " + Backend);
            }
            AppliedFeatures.Add(CoreId);
        }

        /// <summary>
        /// Finds the core's patch classes: types of this assembly in <c>PTMod.Core…</c> (not the pure logic) with <c>[ModPatch]</c> methods.
        /// Runs once; types that cannot be loaded (their patches need the missing 0Harmony.dll) are skipped with one warning.
        /// </summary>
        private static void ScanCore()
        {
            if (_coreScanned) return;
            _coreScanned = true;

            Type[] types;
            try
            {
                types = typeof(ModHooks).Assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException ex)
            {
                types = ex.Types ?? new Type[0];
                ModLog.Warn(CoreId, "hooks: some types of PTMod.dll could not be loaded; their core patches are not found");
            }

            var found = new List<Type>();
            foreach (Type type in types)
            {
                if (type == null) continue;
                try
                {
                    string ns = type.Namespace;
                    if (ns == null || !(ns == "PTMod.Core" || ns.StartsWith("PTMod.Core.", StringComparison.Ordinal))) continue;
                    if (ns == "PTMod.Core.Logic" || ns.StartsWith("PTMod.Core.Logic.", StringComparison.Ordinal)) continue;
                    if (type.Name.IndexOf('<') >= 0 || type.IsInterface || type.IsEnum) continue;

                    foreach (MethodInfo method in type.GetMethods(AnyStatic))
                    {
                        if (!method.IsDefined(typeof(ModPatchAttribute), false)) continue;
                        found.Add(type);
                        break;
                    }
                }
                catch (Exception ex)
                {
                    ModLog.Warn(CoreId, "hooks: type " + type.FullName + " is skipped by the patch scan: " + ex.GetType().Name + ": " + ex.Message);
                }
            }

            found.Sort((a, b) => string.CompareOrdinal(a.FullName, b.FullName));
            foreach (Type type in found)
            {
                CoreClasses.Add(type);
                int added;
                try
                {
                    added = Read(CoreId, type);
                }
                catch (Exception ex)
                {
                    ModLog.Error(CoreId, "hooks: cannot read the core patch class " + type.FullName, ex);
                    BrokenCoreClasses.Add(type);
                    continue;
                }
                if (added == 0) BrokenCoreClasses.Add(type);
            }
        }

        private static List<Entry> EntriesOfClass(string featureId, Type patchClass)
        {
            var list = new List<Entry>();
            List<Entry> all;
            if (!ByFeature.TryGetValue(featureId, out all)) return list;
            foreach (Entry entry in all) if (entry.PatchClass == patchClass) list.Add(entry);
            return list;
        }

        // ---- lifecycle (core init stages and the registry's events) ----

        /// <summary>Declare stage: the backend, the threshold, the registry's events, the fault sink, the quit summaries.</summary>
        [CoreInit(CoreInitStage.Declare)]
        internal static void Start()
        {
            if (_started) return;
            _started = true;

            int threshold = FaultThreshold;
            IPatchBackend backend = Backend;
            ModFeatures.FeatureRegistered += OnFeatureRegistered;
            ModFeatures.FeatureActivating += OnFeatureActivating;
            ModFeatures.FeatureDeactivated += OnFeatureDeactivated;
            ModFeatures.FaultSink = ReportFault;
            PTModHost.ApplicationQuitting += FlushFaultSummaries;

            if (_backendProblem == null) ModLog.Debug(CoreId, "hooks: backend " + backend + ", hooks.faultThreshold = " + threshold);
        }

        /// <summary>Patch stage: the core's own patches (Harmony id <c>ptmod.mod-framework</c>), before any feature registers.</summary>
        [CoreInit(CoreInitStage.Patch)]
        internal static void ApplyCorePatches()
        {
            ApplyFeature(CoreId);
        }

        /// <summary>AfterRegister stage: one line about what the features declared (the problems were logged where they were found).</summary>
        [CoreInit(CoreInitStage.AfterRegister, Optional = true)]
        internal static void SummarizeDeclarations()
        {
            int features = 0, patches = 0, problems = 0;
            foreach (KeyValuePair<string, List<Entry>> pair in ByFeature)
            {
                if (pair.Key == CoreId) continue;
                features++;
                patches += pair.Value.Count;
                foreach (Entry entry in pair.Value) if (entry.Spec == null) problems++;
            }
            if (features > 0)
                ModLog.Debug(CoreId, "hooks: " + patches + " patch(es) declared by " + features + " feature(s), " + problems + " cannot be applied");
        }

        /// <summary>Rollback stage (only when Init failed): every patch of the mod goes, the core's last. A no-op when nothing was applied.</summary>
        [CoreInit(CoreInitStage.Rollback)]
        internal static void Rollback()
        {
            if (_backend == null) return;
            foreach (string featureId in new List<string>(AppliedFeatures))
            {
                if (featureId != CoreId) RemoveFeature(featureId);
            }
            foreach (string featureId in new List<string>(PendingRemovals)) RemoveFeature(featureId);
            RemoveFeature(CoreId);
        }

        private static void OnFeatureRegistered(FeatureInfo info)
        {
            // A required patch that cannot be applied makes the feature Unavailable before it starts; its error is already in the log.
            Problem classProblem;
            if (ClassProblems.TryGetValue(info.Id, out classProblem))
            {
                MarkUnavailable(info.Id, classProblem.Reason);
                return;
            }

            List<Entry> list;
            if (!ByFeature.TryGetValue(info.Id, out list)) return;
            foreach (Entry entry in list)
            {
                if (entry.Optional || entry.Spec != null) continue;
                MarkUnavailable(info.Id, entry.Reason ?? entry.Detail);
                return;
            }
        }

        private static void OnFeatureActivating(FeatureInfo info)
        {
            ApplyFeature(info.Id);
        }

        private static void OnFeatureDeactivated(FeatureInfo info)
        {
            if (!InGuardedCall || PTModHost.Instance == null)
            {
                RemoveFeature(info.Id);
                return;
            }

            // Inside a patch body or a handler (a breaker tripped there): Harmony must not rebuild a method that may be running right now.
            // The bodies are already skipped (the feature is not active); the patches go on the next frame.
            if (!PendingRemovals.Contains(info.Id)) PendingRemovals.Add(info.Id);
            if (!_frameHooked)
            {
                _frameHooked = true;
                PTModHost.FrameUpdate += RemovePending;
            }
        }

        private static void RemovePending()
        {
            PTModHost.FrameUpdate -= RemovePending;
            _frameHooked = false;
            foreach (string featureId in new List<string>(PendingRemovals))
            {
                if (ModFeatures.IsActive(featureId))
                {
                    PendingRemovals.Remove(featureId);   // active again: ApplyFeature already redid its patches
                    continue;
                }
                RemoveFeature(featureId);
            }
        }

        // ---- target resolution ----

        /// <summary>
        /// Finds the target like Harmony's <c>AccessTools.Method</c> (with <c>AccessTools.Inner</c> for <paramref name="inner"/>): members of
        /// every visibility, static or instance, declared on the type or else on the nearest base type that has the name. Without
        /// <paramref name="args"/> the name must be unique there; with them the parameter types must match exactly. Null and an English
        /// <paramref name="error"/> when not found, ambiguous, abstract or generic.
        /// </summary>
        internal static MethodBase ResolveTarget(Type type, string inner, string method, Type[] args, out string error)
        {
            error = null;
            if (type == null)
            {
                error = "no target type";
                return null;
            }
            if (string.IsNullOrEmpty(method))
            {
                error = "no method name";
                return null;
            }

            if (!string.IsNullOrEmpty(inner))
            {
                foreach (string part in inner.Split('.', '+'))
                {
                    Type nested = FindNested(type, part);
                    if (nested == null)
                    {
                        error = "no nested type '" + part + "' in " + type.FullName;
                        return null;
                    }
                    type = nested;
                }
            }

            if (method == ".ctor" || method == ".cctor")
            {
                bool isStatic = method == ".cctor";
                var constructors = new List<MethodBase>();
                foreach (ConstructorInfo ctor in type.GetConstructors(AnyMethod))
                {
                    if (ctor.IsStatic == isStatic && (args == null || SameParameters(ctor, args))) constructors.Add(ctor);
                }
                return Single(constructors, type, method, args, out error);
            }

            for (Type current = type; current != null; current = current.BaseType)
            {
                var candidates = new List<MethodBase>();
                foreach (MethodInfo candidate in current.GetMethods(AnyMethod))
                {
                    if (candidate.Name == method && (args == null || SameParameters(candidate, args))) candidates.Add(candidate);
                }
                if (candidates.Count == 0) continue;

                MethodBase found = Single(candidates, current, method, args, out error);
                if (found == null) return null;
                if (found.IsAbstract)
                {
                    error = NameOf(found) + " is abstract (no body to patch)";
                    return null;
                }
                if (found.IsGenericMethodDefinition)
                {
                    error = NameOf(found) + " is generic";
                    return null;
                }
                return found;
            }

            error = "no method '" + method + "'" + (args == null ? "" : " with parameters (" + TypeList(args) + ")") + " in " + type.FullName
                    + " or its base types";
            return null;
        }

        private static MethodBase Single(List<MethodBase> candidates, Type type, string method, Type[] args, out string error)
        {
            error = null;
            if (candidates.Count == 1) return candidates[0];
            if (candidates.Count == 0)
            {
                error = "no " + method + (args == null ? "" : "(" + TypeList(args) + ")") + " in " + type.FullName;
                return null;
            }
            error = candidates.Count + " overloads of " + type.Name + "." + method + "; set ArgTypes";
            return null;
        }

        private static Type FindNested(Type type, string name)
        {
            for (Type current = type; current != null; current = current.BaseType)
            {
                Type nested = current.GetNestedType(name, BindingFlags.Public | BindingFlags.NonPublic);
                if (nested != null) return nested;
            }
            return null;
        }

        private static bool SameParameters(MethodBase method, Type[] args)
        {
            ParameterInfo[] parameters = method.GetParameters();
            if (parameters.Length != args.Length) return false;
            for (int i = 0; i < parameters.Length; i++)
            {
                if (parameters[i].ParameterType != args[i]) return false;
            }
            return true;
        }

        private static bool SameMethod(MethodBase a, MethodBase b)
        {
            return a == b || (a.Module == b.Module && a.MetadataToken == b.MetadataToken);
        }

        /// <summary>Light checks of the handler (Harmony checks the rest when it applies): static, not generic, the return type of its kind.</summary>
        private static string CheckHandler(MethodInfo handler, PatchKind kind)
        {
            if (!handler.IsStatic) return "is not static";
            if (handler.ContainsGenericParameters) return "is generic";

            Type returns = handler.ReturnType;
            switch (kind)
            {
                case PatchKind.Prefix:
                    return returns == typeof(void) || returns == typeof(bool) ? null : "must return void or bool (a prefix)";
                case PatchKind.Finalizer:
                    return returns == typeof(void) || returns == typeof(Exception) ? null : "must return void or Exception (a finalizer)";
                case PatchKind.Transpiler:
                    return typeof(IEnumerable).IsAssignableFrom(returns) ? null : "must return IEnumerable<CodeInstruction> (a transpiler)";
                default:
                    return null;
            }
        }

        /// <summary><c>Class.Method</c> with the nesting chain: <c>Contract.CpuOffer.DefineChance</c>.</summary>
        internal static string NameOf(MethodBase method)
        {
            if (method == null) return "?";
            return TypeName(method.DeclaringType) + "." + method.Name;
        }

        private static string TypeName(Type type)
        {
            if (type == null) return "?";
            return type.DeclaringType == null ? type.Name : TypeName(type.DeclaringType) + "." + type.Name;
        }

        /// <summary>The parameter types of the target: <c>(Cpu, Boolean, Boolean, Int32, Single)</c>.</summary>
        private static string SignatureOf(MethodBase method)
        {
            ParameterInfo[] parameters = method.GetParameters();
            var types = new Type[parameters.Length];
            for (int i = 0; i < parameters.Length; i++) types[i] = parameters[i].ParameterType;
            return "(" + TypeList(types) + ")";
        }

        private static string DeclaredTargetName(ModPatchAttribute patch)
        {
            string type = patch.Target == null ? "?" : TypeName(patch.Target);
            if (!string.IsNullOrEmpty(patch.InnerType)) type += "." + patch.InnerType.Replace('+', '.');
            return type + "." + (patch.Method ?? "?");
        }

        private static string TypeList(Type[] args)
        {
            var names = new string[args.Length];
            for (int i = 0; i < args.Length; i++) names[i] = args[i] == null ? "null" : args[i].Name;
            return string.Join(", ", names);
        }

        // ---- internals ----

        /// <summary>
        /// Creates the Harmony backend from a method of its own that is never inlined: when 0Harmony.dll is missing the failure happens when
        /// that method is compiled, i.e. at the call below, inside the try.
        /// </summary>
        private static IPatchBackend CreateBackend()
        {
            try
            {
                return CreateHarmonyBackend();
            }
            catch (Exception ex)
            {
                _backendProblem = "0Harmony.dll could not be loaded: " + LogFormatter.DescribeException(ex);
                ModLog.Error(CoreId, "hooks: no patch backend (" + _backendProblem + "); the core services that need patches are off and "
                                     + "every feature that declares a patch is unavailable");
                return new UnavailableBackend(_backendProblem);
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static IPatchBackend CreateHarmonyBackend()
        {
            return new HarmonyBackend();
        }

        private static void Add(Entry entry)
        {
            Entries.Add(entry);
            List<Entry> list;
            if (!ByFeature.TryGetValue(entry.FeatureId, out list))
            {
                list = new List<Entry>();
                ByFeature[entry.FeatureId] = list;
            }
            list.Add(entry);
        }

        private static string UniqueId(string id)
        {
            string candidate = id;
            for (int n = 2; ; n++)
            {
                bool taken = false;
                foreach (Entry entry in Entries)
                {
                    if (entry.Id != candidate) continue;
                    taken = true;
                    break;
                }
                if (!taken) return candidate;
                candidate = id + "#" + n;
            }
        }

        private static void MarkUnavailable(string featureId, string reason)
        {
            if (featureId == CoreId) return;
            ModFeatures.MarkUnavailable(featureId, reason);
        }

        /// <summary>The text of a ModL10n key in the current language, or the English format when the tables do not have it.</summary>
        private static string LocalizedOr(string key, string english, params object[] args)
        {
            try
            {
                if (ModL10n.Has(key)) return ModL10n.L(key, args);
            }
            catch (Exception)
            {
                // ModL10n does not throw; the English text is the answer either way.
            }
            try
            {
                return string.Format(english, args);
            }
            catch (FormatException)
            {
                return english;
            }
        }

        /// <summary>Forgets every declaration, status and fault (the offline harness only: the game never resets the registry).</summary>
        internal static void ResetForTests(IPatchBackend backend)
        {
            Entries.Clear();
            ByFeature.Clear();
            DeclaredClasses.Clear();
            ClassProblems.Clear();
            AppliedFeatures.Clear();
            PendingRemovals.Clear();
            CoreClasses.Clear();
            BrokenCoreClasses.Clear();
            _coreScanned = false;
            _backend = backend;
            _backendProblem = null;
            ResetFaultsForTests();
        }

        /// <summary>A problem of a whole declaration (a patch class of another feature, an unreadable class).</summary>
        private sealed class Problem
        {
            public Problem(string detail, string reason)
            {
                Detail = detail;
                Reason = reason;
            }

            /// <summary>English, for the log.</summary>
            public string Detail { get; }

            /// <summary>For the player (<c>core.feature.reason.unavailable</c>, {0}).</summary>
            public string Reason { get; }
        }

        /// <summary>One declared patch and what became of it.</summary>
        private sealed class Entry
        {
            public string FeatureId;
            public string Id;
            public Type PatchClass;
            public string TargetName;
            public string Signature;
            public string HandlerName;
            public PatchKind Kind;
            public int Priority;
            public bool Optional;
            public MethodBase Target;
            public MethodInfo Handler;

            /// <summary>Null when the declaration cannot be applied (missing target, reserved, bad handler, unsupported kind).</summary>
            public PatchSpec Spec;

            public PatchStatus Status = PatchStatus.Declared;
            public string Detail;
            public string Reason;
            public ReservedTarget Reserved;
            public string ConflictWith;

            public void Fail(PatchStatus status, string detail, string reason)
            {
                Status = status;
                Detail = detail;
                Reason = reason;
            }

            public PatchReport ToReport()
            {
                return new PatchReport(FeatureId, Id, TargetName, Signature, HandlerName, Kind, Priority, Optional, Status, Detail, Reserved, ConflictWith);
            }

            public override string ToString()
            {
                var sb = new StringBuilder(Id);
                sb.Append(' ').Append(Status);
                if (Detail != null) sb.Append(": ").Append(Detail);
                return sb.ToString();
            }
        }
    }
}
