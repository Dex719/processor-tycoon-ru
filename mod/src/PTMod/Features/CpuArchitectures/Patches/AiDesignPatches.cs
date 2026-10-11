using System;
using PTMod.Core;
using CpuDesign = ProcessorTycoon.Hardware.CpuDesign;
using CpuMath = ProcessorTycoon.Hardware.Math.CpuMath;
using DataConverter = ProcessorTycoon.Save.DataConverter;
using GameCpu = ProcessorTycoon.Hardware.Cpu;
using ICompany = ProcessorTycoon.CompanySystem.ICompany;

namespace PTMod.Features.CpuArchitectures
{
    /// <summary>
    /// The design of the AI companies (FR-4 item 5; design.md, "Патчи Harmony"): the postfix of <c>DataConverter.CpuDesignToCpu</c>, the one place where
    /// the game turns the design an AI company picked into its CPU (the only caller is <c>AIBehaviourCreation.CreateCpu</c>). The CPU gets the multipliers
    /// of the ACTIVE family of the company, exactly like the CPU of the player gets the ones of the family in the designer: IPC and IPS × <c>design.ipc</c>,
    /// power × <c>design.power</c>, the temperature recomputed by the game's <c>CpuMath.Temperature</c> (AC-4.3: an x86 CPU of Intel has 5 % more IPS than
    /// the vanilla design). The failure rate is not recomputed - a documented simplification of design.md: the AI already chose the design by its own
    /// numbers, the architecture only shifts the result a little.
    /// <para>
    /// The cost of the AI's projects is not here: <c>AIBehaviourCreation.SimulateCpus</c> asks <c>CpuMath.ProjectCost</c>, which the core answers through
    /// the channel <c>development.cost.design</c>, and the <see cref="DesignCostProvider"/> gives the company's active family there.
    /// While the feature is neutral (off, the fast-forward of a new game) the postfix returns first and the CPU is the vanilla one. The target is not
    /// reserved by the core; the patch is <c>Optional</c> (without it the AI designs on the vanilla numbers, the part keeps working for the player).
    /// </para>
    /// </summary>
    internal static class AiDesignPatches
    {
        private const string Site = "AiDesignPatches/CpuDesignToCpu.postfix";

        /// <summary><c>DataConverter.CpuDesignToCpu(CpuDesign)</c> returned the CPU of the AI's design: the multipliers of its active family are put on it.</summary>
        [ModPatch(typeof(DataConverter), "CpuDesignToCpu", PatchKind.Postfix, ArgTypes = new[] { typeof(CpuDesign) }, Optional = true)]
        private static void PostfixCpuDesignToCpu(GameCpu __result)
        {
            if (ArchitecturesFeature.NeutralNow) return;   // off or the fast-forward of a new game: the CPU is the vanilla one
            // Not Guard(lambda): a closure per call is avoidable (the AI makes one CPU a month at most, but the habit keeps the hot paths alike).
            if (__result == null || !ModHooks.CanRun(ArchitecturesFeature.FeatureId, Site)) return;
            try
            {
                ICompany company = __result.Company;
                if (ReferenceEquals(company, null) || company.IsPlayer) return;   // the player's CPUs come from the designer, not from here

                DesignDef design = DesignContext.ForCompany(company);
                if (DesignMath.IsNeutral(design)) return;

                __result.Ipc = DesignMath.Ipc(__result.Ipc, design);
                __result.Ips = DesignMath.Ipc(__result.Ips, design);

                float power = DesignMath.Power(__result.PowerConsumption, design);
                if (power != __result.PowerConsumption)
                {
                    __result.PowerConsumption = power;
                    if (__result.Package != null) __result.Temperature = CpuMath.Temperature(power, __result.Package);
                }
            }
            catch (Exception ex)
            {
                ModHooks.ReportFault(ArchitecturesFeature.FeatureId, Site, ex);
            }
        }
    }
}
