using PTMod.Core;
using PTMod.Core.Logic.Json;

namespace PTMod.Features.FirstYearOnboarding
{
    /// <summary>
    /// The tutorial's part of the player's profile (TSK-001, D11): the section <c>first-year-onboarding</c> of <c>ptmod_profile.json</c>, read
    /// and written through <c>ModProfile</c>, which owns the version of the section (<c>_schema</c>) and the throttled writing. Two values:
    /// whether this profile has already seen the tutorial, and the last choice of the "Tutorial" toggle of the new game window. A wrong or
    /// missing value reads as its default. Main thread only.
    /// </summary>
    internal static class OnboardingProfile
    {
        private const string SeenKey = "hasSeenTutorial";
        private const string ToggleKey = "defaultToggle";

        /// <summary>The profile has seen the tutorial (it ended, was skipped or was aborted once). Default false.</summary>
        internal static bool HasSeenTutorial
        {
            get { return Section().GetBool(SeenKey, false); }
            set { Write(SeenKey, value, false); }
        }

        /// <summary>The last choice of the "Tutorial" toggle of the new game window. Default true.</summary>
        internal static bool DefaultToggle
        {
            get { return Section().GetBool(ToggleKey, true); }
            set { Write(ToggleKey, value, true); }
        }

        /// <summary>
        /// Whether the "Tutorial" toggle starts on: on until the profile has seen the tutorial, afterwards as the player left it
        /// (owner decision: the toggle is on by default only while the profile has not seen the tutorial).
        /// </summary>
        internal static bool ToggleStartsOn
        {
            get { return !HasSeenTutorial || DefaultToggle; }
        }

        private static JsonObject Section()
        {
            return ModProfile.Section(OnboardingFeature.FeatureId);
        }

        private static void Write(string key, bool value, bool defaultValue)
        {
            JsonObject section = Section();
            if (section.ContainsKey(key) && section.GetBool(key, defaultValue) == value) return;

            section.Set(key, value);
            ModProfile.MarkDirty(OnboardingFeature.FeatureId);
        }
    }
}
