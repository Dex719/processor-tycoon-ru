using System;
using PTMod.Core;
using CpuMath = ProcessorTycoon.Hardware.Math.CpuMath;
using CreatorCpu = ProcessorTycoon.Hardware.Creation.CreatorCpu;
using DropdownUI = ProcessorTycoon.UI.DropdownUI;
using IPackage = ProcessorTycoon.Hardware.IPackage;

namespace PTMod.Features.CpuArchitectures
{
    /// <summary>
    /// The CPU designer of the player (FR-4; design.md, "Патчи Harmony"): the list "Архитектура" (postfixes of <c>CreatorCpu.Initialize</c> and
    /// <c>CreatorCpu.UpdateUI</c>) and the capture of the family when the player presses "Create" (prefix and postfix of <c>CreatorCpu.CreateCpu</c>:
    /// <see cref="DesignContext.PendingForCreate"/> is set for the time of the call, in which the project starts and the binder takes it). The
    /// multipliers of the design (TSK-010, FR-4 item 2): the postfixes of <c>ConsumptionAndTemperature</c> (power × <c>design.power</c>, the temperature
    /// recomputed by the game's own <c>CpuMath.Temperature</c>, so the failure rate - <c>UpdateNumbers</c> calls <c>FailureRate</c> next - follows the new
    /// temperature) and <c>Performance</c> (IPC, IPS and IPS without the bottleneck × <c>design.ipc</c>). <c>CreateCpu</c> reads those same fields, so a
    /// CPU keeps what the designer showed (FR-4 item 4). The cost of the project is not here: it goes through the channel <c>development.cost.design</c>
    /// (<see cref="DesignCostProvider"/>), the target <c>CpuMath.ProjectCost</c> belongs to the core.
    /// <para>
    /// No target is reserved by the core. All patches are <c>Optional</c>: a designer without them costs the part <c>Designer</c>, not the feature.
    /// The private <c>coreCountDropdown</c> comes in by Harmony's injection (<c>___coreCountDropdown</c>); the self-check looks for it first.
    /// </para>
    /// </summary>
    internal static class DesignerPatches
    {
        private const string InitializeSite = "DesignerPatches/Initialize.postfix";
        private const string UpdateUiSite = "DesignerPatches/UpdateUI.postfix";
        private const string CreatePrefixSite = "DesignerPatches/CreateCpu.prefix";
        private const string CreatePostfixSite = "DesignerPatches/CreateCpu.postfix";
        private const string PowerSite = "DesignerPatches/ConsumptionAndTemperature.postfix";
        private const string PerformanceSite = "DesignerPatches/Performance.postfix";

        /// <summary><c>CreatorCpu.Initialize</c> (0.2 s after the scene starts): the list is built next to the list of cores.</summary>
        [ModPatch(typeof(CreatorCpu), "Initialize", PatchKind.Postfix, ArgTypes = new Type[0], Optional = true)]
        private static void PostfixInitialize(CreatorCpu __instance, DropdownUI ___coreCountDropdown)
        {
            if (ArchitecturesFeature.NeutralNow) return;   // the list is made by the first UpdateUI after the feature works
            DesignerIntegration designer = Designer();
            if (designer == null) return;
            long started = ArchPerf.Begin();
            ModHooks.Guard(ArchitecturesFeature.FeatureId, InitializeSite, () => designer.OnCreatorInitialized(__instance, ___coreCountDropdown));
            ArchPerf.End(ArchPerfMeter.Designer, started);
        }

        /// <summary><c>CreatorCpu.UpdateUI(bool onOpen)</c>: the window opened or a part changed - the options follow the player's families.</summary>
        [ModPatch(typeof(CreatorCpu), "UpdateUI", PatchKind.Postfix, ArgTypes = new[] { typeof(bool) }, Optional = true)]
        private static void PostfixUpdateUI(CreatorCpu __instance, DropdownUI ___coreCountDropdown)
        {
            if (ArchitecturesFeature.NeutralNow)
            {
                // Off: a list made earlier stays hidden (the designer hides it when the feature is switched off).
                DesignerIntegration idle = Designer();
                if (idle != null) idle.OnCreatorNeutral();
                return;
            }
            DesignerIntegration designer = Designer();
            if (designer == null) return;
            long started = ArchPerf.Begin();
            ModHooks.Guard(ArchitecturesFeature.FeatureId, UpdateUiSite, () => designer.OnCreatorUiUpdated(__instance, ___coreCountDropdown));
            ArchPerf.End(ArchPerfMeter.Designer, started);
        }

