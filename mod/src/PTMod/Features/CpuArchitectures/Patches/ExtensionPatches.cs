using System;
using PTMod.Core;
using CreatorCpu = ProcessorTycoon.Hardware.Creation.CreatorCpu;

namespace PTMod.Features.CpuArchitectures
{
    /// <summary>
    /// The cores and SMT of the CPU the player designs (FR-13 item 2, stage 2): the designer warns that an extension does not give its full rating effect yet
    /// ("Многоядерность раскроется при поддержке ≥ 40 (сейчас N)"), and for that it has to know the cores and SMT of the design that is on the screen now. The game keeps them in the
    /// private <c>currentCoreCount</c> and <c>smtEnabled</c> of <c>CreatorCpu</c>; a change of the list of cores calls no method that could be patched in a useful way, and the SMT
    /// switch writes <c>smtEnabled</c> and nothing else. So the one postfix is on <c>CreatorCpu.UpdateNumbers</c>, which the designer calls every frame while its window is open:
    /// it reads the two fields (Harmony's injection) and tells <see cref="DesignContext.NoteDesign"/>, which answers "changed" only when a number really changed, so the lines of
    /// the designer are written again only then.
    /// <para>
    /// The patch is <c>Optional</c> and belongs to its own part of the self-check (<see cref="ArchParts.Extensions"/>): without it the designer simply gives no warning, the
    /// multipliers of the design and the rating of the CPUs go on. The rating itself never needs this patch (it reads the cores and SMT of the finished CPU).
    /// </para>
    /// </summary>
    internal static class ExtensionPatches
    {
        private const string UpdateNumbersSite = "ExtensionPatches/UpdateNumbers.postfix";

        /// <summary><c>CreatorCpu.UpdateNumbers</c> (every frame while the window is open): notes the cores and SMT of the design; the lines are written only on a change.</summary>
        [ModPatch(typeof(CreatorCpu), "UpdateNumbers", PatchKind.Postfix, ArgTypes = new Type[0], Optional = true)]
        private static void PostfixUpdateNumbers(int ___currentCoreCount, bool ___smtEnabled)
        {
            if (ArchitecturesFeature.NeutralNow) return;
            long started = ArchPerf.Begin();   // NFR-1: one of the postfixes of the designer in a frame (0.05 ms together)
            try
            {
                NoteDesign(___currentCoreCount, ___smtEnabled);
            }
            finally
            {
                ArchPerf.End(ArchPerfMeter.Designer, started);
            }
        }

        private static void NoteDesign(int cores, bool smt)
        {
            if (!DesignContext.NoteDesign(cores, smt)) return;   // the common case: two comparisons, nothing allocated

            ArchitecturesFeature feature = ArchitecturesFeature.Instance;
            DesignerIntegration designer = feature == null ? null : feature.Designer;
            if (designer == null) return;
            ModHooks.Guard(ArchitecturesFeature.FeatureId, UpdateNumbersSite, () => designer.OnDesignChanged());
        }
    }
}
