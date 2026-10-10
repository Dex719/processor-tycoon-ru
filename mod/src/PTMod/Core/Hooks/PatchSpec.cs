using System;
using System.Reflection;

namespace PTMod.Core
{
    /// <summary>
    /// One resolved patch: what a backend applies. Built by <c>ModHooks</c> from a <see cref="ModPatchAttribute"/> once the target and the
    /// handler are known; immutable.
    /// </summary>
    public sealed class PatchSpec
    {
        internal PatchSpec(string featureId, string id, MethodBase target, string targetName, MethodInfo handler, PatchKind kind, int priority,
            bool optional)
        {
            FeatureId = featureId;
            Id = id;
            Target = target;
            TargetName = targetName;
            Handler = handler;
            Kind = kind;
            Priority = priority;
            Optional = optional;
        }

        /// <summary>The feature that declared the patch (<c>mod-framework</c> for the core); its Harmony id is <c>ptmod.&lt;feature-id&gt;</c>.</summary>
        public string FeatureId { get; }

        /// <summary><c>&lt;feature&gt;/&lt;Class.Method&gt;/&lt;kind&gt;</c>, for example <c>mod-framework/Market.CalculateRatings/postfix</c>.</summary>
        public string Id { get; }

        /// <summary>The game method that is patched.</summary>
        public MethodBase Target { get; }

        /// <summary><c>Class.Method</c> of the target (nested types joined with dots: <c>Contract.CpuOffer.DefineChance</c>).</summary>
        public string TargetName { get; }

        /// <summary>The static patch method.</summary>
        public MethodInfo Handler { get; }

        public PatchKind Kind { get; }

        public int Priority { get; }

        public bool Optional { get; }

        public override string ToString()
        {
            return Id;
        }
    }

    /// <summary>Where a declared patch stands (the Diagnostics tab and the self-check read it from <c>ModHooks.Report</c>).</summary>
    public enum PatchStatus
    {
        /// <summary>Valid and waiting: the feature is not running (or has not started yet).</summary>
        Declared,

        /// <summary>The patch is in the game.</summary>
        Applied,

        /// <summary>Could not be applied: the target is missing or ambiguous, the handler is wrong, or the backend refused it.</summary>
        Failed,

        /// <summary>Refused by the rules: a reserved target, a second transpiler on one method, a kind the backend does not support.</summary>
        Rejected,

        /// <summary>Was applied and has been removed (the feature stopped, or its other patches failed and it was rolled back).</summary>
        Removed
    }

    /// <summary>
    /// A snapshot of one declared patch for the Diagnostics tab and the self-check: what was declared, where it stands and why.
    /// <see cref="Detail"/> is English for the log; the hub shows <see cref="StatusKey"/> and, for a reserved target, <see cref="ReservedTarget.Hint"/>.
    /// </summary>
    public sealed class PatchReport
    {
        internal PatchReport(string featureId, string id, string target, string signature, string handler, PatchKind kind, int priority,
            bool optional, PatchStatus status, string detail, ReservedTarget reserved, string conflictWith)
        {
            FeatureId = featureId;
            Id = id;
            Target = target;
            Signature = signature;
            Handler = handler;
            Kind = kind;
            Priority = priority;
            Optional = optional;
            Status = status;
            Detail = detail;
            Reserved = reserved;
            ConflictWith = conflictWith;
        }

        public string FeatureId { get; }

        /// <summary>The patch id (<see cref="PatchSpec.Id"/>), also for patches whose target could not be resolved.</summary>
        public string Id { get; }

        /// <summary><c>Class.Method</c> of the target as declared.</summary>
        public string Target { get; }

        /// <summary>Parameter types of the resolved target, <c>(Cpu, Boolean, Boolean, Int32, Single)</c>; the declared <c>ArgTypes</c> or null when unresolved.</summary>
        public string Signature { get; }

        /// <summary><c>Class.Method</c> of the patch method.</summary>
        public string Handler { get; }

        public PatchKind Kind { get; }

        public int Priority { get; }

        public bool Optional { get; }

        public PatchStatus Status { get; }

        /// <summary>English: why the patch failed or was rejected, or what happened to it last; null when there is nothing to say.</summary>
        public string Detail { get; }

        /// <summary>The reservation that rejected the patch (AC-2.3, AC-2.4), or null.</summary>
        public ReservedTarget Reserved { get; }

        /// <summary>The feature whose transpiler holds the method when this one was refused as the second transpiler (FR-2 item 5), or null.</summary>
        public string ConflictWith { get; }

        /// <summary>ModL10n key of the status column: <c>core.diag.patches.status.applied/failed/rejected</c>; null for Declared and Removed.</summary>
        public string StatusKey
        {
            get
            {
                switch (Status)
                {
                    case PatchStatus.Applied: return "core.diag.patches.status.applied";
                    case PatchStatus.Failed: return "core.diag.patches.status.failed";
                    case PatchStatus.Rejected: return "core.diag.patches.status.rejected";
                    default: return null;
                }
            }
        }

        /// <summary>A problem the self-check reports: a required patch that failed or was rejected, or any patch refused by a reservation or a conflict.</summary>
        public bool IsProblem
        {
            get
            {
                if (Status == PatchStatus.Failed) return !Optional;
                return Status == PatchStatus.Rejected && (!Optional || Reserved != null || ConflictWith != null);
            }
        }

        public override string ToString()
        {
            return Id + " " + Status.ToString().ToLowerInvariant() + (Detail == null ? "" : ": " + Detail);
        }
    }
}
