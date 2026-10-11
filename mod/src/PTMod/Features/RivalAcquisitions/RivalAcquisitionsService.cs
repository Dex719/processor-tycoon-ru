using System.Collections.Generic;
using PTMod.Core;

namespace PTMod.Features.RivalAcquisitions
{
    /// <summary>
    /// The implementation behind the static <c>RivalAcquisitionsApi</c> (FR-13): what the feature registers with
    /// <c>RivalAcquisitionsApi.Register</c> when it is switched on and drops when it is switched off. It only reads the books of the feature
    /// (<see cref="AcquisitionsState"/>), so every answer is exact for the save in play and costs a short loop over a few records. The API catches
    /// an exception of any member, counts it against the feature and answers like the null object.
    /// </summary>
    internal sealed class RivalAcquisitionsService : IRivalAcquisitionsService
    {
        private readonly RivalAcquisitionsFeature _feature;

        public RivalAcquisitionsService(RivalAcquisitionsFeature feature)
        {
            _feature = feature;
        }

        public IReadOnlyList<DealRecord> Deals
        {
            get { return _feature.State.deals; }
        }

        public double GetPlayerShare(int uniqueId)
        {
            return _feature.State.ShareOf(uniqueId);
        }

        public bool IsAbsorbedByPlayer(int uniqueId)
        {
            return _feature.State.IsAbsorbed(uniqueId);
        }

        public void OpenWindow(int focusUniqueId)
        {
            // The window "Deals" (TSK-015) reads the game anew each time it opens; it does nothing outside the game scene.
            AcquisitionsWindow.Open(focusUniqueId);
        }
    }
}
