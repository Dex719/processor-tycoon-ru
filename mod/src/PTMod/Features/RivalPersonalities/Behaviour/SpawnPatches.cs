using System;
using PTMod.Core;
using AICompany = ProcessorTycoon.CompanySystem.AICompany;
using GameAI = ProcessorTycoon.CompanySystem.AI;
using GameController = ProcessorTycoon.AISystem.AIBehaviourController;

namespace PTMod.Features.RivalPersonalities
{
    /// <summary>
    /// Where a character goes onto a company (design.md D1, "Патчи Harmony"; FR-2 items 1, 4-6): two postfixes, the decisions are
    /// <see cref="PersonalityRegistry"/>'s.
    /// <list type="bullet">
    /// <item><c>AICompany.Initialize(int)</c> runs on all three ways a company appears (<c>CompanySpawner.SpawnCompanies</c>, <c>SpawnCompany</c>,
    /// <c>SpawnCompanyFromSave</c>), after <c>AI.Awake</c> has set up the controller and the designers: in a running game the character is applied at
    /// once; during the fast-forward nothing happens (<c>FastForwardEnded</c> gives every character); while a save loads it waits for
    /// <c>ModSave.AfterRead</c>.</item>
    /// <item><c>AIBehaviourController.ReformCyrux(bool)</c> rewrites the priorities of Cyrix and inserts a High End designer: the character goes on
    /// top again.</item>
    /// </list>
    /// Not on <c>Company.AddCpu</c> / <c>AICompany.AddCpu</c> (the core's, <c>ModEvents.CpuReleased</c>). A failure is counted by the core and the game
    /// goes on as without the mod.
    /// </summary>
    internal static class SpawnPatches
    {
        private const string Feature = RivalPersonalitiesFeature.FeatureId;
        private const string InitializeSite = "SpawnPatches/AICompany.Initialize.postfix";
        private const string ReformSite = "SpawnPatches/AIBehaviourController.ReformCyrux.postfix";

        [ModPatch(typeof(AICompany), "Initialize", PatchKind.Postfix, ArgTypes = new[] { typeof(int) })]
        private static void AfterInitialize(AICompany __instance)
        {
            if (!ModHooks.CanRun(Feature, InitializeSite)) return;
            try
            {
                RivalFaultProbe.Trip(InitializeSite);
                RivalPersonalitiesFeature feature = RivalPersonalitiesFeature.Instance;
                if (feature == null || !ModFeatures.IsActive(Feature)) return;
                feature.Registry.OnCompanyInitialized(__instance);
            }
            catch (Exception e)
            {
                ModHooks.ReportFault(Feature, InitializeSite, e);
            }
        }

        [ModPatch(typeof(GameController), "ReformCyrux", PatchKind.Postfix, ArgTypes = new[] { typeof(bool) })]
        private static void AfterReformCyrux(GameAI ___ai)
        {
            if (!ModHooks.CanRun(Feature, ReformSite)) return;
            try
            {
                RivalFaultProbe.Trip(ReformSite);
                RivalPersonalitiesFeature feature = RivalPersonalitiesFeature.Instance;
                if (feature == null || !ModFeatures.IsActive(Feature) || ___ai == null) return;
                feature.Registry.OnCyrixReformed(___ai.Company as AICompany);
            }
            catch (Exception e)
            {
                ModHooks.ReportFault(Feature, ReformSite, e);
            }
        }
    }
}
