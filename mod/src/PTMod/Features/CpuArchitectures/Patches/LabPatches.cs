using System;
using PTMod.Core;
using AIBehaviourResearch = ProcessorTycoon.AISystem.AIBehaviourResearch;
using AICompany = ProcessorTycoon.CompanySystem.AICompany;

namespace PTMod.Features.CpuArchitectures
{
    /// <summary>
    /// The AI companies and the shared lab (FR-7 item 3, D2; design.md, "Патчи Harmony"): while an architecture holds the lab of an AI company the game
    /// must not choose a technology for it, or both would be researched at once. <c>AIBehaviourResearch.OnTick</c> calls <c>ChooseANewResearch</c> every
    /// day for a company whose slot is empty, and an architecture leaves the slot empty; the prefix skips that call for exactly such a company (an
    /// architecture is its current research and is not paused) and lets it through in every other case, so the game picks again by itself the day the
    /// architecture ends or pauses. The target is not reserved by the core. The patch is <c>Optional</c>: without it the part <c>Ai</c> of the research is off
    /// (<see cref="ArchitectureAi.MayResearch"/>) and the AI keeps its seed and its active family. The private <c>company</c> field comes in by
    /// Harmony's injection (<c>___company</c>); the self-check looks for it first. This is the AI half of <c>TSK-034</c> (the arbiter of the player's lab and
    /// the panel of the current research belong to that task).
    /// <para>
    /// It runs once a day for each AI company, so it allocates nothing and does not use <c>ModHooks.Guard</c> with a lambda.
    /// </para>
    /// </summary>
    internal static class LabPatches
    {
        private const string Site = "LabPatches/ChooseANewResearch.prefix";

        /// <summary><c>AIBehaviourResearch.ChooseANewResearch</c>: false skips the original when an architecture holds the lab of this company.</summary>
        [ModPatch(typeof(AIBehaviourResearch), "ChooseANewResearch", PatchKind.Prefix, ArgTypes = new Type[0], Optional = true)]
        private static bool PrefixChooseANewResearch(AICompany ___company)
        {
            if (ArchitecturesFeature.NeutralNow) return true;   // off or the fast-forward of a new game: the game chooses as always
            if (!ModHooks.CanRun(ArchitecturesFeature.FeatureId, Site)) return true;
            try
            {
                ArchitecturesFeature feature = ArchitecturesFeature.Instance;
                LabArbiter lab = feature == null ? null : feature.Lab;
                if (lab == null || ___company == null) return true;
                return !lab.IsOccupiedByArchitecture(___company);
            }
            catch (Exception ex)
            {
                ModHooks.ReportFault(ArchitecturesFeature.FeatureId, Site, ex);
                return true;   // the vanilla way, as if the patch were not there
            }
        }
    }
}
