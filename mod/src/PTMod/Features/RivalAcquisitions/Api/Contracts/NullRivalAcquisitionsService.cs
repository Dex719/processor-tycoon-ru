using System;
using System.Collections.Generic;

namespace PTMod.Features.RivalAcquisitions
{
    /// <summary>
    /// The null object of <see cref="IRivalAcquisitionsService"/>: what <c>RivalAcquisitionsApi</c> answers with while the feature is off or has
    /// not registered its implementation. No deals, no stakes, nobody is absorbed, the window does not open. Stateless and thread-safe.
    /// <para>
    /// Pure contract file: it must not reference PTMod.Core, UnityEngine or the game, so the tests build can compile it with the feature's Logic.
    /// </para>
    /// </summary>
    internal sealed class NullRivalAcquisitionsService : IRivalAcquisitionsService
    {
        /// <summary>The one instance.</summary>
        public static readonly NullRivalAcquisitionsService Instance = new NullRivalAcquisitionsService();

        private NullRivalAcquisitionsService()
        {
        }

        public IReadOnlyList<DealRecord> Deals
        {
            get { return Array.Empty<DealRecord>(); }
        }

        public double GetPlayerShare(int uniqueId)
        {
            return 0.0;
        }

        public bool IsAbsorbedByPlayer(int uniqueId)
        {
            return false;
        }

        public void OpenWindow(int focusUniqueId)
        {
        }
    }
}
