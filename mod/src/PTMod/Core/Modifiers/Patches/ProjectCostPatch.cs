using System;
using GameCpuMath = ProcessorTycoon.Hardware.Math.CpuMath;
using GameMemory = ProcessorTycoon.Hardware.Memory;
using GamePackage = ProcessorTycoon.Hardware.IPackage;
using GameProcessNode = ProcessorTycoon.Hardware.ProcessNode;
using ICompany = ProcessorTycoon.CompanySystem.ICompany;

namespace PTMod.Core
{
    /// <summary>
    /// P19: <c>CpuMath.ProjectCost(package, processNode, memory, frequency, company)</c>, postfix (FR-12 item 12, D24). The game's cost of a CPU
    /// project is <c>(package + node + memory ProjectCost) × Modifiers.GetModifier(3, company)</c>; the mod multiplies the result by
    /// <c>M(development.cost.design, { Company, Design = { package, node, memory, frequency } })</c>. The channel is the one place where a feature
    /// prices a CPU <i>design</i> (the immaturity of an architecture, the discount of a platform generation) without patching the formula itself.
    /// <list type="bullet">
    /// <item><b>Type 3 once.</b> The multiplier <c>development.cost</c> (<c>ModifierType</c> 3, patch P1) is already inside the game's result,
    /// because <c>ProjectCost</c> calls <c>GetModifier(3, company)</c>; this patch never reads that channel, so it is applied once. The two channels
    /// are independent: <c>development.cost</c> knows the company, <c>development.cost.design</c> knows the design.</item>
    /// <item><b>The player and the computer companies.</b> The method is called by the CPU designer on every move of a slider (the player's company)
    /// and by <c>AIBehaviourCreation</c> many times a month for each AI company; both get the same formula, with their own company in the
    /// context (AC-12.8).</item>
    /// <item><b>Never cached.</b> The context carries a design, so the engine does not store the value (and the channel is declared
    /// <c>cacheable: false</c>): every call asks the providers anew, they have to be cheap. The <see cref="DesignContext"/> is a new object per
    /// call, so a provider may keep it.</item>
    /// <item><b>O(1) without records:</b> with nothing registered on the channel the postfix returns after one lookup, with no context built and no
    /// allocation. A call without a company is the game's (like the menu preview of <c>GetModifier</c>, AC-12.5).</item>
    /// <item>Failure is the core's unit <c>P19</c>: the exception is counted, <c>__result</c> keeps the game's value, and after
    /// <c>hooks.faultThreshold</c> failures the patch stops answering until the game restarts.</item>
    /// </list>
    /// The method is reserved for the core (<c>ReservedTargets</c>: channel <c>development.cost.design</c>): a feature that patches
    /// <c>CpuMath.ProjectCost</c> itself is rejected and registers a provider instead.
    /// Example from the design: a project of 1.2 billion with the fabless multiplier of type 3 (0.67) costs 0.8 billion; a provider of the
    /// architecture with ×1.15 makes it 0.92 billion.
    /// <para>
    /// To check in the game (the owner, with <c>framework-sample</c>, TSK-053): with a provider of <c>development.cost.design</c> returning ×1.15
    /// for the chosen process node, the cost of the project in the CPU designer is 1.15 times higher than with the feature off (or with another
    /// node) and follows the sliders at once; the perk of the company (the fabless discount of type 3) is in the figure once, not twice;
    /// <c>Player.log</c> has no <c>P19</c> faults. The AI side is the same method with the AI company in the context; it is not visible in the
    /// interface.
    /// </para>
    /// </summary>
    internal static class ProjectCostPatch
    {
        private const string CoreId = ModLog.CoreFeatureId;
        private const string Site = "P19/postfix";

        [ModPatch(typeof(GameCpuMath), "ProjectCost", PatchKind.Postfix,
            ArgTypes = new[] { typeof(GamePackage), typeof(GameProcessNode), typeof(GameMemory), typeof(float), typeof(ICompany) })]
        private static void ProjectCostPostfix(GamePackage package, GameProcessNode processNode, GameMemory memory, float frequency, ICompany company,
            ref float __result)
        {
            if (!ModModifiers.HasAny(ModChannels.DevelopmentCostDesign)) return;
            if (ReferenceEquals(company, null)) return;
            if (!ModHooks.CanRun(CoreId, Site)) return;

            try
            {
                var context = new ModifierContext
                {
                    Company = company,
                    Design = new DesignContext { Package = package, Node = processNode, Memory = memory, Frequency = frequency },
                };

                float multiplier = ModModifiers.Evaluate(ModChannels.DevelopmentCostDesign, context);
                if (multiplier != 1f) __result *= multiplier;
            }
            catch (Exception ex)
            {
                ModHooks.ReportFault(CoreId, Site, ex);
            }
        }
    }
}
