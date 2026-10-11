using PTMod.Core;
using PTMod.Core.Logic.Json;
using PTMod.Features.ScenariosAchievements.Logic;

namespace PTMod.Features.ScenariosAchievements.Integration
{
    /// <summary>
    /// The adapter between <see cref="ProfileModel"/> and the profile of the core (TSK-015, FR-13, NFR-4). The model works on the live section
    /// <c>ModProfile.Section("scenarios-achievements", 1)</c>; the gate calls <c>ModProfile.MarkDirty</c> after every change the model reports
    /// (its <see cref="ProfileModel.Revision"/> moved), <c>ModProfile.Save</c> when the results are shown and when the profile is reset, and hands
    /// out <c>ModProfile.Status</c> for the settings page of the hub. The file, the backup, the recovery and the throttling of the writes are
    /// the core's; the feature opens no file and keeps no buffer. Every call is guarded: a failure is counted against the feature and the call
    /// answers "nothing changed". Main thread only.
    /// </summary>
    internal static class ProfileGate
    {
        private static ProfileModel _model;
        private static JsonObject _section;
        private static int _reportedRevision;

        /// <summary>The read side of the profile: what is earned, the records, the challenges passed.</summary>
        internal static ProfileView View
        {
            get { return Model().View; }
        }

        /// <summary>How the profile was obtained at start (a new one, loaded, restored from the backup, damaged and started empty).</summary>
        internal static ProfileStatus Status
        {
            get { return ModProfile.Status; }
        }

        /// <summary>The status as a phrase for the player (the texts of the core: <c>core.about.profile.*</c>).</summary>
        internal static string StatusText()
        {
            switch (Status)
            {
                case ProfileStatus.Loaded: return ModL10n.L("core.about.profile.loaded");
                case ProfileStatus.RestoredFromBackup: return ModL10n.L("core.about.profile.restored");
                case ProfileStatus.Corrupt: return ModL10n.L("core.about.profile.corrupt");
                default: return ModL10n.L("core.about.profile.new");
            }
        }

        /// <summary>Puts an achievement into the profile; false when it was there already.</summary>
        internal static bool Unlock(string achievementId, AchievementEntry entry)
        {
            return ModHooks.Guard(ScenariosFeature.FeatureId, "ProfileGate.Unlock", () =>
            {
                ProfileModel model = Model();
                bool added = model.Unlock(achievementId, entry);
                Sync(model);
                return added;
            }, false);
        }

        /// <summary>A run started (a counter of the profile).</summary>
        internal static void RecordRunStarted()
        {
            ModHooks.Guard(ScenariosFeature.FeatureId, "ProfileGate.RecordRunStarted", () =>
            {
                ProfileModel model = Model();
                model.RecordRunStarted();
                Sync(model);
            });
        }

        /// <summary>Records how a scenario run ended; true when the best result of the scenario changed. Call <see cref="Save"/> when the results are shown.</summary>
        internal static bool RecordResult(ScenarioResult result, RunInfo run)
        {
            return ModHooks.Guard(ScenariosFeature.FeatureId, "ProfileGate.RecordResult", () =>
            {
                ProfileModel model = Model();
                bool newBest = model.RecordResult(result, run);
                Sync(model);
                return newBest;
            }, false);
        }

        /// <summary>A challenge was passed after some whole years.</summary>
        internal static void RecordChallenge(string challengeId, int years)
        {
            ModHooks.Guard(ScenariosFeature.FeatureId, "ProfileGate.RecordChallenge", () =>
            {
                ProfileModel model = Model();
                model.RecordChallenge(challengeId, years);
                Sync(model);
            });
        }

        /// <summary>Writes the profile at once (the core keeps the previous file as <c>.bak</c>): when the results are shown.</summary>
        internal static void Save()
        {
            ModHooks.Guard(ScenariosFeature.FeatureId, "ProfileGate.Save", () => ModProfile.Save());
        }

        /// <summary>"Reset the profile": empties the section and writes it at once.</summary>
        internal static void Reset()
        {
            ModHooks.Guard(ScenariosFeature.FeatureId, "ProfileGate.Reset", () =>
            {
                ProfileModel model = Model();
                model.Clear();
                Sync(model);
                ModProfile.Save();
            });
        }

        // The model over the live section; built again when the core hands out another object (it does so only for a detached section).
        private static ProfileModel Model()
        {
            JsonObject section = ModProfile.Section(ScenariosFeature.FeatureId, ProfileModel.SchemaVersion);
            if (_model == null || !ReferenceEquals(section, _section))
            {
                _section = section;
                _model = new ProfileModel(section);
                _reportedRevision = 0;
            }
            return _model;
        }

        // The core is told about a change once per change, never for a call that changed nothing.
        private static void Sync(ProfileModel model)
        {
            if (model.Revision == _reportedRevision) return;
            _reportedRevision = model.Revision;
            ModProfile.MarkDirty(ScenariosFeature.FeatureId);
        }
    }
}
