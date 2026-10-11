using System;
using System.Reflection;
using HarmonyLib;
using PTMod.Core;
using UnityEngine;
using Colors = ProcessorTycoon.Colors;
using CpuSalesUI = ProcessorTycoon.MarketSystem.UI.CpuSalesUI;
using GameCpu = ProcessorTycoon.Hardware.Cpu;
using InfoText = ProcessorTycoon.UI.InfoText;

namespace PTMod.Features.DesignFlawRecalls
{
    /// <summary>
    /// The mark on the sidebar card of a CPU (FR-12 item 3, design.md Component 15): a postfix of <c>CpuSalesUI.UpdateDailyTexts</c> writes "Flaw: &lt;status&gt;" in the colour of a
    /// negative figure (<c>Colors.Instance.DynamicNegativeHex</c>) after the popularity of a CPU whose flaw belongs to the player, is known and is not closed. A card
    /// without a flaw is not touched: the postfix leaves at the first check, and the text of the card stays what the game wrote.
    /// <para>
    /// Where the mark goes. Not the name of the CPU: <c>CpuSalesUI.FixedUpdate</c> writes <c>cpu.Name</c> back into the name text at once when the text is different, so
    /// a mark behind the name would be gone before it is drawn. The popularity text is written by <c>UpdateDailyTexts</c> alone (every game day and when the card is made),
    /// so the postfix appends to a fresh text each time and the marks never pile up - the way the game itself adds the missed sales to the sales text. The popularity is the
    /// figure a flaw hits (<c>popHit</c>), which is also why it fits there. <see cref="RefreshCards"/> redraws the cards when a flaw of the player changes its state, so the
    /// mark does not wait for the next day.
    /// </para>
    /// The patch is <c>Optional</c> with a low priority (it runs after the other mods' postfixes): when it cannot be applied the feature goes on without the mark. The fields
    /// <c>cpu</c> and <c>popularityText</c> come in by Harmony's injection.
    /// </summary>
    internal static class CpuBadgePatch
    {
        private const string Site = "CpuBadgePatch/UpdateDailyTexts.postfix";
        private const string RefreshSite = "CpuBadgePatch/RefreshCards";

        private static MethodInfo _updateDailyTexts;
        private static bool _updateLookedUp;

        /// <summary><c>CpuSalesUI.UpdateDailyTexts</c> ran: the mark is appended to the popularity of a CPU with a known flaw of the player.</summary>
        [ModPatch(typeof(CpuSalesUI), "UpdateDailyTexts", PatchKind.Postfix, ArgTypes = new Type[0], Optional = true, Priority = 200)]
        private static void PostfixUpdateDailyTexts(GameCpu ___cpu, InfoText ___popularityText)
        {
            // This runs once per card per game day: no closures, the guard is written out.
            if (___cpu == null || ___popularityText == null) return;
            if (!ModFeatures.IsActive(DesignFlawRecallsFeature.FeatureId) || !ModHooks.CanRun(DesignFlawRecallsFeature.FeatureId, Site)) return;

            try
            {
                DesignFlawRecallsFeature feature = DesignFlawRecallsFeature.Instance;
                if (feature == null) return;

                string state = FlawBadgeModel.StateKeyOf(feature.Store.FindByCpu(___cpu.SaveID));
                if (state == null) return;

                string status = ModL10n.L(FlawBadgeModel.StateKeyPrefix + state);
                string mark = ModL10n.L(FlawBadgeModel.BadgeKey, status);
                ___popularityText.Text = ___popularityText.Text + " <color=" + Colors.Instance.DynamicNegativeHex() + ">[" + mark + "]</color>";
            }
            catch (Exception ex)
            {
                ModHooks.ReportFault(DesignFlawRecallsFeature.FeatureId, Site, ex);
            }
        }

        /// <summary>
        /// Redraws the daily texts of the cards on the screen (<c>UpdateDailyTexts</c> is called through reflection, so the postfix runs): a flaw of the player changed its
        /// state, and the mark should follow without waiting for the next day. Does nothing when the patch is not in the game or the method cannot be found. Never throws.
        /// </summary>
        internal static void RefreshCards()
        {
            if (!ModHooks.IsApplied(typeof(CpuBadgePatch))) return;

            ModHooks.Guard(DesignFlawRecallsFeature.FeatureId, RefreshSite, () =>
            {
                MethodInfo update = UpdateDailyTexts();
                if (update == null) return;

                foreach (CpuSalesUI card in UnityEngine.Object.FindObjectsByType<CpuSalesUI>(FindObjectsSortMode.None))
                {
                    if (card == null || !card.isActiveAndEnabled) continue;

                    try
                    {
                        update.Invoke(card, null);
                    }
                    catch (TargetInvocationException)
                    {
                        // A card that is not set up yet (its Initialize has not run): the game redraws it itself. The others still get their mark.
                    }
                }
            });
        }

        private static MethodInfo UpdateDailyTexts()
        {
            if (!_updateLookedUp)
            {
                _updateLookedUp = true;
                _updateDailyTexts = AccessTools.Method(typeof(CpuSalesUI), "UpdateDailyTexts", new Type[0]);
                if (_updateDailyTexts == null) ModLog.Warn(DesignFlawRecallsFeature.FeatureId, "badge: CpuSalesUI.UpdateDailyTexts was not found; the cards are redrawn by the game only");
            }
            return _updateDailyTexts;
        }
    }
}
