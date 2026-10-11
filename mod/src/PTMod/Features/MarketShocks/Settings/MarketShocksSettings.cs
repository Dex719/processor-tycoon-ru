using PTMod.Core;
using TMPro;
using UnityEngine;

namespace PTMod.Features.MarketShocks
{
    /// <summary>
    /// The settings of the pack in the hub "Моды" (FR-14, design.md, "Фича" item 5, "UI"): the two sliders of <see cref="ShockParamSpecs"/> - intensity
    /// (×0.5 "mild", ×1 "normal", ×1.5 "harsh", ×2 "chaos") and frequency (×0.5, ×1, ×1.5) - and the one line of the page under them that says in words which
    /// level they stand on. The switch of the pack itself is the feature's own switch in the hub (<c>IModFeature</c>); nothing about it is declared here.
    /// <list type="bullet">
    /// <item><b>Into the engine.</b> The values go into <see cref="ShockPack.Options"/> (<c>PackOptions.Severity</c> and <c>.Frequency</c>), the one object the engine
    /// holds by reference: at <see cref="Declare"/>, when the feature is switched on (<see cref="Apply"/>) and on every <c>ModSettings.Changed</c> of the two keys. The
    /// engine reads them whenever it needs them, so a moved slider is in force at the next lottery draw and the next <c>Resolve</c>; shocks that are already under way
    /// keep the numbers they were resolved with (D1).</item>
    /// <item><b>Snapped.</b> A value that is not on a level (a hand-edited <c>ptmod_settings.json</c>) runs as the nearest one (<see cref="ShockParamSpecs.SnapIntensity"/>).</item>
    /// <item><b>Switching the pack off and on</b> (FR-14 items 2, 3, AC-14.1) is not done here and needs no code in this class: the options name the feature
    /// as the pack's switch (<c>SettingsFeatureId</c>), so the engine freezes the pack's effects while the feature is not Enabled and brings them back, with their
    /// dates, when it is; the core removes the patches of the feature when it stops, so the formulas of the game are vanilla meanwhile (the feature's
    /// <c>OnDisabled</c> / <c>OnEnabled</c> drop the cache of the two channels).</item>
    /// </list>
    /// </summary>
    internal static class MarketShocksSettings
    {
        private const string FeatureId = ShockRegistration.FeatureId;
        private const string Site = "settings/";

        private static bool _attached;
        private static TMP_Text _levelLine;

        /// <summary>The intensity of the shocks now, as the pack runs on it (one of 0.5, 1, 1.5, 2).</summary>
        public static float Intensity
        {
            get { return ShockParamSpecs.SnapIntensity(ModSettings.GetFloat(FeatureId, ShockParamSpecs.Intensity)); }
        }

        /// <summary>The frequency of the shocks now, as the pack runs on it (one of 0.5, 1, 1.5).</summary>
        public static float Frequency
        {
            get { return ShockParamSpecs.SnapFrequency(ModSettings.GetFloat(FeatureId, ShockParamSpecs.Frequency)); }
        }

        /// <summary>Declares the two sliders and the page, and puts their values into the options of the pack. Called from the feature's <c>Register</c>.</summary>
        public static void Declare(FeatureContext ctx)
        {
            foreach (ParamSpec spec in ShockParamSpecs.Create(ctx.FeatureId)) ctx.Param(spec);
            ctx.HubPage(ShockParamSpecs.PageTitleKey, BuildPage);
            Apply();
        }

        /// <summary>Subscribes to the changes of the settings, once.</summary>
        public static void Attach()
        {
            if (_attached) return;
            _attached = true;
            ModSettings.Changed += OnChanged;
        }

        /// <summary>Reads both sliders into the options of the pack (the intensity is the engine's <c>Severity</c>, the frequency its <c>Frequency</c>).</summary>
        public static void Apply()
        {
            ShockPack.Options.Severity = Intensity;
            ShockPack.Options.Frequency = Frequency;
        }

        private static void OnChanged(string featureId, string key)
        {
            if (featureId != FeatureId) return;
            if (key != ShockParamSpecs.Intensity && key != ShockParamSpecs.Frequency) return;

            ModHooks.Guard(FeatureId, Site + "changed", () =>
            {
                Apply();
                RefreshPage();
            });
        }

        // ---- the page ----

        private static void BuildPage(RectTransform page)
        {
            _levelLine = ModUI.Text(page, LevelText());
        }

        private static void RefreshPage()
        {
            // The page lives only while the hub shows it; a destroyed text is "null" to Unity.
            if (_levelLine != null) _levelLine.text = LevelText();
        }

        /// <summary>"Сейчас: интенсивность «Обычно» (×1), частота ×1."</summary>
        private static string LevelText()
        {
            float intensity = Intensity, frequency = Frequency;
            bool comma = ShockText.IsRussian;
            return ModL10n.L(ShockParamSpecs.PageNowKey, ModL10n.L(ShockParamSpecs.IntensityLevelKey(intensity)), ShockFormat.Factor(intensity, comma), ShockFormat.Factor(frequency, comma));
        }
    }
}
