using System;

namespace PTMod.Core
{
    /// <summary>
    /// The steps of <see cref="ModBootstrap.Init"/> at which a core service may run its own start code (<see cref="CoreInitAttribute"/>).
    /// They run in this order; the bootstrap's own steps sit between them as noted.
    /// </summary>
    internal enum CoreInitStage
    {
        /// <summary>
        /// After the infrastructure (settings, log, profile, L10n, content) and before any feature exists: declare the shared registries -
        /// the 27 channels of the channel registry and the prefix table from <c>core.json</c> (D21, D28), the core predicates of
        /// <c>ModConditions</c>; subscribe to <see cref="ModFeatures"/>' lifecycle events and set <see cref="ModFeatures.FaultSink"/>.
        /// The feature registry is built right after this stage.
        /// </summary>
        Declare = 0,

        /// <summary>The feature registry exists (states known, nothing registered): apply the core's own patches (Harmony id <c>ptmod.mod-framework</c>).</summary>
        Patch = 1,

        /// <summary>
        /// Every enabled feature has run <c>Register</c>: validate what they declared (reserved targets, transpiler conflicts) and apply their
        /// patches, all or nothing per feature (a failure calls <see cref="ModFeatures.MarkUnavailable"/>). <see cref="PTModHost"/> is created
        /// and the enabled features get <c>OnEnabled</c> right after this stage.
        /// </summary>
        AfterRegister = 2,

        /// <summary>The features run: the Boot stage of the self-check (patch targets, undeclared channels, rejected ids).</summary>
        SelfCheckBoot = 3,

        /// <summary>Last step of a successful start: the host exists and listens to scene loads; nothing of the game scene exists yet.</summary>
        HostReady = 4,

        /// <summary>
        /// Only when <c>Init</c> failed: undo whatever an earlier stage did (remove the core's patches). Called after every feature was
        /// switched off, in reverse order, even for services whose earlier stage never ran - a rollback method must be a no-op then.
        /// </summary>
        Rollback = 5
    }

    /// <summary>
    /// Marks a static method of a core service (namespace <c>PTMod.Core…</c>) as part of the mod's start:
    /// <c>[CoreInit(CoreInitStage.Declare)] internal static void DeclareChannels() { … }</c>. The bootstrap finds such methods by reflection
    /// in the same pass that finds the features and calls them in the order (stage, <see cref="Order"/>, type name, method name), so a new
    /// service plugs itself into <see cref="ModBootstrap.Init"/> without editing the bootstrap.
    /// <list type="bullet">
    /// <item>The method is static, has no parameters, no generic arguments and returns void; anything else is skipped with a warning, as is a
    /// method of a feature (<c>PTMod.Features.*</c>) - features start through <c>IModFeature.Register</c>.</item>
    /// <item>An exception from a required method stops the start: one <c>Init failed</c> error, the features are switched off, the
    /// <see cref="CoreInitStage.Rollback"/> methods run and the game stays vanilla (FR-1 item 4). An <see cref="Optional"/> method only logs
    /// the error and the start goes on without that service.</item>
    /// <item>Each method runs once per process (<c>Init</c> is idempotent), on the main thread, before the first scene is loaded.</item>
    /// </list>
    /// </summary>
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
    internal sealed class CoreInitAttribute : Attribute
    {
        public CoreInitAttribute(CoreInitStage stage, int order = 0)
        {
            Stage = stage;
            Order = order;
        }

        public CoreInitStage Stage { get; }

        /// <summary>Position inside the stage, smaller first; equal orders go by type and method name.</summary>
        public int Order { get; }

        /// <summary>A failure of this method is logged and the start continues (default: the start fails, the mod stays off).</summary>
        public bool Optional { get; set; }
    }
}
