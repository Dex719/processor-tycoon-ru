using System;
using PTMod.Core;
using PTMod.Features.DesignFlawRecalls;
using AICompany = ProcessorTycoon.CompanySystem.AICompany;
using GameCreation = ProcessorTycoon.AISystem.AIBehaviourCreation;

namespace PTMod.Features.RivalPersonalities
{
    /// <summary>
    /// The caution of an AI company after a public design flaw (design.md, "Патчи Harmony"; FR-17 items 2-4, TSK-030): a postfix on the private
    /// <c>AIBehaviourCreation.AcceptableFailureRate(ProcessNode)</c>, which gives the share of faulty chips the designer accepts for a design. While the
    /// company has a caution in force (<see cref="CautionBook"/>, started by <see cref="FlawLink"/>) the result is multiplied by
    /// <c>flawCaution.factor</c> (0.85), so the designer takes fewer risks.
    /// <list type="bullet">
    /// <item>The value is the game's own when there is no caution, when <c>design-flaw-recalls</c> is off, when the company is bankrupt or dormant, and while
    /// a new game fast-forwards (<see cref="CautionMath.Applies"/>).</item>
    /// <item>The patch is <c>Optional</c>: if the method is not there (another version of the game) the feature starts without it and loses only the
    /// caution; it is not <c>Unavailable</c>. <c>design-flaw-recalls</c> does not patch this method, and the multiplications of other features commute.</item>
    /// <item>The call is made for every design of every AI company each game day; nothing is allocated and a failure leaves the result as it is.</item>
    /// </list>
    /// </summary>
    internal static class CautionPatch
    {
        private const string Feature = RivalPersonalitiesFeature.FeatureId;
        private const string Site = "CautionPatch/AIBehaviourCreation.AcceptableFailureRate.postfix";

        [ModPatch(typeof(GameCreation), "AcceptableFailureRate", PatchKind.Postfix, Optional = true)]
        private static void Caution(AICompany ___company, ref float __result)
        {
            if (!ModHooks.CanRun(Feature, Site)) return;
            long started = RivalTimings.Begin();
            try
            {
                RivalFaultProbe.Trip(Site);
                float factor = FactorFor(___company);
                if (factor != 1f) __result *= factor;
            }
            catch (Exception e)
            {
                ModHooks.ReportFault(Feature, Site, e);
            }
            finally
            {
                RivalTimings.End(started);
            }
        }

        /// <summary>
        /// The factor for the company now: <c>flawCaution.factor</c> while its caution is in force and counts (<see cref="CautionMath.Applies"/>), 1 otherwise.
        /// </summary>
        internal static float FactorFor(AICompany company)
        {
            RivalPersonalitiesFeature feature = RivalPersonalitiesFeature.Instance;
            if (feature == null || ReferenceEquals(company, null) || company == null || !ModFeatures.IsActive(Feature)) return 1f;

            int uniqueId = company.UniqueID;
            if (uniqueId < 0) return 1f;
            if (!CautionMath.Applies(ModFeatures.IsActive(DesignFlawRecallsApi.FeatureId), company.IsBankrupt, feature.Book.IsDormant(uniqueId), ModTicks.IsFastForwarding)) return 1f;

            return feature.Cautions.FactorFor(uniqueId, ModTicks.Today);
        }
    }
}
