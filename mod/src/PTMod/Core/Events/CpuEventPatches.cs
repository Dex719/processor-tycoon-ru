using System;
using GameAiCompany = ProcessorTycoon.CompanySystem.AICompany;
using GamePlayerCompany = ProcessorTycoon.CompanySystem.Company;
using ICompany = ProcessorTycoon.CompanySystem.ICompany;
using Cpu = ProcessorTycoon.Hardware.Cpu;

namespace PTMod.Core
{
    /// <summary>
    /// P20: the release of a CPU (FR-23 items 1-3). One postfix on each of the two virtual methods the game adds a CPU through,
    /// <c>Company.AddCpu</c> (the player) and <c>AICompany.AddCpu</c> (the computer companies); both end in <see cref="ModEvents.CpuAdded"/>,
    /// which decides whether it is a release and raises <see cref="ModEvents.CpuReleased"/>.
    /// <list type="bullet">
    /// <item>The methods are reserved for the core (<c>ReservedTargets</c>: service <c>ModEvents</c>): a feature's patch on either is rejected
    /// with the hint to use <c>ModEvents.CpuReleased</c> (AC-2.4).</item>
    /// <item>The game also calls both methods for every CPU of a save while it loads and (for the computer companies) during the new game's
    /// fast-forward; what to do with those calls is in <see cref="ModEvents"/> and <c>ReleaseDeduper</c>, not here.</item>
    /// <item>The postfix is as short as it can be: with nobody subscribed it returns after one field read. Both patches are one class, so one
    /// unit <c>P20</c> of the circuit breaker: five failures switch the event off until the game restarts, the game goes on without it.</item>
    /// </list>
    /// Found by <c>ModHooks</c>' scan of the core at the Patch stage (Harmony id <c>ptmod.mod-framework</c>). Without the patches (no
    /// 0Harmony.dll, another game version) <see cref="ModEvents.CpuReleased"/> is never raised.
    /// </summary>
    internal static class CpuEventPatches
    {
        private const string CoreId = ModLog.CoreFeatureId;

        // The unit of the breaker is the part before the first '/'.
        private const string CompanySite = "P20/Company.AddCpu";
        private const string AiCompanySite = "P20/AICompany.AddCpu";

        [ModPatch(typeof(GamePlayerCompany), "AddCpu", PatchKind.Postfix)]
        private static void CompanyAddCpu(GamePlayerCompany __instance, Cpu cpu)
        {
            Added(__instance, cpu, CompanySite);
        }

        [ModPatch(typeof(GameAiCompany), "AddCpu", PatchKind.Postfix)]
        private static void AiCompanyAddCpu(GameAiCompany __instance, Cpu cpu)
        {
            Added(__instance, cpu, AiCompanySite);
        }

        private static void Added(ICompany owner, Cpu cpu, string site)
        {
            if (!ModEvents.HasReleaseSubscribers) return;
            if (!ModHooks.CanRun(CoreId, site)) return;

            try
            {
                ModEvents.CpuAdded(owner, cpu);
            }
            catch (Exception ex)
            {
                ModHooks.ReportFault(CoreId, site, ex);
            }
        }
    }
}