        /// <summary><c>CreatorCpu.CreateCpu</c> starts: the family of the project is the one the designer shows (null while the feature is neutral).</summary>
        [ModPatch(typeof(CreatorCpu), "CreateCpu", PatchKind.Prefix, ArgTypes = new Type[0], Optional = true)]
        private static void PrefixCreateCpu()
        {
            if (ArchitecturesFeature.NeutralNow)
            {
                DesignContext.PendingForCreate = null;   // nothing is bound while the feature is off: the CPU gets the default family
                return;
            }
            DesignContext.PendingForCreate = null;
            ModHooks.Guard(ArchitecturesFeature.FeatureId, CreatePrefixSite, () => { DesignContext.PendingForCreate = DesignContext.SelectedForPlayer(); });
        }

        /// <summary><c>CreatorCpu.CreateCpu</c> returned: the project took its family, nothing is pending any more.</summary>
        [ModPatch(typeof(CreatorCpu), "CreateCpu", PatchKind.Postfix, ArgTypes = new Type[0], Optional = true)]
        private static void PostfixCreateCpu()
        {
            if (ArchitecturesFeature.NeutralNow)
            {
                DesignContext.PendingForCreate = null;
                return;
            }
            ModHooks.Guard(ArchitecturesFeature.FeatureId, CreatePostfixSite, () => { DesignContext.PendingForCreate = null; });
        }

        /// <summary>
        /// <c>CreatorCpu.ConsumptionAndTemperature</c> (every frame while the window is open, from <c>UpdateNumbers</c>): the power of the family and the
        /// temperature of that power. The vanilla temperature is not touched when the family does not change the power.
        /// </summary>
        [ModPatch(typeof(CreatorCpu), "ConsumptionAndTemperature", PatchKind.Postfix, ArgTypes = new Type[0], Optional = true)]
        private static void PostfixConsumptionAndTemperature(ref float ___consumption, ref float ___temperature, IPackage ___currentPackage)
        {
            if (ArchitecturesFeature.NeutralNow) return;
            // No closure here: this runs every frame, so the guard is spelled out instead of ModHooks.Guard(lambda).
            if (!ModHooks.CanRun(ArchitecturesFeature.FeatureId, PowerSite)) return;
            long started = ArchPerf.Begin();   // NFR-1: the postfixes of a frame together take 0.05 ms
            try
            {
                DesignDef design = DesignContext.ForPlayer();
                if (DesignMath.IsNeutral(design) || ___currentPackage == null) return;

                float power = DesignMath.Power(___consumption, design);
                if (power == ___consumption) return;
                ___consumption = power;
                ___temperature = CpuMath.Temperature(power, ___currentPackage);
            }
            catch (Exception ex)
            {
                ModHooks.ReportFault(ArchitecturesFeature.FeatureId, PowerSite, ex);
            }
            finally
            {
                ArchPerf.End(ArchPerfMeter.Designer, started);
            }
        }

        /// <summary>
        /// <c>CreatorCpu.Performance</c> (every frame): IPC, IPS and IPS without the bottleneck of the family. The text "multicore ratio" is computed by
        /// the game inside the method before this postfix and stays what it was (it is the ratio of the cores, not the IPC of the architecture).
        /// </summary>
        [ModPatch(typeof(CreatorCpu), "Performance", PatchKind.Postfix, ArgTypes = new Type[0], Optional = true)]
        private static void PostfixPerformance(ref float ___ipc, ref float ___ips, ref float ___ipsWithoutBottleneck)
        {
            if (ArchitecturesFeature.NeutralNow) return;
            if (!ModHooks.CanRun(ArchitecturesFeature.FeatureId, PerformanceSite)) return;
            long started = ArchPerf.Begin();
            try
            {
                DesignDef design = DesignContext.ForPlayer();
                if (DesignMath.IsNeutral(design)) return;

                ___ipc = DesignMath.Ipc(___ipc, design);
                ___ips = DesignMath.Ipc(___ips, design);
                ___ipsWithoutBottleneck = DesignMath.Ipc(___ipsWithoutBottleneck, design);
            }
            catch (Exception ex)
            {
                ModHooks.ReportFault(ArchitecturesFeature.FeatureId, PerformanceSite, ex);
            }
            finally
            {
                ArchPerf.End(ArchPerfMeter.Designer, started);
            }
        }

        private static DesignerIntegration Designer()
        {
            ArchitecturesFeature feature = ArchitecturesFeature.Instance;
            return feature == null ? null : feature.Designer;
        }
    }
}
