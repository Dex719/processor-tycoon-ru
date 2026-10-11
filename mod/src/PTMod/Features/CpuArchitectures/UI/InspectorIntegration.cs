using PTMod.Core;
using GameCpu = ProcessorTycoon.Hardware.Cpu;
using InfoText = ProcessorTycoon.UI.InfoText;

namespace PTMod.Features.CpuArchitectures
{
    /// <summary>
    /// The field "Architecture" of the inspector (FR-5 item 4; design.md Component 14): the name of the architecture of the CPU shown, instead of the game's
    /// "Default". The family is <see cref="ArchRegistry.ResolveCpu"/> (the binding of the CPU, the one of its base CPU for a variant, or the default of
    /// its owner and year - the same answer the market gets); the text is the name of the family, or "Неизвестная (id)" for a family the catalog no
    /// longer knows (<see cref="ArchDisplay"/>). The inspector is left as the game wrote it while the feature is off, and when the part
    /// <c>Inspector</c> did not pass the self-check.
    /// </summary>
    internal static class InspectorIntegration
    {
        /// <summary>The CPU was put into the inspector (postfix of <c>CpuUI.SetCpu</c>): writes the architecture into its field.</summary>
        public static void OnCpuShown(InfoText architectureText, GameCpu cpu)
        {
            ArchitecturesFeature feature = ArchitecturesFeature.Instance;
            if (feature == null || architectureText == null || cpu == null) return;
            if (feature.IsNeutralNow || (feature.Parts & ArchParts.Inspector) == 0 || feature.Registry == null) return;

            string text = NameOf(feature, cpu);
            if (text != null) architectureText.Text = text;
        }

        /// <summary>The name of the architecture of a CPU as the player reads it; null when the CPU has no family to show.</summary>
        public static string NameOf(ArchitecturesFeature feature, GameCpu cpu)
        {
            string familyId = feature.Registry.ResolveCpu(cpu);
            if (string.IsNullOrEmpty(familyId)) return null;
            return ArchDisplay.FamilyName(feature.Catalog, familyId, ModL10n.L);
        }
    }
}
