using System;
using GameModifiers = ProcessorTycoon.CompanySystem.Modifiers;
using GameModifierType = ProcessorTycoon.CompanySystem.ModifierType;
using ICompany = ProcessorTycoon.CompanySystem.ICompany;

namespace PTMod.Core
{
    /// <summary>
    /// P1: <c>Modifiers.GetModifier(ModifierType, ICompany)</c>, postfix (FR-12, the only patch on the game's table of permanent company perks). The seven
    /// values of the enum are seven channels - <c>research.speed</c> (0), <c>factory.operatingCost</c> (1, the game calls it MaintainanceCost),
    /// <c>construction.cost</c> (2), <c>development.cost</c> (3), <c>research.cost</c> (4), <c>taxes</c> (5), <c>bank.interest</c> (6) - and the result is
    /// <c>__result × M(channel, company)</c>, so the game's own perks (IDM, foundry, fabless, TCMS) stay and the mod multiplies on top.
    /// <list type="bullet">
    /// <item><b>No company, no change</b> (AC-12.5): the menu of a new game shows the perks with <c>company = null</c>
    /// (<c>MenuGameSetup</c>, types 0-3); whatever entries exist, the preview stays vanilla.</item>
    /// <item><b>Fast path.</b> The game calls this for every company, many times a day. With nothing registered on the channel of the type the
    /// postfix returns after a null check, a range check and one flag read (<see cref="ModModifiers.HasAnyForType"/>): no lookup, no allocation, well
    /// under a microsecond. With records the cost is the cached value of the day (a dictionary read), or one evaluation per company and channel.</item>
    /// <item><b>Scope</b> is the company's key: an entry for <c>Player</c> multiplies only the player's company (AC-12.1), <c>AIOnly</c> only AI companies.</item>
    /// <item><b>Failure</b> is the core's unit <c>P1</c>: the exception is counted, <c>__result</c> keeps the game's value, and after
    /// <c>hooks.faultThreshold</c> failures the patch stops answering until the game restarts.</item>
    /// </list>
    /// To check in the game (the owner does this in TSK-053): with <c>framework-sample</c> enabled and an entry <c>research.speed ×1.5</c> for
    /// <c>Player</c>, the research of the player's technology progresses 1.5 times faster than in the same save without the entry while the AI
    /// companies' does not change; the new game's menu shows the same preview values with and without the entry; with the feature switched off the
    /// speed is vanilla again and the entry is still in the next side file.
    /// </summary>
    internal static class GetModifierPatch
    {
        private const string CoreId = ModLog.CoreFeatureId;
        private const string Site = "P1/postfix";

        [ModPatch(typeof(GameModifiers), "GetModifier", PatchKind.Postfix, ArgTypes = new[] { typeof(GameModifierType), typeof(ICompany) })]
        private static void Postfix(GameModifierType type, ICompany company, ref float __result)
        {
            if (ReferenceEquals(company, null)) return;               // AC-12.5: the preview of the menu
            int index = (int)type;
            if (!ModModifiers.HasAnyForType(index)) return;           // nothing on this channel (also any value of the enum that has no channel)
            if (!ModHooks.CanRun(CoreId, Site)) return;

            try
            {
                float multiplier = ModModifiers.Evaluate(ModChannels.OfModifierType(index), new ModifierContext { Company = company });
                if (multiplier != 1f) __result *= multiplier;
            }
            catch (Exception ex)
            {
                ModHooks.ReportFault(CoreId, Site, ex);
            }
        }
    }
}
