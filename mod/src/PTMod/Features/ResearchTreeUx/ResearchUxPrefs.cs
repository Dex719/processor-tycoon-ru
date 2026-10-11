using System;
using System.Collections.Generic;
using PTMod.Core;
using PTMod.Core.Logic.Json;

namespace PTMod.Features.ResearchTreeUx
{
    /// <summary>
    /// The view preferences of the player in the global profile (design: "ModProfile: предпочтения вида", FR-8 item 2): the adapter between
    /// <see cref="UxPrefsModel"/> and <c>ModProfile.Section("research-tree-ux")</c>. Read once, on first use; a change is made on
    /// <see cref="Current"/> and written with <see cref="Save"/>, which marks the section dirty so the core writes the file (throttled, and
    /// always on leaving the scene or quitting). The feature never opens the file. Main thread only; nothing here throws.
    /// </summary>
    internal static class ResearchUxPrefs
    {
        private const int SectionSchema = 1;

        private static UxPrefsModel _current;

        /// <summary>The live preferences (read from the profile on first use; defaults when the profile cannot be read).</summary>
        public static UxPrefsModel Current
        {
            get { return _current ?? (_current = Load()); }
        }

        /// <summary>Reads the profile again, dropping any change that was not saved (for tests and for a profile that was replaced).</summary>
        public static UxPrefsModel Reload()
        {
            _current = Load();
            return _current;
        }

        /// <summary>Writes <see cref="Current"/> into the section of the profile and asks the core to save the file.</summary>
        public static void Save()
        {
            try
            {
                UxPrefsModel prefs = Current;
                JsonObject section = ModProfile.Section(ResearchTreeUxFeature.FeatureId, SectionSchema);
                if (section == null) return;

                prefs.Write(section);
                ModProfile.MarkDirty(ResearchTreeUxFeature.FeatureId);
            }
            catch (Exception e)
            {
                ModLog.Error(ResearchTreeUxFeature.FeatureId, "prefs: cannot write the view preferences to the profile", e);
            }
        }

        private static UxPrefsModel Load()
        {
            try
            {
                JsonObject section = ModProfile.Section(ResearchTreeUxFeature.FeatureId, SectionSchema);
                var problems = new List<string>();
                UxPrefsModel prefs = UxPrefsModel.Read(section, problems);
                foreach (string problem in problems) ModLog.Warn(ResearchTreeUxFeature.FeatureId, "prefs: " + problem);
                return prefs;
            }
            catch (Exception e)
            {
                ModLog.Error(ResearchTreeUxFeature.FeatureId, "prefs: cannot read the view preferences from the profile, the defaults are used", e);
                return UxPrefsModel.Defaults();
            }
        }
    }
}
