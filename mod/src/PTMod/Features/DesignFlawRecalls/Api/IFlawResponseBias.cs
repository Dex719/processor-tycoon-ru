using ICompany = ProcessorTycoon.CompanySystem.ICompany;

namespace PTMod.Features.DesignFlawRecalls
{
    /// <summary>
    /// The optional bias of the AI reaction to a flaw (FR-17). <c>rival-personalities</c> implements it and registers it with
    /// <see cref="FlawRegistry.SetResponseBias"/>; <c>design-flaw-recalls</c> multiplies the weights of the four reactions of an AI
    /// company by the numbers it returns (and normalises the result) when the company chooses what to do about its flaw.
    /// <para>
    /// Rules of the contract:
    /// <list type="bullet">
    /// <item>The array is <c>[Recall, Stepping, Patch, Ignore]</c> (<see cref="FlawResponseBiasOrder"/>). <c>null</c> or an array of another
    /// length means "all 1.0". A multiplier that is negative, NaN or infinite makes the whole answer count as "all 1.0".</item>
    /// <item>The call is made inside <c>ModHooks.Guard</c> on behalf of the feature that owns the implementing type: an exception counts as
    /// "all 1.0" and as a fault of that feature, not of <c>design-flaw-recalls</c>.</item>
    /// <item>The method is called on the main thread, a few times per AI flaw decision. It must be fast and must not change the game.</item>
    /// <item>The player's company is never asked.</item>
    /// </list>
    /// </para>
    /// </summary>
    public interface IFlawResponseBias
    {
        /// <summary>
        /// The multipliers of the reaction weights of <paramref name="company"/> (an AI company).
        /// </summary>
        /// <param name="company">The AI company that has to choose.</param>
        /// <param name="isPublic"><c>true</c> when the flaw is public, <c>false</c> when only the company knows about it.</param>
        /// <returns><c>[Recall, Stepping, Patch, Ignore]</c>, or <c>null</c> for no bias.</returns>
        float[] Multipliers(ICompany company, bool isPublic);
    }
}
