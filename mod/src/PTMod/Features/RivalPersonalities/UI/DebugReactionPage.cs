#if PTMOD_DEBUG
using System;
using System.Collections.Generic;
using System.Globalization;
using PTMod.Core;
using UnityEngine;
using AICompany = ProcessorTycoon.CompanySystem.AICompany;

namespace PTMod.Features.RivalPersonalities
{
    /// <summary>
    /// The debug page of the hub (design.md, "UI"; FR-6/FR-7; debug build only): two drop-down lists, "Company" and "Reaction type", and the button
    /// "Plan". The button plans the chosen reaction of the chosen rival at once through the planner (no roll, no limit but the three that cannot be set
    /// aside, a warning of one day), so the manual scenarios 2, 4 and 11 do not have to wait for a trigger. The reaction goes the usual way after that: the
    /// intelligence mail, its delivery, the day of carrying out one day later. The page is a section: the window of the intelligence panel (TSK-018) puts
    /// it on its own page with <see cref="Build"/>. In a release build this file is empty and the feature has no such page.
    /// </summary>
    internal static class DebugReactionPage
    {
        private const string Feature = RivalPersonalitiesFeature.FeatureId;

        private static readonly ReactionType[] Types =
        {
            ReactionType.PriceCut, ReactionType.CounterLaunch, ReactionType.OemPush, ReactionType.Poach, ReactionType.Smear,
        };

        /// <summary>Builds the controls into <paramref name="root"/> (the content area of the hub page, or a section of the intelligence panel).</summary>
        public static void Build(RectTransform root)
        {
            if (root == null) return;
            RivalPersonalitiesFeature feature = RivalPersonalitiesFeature.Instance;

            RectTransform list = ModUI.VerticalList(root, 8f);
            ModUI.Text(list, ModL10n.L("rival.debug.hint"), false);
            if (feature == null || !ModTicks.IsGameReady)
            {
                ModUI.Text(list, ModL10n.L("rival.debug.notReady"), false);
                return;
            }

            List<AICompany> rivals = feature.Registry.Rivals();
            var names = new List<string>();
            foreach (AICompany rival in rivals) names.Add(rival.Name + " (ai:" + rival.UniqueID.ToString(CultureInfo.InvariantCulture) + ")");

            var typeNames = new List<string>();
            foreach (ReactionType type in Types) typeNames.Add(ModL10n.L("rival.reaction." + ReactionExecutor.KeyName(type)));

            if (rivals.Count == 0)
            {
                ModUI.Text(list, ModL10n.L("rival.debug.noRivals"), false);
                return;
            }

            int companyIndex = 0;
            int typeIndex = 0;
            ModUI.Dropdown(list, ModL10n.L("rival.debug.company"), names, 0, index => companyIndex = index);
            ModUI.Dropdown(list, ModL10n.L("rival.debug.type"), typeNames, 0, index => typeIndex = index);
            ModUI.Button(list, ModL10n.L("rival.debug.plan"), () => Plan(rivals, companyIndex, Types[Math.Max(0, Math.Min(typeIndex, Types.Length - 1))]), ButtonStyle.CallToAction);
        }

        private static void Plan(List<AICompany> rivals, int companyIndex, ReactionType type)
        {
            ModHooks.Guard(Feature, "UI/DebugReactionPage.Plan", () =>
            {
                RivalPersonalitiesFeature feature = RivalPersonalitiesFeature.Instance;
                if (feature == null || companyIndex < 0 || companyIndex >= rivals.Count) return;

                AICompany rival = rivals[companyIndex];
                PlanResult result = feature.Planner.PlanManual(rival.UniqueID, type);
                string what = rival.Name + " / " + ModL10n.L("rival.reaction." + ReactionExecutor.KeyName(type));
                if (result.IsPlanned) ModNotify.Show(Feature, ModL10n.L("rival.debug.planned", what));
                else ModNotify.Show(Feature, ModL10n.L("rival.debug.refused", what, result.Status == PlanStatus.Refused ? result.Gate.ToString() : result.Status.ToString()));
            });
        }
    }
}
#endif
