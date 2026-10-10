using System;

namespace PTMod.Core
{
    /// <summary>
    /// The entry point of a feature (FR-3): a <c>[ModFeature] sealed class … : IModFeature</c> with a public parameterless constructor in the
    /// namespace <c>PTMod.Features.&lt;Name&gt;</c>, where <c>&lt;Name&gt;</c> is the Pascal form of <see cref="Id"/> (<c>EventSystem</c> for
    /// <c>event-system</c>): the namespace is how the core tells which feature a handler, a patch or an exception belongs to
    /// (<see cref="ModFeatures.FeatureOfType"/>). A feature whose id does not match its namespace is Unavailable.
    /// <para>
    /// The properties are read once, when the mod starts. <see cref="Register"/> is called once per session, when the feature first becomes
    /// active (at start for a feature the player has on, later for one switched on in the hub), in dependency order. <see cref="OnEnabled"/>
    /// and <see cref="OnDisabled"/> follow every switch (an Unavailable dependency or the circuit breaker switch the feature off too);
    /// <see cref="OnDisabled"/> must drop references to objects of the scene, because a switched-off feature gets no
    /// <see cref="PTModHost.SceneLeaving"/>. None of them may throw into the core: an exception from <see cref="Register"/> makes the feature
    /// Unavailable, one from <see cref="OnEnabled"/> makes it Faulted.
    /// </para>
    /// </summary>
    public interface IModFeature
    {
        /// <summary>The slug of the feature's spec (<c>event-system</c>); also the key of its sections in the save and the profile.</summary>
        string Id { get; }

        /// <summary><see cref="ModL10n"/> key of the feature's name in the hub.</summary>
        string NameKey { get; }

        /// <summary><see cref="ModL10n"/> key of the feature's description in the hub.</summary>
        string DescriptionKey { get; }

        /// <summary>Ids of the features this one cannot work without (hard dependencies only); empty or null for none.</summary>
        string[] DependsOn { get; }

        /// <summary>Whether the feature is on until the player chooses (the choice is kept in <c>ptmod_settings.json</c>).</summary>
        bool EnabledByDefault { get; }

        /// <summary>
        /// True when the feature can be switched on and off during a game (its patches are removed and its handlers skipped at once).
        /// Otherwise a switch made in the game scene takes effect when the scene changes (loading a save, back to the menu).
        /// </summary>
        bool SupportsHotToggle { get; }

        /// <summary>
        /// Declares everything the feature needs through <paramref name="ctx"/>: patches, ticks, the save participant, parameters, the hub page.
        /// At start it runs before <see cref="PTModHost"/> exists: start coroutines in <see cref="OnEnabled"/>, not here.
        /// </summary>
        void Register(FeatureContext ctx);

        /// <summary>The feature became active (after its patches were applied). <see cref="PTModHost.Instance"/> exists from here on.</summary>
        void OnEnabled();

        /// <summary>The feature stopped being active (switched off, a dependency went away, or Faulted); its patches are removed right after.</summary>
        void OnDisabled();
    }

    /// <summary>Marks the entry class of a feature; the bootstrap finds the features by this attribute (FR-3 item 1).</summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
    public sealed class ModFeatureAttribute : Attribute
    {
    }
}
