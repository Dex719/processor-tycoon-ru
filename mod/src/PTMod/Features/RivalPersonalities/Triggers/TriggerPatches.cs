using System;
using PTMod.Core;
using GameBusinessContracts = ProcessorTycoon.CompanySystem.Business.BusinessContractManager;
using GameContract = ProcessorTycoon.ContractSystem.Contract;
using GameContracts = ProcessorTycoon.ContractSystem.ContractManager;
using GamePlayerCompany = ProcessorTycoon.CompanySystem.Company;
using GameTechnology = ProcessorTycoon.ResearchSystem.Technology;
using ICompany = ProcessorTycoon.CompanySystem.ICompany;

namespace PTMod.Features.RivalPersonalities
{
    /// <summary>
    /// The moves of the player that no core event announces (design.md "Патчи Harmony"; FR-5 items 4, 5 and 6): three postfixes that only read. The
    /// decisions are <see cref="PlayerActionMonitor"/>'s.
    /// <list type="bullet">
    /// <item><c>Company.AddTechnology(Technology, bool)</c> of the player: <c>TechLead</c>. The game calls it for every technology of a save at the load
    /// too; the monitor does not record anything then.</item>
    /// <item><c>ContractManager.PromoteContractToActive(Contract)</c>: <c>OemLoss</c> for the rivals that offered a CPU for a contract that went to the
    /// player.</item>
    /// <item><c>BusinessContractManager.BreakContract(ICompany, ICompany, BusinessContract)</c>: -25 for the rival when the player breaks a contract.</item>
    /// </list>
    /// Not on <c>Company.AddCpu</c> / <c>AICompany.AddCpu</c>: the release of a CPU is <c>ModEvents.CpuReleased</c> (the core's, subscribed in
    /// <c>Register</c>). A failure is counted by the core and the game goes on as without the mod. All three targets are declared with
    /// <c>ctx.Patch</c>, so the self-check of the core covers them.
    /// </summary>
    internal static class TriggerPatches
    {
        private const string Feature = RivalPersonalitiesFeature.FeatureId;
        private const string TechnologySite = "TriggerPatches/Company.AddTechnology.postfix";
        private const string OemSite = "TriggerPatches/ContractManager.PromoteContractToActive.postfix";
        private const string BreakSite = "TriggerPatches/BusinessContractManager.BreakContract.postfix";

        [ModPatch(typeof(GamePlayerCompany), "AddTechnology", PatchKind.Postfix, ArgTypes = new[] { typeof(GameTechnology), typeof(bool) })]
        private static void AfterAddTechnology(GamePlayerCompany __instance, GameTechnology technology)
        {
            if (!ModHooks.CanRun(Feature, TechnologySite)) return;
            long started = RivalTimings.Begin();
            try
            {
                RivalFaultProbe.Trip(TechnologySite);
                PlayerActionMonitor monitor = MonitorOrNull();
                if (monitor == null || __instance == null || !__instance.IsPlayer) return;
                monitor.OnPlayerTechnology(technology);
            }
            catch (Exception e)
            {
                ModHooks.ReportFault(Feature, TechnologySite, e);
            }
            finally
            {
                RivalTimings.End(started);
            }
        }

        [ModPatch(typeof(GameContracts), "PromoteContractToActive", PatchKind.Postfix)]
        private static void AfterPromoteContractToActive(GameContract contract)
        {
            if (!ModHooks.CanRun(Feature, OemSite)) return;
            long started = RivalTimings.Begin();
            try
            {
                RivalFaultProbe.Trip(OemSite);
                PlayerActionMonitor monitor = MonitorOrNull();
                if (monitor == null) return;
                monitor.OnOemContractActivated(contract);
            }
            catch (Exception e)
            {
                ModHooks.ReportFault(Feature, OemSite, e);
            }
            finally
            {
                RivalTimings.End(started);
            }
        }

        [ModPatch(typeof(GameBusinessContracts), "BreakContract", PatchKind.Postfix)]
        private static void AfterBreakContract(ICompany breaker, ICompany otherCompany)
        {
            if (!ModHooks.CanRun(Feature, BreakSite)) return;
            long started = RivalTimings.Begin();
            try
            {
                RivalFaultProbe.Trip(BreakSite);
                PlayerActionMonitor monitor = MonitorOrNull();
                if (monitor == null) return;
                monitor.OnBusinessContractBroken(breaker, otherCompany);
            }
            catch (Exception e)
            {
                ModHooks.ReportFault(Feature, BreakSite, e);
            }
            finally
            {
                RivalTimings.End(started);
            }
        }

        private static PlayerActionMonitor MonitorOrNull()
        {
            RivalPersonalitiesFeature feature = RivalPersonalitiesFeature.Instance;
            return feature != null && ModFeatures.IsActive(Feature) ? feature.Monitor : null;
        }
    }
}
