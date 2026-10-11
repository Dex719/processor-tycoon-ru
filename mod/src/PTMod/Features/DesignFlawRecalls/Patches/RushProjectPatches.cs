using System;
using PTMod.Core;
using TMPro;
using CreatorCpu = ProcessorTycoon.Hardware.Creation.CreatorCpu;
using GameCpu = ProcessorTycoon.Hardware.Cpu;
using ProjectCompletedHandler = ProcessorTycoon.ProjectSystem.ProjectCompletedHandler;

namespace PTMod.Features.DesignFlawRecalls
{
    /// <summary>
    /// The patches of the switch "Rush project" (FR-20, design.md Component 15), all <c>Optional</c>: a designer without one of them costs the switch (it is offered only when
    /// every patch of this class is applied, see <see cref="RushProjectOption"/>), not the feature. No target is reserved by the core.
    /// <list type="bullet">
    /// <item><c>CreatorCpu.UpdateUI(bool)</c>, postfix: the window opened or a part changed - the switch is made, shown or hidden.</item>
    /// <item><c>CreatorCpu.Project()</c>, postfix (every frame of the open designer): with the switch on, the term and the cost of the project the game just worked out
    /// (<c>projectTime</c>, <c>projectCost</c>, injected as <c>___projectTime</c>, <c>___projectCost</c>) become the rushed ones (<see cref="RushMath"/>). <c>UpdateInfoTexts</c>,
    /// which runs right after, shows them (cost, term, per month, end date) and <c>CreateCpu</c> uses them for the project, whose <c>DevelopmentCost</c> is the dearer cost.</item>
    /// <item><c>CreatorCpu.CreateCpu()</c>, prefix: remembers the pending project (the name typed into <c>nameInput</c>, the planned end). Postfix: the switch is off again.</item>
    /// <item><c>ProjectCompletedHandler.AddCpu(Cpu)</c>, postfix: the project ended and the CPU has its <c>SaveID</c>; a pending project of that name that ends within 3 days of
    /// today turns into the mark of the CPU (<c>rushCpuIds</c>). This method is not <c>Company.AddCpu</c>: that one belongs to the core (<c>ModEvents.CpuReleased</c>).</item>
    /// </list>
    /// The postfix of <c>Project</c> runs every frame: it takes no closure and guards itself in place (<c>CanRun</c>, <c>ReportFault</c>). The others run now and then and use
    /// <c>ModHooks.Guard</c>. An exception never reaches the game.
    /// </summary>
    internal static class RushProjectPatches
    {
        private const string Feature = DesignFlawRecallsFeature.FeatureId;
        private const string ProjectSite = "RushProjectPatches/Project.postfix";
        private const string UiSite = "RushProjectPatches/UpdateUI.postfix";
        private const string CreatingSite = "RushProjectPatches/CreateCpu.prefix";
        private const string CreatedSite = "RushProjectPatches/CreateCpu.postfix";
        private const string CompletedSite = "RushProjectPatches/AddCpu.postfix";

        /// <summary><c>CreatorCpu.UpdateUI(bool onOpen)</c>: the window opened or a part changed.</summary>
        [ModPatch(typeof(CreatorCpu), "UpdateUI", PatchKind.Postfix, ArgTypes = new[] { typeof(bool) }, Optional = true)]
        private static void PostfixUpdateUI(CreatorCpu __instance)
        {
            RushProjectOption option = Option();
            if (option == null) return;
            ModHooks.Guard(Feature, UiSite, () => option.Refresh(__instance));
        }

        /// <summary><c>CreatorCpu.Project()</c>: the game worked out the term and the cost of the project; with the switch on they become the rushed ones.</summary>
        [ModPatch(typeof(CreatorCpu), "Project", PatchKind.Postfix, ArgTypes = new Type[0], Optional = true)]
        private static void PostfixProject(ref int ___projectTime, ref float ___projectCost)
        {
            RushProjectOption option = Option();
            if (option == null || !option.IsOn) return;

            // No closure here: this runs every frame, so the guard is spelled out instead of ModHooks.Guard(lambda).
            if (!ModHooks.CanRun(Feature, ProjectSite)) return;
            try
            {
                FlawBalance balance = DesignFlawRecallsFeature.Instance.Balance;
                ___projectTime = RushMath.ApplyTime(___projectTime, balance);
                ___projectCost = RushMath.ApplyCost(___projectCost, balance);
            }
            catch (Exception ex)
            {
                ModHooks.ReportFault(Feature, ProjectSite, ex);
            }
        }

        /// <summary><c>CreatorCpu.CreateCpu()</c> starts: the project is made from the fields the designer shows, so the pending project is the name and the (rushed) term shown now.</summary>
        [ModPatch(typeof(CreatorCpu), "CreateCpu", PatchKind.Prefix, ArgTypes = new Type[0], Optional = true)]
        private static void PrefixCreateCpu(TMP_InputField ___nameInput, int ___projectTime)
        {
            RushProjectOption option = Option();
            if (option == null || !option.IsOn) return;

            string name = ___nameInput == null ? null : ___nameInput.text;
            ModHooks.Guard(Feature, CreatingSite, () => option.OnProjectCreating(name, ___projectTime));
        }

        /// <summary><c>CreatorCpu.CreateCpu()</c> returned: the project exists, the switch is off again for the next one.</summary>
        [ModPatch(typeof(CreatorCpu), "CreateCpu", PatchKind.Postfix, ArgTypes = new Type[0], Optional = true)]
        private static void PostfixCreateCpu()
        {
            RushProjectOption option = Option();
            if (option == null) return;
            ModHooks.Guard(Feature, CreatedSite, option.OnProjectCreated);
        }

        /// <summary><c>ProjectCompletedHandler.AddCpu(Cpu)</c>: the player's project is finished (also one restored from a save); the CPU has its <c>SaveID</c> and waits for the release window.</summary>
        [ModPatch(typeof(ProjectCompletedHandler), "AddCpu", PatchKind.Postfix, ArgTypes = new[] { typeof(GameCpu) }, Optional = true)]
        private static void PostfixAddCpu(GameCpu cpu)
        {
            RushProjectOption option = Option();
            if (option == null || cpu == null) return;
            ModHooks.Guard(Feature, CompletedSite, () => option.OnProjectCompleted(cpu));
        }

        private static RushProjectOption Option()
        {
            DesignFlawRecallsFeature feature = DesignFlawRecallsFeature.Instance;
            return feature == null ? null : feature.Rush;
        }
    }
}
