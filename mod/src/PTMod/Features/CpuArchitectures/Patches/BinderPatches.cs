using PTMod.Core;
using CpuProject = ProcessorTycoon.Save.SaveObject.CpuProject;
using GameCpu = ProcessorTycoon.Hardware.Cpu;
using Project = ProcessorTycoon.ProjectSystem.Project;
using ProjectCompletedHandler = ProcessorTycoon.ProjectSystem.ProjectCompletedHandler;

namespace PTMod.Features.CpuArchitectures
{
    /// <summary>
    /// The two moments of the game the binder needs (FR-5 items 1-2; design.md, "Патчи Harmony"): the start of a CPU project and the end of the
    /// player's one. Neither target is reserved by the core (<c>Company.AddCpu</c> and <c>AICompany.AddCpu</c> are, and the feature does not patch
    /// them: the AI's CPUs come through <c>ModEvents.CpuReleased</c>). Both handlers leave first thing while the feature is neutral (off, or the fast-forward
    /// of a new game: <see cref="ArchitecturesFeature.NeutralNow"/>). Both patches are <c>Optional</c>: without them the part <c>Binder</c> is lost
    /// (the CPUs get the lazy default and the release event still binds), not the feature. The bodies are guarded by <c>ModHooks.Guard</c>.
    /// </summary>
    internal static class BinderPatches
    {
        private const string ProjectSite = "BinderPatches/SetProjectType.postfix";
        private const string CompletedSite = "BinderPatches/AddCpu.postfix";

        /// <summary><c>Project.SetProjectType(SaveObject.CpuProject)</c>: a CPU project starts (or is restored from a save, which the binder skips).</summary>
        [ModPatch(typeof(Project), "SetProjectType", PatchKind.Postfix, ArgTypes = new[] { typeof(CpuProject) }, Optional = true)]
        private static void PostfixSetProjectType(CpuProject project)
        {
            if (ArchitecturesFeature.NeutralNow) return;
            ArchitecturesFeature feature = ArchitecturesFeature.Instance;
            if (feature == null || feature.Binder == null || project == null) return;
            ModHooks.Guard(ArchitecturesFeature.FeatureId, ProjectSite, () => feature.Binder.OnProjectTyped(project));
        }

        /// <summary><c>ProjectCompletedHandler.AddCpu(Cpu)</c>: the player's project is finished, the CPU has its <c>SaveID</c> and waits for the release window.</summary>
        [ModPatch(typeof(ProjectCompletedHandler), "AddCpu", PatchKind.Postfix, ArgTypes = new[] { typeof(GameCpu) }, Optional = true)]
        private static void PostfixAddCpu(GameCpu cpu)
        {
            if (ArchitecturesFeature.NeutralNow) return;
            ArchitecturesFeature feature = ArchitecturesFeature.Instance;
            if (feature == null || feature.Binder == null || cpu == null) return;
            ModHooks.Guard(ArchitecturesFeature.FeatureId, CompletedSite, () => feature.Binder.OnPlayerCpuCompleted(cpu));
        }
    }
}
