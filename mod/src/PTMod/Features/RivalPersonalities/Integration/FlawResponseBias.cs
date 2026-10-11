using PTMod.Features.DesignFlawRecalls;
using ICompany = ProcessorTycoon.CompanySystem.ICompany;

namespace PTMod.Features.RivalPersonalities
{
    /// <summary>
    /// The character of a rival in the answer of its AI to a design flaw (design.md, Component 12; FR-13 item 5, TSK-020): the multipliers of the weights
    /// of the four reactions of <c>design-flaw-recalls</c> (Recall, Stepping, Patch, Ignore), taken from <c>flawResponseBias</c> of the archetype
    /// (<see cref="FlawBiasMath"/>, each 0.5..2.0). The policy of that feature asks it for an AI company that has to choose; it is registered with
    /// <c>FlawRegistry.SetResponseBias</c> by <see cref="FlawLink"/> while both features are active.
    /// <para>
    /// Null (all 1.0) for the player and for a company the feature does not know, for a dormant rival, and for an archetype whose numbers are all 1.
    /// The call is fast (the archetype is found in the saved assignments) and changes nothing in the game. The type lives in the namespace of this
    /// feature, so the registry of <c>design-flaw-recalls</c> counts a failure against <c>rival-personalities</c>.
    /// </para>
    /// </summary>
    internal sealed class FlawResponseBias : IFlawResponseBias
    {
        /// <inheritdoc />
        public float[] Multipliers(ICompany company, bool isPublic)
        {
            RivalPersonalitiesFeature feature = RivalPersonalitiesFeature.Instance;
            int? uniqueId = RivalApiService.UidOf(company);
            if (feature == null || !uniqueId.HasValue || feature.Book.IsDormant(uniqueId.Value)) return null;

            float[] multipliers = FlawBiasMath.For(feature.Book.ArchetypeOf(uniqueId.Value));
            return FlawBiasMath.IsNeutral(multipliers) ? null : multipliers;
        }
    }
}
