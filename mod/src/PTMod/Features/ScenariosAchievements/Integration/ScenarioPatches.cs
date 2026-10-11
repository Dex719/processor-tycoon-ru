using PTMod.Core;
using GameContract = ProcessorTycoon.ContractSystem.Contract;
using GameContractManager = ProcessorTycoon.ContractSystem.ContractManager;

namespace PTMod.Features.ScenariosAchievements.Integration
{
    /// <summary>
    /// The postfix on <c>ContractManager.PromoteContractToActive</c> (TSK-017, design.md "Патчи Harmony"; FR-12): a contract became active, and if the CPU it
    /// names is the player's, one more OEM contract was signed (the metric <c>oem.signed.total</c>, the event <c>oem.signed</c>). It only reads. It leaves on its
    /// first line while a save loads (the game promotes every contract of the save) and during the new game's fast-forward, so a load or a fast-forward
    /// never counts a contract. The other patch of the feature is <see cref="DifficultyGuard"/>; there are no patches on <c>Company.AddCpu</c>,
    /// <c>AICompany.AddCpu</c>, <c>Cheats.AddMoney</c> or <c>DateController.NextMonth</c> - the core's services replace them.
    /// </summary>
    internal static class ScenarioPatches
    {
        private const string Site = "ScenarioPatches.PromoteContractToActive";

        [ModPatch(typeof(GameContractManager), "PromoteContractToActive", PatchKind.Postfix)]
        private static void PromoteContractToActivePostfix(GameContract contract)
        {
            if (ModTicks.IsLoading || ModTicks.IsFastForwarding) return;
            ModHooks.Guard(ScenariosFeature.FeatureId, Site, () => GameEventHooks.OnContractPromoted(contract));
        }
    }
}
