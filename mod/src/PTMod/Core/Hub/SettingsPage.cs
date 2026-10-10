using System;
using PTMod.Core.Logic.Ui;
using TMPro;
using UnityEngine;
using ButtonUI = ProcessorTycoon.UI.ButtonUI;

namespace PTMod.Core
{
    /// <summary>
    /// How a value from a widget becomes the number <see cref="ModSettings.Set"/> stores (no Unity): the slider of the game works in <c>float</c>, so <c>1.55f</c> arrives as
    /// 1.5499999523162842 and would be written to <c>ptmod_settings.json</c> that way. The value is rounded to the decimals of the step instead.
    /// </summary>
    internal static class ParamValueMath
    {
        /// <summary>Decimals a continuous slider (step 0) keeps.</summary>
        internal const int ContinuousDecimals = 3;

        /// <summary>The number to store for a value the widget reported: Int rounded to a whole number, Float to the decimals of its step (three when it has none).</summary>
        internal static double ToStored(ParamSpec spec, float widgetValue)
        {
            double value = widgetValue;
            if (spec.Type == ParamType.Int) return Math.Round(value, MidpointRounding.AwayFromZero);

            int decimals = spec.Step > 0d ? SliderMath.Decimals((float)spec.Step) : ContinuousDecimals;
            return Math.Round(value, decimals);
        }
    }

    /// <summary>
    /// The settings page of a feature (FR-17 item 3, AC-17.2): shown in the body of the hub in place of the tab, under a "Back" button and a heading. Two parts, in this order:
    /// <list type="bullet">
    /// <item><b>Parameters.</b> Built from the feature's <see cref="ParamSpec"/> list: <see cref="ParamType.Bool"/> becomes a toggle, <see cref="ParamType.Int"/> and <see cref="ParamType.Float"/> a slider
    /// (the spec's range and step, the label and tooltip from its keys). A change calls <see cref="ModSettings.Set"/> at once, so the file and <c>ModSettings.GetFloat</c> agree
    /// with the slider; a spec with <see cref="ParamSpec.RequiresReload"/> gets a note under it. The parameters can be changed while the feature is off.</item>
    /// <item><b>The feature's own page.</b> The builder of <c>FeatureContext.HubPage</c>, called with a vertical list under the parameters, only while the feature is Enabled
    /// (a switched-off feature shows its state instead), under <see cref="ModHooks.Guard(string, string, Action)"/> of the feature: an exception is counted against it and
    /// the page shows the line "section not loaded" in its place.</item>
    /// </list>
    /// Main thread only.
    /// </summary>
    internal static class SettingsPage
    {
        private const string CoreId = ModLog.CoreFeatureId;
        private const float BackButtonWidth = 120f;

        /// <summary>The heading of the page: the title of the feature's <c>HubPage</c>, otherwise "Settings: &lt;name&gt;".</summary>
        internal static string TitleOf(FeatureInfo info)
        {
            HubPageDecl page = ModHub.PageOf(info.Id);
            if (page != null && !string.IsNullOrEmpty(page.TitleKey)) return ModUI.TextOfKey(page.TitleKey);
            return ModL10n.L("core.hub.feature.settingsTitle", FeaturesTab.NameOf(info));
        }

        /// <summary>Fills the emptied body of the hub with the page of the feature.</summary>
        internal static void Build(RectTransform body, FeatureInfo info)
        {
            BuildHeader(body, info);

            try
            {
                foreach (ParamSpec spec in ModSettings.SpecsOf(info.Id)) AddParam(body, info.Id, spec);
            }
            catch (Exception ex)
            {
                ModLog.Error(info.Id, "hub: cannot build the parameters page", ex);
            }

            HubPageDecl page = ModHub.PageOf(info.Id);
            if (page != null) BuildFeaturePage(body, info, page);
        }

        private static void BuildHeader(RectTransform body, FeatureInfo info)
        {
            RectTransform row = HubWidgets.Row(body, "PTModPageHeader", 0f, 10f);
            ButtonUI back = ModUI.Button(row, ModL10n.L("core.common.back"), ModHub.CloseFeaturePage);
            HubWidgets.SetWidth(back, BackButtonWidth, 0f);

            TMP_Text title = ModUI.Text(row, TitleOf(info), true);
            if (title != null)
            {
                title.richText = false;
                title.alignment = TextAlignmentOptions.MidlineLeft;
                HubWidgets.SetWidth(title, 0f, 1f);
            }
        }

        // ---- parameters ----

        private static void AddParam(RectTransform body, string featureId, ParamSpec spec)
        {
            try
            {
                string label = !string.IsNullOrEmpty(spec.LabelKey) && ModL10n.Has(spec.LabelKey) ? ModL10n.L(spec.LabelKey) : spec.Key;
                string tooltip = !string.IsNullOrEmpty(spec.TooltipKey) && ModL10n.Has(spec.TooltipKey) ? ModL10n.L(spec.TooltipKey) : null;

                if (spec.Type == ParamType.Bool)
                {
                    ModUI.Toggle(body, label, ModSettings.GetBool(featureId, spec.Key),
                        on => ModSettings.Set(featureId, spec.Key, on ? 1d : 0d), tooltip);
                }
                else
                {
                    float value = spec.Type == ParamType.Int ? ModSettings.GetInt(featureId, spec.Key) : ModSettings.GetFloat(featureId, spec.Key);
                    ModUI.Slider(body, label, (float)spec.Min, (float)spec.Max, (float)spec.Step, value,
                        changed => ModSettings.Set(featureId, spec.Key, ParamValueMath.ToStored(spec, changed)), tooltip);
                }

                if (spec.RequiresReload) AddNote(body, ModL10n.L("core.feature.reloadRequired"));
            }
            catch (Exception ex)
            {
                ModLog.Error(featureId, "hub: cannot make the widget of the parameter '" + spec.Key + "'", ex);
            }
        }

        private static void AddNote(RectTransform body, string text)
        {
            TMP_Text note = ModUI.Text(body, text);
            if (note == null) return;

            note.richText = false;
            note.fontSize = Mathf.Max(11f, note.fontSize - 2f);
        }

        // ---- the feature's own page ----

        private static void BuildFeaturePage(RectTransform body, FeatureInfo info, HubPageDecl page)
        {
            if (!ModFeatures.IsActive(info.Id))
            {
                // The feature's code runs only while the feature does; the parameters above are the core's and stay editable.
                AddNote(body, FeatureRowModel.StateTextOf(ModFeatures.StateOf(info.Id), ModFeatures.ReasonOf(info.Id)));
                return;
            }

            RectTransform list = ModUI.VerticalList(body);
            if (list == null) return;

            bool built = ModHooks.Guard(info.Id, "ModHub.Page", () => page.Build(list));
            if (!built)
            {
                ModLog.Warn(CoreId, "hub: the settings page of '" + info.Id + "' failed to build; see the log lines above");
                ModUI.Text(body, ModL10n.L("core.newgame.sectionError", FeaturesTab.NameOf(info)));
            }
        }
    }
}
