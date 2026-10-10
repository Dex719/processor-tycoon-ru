using System;
using System.Collections.Generic;
using PTMod.Core.Logic.Features;

namespace PTMod.Core
{
    /// <summary>
    /// One discovered feature as the hub and the core services see it (<see cref="ModFeatures.All"/>): what the feature declared, read once
    /// at start, and its live state from the registry. A feature whose class could not be used (no parameterless constructor, a throwing
    /// property, an id that does not match its namespace) is listed too, as Unavailable with the reason, so the hub can show why.
    /// </summary>
    public sealed class FeatureInfo
    {
        private static readonly string[] NoIds = new string[0];

        internal FeatureInfo(Type type, IModFeature feature, string id, string nameKey, string descriptionKey, string[] dependsOn,
            bool enabledByDefault, bool supportsHotToggle, string problem)
        {
            FeatureType = type;
            Feature = feature;
            Id = id ?? string.Empty;
            NameKey = nameKey;
            DescriptionKey = descriptionKey;
            DependsOn = dependsOn ?? NoIds;
            EnabledByDefault = enabledByDefault;
            SupportsHotToggle = supportsHotToggle;
            Problem = problem;
            Context = new FeatureContext(this);
        }

        public string Id { get; }

        /// <summary><see cref="ModL10n"/> key of the name; null for a feature that could not be read (show <see cref="Id"/> then).</summary>
        public string NameKey { get; }

        public string DescriptionKey { get; }

        /// <summary>Declared hard dependencies (a copy taken at start).</summary>
        public IReadOnlyList<string> DependsOn { get; }

        public bool EnabledByDefault { get; }

        public bool SupportsHotToggle { get; }

        /// <summary>The class marked <c>[ModFeature]</c>.</summary>
        public Type FeatureType { get; }

        public FeatureState State
        {
            get { return ModFeatures.StateOf(Id); }
        }

        /// <summary>Enabled: the feature's patches, handlers and providers run.</summary>
        public bool IsActive
        {
            get { return ModFeatures.IsActive(Id); }
        }

        /// <summary>The structured state for the hub (<c>ModL10n.L(Status.ReasonKey, Status.ReasonArg)</c>).</summary>
        public FeatureStatus Status
        {
            get { return ModFeatures.StatusOf(Id); }
        }

        /// <summary>Text for the player why the feature is Unavailable or Faulted (see <see cref="ModFeatures.ReasonOf"/>), otherwise null.</summary>
        public string Reason
        {
            get { return ModFeatures.ReasonOf(Id); }
        }

        /// <summary>The position of the hub toggle: the player's choice, including one that waits for the scene to change.</summary>
        public bool WantsEnabled
        {
            get { return ModFeatures.WantsEnabled(Id); }
        }

        /// <summary>The player switched a feature without hot toggle in the game scene; the switch applies when the scene changes.</summary>
        public bool HasPendingChange
        {
            get { return ModFeatures.HasPendingChange(Id); }
        }

        /// <summary><see cref="IModFeature.Register"/> ran and returned (it runs once per session).</summary>
        public bool IsRegistered { get; internal set; }

        public override string ToString()
        {
            return Id;
        }

        // ---- registry internals ----

        /// <summary>The feature object; null when the class could not be created (then <see cref="Problem"/> says why).</summary>
        internal IModFeature Feature { get; }

        /// <summary>The single context of the feature, passed to <see cref="IModFeature.Register"/>.</summary>
        internal FeatureContext Context { get; }

        /// <summary>Why the feature cannot run at all, found at discovery; null when it is fine.</summary>
        internal string Problem { get; }

        /// <summary><see cref="IModFeature.OnEnabled"/> was called and <see cref="IModFeature.OnDisabled"/> not yet (the registry reconciles this with the state).</summary>
        internal bool Live { get; set; }

        /// <summary><see cref="IModFeature.Register"/> was attempted (successfully or not); it is never called twice.</summary>
        internal bool RegisterAttempted { get; set; }
    }
}
