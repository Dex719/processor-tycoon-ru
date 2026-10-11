using System;
using PTMod.Core;
using AICompany = ProcessorTycoon.CompanySystem.AICompany;
using GameCreation = ProcessorTycoon.AISystem.AIBehaviourCreation;
using GameResearch = ProcessorTycoon.AISystem.AIBehaviourResearch;

namespace PTMod.Features.RivalPersonalities
{
    /// <summary>
    /// The pace of the AI's launches and its research funding (design.md, Component 4, D2; formulas 1 and 2; FR-3 items 1, 2, 7, 8; FR-7 item 4):
    /// <list type="bullet">
    /// <item><c>AIBehaviourCreation.OnTick()</c>, postfix: the design rating that the controller compares with its approval threshold is multiplied
    /// by <c>approvalBias</c> of the archetype and by the <c>CounterLaunch</c> boosts of the designer's segment (<c>MarketFocus</c>). A rating that is
    /// NaN, 0 or less, or below 1.05 stays as it is; the result is at most 10 (<see cref="BehaviourMath.ApprovalRating(float, float, float)"/>).
    /// Multiplying the rating by k is dividing the threshold by k: the lambda of the controller rewrites the threshold every day, so the rating is
    /// the stable place.</item>
    /// <item><c>AIBehaviourResearch.GetMaxFunding()</c> (private), postfix: the cap is multiplied by <c>researchFundingMult</c> and the funding
    /// boosts of the company, clamp 0.01-5.0 (<see cref="BehaviourMath.MaxFundingOf"/>). The game then lowers the funding by itself when it cannot
    /// afford it (<c>AvaliateFunding</c>).</item>
    /// </list>
    /// During the fast-forward and before the character of the company is applied the multiplier is 1 and the result is the game's
    /// (<see cref="BehaviourModifiers"/>). Both targets are declared with <c>ctx.Patch</c>, so the registry of the core and its self-check know them.
    /// Every game day, ~40 calls: no allocation; a failure is counted by the core and leaves the game's value.
    /// </summary>
    internal static class CreationAndResearchPatches
    {
        private const string Feature = RivalPersonalitiesFeature.FeatureId;
        private const string CreationSite = "CreationAndResearchPatches/AIBehaviourCreation.OnTick.postfix";
        private const string FundingSite = "CreationAndResearchPatches/AIBehaviourResearch.GetMaxFunding.postfix";

        [ModPatch(typeof(GameCreation), "OnTick", PatchKind.Postfix)]
        private static void CreationOnTick(GameCreation __instance, AICompany ___company, ref float __result)
        {
            if (!ModHooks.CanRun(Feature, CreationSite)) return;
            long started = RivalTimings.Begin();
            try
            {
                RivalFaultProbe.Trip(CreationSite);
                RivalPersonalitiesFeature feature = RivalPersonalitiesFeature.Instance;
                if (feature == null || __instance == null || !ModFeatures.IsActive(Feature)) return;

                float multiplier = feature.Modifiers.ApprovalMultiplier(___company, (Segment)(int)__instance.MarketFocus);
                if (multiplier == 1f) return;
                __result = BehaviourMath.ApprovalRating(__result, multiplier, feature.MinApprovalForBoost);
            }
            catch (Exception e)
            {
                ModHooks.ReportFault(Feature, CreationSite, e);
            }
            finally
            {
                RivalTimings.End(started);
            }
        }

        [ModPatch(typeof(GameResearch), "GetMaxFunding", PatchKind.Postfix)]
        private static void ResearchMaxFunding(AICompany ___company, ref float __result)
        {
            if (!ModHooks.CanRun(Feature, FundingSite)) return;
            long started = RivalTimings.Begin();
            try
            {
                RivalFaultProbe.Trip(FundingSite);
                RivalPersonalitiesFeature feature = RivalPersonalitiesFeature.Instance;
                if (feature == null || !ModFeatures.IsActive(Feature)) return;

                float multiplier = feature.Modifiers.ResearchFundingMultiplier(___company);
                if (multiplier == 1f) return;
                __result = BehaviourMath.MaxFundingOf(__result, multiplier);
            }
            catch (Exception e)
            {
                ModHooks.ReportFault(Feature, FundingSite, e);
            }
            finally
            {
                RivalTimings.End(started);
            }
        }
    }
}
