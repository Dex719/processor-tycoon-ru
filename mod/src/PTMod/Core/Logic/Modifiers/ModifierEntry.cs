using System;
using System.Globalization;

namespace PTMod.Core.Logic.Modifiers
{
    /// <summary>
    /// One saved modifier (FR-12): a multiplier on a channel for a scope, owned by a feature, optionally limited in time
    /// and optionally carrying a reason for <c>Explain</c>. Immutable; created by <see cref="ChannelRegistry"/>
    /// (<c>Add</c>, <c>Restore</c>). The entry of a disabled feature stays in the registry and in the save, it is just not applied.
    /// </summary>
    public sealed class ModifierEntry
    {
        private static readonly string[] NoArgs = new string[0];

        internal ModifierEntry(string id, string featureId, string channel, ModifierScope scope, float multiplier,
            DateTime? expiresOn, string reasonKey, string[] reasonArgs)
        {
            Id = id;
            FeatureId = featureId;
            Channel = channel;
            Scope = scope;
            Multiplier = multiplier;
            ExpiresOn = expiresOn.HasValue ? (DateTime?)expiresOn.Value.Date : null;
            ReasonKey = string.IsNullOrEmpty(reasonKey) ? null : reasonKey;
            ReasonArgs = reasonArgs == null || reasonArgs.Length == 0 ? NoArgs : (string[])reasonArgs.Clone();
        }

        /// <summary>Unique id inside the registry ("m1", "m2"...); the key of <c>Remove</c>.</summary>
        public string Id { get; }

        /// <summary>Id of the feature that owns the entry (the feature whose state decides whether it applies).</summary>
        public string FeatureId { get; }

        public string Channel { get; }

        public ModifierScope Scope { get; }

        /// <summary>Finite, 0 or more.</summary>
        public float Multiplier { get; }

        /// <summary>First day on which the entry no longer applies, or null for an unlimited entry.</summary>
        public DateTime? ExpiresOn { get; }

        /// <summary>ModL10n key of the reason shown by Explain, or null.</summary>
        public string ReasonKey { get; }

        /// <summary>Arguments of <see cref="ReasonKey"/>; never null, never changes (do not modify the array).</summary>
        public string[] ReasonArgs { get; }

        /// <summary>True for an entry with an end date.</summary>
        public bool IsTemporary
        {
            get { return ExpiresOn.HasValue; }
        }

        /// <summary>True when the entry does not apply on <paramref name="day"/> because its end date has come: the end date itself is already outside.</summary>
        public bool IsExpiredOn(DateTime day)
        {
            return ExpiresOn.HasValue && day.Date >= ExpiresOn.Value;
        }

        public override string ToString()
        {
            return Id + " " + FeatureId + ": " + Channel + " x" + Multiplier.ToString("0.###", CultureInfo.InvariantCulture)
                   + " " + Scope + (ExpiresOn.HasValue ? " until " + ExpiresOn.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : string.Empty);
        }
    }
}
