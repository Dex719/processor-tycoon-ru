using PTMod.Core;
using CpuUI = ProcessorTycoon.Desktop.Inspector.CpuUI;
using GameCpu = ProcessorTycoon.Hardware.Cpu;
using InfoText = ProcessorTycoon.UI.InfoText;

namespace PTMod.Features.CpuArchitectures
{
    /// <summary>
    /// The inspector of a CPU (FR-5 item 4; design.md, "Патчи Harmony"): the postfix of <c>CpuUI.SetCpu(Cpu)</c> writes the name of the architecture into
    /// the field "Architecture" (<c>architectureText</c>), where the game writes "Default", for the CPUs of the player and of the AI alike. The target
    /// is not reserved by the core and <c>research-tree-ux</c> does not patch it (its compare button is on <c>InspectorWindow.Start</c>). The patch is
    /// <c>Optional</c>: without it the inspector keeps "Default" and the rest of the feature works. The private field comes in by Harmony's injection
    /// (<c>___architectureText</c>); the self-check looks for it first (the part <c>Inspector</c>). The body is guarded by <c>ModHooks.Guard</c>.
    /// </summary>
    internal static class InspectorPatches
    {
        private const string Site = "InspectorPatches/SetCpu.postfix";

        /// <summary><c>CpuUI.SetCpu(Cpu)</c> filled the inspector: the field "Architecture" shows the family of the CPU.</summary>
        [ModPatch(typeof(CpuUI), "SetCpu", PatchKind.Postfix, ArgTypes = new[] { typeof(GameCpu) }, Optional = true)]
        private static void PostfixSetCpu(GameCpu cpu, InfoText ___architectureText)
        {
            if (ArchitecturesFeature.NeutralNow) return;   // off: the inspector keeps the game's "Default"
            if (cpu == null || ___architectureText == null) return;
            ModHooks.Guard(ArchitecturesFeature.FeatureId, Site, () => InspectorIntegration.OnCpuShown(___architectureText, cpu));
        }
    }
}
