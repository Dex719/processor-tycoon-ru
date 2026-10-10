using System;

namespace PTMod.Core
{
    /// <summary>The kind of a patch method, as Harmony knows them.</summary>
    public enum PatchKind
    {
        /// <summary>Runs before the original. Returns void, or bool: false skips the original (and the prefixes after it).</summary>
        Prefix,

        /// <summary>Runs after the original returned normally; may read and change <c>__result</c>.</summary>
        Postfix,

        /// <summary>Rewrites the IL of the original (<c>IEnumerable&lt;CodeInstruction&gt;</c> in and out). One per method (FR-2 item 5).</summary>
        Transpiler,

        /// <summary>Runs after the original in every case, also when it threw; returns void or the exception to rethrow (null swallows it).</summary>
        Finalizer
    }

    /// <summary>
    /// Declares a patch method of the mod (FR-2 item 1): <c>[ModPatch(typeof(Market), "CalculateRatings", PatchKind.Postfix)]</c> on a static
    /// method of a patch class. A feature hands the class to <see cref="FeatureContext.Patch"/> in <c>Register</c>; the core's own patch classes
    /// (namespace <c>PTMod.Core…</c>) are found by <c>ModHooks</c> itself. Nothing is patched at declaration: <c>ModHooks</c> resolves the target,
    /// checks the reserved targets and the handler, and applies the patches of a feature all or nothing when the feature starts.
    /// <list type="bullet">
    /// <item>The target is looked up like <c>AccessTools.Method</c>: methods of every visibility, static or instance, declared on the type or,
    /// failing that, on its base types. With <see cref="ArgTypes"/> null the name must be unique on the type that declares it (otherwise the
    /// declaration fails as ambiguous); <c>".ctor"</c> and <c>".cctor"</c> name the constructors.</item>
    /// <item><see cref="InnerType"/> reaches a nested type that cannot be named in C#, like <c>AccessTools.Inner</c>:
    /// <c>[ModPatch(typeof(Contract), "DefineChance", PatchKind.Postfix, InnerType = "CpuOffer")]</c>; a dotted path goes deeper.</item>
    /// <item>The same method may carry several attributes (one handler on <c>Company.EarnMoney</c> and <c>AICompany.EarnMoney</c>).</item>
    /// <item>The body is the patch author's responsibility: wrap it in <c>ModHooks.Guard</c> (a prefix returns true on failure, a postfix
    /// leaves <c>__result</c> alone, a value replaced in a prefix is restored in a finalizer).</item>
    /// </list>
    /// </summary>
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = true, Inherited = false)]
    public sealed class ModPatchAttribute : Attribute
    {
        /// <summary>Harmony's <c>Priority.Normal</c>.</summary>
        public const int NormalPriority = 400;

        public ModPatchAttribute(Type target, string method, PatchKind kind)
        {
            Target = target;
            Method = method;
            Kind = kind;
            Priority = NormalPriority;
        }

        /// <summary>The type that declares the target (or that the nested <see cref="InnerType"/> lives in).</summary>
        public Type Target { get; }

        /// <summary>The name of the target method.</summary>
        public string Method { get; }

        public PatchKind Kind { get; }

        /// <summary>Exact parameter types of the target (<c>typeof(float).MakeByRefType()</c> for <c>ref float</c>); null - the name is unique.</summary>
        public Type[] ArgTypes { get; set; }

        /// <summary>A nested type of <see cref="Target"/> that declares the method: <c>"CpuOffer"</c> of <c>Contract</c>; <c>"A.B"</c> for deeper nesting.</summary>
        public string InnerType { get; set; }

        /// <summary>Harmony priority: higher runs earlier among the prefixes and postfixes of one method (<c>Priority.Last</c> = 0, <c>First</c> = 800).</summary>
        public int Priority { get; set; }

        /// <summary>
        /// A failure of this patch (missing target, apply error, transpiler conflict) is a warning and the feature still starts without it; a
        /// required patch that fails makes the feature Unavailable. A patch on a target reserved for someone else is rejected either way.
        /// </summary>
        public bool Optional { get; set; }
    }
}
