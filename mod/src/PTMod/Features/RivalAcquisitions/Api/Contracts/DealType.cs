namespace PTMod.Features.RivalAcquisitions
{
    /// <summary>
    /// The kinds of deal of <c>rival-acquisitions</c> (FR-3..FR-6). <see cref="DealRecord.type"/> holds the name of one of them
    /// (<c>DealType.Hostile.ToString()</c> is <c>"Hostile"</c>), so other features can compare it as a string or parse it with
    /// <c>Enum.TryParse</c>.
    /// <para>
    /// Pure contract file: it must not reference PTMod.Core, UnityEngine or the game, so the tests build can compile it with the feature's Logic.
    /// </para>
    /// </summary>
    public enum DealType
    {
        /// <summary>The player buys the assets of a bankrupt AI company at the post-bankruptcy auction (FR-3).</summary>
        BankruptAssets,

        /// <summary>The player buys a company that is in default (FR-4); the company is then taken out of the game.</summary>
        Distressed,

        /// <summary>The player buys out a healthy company against its will (FR-5); the company is then taken out of the game.</summary>
        Hostile,

        /// <summary>The player buys a minority stake (below 50 percent, at most 0.49) of an AI company (FR-6).</summary>
        Stake,

        /// <summary>The player gives a rescue order that injects money into a stake company before it goes bankrupt (FR-6).</summary>
        RescueOrder,
    }
}
