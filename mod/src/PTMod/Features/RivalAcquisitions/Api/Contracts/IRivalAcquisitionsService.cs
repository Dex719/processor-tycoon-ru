using System;
using System.Collections.Generic;

namespace PTMod.Features.RivalAcquisitions
{
    /// <summary>
    /// What the feature <c>rival-acquisitions</c> implements behind the static <c>RivalAcquisitionsApi</c>: the reads that other features
    /// (<c>rival-personalities</c>, <c>scenarios-achievements</c>) use. The feature registers one implementation with
    /// <c>RivalAcquisitionsApi.Register</c> when it starts and drops it with <c>RivalAcquisitionsApi.Unregister</c> when it stops; while there is
    /// none (the feature is off, Faulted or did not start) the API answers with <see cref="NullRivalAcquisitionsService"/>.
    /// <para>
    /// Main thread only. An implementation may throw: the API catches it, counts it against the feature and answers like the null object.
    /// </para>
    /// <para>
    /// Pure contract file: it must not reference PTMod.Core, UnityEngine or the game, so the tests build can compile it with the feature's Logic.
    /// </para>
    /// </summary>
    public interface IRivalAcquisitionsService
    {
        /// <summary>All deals of the current save, oldest first (a read-only view; never null). The records are shared: callers must not change them.</summary>
        IReadOnlyList<DealRecord> Deals { get; }

        /// <summary>The share of the AI company <paramref name="uniqueId"/> that the player owns, 0 to 0.49 (0 for no stake and for an unknown id).</summary>
        double GetPlayerShare(int uniqueId);

        /// <summary>
        /// The AI company <paramref name="uniqueId"/> was bought by the player (a <see cref="DealType.BankruptAssets"/>, <see cref="DealType.Distressed"/>
        /// or <see cref="DealType.Hostile"/> deal exists for it): it is out of the game because of the player, not because of a bankruptcy of its own.
        /// True already inside the game's <c>OnCompanyWentBankrupt</c> that the takeover itself causes.
        /// </summary>
        bool IsAbsorbedByPlayer(int uniqueId);

        /// <summary>Opens the "Deals" window; <paramref name="focusUniqueId"/> (when 0 or more) selects that company. Does nothing outside the game scene.</summary>
        void OpenWindow(int focusUniqueId);
    }
}
