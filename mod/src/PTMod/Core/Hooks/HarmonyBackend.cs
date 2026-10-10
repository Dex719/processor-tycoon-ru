using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Reflection;
using HarmonyLib;
using PTMod.Core.Logic.Log;

namespace PTMod.Core
{
    /// <summary>
    /// The Harmony 2 backend (decision 0002). Each feature patches under its own Harmony instance <c>ptmod.&lt;feature-id&gt;</c>
    /// (<c>ptmod.mod-framework</c> for the core), so removing a feature's patches never touches another feature's patches on the same method.
    /// Patches are applied one handler per <c>Harmony.Patch</c> call, imperatively, the way spike 0002 proved them (private and nested targets,
    /// <c>__state</c>, finalizers, transpilers).
    /// <para>
    /// This is the only class of the core that names Harmony types. <c>ModHooks</c> creates it from a separate method that is not inlined,
    /// so a missing <c>0Harmony.dll</c> fails there, inside a try/catch, and the rest of the mod loads and runs without patches.
    /// </para>
    /// </summary>
    internal sealed class HarmonyBackend : IPatchBackend
    {
        private readonly Dictionary<string, Harmony> _instances = new Dictionary<string, Harmony>(StringComparer.Ordinal);
        private readonly string _name;

        internal HarmonyBackend()
        {
            // Touching the assembly here makes a missing or unloadable 0Harmony.dll fail at creation, not at the first patch.
            Version version = typeof(Harmony).Assembly.GetName().Version;
            _name = "Harmony " + (version == null ? "?" : version.ToString());
        }

        /// <summary>The Harmony owner id of a feature's patches.</summary>
        internal static string HarmonyIdOf(string featureId)
        {
            return ModHooks.HarmonyIdPrefix + featureId;
        }

        public string Name
        {
            get { return _name; }
        }

        public bool Supports(PatchKind kind)
        {
            return kind == PatchKind.Prefix || kind == PatchKind.Postfix || kind == PatchKind.Transpiler || kind == PatchKind.Finalizer;
        }

        public bool TryApply(PatchSpec spec, out string error)
        {
            error = null;
            if (spec == null || spec.Target == null || spec.Handler == null)
            {
                error = "the patch has no target or no handler";
                return false;
            }

            Harmony harmony;
            try
            {
                harmony = InstanceOf(spec.FeatureId);
                var method = new HarmonyMethod(spec.Handler) { priority = spec.Priority };
                switch (spec.Kind)
                {
                    case PatchKind.Prefix:
                        harmony.Patch(spec.Target, prefix: method);
                        break;
                    case PatchKind.Postfix:
                        harmony.Patch(spec.Target, postfix: method);
                        break;
                    case PatchKind.Transpiler:
                        harmony.Patch(spec.Target, transpiler: method);
                        break;
                    case PatchKind.Finalizer:
                        harmony.Patch(spec.Target, finalizer: method);
                        break;
                    default:
                        error = "unknown patch kind " + spec.Kind;
                        return false;
                }
            }
            catch (Exception ex)
            {
                error = Describe(ex);
                Purge(spec);
                return false;
            }

            // Trust Harmony's registry, not only the absence of an exception (spike 0002 does the same).
            try
            {
                if (IsListed(spec, harmony.Id)) return true;
                error = "Harmony.GetPatchInfo does not list the patch after Patch()";
            }
            catch (Exception ex)
            {
                error = "Harmony.GetPatchInfo failed after Patch(): " + Describe(ex);
            }
            Purge(spec);
            return false;
        }

        public void Remove(PatchSpec spec)
        {
            if (spec == null || spec.Target == null || spec.Handler == null) return;

            Harmony harmony;
            if (!_instances.TryGetValue(spec.FeatureId ?? string.Empty, out harmony)) return;
            try
            {
                harmony.Unpatch(spec.Target, spec.Handler);
            }
            catch (Exception ex)
            {
                ModLog.Error(spec.FeatureId, "hooks: cannot remove the patch " + spec.Id, ex);
            }
        }

        public void RemoveFeature(string featureId)
        {
            Harmony harmony;
            if (string.IsNullOrEmpty(featureId) || !_instances.TryGetValue(featureId, out harmony)) return;
            try
            {
                // Never UnpatchAll(null): that would remove every owner's patches, the game's other mods included.
                harmony.UnpatchAll(harmony.Id);
            }
            catch (Exception ex)
            {
                ModLog.Error(featureId, "hooks: cannot remove the patches of " + harmony.Id, ex);
            }
        }

        public override string ToString()
        {
            return _name;
        }

        // ---- internals ----

        private Harmony InstanceOf(string featureId)
        {
            string key = featureId ?? string.Empty;
            Harmony harmony;
            if (!_instances.TryGetValue(key, out harmony))
            {
                harmony = new Harmony(HarmonyIdOf(key));
                _instances[key] = harmony;
            }
            return harmony;
        }

        private static bool IsListed(PatchSpec spec, string owner)
        {
            Patches info = Harmony.GetPatchInfo(spec.Target);
            if (info == null) return false;

            ReadOnlyCollection<Patch> list;
            switch (spec.Kind)
            {
                case PatchKind.Prefix:
                    list = info.Prefixes;
                    break;
                case PatchKind.Postfix:
                    list = info.Postfixes;
                    break;
                case PatchKind.Transpiler:
                    list = info.Transpilers;
                    break;
                default:
                    list = info.Finalizers;
                    break;
            }
            if (list == null) return false;

            foreach (Patch patch in list)
            {
                if (patch != null && patch.owner == owner && patch.PatchMethod == spec.Handler) return true;
            }
            return false;
        }

        /// <summary>
        /// A <c>Patch</c> that threw may have left the handler in Harmony's registry for the method (the registry is updated before the new
        /// method body is built); removing it keeps the next patch of that method, by anyone, from failing on it.
        /// </summary>
        private void Purge(PatchSpec spec)
        {
            Harmony harmony;
            if (!_instances.TryGetValue(spec.FeatureId ?? string.Empty, out harmony)) return;
            try
            {
                harmony.Unpatch(spec.Target, spec.Handler);
            }
            catch (Exception)
            {
                // Nothing was registered, or Harmony cannot rebuild the method either way; the caller reports the original error.
            }
        }

        /// <summary>The exception chain without the IL dump Harmony appends to its messages.</summary>
        private static string Describe(Exception ex)
        {
            Exception cause = ex is TargetInvocationException && ex.InnerException != null ? ex.InnerException : ex;
            string text = LogFormatter.DescribeException(cause);
            int cut = text.IndexOf('\n');
            return cut < 0 ? text : text.Substring(0, cut).TrimEnd();
        }
    }
}
