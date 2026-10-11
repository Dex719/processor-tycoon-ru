using System;
using PTMod.Core;
using PTMod.Features.RivalAcquisitions;

namespace PTMod.Features.ScenariosAchievements.Integration
{
    /// <summary>
    /// The soft link to <c>rival-acquisitions</c> (TSK-017, FR-12 item 7, E15): a direct call into its public API (<see cref="RivalAcquisitionsApi"/>, the same
    /// assembly), nothing else of that feature is touched. A company the player took out of the game is not a rival that went bankrupt, so it must not
    /// count for "Outlived a rival" and "Last one standing". The API answers false for every company while that feature is off, so no filter is applied
    /// then. Never throws.
    /// </summary>
    internal static class AcquisitionBridge
    {
        /// <summary>The AI company with this <c>UniqueID</c> was absorbed by the player (a deal for it exists); false when the feature is off or the call failed.</summary>
        internal static bool IsAbsorbedByPlayer(int uniqueId)
        {
            try
            {
                return RivalAcquisitionsApi.IsAvailable && RivalAcquisitionsApi.IsAbsorbedByPlayer(uniqueId);
            }
            catch (Exception ex)
            {
                ModLog.Warn(ScenariosFeature.FeatureId, "rival-acquisitions: IsAbsorbedByPlayer(" + uniqueId + ") failed: " + ex.GetType().Name + ": " + ex.Message);
                return false;
            }
        }
    }
}
