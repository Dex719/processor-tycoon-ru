using System;
using System.Collections.Generic;
using PTMod.Core;
using PTMod.Features.ScenariosAchievements.Logic;

namespace PTMod.Features.ScenariosAchievements.Integration
{
    /// <summary>
    /// What the <see cref="RunEngine"/> writes outside the run, done with the services of the core (TSK-019): the profile through
    /// <see cref="ProfileGate"/> (<c>ModProfile</c> keeps the file) and the records of <c>ModModifiers</c> through <see cref="ModifierBinder"/>.
    /// </summary>
    internal sealed class GameRunPorts : IRunPorts
    {
        private bool _unreadable;

        public ProfileView Profile
        {
            get
            {
                try
                {
                    return ProfileGate.View;
                }
                catch (Exception ex)
                {
                    if (!_unreadable)
                    {
                        _unreadable = true;
                        ModLog.Error(ScenariosFeature.FeatureId, "the profile cannot be read; achievements are not judged meanwhile", ex);
                    }
                    return null;
                }
            }
        }

        public bool UnlockAchievement(string achievementId, AchievementEntry entry)
        {
            return ProfileGate.Unlock(achievementId, entry);
        }

        public void RecordScenarioResult(ScenarioResult result, RunInfo run)
        {
            ProfileGate.RecordResult(result, run);
        }

        public void RecordChallengePassed(string challengeId, int years)
        {
            ProfileGate.RecordChallenge(challengeId, years);
        }

        public void RemoveModifierEntries(IList<string> entryIds)
        {
            ModifierBinder.Remove(entryIds);
        }
    }
}
